using System.Text.Json.Nodes;
using System.Runtime.InteropServices;
using Zeus.Cleanup;

namespace Zeus.Cleanup.Tests;

public sealed class TemporaryFileCleanupTests : IDisposable
{
    private readonly string _fixture = Path.Combine(Path.GetTempPath(), "zeus-cleanup-tests-" + Guid.NewGuid().ToString("N"));
    private string TemporaryRoot => Path.Combine(_fixture, "temporary");
    private string StorageRoot => Path.Combine(_fixture, "recovery");

    public TemporaryFileCleanupTests() => Directory.CreateDirectory(TemporaryRoot);

    [Fact]
    public async Task ScanIncludesOnlyOldBoundedRegularFiles()
    {
        var old = WriteOld("nested/eligible.tmp", "old data");
        File.WriteAllText(Path.Combine(TemporaryRoot, "young.tmp"), "recent data");
        var oversized = WriteOld("oversized.tmp", "");
        using (var stream = new FileStream(oversized, FileMode.Open, FileAccess.Write))
            stream.SetLength(128L * 1024 * 1024 + 1);
        File.SetLastWriteTimeUtc(oversized, DateTime.UtcNow.AddDays(-8));

        var scan = await Create().ScanAsync();

        var file = Assert.Single(scan.Files);
        Assert.Equal(Path.GetRelativePath(TemporaryRoot, old), file.RelativePath);
        Assert.Equal(8UL, file.SizeBytes);
        Assert.False(Directory.Exists(StorageRoot));
    }

    [Fact]
    public async Task QuarantinePreservesBytesAndFreshInstanceCanRestoreNestedFile()
    {
        var original = WriteOld("nested/recover.tmp", "preserve me");
        var cleanup = Create();
        var scan = await cleanup.ScanAsync();

        var result = await cleanup.QuarantineAsync(scan, scan.Files.Select(file => file.Id).ToArray());

        AssertSessionFiles(result, scan.Files.Count);
        Assert.Equal(1, result.MovedFiles);
        Assert.Equal(0, result.SkippedFiles);
        Assert.Equal(11UL, result.QuarantinedBytes);
        Assert.False(File.Exists(original));
        var stored = Assert.Single(Directory.EnumerateFiles(SessionPath(result.SessionId), "*.bin"));
        Assert.Equal("preserve me", await File.ReadAllTextAsync(stored));
        Assert.Equal(11UL, Assert.Single(await Create().ListSessionsAsync()).TotalBytes);
        Directory.Delete(Path.GetDirectoryName(original)!);

        var restore = await Create().RestoreAsync(result.SessionId);

        Assert.Equal(1, restore.RestoredFiles);
        Assert.Equal("preserve me", await File.ReadAllTextAsync(original));
        Assert.Empty(Directory.EnumerateFiles(SessionPath(result.SessionId), "*.bin"));
        Assert.Equal("Restored", Assert.Single(await Create().ListSessionsAsync()).Status);
    }

    [Fact]
    public async Task PermanentDeletionReportsOnlyActualBytesAndLeavesUnownedFilesAlone()
    {
        WriteOld("discard.tmp", "delete me");
        var cleanup = Create();
        var result = await QuarantineEverything(cleanup);
        var unowned = Path.Combine(SessionPath(result.SessionId), "unmanaged.txt");
        await File.WriteAllTextAsync(unowned, "must survive");

        Assert.Equal(9UL, await cleanup.PermanentlyDeleteAsync(result.SessionId));
        Assert.Equal(0UL, await cleanup.PermanentlyDeleteAsync(result.SessionId));
        Assert.Equal("must survive", await File.ReadAllTextAsync(unowned));
        Assert.Equal("Deleted", Assert.Single(await cleanup.ListSessionsAsync()).Status);
        Assert.Equal(0, (await cleanup.RestoreAsync(result.SessionId)).RestoredFiles);
    }

