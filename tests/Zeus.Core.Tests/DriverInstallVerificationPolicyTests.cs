using Zeus.Core;

namespace Zeus.Core.Tests;

public sealed class DriverInstallVerificationPolicyTests
{
    [Fact]
    public void ExactInstalledUpdateRecordConfirmsPackageButNotActiveDriver()
    {
        var result = DriverInstallVerificationPolicy.Resolve(true, true, false);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        Assert.Equal(MaintenanceVerificationStatus.ProviderConfirmed, result.Verification);
        Assert.Contains("não que o dispositivo já esteja usando", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingInstalledRecordWithRestartRequestRemainsPendingWithoutRetry()
    {
        var result = DriverInstallVerificationPolicy.Resolve(true, false, true);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        Assert.Equal(MaintenanceVerificationStatus.Pending, result.Verification);
        Assert.Contains("não repita", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingInstalledRecordWithoutRestartRequestRequiresReview()
    {
        var result = DriverInstallVerificationPolicy.Resolve(true, false, false);

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Equal(MaintenanceVerificationStatus.ManualReviewRequired, result.Verification);
    }

    [Fact]
    public void FailedInstallationCannotBeOverriddenByInstalledRecord()
    {
        var result = DriverInstallVerificationPolicy.Resolve(false, true, true);

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Equal(MaintenanceVerificationStatus.ManualReviewRequired, result.Verification);
    }
}
