using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Zeus.Core;
using Zeus.Storage;
using Zeus.Windows;

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
                    !Enum.IsDefined(step.Verification) ||
                    !actions.Add((step.Action, step.Action is MaintenanceActionId.InstallDriverUpdate or MaintenanceActionId.RollbackDriver ? step.TargetId : null)) || step.Message is null)
                    throw new InvalidDataException("O histórico contém uma ação inválida ou repetida.");
                if (step.Action == MaintenanceActionId.InstallDriverUpdate && !MaintenanceRequestProtocol.TryParseDriverIdentity(step.TargetId, out _, out _))
                    throw new InvalidDataException("O histórico contém uma identidade de driver inválida.");
                if (step.Action == MaintenanceActionId.InstallDriverUpdate &&
                    (step.UpdateServerSelection is not null || step.UpdateServiceId is not null) &&
                    !WindowsUpdateSourcePolicy.IsAllowed(step.UpdateServerSelection, step.UpdateServiceId))
                    throw new InvalidDataException("O histórico contém uma origem de Windows Update inválida para a verificação do driver.");
                if (step.Action != MaintenanceActionId.InstallDriverUpdate && (step.UpdateServerSelection is not null || step.UpdateServiceId is not null))
                    throw new InvalidDataException("Somente uma etapa de instalação de driver pode registrar a origem do Windows Update.");
                if (step.Action == MaintenanceActionId.RollbackDriver && !MaintenanceRequestProtocol.TryParsePnpInstanceId(step.TargetId))
                    throw new InvalidDataException("O histórico contém uma identidade PnP inválida para reversão de driver.");
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
        return new(false, FirstRunSetupComplete: false);
    }

    public Task SavePreferencesAsync(DesktopPreferences preferences) =>
        _database.WriteSettingAsync(PreferencesSettingKey, JsonSerializer.Serialize(preferences, JsonOptions));

    public Task AppendActivityAsync(ActivityEntry entry) => _database.AppendActivityAsync(entry);
    public Task<IReadOnlyList<ActivityEntry>> ReadRecentActivityAsync(int limit = 500) => _database.ReadRecentActivityAsync(limit);
    public Task<DatabaseHealth> CheckHealthAsync() => _database.CheckHealthAsync();
    public Task BackupDatabaseAsync(string destinationPath, CancellationToken cancellationToken = default) =>
        _database.BackupToAsync(destinationPath, cancellationToken);
    public Task<string> RestoreDatabaseAsync(string sourcePath, CancellationToken cancellationToken = default) =>
        _database.RestoreFromAsync(sourcePath, cancellationToken);

    public Task StartPerformanceSessionAsync(Guid sessionId, string label, DateTimeOffset startedAt) =>
        _database.StartPerformanceSessionAsync(sessionId.ToString("D"), label, startedAt);

    public Task AppendPerformanceObservationAsync(Guid sessionId, int sequence, PerformanceObservation observation) =>
        _database.AppendPerformanceSampleAsync(sessionId.ToString("D"), new(sequence, observation.CollectedAt,
            (int)Math.Clamp(observation.SamplingDuration.TotalMilliseconds, 0, 30_000), observation.CpuPercent,
            observation.TotalMemoryBytes, observation.AvailableMemoryBytes,
            JsonSerializer.Serialize(observation, JsonOptions)));

    public Task FinishPerformanceSessionAsync(Guid sessionId, DateTimeOffset finishedAt) =>
        _database.FinishPerformanceSessionAsync(sessionId.ToString("D"), finishedAt);

    public Task MarkPerformanceReferenceAsync(Guid sessionId) =>
        _database.MarkPerformanceReferenceAsync(sessionId.ToString("D"));

    public Task<IReadOnlyList<StoredPerformanceSession>> ReadPerformanceSessionsAsync() =>
        _database.ReadPerformanceSessionsAsync();

    public static Task ExportAsync(string path, ExportDocument document) => WriteAsync(path, document);

    public static async Task ExportDiagnosticPackageAsync(string path, ExportDocument document, string applicationVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationVersion);

        var generatedAt = DateTimeOffset.UtcNow;
        var reportBytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        var reportHash = Convert.ToHexString(SHA256.HashData(reportBytes)).ToLowerInvariant();
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(
            new DiagnosticPackageManifest(1, generatedAt, applicationVersion, document.SchemaVersion, reportHash), JsonOptions);
        var readme = string.Join(Environment.NewLine,
        [
            "Pacote de diagnóstico ZEUS",
            $"Versão do aplicativo: {applicationVersion}",
            $"Criado em UTC: {generatedAt:O}",
            "Conteúdo: relatorio.json, LEIA-ANTES.txt e manifesto.json.",
            "Privacidade: o relatório pode incluir nomes do computador, dispositivos, programas, processos, serviços, eventos, rede local, manutenção, limpeza e desempenho.",
            "Revise o relatorio.json e remova dados pessoais antes de compartilhar.",
            "Este pacote é salvo somente no destino escolhido; o ZEUS não o envia.",
            "O hash SHA-256 do manifesto confere a integridade dos bytes do relatório, mas não autentica sua origem.",
            "O pacote não inclui o banco SQLite nem arquivos brutos de log."
        ]) + Environment.NewLine;
        var readmeBytes = Encoding.UTF8.GetBytes(readme);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new IOException("Diretório de destino inválido.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".zeus-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
            {
                using (var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
                {
                    await WriteZipEntryAsync(archive, "relatorio.json", reportBytes, cancellationToken);
                    await WriteZipEntryAsync(archive, "LEIA-ANTES.txt", readmeBytes, cancellationToken);
                    await WriteZipEntryAsync(archive, "manifesto.json", manifestBytes, cancellationToken);
                }
                await file.FlushAsync(cancellationToken);
                file.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static async Task WriteZipEntryAsync(ZipArchive archive, string name, byte[] contents, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await stream.WriteAsync(contents, cancellationToken);
    }

    private static DesktopPreferences DeserializePreferences(string json) =>
        JsonSerializer.Deserialize<DesktopPreferences>(json, JsonOptions)
        ?? throw new InvalidDataException("As preferências armazenadas não são válidas.");

    private static StoredMaintenanceSession ToStored(MaintenanceReport report) => new(
        report.SessionId.ToString("D"), report.StartedAt, report.FinishedAt, report.RestorePointConfirmed, report.IsComplete, report.Error,
        report.Steps.Select((step, index) => new StoredMaintenanceStep(index, step.Action.ToString(), step.Outcome.ToString(), step.Message,
            step.LogFile, step.TargetId, step.Verification.ToString(), step.UpdateServerSelection, step.UpdateServiceId)).ToArray());

    private static MaintenanceReport FromStored(StoredMaintenanceSession session)
    {
        if (!Guid.TryParseExact(session.SessionId, "D", out var id) || id == Guid.Empty)
            throw new InvalidDataException("O banco contém um identificador de sessão inválido.");
        var steps = session.Steps.OrderBy(step => step.Sequence).Select(step => new MaintenanceStepResult(
            ParseEnum<MaintenanceActionId>(step.Action), ParseEnum<StepOutcome>(step.Outcome), step.Message, step.LogFile, step.TargetId,
            ParseEnum<MaintenanceVerificationStatus>(step.Verification), step.UpdateServerSelection, step.UpdateServiceId)).ToArray();
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
