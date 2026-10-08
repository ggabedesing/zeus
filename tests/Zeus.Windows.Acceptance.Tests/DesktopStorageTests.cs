using System.Text.Json;
using Zeus.Core;
using Zeus.Desktop;
using Zeus.Storage;
using Zeus.Windows;

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
        Assert.Equal(MaintenanceVerificationStatus.NotRecorded, migratedHistory.Steps[0].Verification);
        Assert.True(migratedPreferences.IsMinimal);
        Assert.Equal(UsageProfile.Gaming, migratedPreferences.Profile);
        Assert.True(migratedPreferences.FirstRunSetupComplete);
        Assert.True(File.Exists(historyPath));
        Assert.True(File.Exists(preferencesPath));
        Assert.Equal(historyJson, await File.ReadAllTextAsync(historyPath));
        Assert.Equal("ZEUS iniciado.", activity.Summary);
        Assert.True(health.IsHealthy);
        Assert.Equal(1, health.MaintenanceSessionCount);
    }

    [Fact]
    public async Task PerformanceObservationAndBaselinePersistAndReloadFromSqlite()
    {
        Directory.CreateDirectory(_root);
        var storage = new DesktopStorage(_root);
        var id = Guid.NewGuid();
        var observation = new PerformanceObservation(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2), 21.5,
            16_000, 8_000,
            [new(42, "game.exe", 12.5, 2_000)], ["source warning"],
            [new("pid_42_engtype_3D", 42, "3D", 31.0)],
            [new("0 C:", 1200, 4, 1.2)],
            [new("Ethernet", 800, 1_000_000_000, 0, 3)],
            GpuMemory: [new("luid_0x1_phys_0", 1_024, 2_048, 3_072, 4_096)]);
        await storage.StartPerformanceSessionAsync(id, "Referência", observation.CollectedAt.AddSeconds(-2));
        await storage.AppendPerformanceObservationAsync(id, 0, observation);
        await storage.MarkPerformanceReferenceAsync(id);
        await storage.FinishPerformanceSessionAsync(id, observation.CollectedAt);

        var session = Assert.Single(await storage.ReadPerformanceSessionsAsync());
        var sample = Assert.Single(session.Samples);
        var restored = Assert.IsType<PerformanceObservation>(JsonSerializer.Deserialize<PerformanceObservation>(sample.DetailsJson, DesktopStorage.JsonOptions));
        Assert.True(session.IsReference);
        Assert.Equal("game.exe", Assert.Single(restored.Processes).Name);
        Assert.Equal(31.0, Assert.Single(restored.GpuEngines!).UtilizationPercent);
        Assert.Equal("Ethernet", Assert.Single(restored.Networks!).Adapter);
        Assert.Equal(1_024UL, Assert.Single(restored.GpuMemory!).DedicatedUsageBytes);
        Assert.Equal(4_096UL, Assert.Single(restored.GpuMemory!).DedicatedCapacityBytes);
        Assert.Equal(21.5, sample.CpuPercent!.Value);
    }

    [Fact]
    public async Task MaintenanceVerificationStatePersistsAndAppearsInHistory()
    {
        var storage = new DesktopStorage(_root);
        var report = new MaintenanceReport(Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, false,
            [new(MaintenanceActionId.InstallDriverUpdate, StepOutcome.Succeeded, "Provedor confirmou instalação.",
                TargetId: "12345678-1234-1234-1234-123456789abc:1", Verification: MaintenanceVerificationStatus.ProviderConfirmed,
                UpdateServerSelection: 3, UpdateServiceId: WindowsUpdateSourcePolicy.MicrosoftUpdateServiceId)]);

        await storage.SaveHistoryAsync([report]);
        var restored = Assert.Single(await storage.ReadHistoryAsync());
        var row = HistoryRow.From(restored);

        Assert.Equal(MaintenanceVerificationStatus.ProviderConfirmed, Assert.Single(restored.Steps).Verification);
        Assert.Equal(3, Assert.Single(restored.Steps).UpdateServerSelection);
        Assert.Equal(WindowsUpdateSourcePolicy.MicrosoftUpdateServiceId, Assert.Single(restored.Steps).UpdateServiceId);
        Assert.Contains("resultado confirmado pelo provedor", Assert.Single(row.Steps));

        var invalid = report with
        {
            SessionId = Guid.NewGuid(),
            Steps = [report.Steps[0] with { UpdateServiceId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa" }]
        };
        Assert.Throws<InvalidDataException>(() => DesktopStorage.ValidateHistory([invalid]));
    }

    [Fact]
    public void PendingDriverVerificationIsNotPresentedAsAnOngoingCommand()
    {
        var report = new MaintenanceReport(Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, false,
            [new(MaintenanceActionId.InstallDriverUpdate, StepOutcome.Succeeded,
                "Pacote aguardando conferência após reinício.", Verification: MaintenanceVerificationStatus.Pending)]);

        var row = HistoryRow.From(report);

        Assert.Contains("verificação pendente", Assert.Single(row.Steps));
        Assert.DoesNotContain("em andamento", Assert.Single(row.Steps), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PendingDriverVerificationDoesNotCompleteMaintenanceSession()
    {
        var request = new MaintenanceRequest(MaintenanceActionId.InstallDriverUpdate,
            "12345678-1234-1234-1234-123456789abc:1", false, 2);
        var report = new MaintenanceReport(Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, true,
            [new(MaintenanceActionId.InstallDriverUpdate, StepOutcome.Succeeded,
                "Pacote aguardando conferência após reinício.", TargetId: request.TargetId,
                Verification: MaintenanceVerificationStatus.Pending)]);

        var result = MaintenanceResultPresentation.From(report, [request]);

        Assert.False(result.IsSuccessful);
        Assert.Equal("Sessão encerrada com verificação pendente", result.Title);
        Assert.Contains("antes de repetir", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DriverRollbackTargetIsPersistedAndMalformedTargetIsRejected()
    {
        var storage = new DesktopStorage(_root);
        var report = new MaintenanceReport(Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, false,
            [new(MaintenanceActionId.RollbackDriver, StepOutcome.Failed, "Sem driver anterior disponível.",
                TargetId: "USB\\VID_1234&PID_5678\\A1", Verification: MaintenanceVerificationStatus.ManualReviewRequired)]);
        await storage.SaveHistoryAsync([report]);
        Assert.Equal("USB\\VID_1234&PID_5678\\A1", Assert.Single(Assert.Single(await storage.ReadHistoryAsync()).Steps).TargetId);

        var invalid = report with { SessionId = Guid.NewGuid(), Steps = [report.Steps[0] with { TargetId = "not-a-pnp-id" }] };
        await Assert.ThrowsAsync<InvalidDataException>(() => storage.SaveHistoryAsync([invalid]));
    }

    [Fact]
    public async Task ReportSchemaSevenExportsManufacturerSignatureAndPnpPresence()
    {
        var inventory = new WindowsInventoryInfo([], [new("Display", "Fixture", "1.2.3", "2025-01-02", "Fixture Signer", false, "Dell Inc.")],
            [new("Display", "Display", "OK", null, "USB\\VID_1234&PID_5678\\A1", true)], [], [], [], [], [], null, null, null, []);
        var snapshot = new HardwareSnapshot(DateTimeOffset.UtcNow, "Windows fixture", "fixture", null, null, [], [], [], null, [],
            WindowsInventory: inventory);
        var path = Path.Combine(_root, "export.json");
        await DesktopStorage.ExportAsync(path, new ExportDocument(7, DateTimeOffset.UtcNow, snapshot, []));

        using var export = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var diagnostics = export.RootElement.GetProperty("Diagnostics").GetProperty("WindowsInventory");
        Assert.Equal(7, export.RootElement.GetProperty("SchemaVersion").GetInt32());
        Assert.False(diagnostics.GetProperty("Drivers")[0].GetProperty("IsSigned").GetBoolean());
        Assert.Equal("Dell Inc.", diagnostics.GetProperty("Drivers")[0].GetProperty("Manufacturer").GetString());
        Assert.True(diagnostics.GetProperty("PnpDevices")[0].GetProperty("IsPresent").GetBoolean());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
