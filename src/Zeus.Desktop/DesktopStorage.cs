using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Zeus.Core;

namespace Zeus.Desktop;

internal sealed class DesktopStorage
{
    private readonly string _directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zeus");

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        RespectRequiredConstructorParameters = true,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) }
    };

    public async Task<IReadOnlyList<MaintenanceReport>> ReadHistoryAsync()
    {
        var path = Path.Combine(_directory, "history.json");
        if (!File.Exists(path)) return [];
        var reports = await ReadAsync<List<MaintenanceReport>>(path)
            ?? throw new InvalidDataException("O histórico não contém uma lista de sessões válida.");
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
            var actions = new HashSet<MaintenanceActionId>();
            foreach (var step in report.Steps)
            {
                if (step is null || !Enum.IsDefined(step.Action) || !Enum.IsDefined(step.Outcome) ||
                    !actions.Add(step.Action) || step.Message is null)
                    throw new InvalidDataException("O histórico contém uma ação inválida ou repetida.");
            }
        }
    }

    public Task SaveHistoryAsync(IReadOnlyList<MaintenanceReport> reports) =>
        WriteAsync(Path.Combine(_directory, "history.json"), reports);

    public async Task<DesktopPreferences> ReadPreferencesAsync()
    {
        var path = Path.Combine(_directory, "preferences.json");
        return File.Exists(path) ? await ReadAsync<DesktopPreferences>(path) ?? new(false) : new(false);
    }

    public Task SavePreferencesAsync(bool isMinimal) =>
        WriteAsync(Path.Combine(_directory, "preferences.json"), new DesktopPreferences(isMinimal));

    public static Task ExportAsync(string path, HardwareSnapshot? snapshot, IReadOnlyList<MaintenanceReport> reports) =>
        WriteAsync(path, new ExportDocument(1, DateTimeOffset.UtcNow, snapshot, reports));

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
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
