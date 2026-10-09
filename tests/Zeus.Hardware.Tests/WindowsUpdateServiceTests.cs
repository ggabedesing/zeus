using Zeus.Windows;

namespace Zeus.Hardware.Tests;

public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Este teste requer Windows.";
    }
}

public sealed class WindowsUpdateServiceTests
{
    [Fact]
    public void ExactInstalledDriverRecheckRequiresCompleteMatchingSource()
    {
        var installed = WindowsUpdateService.ParseInstalledDriverVerificationPayload(
            """{"IsComplete":true,"IsInstalled":true,"SourceMatches":true,"Warnings":[]}""");
        var absent = WindowsUpdateService.ParseInstalledDriverVerificationPayload(
            """{"IsComplete":true,"IsInstalled":false,"SourceMatches":true,"Warnings":[]}""");

        Assert.True(installed.IsComplete);
        Assert.True(installed.IsInstalled);
        Assert.False(absent.IsInstalled);
    }

    [Fact]
    public void IncompleteOrDifferentSourceRecheckCannotConfirmInstalledState()
    {
        var unavailable = WindowsUpdateService.ParseInstalledDriverVerificationPayload(
            """{"IsComplete":false,"IsInstalled":null,"SourceMatches":true,"Warnings":["resultado incompleto"]}""");
        var wrongSource = WindowsUpdateService.ParseInstalledDriverVerificationPayload(
            """{"IsComplete":false,"IsInstalled":null,"SourceMatches":false,"Warnings":["origem diferente"]}""");

        Assert.Null(unavailable.IsInstalled);
        Assert.False(unavailable.IsComplete);
        Assert.Equal(false, wrongSource.SourceMatches);
        Assert.Null(wrongSource.IsInstalled);
        Assert.Throws<InvalidDataException>(() => WindowsUpdateService.ParseInstalledDriverVerificationPayload(
            """{"IsComplete":true,"IsInstalled":true,"SourceMatches":false,"Warnings":[]}"""));
    }

    [WindowsFact]
    public async Task PostRestartRecheckRejectsInvalidIdentityBeforeStartingWindowsUpdate()
    {
        var result = await new WindowsUpdateService().VerifyInstalledDriverUpdateAsync("invalid", 2, null, DateTimeOffset.UtcNow);

        Assert.False(result.IsComplete);
        Assert.Null(result.IsInstalled);
        Assert.Contains(result.Warnings, warning => warning.Contains("inválida", StringComparison.OrdinalIgnoreCase));
    }

