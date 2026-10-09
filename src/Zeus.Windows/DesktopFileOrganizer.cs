using System.Security.Cryptography;
using System.Text.Json;

namespace Zeus.Windows;

public sealed record DesktopOrganizationItem(string Name, string Category, long SizeBytes, string OriginalPath, string DestinationPath, string Sha256)
{
    public string SizeDescription => Zeus.Core.ByteFormatting.Format((ulong)Math.Max(0, SizeBytes));
}
public sealed record DesktopOrganizationPreview(string DesktopPath, IReadOnlyList<DesktopOrganizationItem> Items, IReadOnlyList<string> Skipped, DateTimeOffset CreatedAt)
{
    public long TotalBytes => Items.Sum(item => item.SizeBytes);
}
public sealed record DesktopOrganizationSession(Guid Id, DateTimeOffset CreatedAt, int MovedFiles, int RestoredFiles, int Conflicts, string Status)
{
    public bool CanRestore => MovedFiles > RestoredFiles;
}
public sealed record DesktopOrganizationResult(Guid SessionId, bool Succeeded, int MovedFiles, int RestoredFiles, int Conflicts, string Message);

/// <summary>Preview-first organizer for ordinary files directly on the user's desktop, with hash-checked rollback.</summary>
public sealed class DesktopFileOrganizer
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    private static readonly Dictionary<string, string> Categories = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "Imagens", [".jpg"] = "Imagens", [".jpeg"] = "Imagens", [".bmp"] = "Imagens", [".gif"] = "Imagens", [".webp"] = "Imagens",
        [".mp4"] = "Vídeos", [".mkv"] = "Vídeos", [".mov"] = "Vídeos", [".avi"] = "Vídeos", [".webm"] = "Vídeos",
        [".pdf"] = "Documentos", [".doc"] = "Documentos", [".docx"] = "Documentos", [".xls"] = "Documentos", [".xlsx"] = "Documentos", [".ppt"] = "Documentos", [".pptx"] = "Documentos", [".txt"] = "Documentos", [".csv"] = "Documentos", [".rtf"] = "Documentos",
        [".mp3"] = "Áudio", [".wav"] = "Áudio", [".flac"] = "Áudio", [".m4a"] = "Áudio", [".aac"] = "Áudio", [".ogg"] = "Áudio",
        [".zip"] = "Compactados", [".7z"] = "Compactados", [".rar"] = "Compactados", [".tar"] = "Compactados", [".gz"] = "Compactados"
    };
    private readonly string _desktopPath;
    private readonly string _sessionsPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DesktopFileOrganizer(string? desktopPath = null, string? sessionsPath = null)
    {
        var configuredDesktop = desktopPath ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(configuredDesktop))
            throw new InvalidOperationException("O Windows não informou a pasta Área de Trabalho deste usuário.");
        _desktopPath = Path.GetFullPath(configuredDesktop);
        _sessionsPath = Path.GetFullPath(sessionsPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zeus", "DesktopOrganization"));
    }

    public Task<DesktopOrganizationPreview> PreviewAsync(CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureDesktopAllowed();
        var items = new List<DesktopOrganizationItem>();
        var skipped = new List<string>();
        foreach (var path in Directory.EnumerateFileSystemEntries(_desktopPath, "*", SearchOption.TopDirectoryOnly).Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(path);
            var attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System)) != 0)
            {
                skipped.Add($"{name}: pasta, item oculto/de sistema ou link preservado.");
                continue;
            }
            if (!Categories.TryGetValue(Path.GetExtension(path), out var category))
            {
                skipped.Add($"{name}: tipo fora das categorias seguras ou atalho preservado.");
                continue;
            }
            var info = new FileInfo(path);
            if (info.Length == 0 || info.Length > 20L * 1024 * 1024 * 1024)
            {
                skipped.Add($"{name}: arquivo vazio ou acima do limite de 20 GiB.");
                continue;
            }
            var destination = Path.Combine(_desktopPath, $"ZEUS - {category}", name);
            if (File.Exists(destination))
            {
                skipped.Add($"{name}: já existe um arquivo com esse nome no destino.");
                continue;
            }
            if (items.Count >= 200) { skipped.Add($"{name}: limite de 200 itens por prévia atingido."); continue; }
            var hash = await HashAsync(path, cancellationToken);
            items.Add(new(name, category, info.Length, path, destination, hash));
        }
        return new DesktopOrganizationPreview(_desktopPath, items, skipped, DateTimeOffset.UtcNow);
    }, cancellationToken);

    public async Task<DesktopOrganizationResult> ApplyAsync(DesktopOrganizationPreview preview, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureDesktopAllowed();
            if (!PathEquals(preview.DesktopPath, _desktopPath) || preview.Items.Count == 0)
                return new(Guid.Empty, false, 0, 0, 0, "A prévia não pertence a esta Área de Trabalho ou não contém arquivos elegíveis.");
            var session = new SessionDocument { Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow, DesktopPath = _desktopPath };
            foreach (var item in preview.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsDirectChild(item.OriginalPath) || !IsDirectChild(Path.GetDirectoryName(item.DestinationPath)!) ||
                    !IsSafeRegularFile(item.OriginalPath) || File.Exists(item.DestinationPath) || new FileInfo(item.OriginalPath).Length != item.SizeBytes || await HashAsync(item.OriginalPath, cancellationToken) != item.Sha256)
                    return new(Guid.Empty, false, 0, 0, 1, $"A lista mudou desde a prévia ({item.Name}); gere outra prévia.");
                var category = Categories.GetValueOrDefault(Path.GetExtension(item.OriginalPath));
                if (category is null || !PathEquals(Path.GetDirectoryName(item.DestinationPath)!, Path.Combine(_desktopPath, $"ZEUS - {category}")))
                    return new(Guid.Empty, false, 0, 0, 1, $"Destino não permitido para {item.Name}.");
                var folder = Path.GetDirectoryName(item.DestinationPath)!;
                if (Directory.Exists(folder) && (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
                    return new(Guid.Empty, false, 0, 0, 1, $"A pasta de destino {Path.GetFileName(folder)} é um link e foi preservada.");
            }
            Directory.CreateDirectory(_sessionsPath);
            var sessionPath = GetSessionPath(session.Id);
            foreach (var item in preview.Items)
                session.Entries.Add(new Entry { OriginalPath = item.OriginalPath, DestinationPath = item.DestinationPath, Sha256 = item.Sha256 });
            await SaveAsync(sessionPath, session, cancellationToken);

            var moved = 0;
            var conflicts = 0;
            for (var index = 0; index < session.Entries.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = session.Entries[index];
                try
                {
                    var folder = Path.GetDirectoryName(entry.DestinationPath)!;
                    if (Directory.Exists(folder) && (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) throw new IOException("pasta de destino virou link");
                    Directory.CreateDirectory(folder);
                    if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) throw new IOException("pasta de destino virou link durante a operação");
                    if (!IsSafeRegularFile(entry.OriginalPath) || File.Exists(entry.DestinationPath) || await HashAsync(entry.OriginalPath, cancellationToken) != entry.Sha256)
                        throw new IOException("arquivo mudou desde a prévia");
                    entry.State = "Applying";
                    await SaveAsync(sessionPath, session, CancellationToken.None);
                    File.Move(entry.OriginalPath, entry.DestinationPath);
                    entry.State = "Applied";
                    await SaveAsync(sessionPath, session, CancellationToken.None);
                    moved++;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    entry.Error = error.Message;
                    if (entry.State == "Applying") entry.State = "NeedsReview";
                    conflicts++;
                    await SaveAsync(sessionPath, session, CancellationToken.None);
                }
            }
            session.Status = conflicts == 0 ? "Applied" : moved == 0 ? "NeedsReview" : "PartiallyApplied";
            await SaveAsync(sessionPath, session, CancellationToken.None);
            return new(session.Id, moved > 0, moved, 0, conflicts, moved == preview.Items.Count
                ? $"{moved} arquivo(s) organizados. O histórico permite restaurar cada um após verificar hash e conflito."
                : $"{moved} arquivo(s) organizados; {conflicts} conflito(s) preservado(s). Revise o histórico antes de repetir.");
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<DesktopOrganizationSession>> ListSessionsAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_sessionsPath)) return [];
        var result = new List<DesktopOrganizationSession>();
        foreach (var file in Directory.EnumerateFiles(_sessionsPath, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = await ReadAsync(file, cancellationToken);
            await ReconcileAsync(file, session, cancellationToken);
            result.Add(new(session.Id, session.CreatedAt, session.Entries.Count(entry => entry.State is "Applied" or "Restoring"), session.Entries.Count(entry => entry.State == "Restored"), session.Entries.Count(entry => entry.State == "NeedsReview"), session.Status));
        }
        return result.OrderByDescending(item => item.CreatedAt).ToArray();
    }

    public async Task<DesktopOrganizationResult> RestoreAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureDesktopAllowed();
            if (id == Guid.Empty) return new(id, false, 0, 0, 1, "Sessão inválida.");
            var path = GetSessionPath(id);
            var session = await ReadAsync(path, cancellationToken);
            await ReconcileAsync(path, session, cancellationToken);
            if (!PathEquals(session.DesktopPath, _desktopPath)) return new(id, false, 0, 0, 1, "A sessão pertence a outra Área de Trabalho.");
            var restored = 0;
            var conflicts = 0;
            foreach (var entry in session.Entries.Where(entry => entry.State is "Applied" or "Restoring"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (!IsDirectChild(entry.OriginalPath) || !IsAllowedDestination(entry) || !IsSafeRegularFile(entry.DestinationPath) || File.Exists(entry.OriginalPath) || await HashAsync(entry.DestinationPath, cancellationToken) != entry.Sha256)
                        throw new IOException("o arquivo mudou ou o caminho original está ocupado");
                    entry.State = "Restoring";
                    await SaveAsync(path, session, CancellationToken.None);
                    File.Move(entry.DestinationPath, entry.OriginalPath);
                    entry.State = "Restored";
                    await SaveAsync(path, session, CancellationToken.None);
                    restored++;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    entry.Error = error.Message;
                    entry.State = "NeedsReview";
                    conflicts++;
                    await SaveAsync(path, session, CancellationToken.None);
                }
            }
            session.Status = conflicts > 0 ? "RestoreBlocked" : "Restored";
            await SaveAsync(path, session, CancellationToken.None);
            return new(id, restored > 0 || conflicts == 0, 0, restored, conflicts, conflicts == 0
                ? $"{restored} arquivo(s) restaurados e verificados."
                : $"{restored} arquivo(s) restaurados; {conflicts} conflito(s) preservado(s). Nenhum arquivo foi sobrescrito.");
        }
        finally { _gate.Release(); }
    }

    private void EnsureDesktopAllowed()
    {
        if (!Directory.Exists(_desktopPath)) throw new DirectoryNotFoundException("A Área de Trabalho deste usuário não está disponível.");
        if ((File.GetAttributes(_desktopPath) & FileAttributes.ReparsePoint) != 0) throw new IOException("A pasta Área de Trabalho é um link; organização automática foi bloqueada.");
        foreach (var name in new[] { "OneDrive", "OneDriveCommercial", "OneDriveConsumer" })
        {
            var cloudRoot = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(cloudRoot) && IsWithin(_desktopPath, Path.GetFullPath(cloudRoot)))
                throw new IOException("A Área de Trabalho está dentro de uma pasta OneDrive conhecida. A organização automática foi bloqueada para preservar a sincronização.");
        }
    }

    private async Task ReconcileAsync(string path, SessionDocument session, CancellationToken token)
    {
        var changed = false;
        foreach (var entry in session.Entries.Where(entry => entry.State is "Applying" or "Restoring"))
        {
            token.ThrowIfCancellationRequested();
            if (!IsDirectChild(entry.OriginalPath) || !IsAllowedDestination(entry) ||
                (File.Exists(entry.OriginalPath) && !IsSafeRegularFile(entry.OriginalPath)) ||
                (File.Exists(entry.DestinationPath) && !IsSafeRegularFile(entry.DestinationPath)))
            { entry.State = "NeedsReview"; changed = true; continue; }
            var sourceExists = File.Exists(entry.OriginalPath);
            var destinationExists = File.Exists(entry.DestinationPath);
            if (!sourceExists && destinationExists && await HashAsync(entry.DestinationPath, token) == entry.Sha256)
                entry.State = "Applied";
            else if (sourceExists && !destinationExists && await HashAsync(entry.OriginalPath, token) == entry.Sha256)
                entry.State = entry.State == "Restoring" ? "Restored" : "Prepared";
            else entry.State = "NeedsReview";
            changed = true;
        }
        var reviewed = session.Entries.Count(entry => entry.State == "NeedsReview");
        var applied = session.Entries.Count(entry => entry.State == "Applied");
        var restored = session.Entries.Count(entry => entry.State == "Restored");
        var prepared = session.Entries.Count(entry => entry.State == "Prepared");
        var status = reviewed > 0 ? "NeedsReview" : applied > 0 && restored > 0 ? "PartiallyRestored" :
            applied > 0 && prepared > 0 ? "PartiallyApplied" : applied > 0 ? "Applied" :
            restored == session.Entries.Count ? "Restored" : "Prepared";
        if (session.Status != status) { session.Status = status; changed = true; }
        if (changed) await SaveAsync(path, session, CancellationToken.None);
    }

    private bool IsDirectChild(string path) => PathEquals(Path.GetDirectoryName(Path.GetFullPath(path))!, _desktopPath);
    private static bool IsSafeRegularFile(string path)
    {
        if (!File.Exists(path)) return false;
        var attributes = File.GetAttributes(path);
        return (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System)) == 0;
    }
    private bool IsAllowedDestination(Entry entry)
    {
        var category = Categories.GetValueOrDefault(Path.GetExtension(entry.OriginalPath));
        if (category is null || !string.Equals(Path.GetFileName(entry.OriginalPath), Path.GetFileName(entry.DestinationPath), StringComparison.Ordinal)) return false;
        var expected = Path.Combine(_desktopPath, $"ZEUS - {category}");
        if (!PathEquals(Path.GetDirectoryName(Path.GetFullPath(entry.DestinationPath))!, expected)) return false;
        return !Directory.Exists(expected) || (File.GetAttributes(expected) & FileAttributes.ReparsePoint) == 0;
    }
    private string GetSessionPath(Guid id) => Path.Combine(_sessionsPath, id.ToString("N") + ".json");
    private static bool IsWithin(string path, string root) => path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || PathEquals(path, root);
    private static bool PathEquals(string left, string right) => string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    private static async Task<string> HashAsync(string path, CancellationToken token) { await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan); return Convert.ToHexString(await SHA256.HashDataAsync(stream, token)); }
    private static async Task SaveAsync(string path, SessionDocument value, CancellationToken token)
    {
        var temp = path + ".tmp";
        await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
        { await JsonSerializer.SerializeAsync(stream, value, JsonOptions, token); await stream.FlushAsync(token); stream.Flush(true); }
        File.Move(temp, path, true);
    }
    private static async Task<SessionDocument> ReadAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, FileOptions.Asynchronous);
        return await JsonSerializer.DeserializeAsync<SessionDocument>(stream, JsonOptions, token)
            ?? throw new InvalidDataException("O histórico da organização está vazio ou inválido.");
    }

    private sealed class SessionDocument { public Guid Id { get; set; } public DateTimeOffset CreatedAt { get; set; } public string DesktopPath { get; set; } = ""; public string Status { get; set; } = "Prepared"; public List<Entry> Entries { get; set; } = []; }
    private sealed class Entry { public string OriginalPath { get; set; } = ""; public string DestinationPath { get; set; } = ""; public string Sha256 { get; set; } = ""; public string State { get; set; } = "Prepared"; public string? Error { get; set; } }
}
