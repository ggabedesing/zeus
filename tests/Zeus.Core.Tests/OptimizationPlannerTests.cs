using Zeus.Core;

namespace Zeus.Core.Tests;

public sealed class OptimizationPlannerTests
{
    private const ulong GiB = 1024UL * 1024 * 1024;
    private static readonly DateTimeOffset CollectedAt = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    private readonly OptimizationPlanner planner = new();
    private readonly OptimizationRuleEngine ruleEngine = new();

    [Fact]
    public void FormalPlanDistinguishesNoReviewFromUnavailableEvidence()
    {
        var healthy = ruleEngine.Evaluate(HealthySnapshot(), OptimizationProfile.Gaming);
        var incomplete = ruleEngine.Evaluate(HealthySnapshot() with { Memory = null, Disks = [], Security = null }, OptimizationProfile.Gaming);

        Assert.Equal(OptimizationPlanStatus.NoOptimizationRequired, healthy.Status);
        Assert.Equal(OptimizationPlanStatus.NeedsMoreData, incomplete.Status);
        Assert.All(healthy.Rules, result => Assert.Null(result.Action));
        Assert.Empty(healthy.Conflicts);
        Assert.Empty(healthy.UnmetDependencies);
    }

    [Fact]
    public void FormalDefinitionsExposeProfileMatrixEvidenceConfidenceAndTestPlan()
    {
        var definitions = ruleEngine.GetDefinitions();

        Assert.NotEmpty(definitions);
        Assert.All(definitions, rule =>
        {
            Assert.False(string.IsNullOrWhiteSpace(rule.Id));
            Assert.NotEmpty(rule.CompatibleProfiles);
            Assert.False(string.IsNullOrWhiteSpace(rule.EvidenceRequired));
            Assert.False(string.IsNullOrWhiteSpace(rule.TestPlan));
            Assert.True(Enum.IsDefined(rule.Confidence));
        });
        Assert.Contains(definitions, rule => rule.CompatibleProfiles.Contains(OptimizationProfile.GamingStreaming));
    }

    [Fact]
    public void FormalPlanCarriesReviewEvidenceAndDoesNotPromoteItToAction()
    {
        var snapshot = HealthySnapshot() with { Memory = new MemoryInfo(10 * GiB, GiB) };

        var plan = ruleEngine.Evaluate(snapshot, OptimizationProfile.General);
        var rule = Assert.Single(plan.Rules, result => result.Rule.Id == "memory.pressure");

        Assert.Equal(OptimizationPlanStatus.RecommendationsAvailable, plan.Status);
        Assert.True(rule.EvidenceAvailable);
        Assert.True(rule.Triggered);
        Assert.Null(rule.Action);
        Assert.Contains("paginação", rule.Rule.TestPlan);
    }

    [Fact]
    public void FailedStartupCollectionIsUnknownRatherThanAnEmptyConfirmedInventory()
    {
        var snapshot = HealthySnapshot() with { Warnings = ["Inicialização: o provedor excedeu o prazo de consulta; dados indisponíveis."] };

        var plan = ruleEngine.Evaluate(snapshot, OptimizationProfile.Work);
        var startup = Assert.Single(plan.Rules, result => result.Rule.Id == "startup.review");

        Assert.Equal(OptimizationPlanStatus.NeedsMoreData, plan.Status);
        Assert.False(startup.EvidenceAvailable);
        Assert.False(startup.Triggered);
        Assert.Contains("não estão disponíveis", startup.Reason);
    }

    [Fact]
    public void ConflictingRulesKeepTheHigherConfidenceSuggestionAndRecordResolution()
    {
        var rules = ruleEngine.GetDefinitions().Where(rule => rule.Id is "memory.pressure" or "memory.capacity").ToArray();
        var conflicting = rules.Select(rule => rule with
        {
            ConflictsWith = [rule.Id == "memory.pressure" ? "memory.capacity" : "memory.pressure"]
        }).ToArray();
        var engine = new OptimizationRuleEngine(conflicting);
        var snapshot = HealthySnapshot() with { Memory = new MemoryInfo(4 * GiB, 300UL * 1024 * 1024) };

        var plan = engine.Evaluate(snapshot, OptimizationProfile.General);

        Assert.Single(plan.Conflicts);
        Assert.Contains(plan.Rules, result => result.Rule.Id == "memory.pressure" && result.Triggered);
        Assert.Contains(plan.Rules, result => result.Rule.Id == "memory.capacity" && !result.Triggered && result.Action is null);
    }

    [Fact]
    public void UnmetRuleDependencySuppressesSuggestionAndDoesNotClaimNoOptimizationIsNeeded()
    {
        var storage = ruleEngine.GetDefinitions().Single(rule => rule.Id == "storage.free-space") with { DependsOn = ["memory.pressure"] };
        var engine = new OptimizationRuleEngine([ruleEngine.GetDefinitions().Single(rule => rule.Id == "memory.pressure"), storage]);
        var snapshot = HealthySnapshot() with { Disks = [new DiskInfo("SSD", "C:", 100 * GiB, 5 * GiB, "NTFS")] };

        var plan = engine.Evaluate(snapshot, OptimizationProfile.General);

        Assert.Equal(OptimizationPlanStatus.PrerequisitesNotMet, plan.Status);
        Assert.NotEmpty(plan.UnmetDependencies);
        Assert.Contains(plan.Rules, result => result.Rule.Id == "storage.free-space" && !result.Triggered);
    }

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
