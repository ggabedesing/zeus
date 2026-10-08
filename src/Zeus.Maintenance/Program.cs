using System.Security.Principal;
using Zeus.Core;
using Zeus.Windows;

namespace Zeus.Maintenance;

internal static class Program
{
    private static async Task<int> Main(string[] arguments)
    {
        if (!OperatingSystem.IsWindows()) return 2;
        if (!TryReadArguments(arguments, out var sessionId, out var actions)) return 2;
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
            if (MaintenancePolicy.RequiresRestorePoint(actions))
            {
                var restore = await CommandRunner.CreateRestorePointAsync(sessionId);
                restoreConfirmed = restore.ExitCode == 0 && restore.LogError is null &&
                    restore.Output.Contains("ZEUS_RESTORE_POINT_CONFIRMED", StringComparison.Ordinal);
                if (!restoreConfirmed)
                    error = restore.LogError is not null
                        ? $"Falha ao registrar a preparação de recuperação: {restore.LogError}. Reparos foram bloqueados."
                        : "Não foi possível confirmar um novo ponto de restauração. Reparos foram bloqueados; consulte restore-point.log. Proteção desativada ou o limite de 24 horas podem impedir a criação.";
            }

            foreach (var action in actions)
            {
                var definition = MaintenanceCatalog.Get(action);
                if (definition.RequiresRestorePoint && !restoreConfirmed)
                {
                    results.Add(new MaintenanceStepResult(action, StepOutcome.Skipped,
                        "Reparo não executado: nenhum novo ponto de restauração foi confirmado nesta sessão.",
                        Path.Combine(SessionStore.GetSessionDirectory(sessionId), "restore-point.log")));
                    continue;
                }
                MaintenancePolicy.EnsureRestorePoint([action], restoreConfirmed);
                try
                {
                    results.Add(await CommandRunner.ExecuteAsync(sessionId, action));
                }
                catch (Exception exception)
                {
                    results.Add(new MaintenanceStepResult(action, StepOutcome.Failed,
                        $"Falha ao executar a ação: {exception.Message}"));
                }
            }
        }
        catch (Exception exception)
        {
            error = $"A sessão foi interrompida: {exception.Message}";
            foreach (var action in actions.Where(a => results.All(r => r.Action != a)))
                results.Add(new MaintenanceStepResult(action, StepOutcome.Skipped, "Ação não iniciada devido a uma falha na preparação da sessão."));
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

    private static bool TryReadArguments(string[] arguments, out Guid sessionId, out IReadOnlyList<MaintenanceActionId> actions)
    {
        sessionId = Guid.Empty;
        actions = [];
        if (arguments.Length != 4 || arguments[0] != "--session" || arguments[2] != "--actions" ||
            !Guid.TryParseExact(arguments[1], "D", out sessionId) || sessionId == Guid.Empty ||
            arguments[3].Length > 256) return false;
        var selected = new List<MaintenanceActionId>();
        foreach (var name in arguments[3].Split(','))
        {
            // Canonical names only: Enum.TryParse alone also accepts arbitrary integers.
            if (!Enum.TryParse<MaintenanceActionId>(name, ignoreCase: false, out var action) ||
                !Enum.IsDefined(action) || Enum.GetName(action) != name || selected.Contains(action)) return false;
            selected.Add(action);
        }
        if (selected.Count == 0) return false;
        try { actions = MaintenancePolicy.ValidateAndOrder(selected); return true; }
        catch (ArgumentException) { return false; }
    }
}
