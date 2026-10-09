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
    private readonly string _directory;
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) }
    };

    public PendingMaintenanceSessions() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zeus", "PendingMaintenance")) { }

    public PendingMaintenanceSessions(string storageDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageDirectory);
        _directory = Path.GetFullPath(storageDirectory);
    }

    public async Task RememberAsync(Guid sessionId, IReadOnlyCollection<MaintenanceRequest> requests,
        DateTimeOffset startedAt, CancellationToken cancellationToken = default, Guid? verificationOfSessionId = null)
    {
        ValidateId(sessionId);
        _ = MaintenancePolicy.ValidateRequests(requests);
        ValidateVerificationLink(sessionId, requests, verificationOfSessionId);
        Directory.CreateDirectory(_directory);
        AssertSafeDirectory();
        var destination = Path.Combine(_directory, sessionId.ToString("D") + ".json");
        var temporary = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".pending");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream,
                    new LaunchReceipt(sessionId, startedAt, requests.ToArray(), verificationOfSessionId), Options, cancellationToken);
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
            Guid? verificationOfSessionId = null;
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
                    ValidateVerificationLink(id, receipt.Requests, receipt.VerificationOfSessionId);
                    started = receipt.StartedAt;
                    requests = receipt.Requests;
                    verificationOfSessionId = receipt.VerificationOfSessionId;
                }
                var report = await SessionStore.ReadReportAsync(id);
                if (report.Steps is null || report.Steps.Any(step => step is null || !Enum.IsDefined(step.Action) || !Enum.IsDefined(step.Outcome)))
                    throw new InvalidDataException("Relatório protegido inválido.");
                if (verificationOfSessionId is not null && report.Steps.Any(step =>
                    step.TargetId is not null || !requests.Any(request => request.Action == step.Action)))
                    throw new InvalidDataException("O relatório protegido não corresponde às varreduras posteriores solicitadas.");
                if (verificationOfSessionId is not null && report.Steps.Count == 0)
                    throw new InvalidDataException("Nenhum resultado das varreduras posteriores foi recebido.");
                reports.Add(await RecoverDriverObservationsAsync(report with { VerificationOfSessionId = verificationOfSessionId }, requests,
                    SessionStore.ReadDriverActiveCheckpointAsync, cancellationToken));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
            {
                var unresolved = new MaintenanceReport(id, started, DateTimeOffset.UtcNow, false,
                    requests.Select(request => new MaintenanceStepResult(request.Action, StepOutcome.Skipped,
                        "O resultado desta ação não foi confirmado. Nenhuma ação será repetida automaticamente.", TargetId: request.TargetId,
                        Verification: MaintenanceVerificationStatus.ManualReviewRequired,
                        UpdateServerSelection: request.UpdateServerSelection, UpdateServiceId: request.UpdateServiceId)).ToArray(),
                    "Sessão pendente: não foi possível confirmar a conclusão. Consulte os logs antes de iniciar outro plano. " + error.Message,
                    IsComplete: false, VerificationOfSessionId: verificationOfSessionId);
                reports.Add(await RecoverDriverObservationsAsync(unresolved, requests, SessionStore.ReadDriverActiveCheckpointAsync,
                    cancellationToken));
            }
        }
        return reports.OrderByDescending(report => report.StartedAt).ToArray();
    }

    internal static async Task<MaintenanceReport> RecoverDriverObservationsAsync(MaintenanceReport report,
        IReadOnlyList<MaintenanceRequest> requests,
        Func<Guid, Guid, int, CancellationToken, Task<DriverActiveEvidence>> readCheckpoint,
        CancellationToken cancellationToken)
    {
        // A receipt identifies a possible protected observation. It never authorizes execution
        // or supplies evidence of WUA success. Preserve every original outcome and verification.
        if (requests.Count == 0) return report;
        _ = MaintenancePolicy.ValidateRequests(requests);
        var steps = report.Steps.ToArray();
        for (var index = 0; index < steps.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var step = steps[index];
            if (step.Action != MaintenanceActionId.InstallDriverUpdate || step.ActiveDriver is not null ||
                !MaintenanceRequestProtocol.TryParseDriverIdentity(step.TargetId, out var updateId, out var revision)) continue;
            var matches = requests.Where(request => request.Action == MaintenanceActionId.InstallDriverUpdate &&
                MaintenanceRequestProtocol.TryParseDriverIdentity(request.TargetId, out var requestedId, out var requestedRevision) &&
                requestedId == updateId && requestedRevision == revision).ToArray();
            if (matches.Length != 1 || step.UpdateServerSelection is not null && step.UpdateServerSelection != matches[0].UpdateServerSelection ||
                step.UpdateServiceId is not null && !string.Equals(step.UpdateServiceId, matches[0].UpdateServiceId, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var evidence = await readCheckpoint(report.SessionId, updateId, revision, cancellationToken);
                DriverActiveStatePolicy.Validate(evidence);
                steps[index] = step with { ActiveDriver = DriverActiveStatePolicy.BoundForPersistence(evidence),
                    UpdateServerSelection = step.UpdateServerSelection ?? matches[0].UpdateServerSelection,
                    UpdateServiceId = step.UpdateServiceId ?? matches[0].UpdateServiceId };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
            { /* Invalid or missing checkpoints remain unavailable; the unresolved attempt is preserved. */ }
        }
        return report with { Steps = steps };
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

    private static void ValidateVerificationLink(Guid id, IReadOnlyCollection<MaintenanceRequest> requests, Guid? parent)
    {
        if (parent is null) return;
        if (parent == Guid.Empty || parent == id || requests.Count == 0 || requests.Any(request =>
            request.Action is not MaintenanceActionId.ScanWindowsImage and not MaintenanceActionId.VerifySystemFiles || request.TargetId is not null))
            throw new ArgumentException("Vínculo posterior inválido: somente varreduras SCAN são permitidas.");
    }

    private sealed record LaunchReceipt(Guid SessionId, DateTimeOffset StartedAt, IReadOnlyList<MaintenanceRequest> Requests,
        Guid? VerificationOfSessionId = null);
}
