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
    public async Task IntegrityStatesAndLinkedVerificationPersistWithoutRequiringParentHistory()
    {
        var storage = new DesktopStorage(_root);
        var report = new MaintenanceReport(Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, false,
            [new(MaintenanceActionId.ScanWindowsImage, StepOutcome.Succeeded, "Scan", ImageHealthState: WindowsImageHealthState.NoCorruptionDetected),
             new(MaintenanceActionId.VerifySystemFiles, StepOutcome.Succeeded, "Verify", SystemFilesState: SfcVerificationState.IntegrityViolationsDetected)],
            VerificationOfSessionId: Guid.NewGuid());
        await storage.SaveHistoryAsync([report]);
        var restored = Assert.Single(await storage.ReadHistoryAsync());
        Assert.Equal(report.VerificationOfSessionId, restored.VerificationOfSessionId);
        Assert.Equal(WindowsImageHealthState.NoCorruptionDetected, restored.Steps[0].ImageHealthState);
        Assert.Equal(SfcVerificationState.Unknown, restored.Steps[0].SystemFilesState);
        Assert.Equal(SfcVerificationState.IntegrityViolationsDetected, restored.Steps[1].SystemFilesState);
        Assert.Equal(WindowsImageHealthState.Unknown, restored.Steps[1].ImageHealthState);
        Assert.Equal(6, (await storage.CheckHealthAsync()).SchemaVersion);
    }

    [Fact]
    public void HistoryRejectsInvalidIntegrityEnumsActionStatesAndVerificationLinks()
    {
        var report = new MaintenanceReport(Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, false,
            [new(MaintenanceActionId.ScanWindowsImage, StepOutcome.Succeeded, "Scan")]);
        Assert.Throws<InvalidDataException>(() => DesktopStorage.ValidateHistory([report with { VerificationOfSessionId = Guid.Empty }]));
        Assert.Throws<InvalidDataException>(() => DesktopStorage.ValidateHistory([report with { VerificationOfSessionId = report.SessionId }]));
        Assert.Throws<InvalidDataException>(() => DesktopStorage.ValidateHistory([report with { VerificationOfSessionId = Guid.NewGuid(), Steps = [] }]));
        Assert.Throws<InvalidDataException>(() => DesktopStorage.ValidateHistory([report with { VerificationOfSessionId = Guid.NewGuid(),
            Steps = [new(MaintenanceActionId.RepairWindowsImage, StepOutcome.Succeeded, "Repair")] }]));
        Assert.Throws<InvalidDataException>(() => DesktopStorage.ValidateHistory([report with {
            Steps = [report.Steps[0] with { ImageHealthState = (WindowsImageHealthState)999 }] }]));
        Assert.Throws<InvalidDataException>(() => DesktopStorage.ValidateHistory([report with {
            Steps = [report.Steps[0] with { SystemFilesState = (SfcVerificationState)999 }] }]));
        Assert.Throws<InvalidDataException>(() => DesktopStorage.ValidateHistory([report with {
            Steps = [new(MaintenanceActionId.RepairWindowsImage, StepOutcome.Succeeded, "Repair", ImageHealthState: WindowsImageHealthState.NoCorruptionDetected)] }]));
        Assert.Throws<InvalidDataException>(() => DesktopStorage.ValidateHistory([report with {
            Steps = [report.Steps[0] with { SystemFilesState = SfcVerificationState.NoIntegrityViolationsDetected }] }]));
    }

    [Fact]
    public async Task CorruptedStoredIntegrityEnumAndVerificationGuidAreRejectedOnRead()
    {
        var storage = new DesktopStorage(_root);
        var report = new MaintenanceReport(Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, false,
            [new(MaintenanceActionId.ScanWindowsImage, StepOutcome.Succeeded, "Scan")]);
        await storage.SaveHistoryAsync([report]);
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(_root, "zeus.db")};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE maintenance_steps SET image_health_state='999';";
        await command.ExecuteNonQueryAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => storage.ReadHistoryAsync());
        command.CommandText = "UPDATE maintenance_steps SET image_health_state='Unknown'; UPDATE maintenance_sessions SET verification_of_session_id='not-a-guid';";
        await command.ExecuteNonQueryAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => storage.ReadHistoryAsync());
    }

    [Fact]
    public async Task LightThemePreferencePersistsInSqlite()
    {
        var storage = new DesktopStorage(_root);
        const string customCatalog = "{\"schemaVersion\":1,\"presets\":[]}";
        await storage.SavePreferencesAsync(new DesktopPreferences(false, DesktopTheme.Light, AccentColor: AppAccentColor.Green,
            VisualLayoutPresetId: "aurora", CustomVisualLayoutsJson: customCatalog, ReduceZeusMotion: true,
            Density: DesktopDensity.Compact));

        var restored = await storage.ReadPreferencesAsync();

        Assert.Equal(DesktopTheme.Light, restored.Theme);
        Assert.Equal(AppAccentColor.Green, restored.AccentColor);
        Assert.Equal("aurora", restored.VisualLayoutPresetId);
        Assert.Equal(customCatalog, restored.CustomVisualLayoutsJson);
        Assert.True(restored.ReduceZeusMotion);
        Assert.Equal(DesktopDensity.Compact, restored.Density);
    }

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
            {"IsMinimal":true,"Theme":"Minimal","Profile":"Gaming","ReduceAnimations":true,"ReduceTransparency":false,"NeedsBluetooth":true,"NeedsPrinting":false,"NeedsCloudSync":true,"NeedsVirtualization":false,"Clock":{"Enabled":false,"ShowDate":true,"ShowSeconds":false,"AlwaysOnTop":false,"Opacity":0.88,"Left":40,"Top":80}}
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
        Assert.Equal(WindowsImageHealthState.Unknown, migratedHistory.Steps[0].ImageHealthState);
        Assert.Equal(SfcVerificationState.Unknown, migratedHistory.Steps[0].SystemFilesState);
        Assert.Null(migratedHistory.VerificationOfSessionId);
        Assert.True(migratedPreferences.IsMinimal);
        Assert.Equal(DesktopDensity.Comfortable, migratedPreferences.Density);
        Assert.False(migratedPreferences.ReduceZeusMotion, "Older preference JSON keeps the existing motion behavior by default.");
        Assert.False(migratedPreferences.IsTechnicalMode, "Older preferences must retain the default non-technical mode when the new field is absent.");
        Assert.Equal(AppAccentColor.ThemeDefault, migratedPreferences.AccentColor);
        Assert.Null(migratedPreferences.VisualLayoutPresetId);
        Assert.True(migratedPreferences.Clock!.Use24HourFormat, "Older clock preferences without a format field must default to 24-hour time.");
        Assert.True(migratedPreferences.Clock.HideDuringFullscreen, "Older clock preferences default to hiding the widget in fullscreen applications.");
        Assert.Equal(DesktopClockStyle.Glass, migratedPreferences.Clock.Style);
        Assert.Null(migratedPreferences.Clock.Size);
        Assert.Equal(DesktopClockSize.Medium, MainWindow.ResolveClockSize(migratedPreferences.Clock.Size));
        Assert.Equal(DesktopClockSize.Medium, MainWindow.ResolveClockSize((DesktopClockSize)999));
        Assert.Equal(DesktopClockStyle.Glass, MainWindow.ResolveClockStyle((DesktopClockStyle)999));
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
        await storage.StartPerformanceSessionAsync(id, "Referência de desempenho · jogo + OBS", observation.CollectedAt.AddSeconds(-2));
        await storage.AppendPerformanceObservationAsync(id, 0, observation);
        await storage.MarkPerformanceReferenceAsync(id);
        await storage.FinishPerformanceSessionAsync(id, observation.CollectedAt);

        var session = Assert.Single(await storage.ReadPerformanceSessionsAsync());
        Assert.Equal("Referência de desempenho · jogo + OBS", session.Label);
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
    public void ReportExportRetainsPerProcessGpuCapacityShareComparisonAndCoverage()
    {
        var process = new PerformanceGpuProcessMemoryComparison(42, "game", 123,
            "luid_adapter", 2_000, 4_000, 3, 3, 25, 50, 2, 3);
        var comparison = new PerformanceComparison(3, 3, null, null, null, null,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(1),
            GpuProcessMemoryUsage: [process]);
        var report = new ExportDocument(8, DateTimeOffset.UnixEpoch, null, [], PerformanceComparison: comparison);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(report, DesktopStorage.JsonOptions));
        var exported = json.RootElement.GetProperty("PerformanceComparison")
            .GetProperty("GpuProcessMemoryUsage").EnumerateArray().Single();

        Assert.Equal(25, exported.GetProperty("ReferenceCapacitySharePercent").GetDouble());
        Assert.Equal(50, exported.GetProperty("LaterCapacitySharePercent").GetDouble());
        Assert.Equal(2, exported.GetProperty("ReferenceCapacityShareSamples").GetInt32());
        Assert.Equal(3, exported.GetProperty("LaterCapacityShareSamples").GetInt32());
    }

    [Fact]
    public void ReportExportRetainsPerProcessEquivalentCoresAndCoverage()
    {
        var process = new PerformanceProcessComparison(42, "game", 123,
            2, 3, 2, 3, 2_000, 3_000, 2, 3,
            ReferenceCpuCoresUsed: 0.5, LaterCpuCoresUsed: 1.25,
            ReferenceCpuCoresSamples: 2, LaterCpuCoresSamples: 3);
        var comparison = new PerformanceComparison(3, 3, null, null, null, null,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(1),
            ProcessUsage: [process]);
        var report = new ExportDocument(8, DateTimeOffset.UnixEpoch, null, [], PerformanceComparison: comparison);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(report, DesktopStorage.JsonOptions));
        var exported = json.RootElement.GetProperty("PerformanceComparison")
            .GetProperty("ProcessUsage").EnumerateArray().Single();

        Assert.Equal(0.5, exported.GetProperty("ReferenceCpuCoresUsed").GetDouble());
        Assert.Equal(1.25, exported.GetProperty("LaterCpuCoresUsed").GetDouble());
        Assert.Equal(2, exported.GetProperty("ReferenceCpuCoresSamples").GetInt32());
        Assert.Equal(3, exported.GetProperty("LaterCpuCoresSamples").GetInt32());
    }

    [Fact]
    public async Task LargePerformanceObservationIsSummarizedBeforeTheSqliteSampleLimit()
    {
        Directory.CreateDirectory(_root);
        var storage = new DesktopStorage(_root);
        var id = Guid.NewGuid();
        var collectedAt = DateTimeOffset.UtcNow;
        var gpuRows = Enumerable.Range(1, 512).Select(processId => new GpuProcessMemoryObservation(
            $"pid_{processId}_luid_0x00000001_0x00000002_phys_0",
            "luid_0x00000001_0x00000002_phys_0", processId, "graphics-process-" + new string('x', 120),
            processId, (ulong)processId * 1024, 4096, 2048, 3072, 8192)).ToArray();
        var observation = new PerformanceObservation(collectedAt, TimeSpan.FromSeconds(5), 12,
            32_000, 16_000, [], ["aviso original"], GpuProcessMemory: gpuRows);
        await storage.StartPerformanceSessionAsync(id, "Medição manual · carga de GPU", collectedAt);

        await storage.AppendPerformanceObservationAsync(id, 0, observation);

        var stored = Assert.Single(await storage.ReadPerformanceSessionsAsync());
        var sample = Assert.Single(stored.Samples);
        Assert.True(sample.DetailsJson.Length <= 65_536);
        var restored = Assert.IsType<PerformanceObservation>(JsonSerializer.Deserialize<PerformanceObservation>(sample.DetailsJson, DesktopStorage.JsonOptions));
        Assert.Equal(12, restored.GpuProcessMemory!.Count);
        Assert.Contains("aviso original", restored.Warnings);
        Assert.Contains(restored.Warnings, warning => warning.Contains("processos com memória GPU", StringComparison.Ordinal));
        Assert.Contains(restored.Warnings, warning => warning.Contains("amostra ao vivo não foi reduzida", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProcessIoRoundTripsThroughBoundedSqliteAndSchemaTenExport()
    {
        var storage = new DesktopStorage(_root);
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var processes = Enumerable.Range(1, 40).Select(pid => new ProcessObservation(pid, "io fixture", 1, 1024, pid,
            IoReadBytesPerSecond: pid * 1000, IoWriteBytesPerSecond: 0, IoOtherBytesPerSecond: null, IoSamplingDurationSeconds: 2)).ToArray();
        var observation = new PerformanceObservation(now, TimeSpan.FromSeconds(2), 1, 1024, 512, [], [], IoProcesses: processes);
        await storage.StartPerformanceSessionAsync(id, "I/O fixture", now);
        await storage.AppendPerformanceObservationAsync(id, 0, observation);
        var saved = Assert.Single(Assert.Single(await storage.ReadPerformanceSessionsAsync()).Samples);
        var restored = JsonSerializer.Deserialize<PerformanceObservation>(saved.DetailsJson, DesktopStorage.JsonOptions)!;
        Assert.Equal(30, restored.IoProcesses!.Count);
        Assert.Contains(restored.Warnings, warning => warning.Contains("processos com I/O", StringComparison.Ordinal));
        Assert.Equal(1000, restored.IoProcesses[0].IoReadBytesPerSecond);
        Assert.Equal(0, restored.IoProcesses[0].IoWriteBytesPerSecond);
        Assert.Null(restored.IoProcesses[0].IoOtherBytesPerSecond);
        Assert.Equal(2, restored.IoProcesses[0].IoSamplingDurationSeconds);
        var comparison = PerformanceComparisonBuilder.Compare([restored], [restored]);
        var path = Path.Combine(_root, "process-io-export.json");
        await DesktopStorage.ExportAsync(path, new ExportDocument(10, now, null, [], Performance: restored, PerformanceComparison: comparison));
        using var exported = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.Equal(10, exported.RootElement.GetProperty("SchemaVersion").GetInt32());
        var io = exported.RootElement.GetProperty("Performance").GetProperty("IoProcesses")[0];
        Assert.Equal(1000, io.GetProperty("IoReadBytesPerSecond").GetDouble());
        var compared = exported.RootElement.GetProperty("PerformanceComparison").GetProperty("ProcessUsage")[0];
        Assert.Equal(1, compared.GetProperty("ReferenceIoReadSamples").GetInt32());
        Assert.Equal(1, compared.GetProperty("LaterIoWriteSamples").GetInt32());
        Assert.Equal(0, compared.GetProperty("LaterIoOtherSamples").GetInt32());
    }

    [Fact]
    public async Task MaintenanceVerificationStatePersistsAndAppearsInHistory()
    {
        var storage = new DesktopStorage(_root);
        var report = new MaintenanceReport(Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, true,
            [new(MaintenanceActionId.InstallDriverUpdate, StepOutcome.Succeeded, "Provedor confirmou instalação.",
                TargetId: "12345678-1234-1234-1234-123456789abc:1", Verification: MaintenanceVerificationStatus.ProviderConfirmed,
                UpdateServerSelection: 3, UpdateServiceId: WindowsUpdateSourcePolicy.MicrosoftUpdateServiceId)],
            RestorePointSequenceNumber: 1234);

        await storage.SaveHistoryAsync([report]);
        var restored = Assert.Single(await storage.ReadHistoryAsync());
        var row = HistoryRow.From(restored);

        Assert.Equal(MaintenanceVerificationStatus.ProviderConfirmed, Assert.Single(restored.Steps).Verification);
        Assert.Equal(3, Assert.Single(restored.Steps).UpdateServerSelection);
        Assert.Equal(WindowsUpdateSourcePolicy.MicrosoftUpdateServiceId, Assert.Single(restored.Steps).UpdateServiceId);
        Assert.Contains("resultado confirmado pelo provedor", Assert.Single(row.Steps));
        Assert.Equal(1234, restored.RestorePointSequenceNumber);
        Assert.Contains("#1234", row.Protection, StringComparison.Ordinal);

        var invalid = report with
        {
            SessionId = Guid.NewGuid(),
            Steps = [report.Steps[0] with { UpdateServiceId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa" }]
        };
        Assert.Throws<InvalidDataException>(() => DesktopStorage.ValidateHistory([invalid]));
        Assert.Throws<InvalidDataException>(() => DesktopStorage.ValidateHistory([report with { SessionId = Guid.NewGuid(), RestorePointSequenceNumber = 0 }]));
        Assert.Throws<InvalidDataException>(() => DesktopStorage.ValidateHistory([report with { SessionId = Guid.NewGuid(), RestorePointConfirmed = false }]));
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
    public async Task ReportSchemaEightExportsInventoryAndPerformanceSessionMetadata()
    {
        var inventory = new WindowsInventoryInfo([], [new("Display", "Fixture", "1.2.3", "2025-01-02", "Fixture Signer", false, "Dell Inc.")],
            [new("Display", "Display", "OK", null, "USB\\VID_1234&PID_5678\\A1", true)], [], [], [],
            [new("Store Fixture", "2.0", "Fixture Publisher", "Pacote Appx/MSIX do usuário")], [], null, null, null, []);
        var snapshot = new HardwareSnapshot(DateTimeOffset.UtcNow, "Windows fixture", "fixture", null, null, [], [], [], null, [],
            WindowsInventory: inventory);
        var path = Path.Combine(_root, "export.json");
        var started = DateTimeOffset.UtcNow.AddMinutes(-2);
        await DesktopStorage.ExportAsync(path, new ExportDocument(8, DateTimeOffset.UtcNow, snapshot, [],
            PerformanceSessions: [new("Jogo teste + OBS", started, started.AddMinutes(1), true, 6)]));

        using var export = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var diagnostics = export.RootElement.GetProperty("Diagnostics").GetProperty("WindowsInventory");
        Assert.Equal(8, export.RootElement.GetProperty("SchemaVersion").GetInt32());
        Assert.False(diagnostics.GetProperty("Drivers")[0].GetProperty("IsSigned").GetBoolean());
        Assert.Equal("Dell Inc.", diagnostics.GetProperty("Drivers")[0].GetProperty("Manufacturer").GetString());
        Assert.True(diagnostics.GetProperty("PnpDevices")[0].GetProperty("IsPresent").GetBoolean());
        var installedSoftware = Assert.Single(diagnostics.GetProperty("InstalledSoftware").EnumerateArray());
        Assert.Equal("Store Fixture", installedSoftware.GetProperty("Name").GetString());
        Assert.Equal("Pacote Appx/MSIX do usuário", installedSoftware.GetProperty("Source").GetString());
        var session = Assert.Single(export.RootElement.GetProperty("PerformanceSessions").EnumerateArray());
        Assert.Equal("Jogo teste + OBS", session.GetProperty("Label").GetString());
        Assert.Equal(started, session.GetProperty("StartedAt").GetDateTimeOffset());
        Assert.True(session.GetProperty("IsReference").GetBoolean());
        Assert.Equal(6, session.GetProperty("SampleCount").GetInt32());
    }

    [Fact]
    public async Task SchemaNinePreservesEventGuidanceAndSourceWithoutRawMessages()
    {
        var report = EventPatternAnalyzer.Analyze([
            new(DateTimeOffset.UnixEpoch, "System", "Microsoft-Windows-Kernel-Power", 41, "Critical", "private raw message")],
            sourcesComplete: false);
        var path = Path.Combine(_root, "events-export.json");
        await DesktopStorage.ExportAsync(path, new ExportDocument(9, DateTimeOffset.UtcNow, null, [], EventDiagnostics: report));
        var text = await File.ReadAllTextAsync(path);
        using var export = JsonDocument.Parse(text);
        Assert.Equal(9, export.RootElement.GetProperty("SchemaVersion").GetInt32());
        var events = export.RootElement.GetProperty("EventDiagnostics");
        Assert.Contains("amostra está incompleta", events.GetProperty("Summary").GetString());
        var finding = Assert.Single(events.GetProperty("Findings").EnumerateArray());
        Assert.Contains("event-id-41-restart", finding.GetProperty("Detail").GetString());
        Assert.DoesNotContain("private raw message", text);
    }

    [Fact]
    public async Task DiagnosticPackageContainsReviewInstructionsAndVerifiableReportHash()
    {
        Directory.CreateDirectory(_root);
        var report = new ExportDocument(8, DateTimeOffset.UtcNow, null, []);
        var path = Path.Combine(_root, "zeus-diagnostic.zip");

        await DesktopStorage.ExportDiagnosticPackageAsync(path, report, "1.2.3+fixture");

        using var archive = System.IO.Compression.ZipFile.OpenRead(path);
        Assert.Equal(new[] { "LEIA-ANTES.txt", "manifesto.json", "relatorio.json" }, archive.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal));
        var reportEntry = Assert.Single(archive.Entries, entry => entry.FullName == "relatorio.json");
        byte[] reportBytes;
        await using (var reportStream = reportEntry.Open())
        await using (var copy = new MemoryStream())
        {
            await reportStream.CopyToAsync(copy);
            reportBytes = copy.ToArray();
        }
        using var manifestStream = Assert.Single(archive.Entries, entry => entry.FullName == "manifesto.json").Open();
        using var manifest = await JsonDocument.ParseAsync(manifestStream);
        Assert.Equal(1, manifest.RootElement.GetProperty("FormatVersion").GetInt32());
        Assert.Equal("1.2.3+fixture", manifest.RootElement.GetProperty("ApplicationVersion").GetString());
        Assert.Equal(8, manifest.RootElement.GetProperty("ReportSchemaVersion").GetInt32());
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(reportBytes)).ToLowerInvariant(),
            manifest.RootElement.GetProperty("ReportSha256").GetString());
        using var readmeStream = Assert.Single(archive.Entries, entry => entry.FullName == "LEIA-ANTES.txt").Open();
        using var reader = new StreamReader(readmeStream);
        var readme = await reader.ReadToEndAsync();
        Assert.Contains("o ZEUS não o envia", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("não autentica", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFiles(_root, ".zeus-*.tmp"));

        var existingPath = Path.Combine(_root, "existing.zip");
        await File.WriteAllTextAsync(existingPath, "arquivo anterior preservado");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DesktopStorage.ExportDiagnosticPackageAsync(existingPath, report, "1.2.3+fixture", cancellation.Token));
        Assert.Equal("arquivo anterior preservado", await File.ReadAllTextAsync(existingPath));
        Assert.Empty(Directory.EnumerateFiles(_root, ".zeus-*.tmp"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
