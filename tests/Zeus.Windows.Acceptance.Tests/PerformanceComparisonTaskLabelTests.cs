using Zeus.Desktop;
using Zeus.Windows;

namespace Zeus.Windows.Acceptance.Tests;

public sealed class PerformanceComparisonTaskLabelTests
{
    [Fact]
    public void ReportsSameDeclaredActivityEvenWhenSessionModesHaveDifferentPrefixes()
    {
        var comparison = CreateComparison();
        var sessions = new[]
        {
            new PerformanceSessionExport("Referência de desempenho · Fortnite + OBS", comparison.ReferenceEndedAt.AddMinutes(-1), comparison.ReferenceEndedAt.AddSeconds(1), true, 5),
            new PerformanceSessionExport("Medição manual · fortnite + obs", comparison.LaterEndedAt.AddMinutes(-1), comparison.LaterEndedAt.AddSeconds(1), false, 5)
        };

        var summary = MainWindow.FormatPerformanceComparisonTaskLabels(comparison, sessions);

        Assert.Contains("Atividade informada nos dois períodos", summary);
        Assert.Contains("Fortnite + OBS", summary);
        Assert.Contains("não comprova", summary);
    }

    [Fact]
    public void HighlightsDifferentDeclaredActivities()
    {
        var comparison = CreateComparison();
        var sessions = new[]
        {
            new PerformanceSessionExport("Referência de desempenho · Fortnite", comparison.ReferenceEndedAt.AddMinutes(-1), comparison.ReferenceEndedAt.AddSeconds(1), true, 5),
            new PerformanceSessionExport("Observador adaptativo · Área de Trabalho", comparison.LaterEndedAt.AddMinutes(-1), comparison.LaterEndedAt.AddSeconds(1), false, 5)
        };

        var summary = MainWindow.FormatPerformanceComparisonTaskLabels(comparison, sessions);

        Assert.Contains("Rótulos de atividade diferentes", summary);
        Assert.Contains("Fortnite", summary);
        Assert.Contains("Área de Trabalho", summary);
        Assert.Contains("comparáveis", summary);
    }

    [Fact]
    public void DoesNotInferActivityWhenSessionsHaveNoUserProvidedLabel()
    {
        var comparison = CreateComparison();
        var sessions = new[]
        {
            new PerformanceSessionExport("Referência de desempenho", comparison.ReferenceEndedAt.AddMinutes(-1), comparison.ReferenceEndedAt.AddSeconds(1), true, 5),
            new PerformanceSessionExport("Medição manual", comparison.LaterEndedAt.AddMinutes(-1), comparison.LaterEndedAt.AddSeconds(1), false, 5)
        };

        var summary = MainWindow.FormatPerformanceComparisonTaskLabels(comparison, sessions);

        Assert.Contains("sem rótulo em ambos", summary);
        Assert.Contains("não confirma", summary);
    }

    private static PerformanceComparison CreateComparison()
    {
        var referenceEnd = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
        return new(5, 5, null, null, null, null, referenceEnd, referenceEnd.AddHours(1));
    }
}
