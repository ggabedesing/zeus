using Zeus.Core;

namespace Zeus.Core.Tests;

public sealed class EventTroubleshootingCatalogTests
{
    [Theory]
    [InlineData("Microsoft-Windows-Kernel-Power", 41, "BugcheckCode", "event-id-41-restart")]
    [InlineData("disk", 153, "não foram coletados", "troubleshoot-data-corruption-and-disk-errors")]
    [InlineData("Microsoft-Windows-DistributedCOM", 10016, "Não altere permissões", "event-10016-logged-when-accessing-dcom")]
    public void DocumentedSignatureGetsReadOnlyGuidanceEvenForSingleOccurrence(string provider, int id, string nextStep, string source)
    {
        var item = Event("System", provider, id);
        var guide = Assert.IsType<EventTroubleshootingGuide>(EventTroubleshootingCatalog.Find(item.Log, item.Provider, item.Id));
        Assert.Contains(nextStep, guide.NextStep);
        Assert.StartsWith("https://learn.microsoft.com/en-us/troubleshoot/", guide.SourceUrl);
        Assert.EndsWith(source, guide.SourceUrl);
        var report = EventPatternAnalyzer.Analyze([item]);
        Assert.Contains("0 assinatura(s) repetida(s)", report.Summary);
        Assert.Contains("1 com orientação documentada", report.Summary);
        Assert.Contains(guide.SourceUrl, Assert.Single(report.Findings).Detail);
    }

    [Theory]
    [InlineData("Application", "Microsoft-Windows-Kernel-Power", 41)]
    [InlineData("System", "OtherProvider", 41)]
    [InlineData("System", "Microsoft-Windows-Kernel-Power", 42)]
    [InlineData("System", "nvlddmkm", 153)]
    [InlineData("System", "Example", 10016)]
    public void IdAloneNeverMatchesDocumentedGuidance(string log, string provider, int id)
    {
        Assert.Null(EventTroubleshootingCatalog.Find(log, provider, id));
        Assert.Empty(EventPatternAnalyzer.Analyze([Event(log, provider, id)]).Findings);
    }

    [Fact]
    public void CaseInsensitiveSignatureDoesNotMergeDifferentProviders()
    {
        var report = EventPatternAnalyzer.Analyze([
            Event("system", "MICROSOFT-WINDOWS-KERNEL-POWER", 41),
            Event("System", "Microsoft-Windows-Kernel-Power", 41),
            Event("System", "Other", 41)]);
        var finding = Assert.Single(report.Findings);
        Assert.Contains("2 ocorrências", finding.Detail);
        Assert.Contains("1 assinatura(s) repetida(s)", report.Summary);
    }

    [Fact]
    public void KnownSingletonIsVisibleBeforeFrequentlyRepeatedUnknownSignature()
    {
        var events = Enumerable.Repeat(Event("System", "Unknown", 41), 10)
            .Append(Event("System", "Microsoft-Windows-Kernel-Power", 41)).ToArray();
        var report = EventPatternAnalyzer.Analyze(events, sourcesComplete: false);
        Assert.Equal(2, report.Findings.Count);
        Assert.Contains("Kernel-Power", report.Findings[0].Title);
        Assert.Contains("amostra está incompleta", report.Summary);
    }

    [Fact]
    public void DcomGuidanceDoesNotDeclareUnverifiedOccurrenceHarmless()
    {
        var guide = EventTroubleshootingCatalog.Find("System", "Microsoft-Windows-DistributedCOM", 10016)!;
        Assert.Contains("não coletou CLSID/APPID", guide.Explanation);
        Assert.Contains("confirmar", guide.Explanation);
    }

    [Fact]
    public void InventorySourceWarningsArePreservedInDerivedReport()
    {
        var inventory = new WindowsInventoryInfo([], [], [], [], [], [], [],
            [Event("System", "disk", 153)], null, null, null, ["Eventos Application: leitura indisponível"]);
        Assert.Contains("amostra está incompleta", EventPatternAnalyzer.AnalyzeInventory(inventory).Summary);
        Assert.Single(EventPatternAnalyzer.AnalyzeInventory(inventory).Findings);
        Assert.Contains("indisponíveis", EventPatternAnalyzer.AnalyzeInventory(null).Summary);
    }

    private static WindowsEventInfo Event(string log, string provider, int id) =>
        new(DateTimeOffset.Parse("2026-01-01T10:00:00Z"), log, provider, id, "Warning", string.Empty);
}
