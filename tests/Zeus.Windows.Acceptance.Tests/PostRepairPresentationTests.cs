using Zeus.Core;
using Zeus.Desktop;

namespace Zeus.Windows.Acceptance.Tests;

public sealed class PostRepairPresentationTests
{
    [Fact]
    public void HistoryOffersScansOnlyForCompletedRepairAndShowsSeparateLink()
    {
        var started = DateTimeOffset.UtcNow.AddMinutes(-5);
        var repair = new MaintenanceReport(Guid.NewGuid(), started, started.AddMinutes(1), true,
            [new(MaintenanceActionId.RepairWindowsImage, StepOutcome.Succeeded, "comando concluído", Verification: MaintenanceVerificationStatus.ManualReviewRequired)]);
        Assert.True(HistoryRow.From(repair).CanVerifyAfterRepair);
        Assert.False(HistoryRow.From(repair with { IsComplete = false }).CanVerifyAfterRepair);
        var scan = PostRepairVerification.Link(repair, new(Guid.NewGuid(), started.AddMinutes(2), started.AddMinutes(3), false,
            [new(MaintenanceActionId.ScanWindowsImage, StepOutcome.Succeeded, "saída desconhecida", Verification: MaintenanceVerificationStatus.ManualReviewRequired)]));
        var row = HistoryRow.From(scan);
        Assert.False(row.CanVerifyAfterRepair);
        Assert.Contains(repair.SessionId.ToString("D"), row.PostRepairSummary);
        Assert.Contains("revisão manual necessária", Assert.Single(row.Steps));
        Assert.Contains("não confirmado", PostRepairVerification.Describe(repair, scan));
    }
}
