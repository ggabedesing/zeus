using Zeus.Core;
using Zeus.Windows;

namespace Zeus.Windows.Acceptance.Tests;

public sealed class PendingMaintenanceSessionsTests
{
    [Fact]
    public async Task InvalidReceiptDoesNotPreventRecoveryOfOtherSessions()
    {
        var root = Path.Combine(Path.GetTempPath(), "Zeus.InvalidReceipt." + Guid.NewGuid().ToString("N"));
        var sessions = new PendingMaintenanceSessions(root);
        var invalid = Guid.NewGuid(); var valid = Guid.NewGuid();
        try
        {
            await sessions.RememberAsync(valid, [new(MaintenanceActionId.VerifySystemFiles)], DateTimeOffset.UtcNow);
            await File.WriteAllTextAsync(Path.Combine(root, invalid.ToString("D") + ".json"), "{}");
            var recovered = await sessions.RecoverAsync();
            Assert.Equal(2, recovered.Count);
            var invalidReport = Assert.Single(recovered, report => report.SessionId == invalid);
            Assert.False(invalidReport.IsComplete);
            Assert.Empty(invalidReport.Steps);
            Assert.Single(Assert.Single(recovered, report => report.SessionId == valid).Steps);
        }
        finally
        {
            await sessions.ForgetAsync(valid); await sessions.ForgetAsync(invalid);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PostRepairReceiptPreservesLinkWhenNoHelperResultExists()
    {
        var root = Path.Combine(Path.GetTempPath(), "Zeus.PendingRepair." + Guid.NewGuid().ToString("N"));
        var sessions = new PendingMaintenanceSessions(root);
        var id = Guid.NewGuid();
        var parent = Guid.NewGuid();
        try
        {
            await sessions.RememberAsync(id, [new(MaintenanceActionId.ScanWindowsImage), new(MaintenanceActionId.VerifySystemFiles)],
                DateTimeOffset.UtcNow, verificationOfSessionId: parent);
            var receipt = await File.ReadAllTextAsync(Path.Combine(root, id.ToString("D") + ".json"));
            Assert.Contains(parent.ToString("D"), receipt);
            var recovered = Assert.Single(await sessions.RecoverAsync());
            Assert.Equal(parent, recovered.VerificationOfSessionId);
            Assert.False(recovered.IsComplete);
            Assert.Equal(2, recovered.Steps.Count);
            Assert.All(recovered.Steps, step =>
            {
                Assert.Equal(MaintenanceVerificationStatus.ManualReviewRequired, step.Verification);
                Assert.Equal(WindowsImageHealthState.Unknown, step.ImageHealthState);
                Assert.Equal(SfcVerificationState.Unknown, step.SystemFilesState);
            });
        }
        finally
        {
            await sessions.ForgetAsync(id);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PostRepairReceiptCannotAuthorizeRepairsOrSelfLinks()
    {
        var root = Path.Combine(Path.GetTempPath(), "Zeus.PendingRepair." + Guid.NewGuid().ToString("N"));
        var sessions = new PendingMaintenanceSessions(root);
        var id = Guid.NewGuid();
        await Assert.ThrowsAsync<ArgumentException>(() => sessions.RememberAsync(id, [new(MaintenanceActionId.RepairWindowsImage)], DateTimeOffset.UtcNow, verificationOfSessionId: Guid.NewGuid()));
        await Assert.ThrowsAsync<ArgumentException>(() => sessions.RememberAsync(id, [new(MaintenanceActionId.ScanWindowsImage)], DateTimeOffset.UtcNow, verificationOfSessionId: id));
        await Assert.ThrowsAsync<ArgumentException>(() => sessions.RememberAsync(id, [new(MaintenanceActionId.ScanWindowsImage)], DateTimeOffset.UtcNow, verificationOfSessionId: Guid.Empty));
        Assert.False(Directory.Exists(root));
    }

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
