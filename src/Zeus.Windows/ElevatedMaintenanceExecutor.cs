using System.ComponentModel;
using System.Diagnostics;
using Zeus.Core;

namespace Zeus.Windows;

/// <summary>Starts the adjacent, fixed helper via UAC; never passes shell commands.</summary>
public sealed class ElevatedMaintenanceExecutor : IMaintenanceExecutor
{
    public async Task<MaintenanceReport> ExecuteAsync(IReadOnlyCollection<MaintenanceActionId> actions,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("A manutenção requer Windows.");
        var selected = MaintenancePolicy.ValidateAndOrder(actions);
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
        start.ArgumentList.Add("--actions");
        start.ArgumentList.Add(string.Join(',', selected));
        progress?.Report("Aguardando autorização de administrador no Windows…");
        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("O auxiliar não iniciou.");
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
                    $"O auxiliar terminou com código {process.ExitCode}, mas o relatório não pôde ser validado: {error.Message}");
            }
        }
        catch (Win32Exception error) when (error.NativeErrorCode == 1223)
        {
            return new MaintenanceReport(id, started, DateTimeOffset.UtcNow, false,
                selected.Select(action => new MaintenanceStepResult(action, StepOutcome.Cancelled, "Autorização de administrador cancelada; nenhuma ação executada.")).ToArray());
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        {
            return new MaintenanceReport(id, started, DateTimeOffset.UtcNow, false, [], $"Não foi possível iniciar a manutenção: {error.Message}");
        }
    }
}
