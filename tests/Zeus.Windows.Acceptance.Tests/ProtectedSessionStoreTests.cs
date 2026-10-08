using System.Security.AccessControl;
using System.Security.Principal;
using Zeus.Core;
using Zeus.Windows;

namespace Zeus.Windows.Acceptance.Tests;

public sealed class ProtectedSessionStoreTests
{
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
