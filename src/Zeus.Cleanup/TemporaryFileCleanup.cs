using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zeus.Cleanup;

/// <summary>
/// User-scoped, recoverable cleanup of a caller-selected temporary directory. Nothing is
/// deleted during quarantine. No elevation, directory-link traversal or recursive deletion.
/// Scans are valid for thirty minutes in the instance that produced them.
/// </summary>
public sealed class TemporaryFileCleanup
{
    private const int MaximumEntries = 10_000;
    private const long MaximumFileBytes = 128L * 1024 * 1024;
    private const long MaximumScanBytes = 512L * 1024 * 1024;
    private const long MaximumManifestBytes = 16L * 1024 * 1024;
    private readonly string _temporaryRoot;
    private readonly string _storageRoot;
    private readonly string _rootIdentity;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Guid, ScanState> _scans = [];
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public TemporaryFileCleanup(string temporaryRoot, string storageRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(temporaryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageRoot);
        // Reject redirections before canonicalization; normalize physical Windows aliases
        // afterwards so device prefixes, 8.3 paths and mapped drives cannot hide overlap.
        CheckAncestors(temporaryRoot);
        CheckAncestors(storageRoot);
        _temporaryRoot = CleanupFileLease.CanonicalizeRoot(temporaryRoot);
        _storageRoot = CleanupFileLease.CanonicalizeRoot(storageRoot);
        if (PathComparer.Equals(_temporaryRoot, Path.GetPathRoot(_temporaryRoot)) ||
            PathComparer.Equals(_storageRoot, Path.GetPathRoot(_storageRoot)))
            throw new ArgumentException("A limpeza não pode usar a raiz de um volume.");
        if (IsWithin(_storageRoot, _temporaryRoot) || IsWithin(_temporaryRoot, _storageRoot))
            throw new ArgumentException("Temporários e recuperação precisam usar diretórios separados.");
        CheckAncestors(_temporaryRoot);
        CheckAncestors(_storageRoot);
        _rootIdentity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            OperatingSystem.IsWindows() ? _temporaryRoot.ToUpperInvariant() : _temporaryRoot)));
    }

    public async Task<CleanupScan> ScanAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var warnings = new List<string>();
            var candidates = new List<TemporaryFileCandidate>();
            var fingerprints = new Dictionary<string, Fingerprint>(StringComparer.Ordinal);
            var now = DateTimeOffset.UtcNow;
            CheckAncestors(_temporaryRoot);
            if (!Directory.Exists(_temporaryRoot))
                warnings.Add("O diretório de temporários não existe.");
            else
            {
                var pending = new Stack<(string Directory, int Depth)>();
                pending.Push((_temporaryRoot, 0));
                var visited = 0;
                long scannedBytes = 0;
                while (pending.Count > 0 && visited < MaximumEntries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var (directory, depth) = pending.Pop();
                    try
                    {
                        CheckAncestors(directory);
                        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (++visited > MaximumEntries) break;
                            try
                            {
                                CheckAncestors(path);
                                var attributes = File.GetAttributes(path);
                                if ((attributes & FileAttributes.Directory) != 0)
                                {
                                    if (depth < 64) pending.Push((path, depth + 1));
                                    else warnings.Add("Um diretório profundo foi ignorado.");
                                    continue;
                                }
                                var info = new FileInfo(path);
                                if (info.Length > MaximumFileBytes || info.LastWriteTimeUtc > now.UtcDateTime.AddDays(-7))
                                    continue;
                                if (scannedBytes + info.Length > MaximumScanBytes)
                                {
                                    warnings.Add("A leitura atingiu 512 MiB; os demais arquivos ficaram para outra análise.");
                                    pending.Clear();
                                    visited = MaximumEntries;
                                    break;
                                }
                                var relative = Path.GetRelativePath(_temporaryRoot, path);
                                ValidateRelative(relative);
                                var before = Capture(info);
                                var digest = await HashAsync(path, cancellationToken).ConfigureAwait(false);
                                info.Refresh();
                                if (before != Capture(info))
                                {
                                    warnings.Add($"Arquivo alterado durante a análise: {relative}");
                                    continue;
                                }
                                scannedBytes += info.Length;
                                var id = Guid.NewGuid().ToString("N");
                                var candidate = new TemporaryFileCandidate(id, relative, (ulong)info.Length,
                                    new DateTimeOffset(info.LastWriteTimeUtc));
                                candidates.Add(candidate);
                                fingerprints.Add(id, new Fingerprint(candidate, before, digest));
                            }
                            catch (Exception exception) when (IsFileFailure(exception))
                            {
                                warnings.Add($"Um arquivo foi ignorado: {Path.GetFileName(path)} ({exception.Message})");
                            }
                        }
                    }
                    catch (Exception exception) when (IsFileFailure(exception))
                    {
                        warnings.Add($"Um diretório foi ignorado: {Path.GetFileName(directory)} ({exception.Message})");
                    }
                }
                if (visited >= MaximumEntries)
                    warnings.Add("A análise é limitada a 10.000 entradas; outras entradas ficaram para outra análise.");
            }
            foreach (var expired in _scans.Where(pair => now - pair.Value.Scan.ScannedAt > TimeSpan.FromMinutes(30))
                         .Select(pair => pair.Key).ToArray())
                _scans.Remove(expired);
            var scan = new CleanupScan(Guid.NewGuid(), _temporaryRoot, now,
                candidates.AsReadOnly(), warnings.AsReadOnly());
            _scans.Add(scan.Id, new ScanState(scan, fingerprints));
            return scan;
        }
        finally { _gate.Release(); }
    }

    public async Task<CleanupResult> QuarantineAsync(CleanupScan scan, IReadOnlyCollection<string> selectedIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(selectedIds);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_scans.TryGetValue(scan.Id, out var state) ||
                DateTimeOffset.UtcNow - state.Scan.ScannedAt > TimeSpan.FromMinutes(30) ||
                scan.ScannedAt != state.Scan.ScannedAt || !PathComparer.Equals(scan.RootPath, _temporaryRoot) ||
                !scan.Files.SequenceEqual(state.Scan.Files))
                throw new ArgumentException("A análise não é válida nesta instância ou expirou. Analise novamente.", nameof(scan));
            var selections = selectedIds.ToArray();
            if (selections.Length == 0 || selections.Distinct(StringComparer.Ordinal).Count() != selections.Length ||
                selections.Any(id => id is null || !state.Fingerprints.ContainsKey(id)))
                throw new ArgumentException("A seleção contém identificadores ausentes, repetidos ou desconhecidos.", nameof(selectedIds));

            using var storageLock = await LockStorageAsync(cancellationToken).ConfigureAwait(false);
            var manifest = new Manifest
            {
                Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow, RootIdentity = _rootIdentity
            };
            // Freeze every destination and its metadata in one checkpoint. Subsequent state
            // changes are journaled in constant space rather than rewriting a growing manifest.
            foreach (var selected in selections)
            {
                var fingerprint = state.Fingerprints[selected];
                manifest.Entries.Add(new ManifestEntry
                {
                    Id = Guid.NewGuid().ToString("N"), RelativePath = fingerprint.Candidate.RelativePath,
                    SizeBytes = fingerprint.Candidate.SizeBytes, Hash = fingerprint.Hash, State = "Planned"
                });
            }
            var sessionPath = SessionPath(manifest.Id);
            EnsureDirectory(sessionPath);
            using var sessionScope = CleanupFileLease.PinExistingParents(Path.Combine(sessionPath, "entry"));
            await SaveAsync(manifest, cancellationToken).ConfigureAwait(false);
            var warnings = new List<string>();
            var skipped = 0;
            var moved = 0;
            ulong bytes = 0;
            for (var index = 0; index < selections.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var selected = selections[index];
                var fingerprint = state.Fingerprints[selected];
                var candidate = fingerprint.Candidate;
                var entry = manifest.Entries[index];
                var sourcePath = SourcePath(candidate.RelativePath);
                try
                {
                    CheckAncestors(sourcePath);
                    using var fileLease = CleanupFileLease.Open(sourcePath);
                    if (Capture(new FileInfo(sourcePath)) != fingerprint.Metadata ||
                        fileLease.Length != (long)candidate.SizeBytes ||
                        await fileLease.HashAsync(cancellationToken).ConfigureAwait(false) != fingerprint.Hash)
                    {
                        skipped++;
                        entry.State = "Skipped";
                        warnings.Add($"Arquivo alterado ou ausente; analise novamente: {candidate.RelativePath}");
                        await SaveAsync(manifest, CancellationToken.None, entry).ConfigureAwait(false);
                        continue;
                    }
                    // Durable intent comes before the move, making a crash between move and update recoverable.
                    CheckAncestors(sourcePath);
                    CheckAncestors(sessionPath);
                    if (Capture(new FileInfo(sourcePath)) != fingerprint.Metadata)
                    {
                        entry.State = "Skipped";
                        skipped++;
                        warnings.Add($"Arquivo alterado antes da movimentação: {candidate.RelativePath}");
                    }
                    else
                    {
                        fileLease.MoveTo(QuarantinedPath(manifest.Id, entry));
                        entry.State = "Moved";
                        moved++;
                        bytes += entry.SizeBytes;
                    }
                    // Once a file moved, finish the journal even if cancellation arrived in the meantime.
                    await SaveAsync(manifest, CancellationToken.None, entry).ConfigureAwait(false);
                }
                catch (Exception exception) when (IsFileFailure(exception))
                {
                    skipped++;
                    warnings.Add($"Não foi possível isolar {candidate.RelativePath}: {exception.Message}");
                    // Future Planned entries must stay Planned until their own move. Reconciling
                    // the whole active batch here could erase another entry's recovery intent.
                    await ReconcileAsync(manifest, warnings, CancellationToken.None, entry).ConfigureAwait(false);
                    await SaveAsync(manifest, CancellationToken.None, entry).ConfigureAwait(false);
                }
            }
            _scans.Remove(scan.Id);
            return new CleanupResult(manifest.Id, moved, skipped, bytes, warnings.AsReadOnly());
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<QuarantineSession>> ListSessionsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var storageLock = await LockStorageAsync(cancellationToken).ConfigureAwait(false);
            var sessions = new List<QuarantineSession>();
            foreach (var directory in Directory.EnumerateDirectories(_storageRoot).Take(MaximumEntries))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out var id)) continue;
                try
                {
                    using var sessionScope = CleanupFileLease.PinExistingParents(Path.Combine(directory, "entry"));
                    var manifest = await LoadAsync(id, cancellationToken).ConfigureAwait(false);
                    var failedRecovery = await ReconcileAsync(manifest, [], cancellationToken).ConfigureAwait(false);
                    var moved = manifest.Entries.Where(entry => entry.State == "Moved" &&
                        !manifest.FailedEntries.Contains(entry.Id)).ToArray();
                    var status = moved.Length > 0 ? "Quarantined" :
                        failedRecovery > 0 || manifest.Entries.Any(entry => entry.State is "Missing" or "Planned" or "Restoring" or "Purging") ? "Incomplete" :
                        manifest.Entries.Any(entry => entry.State == "Purged") ? "Deleted" : "Restored";
                    sessions.Add(new QuarantineSession(id, manifest.CreatedAt, moved.Length,
                        SumBytes(moved), status));
                }
                catch (Exception exception) when (IsFileFailure(exception) || exception is JsonException)
                {
                    // Untrusted/corrupt directories are never presented as actionable recovery sessions.
                }
            }
            return sessions.OrderByDescending(session => session.CreatedAt).ToArray();
        }
        finally { _gate.Release(); }
    }

    public async Task<CleanupRestoreResult> RestoreAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var storageLock = await LockStorageAsync(cancellationToken).ConfigureAwait(false);
            using var sessionScope = CleanupFileLease.PinExistingParents(Path.Combine(SessionPath(sessionId), "entry"));
            var manifest = await LoadAsync(sessionId, cancellationToken).ConfigureAwait(false);
            var warnings = new List<string>();
            var failedRecovery = await ReconcileAsync(manifest, warnings, cancellationToken).ConfigureAwait(false);
            var restored = 0;
            var skipped = failedRecovery;
            foreach (var entry in manifest.Entries.Where(entry => entry.State == "Moved" &&
                         !manifest.FailedEntries.Contains(entry.Id)).ToArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var destination = SourcePath(entry.RelativePath);
                    using var destinationScope = CleanupFileLease.PinExistingParents(destination);
                    CheckAncestors(destination);
                    if (PathExists(destination))
                    {
                        skipped++;
                        warnings.Add($"Já existe um arquivo no destino; ele foi preservado: {entry.RelativePath}");
                        continue;
                    }
                    var quarantined = QuarantinedPath(sessionId, entry);
                    using var fileLease = await OpenValidatedContentsAsync(quarantined, entry, cancellationToken).ConfigureAwait(false);
                    EnsureDirectory(Path.GetDirectoryName(destination)!);
                    entry.State = "Restoring";
                    await SaveAsync(manifest, cancellationToken, entry).ConfigureAwait(false);
                    CheckAncestors(destination);
                    CheckAncestors(quarantined);
                    fileLease.MoveTo(destination);
                    entry.State = "Restored";
                    restored++;
                    await SaveAsync(manifest, CancellationToken.None, entry).ConfigureAwait(false);
                }
                catch (Exception exception) when (IsFileFailure(exception))
                {
                    skipped++;
                    warnings.Add($"Não foi possível restaurar {entry.RelativePath}: {exception.Message}");
                    await ReconcileAsync(manifest, warnings, CancellationToken.None).ConfigureAwait(false);
                }
            }
            return new CleanupRestoreResult(restored, skipped, warnings.AsReadOnly());
        }
        finally { _gate.Release(); }
    }

    /// <summary>Explicit irreversible operation. Returns only the sizes of files actually deleted.</summary>
    public async Task<ulong> PermanentlyDeleteAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var storageLock = await LockStorageAsync(cancellationToken).ConfigureAwait(false);
            using var sessionScope = CleanupFileLease.PinExistingParents(Path.Combine(SessionPath(sessionId), "entry"));
            var manifest = await LoadAsync(sessionId, cancellationToken).ConfigureAwait(false);
            await ReconcileAsync(manifest, [], cancellationToken).ConfigureAwait(false);
            ulong deletedBytes = 0;
            foreach (var entry in manifest.Entries.Where(entry => entry.State == "Moved").ToArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var quarantined = QuarantinedPath(sessionId, entry);
                // A replaced, corrupt or linked object is never deleted as part of this session.
                using var fileLease = await OpenValidatedContentsAsync(quarantined, entry, cancellationToken).ConfigureAwait(false);
                entry.State = "Purging";
                await SaveAsync(manifest, cancellationToken, entry).ConfigureAwait(false);
                CheckAncestors(quarantined);
                var actualLength = fileLease.Length;
                fileLease.Delete();
                entry.State = "Purged";
                deletedBytes += (ulong)actualLength;
                await SaveAsync(manifest, CancellationToken.None, entry).ConfigureAwait(false);
            }
            return deletedBytes;
        }
        finally { _gate.Release(); }
    }

    private async Task<StorageLease> LockStorageAsync(CancellationToken cancellationToken)
    {
        var lockPath = Path.Combine(_storageRoot, "storage.lock");
        var parentScope = CleanupFileLease.PinExistingParents(lockPath);
        try
        {
            EnsureDirectory(_storageRoot);
            var fullScope = CleanupFileLease.PinExistingParents(lockPath);
            try
            {
                for (var attempt = 0; ; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    CheckAncestors(lockPath);
                    try
                    {
                        var stream = CleanupFileLease.OpenOwnedMetadataStream(lockPath);
                        return new StorageLease(stream, parentScope, fullScope);
                    }
                    catch (IOException) when (attempt < 100)
                    {
                        await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch { fullScope.Dispose(); throw; }
        }
        catch { parentScope.Dispose(); throw; }
    }

    private async Task<Manifest> LoadAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        if (sessionId == Guid.Empty) throw new ArgumentException("Sessão inválida.", nameof(sessionId));
        var path = Path.Combine(SessionPath(sessionId), "manifest.json");
        CheckAncestors(path);
        using var manifestLease = CleanupFileLease.Open(path);
        if (manifestLease.Length > MaximumManifestBytes)
            throw new InvalidDataException("Registro de recuperação ausente ou inválido.");
        var manifest = await JsonSerializer.DeserializeAsync<Manifest>(manifestLease.ReadStream, JsonOptions, cancellationToken)
            .ConfigureAwait(false) ?? throw new InvalidDataException("Registro de recuperação inválido.");
        ValidateManifest(manifest, sessionId);
        await ReadJournalAsync(manifest, cancellationToken).ConfigureAwait(false);
        ValidateManifest(manifest, sessionId);
        manifest.PersistedStates = manifest.Entries.ToDictionary(entry => entry.Id, entry => entry.State, StringComparer.Ordinal);
        return manifest;
    }

    private async Task SaveAsync(Manifest manifest, CancellationToken cancellationToken, ManifestEntry? changedEntry = null)
    {
        var directory = SessionPath(manifest.Id);
        CheckAncestors(directory);
        if (manifest.PersistedStates is not null)
        {
            if (manifest.Entries.Count != manifest.PersistedStates.Count)
                throw new InvalidDataException("Os identificadores de um registro persistido não podem mudar.");
            var inspected = changedEntry is null ? manifest.Entries : [changedEntry];
            var changes = inspected.Where(entry => entry.State != manifest.PersistedStates[entry.Id]).ToArray();
            if (changes.Length == 0) return;
            var journalPath = Path.Combine(directory, "journal.jsonl");
            CheckAncestors(journalPath);
            await using var journal = CleanupFileLease.OpenOwnedMetadataStream(journalPath);
            if (manifest.ValidJournalLength is { } length)
            {
                // A killed process may have left an incomplete final record. Only the incomplete
                // tail is discarded; validated complete records are never rewritten.
                if (journal.Length < length) throw new InvalidDataException("O journal de recuperação foi truncado.");
                journal.SetLength(length);
            }
            journal.Seek(0, SeekOrigin.End);
            foreach (var entry in changes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsKnownState(entry.State)) throw new InvalidDataException("Estado de recuperação inválido.");
                var record = new JournalRecord { Id = entry.Id, State = entry.State };
                var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record) + "\n");
                if (journal.Length + bytes.Length > MaximumManifestBytes)
                    throw new InvalidDataException("O journal excedeu seu limite de segurança.");
                // Complete and flush a record once started. Cancellation is observed between records.
                await journal.WriteAsync(bytes, CancellationToken.None).ConfigureAwait(false);
                await journal.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                journal.Flush(flushToDisk: true);
                manifest.PersistedStates[entry.Id] = entry.State;
                manifest.ValidJournalLength = journal.Length;
            }
            return;
        }
        ValidateManifest(manifest, manifest.Id);
        var temporary = Path.Combine(directory, "manifest." + Guid.NewGuid().ToString("N") + ".tmp");
        var destination = Path.Combine(directory, "manifest.json");
        CheckAncestors(temporary);
        CheckAncestors(destination);
        await using (var stream = CleanupFileLease.OpenOwnedMetadataStream(temporary, createNew: true))
        {
            await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
            CheckAncestors(destination);
            CleanupFileLease.MoveStreamTo(stream, temporary, destination);
        }
        manifest.PersistedStates = manifest.Entries.ToDictionary(entry => entry.Id, entry => entry.State, StringComparer.Ordinal);
        manifest.ValidJournalLength = 0;
    }

    private async Task ReadJournalAsync(Manifest manifest, CancellationToken cancellationToken)
    {
        var path = Path.Combine(SessionPath(manifest.Id), "journal.jsonl");
        CheckAncestors(path);
        if (!File.Exists(path)) { manifest.ValidJournalLength = 0; return; }
        using var journalLease = CleanupFileLease.Open(path);
        if (journalLease.Length > MaximumManifestBytes)
            throw new InvalidDataException("O journal de recuperação excedeu seu limite de segurança.");
        var bytes = new byte[(int)journalLease.Length];
        await journalLease.ReadStream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        var completeLength = Array.LastIndexOf(bytes, (byte)'\n') + 1;
        var entries = manifest.Entries.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
        var start = 0;
        while (start < completeLength)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var end = Array.IndexOf(bytes, (byte)'\n', start);
            if (end == start || end - start > 1024)
                throw new InvalidDataException("Registro do journal inválido.");
            var record = JsonSerializer.Deserialize<JournalRecord>(bytes.AsSpan(start, end - start), JsonOptions)
                ?? throw new InvalidDataException("Registro do journal inválido.");
            if (record.Id is null || !entries.TryGetValue(record.Id, out var entry) || !IsKnownState(record.State))
                throw new InvalidDataException("O journal referencia uma entrada ou estado desconhecido.");
            entry.State = record.State;
            start = end + 1;
        }
        manifest.ValidJournalLength = completeLength;
    }

    private async Task<int> ReconcileAsync(Manifest manifest, List<string> warnings, CancellationToken cancellationToken,
        ManifestEntry? onlyEntry = null)
    {
        var changed = false;
        var failed = 0;
        var inspected = onlyEntry is null ? manifest.Entries : [onlyEntry];
        foreach (var entry in inspected)
        {
            if (entry.State is not ("Planned" or "Restoring" or "Purging" or "Moved")) continue;
            try
            {
                var quarantined = QuarantinedPath(manifest.Id, entry);
                CheckAncestors(quarantined);
                var exists = File.Exists(quarantined);
                if (exists && entry.State != "Moved")
                {
                    await ValidateContentsAsync(quarantined, entry, cancellationToken).ConfigureAwait(false);
                    entry.State = "Moved";
                    changed = true;
                }
                else if (!exists)
                {
                    var prior = entry.State;
                    if (prior == "Purging") entry.State = "Purged";
                    else if (prior == "Planned") entry.State = "Skipped";
                    else
                    {
                        var original = SourcePath(entry.RelativePath);
                        CheckAncestors(original);
                        if (prior == "Restoring" && File.Exists(original))
                        {
                            await ValidateContentsAsync(original, entry, cancellationToken).ConfigureAwait(false);
                            entry.State = "Restored";
                        }
                        else
                        {
                            entry.State = "Missing";
                            warnings.Add($"O arquivo de recuperação está ausente: {entry.RelativePath}");
                        }
                    }
                    changed = true;
                }
                manifest.FailedEntries.Remove(entry.Id);
            }
            catch (Exception exception) when (IsFileFailure(exception))
            {
                // A damaged/locked entry must not hide other files from the same recovery
                // session. Keep its durable state intact so unlocking/repairing can be retried.
                failed++;
                manifest.FailedEntries.Add(entry.Id);
                warnings.Add($"Recuperação pendente para {entry.RelativePath}: {exception.Message}");
            }
        }
        if (changed) await SaveAsync(manifest, cancellationToken, onlyEntry).ConfigureAwait(false);
        return failed;
    }

    private void ValidateManifest(Manifest manifest, Guid expectedId)
    {
        if (manifest.Version != 1 || manifest.Id != expectedId || manifest.Id == Guid.Empty ||
            manifest.RootIdentity != _rootIdentity || manifest.Entries is null ||
            manifest.Entries.Count > MaximumEntries || manifest.CreatedAt < DateTimeOffset.UnixEpoch ||
            manifest.CreatedAt > DateTimeOffset.UtcNow.AddHours(1))
            throw new InvalidDataException("O registro não pertence a este diretório de temporários ou está inválido.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var paths = new HashSet<string>(PathComparer);
        foreach (var entry in manifest.Entries)
        {
            if (entry is null || !Guid.TryParseExact(entry.Id, "N", out var entryId) ||
                entry.Id != entryId.ToString("N") || !ids.Add(entry.Id) ||
                !paths.Add(entry.RelativePath) || entry.SizeBytes > MaximumFileBytes ||
                entry.Hash is null || entry.Hash.Length != 64 || !entry.Hash.All(Uri.IsHexDigit) ||
                !IsKnownState(entry.State))
                throw new InvalidDataException("Uma entrada do registro de recuperação é inválida.");
            ValidateRelative(entry.RelativePath);
            _ = SourcePath(entry.RelativePath);
        }
    }

    private static ulong SumBytes(IEnumerable<ManifestEntry> entries)
    {
        ulong total = 0;
        foreach (var entry in entries) total = checked(total + entry.SizeBytes);
        return total;
    }

    private static bool IsKnownState(string state) => state is
        "Planned" or "Moved" or "Skipped" or "Restoring" or "Restored" or "Purging" or "Purged" or "Missing";

    private static async Task ValidateContentsAsync(string path, ManifestEntry entry, CancellationToken cancellationToken)
    {
        using var lease = await OpenValidatedContentsAsync(path, entry, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<CleanupFileLease> OpenValidatedContentsAsync(string path, ManifestEntry entry,
        CancellationToken cancellationToken)
    {
        CheckAncestors(path);
        var lease = CleanupFileLease.Open(path);
        try
        {
            if ((ulong)lease.Length != entry.SizeBytes ||
                !string.Equals(await lease.HashAsync(cancellationToken).ConfigureAwait(false), entry.Hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("O arquivo não corresponde ao registro de recuperação; nenhuma alteração foi feita nele.");
            return lease;
        }
        catch { lease.Dispose(); throw; }
    }

    private string SessionPath(Guid sessionId) => Path.Combine(_storageRoot, sessionId.ToString("N"));

    private string QuarantinedPath(Guid sessionId, ManifestEntry entry) =>
        Path.Combine(SessionPath(sessionId), entry.Id + ".bin");

    private string SourcePath(string relative)
    {
        ValidateRelative(relative);
        var result = Path.GetFullPath(Path.Combine(_temporaryRoot, relative));
        if (!IsWithin(result, _temporaryRoot) || PathComparer.Equals(result, _temporaryRoot))
            throw new InvalidDataException("O caminho sai do diretório de temporários.");
        return result;
    }

    private static void ValidateRelative(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) ||
            relative.Contains(':') || relative.Contains('\0') ||
            relative.Split(['/', '\\']).Any(component => component is "" or "." or ".." ||
                component.EndsWith(' ') || component.EndsWith('.') || component.Any(character =>
                    character < 32 || "<>\"|?*".Contains(character)) || IsReservedName(component)))
            throw new InvalidDataException("Caminho relativo de recuperação inválido.");
        // Backslashes are treated as separators on every platform, not as a safe Unix filename.
        if (!OperatingSystem.IsWindows() && relative.Contains('\\'))
            throw new InvalidDataException("Separador de caminho inválido.");
    }

    private static bool IsReservedName(string component)
    {
        var stem = component.Split('.')[0].ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
            stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) ||
                                stem.StartsWith("LPT", StringComparison.Ordinal)) &&
            (stem[3] is >= '1' and <= '9' or '¹' or '²' or '³');
    }

    private static bool IsWithin(string path, string root) => PathComparer.Equals(path, root) ||
        path.StartsWith(root + Path.DirectorySeparatorChar, PathComparison);

    private static bool PathExists(string path)
    {
        try { _ = File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private static void CheckAncestors(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current);
             current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Links e pontos de redirecionamento são ignorados por segurança.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static void EnsureDirectory(string path)
    {
        CheckAncestors(path);
        if (Directory.Exists(path)) return;
        var parent = Path.GetDirectoryName(path);
        if (parent is not null) EnsureDirectory(parent);
        CheckAncestors(path);
        Directory.CreateDirectory(path);
        CheckAncestors(path);
    }

    private static Metadata Capture(FileInfo info) => new(info.Length, info.LastWriteTimeUtc.Ticks,
        info.CreationTimeUtc.Ticks);

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        CheckAncestors(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }

    private static bool IsFileFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidDataException;

    private sealed record Metadata(long Length, long LastWriteTicks, long CreationTicks);
    private sealed record Fingerprint(TemporaryFileCandidate Candidate, Metadata Metadata, string Hash);
    private sealed record ScanState(CleanupScan Scan, IReadOnlyDictionary<string, Fingerprint> Fingerprints);

    private sealed class Manifest
    {
        public Manifest() { }
        public int Version { get; set; } = 1;
        public Guid Id { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string RootIdentity { get; set; } = "";
        public List<ManifestEntry> Entries { get; set; } = [];
        [JsonIgnore] public Dictionary<string, string>? PersistedStates { get; set; }
        [JsonIgnore] public long? ValidJournalLength { get; set; }
        [JsonIgnore] public HashSet<string> FailedEntries { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class ManifestEntry
    {
        public ManifestEntry() { }
        public string Id { get; set; } = "";
        public string RelativePath { get; set; } = "";
        public ulong SizeBytes { get; set; }
        public string Hash { get; set; } = "";
        public string State { get; set; } = "";
    }

    private sealed class JournalRecord
    {
        public JournalRecord() { }
        public string Id { get; set; } = "";
        public string State { get; set; } = "";
    }

    private sealed class StorageLease(FileStream stream, IDisposable parentScope, IDisposable fullScope) : IDisposable
    {
        public void Dispose()
        {
            stream.Dispose();
            fullScope.Dispose();
            parentScope.Dispose();
        }
    }
}
