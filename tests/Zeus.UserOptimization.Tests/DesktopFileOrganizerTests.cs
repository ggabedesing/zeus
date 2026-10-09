using Zeus.Windows;
using System.Text.Json.Nodes;

namespace Zeus.UserOptimization.Tests;

public sealed class DesktopFileOrganizerTests
{
    [Fact]
    public async Task PreviewIncludesOnlySupportedTopLevelRegularFiles()
    {
        using var fixture = new Fixture();
        await File.WriteAllTextAsync(Path.Combine(fixture.Desktop, "photo.png"), "image");
        await File.WriteAllTextAsync(Path.Combine(fixture.Desktop, "setup.exe"), "installer");
        await File.WriteAllTextAsync(Path.Combine(fixture.Desktop, "shortcut.lnk"), "shortcut");
        var hidden = Path.Combine(fixture.Desktop, "hidden.pdf");
        await File.WriteAllTextAsync(hidden, "hidden");
        File.SetAttributes(hidden, FileAttributes.Hidden);
        Directory.CreateDirectory(Path.Combine(fixture.Desktop, "folder"));

        var preview = await fixture.Service.PreviewAsync();

        if (OperatingSystem.IsWindows())
        {
            var item = Assert.Single(preview.Items);
            Assert.Equal("photo.png", item.Name);
            Assert.Equal("Imagens", item.Category);
            Assert.Equal(5, preview.TotalBytes);
            Assert.Equal(4, preview.Skipped.Count);
        }
        else
        {
            // Unix file systems do not model the Windows Hidden attribute set above.
            Assert.Equal(new[] { "hidden.pdf", "photo.png" }, preview.Items.Select(item => item.Name).OrderBy(name => name));
            Assert.Equal(11, preview.TotalBytes);
            Assert.Equal(3, preview.Skipped.Count);
        }
    }

    [Fact]
    public async Task ApplyAndRestoreVerifyHashAndPreserveOriginalBytes()
    {
        using var fixture = new Fixture();
        var original = Path.Combine(fixture.Desktop, "report.pdf");
        await File.WriteAllTextAsync(original, "report contents");
        var preview = await fixture.Service.PreviewAsync();

        var applied = await fixture.Service.ApplyAsync(preview);

        Assert.True(applied.Succeeded, applied.Message);
        Assert.False(File.Exists(original));
        var destination = Path.Combine(fixture.Desktop, "ZEUS - Documentos", "report.pdf");
        Assert.Equal("report contents", await File.ReadAllTextAsync(destination));
        var session = Assert.Single(await fixture.Service.ListSessionsAsync());
        Assert.Equal(1, session.MovedFiles);

        var restored = await fixture.Service.RestoreAsync(applied.SessionId);

        Assert.True(restored.Succeeded, restored.Message);
        Assert.Equal(1, restored.RestoredFiles);
        Assert.Equal("report contents", await File.ReadAllTextAsync(original));
        Assert.False(File.Exists(destination));
        Assert.Equal("Restored", Assert.Single(await fixture.Service.ListSessionsAsync()).Status);
    }

    [Fact]
    public async Task RestorePreservesChangedDestinationAndOccupiedOriginal()
    {
        using var fixture = new Fixture();
        var original = Path.Combine(fixture.Desktop, "clip.mp4");
        await File.WriteAllTextAsync(original, "original");
        var applied = await fixture.Service.ApplyAsync(await fixture.Service.PreviewAsync());
        Assert.True(applied.Succeeded, applied.Message);
        var destination = Path.Combine(fixture.Desktop, "ZEUS - Vídeos", "clip.mp4");
        await File.WriteAllTextAsync(destination, "user changed this");
        await File.WriteAllTextAsync(original, "new file from user");

        var restore = await fixture.Service.RestoreAsync(applied.SessionId);

        Assert.False(restore.Succeeded);
        Assert.Equal(1, restore.Conflicts);
        Assert.Equal("user changed this", await File.ReadAllTextAsync(destination));
        Assert.Equal("new file from user", await File.ReadAllTextAsync(original));
    }

    [Fact]
    public async Task ApplyRejectsStalePreviewWithoutMovingFiles()
    {
        using var fixture = new Fixture();
        var original = Path.Combine(fixture.Desktop, "notes.txt");
        await File.WriteAllTextAsync(original, "before");
        var preview = await fixture.Service.PreviewAsync();
        await File.WriteAllTextAsync(original, "change");

        var result = await fixture.Service.ApplyAsync(preview);

        Assert.False(result.Succeeded);
        Assert.Equal(1, result.Conflicts);
        Assert.Equal("change", await File.ReadAllTextAsync(original));
        Assert.False(Directory.Exists(Path.Combine(fixture.Desktop, "ZEUS - Documentos")));
    }