    [WindowsFact]
    public async Task PostRestartRecheckWaitsUntilWindowsBootTimeIsLaterThanInstallation()
    {
        var result = await new WindowsUpdateService().VerifyInstalledDriverUpdateAsync(
            "12345678-1234-1234-1234-123456789abc:1", 2, null, DateTimeOffset.UtcNow.AddMinutes(1));

        Assert.False(result.IsComplete);
        Assert.Null(result.SourceMatches);
        Assert.Null(result.IsInstalled);
        Assert.Contains(result.Warnings, warning => warning.Contains("reinicialização posterior", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DriverSearchRetainsWindowsUpdateProviderClassAndDateWithoutInventingVersionOrSignature()
    {
        var payload = """{"IsComplete":true,"Updates":[{"Id":"9d1fa4a8-a21a-4cc9-84a1-42d7428a46d8:2","Title":"NVIDIA Display Update","Manufacturer":"NVIDIA","DeviceName":"Graphics Adapter","DriverVersion":null,"RequiresEula":false,"DriverProvider":"NVIDIA","DriverClass":"Display","DriverDate":"2025-11-04"}],"Warnings":[],"ServerSelection":2,"ServiceId":null}""";

        var result = WindowsUpdateService.ParseDriverUpdatesPayload(payload);

        var candidate = Assert.Single(result.Updates);
        Assert.True(result.IsComplete);
        Assert.Equal(2, result.UpdateServerSelection);
        Assert.Null(result.UpdateServiceId);
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
        var payload = """{"IsComplete":true,"Updates":[{"Id":"9d1fa4a8-a21a-4cc9-84a1-42d7428a46d8:2","Title":"A","RequiresEula":false},{"Id":"9D1FA4A8-A21A-4CC9-84A1-42D7428A46D8:2","Title":"B","RequiresEula":false},{"Id":"bad","Title":"C","RequiresEula":false}],"Warnings":[]}""";

        var result = WindowsUpdateService.ParseDriverUpdatesPayload(payload);

        Assert.Single(result.Updates);
        Assert.False(result.IsComplete);
        Assert.Equal(2, result.Warnings.Count(warning => warning.Contains("identidade era inválida ou repetida", StringComparison.OrdinalIgnoreCase)));
    }

    [Theory]
    [InlineData("not-a-date")]
    [InlineData("0001-01-01")]
    public void InvalidDriverDateStaysUnavailableWithoutDiscardingOtherSearchResults(string value)
    {
        var payload = $$"""{"IsComplete":true,"Updates":[{"Id":"9d1fa4a8-a21a-4cc9-84a1-42d7428a46d8:2","Title":"Driver","Manufacturer":"Vendor","DeviceName":"Device","RequiresEula":false,"DriverDate":"{{value}}"}],"Warnings":[]}""";

        var result = WindowsUpdateService.ParseDriverUpdatesPayload(payload);

        Assert.Null(Assert.Single(result.Updates).DriverDate);
        Assert.Contains(result.Warnings, warning => warning.Contains("data do driver", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DriverWithMissingTargetAndDateRemainsExplicitButIsNotInstallable()
    {
        var payload = """{"IsComplete":true,"Updates":[{"Id":"9d1fa4a8-a21a-4cc9-84a1-42d7428a46d8:2","Title":"Driver","Manufacturer":"","DeviceName":null,"RequiresEula":false,"DriverDate":null}],"Warnings":[]}""";

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
            IsComplete = true, Warnings = Array.Empty<string>(), ServerSelection = selection, ServiceId = serviceId
        });

        var result = WindowsUpdateService.ParseDriverUpdatesPayload(payload);

        Assert.Single(result.Updates);
        Assert.Contains(result.Warnings, warning => warning.Contains("não é reconhecida", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MicrosoftUpdateServiceIdIsAcceptedButUnrelatedAdditionalServiceIsNot()
    {
        var microsoftPayload = $$"""{"IsComplete":true,"Updates":[{"Id":"9d1fa4a8-a21a-4cc9-84a1-42d7428a46d8:2","Title":"Driver","Manufacturer":"Vendor","DeviceName":"Device","RequiresEula":false,"DriverDate":"2025-11-04"}],"Warnings":[],"ServerSelection":3,"ServiceId":"{{Zeus.Core.WindowsUpdateSourcePolicy.MicrosoftUpdateServiceId}}"}""";
        var unrelatedPayload = microsoftPayload.Replace(Zeus.Core.WindowsUpdateSourcePolicy.MicrosoftUpdateServiceId,
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", StringComparison.OrdinalIgnoreCase);

        var accepted = WindowsUpdateService.ParseDriverUpdatesPayload(microsoftPayload);
        var blocked = WindowsUpdateService.ParseDriverUpdatesPayload(unrelatedPayload);

        Assert.DoesNotContain(accepted.Warnings, warning => warning.Contains("não é reconhecida", StringComparison.OrdinalIgnoreCase));
        Assert.True(accepted.IsComplete);
        Assert.Contains(blocked.Warnings, warning => warning.Contains("não é reconhecida", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void IncompleteDriverSearchRemainsExplicitEvenWhenItReturnsCandidates()
    {
        var payload = """{"IsComplete":false,"Updates":[{"Id":"9d1fa4a8-a21a-4cc9-84a1-42d7428a46d8:2","Title":"Driver","RequiresEula":false}],"Warnings":["fonte parcial"]}""";

        var result = WindowsUpdateService.ParseDriverUpdatesPayload(payload);

        Assert.False(result.IsComplete);
        Assert.Single(result.Updates);
        Assert.Contains("fonte parcial", result.Warnings);
        Assert.Throws<InvalidDataException>(() => WindowsUpdateService.ParseDriverUpdatesPayload(
            """{"Updates":[],"Warnings":[]}"""));
    }

    [Fact]
    public void EmptyDriverSearchRetainsCompletenessAndConfiguredSource()
    {
        var payload = """{"IsComplete":true,"Updates":[],"Warnings":[],"ServerSelection":1,"ServiceId":null}""";

        var result = WindowsUpdateService.ParseDriverUpdatesPayload(payload);

        Assert.True(result.IsComplete);
        Assert.Empty(result.Updates);
        Assert.Equal(1, result.UpdateServerSelection);
        Assert.Equal("Windows Update Agent · servidor gerenciado", Zeus.Core.WindowsUpdateSourcePolicy.Describe(result.UpdateServerSelection, result.UpdateServiceId));
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

    [Fact]
    public void ParsesLocalUpdateHistoryAndKeepsUnknownResultCodesExplicit()
    {
        var payload = """{"IsComplete":true,"Entries":[{"DateUtc":"2026-10-09T12:34:56Z","Title":"Atualização cumulativa","Operation":"Instalação","Result":"Resultado desconhecido (99)","HResult":"0x80070005"}],"Warnings":[]}""";

        var result = WindowsUpdateService.ParseHistoryPayload(payload);

        Assert.True(result.IsComplete);
        var entry = Assert.Single(result.Entries);
        Assert.Equal(new DateTimeOffset(2026, 10, 9, 12, 34, 56, TimeSpan.Zero), entry.Date);
        Assert.Equal("Atualização cumulativa", entry.Title);
        Assert.Equal("Instalação", entry.Operation);
        Assert.Equal("Resultado desconhecido (99)", entry.Result);
        Assert.Equal("0X80070005", entry.HResult);
    }

    [Fact]
    public void InvalidHistoryRecordsAreOmittedAndMarkTheResultIncomplete()
    {
        var payload = """{"IsComplete":true,"Entries":[{"DateUtc":"not-a-date","Title":"Atualização","Operation":"Instalação","Result":"Concluído","HResult":null}],"Warnings":[]}""";

        var result = WindowsUpdateService.ParseHistoryPayload(payload);

        Assert.False(result.IsComplete);
        Assert.Empty(result.Entries);
        Assert.Contains(result.Warnings, warning => warning.Contains("data, título ou resultado inválido", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void HistoryPayloadCannotExceedTheBoundedReadLimitSilently()
    {
        var entries = string.Join(',', Enumerable.Range(0, WindowsUpdateService.WindowsUpdateHistoryLimit + 1).Select(index =>
            $"{{\"DateUtc\":\"2026-10-09T12:34:{index % 60:00}Z\",\"Title\":\"Update {index}\",\"Operation\":\"Instalação\",\"Result\":\"Concluído\",\"HResult\":null}}"));
        var payload = $"{{\"IsComplete\":true,\"Entries\":[{entries}],\"Warnings\":[]}}";

        var result = WindowsUpdateService.ParseHistoryPayload(payload);

        Assert.False(result.IsComplete);
        Assert.Equal(WindowsUpdateService.WindowsUpdateHistoryLimit, result.Entries.Count);
        Assert.Contains(result.Warnings, warning => warning.Contains("excedeu o limite", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ReadsOnlyTheBoundedLocalWindowsUpdateHistory()
    {
        if (!OperatingSystem.IsWindows()) return;

        var result = await new WindowsUpdateService().ReadHistoryAsync();

        Assert.True(result.IsComplete, string.Join(" ", result.Warnings));
        Assert.True(result.Entries.Count <= WindowsUpdateService.WindowsUpdateHistoryLimit);
        Assert.Contains(result.Warnings, warning => warning.Contains("Histórico local somente leitura", StringComparison.OrdinalIgnoreCase));
    }
}
