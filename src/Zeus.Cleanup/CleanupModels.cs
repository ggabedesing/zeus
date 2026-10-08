namespace Zeus.Cleanup;

public sealed record TemporaryFileCandidate(string Id, string RelativePath, ulong SizeBytes, DateTimeOffset LastWriteTime);

public sealed record CleanupScan(Guid Id, string RootPath, DateTimeOffset ScannedAt,
    IReadOnlyList<TemporaryFileCandidate> Files, IReadOnlyList<string> Warnings);

/// <summary>Bytes moved into recovery storage, not bytes of disk space recovered.</summary>
public sealed record CleanupResult(Guid SessionId, int MovedFiles, int SkippedFiles,
    ulong QuarantinedBytes, IReadOnlyList<string> Warnings);

public sealed record QuarantineSession(Guid Id, DateTimeOffset CreatedAt, int FileCount,
    ulong TotalBytes, string Status);

public sealed record CleanupRestoreResult(int RestoredFiles, int SkippedFiles, IReadOnlyList<string> Warnings);
