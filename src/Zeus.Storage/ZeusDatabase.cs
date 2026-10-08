using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Zeus.Storage;

public sealed record ActivityEntry(
    DateTimeOffset OccurredAt,
    string Category,
    string EventType,
    string Severity,
    string Summary,
    string? DetailsJson = null,
    string? CorrelationId = null);

public sealed record StoredMaintenanceStep(
    int Sequence,
    string Action,
    string Outcome,
    string Message,
    string? LogFile,
    string? TargetId);

public sealed record StoredMaintenanceSession(
    string SessionId,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    bool RestorePointConfirmed,
    bool IsComplete,
    string? Error,
    IReadOnlyList<StoredMaintenanceStep> Steps);

public sealed record StoredPerformanceSample(int Sequence, DateTimeOffset CollectedAt, int SamplingMilliseconds,
    double? CpuPercent, ulong TotalMemoryBytes, ulong AvailableMemoryBytes, string DetailsJson);

public sealed record StoredPerformanceSession(string SessionId, string Label, DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt, bool IsReference, IReadOnlyList<StoredPerformanceSample> Samples);

public sealed record DatabaseHealth(
    bool IsHealthy,
    int SchemaVersion,
    string IntegrityCheck,
    string SqliteVersion,
    long FileSizeBytes,
    long ActivityCount,
    long MaintenanceSessionCount,
    string? Error = null,
    long PerformanceSessionCount = 0,
    long PerformanceSampleCount = 0);

/// <summary>Local, versioned SQLite storage for user configuration, activity and maintenance history.</summary>
public sealed class ZeusDatabase
{
    public const int CurrentSchemaVersion = 2;
    private const int ActivityRetentionLimit = 10_000;
    private const int PerformanceSessionRetentionLimit = 200;
    private const int PerformanceSampleRetentionLimit = 4_000;
    private readonly string _path;
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initializeGate = new(1, 1);
    private bool _initialized;

