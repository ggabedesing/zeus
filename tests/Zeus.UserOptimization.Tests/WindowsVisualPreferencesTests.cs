using System.Runtime.InteropServices;
using Microsoft.Win32;
using Zeus.Windows;

namespace Zeus.UserOptimization.Tests;

public sealed class WindowsAcceptanceFactAttribute : FactAttribute
{
    public WindowsAcceptanceFactAttribute()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("ZEUS_WINDOWS_ACCEPTANCE") != "1")
            Skip = "Requer runner Windows descartável com ZEUS_WINDOWS_ACCEPTANCE=1; não altera preferências de PCs pessoais.";
    }
}

public sealed class WindowsVisualPreferencesTests
{
    private const string PersonalizePath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string ValueName = "EnableTransparency";

    [WindowsAcceptanceFact]
    public async Task RealUserVisualSettingsAreBackedUpAppliedAndRestored()
    {
        var root = Path.Combine(Path.GetTempPath(), $"Zeus.VisualTests.{Guid.NewGuid():N}");
        var service = new UserOptimizationService(root);
        var originalAnimation = ReadAnimation();
        using var originalKey = Registry.CurrentUser.OpenSubKey(PersonalizePath, writable: false);
        var originalExists = originalKey?.GetValueNames().Contains(ValueName, StringComparer.OrdinalIgnoreCase) == true;
        var originalKind = originalExists ? originalKey!.GetValueKind(ValueName) : (RegistryValueKind?)null;
        var originalValue = originalExists ? originalKey!.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) : null;
        var preferences = new UserOptimizationPreferences(UsageProfile.Work, originalAnimation, originalValue is not int number || number != 0);
        var originalPowerPlan = Assert.Single(await service.ReadPowerPlansAsync(), plan => plan.IsActive).Id;
        Guid sessionId = default;
        try
        {
            var applied = await service.ApplyPreferencesAsync(preferences);
            sessionId = applied.SessionId;
            Assert.True(applied.Succeeded, applied.Message);
            Assert.NotEqual(Guid.Empty, sessionId);
            Assert.Equal(!preferences.ReduceAnimations, ReadAnimation());
            using (var current = Registry.CurrentUser.OpenSubKey(PersonalizePath, writable: false))
            {
                Assert.NotNull(current);
                Assert.Equal(RegistryValueKind.DWord, current.GetValueKind(ValueName));
                Assert.Equal(preferences.ReduceTransparency ? 0 : 1, current.GetValue(ValueName));
            }
            var journal = Assert.Single(await service.ListChangesAsync());
            Assert.Equal(sessionId, journal.Id);
            Assert.False(journal.Restored);
            // The usage profile is informational and must not automatically switch power plans.
            Assert.Equal(originalPowerPlan, Assert.Single(await service.ReadPowerPlansAsync(), plan => plan.IsActive).Id);
            var restored = await service.RestoreAsync(sessionId);
            Assert.True(restored.Succeeded, restored.Message);
            Assert.Equal(originalAnimation, ReadAnimation());
            using (var current = Registry.CurrentUser.OpenSubKey(PersonalizePath, writable: false))
            {
                Assert.Equal(originalExists, current?.GetValueNames().Contains(ValueName, StringComparer.OrdinalIgnoreCase) == true);
                if (originalExists)
                {
                    Assert.Equal(originalKind, current!.GetValueKind(ValueName));
                    Assert.Equal(originalValue, current.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames));
                }
            }
            Assert.True(Assert.Single(await service.ListChangesAsync()).Restored);
        }
        finally
        {
            // The disposable CI fixture restores even when an assertion fails after a partial write.
            try
            {
                foreach (var journal in await service.ListChangesAsync())
                    if (!journal.Restored) await service.RestoreAsync(journal.Id);
            }
            finally
            {
                Assert.True(SetAnimation(0x1043, 0, originalAnimation, 0x01 | 0x02), "Fixture could not restore animation state.");
                using var key = Registry.CurrentUser.CreateSubKey(PersonalizePath, writable: true);
                if (originalExists) key.SetValue(ValueName, originalValue!, originalKind!.Value);
                else key.DeleteValue(ValueName, throwOnMissingValue: false);
                key.Flush();
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
        }
    }

    private static bool ReadAnimation()
    {
        Assert.True(GetAnimation(0x1042, 0, out var enabled, 0), "Fixture could not read animation state.");
        return enabled != 0;
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetAnimation(uint action, uint parameter, out int value, uint flags);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetAnimation(uint action, uint parameter, [MarshalAs(UnmanagedType.Bool)] bool value, uint flags);
}