    [Fact]
    public async Task PreviewSkipsNameCollisionAndLeavesUnsupportedFilesAlone()
    {
        using var fixture = new Fixture();
        var destination = Path.Combine(fixture.Desktop, "ZEUS - Imagens");
        Directory.CreateDirectory(destination);
        await File.WriteAllTextAsync(Path.Combine(destination, "same.jpg"), "existing");
        await File.WriteAllTextAsync(Path.Combine(fixture.Desktop, "same.jpg"), "source");
        await File.WriteAllTextAsync(Path.Combine(fixture.Desktop, "notes.md"), "unsupported");

        var preview = await fixture.Service.PreviewAsync();

        Assert.Empty(preview.Items);
        Assert.Equal(3, preview.Skipped.Count);
    }

    [Fact]
    public async Task RestoreRejectsTamperedSessionDestinationOutsideDesktopCategory()
    {
        using var fixture = new Fixture();
        var original = Path.Combine(fixture.Desktop, "image.png");
        await File.WriteAllTextAsync(original, "owned bytes");
        var applied = await fixture.Service.ApplyAsync(await fixture.Service.PreviewAsync());
        Assert.True(applied.Succeeded, applied.Message);
        var external = Path.Combine(Path.GetDirectoryName(fixture.Desktop)!, "external.png");
        await File.WriteAllTextAsync(external, "owned bytes");
        var journalPath = Path.Combine(fixture.Sessions, applied.SessionId.ToString("N") + ".json");
        var journal = JsonNode.Parse(await File.ReadAllTextAsync(journalPath))!;
        journal["entries"]![0]!["destinationPath"] = external;
        await File.WriteAllTextAsync(journalPath, journal.ToJsonString());

        var result = await fixture.Service.RestoreAsync(applied.SessionId);

        Assert.False(result.Succeeded);
        Assert.Equal("owned bytes", await File.ReadAllTextAsync(external));
        Assert.False(File.Exists(original));
    }

    [Fact]
    public async Task SessionReconcilesFileMovedBackBeforeInterruptedJournalSave()
    {
        using var fixture = new Fixture();
        var original = Path.Combine(fixture.Desktop, "notes.txt");
        await File.WriteAllTextAsync(original, "notes");
        var applied = await fixture.Service.ApplyAsync(await fixture.Service.PreviewAsync());
        Assert.True(applied.Succeeded, applied.Message);
        var destination = Path.Combine(fixture.Desktop, "ZEUS - Documentos", "notes.txt");
        var journalPath = Path.Combine(fixture.Sessions, applied.SessionId.ToString("N") + ".json");
        var journal = JsonNode.Parse(await File.ReadAllTextAsync(journalPath))!;
        journal["entries"]![0]!["state"] = "Restoring";
        await File.WriteAllTextAsync(journalPath, journal.ToJsonString());
        File.Move(destination, original);

        var session = Assert.Single(await fixture.Service.ListSessionsAsync());

        Assert.Equal(1, session.RestoredFiles);
        Assert.False(session.CanRestore);
        Assert.Equal("notes", await File.ReadAllTextAsync(original));
    }

    [Fact]
    public async Task SessionReconcilesFileMovedBeforeInterruptedApplyJournalSave()
    {
        using var fixture = new Fixture();
        var original = Path.Combine(fixture.Desktop, "report.pdf");
        await File.WriteAllTextAsync(original, "report");
        var applied = await fixture.Service.ApplyAsync(await fixture.Service.PreviewAsync());
        Assert.True(applied.Succeeded, applied.Message);
        var journalPath = Path.Combine(fixture.Sessions, applied.SessionId.ToString("N") + ".json");
        var journal = JsonNode.Parse(await File.ReadAllTextAsync(journalPath))!;
        journal["entries"]![0]!["state"] = "Applying";
        journal["status"] = "Prepared";
        await File.WriteAllTextAsync(journalPath, journal.ToJsonString());

        var session = Assert.Single(await fixture.Service.ListSessionsAsync());

        Assert.Equal(1, session.MovedFiles);
        Assert.Equal("Applied", session.Status);
        Assert.True(session.CanRestore);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "Zeus.DesktopOrganizer." + Guid.NewGuid().ToString("N"));
        public string Desktop { get; }
        public string Sessions { get; }
        public DesktopFileOrganizer Service { get; }
        public Fixture()
        {
            Desktop = Path.Combine(_root, "Desktop");
            Sessions = Path.Combine(_root, "Sessions");
            Directory.CreateDirectory(Desktop);
            Service = new DesktopFileOrganizer(Desktop, Sessions);
        }
        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
    }
}
