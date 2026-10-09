using System.Text.Json;
using Zeus.Core;

namespace Zeus.Core.Tests;

public sealed class PostRepairVerificationTests
{
    [Fact]
    public void RecoveryRetainsFreshLinkUsesFallbackAndKeepsConflictPending()
    {
        var repair = Repair();
        var scan = PostRepairVerification.Link(repair, Scan(new MaintenanceStepResult(MaintenanceActionId.ScanWindowsImage, StepOutcome.Succeeded, "done")));
        var noLink = scan with { VerificationOfSessionId = null, IsComplete = false, Steps = [] };
        Assert.Equal(repair.SessionId, PostRepairVerification.ReconcileRecoveredReport(noLink, scan).VerificationOfSessionId);
        Assert.Equal(repair.SessionId, PostRepairVerification.ReconcileRecoveredReport(scan, scan with { VerificationOfSessionId = null }).VerificationOfSessionId);
        var conflict = PostRepairVerification.ReconcileRecoveredReport(scan with { VerificationOfSessionId = Guid.NewGuid() }, scan);
        Assert.False(conflict.IsComplete);
        Assert.Null(conflict.VerificationOfSessionId);
        Assert.Contains("divergentes", conflict.Error);
        Assert.Throws<ArgumentException>(() => PostRepairVerification.ReconcileRecoveredReport(noLink with { SessionId = Guid.NewGuid() }, scan));
    }

    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-09T10:00:00Z");
    private static MaintenanceReport Repair() => new(Guid.NewGuid(), Start, Start.AddMinutes(1), true,
        [new(MaintenanceActionId.RepairWindowsImage, StepOutcome.Succeeded, "command done", Verification: MaintenanceVerificationStatus.ManualReviewRequired),
         new(MaintenanceActionId.RepairSystemFiles, StepOutcome.Succeeded, "command done", Verification: MaintenanceVerificationStatus.ManualReviewRequired)]);
    private static MaintenanceReport Scan(params MaintenanceStepResult[] steps) => new(Guid.NewGuid(), Start.AddMinutes(2), Start.AddMinutes(3), false, steps);

    [Fact]
    public void PlanIsOnlyScansAndDoesNotChangeRepair()
    {
        var repair = Repair();
        Assert.Equal(new[] { MaintenanceActionId.ScanWindowsImage, MaintenanceActionId.VerifySystemFiles }, PostRepairVerification.CreatePlan(repair).Select(item => item.Action));
        Assert.All(PostRepairVerification.CreatePlan(repair), item => Assert.False(MaintenanceCatalog.Get(item.Action).RequiresRestorePoint));
        Assert.All(repair.Steps, item => Assert.Equal(MaintenanceVerificationStatus.ManualReviewRequired, item.Verification));
    }

    [Fact]
    public void IncompleteFailedAndLinkedRepairsCannotGeneratePlan()
    {
        var repair = Repair();
        Assert.Empty(PostRepairVerification.CreatePlan(repair with { IsComplete = false }));
        Assert.Empty(PostRepairVerification.CreatePlan(repair with { Error = "interrupted" }));
        Assert.Empty(PostRepairVerification.CreatePlan(repair with { SessionId = Guid.Empty }));
        Assert.Empty(PostRepairVerification.CreatePlan(repair with { FinishedAt = Start.AddSeconds(-1) }));
        Assert.Empty(PostRepairVerification.CreatePlan(repair with { VerificationOfSessionId = Guid.NewGuid() }));
        Assert.Empty(PostRepairVerification.CreatePlan(repair with { Steps = [repair.Steps[0] with { Outcome = StepOutcome.Failed }] }));
    }

