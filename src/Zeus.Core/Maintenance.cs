namespace Zeus.Core;

/// <summary>Explicit allowlist of supported operations. Never accept arbitrary command text.</summary>
public enum MaintenanceActionId
{
    DefenderQuickScan,
    ScanWindowsImage,
    RepairWindowsImage,
    VerifySystemFiles,
    RepairSystemFiles,
    AnalyzeSystemDrive,
    UpdateDefenderSignatures,
    DefenderFullScan,
    DefenderOfflineScan,
    OptimizeSystemDrive,
    InstallDriverUpdate,
    RollbackDriver
}

public enum StepOutcome
{
    Succeeded,
    Failed,
    Skipped,
    Cancelled
}

/// <summary>Post-action evidence; command completion alone does not prove the Windows state was repaired.</summary>
public enum MaintenanceVerificationStatus
{
    NotRecorded,
    NotStarted,
    Pending,
    CommandCompleted,
    ProviderConfirmed,
    ManualReviewRequired
}

public sealed record MaintenanceStepResult(
    MaintenanceActionId Action,
    StepOutcome Outcome,
    string Message,
    string? LogFile = null,
    string? TargetId = null,
    MaintenanceVerificationStatus Verification = MaintenanceVerificationStatus.NotRecorded,
    int? UpdateServerSelection = null,
    string? UpdateServiceId = null,
    WindowsImageHealthState ImageHealthState = WindowsImageHealthState.Unknown,
    SfcVerificationState SystemFilesState = SfcVerificationState.Unknown,
    DriverActiveEvidence? ActiveDriver = null);

public sealed record MaintenanceReport(
    Guid SessionId,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    bool RestorePointConfirmed,
    IReadOnlyList<MaintenanceStepResult> Steps,
    string? Error = null,
    bool IsComplete = true,
    int? RestorePointSequenceNumber = null,
    Guid? VerificationOfSessionId = null);

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

/// <summary>A typed request; only a Windows Update identity may be supplied as a target.</summary>
public sealed record MaintenanceRequest(
    MaintenanceActionId Action,
    string? TargetId = null,
    bool EulaAccepted = false,
    int? UpdateServerSelection = null,
    string? UpdateServiceId = null,
    string? EulaTextSha256 = null);

public interface IAdvancedMaintenanceExecutor
{
    Task<MaintenanceReport> ExecutePostRepairVerificationAsync(MaintenanceReport repair,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    Task<MaintenanceReport> ExecuteRequestsAsync(
        IReadOnlyCollection<MaintenanceRequest> requests,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
