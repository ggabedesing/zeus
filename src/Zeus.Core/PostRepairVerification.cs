namespace Zeus.Core;

/// <summary>Only fresh scans can describe post-repair state. No claim of causation or rollback.</summary>
public static class PostRepairVerification
{
    public static MaintenanceReport ReconcileRecoveredReport(MaintenanceReport existing, MaintenanceReport recovered)
    {
        if (existing.SessionId != recovered.SessionId) throw new ArgumentException("As sessões recuperadas não correspondem.");
        if (existing.VerificationOfSessionId is { } oldParent && recovered.VerificationOfSessionId is { } newParent && oldParent != newParent)
            return recovered with { VerificationOfSessionId = null, IsComplete = false,
                Error = (recovered.Error is null ? "" : recovered.Error + " ") + "Vínculos de reparo divergentes; associação não confirmada. Recibo pendente preservado para revisão." };
        return recovered with { VerificationOfSessionId = recovered.VerificationOfSessionId ?? existing.VerificationOfSessionId };
    }

    public static IReadOnlyList<MaintenanceRequest> CreatePlan(MaintenanceReport repair)
    {
        if (repair.SessionId == Guid.Empty || !repair.IsComplete || repair.FinishedAt < repair.StartedAt || repair.Error is not null || repair.VerificationOfSessionId is not null)
            return [];
        return repair.Steps.Where(step => step.Outcome == StepOutcome.Succeeded && step.TargetId is null)
            .Select(step => step.Action switch
            {
                MaintenanceActionId.RepairWindowsImage => (MaintenanceActionId?)MaintenanceActionId.ScanWindowsImage,
                MaintenanceActionId.RepairSystemFiles => MaintenanceActionId.VerifySystemFiles,
                _ => null
            }).Where(action => action.HasValue).Select(action => new MaintenanceRequest(action!.Value)).Distinct().ToArray();
    }

    public static MaintenanceReport Link(MaintenanceReport repair, MaintenanceReport scan)
    {
        var allowed = CreatePlan(repair).Select(request => request.Action).ToHashSet();
        if (allowed.Count == 0 || scan.SessionId == Guid.Empty || scan.SessionId == repair.SessionId ||
            scan.StartedAt < repair.FinishedAt || scan.FinishedAt < scan.StartedAt || scan.Steps.Count == 0 ||
            (scan.VerificationOfSessionId is { } parent && parent != repair.SessionId) || scan.Steps.Any(step => !allowed.Contains(step.Action) || step.TargetId is not null))
            throw new ArgumentException("A sessão posterior não corresponde a uma verificação somente SCAN deste reparo.");
        return scan with { VerificationOfSessionId = repair.SessionId };
    }

    public static string Describe(MaintenanceReport repair, MaintenanceReport scan)
    {
        if (scan.VerificationOfSessionId != repair.SessionId || scan.SessionId == repair.SessionId || CreatePlan(repair).Count == 0 ||
            scan.StartedAt < repair.FinishedAt || scan.FinishedAt < scan.StartedAt ||
            scan.Steps.Any(step => step.Action is not MaintenanceActionId.ScanWindowsImage and not MaintenanceActionId.VerifySystemFiles || step.TargetId is not null))
            return "Vínculo de verificação inválido ou reparo de origem indisponível; resultado desconhecido.";
        var details = CreatePlan(repair).Select(request =>
        {
            var matches = scan.Steps.Where(step => step.Action == request.Action).ToArray();
            if (!scan.IsComplete || scan.Error is not null || matches.Length != 1 || matches[0].Outcome != StepOutcome.Succeeded ||
                matches[0].Verification != MaintenanceVerificationStatus.ProviderConfirmed)
                return $"{MaintenanceCatalog.Get(request.Action).Title}: resultado posterior desconhecido ou não confirmado.";
            var step = matches[0];
            return request.Action == MaintenanceActionId.ScanWindowsImage ? step.ImageHealthState switch
            {
                WindowsImageHealthState.NoCorruptionDetected => "DISM posterior: não detectou corrupção no armazenamento de componentes nesta varredura.",
                WindowsImageHealthState.RepairableCorruptionDetected => "DISM posterior: detectou corrupção reparável nesta varredura.",
                WindowsImageHealthState.NonRepairableCorruptionDetected => "DISM posterior: detectou corrupção não reparável por este mecanismo.",
                _ => "DISM posterior: estado desconhecido."
            } : step.SystemFilesState switch
            {
                SfcVerificationState.NoIntegrityViolationsDetected => "SFC posterior: não detectou violações de integridade nesta varredura.",
                SfcVerificationState.IntegrityViolationsDetected => "SFC posterior: detectou violações de integridade nesta varredura.",
                SfcVerificationState.UnrepairableIntegrityViolationsDetected => "SFC posterior: detectou violações sem reparação confirmada.",
                _ => "SFC posterior: estado desconhecido."
            };
        });
        return string.Join(" ", details) + " Comparação temporal com o comando de reparo; não comprova que ele causou o estado observado nem a saúde completa do Windows. Reinício solicitado deve ser realizado manualmente antes de interpretar o resultado definitivo.";
    }
}
