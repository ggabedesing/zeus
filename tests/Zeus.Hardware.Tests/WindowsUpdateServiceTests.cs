using Zeus.Windows;

namespace Zeus.Hardware.Tests;

public sealed class WindowsUpdateServiceTests
{
    [Fact]
    public void ParsesPendingUpdatesAsDataWithoutInferringDownloadOrInstallation()
    {
        var payload = """{"IsComplete":true,"Updates":[{"Title":"2026-10 Cumulative Update","KnowledgeBaseIds":["KB123456"],"Downloaded":false,"UpdateId":"9d1fa4a8-a21a-4cc9-84a1-42d7428a46d8"}],"Warnings":[]}""";

        var result = WindowsUpdateService.ParsePendingSoftwareUpdatesPayload(payload);

        Assert.True(result.IsComplete);
        var update = Assert.Single(result.Updates);
        Assert.Equal("2026-10 Cumulative Update", update.Title);
        Assert.Equal(["KB123456"], update.KnowledgeBaseIds);
        Assert.False(update.Downloaded);
    }

    [Fact]
    public void PartialSearchRemainsExplicitlyIncomplete()
    {
        var payload = """{"IsComplete":false,"Updates":[],"Warnings":["fonte parcial"]}""";

        var result = WindowsUpdateService.ParsePendingSoftwareUpdatesPayload(payload);

        Assert.False(result.IsComplete);
        Assert.Contains("fonte parcial", result.Warnings);
    }

    [Fact]
    public void MalformedOrDuplicateUpdateIdentityMakesSearchIncomplete()
    {
        var payload = """{"IsComplete":true,"Updates":[{"Title":"A","KnowledgeBaseIds":[],"Downloaded":false,"UpdateId":"9d1fa4a8-a21a-4cc9-84a1-42d7428a46d8"},{"Title":"B","KnowledgeBaseIds":[],"Downloaded":true,"UpdateId":"9D1FA4A8-A21A-4CC9-84A1-42D7428A46D8"}],"Warnings":[]}""";

        var result = WindowsUpdateService.ParsePendingSoftwareUpdatesPayload(payload);

        Assert.False(result.IsComplete);
        Assert.Single(result.Updates);
        Assert.Contains(result.Warnings, warning => warning.Contains("inválida ou repetida", StringComparison.OrdinalIgnoreCase));
    }
}
