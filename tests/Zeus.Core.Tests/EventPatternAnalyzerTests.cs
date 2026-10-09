using Zeus.Core;

namespace Zeus.Core.Tests;

public sealed class EventPatternAnalyzerTests
{
    [Fact]
    public void GroupsRepeatedProviderAndIdWithoutTreatingItAsCause()
    {
        var events = new[]
        {
            Event("System", "ExampleProvider", 17, "Warning", DateTimeOffset.Parse("2026-01-01T10:00:00Z")),
            Event("system", "exampleprovider", 17, "Error", DateTimeOffset.Parse("2026-01-01T10:02:00Z")),
            Event("Application", "OtherProvider", 17, "Error", DateTimeOffset.Parse("2026-01-01T10:01:00Z"))
        };

        var report = EventPatternAnalyzer.Analyze(events);

        var finding = Assert.Single(report.Findings);
        Assert.Contains("ExampleProvider", finding.Title);
        Assert.Contains("2 ocorrências", finding.Detail);
        Assert.Contains("Error, Warning", finding.Detail);
        Assert.Contains("período:", finding.Detail);
        Assert.Contains("não prova causa", report.Summary);
    }

    [Fact]
    public void KeepsWindowsUpdateEventsDistinctFromSystemEvents()
    {
        var events = new[]
        {
            Event("Microsoft-Windows-WindowsUpdateClient/Operational", "WindowsUpdateClient", 20, "Error", DateTimeOffset.Parse("2026-01-01T10:00:00Z")),
            Event("Microsoft-Windows-WindowsUpdateClient/Operational", "WindowsUpdateClient", 20, "Error", DateTimeOffset.Parse("2026-01-01T10:02:00Z")),
            Event("System", "WindowsUpdateClient", 20, "Error", DateTimeOffset.Parse("2026-01-01T10:03:00Z"))
        };

        var report = EventPatternAnalyzer.Analyze(events);

        var finding = Assert.Single(report.Findings);
        Assert.Contains("Microsoft-Windows-WindowsUpdateClient/Operational", finding.Title);
        Assert.Contains("2 ocorrências", finding.Detail);
    }

    [Fact]
    public void EmptyIncompleteSourceRemainsUnknown()
    {
        var report = EventPatternAnalyzer.Analyze([], sourcesComplete: false);
        Assert.Contains("indisponíveis", report.Summary);
        Assert.Empty(report.Findings);
    }

    [Fact]
    public void EmptyCompleteSourceDoesNotClaimWindowsHealth()
    {
        var report = EventPatternAnalyzer.Analyze([]);
        Assert.Contains("não comprova ausência", report.Summary);
        Assert.Empty(report.Findings);
    }

    [Fact]
    public void MissingEventCollectionIsNotTreatedAsEmpty()
    {
        var report = EventPatternAnalyzer.Analyze(null);
        Assert.Contains("indisponíveis", report.Summary);
    }

    private static WindowsEventInfo Event(string log, string provider, int id, string level, DateTimeOffset time) =>
        new(time, log, provider, id, level, string.Empty);
}
