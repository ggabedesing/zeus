using Microsoft.Win32;
using Zeus.Windows;

namespace Zeus.UserOptimization.Tests;

public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Validação real HKCU executada somente no runner Windows.";
    }
}

/// <summary>Fixtures touch only their own Zeus.Test.GUID value and delete it even on failure.</summary>
public sealed class WindowsStartupTests
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Command = @"%SystemRoot%\System32\notepad.exe";

    [WindowsFact]
    public async Task DisableAndRestorePreserveUnexpandedCommandAndExactType()
    {
        await WithFixtureAsync(async (service, name, key) =>
        {
            key.SetValue(name, Command, RegistryValueKind.ExpandString);
            var entry = Assert.Single(await service.ReadStartupAsync(), item => item.Name == name);
            Assert.Equal(Command, entry.Command);
            var disabled = await service.DisableStartupAsync(entry.Id);
            Assert.True(disabled.Succeeded, disabled.Message);
            Assert.DoesNotContain(name, key.GetValueNames());
            Assert.Equal(UserChangeStatus.Applied, Assert.Single(await service.ListChangesAsync()).Status);
            var retained = Assert.Single(await service.ReadStartupAsync(), item => item.Name == name);
            Assert.False(retained.IsEnabled);
            var restored = await service.RestoreAsync(disabled.SessionId);
            Assert.True(restored.Succeeded, restored.Message);
            Assert.Equal(RegistryValueKind.ExpandString, key.GetValueKind(name));
            Assert.Equal(Command, key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames));
            var change = Assert.Single(await service.ListChangesAsync());
            Assert.True(change.Restored);
            Assert.Equal(UserChangeStatus.Restored, change.Status);
            Assert.True((await service.RestoreAsync(disabled.SessionId)).Succeeded);
        });
    }

    [WindowsFact]
    public async Task StaleFingerprintCannotDeleteChangedEntry()
    {
        await WithFixtureAsync(async (service, name, key) =>
        {
            key.SetValue(name, Command, RegistryValueKind.ExpandString);
            var old = Assert.Single(await service.ReadStartupAsync(), item => item.Name == name);
            key.SetValue(name, Command + " ", RegistryValueKind.ExpandString);
            var result = await service.DisableStartupAsync(old.Id);
            Assert.False(result.Succeeded);
            Assert.Equal(Command + " ", key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames));
            Assert.Empty(await service.ListChangesAsync());
        });
    }

    [WindowsFact]
    public async Task UndoDoesNotOverwriteChangedEntry()
    {
        await WithFixtureAsync(async (service, name, key) =>
        {
            key.SetValue(name, Command, RegistryValueKind.ExpandString);
            var entry = Assert.Single(await service.ReadStartupAsync(), item => item.Name == name);
            var disabled = await service.DisableStartupAsync(entry.Id);
            Assert.True(disabled.Succeeded, disabled.Message);
            key.SetValue(name, Command + " ", RegistryValueKind.String);
            var restored = await service.RestoreAsync(disabled.SessionId);
            Assert.False(restored.Succeeded);
            Assert.Equal(RegistryValueKind.String, key.GetValueKind(name));
            Assert.Equal(Command + " ", key.GetValue(name));
            var change = Assert.Single(await service.ListChangesAsync());
            Assert.False(change.Restored);
            Assert.Equal(UserChangeStatus.RestoreBlocked, change.Status);
        });
    }

    [WindowsFact]
    public async Task SecurityHeuristicProtectsEntryAndDescribesItsLimit()
    {
        await WithFixtureAsync(async (service, name, key) =>
        {
            key.SetValue(name, Command + " --defender", RegistryValueKind.ExpandString);
            var entry = Assert.Single(await service.ReadStartupAsync(), item => item.Name == name);
            Assert.True(entry.IsProtected);
            Assert.Contains("heurística", entry.ProtectionReason);
            Assert.False((await service.DisableStartupAsync(entry.Id)).Succeeded);
            Assert.Contains(name, key.GetValueNames());
            Assert.Empty(await service.ListChangesAsync());
        });
    }

    [WindowsFact]
    public async Task BackupAndSyncEntriesArePreserved()
    {
        await WithFixtureAsync(async (service, name, key) =>
        {
            key.SetValue(name, Command + " --backup", RegistryValueKind.ExpandString);
            var entry = Assert.Single(await service.ReadStartupAsync(), item => item.Name == name);
            Assert.True(entry.IsProtected);
            Assert.Contains("backup", entry.ProtectionReason);
            Assert.False((await service.DisableStartupAsync(entry.Id)).Succeeded);
            Assert.Contains(name, key.GetValueNames());
        });
    }

    [WindowsFact]
    public async Task CurrentPowerPlanCanBeReadWithoutChangingIt()
    {
        var root = Path.Combine(Path.GetTempPath(), $"Zeus.UserTests.{Guid.NewGuid():N}");
        try
        {
            var service = new UserOptimizationService(root);
            var plans = await service.ReadPowerPlansAsync();
            var active = Assert.Single(plans, plan => plan.IsActive);
            Assert.NotEqual(Guid.Empty, active.Id);
            Assert.False((await service.SetPowerPlanAsync(Guid.NewGuid())).Succeeded);
            var result = await service.SetPowerPlanAsync(active.Id);
            Assert.True(result.Succeeded, result.Message);
            Assert.Empty(await service.ListChangesAsync());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static async Task WithFixtureAsync(Func<UserOptimizationService, string, RegistryKey, Task> action)
    {
        var name = $"Zeus.Test.{Guid.NewGuid():N}";
        var root = Path.Combine(Path.GetTempPath(), $"Zeus.UserTests.{Guid.NewGuid():N}");
        using var key = Registry.CurrentUser.CreateSubKey(RunPath, writable: true);
        try { await action(new UserOptimizationService(root), name, key); }
        finally
        {
            key.DeleteValue(name, throwOnMissingValue: false);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
