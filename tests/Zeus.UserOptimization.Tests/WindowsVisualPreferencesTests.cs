using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
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
        var slideshow = platform.IsSlideshowConfigured();
        if (slideshow) return;
        try
        {
            var monitors = platform.GetAttachedMonitorWallpapers();
            Assert.NotEmpty(monitors);
            Assert.All(monitors, state => Assert.True(Path.IsPathFullyQualified(state.Path)));
        }
        catch (InvalidDataException error)
        {
            Assert.Contains("imagem estática", error.Message, StringComparison.OrdinalIgnoreCase);
        }
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
        var previousOne = Path.Combine(root, "previous-one.bmp");
        var previousTwo = Path.Combine(root, "previous-two.bmp");
        var selected = Path.Combine(root, "selected.bmp");
        var external = Path.Combine(root, "external.bmp");
        await File.WriteAllBytesAsync(previousOne, Bmp(1, 2, 3));
        await File.WriteAllBytesAsync(previousTwo, Bmp(2, 3, 4));
        await File.WriteAllBytesAsync(selected, Bmp(4, 5, 6));
        await File.WriteAllBytesAsync(external, Bmp(7, 8, 9));
        var platform = new FixtureWallpaperPlatform(new Dictionary<string, string>
        {
            ["DISPLAY-1"] = previousOne,
            ["DISPLAY-2"] = previousTwo
        });
        try
        {
            var service = new UserOptimizationService(Path.Combine(root, "history"), platform);
            var applied = await service.ApplyWallpaperAsync(selected);
            Assert.True(applied.Succeeded, applied.Message);
            platform.MonitorPaths["DISPLAY-2"] = Path.GetFullPath(external);

            var restored = await service.RestoreAsync(applied.SessionId);

            Assert.False(restored.Succeeded);
            Assert.Contains("alterado fora", restored.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(Path.GetFullPath(selected), platform.MonitorPaths["DISPLAY-1"]);
            Assert.Equal(Path.GetFullPath(external), platform.MonitorPaths["DISPLAY-2"]);
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

    [WindowsFact]
    public async Task WallpaperChangeRestoresTheDistinctImageForEachMonitor()
    {
        var root = Path.Combine(Path.GetTempPath(), $"Zeus.WallpaperTests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var previousOne = Path.Combine(root, "previous-one.bmp");
        var previousTwo = Path.Combine(root, "previous-two.bmp");
        var selected = Path.Combine(root, "selected.bmp");
        await File.WriteAllBytesAsync(previousOne, Bmp(1, 2, 3));
        await File.WriteAllBytesAsync(previousTwo, Bmp(2, 3, 4));
        await File.WriteAllBytesAsync(selected, Bmp(4, 5, 6));
        var platform = new FixtureWallpaperPlatform(new Dictionary<string, string>
        {
            ["DISPLAY-1"] = previousOne,
            ["DISPLAY-2"] = previousTwo
        });
        try
        {
            var service = new UserOptimizationService(Path.Combine(root, "history"), platform);

            var applied = await service.ApplyWallpaperAsync(selected);

            Assert.True(applied.Succeeded, applied.Message);
            Assert.All(platform.MonitorPaths.Values, path => Assert.Equal(Path.GetFullPath(selected), path));
            Assert.Contains("2 de 2 monitor(es)", applied.Message, StringComparison.Ordinal);

            var restored = await service.RestoreAsync(applied.SessionId);

            Assert.True(restored.Succeeded, restored.Message);
            Assert.Equal(Bmp(1, 2, 3), await File.ReadAllBytesAsync(platform.MonitorPaths["DISPLAY-1"]));
            Assert.Equal(Bmp(2, 3, 4), await File.ReadAllBytesAsync(platform.MonitorPaths["DISPLAY-2"]));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [WindowsFact]
    public async Task WallpaperChangeCanTargetOneMonitorAndRestoreOnlyThatMonitor()
    {
        var root = Path.Combine(Path.GetTempPath(), $"Zeus.WallpaperTests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var first = Path.Combine(root, "first.bmp");
        var second = Path.Combine(root, "second.bmp");
        var selected = Path.Combine(root, "selected.bmp");
        await File.WriteAllBytesAsync(first, Bmp(1, 2, 3));
        await File.WriteAllBytesAsync(second, Bmp(2, 3, 4));
        await File.WriteAllBytesAsync(selected, Bmp(4, 5, 6));
        var platform = new FixtureWallpaperPlatform(new Dictionary<string, string> { ["DISPLAY-1"] = first, ["DISPLAY-2"] = second });
        try
        {
            var service = new UserOptimizationService(Path.Combine(root, "history"), platform);
            var applied = await service.ApplyWallpaperAsync(selected, "DISPLAY-1");
            Assert.True(applied.Succeeded, applied.Message);
            Assert.Equal(Path.GetFullPath(selected), platform.MonitorPaths["DISPLAY-1"]);
            Assert.Equal(Path.GetFullPath(second), platform.MonitorPaths["DISPLAY-2"]);
            var restored = await service.RestoreAsync(applied.SessionId);
            Assert.True(restored.Succeeded, restored.Message);
            Assert.Equal(Bmp(1, 2, 3), await File.ReadAllBytesAsync(platform.MonitorPaths["DISPLAY-1"]));
            Assert.Equal(Bmp(2, 3, 4), await File.ReadAllBytesAsync(platform.MonitorPaths["DISPLAY-2"]));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [WindowsFact]
    public async Task WallpaperRestoreBlocksExternalChangeOnUntargetedMonitor()
    {
        var root = Path.Combine(Path.GetTempPath(), $"Zeus.WallpaperTests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var first = Path.Combine(root, "first.bmp");
        var second = Path.Combine(root, "second.bmp");
        var selected = Path.Combine(root, "selected.bmp");
        var external = Path.Combine(root, "external.bmp");
        await File.WriteAllBytesAsync(first, Bmp(1, 2, 3));
        await File.WriteAllBytesAsync(second, Bmp(2, 3, 4));
        await File.WriteAllBytesAsync(selected, Bmp(4, 5, 6));
        await File.WriteAllBytesAsync(external, Bmp(6, 5, 4));
        var platform = new FixtureWallpaperPlatform(new Dictionary<string, string> { ["DISPLAY-1"] = first, ["DISPLAY-2"] = second });
        try
        {
            var service = new UserOptimizationService(Path.Combine(root, "history"), platform);
            var applied = await service.ApplyWallpaperAsync(selected, "DISPLAY-1");
            Assert.True(applied.Succeeded, applied.Message);
            platform.MonitorPaths["DISPLAY-2"] = Path.GetFullPath(external);
            var restored = await service.RestoreAsync(applied.SessionId);
            Assert.False(restored.Succeeded);
            Assert.Equal(Path.GetFullPath(selected), platform.MonitorPaths["DISPLAY-1"]);
            Assert.Equal(Path.GetFullPath(external), platform.MonitorPaths["DISPLAY-2"]);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [WindowsFact]
    public async Task WallpaperChangePreservesSlideshowAndDoesNotCreateSession()
    {
        var root = Path.Combine(Path.GetTempPath(), $"Zeus.WallpaperTests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var previous = Path.Combine(root, "previous.bmp");
        var selected = Path.Combine(root, "selected.bmp");
        await File.WriteAllBytesAsync(previous, Bmp(1, 2, 3));
        await File.WriteAllBytesAsync(selected, Bmp(4, 5, 6));
        var platform = new FixtureWallpaperPlatform(previous) { Slideshow = true };
        try
        {
            var service = new UserOptimizationService(Path.Combine(root, "history"), platform);
            var result = await service.ApplyWallpaperAsync(selected);
            Assert.False(result.Succeeded);
            Assert.Contains("slides", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(Path.GetFullPath(previous), platform.CurrentPath);
            Assert.Empty(await service.ListChangesAsync());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [WindowsFact]
    public async Task WallpaperRestoreBlocksWhenMonitorTopologyChanges()
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
            platform.MonitorPaths.Clear();
            platform.MonitorPaths["DISPLAY-2"] = selected;

            var restored = await service.RestoreAsync(applied.SessionId);

            Assert.False(restored.Succeeded);
            Assert.Contains("monitores conectados mudaram", restored.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(Path.GetFullPath(selected), platform.MonitorPaths["DISPLAY-2"]);
            Assert.Equal(UserChangeStatus.RestoreBlocked, Assert.Single(await service.ListChangesAsync()).Status);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [WindowsFact]
    public async Task WallpaperRestoreReadsLegacyUniformHistory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"Zeus.WallpaperTests.{Guid.NewGuid():N}");
        var history = Path.Combine(root, "history");
        Directory.CreateDirectory(history);
        var previous = Path.Combine(root, "previous.bmp");
        var selected = Path.Combine(root, "selected.bmp");
        await File.WriteAllBytesAsync(previous, Bmp(1, 2, 3));
        await File.WriteAllBytesAsync(selected, Bmp(4, 5, 6));
        var id = Guid.NewGuid();
        var backup = Path.Combine(history, $"{id:N}.wallpaper.bmp");
        File.Copy(previous, backup);
        var document = new
        {
            Version = 1,
            Scope = "CurrentUser",
            Id = id,
            CreatedAt = DateTimeOffset.UtcNow,
            Description = "Legacy uniform wallpaper change",
            Kind = "wallpaper",
            RegistryPath = (string?)null,
            Restored = false,
            Status = UserChangeStatus.Applied,
            Startup = (object?)null,
            Preferences = (object?)null,
            PreviousAnimation = (bool?)null,
            PreviousTransparency = (object?)null,
            PreviousPowerPlan = (Guid?)null,
            NewPowerPlan = (Guid?)null,
            PreviousWallpaperBackupPath = backup,
            PreviousWallpaperSha256 = Hash(previous),
            NewWallpaperSha256 = Hash(selected)
        };
        await File.WriteAllTextAsync(Path.Combine(history, $"{id:D}.json"), JsonSerializer.Serialize(document));
        var platform = new FixtureWallpaperPlatform(selected);
        try
        {
            var service = new UserOptimizationService(history, platform);

            var restored = await service.RestoreAsync(id);

            Assert.True(restored.Succeeded, restored.Message);
            Assert.Equal(Bmp(1, 2, 3), await File.ReadAllBytesAsync(platform.CurrentPath));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static byte[] Bmp(byte red, byte green, byte blue) =>
    [0x42, 0x4d, 0x3a, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x36, 0x00, 0x00, 0x00,
     0x28, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00,
     0x18, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x13, 0x0b, 0x00, 0x00,
     0x13, 0x0b, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
     blue, green, red, 0x00];

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private sealed class FixtureWallpaperPlatform : IWallpaperPlatform
    {
        public FixtureWallpaperPlatform(string initialPath) : this(new Dictionary<string, string> { ["DISPLAY-1"] = initialPath }) { }
        public FixtureWallpaperPlatform(Dictionary<string, string> initialPaths) => MonitorPaths = initialPaths.ToDictionary(pair => pair.Key, pair => Path.GetFullPath(pair.Value), StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> MonitorPaths { get; }
        public string CurrentPath { get => MonitorPaths["DISPLAY-1"]; set => MonitorPaths["DISPLAY-1"] = Path.GetFullPath(value); }
        public bool Slideshow { get; set; }
        public bool IsSlideshowConfigured() => Slideshow;
        public IReadOnlyList<WallpaperMonitorState> GetAttachedMonitorWallpapers() => MonitorPaths.Select(pair => new WallpaperMonitorState(pair.Key, pair.Value)).ToArray();
        public bool SetWallpaperPath(string monitorId, string path)
        {
            if (!MonitorPaths.ContainsKey(monitorId) || !File.Exists(path)) return false;
            MonitorPaths[monitorId] = Path.GetFullPath(path);
            return true;
        }
    }
}
