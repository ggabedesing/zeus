using Zeus.Core;

namespace Zeus.Hardware.Tests;

public sealed class ServiceDependencyAnalyzerTests
{
    [Fact]
    public void ReportsKnownEmptyDependenciesAndDoesNotCallStoppedServiceBroken()
    {
        var services = new[]
        {
            new ServiceInfo("A", "Serviço A", "Running", "Auto", ["B"], true),
            new ServiceInfo("B", "Serviço B", "Stopped", "Manual", [], true)
        };

        var report = ServiceDependencyAnalyzer.Analyze(services);

        Assert.Contains(report.Findings, item => item.Name == "Serviço A" && item.Detail.Contains("estado informado Stopped"));
        Assert.Contains(report.Findings, item => item.Name == "Serviço B" && item.Detail.Contains("Nenhuma dependência declarada"));
        Assert.DoesNotContain(report.Findings, item => item.Detail.Contains("defeito", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void KeepsUnknownUnresolvedAndGroupDependenciesDistinct()
    {
        var services = new[]
        {
            new ServiceInfo("A", "Serviço A", "Running", "Auto", ["missing", "+NetworkProvider"], true),
            new ServiceInfo("B", "Serviço B", "Running", "Auto")
        };

        var report = ServiceDependencyAnalyzer.Analyze(services, sourceComplete: false);

        Assert.Contains(report.Findings, item => item.Name == "Serviço A" && item.Detail.Contains("referência não resolvida"));
        Assert.Contains(report.Findings, item => item.Name == "Serviço A" && item.Detail.Contains("grupo +NetworkProvider"));
        Assert.Contains(report.Findings, item => item.Name == "Serviço B" && item.Detail.Contains("não informadas"));
        Assert.Contains("incompletos", report.Summary);
    }

    [Fact]
    public void UnavailableInventoryIsNotReportedAsEmpty()
    {
        var report = ServiceDependencyAnalyzer.Analyze(null);
        Assert.Contains("indisponível", report.Summary);
        Assert.Empty(report.Findings);
    }
}
