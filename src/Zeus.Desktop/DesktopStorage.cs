using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Zeus.Core;
using Zeus.Storage;

namespace Zeus.Desktop;

internal sealed class DesktopStorage
{
    private const string HistoryMigrationKey = "history-json-v1";
    private const string PreferencesMigrationKey = "preferences-json-v1";
    private const string PreferencesSettingKey = "desktop.preferences";
    private readonly string _directory;
    private readonly ZeusDatabase _database;

    public DesktopStorage(string? directory = null)
    {
        _directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zeus");
        _database = new(Path.Combine(_directory, "zeus.db"));
    }

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        RespectRequiredConstructorParameters = true,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) }
    };

    public async Task<IReadOnlyList<MaintenanceReport>> ReadHistoryAsync()
    {
        var legacyPath = Path.Combine(_directory, "history.json");
        if (!await _database.HasLegacyImportAsync(HistoryMigrationKey) && File.Exists(legacyPath))
        {
            var legacy = await ReadAsync<List<MaintenanceReport>>(legacyPath)
                ?? throw new InvalidDataException("O histórico antigo não contém uma lista de sessões válida. O arquivo foi preservado.");
            ValidateHistory(legacy);
            await _database.ImportLegacyHistoryOnceAsync(legacy.Select(ToStored).ToArray(), HistoryMigrationKey);
        }

        var reports = (await _database.ReadMaintenanceHistoryAsync()).Select(FromStored).ToArray();
        ValidateHistory(reports);
        return reports.OrderByDescending(report => report.StartedAt).ToArray();
    }

    internal static void ValidateHistory(IReadOnlyList<MaintenanceReport> reports)
    {
        var sessionIds = new HashSet<Guid>();
        foreach (var report in reports)
        {
            if (report is null || report.SessionId == Guid.Empty || !sessionIds.Add(report.SessionId) || report.Steps is null)
                throw new InvalidDataException("O histórico contém uma sessão inválida ou repetida.");
            var actions = new HashSet<(MaintenanceActionId Action, string? Target)>();
            foreach (var step in report.Steps)
            {
                if (step is null || !Enum.IsDefined(step.Action) || !Enum.IsDefined(step.Outcome) ||
                    !actions.Add((step.Action, step.Action == MaintenanceActionId.InstallDriverUpdate ? step.TargetId : null)) || step.Message is null)
                    throw new InvalidDataException("O histórico contém uma ação inválida ou repetida.");
                if (step.Action == MaintenanceActionId.InstallDriverUpdate && !MaintenanceRequestProtocol.TryParseDriverIdentity(step.TargetId, out _, out _))
                    throw new InvalidDataException("O histórico contém uma identidade de driver inválida.");
            }
        }
    }

    public Task SaveHistoryAsync(IReadOnlyList<MaintenanceReport> reports)
    {
        ValidateHistory(reports);
        return _database.SaveMaintenanceHistoryAsync(reports.Select(ToStored).ToArray());
    }

    public async Task<DesktopPreferences> ReadPreferencesAsync()
    {
        var json = await _database.ReadSettingAsync(PreferencesSettingKey);
        if (json is not null) return DeserializePreferences(json);

        var legacyPath = Path.Combine(_directory, "preferences.json");
        if (!await _database.HasLegacyImportAsync(PreferencesMigrationKey) && File.Exists(legacyPath))
        {
            var legacy = await ReadAsync<DesktopPreferences>(legacyPath)
                ?? throw new InvalidDataException("As preferências antigas não são válidas. O arquivo foi preservado.");
            var legacyJson = JsonSerializer.Serialize(legacy, JsonOptions);
            await _database.ImportLegacySettingOnceAsync(PreferencesSettingKey, legacyJson, PreferencesMigrationKey);
            json = await _database.ReadSettingAsync(PreferencesSettingKey);
            if (json is not null) return DeserializePreferences(json);
        }
        return new(false);
    }

    public Task SavePreferencesAsync(DesktopPreferences preferences) =>
        _database.WriteSettingAsync(PreferencesSettingKey, JsonSerializer.Serialize(preferences, JsonOptions));

    public Task AppendActivityAsync(ActivityEntry entry) => _database.AppendActivityAsync(entry);
    public Task<IReadOnlyList<ActivityEntry>> ReadRecentActivityAsync(int limit = 500) => _database.ReadRecentActivityAsync(limit);
    public Task<DatabaseHealth> CheckHealthAsync() => _database.CheckHealthAsync();

    public static Task ExportAsync(string path, ExportDocument document) => WriteAsync(path, document);

    private static DesktopPreferences DeserializePreferences(string json) =>
        JsonSerializer.Deserialize<DesktopPreferences>(json, JsonOptions)
        ?? throw new InvalidDataException("As preferências armazenadas não são válidas.");

    private static StoredMaintenanceSession ToStored(MaintenanceReport report) => new(
        report.SessionId.ToString("D"), report.StartedAt, report.FinishedAt, report.RestorePointConfirmed, report.IsComplete, report.Error,
        report.Steps.Select((step, index) => new StoredMaintenanceStep(index, step.Action.ToString(), step.Outcome.ToString(), step.Message, step.LogFile, step.TargetId)).ToArray());

    private static MaintenanceReport FromStored(StoredMaintenanceSession session)
    {
        if (!Guid.TryParseExact(session.SessionId, "D", out var id) || id == Guid.Empty)
            throw new InvalidDataException("O banco contém um identificador de sessão inválido.");
        var steps = session.Steps.OrderBy(step => step.Sequence).Select(step => new MaintenanceStepResult(
            ParseEnum<MaintenanceActionId>(step.Action), ParseEnum<StepOutcome>(step.Outcome), step.Message, step.LogFile, step.TargetId)).ToArray();
        return new(id, session.StartedAt, session.FinishedAt, session.RestorePointConfirmed, steps, session.Error, session.IsComplete);
    }

    private static T ParseEnum<T>(string value) where T : struct, Enum =>
        Enum.GetNames<T>().Contains(value, StringComparer.Ordinal) &&
        Enum.TryParse<T>(value, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new InvalidDataException($"O banco contém um valor de {typeof(T).Name} desconhecido.");

    private static async Task<T?> ReadAsync<T>(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > 16 * 1024 * 1024)
            throw new InvalidDataException("O arquivo local excede o limite de leitura de 16 MiB.");
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions);
    }

    private static async Task WriteAsync<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))
            ?? throw new IOException("Diretório de destino inválido.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".zeus-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, value, JsonOptions);
                await stream.FlushAsync();
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