    public ZeusDatabase(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
            DefaultTimeout = 5
        }.ToString();
    }

    public string PathName => _path;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _initializeGate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await using var connection = await OpenAsync(cancellationToken);
            var version = await ReadSchemaVersionAsync(connection, cancellationToken);
            if (version > CurrentSchemaVersion)
                throw new InvalidDataException($"O banco usa o esquema {version}, mais novo que esta versão ({CurrentSchemaVersion}). O arquivo foi preservado.");
            if (version == 0)
            {
                await CreateSchemaV1Async(connection, cancellationToken);
                version = 1;
            }
            if (version == 1)
            {
                await ValidateSchemaV1Async(connection, cancellationToken);
                await UpgradeSchemaV2Async(connection, cancellationToken);
                version = 2;
            }
            if (version != CurrentSchemaVersion) throw new InvalidDataException("A versão do esquema do banco não é reconhecida. O arquivo foi preservado.");
            await ValidateSchemaAsync(connection, cancellationToken);
            _initialized = true;
        }
        finally { _initializeGate.Release(); }
    }

    public async Task<string?> ReadSettingAsync(string key, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT json_value FROM app_settings WHERE setting_key = $key;";
        command.Parameters.AddWithValue("$key", key);
        return (string?)await command.ExecuteScalarAsync(cancellationToken);
    }

    public async Task WriteSettingAsync(string key, string jsonValue, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(jsonValue);
        ValidateJson(jsonValue, nameof(jsonValue));
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO app_settings(setting_key, json_value, updated_utc)
            VALUES($key, $value, $updated)
            ON CONFLICT(setting_key) DO UPDATE SET json_value=excluded.json_value, updated_utc=excluded.updated_utc;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", jsonValue);
        command.Parameters.AddWithValue("$updated", Utc(DateTimeOffset.UtcNow));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ImportLegacySettingOnceAsync(string key, string jsonValue, string migrationKey, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        ValidateKey(migrationKey);
        ArgumentNullException.ThrowIfNull(jsonValue);
        ValidateJson(jsonValue, nameof(jsonValue));
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        if (await HasMigrationAsync(connection, transaction, migrationKey, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }
        await using (var setting = connection.CreateCommand())
        {
            setting.Transaction = transaction;
            setting.CommandText = "INSERT OR IGNORE INTO app_settings(setting_key,json_value,updated_utc) VALUES($key,$value,$updated);";
            setting.Parameters.AddWithValue("$key", key);
            setting.Parameters.AddWithValue("$value", jsonValue);
            setting.Parameters.AddWithValue("$updated", Utc(DateTimeOffset.UtcNow));
            await setting.ExecuteNonQueryAsync(cancellationToken);
        }
        await RecordMigrationAsync(connection, transaction, migrationKey, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> HasLegacyImportAsync(string migrationKey, CancellationToken cancellationToken = default)
    {
        ValidateKey(migrationKey);
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM app_metadata WHERE metadata_key=$key LIMIT 1;";
        command.Parameters.AddWithValue("$key", "legacy:" + migrationKey);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    public async Task AppendActivityAsync(ActivityEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ValidateText(entry.Category, 80, nameof(entry.Category));
        ValidateText(entry.EventType, 100, nameof(entry.EventType));
        ValidateText(entry.Severity, 24, nameof(entry.Severity));
        ValidateText(entry.Summary, 2_000, nameof(entry.Summary));
        if (entry.DetailsJson is { Length: > 16_384 }) throw new ArgumentOutOfRangeException(nameof(entry), "Detalhes excedem 16 KiB.");
        if (entry.DetailsJson is not null)
        {
            try { using var _ = JsonDocument.Parse(entry.DetailsJson); }
            catch (JsonException error) { throw new ArgumentException("Detalhes devem conter JSON válido.", nameof(entry), error); }
        }
        if (entry.CorrelationId is { Length: > 100 }) throw new ArgumentOutOfRangeException(nameof(entry), "Identificador de correlação excede 100 caracteres.");
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO activity_entries(occurred_utc,category,event_type,severity,summary,details_json,correlation_id) VALUES($at,$category,$type,$severity,$summary,$details,$correlation);";
            command.Parameters.AddWithValue("$at", Utc(entry.OccurredAt));
            command.Parameters.AddWithValue("$category", entry.Category);
            command.Parameters.AddWithValue("$type", entry.EventType);
            command.Parameters.AddWithValue("$severity", entry.Severity);
            command.Parameters.AddWithValue("$summary", entry.Summary);
            command.Parameters.AddWithValue("$details", (object?)entry.DetailsJson ?? DBNull.Value);
            command.Parameters.AddWithValue("$correlation", (object?)entry.CorrelationId ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var trim = connection.CreateCommand())
        {
            trim.Transaction = transaction;
            trim.CommandText = "DELETE FROM activity_entries WHERE id NOT IN (SELECT id FROM activity_entries ORDER BY id DESC LIMIT $limit);";
            trim.Parameters.AddWithValue("$limit", ActivityRetentionLimit);
            await trim.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ActivityEntry>> ReadRecentActivityAsync(int limit = 500, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > ActivityRetentionLimit) throw new ArgumentOutOfRangeException(nameof(limit));
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT occurred_utc,category,event_type,severity,summary,details_json,correlation_id FROM activity_entries ORDER BY id DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var entries = new List<ActivityEntry>();
        while (await reader.ReadAsync(cancellationToken))
            entries.Add(new(ParseUtc(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6)));
        return entries;
    }

    public async Task<IReadOnlyList<StoredMaintenanceSession>> ReadMaintenanceHistoryAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        var sessions = new List<StoredMaintenanceSession>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT session_id,started_utc,finished_utc,restore_point_confirmed,is_complete,error FROM maintenance_sessions ORDER BY started_utc DESC;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                sessions.Add(new(reader.GetString(0), ParseUtc(reader.GetString(1)), ParseUtc(reader.GetString(2)), reader.GetInt64(3) != 0,
                    reader.GetInt64(4) != 0, reader.IsDBNull(5) ? null : reader.GetString(5), []));
        }
        for (var index = 0; index < sessions.Count; index++)
        {
            var session = sessions[index];
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT sequence,action,outcome,message,log_file,target_id FROM maintenance_steps WHERE session_id=$id ORDER BY sequence;";
            command.Parameters.AddWithValue("$id", session.SessionId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var steps = new List<StoredMaintenanceStep>();
            while (await reader.ReadAsync(cancellationToken))
                steps.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5)));
            sessions[index] = session with { Steps = steps };
        }
        return sessions;
    }

    public async Task SaveMaintenanceHistoryAsync(IReadOnlyList<StoredMaintenanceSession> sessions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        await ExecuteAsync(connection, transaction, "DELETE FROM maintenance_steps; DELETE FROM maintenance_sessions;", cancellationToken);
        foreach (var session in sessions)
        {
            await InsertSessionAsync(connection, transaction, session, cancellationToken);
            foreach (var step in session.Steps) await InsertStepAsync(connection, transaction, session.SessionId, step, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ImportLegacyHistoryOnceAsync(IReadOnlyList<StoredMaintenanceSession> sessions, string migrationKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ValidateKey(migrationKey);
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        if (await HasMigrationAsync(connection, transaction, migrationKey, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }
        foreach (var session in sessions)
        {
            var inserted = await InsertSessionAsync(connection, transaction, session, cancellationToken, ignoreConflict: true);
            if (inserted)
                foreach (var step in session.Steps) await InsertStepAsync(connection, transaction, session.SessionId, step, cancellationToken);
        }
        await RecordMigrationAsync(connection, transaction, migrationKey, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task StartPerformanceSessionAsync(string sessionId, string label, DateTimeOffset startedAt, CancellationToken cancellationToken = default)
    {
        ValidateSessionId(sessionId);
        ValidateText(label, 120, nameof(label));
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO performance_sessions(session_id,label,started_utc,finished_utc,is_reference) VALUES($id,$label,$started,NULL,0);";
        command.Parameters.AddWithValue("$id", sessionId);
        command.Parameters.AddWithValue("$label", label);
        command.Parameters.AddWithValue("$started", Utc(startedAt));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AppendPerformanceSampleAsync(string sessionId, StoredPerformanceSample sample, CancellationToken cancellationToken = default)
    {
        ValidateSessionId(sessionId);
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentNullException.ThrowIfNull(sample.DetailsJson);
        if (sample.Sequence is < 0 or >= 600) throw new ArgumentOutOfRangeException(nameof(sample));
        if (sample.SamplingMilliseconds is < 0 or > 30_000) throw new ArgumentOutOfRangeException(nameof(sample));
        if (sample.CpuPercent is { } cpu && (!double.IsFinite(cpu) || cpu is < 0 or > 100)) throw new ArgumentOutOfRangeException(nameof(sample));
        if (sample.AvailableMemoryBytes > sample.TotalMemoryBytes && sample.TotalMemoryBytes != 0) throw new ArgumentException("RAM disponível excede o total informado.", nameof(sample));
        if (sample.DetailsJson.Length > 65_536) throw new ArgumentOutOfRangeException(nameof(sample), "Detalhes da amostra excedem 64 KiB.");
        ValidateJson(sample.DetailsJson, nameof(sample));
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO performance_samples(session_id,sequence,observed_utc,sampling_ms,cpu_percent,total_memory_bytes,available_memory_bytes,details_json) VALUES($id,$sequence,$observed,$sampling,$cpu,$total,$available,$details);";
            insert.Parameters.AddWithValue("$id", sessionId);
            insert.Parameters.AddWithValue("$sequence", sample.Sequence);
            insert.Parameters.AddWithValue("$observed", Utc(sample.CollectedAt));
            insert.Parameters.AddWithValue("$sampling", sample.SamplingMilliseconds);
            insert.Parameters.AddWithValue("$cpu", (object?)sample.CpuPercent ?? DBNull.Value);
            insert.Parameters.AddWithValue("$total", sample.TotalMemoryBytes.ToString(CultureInfo.InvariantCulture));
            insert.Parameters.AddWithValue("$available", sample.AvailableMemoryBytes.ToString(CultureInfo.InvariantCulture));
            insert.Parameters.AddWithValue("$details", sample.DetailsJson);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var trim = connection.CreateCommand())
        {
            trim.Transaction = transaction;
            trim.CommandText = "DELETE FROM performance_samples WHERE id NOT IN (SELECT id FROM performance_samples ORDER BY id DESC LIMIT $limit);";
            trim.Parameters.AddWithValue("$limit", PerformanceSampleRetentionLimit);
            await trim.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task FinishPerformanceSessionAsync(string sessionId, DateTimeOffset finishedAt, CancellationToken cancellationToken = default)
    {
        ValidateSessionId(sessionId);
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE performance_sessions SET finished_utc=$finished WHERE session_id=$id AND finished_utc IS NULL;";
        command.Parameters.AddWithValue("$id", sessionId);
        command.Parameters.AddWithValue("$finished", Utc(finishedAt));
        await command.ExecuteNonQueryAsync(cancellationToken);
        await TrimPerformanceSessionsAsync(connection, cancellationToken);
    }

    public async Task MarkPerformanceReferenceAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ValidateSessionId(sessionId);
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE performance_sessions SET is_reference=CASE WHEN session_id=$id THEN 1 ELSE 0 END WHERE EXISTS(SELECT 1 FROM performance_sessions WHERE session_id=$id);";
        command.Parameters.AddWithValue("$id", sessionId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            throw new InvalidOperationException("A sessão de desempenho não existe; nenhuma referência foi alterada.");
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StoredPerformanceSession>> ReadPerformanceSessionsAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        var sessions = new List<StoredPerformanceSession>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT session_id,label,started_utc,finished_utc,is_reference FROM performance_sessions ORDER BY started_utc DESC LIMIT $limit;";
            command.Parameters.AddWithValue("$limit", PerformanceSessionRetentionLimit + 1);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                sessions.Add(new(reader.GetString(0), reader.GetString(1), ParseUtc(reader.GetString(2)),
                    reader.IsDBNull(3) ? null : ParseUtc(reader.GetString(3)), reader.GetInt64(4) != 0, []));
        }
        for (var index = 0; index < sessions.Count; index++)
        {
            var session = sessions[index];
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT sequence,observed_utc,sampling_ms,cpu_percent,total_memory_bytes,available_memory_bytes,details_json FROM performance_samples WHERE session_id=$id ORDER BY sequence;";
            command.Parameters.AddWithValue("$id", session.SessionId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var samples = new List<StoredPerformanceSample>();
            while (await reader.ReadAsync(cancellationToken))
                samples.Add(new(reader.GetInt32(0), ParseUtc(reader.GetString(1)), reader.GetInt32(2),
                    reader.IsDBNull(3) ? null : reader.GetDouble(3), ulong.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
                    ulong.Parse(reader.GetString(5), CultureInfo.InvariantCulture), reader.GetString(6)));
            sessions[index] = session with { Samples = samples };
        }
        return sessions;
    }

    private async Task TrimPerformanceSessionsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM performance_sessions WHERE is_reference=0 AND session_id NOT IN (SELECT session_id FROM performance_sessions WHERE is_reference=0 ORDER BY started_utc DESC LIMIT $limit);";
        command.Parameters.AddWithValue("$limit", PerformanceSessionRetentionLimit);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<DatabaseHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        var integrity = await ScalarStringAsync(connection, "PRAGMA quick_check;", cancellationToken);
        var sqliteVersion = await ScalarStringAsync(connection, "SELECT sqlite_version();", cancellationToken);
        var activityCount = await ScalarLongAsync(connection, "SELECT COUNT(*) FROM activity_entries;", cancellationToken);
        var sessionCount = await ScalarLongAsync(connection, "SELECT COUNT(*) FROM maintenance_sessions;", cancellationToken);
        var performanceSessionCount = await ScalarLongAsync(connection, "SELECT COUNT(*) FROM performance_sessions;", cancellationToken);
        var performanceSampleCount = await ScalarLongAsync(connection, "SELECT COUNT(*) FROM performance_samples;", cancellationToken);
        var size = File.Exists(_path) ? new FileInfo(_path).Length : 0;
        return new(string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase), CurrentSchemaVersion, integrity, sqliteVersion, size, activityCount, sessionCount,
            string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase) ? null : "A verificação rápida do SQLite encontrou inconsistências.",
            performanceSessionCount, performanceSampleCount);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
            await command.ExecuteNonQueryAsync(cancellationToken);
            return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    private static async Task<int> ReadSchemaVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private static async Task CreateSchemaV1Async(SqliteConnection connection, CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS schema_migrations(version INTEGER PRIMARY KEY, applied_utc TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS app_settings(setting_key TEXT PRIMARY KEY, json_value TEXT NOT NULL CHECK(json_valid(json_value)), updated_utc TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS app_metadata(metadata_key TEXT PRIMARY KEY, metadata_value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS activity_entries(
                id INTEGER PRIMARY KEY AUTOINCREMENT, occurred_utc TEXT NOT NULL, category TEXT NOT NULL,
                event_type TEXT NOT NULL, severity TEXT NOT NULL, summary TEXT NOT NULL,
                details_json TEXT NULL CHECK(details_json IS NULL OR json_valid(details_json)), correlation_id TEXT NULL);
            CREATE INDEX IF NOT EXISTS ix_activity_entries_occurred ON activity_entries(occurred_utc DESC);
            CREATE TABLE IF NOT EXISTS maintenance_sessions(
                session_id TEXT PRIMARY KEY, started_utc TEXT NOT NULL, finished_utc TEXT NOT NULL,
                restore_point_confirmed INTEGER NOT NULL CHECK(restore_point_confirmed IN (0,1)),
                is_complete INTEGER NOT NULL CHECK(is_complete IN (0,1)), error TEXT NULL);
            CREATE TABLE IF NOT EXISTS maintenance_steps(
                session_id TEXT NOT NULL REFERENCES maintenance_sessions(session_id) ON DELETE CASCADE,
                sequence INTEGER NOT NULL CHECK(sequence >= 0), action TEXT NOT NULL, outcome TEXT NOT NULL,
                message TEXT NOT NULL, log_file TEXT NULL, target_id TEXT NULL,
                PRIMARY KEY(session_id, sequence));
            INSERT OR IGNORE INTO schema_migrations(version,applied_utc) VALUES(1,$applied);
            PRAGMA user_version=1;
            """;
        command.Parameters.AddWithValue("$applied", Utc(DateTimeOffset.UtcNow));
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task UpgradeSchemaV2Async(SqliteConnection connection, CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE performance_sessions(
                session_id TEXT PRIMARY KEY, label TEXT NOT NULL, started_utc TEXT NOT NULL,
                finished_utc TEXT NULL, is_reference INTEGER NOT NULL CHECK(is_reference IN (0,1)));
            CREATE TABLE performance_samples(
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id TEXT NOT NULL REFERENCES performance_sessions(session_id) ON DELETE CASCADE,
                sequence INTEGER NOT NULL CHECK(sequence >= 0 AND sequence < 600),
                observed_utc TEXT NOT NULL, sampling_ms INTEGER NOT NULL CHECK(sampling_ms BETWEEN 0 AND 30000),
                cpu_percent REAL NULL CHECK(cpu_percent IS NULL OR (cpu_percent >= 0 AND cpu_percent <= 100)),
                total_memory_bytes TEXT NOT NULL, available_memory_bytes TEXT NOT NULL,
                details_json TEXT NOT NULL CHECK(json_valid(details_json)),
                UNIQUE(session_id,sequence));
            CREATE INDEX ix_performance_sessions_started ON performance_sessions(started_utc DESC);
            CREATE INDEX ix_performance_samples_observed ON performance_samples(observed_utc DESC);
            INSERT INTO schema_migrations(version,applied_utc) VALUES(2,$applied);
            PRAGMA user_version=2;
            """;
        command.Parameters.AddWithValue("$applied", Utc(DateTimeOffset.UtcNow));
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static Task ValidateSchemaV1Async(SqliteConnection connection, CancellationToken cancellationToken) =>
        ValidateSchemaTablesAsync(connection, cancellationToken,
            "schema_migrations", "app_settings", "app_metadata", "activity_entries", "maintenance_sessions", "maintenance_steps");

    private static Task ValidateSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken) =>
        ValidateSchemaTablesAsync(connection, cancellationToken,
            "schema_migrations", "app_settings", "app_metadata", "activity_entries", "maintenance_sessions", "maintenance_steps", "performance_sessions", "performance_samples");

    private static async Task ValidateSchemaTablesAsync(SqliteConnection connection, CancellationToken cancellationToken, params string[] tables)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ({string.Join(",", tables.Select(table => "'" + table + "'"))});";
        var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        if (count != tables.Length) throw new InvalidDataException("O esquema do banco Zeus está incompleto. O arquivo foi preservado.");
    }

    private static async Task<bool> HasMigrationAsync(SqliteConnection connection, SqliteTransaction transaction, string key, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM app_metadata WHERE metadata_key=$key LIMIT 1;";
        command.Parameters.AddWithValue("$key", "legacy:" + key);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task RecordMigrationAsync(SqliteConnection connection, SqliteTransaction transaction, string key, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO app_metadata(metadata_key,metadata_value) VALUES($key,$value);";
        command.Parameters.AddWithValue("$key", "legacy:" + key);
        command.Parameters.AddWithValue("$value", Utc(DateTimeOffset.UtcNow));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> InsertSessionAsync(SqliteConnection connection, SqliteTransaction transaction, StoredMaintenanceSession session,
        CancellationToken cancellationToken, bool ignoreConflict = false)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"INSERT {(ignoreConflict ? "OR IGNORE " : string.Empty)}INTO maintenance_sessions(session_id,started_utc,finished_utc,restore_point_confirmed,is_complete,error) VALUES($id,$started,$finished,$restore,$complete,$error);";
        command.Parameters.AddWithValue("$id", session.SessionId);
        command.Parameters.AddWithValue("$started", Utc(session.StartedAt));
        command.Parameters.AddWithValue("$finished", Utc(session.FinishedAt));
        command.Parameters.AddWithValue("$restore", session.RestorePointConfirmed ? 1 : 0);
        command.Parameters.AddWithValue("$complete", session.IsComplete ? 1 : 0);
        command.Parameters.AddWithValue("$error", (object?)session.Error ?? DBNull.Value);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private static async Task InsertStepAsync(SqliteConnection connection, SqliteTransaction transaction, string sessionId,
        StoredMaintenanceStep step, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO maintenance_steps(session_id,sequence,action,outcome,message,log_file,target_id) VALUES($id,$sequence,$action,$outcome,$message,$log,$target);";
        command.Parameters.AddWithValue("$id", sessionId);
        command.Parameters.AddWithValue("$sequence", step.Sequence);
        command.Parameters.AddWithValue("$action", step.Action);
        command.Parameters.AddWithValue("$outcome", step.Outcome);
        command.Parameters.AddWithValue("$message", step.Message);
        command.Parameters.AddWithValue("$log", (object?)step.LogFile ?? DBNull.Value);
        command.Parameters.AddWithValue("$target", (object?)step.TargetId ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<string> ScalarStringAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static async Task<long> ScalarLongAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private static string Utc(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset ParseUtc(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private static void ValidateSessionId(string value)
    {
        if (!Guid.TryParseExact(value, "D", out var parsed) || parsed == Guid.Empty)
            throw new ArgumentException("O identificador da sessão deve ser um GUID não vazio no formato D.", nameof(value));
    }
    private static void ValidateKey(string value) => ValidateText(value, 120, nameof(value));
    private static void ValidateJson(string value, string parameter)
    {
        try { using var _ = JsonDocument.Parse(value); }
        catch (JsonException error) { throw new ArgumentException("O valor deve conter JSON válido.", parameter, error); }
    }
    private static void ValidateText(string value, int maxLength, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength) throw new ArgumentOutOfRangeException(parameter);
    }
}