    [Fact]
    public async Task StaleFileIsSkippedEvenWhenSizeAndModifiedTimeMatchScan()
    {
        var original = WriteOld("changed.tmp", "first");
        var cleanup = Create();
        var scan = await cleanup.ScanAsync();
        var previousTime = File.GetLastWriteTimeUtc(original);
        await File.WriteAllTextAsync(original, "other");
        File.SetLastWriteTimeUtc(original, previousTime);

        var result = await cleanup.QuarantineAsync(scan, [scan.Files[0].Id]);

        Assert.Equal(0, result.MovedFiles);
        Assert.Equal(1, result.SkippedFiles);
        Assert.Equal(0UL, result.QuarantinedBytes);
        Assert.Equal("other", await File.ReadAllTextAsync(original));
    }

    [Fact]
    public async Task UnknownAndRepeatedSelectionsAreRejectedBeforeAnyMove()
    {
        var original = WriteOld("keep.tmp", "keep");
        var cleanup = Create();
        var scan = await cleanup.ScanAsync();
        var id = scan.Files[0].Id;

        await Assert.ThrowsAsync<ArgumentException>(() => cleanup.QuarantineAsync(scan, ["unknown"]));
        await Assert.ThrowsAsync<ArgumentException>(() => cleanup.QuarantineAsync(scan, [id, id]));
        await Assert.ThrowsAsync<ArgumentException>(() => cleanup.QuarantineAsync(scan, []));

        Assert.True(File.Exists(original));
        Assert.False(Directory.Exists(StorageRoot));
    }

    [Fact]
    public async Task AlteredScanAndForeignInstanceAreRejected()
    {
        var original = WriteOld("keep.tmp", "keep");
        var cleanup = Create();
        var scan = await cleanup.ScanAsync();
        var tampered = scan with { Files = [scan.Files[0] with { RelativePath = "../outside.txt" }] };

        await Assert.ThrowsAsync<ArgumentException>(() => cleanup.QuarantineAsync(tampered, [scan.Files[0].Id]));
        await Assert.ThrowsAsync<ArgumentException>(() => Create().QuarantineAsync(scan, [scan.Files[0].Id]));

        Assert.True(File.Exists(original));
    }

    [Fact]
    public async Task RestoreNeverOverwritesANewDestinationFile()
    {
        var original = WriteOld("collision.tmp", "old content");
        var cleanup = Create();
        var result = await QuarantineEverything(cleanup);
        await File.WriteAllTextAsync(original, "new content");

        var restore = await cleanup.RestoreAsync(result.SessionId);

        Assert.Equal(0, restore.RestoredFiles);
        Assert.Equal(1, restore.SkippedFiles);
        Assert.Equal("new content", await File.ReadAllTextAsync(original));
        Assert.Equal(11UL, Assert.Single(await cleanup.ListSessionsAsync()).TotalBytes);
        Assert.Single(Directory.EnumerateFiles(SessionPath(result.SessionId), "*.bin"));
    }

    [Fact]
    public async Task CorruptQuarantineIsNeitherRestoredNorDeleted()
    {
        var original = WriteOld("corrupt.tmp", "original");
        var cleanup = Create();
        var result = await QuarantineEverything(cleanup);
        var stored = Assert.Single(Directory.EnumerateFiles(SessionPath(result.SessionId), "*.bin"));
        await File.WriteAllTextAsync(stored, "tampered");

        var restore = await cleanup.RestoreAsync(result.SessionId);

        Assert.Equal(0, restore.RestoredFiles);
        Assert.Equal(1, restore.SkippedFiles);
        Assert.False(File.Exists(original));
        await Assert.ThrowsAsync<InvalidDataException>(() => cleanup.PermanentlyDeleteAsync(result.SessionId));
        Assert.Equal("tampered", await File.ReadAllTextAsync(stored));
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("folder/../../outside.txt")]
    [InlineData("C:\\outside.txt")]
    [InlineData("/outside.txt")]
    [InlineData("folder/./bad.txt")]
    [InlineData("folder/bad.txt.")]
    [InlineData("CON.txt")]
    [InlineData("folder/LPT1")]
    [InlineData("bad?.tmp")]
    public async Task UnsafePersistedRelativePathIsRejected(string relativePath)
    {
        WriteOld("old.tmp", "keep");
        var outside = Path.Combine(_fixture, "outside.txt");
        await File.WriteAllTextAsync(outside, "outside must survive");
        var cleanup = Create();
        var result = await QuarantineEverything(cleanup);
        await EditManifest(result.SessionId, manifest => Entry(manifest)["RelativePath"] = relativePath);

        await Assert.ThrowsAsync<InvalidDataException>(() => cleanup.RestoreAsync(result.SessionId));
        await Assert.ThrowsAsync<InvalidDataException>(() => cleanup.PermanentlyDeleteAsync(result.SessionId));
        Assert.Empty(await cleanup.ListSessionsAsync());
        Assert.Equal("outside must survive", await File.ReadAllTextAsync(outside));
    }

