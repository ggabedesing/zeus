using System.Security.Principal;
using Zeus.Core;
using Zeus.Windows;

namespace Zeus.Maintenance;

internal static class Program
{
    private static async Task<int> Main(string[] arguments)
    {
        if (!OperatingSystem.IsWindows()) return 2;
        if (!MaintenanceRequestProtocol.TryReadArguments(arguments, out var sessionId, out var requests)) return 2;
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) return 3;

        var started = DateTimeOffset.UtcNow;
        var results = new List<MaintenanceStepResult>();
        var restoreConfirmed = false;
        string? error = null;
        var sessionCreated = false;
        try
        {
            SessionStore.CreateSession(sessionId);
            sessionCreated = true;
            using var maintenanceLock = SessionStore.AcquireMaintenanceLock(sessionId);
            if (MaintenancePolicy.RequiresRestorePoint(requests.Select(request => request.Action).Distinct()))
            {
                var restore = await CommandRunner.CreateRestorePointAsync(sessionId);
                restoreConfirmed = RestorePointConfirmationParser.ParseSequenceNumber(
                    restore.ExitCode, restore.Output, restore.LogError) is not null;
                if (!restoreConfirmed)
                    error = restore.LogError is not null
                        ? $"Falha ao registrar a preparação de recuperação: {restore.LogError}. Reparos foram bloqueados."
                        : "Não foi possível confirmar um novo ponto de restauração. Reparos foram bloqueados; consulte restore-point.log. Proteção desativada ou o limite de 24 horas podem impedir a criação.";
            }

            foreach (var request in requests)
            {
                var action = request.Action;
                var definition = MaintenanceCatalog.Get(action);
                if (definition.RequiresRestorePoint && !restoreConfirmed)
                {
                    results.Add(new MaintenanceStepResult(action, StepOutcome.Skipped,
                        "Reparo não executado: nenhum novo ponto de restauração foi confirmado nesta sessão.",
                        Path.Combine(SessionStore.GetSessionDirectory(sessionId), "restore-point.log"), request.TargetId,
                        MaintenanceVerificationStatus.NotStarted));
                    continue;
                }
                MaintenancePolicy.EnsureRestorePoint([action], restoreConfirmed);
                try
                {
                    // Persist the actual completed steps and an honest pending entry before any reboot-capable action.
                    var pending = results.Concat([new MaintenanceStepResult(action, StepOutcome.Skipped,
                        "Ação em andamento; ainda não existe confirmação de conclusão.", TargetId: request.TargetId,
                        Verification: MaintenanceVerificationStatus.Pending)]).ToArray();
                    await SessionStore.WriteProgressReportAsync(sessionId, new MaintenanceReport(sessionId, started,
                        DateTimeOffset.UtcNow, restoreConfirmed, pending, IsComplete: false));
                    results.Add(await CommandRunner.ExecuteAsync(sessionId, request));
                    await SessionStore.WriteProgressReportAsync(sessionId, new MaintenanceReport(sessionId, started,
                        DateTimeOffset.UtcNow, restoreConfirmed, results.ToArray(), error, IsComplete: false));
                }
                catch (Exception exception)
                {
                    if (results.All(result => result.Action != action || result.TargetId != request.TargetId))
                        results.Add(new MaintenanceStepResult(action, StepOutcome.Failed,
                            $"Falha ao executar a ação: {exception.Message}", TargetId: request.TargetId,
                            Verification: MaintenanceVerificationStatus.ManualReviewRequired));
                    else
                        error = $"A ação terminou, mas o relatório de progresso não pôde ser preservado: {exception.Message}";
                }
            }
        }
        catch (Exception exception)
        {
            error = $"A sessão foi interrompida: {exception.Message}";
            foreach (var request in requests.Where(request => results.All(result => result.Action != request.Action || result.TargetId != request.TargetId)))
                results.Add(new MaintenanceStepResult(request.Action, StepOutcome.Skipped,
                    "Ação não iniciada devido a uma falha na preparação da sessão.", TargetId: request.TargetId,
                    Verification: MaintenanceVerificationStatus.NotStarted));
        }

        if (!sessionCreated) return 4;
        try
        {
            var report = new MaintenanceReport(sessionId, started, DateTimeOffset.UtcNow,
                restoreConfirmed, results, error);
            await SessionStore.WriteReportAsync(sessionId, report);
        }
        catch { return 5; }
        return error is null && results.All(result => result.Outcome == StepOutcome.Succeeded) ? 0 : 1;
    }

}
