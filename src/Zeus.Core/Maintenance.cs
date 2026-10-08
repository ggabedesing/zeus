namespace Zeus.Core;

/// <summary>Explicit allowlist of supported operations. Never accept arbitrary command text.</summary>
public enum MaintenanceActionId
{
    DefenderQuickScan,
    ScanWindowsImage,
    RepairWindowsImage,
    VerifySystemFiles,
    RepairSystemFiles,
    AnalyzeSystemDrive
}

public enum StepOutcome
{
    Succeeded,
    Failed,
    Skipped,
    Cancelled
}

public sealed record MaintenanceStepResult(
    MaintenanceActionId Action,
    StepOutcome Outcome,
    string Message,
    string? LogFile = null);

public sealed record MaintenanceReport(
    Guid SessionId,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    bool RestorePointConfirmed,
    IReadOnlyList<MaintenanceStepResult> Steps,
    string? Error = null);

public interface IMaintenanceExecutor
{
    /// <summary>
    /// Executes a validated plan sequentially. Implementations must confirm recovery
    /// protection before repairs and keep a per-step log of actual outcomes.
    /// </summary>
    Task<MaintenanceReport> ExecuteAsync(
        IReadOnlyCollection<MaintenanceActionId> actions,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record MaintenanceActionDefinition(
    MaintenanceActionId Id,
    string Title,
    string Description,
    bool RequiresRestorePoint,
    bool MayRequireRestart);