    [Fact]
    public async Task SessionCannotBeReboundToAnArbitrarySourceRoot()
    {
        WriteOld("old.tmp", "keep");
        var result = await QuarantineEverything(Create());
        var otherRoot = Path.Combine(_fixture, "other-temp");
        Directory.CreateDirectory(otherRoot);

        var otherCleanup = new TemporaryFileCleanup(otherRoot, StorageRoot);

        await Assert.ThrowsAsync<InvalidDataException>(() => otherCleanup.RestoreAsync(result.SessionId));
        await Assert.ThrowsAsync<InvalidDataException>(() => otherCleanup.PermanentlyDeleteAsync(result.SessionId));
        Assert.Empty(await otherCleanup.ListSessionsAsync());
    }

    [Fact]
    public async Task CorruptAndUnknownManifestFieldsAreRejected()
    {
        WriteOld("old.tmp", "keep");
        var cleanup = Create();
        var result = await QuarantineEverything(cleanup);
        await EditManifest(result.SessionId, manifest => manifest["OriginalRoot"] = Path.GetPathRoot(_fixture));

        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => cleanup.RestoreAsync(result.SessionId));
        Assert.Empty(await cleanup.ListSessionsAsync());
    }

    [Fact]
    public async Task InterruptedMoveIntentIsReconciledAndRemainsRecoverable()
    {
        var original = WriteOld("recover.tmp", "recover after crash");
        var result = await QuarantineEverything(Create());
        await EditManifest(result.SessionId, manifest => Entry(manifest)["State"] = "Planned");

        var cleanupAfterRestart = Create();
        Assert.Equal("Quarantined", Assert.Single(await cleanupAfterRestart.ListSessionsAsync()).Status);
        Assert.Equal(1, (await cleanupAfterRestart.RestoreAsync(result.SessionId)).RestoredFiles);
        Assert.Equal("recover after crash", await File.ReadAllTextAsync(original));
    }

    [Fact]
    public async Task InterruptedRestoreIntentIsReconciledWithoutMovingOrDeletingRestoredFile()
    {
        var original = WriteOld("recover.tmp", "recovered");
        var result = await QuarantineEverything(Create());
        File.Move(Assert.Single(Directory.EnumerateFiles(SessionPath(result.SessionId), "*.bin")), original);
        await EditManifest(result.SessionId, manifest => Entry(manifest)["State"] = "Restoring");

        var cleanupAfterRestart = Create();
        Assert.Equal("Restored", Assert.Single(await cleanupAfterRestart.ListSessionsAsync()).Status);
        Assert.Equal(0, (await cleanupAfterRestart.RestoreAsync(result.SessionId)).RestoredFiles);
        Assert.Equal(0UL, await cleanupAfterRestart.PermanentlyDeleteAsync(result.SessionId));
        Assert.Equal("recovered", await File.ReadAllTextAsync(original));
    }

    [Fact]
    public async Task InterruptedDeletionIsNotCountedTwice()
    {
        WriteOld("discard.tmp", "discard");
        var result = await QuarantineEverything(Create());
        File.Delete(Assert.Single(Directory.EnumerateFiles(SessionPath(result.SessionId), "*.bin")));
        await EditManifest(result.SessionId, manifest => Entry(manifest)["State"] = "Purging");

        var cleanupAfterRestart = Create();
        Assert.Equal("Deleted", Assert.Single(await cleanupAfterRestart.ListSessionsAsync()).Status);
        Assert.Equal(0UL, await cleanupAfterRestart.PermanentlyDeleteAsync(result.SessionId));
    }

    [Fact]
    public async Task IncompleteLastJournalRecordIsDiscardedWhileEarlierRecordsRemainRecoverable()
    {
        var original = WriteOld("recover.tmp", "recover");
        var result = await QuarantineEverything(Create());
        var journal = Path.Combine(SessionPath(result.SessionId), "journal.jsonl");
        await File.AppendAllTextAsync(journal, "{\"Id\":\"partial");

        var cleanupAfterRestart = Create();

        Assert.Equal("Quarantined", Assert.Single(await cleanupAfterRestart.ListSessionsAsync()).Status);
        Assert.Equal(1, (await cleanupAfterRestart.RestoreAsync(result.SessionId)).RestoredFiles);
        Assert.Equal("recover", await File.ReadAllTextAsync(original));
        Assert.DoesNotContain("partial", await File.ReadAllTextAsync(journal));
    }

    [Fact]
    public async Task CompleteJournalRecordWithUnknownIdentifierIsRejected()
    {
        WriteOld("old.tmp", "keep");
        var result = await QuarantineEverything(Create());
        var journal = Path.Combine(SessionPath(result.SessionId), "journal.jsonl");
        await File.AppendAllTextAsync(journal, "{\"Id\":\"unknown\",\"State\":\"Purged\"}\n");

        var cleanup = Create();

        await Assert.ThrowsAsync<InvalidDataException>(() => cleanup.RestoreAsync(result.SessionId));
        await Assert.ThrowsAsync<InvalidDataException>(() => cleanup.PermanentlyDeleteAsync(result.SessionId));
        Assert.Empty(await cleanup.ListSessionsAsync());
        Assert.Single(Directory.EnumerateFiles(SessionPath(result.SessionId), "*.bin"));
    }

    [Fact]
    public async Task SourceFileAndDirectoryLinksAreExcludedWithoutTouchingTheirTargets()
    {
        var outside = Path.Combine(_fixture, "outside");
        Directory.CreateDirectory(outside);
        var outsideFile = Path.Combine(outside, "outside.tmp");
        await File.WriteAllTextAsync(outsideFile, "external");
        File.SetLastWriteTimeUtc(outsideFile, DateTime.UtcNow.AddDays(-8));
        File.CreateSymbolicLink(Path.Combine(TemporaryRoot, "linked.tmp"), outsideFile);
        Directory.CreateSymbolicLink(Path.Combine(TemporaryRoot, "linked-directory"), outside);

        var scan = await Create().ScanAsync();

        Assert.Empty(scan.Files);
        Assert.NotEmpty(scan.Warnings);
        Assert.Equal("external", await File.ReadAllTextAsync(outsideFile));
    }

    [Fact]
    public void SymlinkRootsAndOverlappingRootsAreRejected()
    {
        var link = Path.Combine(_fixture, "temporary-link");
        Directory.CreateSymbolicLink(link, TemporaryRoot);
        Assert.Throws<InvalidDataException>(() => new TemporaryFileCleanup(link, StorageRoot));
        Assert.Throws<InvalidDataException>(() => new TemporaryFileCleanup(TemporaryRoot, link));
        Assert.Throws<ArgumentException>(() => new TemporaryFileCleanup(TemporaryRoot, TemporaryRoot));
        Assert.Throws<ArgumentException>(() => new TemporaryFileCleanup(TemporaryRoot, Path.Combine(TemporaryRoot, "recovery")));
        Assert.Throws<ArgumentException>(() => new TemporaryFileCleanup(TemporaryRoot, _fixture));
    }

    [Fact]
    public async Task SourceDirectoryReplacedBySymlinkAfterScanIsSkipped()
    {
        WriteOld("nested/old.tmp", "old");
        var cleanup = Create();
        var scan = await cleanup.ScanAsync();
        var movedDirectory = Path.Combine(_fixture, "external");
        Directory.Move(Path.Combine(TemporaryRoot, "nested"), movedDirectory);
        Directory.CreateSymbolicLink(Path.Combine(TemporaryRoot, "nested"), movedDirectory);

        var result = await cleanup.QuarantineAsync(scan, [scan.Files[0].Id]);

        Assert.Equal(0, result.MovedFiles);
        Assert.Equal(1, result.SkippedFiles);
        Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(movedDirectory, "old.tmp")));
    }

    [Fact]
    public async Task RestoreDoesNotFollowASymlinkDestinationDirectory()
    {
        WriteOld("nested/old.tmp", "old");
        var cleanup = Create();
        var result = await QuarantineEverything(cleanup);
        Directory.Delete(Path.Combine(TemporaryRoot, "nested"));
        var outside = Path.Combine(_fixture, "outside");
        Directory.CreateDirectory(outside);
        Directory.CreateSymbolicLink(Path.Combine(TemporaryRoot, "nested"), outside);

        var restored = await cleanup.RestoreAsync(result.SessionId);

        Assert.Equal(0, restored.RestoredFiles);
        Assert.Equal(1, restored.SkippedFiles);
        Assert.Empty(Directory.EnumerateFiles(outside));
        Assert.Single(Directory.EnumerateFiles(SessionPath(result.SessionId), "*.bin"));
    }

    [Fact]
    public async Task ReplacedSessionDirectoryIsNeverUsedForRestoreOrPurge()
    {
        WriteOld("old.tmp", "keep");
        var cleanup = Create();
        var result = await QuarantineEverything(cleanup);
        var destination = Path.Combine(_fixture, "outside-session");
        Directory.Move(SessionPath(result.SessionId), destination);
        Directory.CreateSymbolicLink(SessionPath(result.SessionId), destination);

        await Assert.ThrowsAsync<InvalidDataException>(() => cleanup.RestoreAsync(result.SessionId));
        await Assert.ThrowsAsync<InvalidDataException>(() => cleanup.PermanentlyDeleteAsync(result.SessionId));
        Assert.Empty(await cleanup.ListSessionsAsync());
        Assert.Single(Directory.EnumerateFiles(destination, "*.bin"));
    }

    [Fact]
    public async Task CanceledScanDoesNotWriteRecoveryStorage()
    {
        WriteOld("old.tmp", "keep");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create().ScanAsync(cancellation.Token));
        Assert.False(Directory.Exists(StorageRoot));
    }

    [Fact]
    public async Task LockedFileIsSkippedWhileAnotherSelectedFileIsStillQuarantined()
    {
        var locked = WriteOld("locked.tmp", "busy");
        var available = WriteOld("available.tmp", "available");
        var cleanup = Create();
        var scan = await cleanup.ScanAsync();
        using var exclusive = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var result = await cleanup.QuarantineAsync(scan, scan.Files.Select(file => file.Id).ToArray());

        AssertSessionFiles(result, 1);
        Assert.Equal(1, result.SkippedFiles);
        Assert.Equal(1, result.MovedFiles);
        Assert.Equal(9UL, result.QuarantinedBytes);
        Assert.True(File.Exists(locked));
        Assert.False(File.Exists(available));
        Assert.Equal(1, Assert.Single(await Create().ListSessionsAsync()).FileCount);
    }

    [Fact]
    public async Task InterruptedIntentBeforeMoveLeavesOriginalFileUntouched()
    {
        var original = WriteOld("original.tmp", "original");
        var result = await QuarantineEverything(Create());
        File.Move(Assert.Single(Directory.EnumerateFiles(SessionPath(result.SessionId), "*.bin")), original);
        await EditManifest(result.SessionId, manifest => Entry(manifest)["State"] = "Planned");

        var cleanupAfterRestart = Create();

        Assert.Equal(0, Assert.Single(await cleanupAfterRestart.ListSessionsAsync()).FileCount);
        Assert.Equal(0UL, await cleanupAfterRestart.PermanentlyDeleteAsync(result.SessionId));
        Assert.Equal("original", await File.ReadAllTextAsync(original));
    }

    [Fact]
    public async Task RecoveryStorageChangedIntoLinkAfterConstructionIsRejected()
    {
        WriteOld("old.tmp", "keep");
        var cleanup = Create();
        var scan = await cleanup.ScanAsync();
        var outside = Path.Combine(_fixture, "outside-storage");
        Directory.CreateDirectory(outside);
        Directory.CreateSymbolicLink(StorageRoot, outside);

        await Assert.ThrowsAsync<InvalidDataException>(() => cleanup.QuarantineAsync(scan, [scan.Files[0].Id]));
        Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
    }

    [Fact]
    public async Task LockedInterruptedEntryDoesNotHideOrBlockOtherRecoverableFiles()
    {
        var blockedOriginal = WriteOld("blocked.tmp", "blocked");
        var availableOriginal = WriteOld("available.tmp", "available");
        var result = await QuarantineEverything(Create());
        await EditManifest(result.SessionId, manifest => EntryByPath(manifest, "blocked.tmp")["State"] = "Restoring");
        var storedBlocked = await StoredFileFor(result.SessionId, "blocked.tmp");
        using (var locked = new FileStream(storedBlocked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var cleanup = Create();
            var session = Assert.Single(await cleanup.ListSessionsAsync());
            Assert.Equal(1, session.FileCount);
            Assert.Equal(9UL, session.TotalBytes);

            var restored = await cleanup.RestoreAsync(result.SessionId);

            Assert.Equal(1, restored.RestoredFiles);
            Assert.Equal(1, restored.SkippedFiles);
            Assert.NotEmpty(restored.Warnings);
            Assert.Equal("available", await File.ReadAllTextAsync(availableOriginal));
            Assert.False(File.Exists(blockedOriginal));
            Assert.True(File.Exists(storedBlocked));
        }
        Assert.Equal(1, (await Create().RestoreAsync(result.SessionId)).RestoredFiles);
        Assert.Equal("blocked", await File.ReadAllTextAsync(blockedOriginal));
    }

    [Fact]
    public async Task CorruptInterruptedEntryDoesNotBlockOtherRecoverableFiles()
    {
        var brokenOriginal = WriteOld("broken.tmp", "original");
        var availableOriginal = WriteOld("available.tmp", "available");
        var result = await QuarantineEverything(Create());
        await EditManifest(result.SessionId, manifest => EntryByPath(manifest, "broken.tmp")["State"] = "Planned");
        var corruptFile = await StoredFileFor(result.SessionId, "broken.tmp");
        await File.WriteAllTextAsync(corruptFile, "tampered");

        var cleanup = Create();
        Assert.Equal(1, Assert.Single(await cleanup.ListSessionsAsync()).FileCount);
        var restored = await cleanup.RestoreAsync(result.SessionId);

        Assert.Equal(1, restored.RestoredFiles);
        Assert.Equal(1, restored.SkippedFiles);
        Assert.NotEmpty(restored.Warnings);
        Assert.False(File.Exists(brokenOriginal));
        Assert.Equal("available", await File.ReadAllTextAsync(availableOriginal));
        Assert.Equal("tampered", await File.ReadAllTextAsync(corruptFile));
        Assert.Equal("Incomplete", Assert.Single(await cleanup.ListSessionsAsync()).Status);
    }

    [Fact]
    public async Task ExclusiveLeasePreventsConcurrentContentReplacementDuringChecksumAndMove()
    {
        var original = WriteOld("exclusive.tmp", "must remain unchanged");
        var destination = Path.Combine(TemporaryRoot, "moved.tmp");
        using (var lease = CleanupFileLease.Open(original))
        {
            var expectedHash = await lease.HashAsync(CancellationToken.None);
            await Assert.ThrowsAsync<IOException>(() => File.WriteAllTextAsync(original, "replacement"));
            Assert.Equal(expectedHash, await lease.HashAsync(CancellationToken.None));
            lease.MoveTo(destination);
        }

        Assert.False(File.Exists(original));
        Assert.Equal("must remain unchanged", await File.ReadAllTextAsync(destination));
    }

    [Fact]
    public async Task ExclusiveLeaseDeletesTheSameFileThatWasChecksummed()
    {
        var original = WriteOld("exclusive.tmp", "must remain unchanged");
        using var lease = CleanupFileLease.Open(original);
        Assert.Equal(21, lease.Length);
        Assert.NotEmpty(await lease.HashAsync(CancellationToken.None));
        await Assert.ThrowsAsync<IOException>(() => File.WriteAllTextAsync(original, "replacement"));

        lease.Delete();

        Assert.False(File.Exists(original));
    }

    [WindowsFact]
    public void WindowsExtendedPathAliasesCannotHideRootOverlap()
    {
        var extendedTemporary = @"\\?\" + TemporaryRoot;
        var nestedStorage = Path.Combine(TemporaryRoot, "recovery");
        Assert.Throws<ArgumentException>(() => new TemporaryFileCleanup(TemporaryRoot, @"\\?\" + nestedStorage));
        Assert.Throws<ArgumentException>(() => new TemporaryFileCleanup(extendedTemporary, nestedStorage));
        Assert.Throws<ArgumentException>(() => new TemporaryFileCleanup(extendedTemporary, TemporaryRoot));
    }

    [WindowsFact]
    public async Task WindowsFileAndParentDirectoryCannotBeReplacedWhileLeaseIsHeld()
    {
        var original = WriteOld("pinned/original.tmp", "pinned");
        var parent = Path.GetDirectoryName(original)!;
        var replacementParent = Path.Combine(TemporaryRoot, "renamed-parent");
        using (var lease = CleanupFileLease.Open(original))
        {
            Assert.Throws<IOException>(() => File.Move(original, original + ".other"));
            Assert.Throws<IOException>(() => File.Delete(original));
            Assert.Throws<IOException>(() => Directory.Move(parent, replacementParent));
            Assert.NotEmpty(await lease.HashAsync(CancellationToken.None));
        }
        Assert.Equal("pinned", await File.ReadAllTextAsync(original));
        Directory.Move(parent, replacementParent);
        Assert.True(Directory.Exists(replacementParent));
    }

    [WindowsFact]
    public async Task WindowsHardLinkedSourceIsPreserved()
    {
        var original = WriteOld("linked.tmp", "original document");
        var outside = Path.Combine(_fixture, "document.txt");
        Assert.True(CreateHardLinkW(outside, original, IntPtr.Zero));

        var cleanup = Create();
        var scan = await cleanup.ScanAsync();
        var result = await cleanup.QuarantineAsync(scan, [Assert.Single(scan.Files).Id]);

        Assert.Equal(0, result.MovedFiles);
        Assert.Equal(1, result.SkippedFiles);
        Assert.Equal("original document", await File.ReadAllTextAsync(original));
        Assert.Equal("original document", await File.ReadAllTextAsync(outside));
    }

    [WindowsFact]
    public async Task WindowsMetadataLinksCannotModifyExternalFiles()
    {
        var outside = Path.Combine(_fixture, "document.txt");
        await File.WriteAllTextAsync(outside, "original document");
        var symbolic = Path.Combine(TemporaryRoot, "metadata-link.json");
        var hard = Path.Combine(TemporaryRoot, "metadata-hard.json");
        File.CreateSymbolicLink(symbolic, outside);
        Assert.True(CreateHardLinkW(hard, outside, IntPtr.Zero));

        Assert.Throws<InvalidDataException>(() => CleanupFileLease.OpenOwnedMetadataStream(symbolic));
        Assert.Throws<InvalidDataException>(() => CleanupFileLease.OpenOwnedMetadataStream(hard));

        Assert.Equal("original document", await File.ReadAllTextAsync(outside));
    }

    [WindowsFact]
    public async Task WindowsRepeatedCrossDirectoryRenamesPreserveExactLongUnicodeNames()
    {
        Directory.CreateDirectory(StorageRoot);
        for (var index = 0; index < 24; index++)
        {
            var original = WriteOld($"source-{index}.tmp", $"contents-{index}");
            var session = Path.Combine(StorageRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(session);
            var destination = Path.Combine(session, Guid.NewGuid().ToString("N") + "-recuperação.bin");
            using (var lease = CleanupFileLease.Open(original))
            {
                Assert.NotEmpty(await lease.HashAsync(CancellationToken.None));
                lease.MoveTo(destination);
            }

            Assert.True(File.Exists(destination), $"Destino exato não encontrado: {destination}; observados: " +
                string.Join(" | ", Directory.EnumerateFileSystemEntries(session)));
            Assert.Equal($"contents-{index}", await File.ReadAllTextAsync(destination));
            Assert.Equal(destination, Assert.Single(Directory.EnumerateFiles(session)));
            Assert.False(File.Exists(original));
        }
    }

    private TemporaryFileCleanup Create() => new(TemporaryRoot, StorageRoot);

    private string WriteOld(string relativePath, string contents)
    {
        var path = Path.Combine(TemporaryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-8));
        return path;
    }

    private async Task<CleanupResult> QuarantineEverything(TemporaryFileCleanup cleanup)
    {
        var scan = await cleanup.ScanAsync();
        var result = await cleanup.QuarantineAsync(scan, scan.Files.Select(file => file.Id).ToArray());
        AssertSessionFiles(result, scan.Files.Count);
        return result;
    }

    private void AssertSessionFiles(CleanupResult result, int expectedMoved)
    {
        var entries = Directory.Exists(StorageRoot)
            ? Directory.EnumerateFileSystemEntries(StorageRoot, "*", SearchOption.AllDirectories)
                .Take(64).Select(path => System.Text.Json.JsonSerializer.Serialize(Path.GetRelativePath(StorageRoot, path))).ToArray()
            : [];
        var details = $"Session {result.SessionId:N}: moved={result.MovedFiles}, skipped={result.SkippedFiles}; " +
            $"warnings: {string.Join(" | ", result.Warnings)}; storage entries: {string.Join(" | ", entries)}";
        Assert.True(result.MovedFiles == expectedMoved, details);
        Assert.True(File.Exists(Path.Combine(SessionPath(result.SessionId), "manifest.json")), details);
        Assert.True(Directory.EnumerateFiles(SessionPath(result.SessionId), "*.bin").Count() == expectedMoved, details);
    }

    private string SessionPath(Guid id) => Path.Combine(StorageRoot, id.ToString("N"));

    private async Task EditManifest(Guid sessionId, Action<JsonObject> edit)
    {
        var path = Path.Combine(SessionPath(sessionId), "manifest.json");
        var document = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        edit(document);
        await File.WriteAllTextAsync(path, document.ToJsonString());
        // Crash fixtures deliberately replace the last durable state. Clear the old journal
        // so it cannot legitimately override the simulated checkpoint state.
        await File.WriteAllTextAsync(Path.Combine(SessionPath(sessionId), "journal.jsonl"), "");
    }

    private static JsonObject Entry(JsonObject manifest) => manifest["Entries"]![0]!.AsObject();

    private static JsonObject EntryByPath(JsonObject manifest, string relative) =>
        manifest["Entries"]!.AsArray().Select(node => node!.AsObject())
            .Single(entry => entry["RelativePath"]!.GetValue<string>() == relative);

    private async Task<string> StoredFileFor(Guid sessionId, string relative)
    {
        var manifestPath = Path.Combine(SessionPath(sessionId), "manifest.json");
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!.AsObject();
        return Path.Combine(SessionPath(sessionId), EntryByPath(manifest, relative)["Id"]!.GetValue<string>() + ".bin");
    }

    public void Dispose()
    {
        if (Directory.Exists(_fixture)) Directory.Delete(_fixture, recursive: true);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string newFileName, string existingFileName, IntPtr securityAttributes);
}
