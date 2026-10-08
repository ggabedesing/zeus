using System.Text.Json;
using Zeus.Core;
using Zeus.Desktop;
using Zeus.Storage;

namespace Zeus.Windows.Acceptance.Tests;

public sealed class DesktopStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "zeus-desktop-storage-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task LegacyJsonHistoryAndPreferencesAreImportedWithoutRemovingSourceFiles()
    {
        Directory.CreateDirectory(_root);
        var report = new MaintenanceReport(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-2), DateTimeOffset.UtcNow,
            false, [new(MaintenanceActionId.VerifySystemFiles, StepOutcome.Succeeded, "Verificação concluída.")]);
        var historyPath = Path.Combine(_root, "history.json");
        var preferencesPath = Path.Combine(_root, "preferences.json");
        var historyJson = JsonSerializer.Serialize(new[] { report }, DesktopStorage.JsonOptions);
        var preferencesJson = """
            {"IsMinimal":true,"Theme":"Minimal","Profile":"Gaming","ReduceAnimations":true,"ReduceTransparency":false,"NeedsBluetooth":true,"NeedsPrinting":false,"NeedsCloudSync":true,"NeedsVirtualization":false}
            """;
        await File.WriteAllTextAsync(historyPath, historyJson);
        await File.WriteAllTextAsync(preferencesPath, preferencesJson);
        var storage = new DesktopStorage(_root);

        var migratedHistory = Assert.Single(await storage.ReadHistoryAsync());
        var migratedPreferences = await storage.ReadPreferencesAsync();
        await storage.AppendActivityAsync(new(DateTimeOffset.UtcNow, "application", "started", "info", "ZEUS iniciado."));
        var activity = Assert.Single(await storage.ReadRecentActivityAsync());
        var health = await storage.CheckHealthAsync();

        Assert.Equal(report.SessionId, migratedHistory.SessionId);
        Assert.Equal(MaintenanceActionId.VerifySystemFiles, migratedHistory.Steps[0].Action);
        Assert.True(migratedPreferences.IsMinimal);
        Assert.Equal(UsageProfile.Gaming, migratedPreferences.Profile);
        Assert.True(File.Exists(historyPath));
        Assert.True(File.Exists(preferencesPath));
        Assert.Equal(historyJson, await File.ReadAllTextAsync(historyPath));
        Assert.Equal("ZEUS iniciado.", activity.Summary);
        Assert.True(health.IsHealthy);
        Assert.Equal(1, health.MaintenanceSessionCount);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
