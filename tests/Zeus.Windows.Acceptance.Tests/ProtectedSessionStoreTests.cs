using System.Security.AccessControl;
using System.Security.Principal;
using Zeus.Core;
using Zeus.Windows;

namespace Zeus.Windows.Acceptance.Tests;

public sealed class ProtectedSessionStoreTests
{
    [AdministratorFact]
    public async Task ProtectedDriverCheckpointRecoversObservationWithoutClaimingInstallSuccess()
    {
        var id = Guid.NewGuid(); var updateId = Guid.NewGuid(); var started = DateTimeOffset.UtcNow;
        var request = new MaintenanceRequest(MaintenanceActionId.InstallDriverUpdate, $"{updateId:D}:1", UpdateServerSelection: 2);
        var evidence = new DriverActiveEvidence("PCI\\VEN_1234", new(started, true,
            [new("PCI\\VEN_1234\\DEVICE", "oem1.inf", "1", "Vendor", true)], []));
        var receiptRoot = Path.Combine(Path.GetTempPath(), "Zeus.PendingDriver." + Guid.NewGuid().ToString("N"));
        var receipts = new PendingMaintenanceSessions(receiptRoot);
        string? directory = null;
        try
        {
            directory = SessionStore.CreateSession(id);
            await using (var file = SessionStore.CreateDriverActiveCheckpoint(id, updateId, 1))
                await System.Text.Json.JsonSerializer.SerializeAsync(file, evidence);
            var path = Path.Combine(directory, SessionStore.GetDriverActiveCheckpointFileName(updateId, 1));
            AssertProtectedAcl(new FileInfo(path).GetAccessControl());
            Assert.Throws<ArgumentException>(() => SessionStore.CreateLog(id, Path.GetFileName(path)));
            var checkpoint = await SessionStore.ReadDriverActiveCheckpointAsync(id, updateId, 1);
            Assert.Equal(evidence.Before.Devices[0], checkpoint.Before.Devices[0]);
            await receipts.RememberAsync(id, [request], started);

            // No report exists: the checkpoint must survive but cannot confirm WUA outcomes.
            var recovered = Assert.Single(await receipts.RecoverAsync());
            Assert.False(recovered.IsComplete);
            var step = Assert.Single(recovered.Steps);
            Assert.Equal(StepOutcome.Skipped, step.Outcome);
            Assert.Equal(MaintenanceVerificationStatus.ManualReviewRequired, step.Verification);
            Assert.Equal(2, step.UpdateServerSelection);
            Assert.NotNull(step.ActiveDriver);
            Assert.Single(MaintenancePolicy.FindUnresolvedAttempts([request], [recovered]));

            var pending = step with { ActiveDriver = null, UpdateServerSelection = null, Verification = MaintenanceVerificationStatus.Pending };
            await SessionStore.WriteProgressReportAsync(id, recovered with { Steps = [pending] });
            var recoveredPending = Assert.Single(await receipts.RecoverAsync());
            Assert.False(recoveredPending.IsComplete);
            Assert.Equal(MaintenanceVerificationStatus.Pending, recoveredPending.Steps[0].Verification);
            Assert.Equal(2, recoveredPending.Steps[0].UpdateServerSelection);
            Assert.NotNull(recoveredPending.Steps[0].ActiveDriver);

            // Administrative corruption and an ACL granting user writes are both rejected.
            await File.WriteAllBytesAsync(path, new byte[DriverActiveStatePolicy.MaximumPersistedBytes + 1]);
            await Assert.ThrowsAsync<InvalidDataException>(() => SessionStore.ReadDriverActiveCheckpointAsync(id, updateId, 1));
            await File.WriteAllTextAsync(path, System.Text.Json.JsonSerializer.Serialize(evidence));
            var fileInfo = new FileInfo(path);
            var security = fileInfo.GetAccessControl();
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                FileSystemRights.WriteData, AccessControlType.Allow));
            fileInfo.SetAccessControl(security);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => SessionStore.ReadDriverActiveCheckpointAsync(id, updateId, 1));
        }
        finally
        {
            await receipts.ForgetAsync(id);
            if (Directory.Exists(receiptRoot)) Directory.Delete(receiptRoot, recursive: true);
            if (directory is not null && Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [AdministratorFact]
    public async Task RecoveryPreservesDurablePendingProgressWithoutClaimingCompletion()
    {
        var id = Guid.NewGuid();
        var started = DateTimeOffset.UtcNow;
        var request = new MaintenanceRequest(MaintenanceActionId.VerifySystemFiles);
        string? directory = null;
        var receiptDirectory = Path.Combine(Path.GetTempPath(), "Zeus.PendingProgress." + Guid.NewGuid().ToString("N"));
        var receipts = new PendingMaintenanceSessions(Path.Combine(receiptDirectory, "PendingMaintenance"));
        try
        {
            directory = SessionStore.CreateSession(id);
            var pendingStep = new MaintenanceStepResult(request.Action, StepOutcome.Skipped,
                "Ação em andamento; ainda não existe confirmação de conclusão.",
                Verification: MaintenanceVerificationStatus.Pending);
            await SessionStore.WriteProgressReportAsync(id, new MaintenanceReport(id, started,
                DateTimeOffset.UtcNow, false, [pendingStep], IsComplete: false));
            await receipts.RememberAsync(id, [request], started);

            // Recovery sees the last durable progress state as if the helper had
            // exited before writing its final report. It must leave the action unresolved.
            var recovered = Assert.Single(await receipts.RecoverAsync());
            Assert.Equal(id, recovered.SessionId);
            Assert.False(recovered.IsComplete);
            var step = Assert.Single(recovered.Steps);
            Assert.Equal(request.Action, step.Action);
            Assert.Equal(MaintenanceVerificationStatus.Pending, step.Verification);
            Assert.Contains("ainda não existe confirmação", step.Message, StringComparison.OrdinalIgnoreCase);
            var unresolved = Assert.Single(MaintenancePolicy.FindUnresolvedAttempts([request], [recovered]));
            Assert.Equal(id, unresolved.SessionId);
            Assert.Equal(MaintenanceVerificationStatus.Pending, unresolved.Verification);
        }
        finally
        {
            await receipts.ForgetAsync(id);
            if (Directory.Exists(receiptDirectory)) Directory.Delete(receiptDirectory, recursive: true);
            // Only the GUID directory created above is removed; no shared lock or other session is touched.
            if (directory is not null && Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [AdministratorFact]
    public async Task RealProtectedSessionSupportsAtomicProgressWithoutUserWriteAccess()
    {
        var id = Guid.NewGuid();
        string? directory = null;
        try
        {
            directory = SessionStore.CreateSession(id);
            Assert.Equal(id.ToString("D"), Path.GetFileName(directory));
            AssertProtectedAcl(new DirectoryInfo(directory).GetAccessControl());
            Assert.Throws<IOException>(() => SessionStore.CreateSession(id));
            using (var firstLock = SessionStore.AcquireMaintenanceLock(id))
            {
                Assert.Throws<IOException>(() => SessionStore.AcquireMaintenanceLock(id));
                var started = DateTimeOffset.UtcNow;
                var progress = new MaintenanceReport(id, started, started, false, [], IsComplete: false);
                await SessionStore.WriteProgressReportAsync(id, progress);
                var pending = await SessionStore.ReadReportAsync(id);
                Assert.Equal(id, pending.SessionId);
                Assert.False(pending.IsComplete);
                Assert.Empty(pending.Steps);
                AssertProtectedAcl(new FileInfo(Path.Combine(directory, "report.json")).GetAccessControl());

                var completed = progress with { FinishedAt = DateTimeOffset.UtcNow, IsComplete = true };
                await using (var concurrentReader = new FileStream(Path.Combine(directory, "report.json"),
                    FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                {
                    await SessionStore.WriteReportAsync(id, completed);
                    var earlier = await System.Text.Json.JsonSerializer.DeserializeAsync<MaintenanceReport>(concurrentReader);
                    Assert.NotNull(earlier);
                    Assert.False(earlier.IsComplete);
                }
                var actual = await SessionStore.ReadReportAsync(id);
                Assert.Equal(id, actual.SessionId);
                Assert.True(actual.IsComplete);
                Assert.Empty(actual.Steps);
                Assert.Empty(Directory.GetFiles(directory, "*.pending"));
                await Assert.ThrowsAsync<ArgumentException>(() =>
                    SessionStore.WriteReportAsync(id, completed with { SessionId = Guid.NewGuid() }));
            }
            var backup = SessionStore.CreateProtectedChildDirectory(id, "driver-backup");
            Assert.Equal(Path.Combine(directory, "driver-backup"), backup);
            AssertProtectedAcl(new DirectoryInfo(backup).GetAccessControl());
            Assert.Throws<ArgumentException>(() => SessionStore.CreateProtectedChildDirectory(id, "other"));
            Assert.Throws<ArgumentException>(() => SessionStore.CreateProtectedChildDirectory(id, "../outside"));
            Assert.Throws<ArgumentException>(() => SessionStore.CreateLog(id, "../outside.log"));
        }
        finally
        {
            // Only this new GUID directory is deleted. No existing session,
            // shared lock, recovery point or machine configuration is removed.
            if (directory is not null && Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static void AssertProtectedAcl(FileSystemSecurity security)
    {
        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        Assert.True(security.AreAccessRulesProtected);
        var owner = security.GetOwner(typeof(SecurityIdentifier));
        Assert.True(administrators.Equals(owner) || system.Equals(owner));
        const FileSystemRights writeRights = FileSystemRights.Write | FileSystemRights.Delete |
            FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
        Assert.Contains(rules, rule => users.Equals(rule.IdentityReference) &&
            rule.AccessControlType == AccessControlType.Allow && (rule.FileSystemRights & FileSystemRights.ReadData) != 0);
        Assert.All(rules.Where(rule => rule.AccessControlType == AccessControlType.Allow &&
            !administrators.Equals(rule.IdentityReference) && !system.Equals(rule.IdentityReference)),
            rule => Assert.Equal((FileSystemRights)0, rule.FileSystemRights & writeRights));
    }
}

public sealed class AdministratorFactAttribute : FactAttribute
{
    public AdministratorFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Protected session acceptance requires Windows.";
            return;
        }
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            Skip = "Protected session acceptance requires an administrator token; no session was created.";
    }
}
