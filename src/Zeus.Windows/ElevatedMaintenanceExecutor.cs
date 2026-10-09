using System.ComponentModel;
using System.Diagnostics;
using Zeus.Core;

namespace Zeus.Windows;

/// <summary>Starts the adjacent, fixed helper via UAC; never passes shell commands.</summary>
public sealed class ElevatedMaintenanceExecutor : IMaintenanceExecutor, IAdvancedMaintenanceExecutor
{
    public Task<MaintenanceReport> ExecuteAsync(IReadOnlyCollection<MaintenanceActionId> actions,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
        ExecuteRequestsAsync(actions.Select(action => new MaintenanceRequest(action)).ToArray(), progress, cancellationToken);

    public Task<MaintenanceReport> ExecuteRequestsAsync(IReadOnlyCollection<MaintenanceRequest> requests,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
        ExecuteRequestsCoreAsync(requests, null, progress, cancellationToken);

    public async Task<MaintenanceReport> ExecutePostRepairVerificationAsync(MaintenanceReport repair,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var plan = PostRepairVerification.CreatePlan(repair);
        if (plan.Count == 0) throw new ArgumentException("Nenhum reparo concluído elegível para verificação.", nameof(repair));
        var report = await ExecuteRequestsCoreAsync(plan, repair.SessionId, progress, cancellationToken);
        return report.Steps.Count == 0 ? report : PostRepairVerification.Link(repair, report);
    }

    private async Task<MaintenanceReport> ExecuteRequestsCoreAsync(IReadOnlyCollection<MaintenanceRequest> requests,
        Guid? verificationOfSessionId,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("A manutenção requer Windows.");
        var selected = MaintenancePolicy.ValidateRequests(requests);
        var id = Guid.NewGuid();
        var started = DateTimeOffset.UtcNow;
        cancellationToken.ThrowIfCancellationRequested();
        if (selected.Count == 0)
            return new MaintenanceReport(id, started, DateTimeOffset.UtcNow, false, [], "Nenhuma ação foi selecionada.");

        var helper = Path.Combine(AppContext.BaseDirectory, "Zeus.Maintenance.exe");
        if (!File.Exists(helper) || (File.GetAttributes(helper) & FileAttributes.ReparsePoint) != 0)
            return new MaintenanceReport(id, started, DateTimeOffset.UtcNow, false, [], "Auxiliar Zeus.Maintenance.exe ausente ou inválido. Use a distribuição completa do ZEUS.");
        var start = new ProcessStartInfo(helper)
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = AppContext.BaseDirectory,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        start.ArgumentList.Add("--session");
        start.ArgumentList.Add(id.ToString("D"));
        start.ArgumentList.Add("--requests");
        start.ArgumentList.Add(MaintenanceRequestProtocol.Encode(selected));
        progress?.Report("Aguardando autorização de administrador no Windows…");
        var helperStarted = false;
        try
        {
            await new PendingMaintenanceSessions().RememberAsync(id, selected, started, cancellationToken, verificationOfSessionId);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("O auxiliar não iniciou.");
            helperStarted = true;
            progress?.Report("Manutenção em execução. Reparos podem levar bastante tempo; aguarde o relatório antes de fechar o aplicativo.");
            // Killing an elevated DISM/SFC repair is unsafe. Cancellation is honored only before launch.
            await process.WaitForExitAsync(CancellationToken.None);
            try
            {
                var report = await SessionStore.ReadReportAsync(id);
                progress?.Report(report.Error is null ? "Sessão concluída; consulte resultados e logs." : report.Error);
                return report;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                return new MaintenanceReport(id, started, DateTimeOffset.UtcNow, false, [],
                    $"O auxiliar terminou com código {process.ExitCode}, mas o relatório não pôde ser validado: {error.Message}", IsComplete: false);
            }
        }
        catch (Win32Exception error) when (!helperStarted && error.NativeErrorCode == 1223)
        {
            var cleanupError = await TryForgetLaunchReceiptAsync(id);
            return new MaintenanceReport(id, started, DateTimeOffset.UtcNow, false,
                selected.Select(request => new MaintenanceStepResult(request.Action, StepOutcome.Cancelled,
                    "Autorização de administrador cancelada; nenhuma ação executada." + cleanupError, TargetId: request.TargetId,
                    Verification: MaintenanceVerificationStatus.NotStarted)).ToArray());
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        {
            var cleanupError = helperStarted ? string.Empty : await TryForgetLaunchReceiptAsync(id);
            return new MaintenanceReport(id, started, DateTimeOffset.UtcNow, false, [],
                $"Não foi possível iniciar a manutenção: {error.Message}{cleanupError}");
        }
    }

    private static async Task<string> TryForgetLaunchReceiptAsync(Guid sessionId)
    {
        try
        {
            await new PendingMaintenanceSessions().ForgetAsync(sessionId);
            return string.Empty;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return $" O registro local da sessão não pôde ser removido: {error.Message}";
        }
    }
}
