using Zeus.Core;
using Zeus.Windows;

namespace Zeus.Windows.Acceptance.Tests;

public sealed class PendingMaintenanceSessionsTests
{
    [Fact]
    public async Task ForgottenLaunchReceiptIsNotReportedAsAnUnknownInterruptedSession()
    {
        var root = Path.Combine(Path.GetTempPath(), "Zeus.PendingSessions." + Guid.NewGuid().ToString("N"));
        var sessions = new PendingMaintenanceSessions(Path.Combine(root, "PendingMaintenance"));
        var id = Guid.NewGuid();
        var started = DateTimeOffset.UtcNow;
        var requests = new[] { new MaintenanceRequest(MaintenanceActionId.VerifySystemFiles) };

        try
        {
            await sessions.RememberAsync(id, requests, started);
            var unknown = Assert.Single(await sessions.RecoverAsync(), report => report.SessionId == id);
            Assert.False(unknown.IsComplete);
            Assert.Contains("não foi possível confirmar", unknown.Error, StringComparison.OrdinalIgnoreCase);
            var unresolved = Assert.Single(MaintenancePolicy.FindUnresolvedAttempts(requests, [unknown]));
            Assert.Equal(id, unresolved.SessionId);
            Assert.Equal(MaintenanceActionId.VerifySystemFiles, unresolved.Action);
            Assert.Equal(MaintenanceVerificationStatus.ManualReviewRequired, unresolved.Verification);

            await sessions.ForgetAsync(id);

            Assert.DoesNotContain((await sessions.RecoverAsync()), report => report.SessionId == id);
        }
        finally
        {
            await sessions.ForgetAsync(id);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
