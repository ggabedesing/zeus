using Zeus.Storage;

namespace Zeus.Storage.Tests;

public sealed class ZeusDatabaseTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "zeus-storage-tests-" + Guid.NewGuid().ToString("N"));
    private ZeusDatabase CreateDatabase() => new(Path.Combine(_root, "zeus.db"));

    [Fact]
    public async Task FreshDatabaseHasVersionedSchemaAndHealthyIntegrity()
    {
        var database = CreateDatabase();

        var health = await database.CheckHealthAsync();

        Assert.True(health.IsHealthy);
        Assert.Equal(ZeusDatabase.CurrentSchemaVersion, health.SchemaVersion);
        Assert.Equal("ok", health.IntegrityCheck);
        Assert.False(string.IsNullOrWhiteSpace(health.SqliteVersion));
        Assert.True(health.FileSizeBytes > 0);
    }

    [Fact]
    public async Task OnlineBackupPreservesSettingsActivityAndSchemaAndCanReplaceAnOlderBackup()
    {
        var database = CreateDatabase();
        await database.WriteSettingAsync("preferences", "{\"theme\":\"Aurora\"}");
        await database.AppendActivityAsync(new(DateTimeOffset.UtcNow, "diagnostics", "completed", "info", "Backup test"));
        var destination = Path.Combine(_root, "backup", "zeus.db");
        var olderBackup = new ZeusDatabase(destination);
        await olderBackup.WriteSettingAsync("preferences", "{\"theme\":\"Old\"}");
        SqliteConnectionClear();

        await database.BackupToAsync(destination);

        var restored = new ZeusDatabase(destination);
        Assert.Equal("{\"theme\":\"Aurora\"}", await restored.ReadSettingAsync("preferences"));
        Assert.Equal("Backup test", Assert.Single(await restored.ReadRecentActivityAsync()).Summary);
        var health = await restored.CheckHealthAsync();
        Assert.True(health.IsHealthy);
        Assert.Equal(ZeusDatabase.CurrentSchemaVersion, health.SchemaVersion);
    }

    [Fact]
    public async Task OnlineBackupRejectsTheLiveDatabaseAsItsOwnDestination()
    {
        var database = CreateDatabase();
        await database.InitializeAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => database.BackupToAsync(database.PathName));
        Assert.True((await database.CheckHealthAsync()).IsHealthy);
    }

    [Fact]
    public async Task SettingsUpsertAndRoundTripWithoutLosingUnknownFields()
    {
        var database = CreateDatabase();
        await database.WriteSettingAsync("preferences", "{\"theme\":\"Aurora\",\"futureOption\":true}");
        await database.WriteSettingAsync("preferences", "{\"theme\":\"Minimal\",\"futureOption\":true}");

        Assert.Equal("{\"theme\":\"Minimal\",\"futureOption\":true}", await database.ReadSettingAsync("preferences"));
    }

    [Fact]
    public async Task SettingsRejectMalformedJson()
    {
        var database = CreateDatabase();

        await Assert.ThrowsAsync<ArgumentException>(() => database.WriteSettingAsync("preferences", "{not-json}"));
    }

    [Fact]
    public async Task ActivityIsStructuredAndReadNewestFirst()
    {
        var database = CreateDatabase();
        var earlier = new ActivityEntry(DateTimeOffset.Parse("2026-10-08T12:00:00Z"), "diagnostics", "completed", "info", "Inventário lido");
        var later = new ActivityEntry(DateTimeOffset.Parse("2026-10-08T12:01:00Z"), "maintenance", "failed", "warning", "Ação não concluída", "{\"reason\":\"cancelled\"}", "session-1");

        await database.AppendActivityAsync(earlier);
        await database.AppendActivityAsync(later);
        var entries = await database.ReadRecentActivityAsync();

        Assert.Equal(2, entries.Count);
        Assert.Equal("maintenance", entries[0].Category);
        Assert.Equal("{\"reason\":\"cancelled\"}", entries[0].DetailsJson);
        Assert.Equal("session-1", entries[0].CorrelationId);
        var health = await database.CheckHealthAsync();
        Assert.Equal(2, health.ActivityCount);
    }

    [Fact]
    public async Task ActivityRejectsMalformedDetailsJson()
    {
        var database = CreateDatabase();
        var entry = new ActivityEntry(DateTimeOffset.UtcNow, "application", "failed", "warning", "Falha", "{not-json}");

        await Assert.ThrowsAsync<ArgumentException>(() => database.AppendActivityAsync(entry));
    }

    [Fact]
    public async Task MaintenanceHistoryUsesNormalizedSessionsAndSteps()
    {
        var database = CreateDatabase();
        var id = Guid.NewGuid().ToString("D");
        var session = new StoredMaintenanceSession(id, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow, true, false,
            "A conclusão não foi confirmada", [new(0, "InstallDriverUpdate", "Succeeded", "Windows Update confirmou a identidade", "log.txt", "12345678-1234-1234-1234-123456789abc:1", "ProviderConfirmed")]);

        await database.SaveMaintenanceHistoryAsync([session]);
        var restored = Assert.Single(await database.ReadMaintenanceHistoryAsync());

        Assert.Equal(id, restored.SessionId);
        Assert.True(restored.RestorePointConfirmed);
        Assert.False(restored.IsComplete);
        Assert.Equal("InstallDriverUpdate", Assert.Single(restored.Steps).Action);
        Assert.Equal("log.txt", restored.Steps[0].LogFile);
        Assert.Equal("ProviderConfirmed", restored.Steps[0].Verification);
        Assert.Equal(1, (await database.CheckHealthAsync()).MaintenanceSessionCount);
    }

    [Fact]
    public async Task PerformanceSessionsSamplesAndReferenceRoundTripInStructuredTables()
    {
        var database = CreateDatabase();
        var id = Guid.NewGuid().ToString("D");
        var started = DateTimeOffset.UtcNow;
        await database.StartPerformanceSessionAsync(id, "Medição de teste", started);
        await database.AppendPerformanceSampleAsync(id, new(0, started.AddSeconds(2), 2000, 42.5, 4096, 2048, "{\"gpu\":[]}"));
        await database.MarkPerformanceReferenceAsync(id);
        await database.FinishPerformanceSessionAsync(id, started.AddSeconds(3));

        var session = Assert.Single(await database.ReadPerformanceSessionsAsync());
        Assert.Equal(id, session.SessionId);
        Assert.Equal("Medição de teste", session.Label);
        Assert.True(session.IsReference);
        Assert.Equal(started.AddSeconds(3), session.FinishedAt);
        var sample = Assert.Single(session.Samples);
        Assert.Equal(0, sample.Sequence);
        Assert.Equal(42.5, sample.CpuPercent);
        Assert.Equal("{\"gpu\":[]}", sample.DetailsJson);
        var health = await database.CheckHealthAsync();
        Assert.Equal(3, health.SchemaVersion);
        Assert.Equal(1, health.PerformanceSessionCount);
        Assert.Equal(1, health.PerformanceSampleCount);
    }

    [Fact]
    public async Task SchemaV1UpgradesTransactionallyAndPreservesExistingSettings()
    {
        var path = Path.Combine(_root, "zeus.db");
        var initial = new ZeusDatabase(path);
        await initial.WriteSettingAsync("preferences", "{\"theme\":\"Aurora\"}");
        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE performance_samples; DROP TABLE performance_sessions; DROP TABLE maintenance_steps; CREATE TABLE maintenance_steps(session_id TEXT NOT NULL REFERENCES maintenance_sessions(session_id) ON DELETE CASCADE,sequence INTEGER NOT NULL CHECK(sequence >= 0),action TEXT NOT NULL,outcome TEXT NOT NULL,message TEXT NOT NULL,log_file TEXT NULL,target_id TEXT NULL,PRIMARY KEY(session_id,sequence)); DELETE FROM schema_migrations WHERE version IN (2,3); PRAGMA user_version=1;";
            await command.ExecuteNonQueryAsync();
        }

        var upgraded = new ZeusDatabase(path);
        var health = await upgraded.CheckHealthAsync();

        Assert.True(health.IsHealthy);
        Assert.Equal(3, health.SchemaVersion);
        Assert.Equal("{\"theme\":\"Aurora\"}", await upgraded.ReadSettingAsync("preferences"));
        Assert.Empty(await upgraded.ReadPerformanceSessionsAsync());
    }

    [Fact]
    public async Task SchemaV2AddsVerificationStatusWithoutChangingHistoricalSteps()
    {
        var path = Path.Combine(_root, "zeus.db");
        var initial = new ZeusDatabase(path);
        var sessionId = Guid.NewGuid().ToString("D");
        await initial.SaveMaintenanceHistoryAsync([new(sessionId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            false, true, null, [new(0, "VerifySystemFiles", "Succeeded", "Verificação concluída", null, null, "CommandCompleted")])]);
        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE maintenance_steps_v2(session_id TEXT NOT NULL REFERENCES maintenance_sessions(session_id) ON DELETE CASCADE,sequence INTEGER NOT NULL CHECK(sequence >= 0),action TEXT NOT NULL,outcome TEXT NOT NULL,message TEXT NOT NULL,log_file TEXT NULL,target_id TEXT NULL,PRIMARY KEY(session_id,sequence)); INSERT INTO maintenance_steps_v2 SELECT session_id,sequence,action,outcome,message,log_file,target_id FROM maintenance_steps; DROP TABLE maintenance_steps; ALTER TABLE maintenance_steps_v2 RENAME TO maintenance_steps; DELETE FROM schema_migrations WHERE version=3; PRAGMA user_version=2;";
            await command.ExecuteNonQueryAsync();
            await using var verify = connection.CreateCommand();
            verify.CommandText = "SELECT COUNT(*) FROM pragma_table_info('maintenance_steps') WHERE name='verification_status';";
            Assert.Equal(0L, (long)(await verify.ExecuteScalarAsync())!);
            verify.CommandText = "PRAGMA user_version;";
            Assert.Equal(2L, (long)(await verify.ExecuteScalarAsync())!);
        }

        var upgraded = new ZeusDatabase(path);
        var health = await upgraded.CheckHealthAsync();
        var migrated = Assert.Single(await upgraded.ReadMaintenanceHistoryAsync());

        Assert.True(health.IsHealthy);
        Assert.Equal(3, health.SchemaVersion);
        Assert.Equal("NotRecorded", Assert.Single(migrated.Steps).Verification);
        Assert.Equal("Verificação concluída", migrated.Steps[0].Message);
    }

    [Fact]
    public async Task LegacyHistoryImportIsIdempotentAndDoesNotOverwriteNewerDatabaseRows()
    {
        var database = CreateDatabase();
        var id = Guid.NewGuid().ToString("D");
        var current = new StoredMaintenanceSession(id, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, false, true, "new", []);
        var legacy = current with { Error = "legacy" };
        await database.SaveMaintenanceHistoryAsync([current]);

        await database.ImportLegacyHistoryOnceAsync([legacy], "history.json.v1");
        await database.ImportLegacyHistoryOnceAsync([legacy], "history.json.v1");

        var restored = Assert.Single(await database.ReadMaintenanceHistoryAsync());
        Assert.Equal("new", restored.Error);
    }

    [Fact]
    public async Task LegacySettingsImportIsRecordedAndDoesNotReplaceExistingPreference()
    {
        var database = CreateDatabase();
        await database.WriteSettingAsync("preferences", "{\"theme\":\"Mínimo\"}");

        await database.ImportLegacySettingOnceAsync("preferences", "{\"theme\":\"Completo\"}", "preferences.json.v1");
        await database.ImportLegacySettingOnceAsync("preferences", "{\"theme\":\"Aurora\"}", "preferences.json.v1");

        await database.ImportLegacySettingOnceAsync("first-run", "{\"version\":1}", "first-run-json.v1");
        await database.ImportLegacySettingOnceAsync("first-run", "{\"version\":2}", "first-run-json.v1");

        Assert.Equal("{\"theme\":\"Mínimo\"}", await database.ReadSettingAsync("preferences"));
        Assert.Equal("{\"version\":1}", await database.ReadSettingAsync("first-run"));
    }

    [Fact]
    public async Task FutureSchemaIsRejectedWithoutDeletingTheDatabase()
    {
        var path = Path.Combine(_root, "zeus.db");
        var database = new ZeusDatabase(path);
        await database.InitializeAsync();
        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version=99;";
            await command.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => new ZeusDatabase(path).InitializeAsync());
        Assert.True(File.Exists(path));
    }

    public void Dispose()
    {
        SqliteConnectionClear();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private void SqliteConnectionClear()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }
}
