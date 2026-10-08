using Zeus.Core;
using Zeus.Windows;

namespace Zeus.Windows.Acceptance.Tests;

public sealed class PendingMaintenanceSessionsTests
{
    [Fact]
    public async Task ForgottenLaunchReceiptIsNotReportedAsAnUnknownInterruptedSession()
    {
        var sessions = new PendingMaintenanceSessions();
        var id = Guid.NewGuid();
        var started = DateTimeOffset.UtcNow;
        var requests = new[] { new MaintenanceRequest(MaintenanceActionId.VerifySystemFiles) };

        try
        {
            await sessions.RememberAsync(id, requests, started);
            var unknown = Assert.Single(await sessions.RecoverAsync(), report => report.SessionId == id);
            Assert.False(unknown.IsComplete);
            Assert.Contains("não foi possível confirmar", unknown.Error, StringComparison.OrdinalIgnoreCase);

            await sessions.ForgetAsync(id);

            Assert.DoesNotContain((await sessions.RecoverAsync()), report => report.SessionId == id);
        }
        finally
        {
            await sessions.ForgetAsync(id);
        }
    }
}
