using System.Runtime.InteropServices;
using Microsoft.Win32;
using Zeus.Windows;

namespace Zeus.UserOptimization.Tests;

public sealed class WindowsAcceptanceFactAttribute : FactAttribute
{
    public WindowsAcceptanceFactAttribute()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("ZEUS_WINDOWS_ACCEPTANCE") != "1")
            Skip = "Requer Windows com ZEUS_WINDOWS_ACCEPTANCE=1; o teste aplica e restaura as preferências visuais do usuário.";
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
            Assert.Equal(UserChangeStatus.Applied, journal.Status);
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
            var restoredJournal = Assert.Single(await service.ListChangesAsync());
            Assert.True(restoredJournal.Restored);
            Assert.Equal(UserChangeStatus.Restored, restoredJournal.Status);
        }
        finally
        {
            // The fixture restores even when an assertion fails after a partial write.
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

public sealed class WallpaperChangeTests
{
    [WindowsFact]
    public void WindowsWallpaperStatusCanBeReadWithoutChangingTheDesktop()
    {
        var platform = new WindowsWallpaperPlatform();

        _ = platform.IsSlideshowConfigured();
        if (platform.HasUniformWallpaper())
            Assert.True(Path.IsPathFullyQualified(platform.GetWallpaperPath()));
    }

    [WindowsFact]
    public async Task WallpaperChangeSavesPreviousImageAndRestoresItFromHistory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"Zeus.WallpaperTests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var previous = Path.Combine(root, "previous.bmp");
        var selected = Path.Combine(root, "selected.bmp");
        await File.WriteAllBytesAsync(previous, Bmp(1, 2, 3));
        await File.WriteAllBytesAsync(selected, Bmp(4, 5, 6));
        var platform = new FixtureWallpaperPlatform(previous);
        try
        {
            var service = new UserOptimizationService(Path.Combine(root, "history"), platform);
            var applied = await service.ApplyWallpaperAsync(selected);
            Assert.True(applied.Succeeded, applied.Message);
            Assert.Equal(Path.GetFullPath(selected), platform.CurrentPath);
            var journal = Assert.Single(await service.ListChangesAsync());
            Assert.Equal(UserChangeStatus.Applied, journal.Status);
            Assert.False(journal.Restored);

            var restored = await service.RestoreAsync(applied.SessionId);

            Assert.True(restored.Succeeded, restored.Message);
            Assert.Equal(Bmp(1, 2, 3), await File.ReadAllBytesAsync(platform.CurrentPath));
            Assert.True(Assert.Single(await service.ListChangesAsync()).Restored);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [WindowsFact]
    public async Task WallpaperRestorePreservesAnImageChangedOutsideTheZeusSession()
    {
        var root = Path.Combine(Path.GetTempPath(), $"Zeus.WallpaperTests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var previous = Path.Combine(root, "previous.bmp");
        var selected = Path.Combine(root, "selected.bmp");
        var external = Path.Combine(root, "external.bmp");
        await File.WriteAllBytesAsync(previous, Bmp(1, 2, 3));
        await File.WriteAllBytesAsync(selected, Bmp(4, 5, 6));
        await File.WriteAllBytesAsync(external, Bmp(7, 8, 9));
        var platform = new FixtureWallpaperPlatform(previous);
        try
        {
            var service = new UserOptimizationService(Path.Combine(root, "history"), platform);
            var applied = await service.ApplyWallpaperAsync(selected);
            Assert.True(applied.Succeeded, applied.Message);
            platform.CurrentPath = external;

            var restored = await service.RestoreAsync(applied.SessionId);

            Assert.False(restored.Succeeded);
            Assert.Contains("alterado fora", restored.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(external, platform.CurrentPath);
            Assert.Equal(UserChangeStatus.RestoreBlocked, Assert.Single(await service.ListChangesAsync()).Status);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [WindowsFact]
    public async Task WallpaperChangeRejectsMisleadingExtensionBeforeCallingWindows()
    {
        var root = Path.Combine(Path.GetTempPath(), $"Zeus.WallpaperTests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var previous = Path.Combine(root, "previous.bmp");
        var mislabeled = Path.Combine(root, "not-an-image.png");
        await File.WriteAllBytesAsync(previous, Bmp(1, 2, 3));
        await File.WriteAllTextAsync(mislabeled, "not a PNG");
        var platform = new FixtureWallpaperPlatform(previous);
        try
        {
            var service = new UserOptimizationService(Path.Combine(root, "history"), platform);
            var result = await service.ApplyWallpaperAsync(mislabeled);
            Assert.False(result.Succeeded);
            Assert.Equal(previous, platform.CurrentPath);
            Assert.Empty(await service.ListChangesAsync());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [WindowsTheory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task WallpaperChangeRefusesSlideshowsAndPerMonitorConfigurations(bool slideshow, bool uniform)
    {
        var root = Path.Combine(Path.GetTempPath(), $"Zeus.WallpaperTests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var previous = Path.Combine(root, "previous.bmp");
        var selected = Path.Combine(root, "selected.bmp");
        await File.WriteAllBytesAsync(previous, Bmp(1, 2, 3));
        await File.WriteAllBytesAsync(selected, Bmp(4, 5, 6));
        var platform = new FixtureWallpaperPlatform(previous) { Slideshow = slideshow, Uniform = uniform };
        try
        {
            var service = new UserOptimizationService(Path.Combine(root, "history"), platform);

            var result = await service.ApplyWallpaperAsync(selected);

            Assert.False(result.Succeeded);
            Assert.Contains(slideshow ? "slides" : "monitores", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(previous, platform.CurrentPath);
            Assert.Empty(await service.ListChangesAsync());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static byte[] Bmp(byte red, byte green, byte blue) =>
    [0x42, 0x4d, 0x3a, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x36, 0x00, 0x00, 0x00,
     0x28, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00,
     0x18, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x13, 0x0b, 0x00, 0x00,
     0x13, 0x0b, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
     blue, green, red, 0x00];

    private sealed class FixtureWallpaperPlatform(string initialPath) : IWallpaperPlatform
    {
        public string CurrentPath { get; set; } = initialPath;
        public bool Slideshow { get; set; }
        public bool Uniform { get; set; } = true;
        public bool IsSlideshowConfigured() => Slideshow;
        public bool HasUniformWallpaper() => Uniform;
        public string GetWallpaperPath() => CurrentPath;
        public bool SetWallpaperPath(string path) { CurrentPath = path; return File.Exists(path); }
    }
}
