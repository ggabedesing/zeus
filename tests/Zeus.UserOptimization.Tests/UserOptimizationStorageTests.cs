using System.Text.Json;
using System.Text.Json.Nodes;
using Zeus.Windows;

namespace Zeus.UserOptimization.Tests;

public sealed class UserOptimizationStorageTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"Zeus.UserTests.{Guid.NewGuid():N}");

    [Fact]
    public async Task EmptyHistoryDoesNotCreateStorage()
    {
        Assert.Empty(await new UserOptimizationService(root).ListChangesAsync());
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task LoadsValidatedMetadataWithoutWindowsMutation()
    {
        var (id, document) = StartupDocument();
        await WriteAsync(id, document);
        var entry = Assert.Single(await new UserOptimizationService(root).ListChangesAsync());
        Assert.Equal(id, entry.Id);
        Assert.Equal("Teste local de metadados", entry.Description);
        Assert.False(entry.Restored);
        Assert.Equal(UserChangeStatus.Unknown, entry.Status);
    }

    [Theory]
    [InlineData("Scope", "LocalMachine")]
    [InlineData("RegistryPath", "Software\\Other\\Run")]
    [InlineData("Kind", "run-command")]
    [InlineData("Description", "")]
    public async Task RejectsHistoryWithUnexpectedScopeOrAction(string field, string value)
    {
        var (id, document) = StartupDocument();
        document[field] = value;
        await WriteAsync(id, document);
        await Assert.ThrowsAsync<InvalidDataException>(() => new UserOptimizationService(root).ListChangesAsync());
    }

    [Fact]
    public async Task RejectsFileWhoseSessionDiffersFromMetadata()
    {
        var (id, document) = StartupDocument();
        document["Id"] = Guid.NewGuid();
        await WriteAsync(id, document);
        await Assert.ThrowsAsync<InvalidDataException>(() => new UserOptimizationService(root).ListChangesAsync());
    }

    [Fact]
    public async Task InterruptedMutationStatusSurvivesLoadingAndIsNotPresentedAsApplied()
    {
        var (id, document) = StartupDocument();
        document["Status"] = (int)UserChangeStatus.Applying;
        await WriteAsync(id, document);

        var change = Assert.Single(await new UserOptimizationService(root).ListChangesAsync());

        Assert.False(change.Restored);
        Assert.Equal(UserChangeStatus.Applying, change.Status);
        Assert.Contains("interrompida", change.StatusText);
    }

    [Fact]
    public async Task RejectsUnknownMutationStatus()
    {
        var (id, document) = StartupDocument();
        document["Status"] = int.MaxValue;
        await WriteAsync(id, document);

        await Assert.ThrowsAsync<InvalidDataException>(() => new UserOptimizationService(root).ListChangesAsync());
    }

    [Fact]
    public async Task RejectsUnsupportedRegistryTypeInStartupBackup()
    {
        var (id, document) = StartupDocument();
        document["Startup"]!["Kind"] = 3; // REG_BINARY cannot become an autorun string.
        await WriteAsync(id, document);
        await Assert.ThrowsAsync<InvalidDataException>(() => new UserOptimizationService(root).ListChangesAsync());
    }

    [Fact]
    public async Task RejectsTooLargeHistoryBeforeDeserialization()
    {
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, $"{Guid.NewGuid():D}.json"), new string('x', 128 * 1024 + 1));
        await Assert.ThrowsAsync<InvalidDataException>(() => new UserOptimizationService(root).ListChangesAsync());
    }

    [Fact]
    public async Task IgnoresNonSessionFiles()
    {
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "preferences.json"), "not a session");
        Assert.Empty(await new UserOptimizationService(root).ListChangesAsync());
    }

    [Fact]
    public async Task RejectsRedirectedHistoryFile()
    {
        if (OperatingSystem.IsWindows()) return; // Linux exercises path validation; Windows fixture avoids symlink privileges.
        var (id, document) = StartupDocument();
        Directory.CreateDirectory(root);
        var outside = Path.Combine(root, "outside.txt");
        await File.WriteAllTextAsync(outside, document.ToJsonString());
        File.CreateSymbolicLink(Path.Combine(root, $"{id:D}.json"), outside);
        await Assert.ThrowsAsync<IOException>(() => new UserOptimizationService(root).ListChangesAsync());
    }

    [Fact]
    public void RejectsRedirectedStorageAncestor()
    {
        if (OperatingSystem.IsWindows()) return;
        Directory.CreateDirectory(root);
        var actual = Path.Combine(root, "actual");
        Directory.CreateDirectory(actual);
        var link = Path.Combine(root, "redirect");
        Directory.CreateSymbolicLink(link, actual);
        Assert.Throws<IOException>(() => new UserOptimizationService(Path.Combine(link, "UserChanges")));
    }

    [Fact]
    public async Task LinuxCannotApplyWindowsOperations()
    {
        if (OperatingSystem.IsWindows()) return;
        var service = new UserOptimizationService(root);
        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => service.ReadStartupAsync());
        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => service.ReadPowerPlansAsync());
        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => service.ApplyPreferencesAsync(new(UsageProfile.Balanced, false, false)));
    }

    private static (Guid Id, JsonObject Document) StartupDocument()
    {
        var id = Guid.NewGuid();
        var document = JsonNode.Parse(JsonSerializer.Serialize(new
        {
            Version = 1, Scope = "CurrentUser", Id = id, CreatedAt = DateTimeOffset.UtcNow,
            Description = "Teste local de metadados", Kind = "startup",
            RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run", Restored = false,
            Startup = new { Name = $"Zeus.Test.{Guid.NewGuid():N}", Kind = 2, StringValue = "%SystemRoot%\\System32\\notepad.exe", DWordValue = (int?)null }
        }))!.AsObject();
        return (id, document);
    }

    private async Task WriteAsync(Guid id, JsonObject document)
    {
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, $"{id:D}.json"), document.ToJsonString());
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