    [Fact]
    public void LinkRejectsRepairUnrelatedTargetEarlierAndSameSession()
    {
        var repair = Repair();
        var scan = Scan(new MaintenanceStepResult(MaintenanceActionId.ScanWindowsImage, StepOutcome.Succeeded, "done"));
        Assert.Throws<ArgumentException>(() => PostRepairVerification.Link(repair, scan with { Steps = repair.Steps }));
        Assert.Throws<ArgumentException>(() => PostRepairVerification.Link(repair, scan with { SessionId = repair.SessionId }));
        Assert.Throws<ArgumentException>(() => PostRepairVerification.Link(repair, scan with { StartedAt = Start }));
        Assert.Throws<ArgumentException>(() => PostRepairVerification.Link(repair, scan with { VerificationOfSessionId = Guid.NewGuid() }));
        Assert.Throws<ArgumentException>(() => PostRepairVerification.Link(repair, scan with { Steps = [scan.Steps[0] with { TargetId = "device" }] }));
        Assert.Throws<ArgumentException>(() => PostRepairVerification.Link(repair with { Steps = [repair.Steps[1]] }, scan));
        Assert.Throws<ArgumentException>(() => PostRepairVerification.Link(repair, scan with { Steps = [] }));
    }

    [Fact]
    public void ExplicitProviderEvidenceDescribesOnlyCurrentScanWithoutCausality()
    {
        var repair = Repair();
        var scan = PostRepairVerification.Link(repair, Scan(
            new(MaintenanceActionId.ScanWindowsImage, StepOutcome.Succeeded, "done", Verification: MaintenanceVerificationStatus.ProviderConfirmed, ImageHealthState: WindowsImageHealthState.NoCorruptionDetected),
            new(MaintenanceActionId.VerifySystemFiles, StepOutcome.Succeeded, "done", Verification: MaintenanceVerificationStatus.ProviderConfirmed, SystemFilesState: SfcVerificationState.IntegrityViolationsDetected)));
        var summary = PostRepairVerification.Describe(repair, scan);
        Assert.Contains("não detectou corrupção", summary);
        Assert.Contains("detectou violações", summary);
        Assert.DoesNotContain("ainda", summary);
        Assert.Contains("não comprova", summary);
        Assert.Equal(repair.SessionId, scan.VerificationOfSessionId);
        Assert.False(scan.RestorePointConfirmed);
    }

    [Fact]
    public void MissingFailedUnconfirmedOrIncompleteScansStayUnknown()
    {
        var repair = Repair();
        var step = new MaintenanceStepResult(MaintenanceActionId.ScanWindowsImage, StepOutcome.Succeeded, "done", ImageHealthState: WindowsImageHealthState.NoCorruptionDetected);
        var scan = PostRepairVerification.Link(repair, Scan(step));
        Assert.Contains("não confirmado", PostRepairVerification.Describe(repair, scan));
        Assert.DoesNotContain("não detectou corrupção", PostRepairVerification.Describe(repair, scan));
        Assert.Contains("não confirmado", PostRepairVerification.Describe(repair, scan with { IsComplete = false }));
        Assert.Contains("não confirmado", PostRepairVerification.Describe(repair, scan with { Steps = [step with { Outcome = StepOutcome.Failed, Verification = MaintenanceVerificationStatus.ProviderConfirmed }] }));
    }

    [Fact]
    public void HistoricalJsonDefaultsToUnknownAndNoLink()
    {
        var scan = Scan(new MaintenanceStepResult(MaintenanceActionId.ScanWindowsImage, StepOutcome.Succeeded, "done"));
        var json = JsonSerializer.Serialize(scan).Replace(",\"VerificationOfSessionId\":null", "")
            .Replace(",\"ImageHealthState\":0", "").Replace(",\"SystemFilesState\":0", "");
        var legacy = JsonSerializer.Deserialize<MaintenanceReport>(json)!;
        Assert.Null(legacy.VerificationOfSessionId);
        Assert.Equal(WindowsImageHealthState.Unknown, legacy.Steps[0].ImageHealthState);
        Assert.Equal(SfcVerificationState.Unknown, legacy.Steps[0].SystemFilesState);
    }
}

