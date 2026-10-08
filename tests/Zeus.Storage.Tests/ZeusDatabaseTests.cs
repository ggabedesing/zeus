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
            "A conclusão não foi confirmada", [new(0, "VerifySystemFiles", "Failed", "Saída não confirmada", "log.txt", null)]);

        await database.SaveMaintenanceHistoryAsync([session]);
        var restored = Assert.Single(await database.ReadMaintenanceHistoryAsync());

        Assert.Equal(id, restored.SessionId);
        Assert.True(restored.RestorePointConfirmed);
        Assert.False(restored.IsComplete);
        Assert.Equal("VerifySystemFiles", Assert.Single(restored.Steps).Action);
        Assert.Equal("log.txt", restored.Steps[0].LogFile);
        Assert.Equal(1, (await database.CheckHealthAsync()).MaintenanceSessionCount);
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
