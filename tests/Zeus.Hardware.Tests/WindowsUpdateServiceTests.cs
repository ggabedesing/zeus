using Zeus.Windows;

namespace Zeus.Hardware.Tests;

public sealed class WindowsUpdateServiceTests
{
    [Fact]
    public void DriverSearchRetainsWindowsUpdateProviderClassAndDateWithoutInventingVersionOrSignature()
    {
        var payload = """{"Updates":[{"Id":"9d1fa4a8-a21a-4cc9-84a1-42d7428a46d8:2","Title":"NVIDIA Display Update","Manufacturer":"NVIDIA","DeviceName":"Graphics Adapter","DriverVersion":null,"RequiresEula":false,"DriverProvider":"NVIDIA","DriverClass":"Display","DriverDate":"2025-11-04"}],"Warnings":[],"ServerSelection":2,"ServiceId":null}""";

        var result = WindowsUpdateService.ParseDriverUpdatesPayload(payload);

        var candidate = Assert.Single(result.Updates);
        Assert.Equal("NVIDIA", candidate.DriverProvider);
        Assert.Equal("Display", candidate.DriverClass);
        Assert.Equal(new DateOnly(2025, 11, 4), candidate.DriverDate);
        Assert.Equal(2, candidate.UpdateServerSelection);
        Assert.Null(candidate.UpdateServiceId);
        Assert.Null(candidate.DriverVersion);
        Assert.Contains(result.Warnings, warning => warning.Contains("hash/assinatura", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, warning => warning.Contains("versão numérica", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DriverSearchOmitsDuplicateOrMalformedWuaIdentities()
    {
        var payload = """{"Updates":[{"Id":"9d1fa4a8-a21a-4cc9-84a1-42d7428a46d8:2","Title":"A","RequiresEula":false},{"Id":"9D1FA4A8-A21A-4CC9-84A1-42D7428A46D8:2","Title":"B","RequiresEula":false},{"Id":"bad","Title":"C","RequiresEula":false}],"Warnings":[]}""";

        var result = WindowsUpdateService.ParseDriverUpdatesPayload(payload);

        Assert.Single(result.Updates);
        Assert.Equal(2, result.Warnings.Count(warning => warning.Contains("identidade era inválida ou repetida", StringComparison.OrdinalIgnoreCase)));
    }

    [Theory]
    [InlineData("not-a-date")]
    [InlineData("0001-01-01")]
    public void InvalidDriverDateStaysUnavailableWithoutDiscardingOtherSearchResults(string value)
    {
        var payload = $$"""{"Updates":[{"Id":"9d1fa4a8-a21a-4cc9-84a1-42d7428a46d8:2","Title":"Driver","Manufacturer":"Vendor","DeviceName":"Device","RequiresEula":false,"DriverDate":"{{value}}"}],"Warnings":[]}""";

        var result = WindowsUpdateService.ParseDriverUpdatesPayload(payload);

        Assert.Null(Assert.Single(result.Updates).DriverDate);
        Assert.Contains(result.Warnings, warning => warning.Contains("data do driver", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DriverWithMissingTargetAndDateRemainsExplicitButIsNotInstallable()
    {
        var payload = """{"Updates":[{"Id":"9d1fa4a8-a21a-4cc9-84a1-42d7428a46d8:2","Title":"Driver","Manufacturer":"","DeviceName":null,"RequiresEula":false,"DriverDate":null}],"Warnings":[]}""";

        var result = WindowsUpdateService.ParseDriverUpdatesPayload(payload);

        var candidate = Assert.Single(result.Updates);
        Assert.Null(candidate.Manufacturer);
        Assert.Null(candidate.DeviceName);
        Assert.Null(candidate.DriverDate);
        Assert.Contains(result.Warnings, warning => warning.Contains("não identifica fabricante e modelo", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(7, null)]
    [InlineData(3, "bad")]
    [InlineData(2, "12345678-1234-1234-1234-123456789abc")]
    public void UnrecognizedWindowsUpdateSourceIsKeptExplicitAndWarned(int selection, string? serviceId)
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            Updates = new[] { new { Id = "9d1fa4a8-a21a-4cc9-84a1-42d7428a46d8:2", Title = "Driver", Manufacturer = "Vendor", DeviceName = "Device", RequiresEula = false, DriverDate = "2025-11-04" } },
            Warnings = Array.Empty<string>(), ServerSelection = selection, ServiceId = serviceId
        });

        var result = WindowsUpdateService.ParseDriverUpdatesPayload(payload);

        Assert.Single(result.Updates);
        Assert.Contains(result.Warnings, warning => warning.Contains("não é permitida", StringComparison.OrdinalIgnoreCase));
    }

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
