using Zeus.Core;

namespace Zeus.Core.Tests;

public sealed class OptimizationPlannerTests
{
    private const ulong GiB = 1024UL * 1024 * 1024;
    private static readonly DateTimeOffset CollectedAt = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    private readonly OptimizationPlanner planner = new();

    [Fact]
    public void HealthySnapshotDoesNotInventProblemsOrPromiseAnUpgrade()
    {
        var recommendations = planner.Build(HealthySnapshot());

        Assert.Empty(recommendations);
    }

    [Fact]
    public void LowAvailableMemoryIsAReviewSuggestionRatherThanARepair()
    {
        var snapshot = HealthySnapshot() with { Memory = new MemoryInfo(10 * GiB, GiB) };

        var recommendation = Assert.Single(planner.Build(snapshot));

        Assert.Contains("memória", recommendation.Title);
        Assert.Contains("10%", recommendation.Reason);
        Assert.Contains("hipótese", recommendation.Reason);
        Assert.Null(recommendation.Action);
    }

    [Fact]
    public void LowCapacityDoesNotPretendThereIsMeasuredMemoryPressure()
    {
        var snapshot = HealthySnapshot() with { Memory = new MemoryInfo(4 * GiB, 3 * GiB) };

        var recommendation = Assert.Single(planner.Build(snapshot));

        Assert.Contains("capacidade", recommendation.Title);
        Assert.Contains("compatibilidade", recommendation.Reason);
        Assert.DoesNotContain("pressão", recommendation.Title);
    }

    [Theory]
    [InlineData(100, 15)]
    [InlineData(20, 9)]
    public void DiskReviewUsesBothRelativeAndAbsoluteFreeSpace(ulong totalGiB, ulong freeGiB)
    {
        var snapshot = HealthySnapshot() with
        {
            Disks = [new DiskInfo("Disco", "C:", totalGiB * GiB, freeGiB * GiB, "NTFS")]
        };

        var recommendation = Assert.Single(planner.Build(snapshot));

        Assert.Contains("C:", recommendation.Title);
        Assert.Contains("antes de apagar", recommendation.Reason);
        Assert.Null(recommendation.Action);
    }

    [Fact]
    public void InvalidReadingsDoNotBecomeZeroHealthOrStorageProblems()
    {
        var snapshot = HealthySnapshot() with
        {
            Memory = new MemoryInfo(GiB, 2 * GiB),
            Disks = [
                new DiskInfo("Não disponível", "D:", 0, 0, ""),
                new DiskInfo("Inválido", "E:", GiB, 2 * GiB, "NTFS")]
        };

        var recommendation = Assert.Single(planner.Build(snapshot));

        Assert.Equal("Medir uso de memória", recommendation.Title);
        Assert.Null(recommendation.Action);
    }

    [Fact]
    public void MissingSensorsAndCollectionWarningsStayExplicitlyUnknown()
    {
        var snapshot = HealthySnapshot() with
        {
            Cpu = null,
            Memory = null,
            Graphics = [],
            Disks = [],
            Security = null,
            Warnings = ["Sensor de temperatura indisponível.", ""]
        };

        var recommendations = planner.Build(snapshot);

        Assert.Equal(3, recommendations.Count);
        Assert.Contains(recommendations, recommendation => recommendation.Reason.Contains("Não há uma leitura válida"));
        Assert.Contains(recommendations, recommendation => recommendation.Reason.Contains("não foi obtido"));
        Assert.Contains(recommendations, recommendation => recommendation.Reason == "Sensor de temperatura indisponível.");
        Assert.All(recommendations, recommendation => Assert.Null(recommendation.Action));
    }

    [Fact]
    public void DisabledDefenderDoesNotAssumeThereIsNoAntivirusOrOfferToOverrideIt()
    {
        var snapshot = HealthySnapshot() with
        {
            Security = new SecurityInfo(false, false, null, "Outro provedor pode estar ativo.")
        };

        var recommendation = Assert.Single(planner.Build(snapshot));

        Assert.Contains("outro antivírus", recommendation.Reason);
        Assert.Null(recommendation.Action);
    }

    [Fact]
    public void DefenderRealTimeWarningDoesNotConfuseScanningWithEnablingProtection()
    {
        var snapshot = HealthySnapshot() with
        {
            Security = new SecurityInfo(true, false, CollectedAt, "Proteção desativada.")
        };

        var recommendation = Assert.Single(planner.Build(snapshot));

        Assert.Contains("não reativa", recommendation.Reason);
        Assert.Null(recommendation.Action);
    }

    [Fact]
    public void StaleDefinitionsAreComparedWithCollectionTimeRatherThanTheCurrentClock()
    {
        var snapshot = HealthySnapshot() with
        {
            Security = new SecurityInfo(true, true, CollectedAt.AddDays(-8), "Ativo")
        };

        var recommendation = Assert.Single(planner.Build(snapshot));

        Assert.Equal("Atualizar definições do Defender", recommendation.Title);
    }

    [Fact]
    public void FutureSignatureTimestampDoesNotTriggerAnInventedOutdatedWarning()
    {
        var snapshot = HealthySnapshot() with
        {
            Security = new SecurityInfo(true, true, CollectedAt.AddDays(1), "Ativo")
        };

        Assert.Empty(planner.Build(snapshot));
    }

    [Fact]
    public void RecommendationsNeverSelectRepairsFromResourceReadingsAlone()
    {
        var snapshot = HealthySnapshot() with
        {
            Memory = new MemoryInfo(4 * GiB, 0),
            Disks = [new DiskInfo("SSD", "C:", 100 * GiB, GiB, "NTFS")],
            Startup = Enumerable.Range(1, 8).Select(index => new StartupInfo($"App {index}", "Registro", "Usuário")).ToArray(),
            Security = new SecurityInfo(true, false, CollectedAt.AddDays(-8), "Ativo"),
            Warnings = ["A consulta de inventário falhou parcialmente."]
        };

        var recommendations = planner.Build(snapshot);

        Assert.NotEmpty(recommendations);
        Assert.DoesNotContain(recommendations, recommendation => recommendation.Action is
            MaintenanceActionId.RepairWindowsImage or MaintenanceActionId.RepairSystemFiles);
        Assert.Contains(recommendations, recommendation => recommendation.Title.Contains("inicialização"));
    }

    private static HardwareSnapshot HealthySnapshot() => new(
        CollectedAt,
        "Microsoft Windows 11 Pro",
        "PC-Teste",
        new CpuInfo("CPU de teste", 4, 8),
        new MemoryInfo(16 * GiB, 8 * GiB),
        [new GpuInfo("GPU de teste", "1.0")],
        [new DiskInfo("SSD", "C:", 256 * GiB, 100 * GiB, "NTFS")],
        [],
        new SecurityInfo(true, true, CollectedAt, "Defender ativo"),
        []);
}
