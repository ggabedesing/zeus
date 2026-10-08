using System.Text.Json;
using System.Text.Json.Serialization;
using Zeus.Core;

namespace Zeus.Windows;

/// <summary>
/// User-owned launch receipts. They never authorize execution; they only identify
/// protected reports to read after a restart or interrupted desktop session.
/// </summary>
public sealed class PendingMaintenanceSessions
{
    private readonly string _directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zeus", "PendingMaintenance");
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) }
    };

    public async Task RememberAsync(Guid sessionId, IReadOnlyCollection<MaintenanceRequest> requests,
        DateTimeOffset startedAt, CancellationToken cancellationToken = default)
    {
        ValidateId(sessionId);
        _ = MaintenancePolicy.ValidateRequests(requests);
        Directory.CreateDirectory(_directory);
        AssertSafeDirectory();
        var destination = Path.Combine(_directory, sessionId.ToString("D") + ".json");
        var temporary = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".pending");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream,
                    new LaunchReceipt(sessionId, startedAt, requests.ToArray()), Options, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, destination, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task<IReadOnlyList<MaintenanceReport>> RecoverAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_directory)) return [];
        AssertSafeDirectory();
        var reports = new List<MaintenanceReport>();
        foreach (var path in Directory.EnumerateFiles(_directory, "*.json").Take(200))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "D", out var id) || id == Guid.Empty)
                continue;
            var started = DateTimeOffset.UtcNow;
            IReadOnlyList<MaintenanceRequest> requests = [];
            try
            {
                var file = new FileInfo(path);
                if ((file.Attributes & FileAttributes.ReparsePoint) != 0 || file.Length > 64 * 1024)
                    throw new InvalidDataException("Registro pendente inválido.");
                await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var receipt = await JsonSerializer.DeserializeAsync<LaunchReceipt>(stream, Options, cancellationToken)
                        ?? throw new InvalidDataException("Registro pendente vazio.");
                    if (receipt.SessionId != id || receipt.Requests is null || receipt.Requests.Any(request => request is null))
                        throw new InvalidDataException("Registro pendente não corresponde à sessão.");
                    _ = MaintenancePolicy.ValidateRequests(receipt.Requests);
                    started = receipt.StartedAt;
                    requests = receipt.Requests;
                }
                var report = await SessionStore.ReadReportAsync(id);
                if (report.Steps is null || report.Steps.Any(step => step is null || !Enum.IsDefined(step.Action) || !Enum.IsDefined(step.Outcome)))
                    throw new InvalidDataException("Relatório protegido inválido.");
                reports.Add(report);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
            {
                reports.Add(new MaintenanceReport(id, started, DateTimeOffset.UtcNow, false,
                    requests.Select(request => new MaintenanceStepResult(request.Action, StepOutcome.Skipped,
                        "O resultado desta ação não foi confirmado. Nenhuma ação será repetida automaticamente.", TargetId: request.TargetId)).ToArray(),
                    "Sessão pendente: não foi possível confirmar a conclusão. Consulte os logs antes de iniciar outro plano. " + error.Message,
                    IsComplete: false));
            }
        }
        return reports.OrderByDescending(report => report.StartedAt).ToArray();
    }

    public Task ForgetAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateId(sessionId);
        if (!Directory.Exists(_directory)) return Task.CompletedTask;
        AssertSafeDirectory();
        var path = Path.Combine(_directory, sessionId.ToString("D") + ".json");
        if (File.Exists(path))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Registro pendente com redirecionamento não permitido.");
            File.Delete(path);
        }
        return Task.CompletedTask;
    }

    private void AssertSafeDirectory()
    {
        for (var directory = new DirectoryInfo(_directory); directory is not null; directory = directory.Parent)
            if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("O armazenamento de sessões pendentes contém redirecionamento.");
    }

    private static void ValidateId(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("Sessão inválida.", nameof(id));
    }

    private sealed record LaunchReceipt(Guid SessionId, DateTimeOffset StartedAt, IReadOnlyList<MaintenanceRequest> Requests);
}
