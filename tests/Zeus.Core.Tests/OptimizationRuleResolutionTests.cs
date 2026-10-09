using Zeus.Core;

namespace Zeus.Core.Tests;

public sealed class OptimizationRuleResolutionTests
{
    private const string Capacity = "memory.capacity";
    private const string Pressure = "memory.pressure";
    private const string Storage = "storage.free-space";
    private const ulong GiB = 1024UL * 1024 * 1024;
    private static readonly OptimizationWorkloadEvidence Workload = new(90, 7, true, true, true, 6, 12, true, 6, 12);

    [Fact]
    public void SuppressedMiddleRuleCannotRemoveAnIndependentSuggestion()
    {
        var rules = new[]
        {
            Definition(Capacity, RuleConfidence.High, conflicts: [Pressure]),
            Definition(Pressure, RuleConfidence.Medium, conflicts: [Storage]),
            Definition(Storage, RuleConfidence.Medium)
        };
        foreach (var order in Permutations(rules))
        {
            var plan = Evaluate(order);
            Assert.Equal(new[] { Capacity, Storage }, Selected(plan));
            Assert.Single(plan.Conflicts);
            Assert.Empty(plan.UnmetDependencies);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PrerequisiteIsReservedByCandidatePriorityEvenForOneSidedConflict(bool declareOnDependency)
    {
        var rules = new[]
        {
            Definition(Capacity, RuleConfidence.High, depends: [Pressure]),
            Definition(Pressure, RuleConfidence.Low, conflicts: declareOnDependency ? [Storage] : []),
            Definition(Storage, RuleConfidence.Medium, conflicts: declareOnDependency ? [] : [Pressure])
        };
        foreach (var order in Permutations(rules))
        {
            var plan = Evaluate(order);
            Assert.Equal(new[] { Capacity, Pressure }, Selected(plan));
            var resolution = Assert.Single(plan.Conflicts);
            Assert.Contains($"conjunto prioritário de {Capacity}", resolution);
            Assert.Contains("High", resolution);
            Assert.DoesNotContain("confiança Low", resolution);
        }
    }

    [Fact]
    public void ImpossibleHighPriorityBundleNeverSuppressesViableAlternative()
    {
        var rules = new[]
        {
            Definition(Capacity, RuleConfidence.High, depends: [Pressure], conflicts: [Pressure]),
            Definition(Pressure, RuleConfidence.Low, conflicts: [Storage]),
            Definition(Storage, RuleConfidence.Medium)
        };
        foreach (var order in Permutations(rules))
        {
            var plan = Evaluate(order);
            Assert.Equal(new[] { Storage }, Selected(plan));
            Assert.Equal(OptimizationPlanStatus.PrerequisitesNotMet, plan.Status);
            Assert.Contains(plan.UnmetDependencies, detail => detail.Contains("incompatíveis", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void MissingTransitiveDependencyDoesNotReserveOrDiscardOtherCandidates()
    {
        var rules = new[]
        {
            Definition(Capacity, RuleConfidence.High, depends: [Pressure]),
            Definition(Pressure, RuleConfidence.Low, depends: [Storage]),
            Definition(Storage, RuleConfidence.Medium, profiles: [OptimizationProfile.Work])
        };
        var plan = Evaluate(rules);
        Assert.Empty(Selected(plan));
        Assert.Equal(OptimizationPlanStatus.PrerequisitesNotMet, plan.Status);
        Assert.Equal(2, plan.UnmetDependencies.Count);
    }

    [Fact]
    public void ConflictBetweenTwoPrerequisitesRejectsWholeParentBundle()
    {
        var plan = Evaluate([
            Definition(Capacity, RuleConfidence.High, depends: [Pressure, Storage]),
            Definition(Pressure, RuleConfidence.Medium, conflicts: [Storage]),
            Definition(Storage, RuleConfidence.Low)]);
        Assert.Equal(new[] { Pressure }, Selected(plan));
        Assert.Contains(plan.UnmetDependencies, detail => detail.Contains(Capacity, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void CyclicDefinitionsAreRejectedEvenForInactiveRules(int nodes)
    {
        var rules = nodes == 2
            ? new[] { Definition(Capacity, depends: [Storage]), Definition(Storage, depends: [Capacity]) }
            : new[] { Definition(Capacity, depends: [Pressure]), Definition(Pressure, depends: [Storage]), Definition(Storage, depends: [Capacity]) };
        var error = Assert.Throws<ArgumentException>(() => new OptimizationRuleEngine(rules));
        Assert.Contains("ciclo", error.Message);
    }

    [Fact]
    public void ValidDependencyChainKeepsAllPrerequisites()
    {
        var plan = Evaluate([
            Definition(Capacity, RuleConfidence.High, depends: [Pressure]),
            Definition(Pressure, RuleConfidence.Medium, depends: [Storage]),
            Definition(Storage, RuleConfidence.Low)]);
        Assert.Equal(new[] { Capacity, Pressure, Storage }, Selected(plan));
        Assert.Empty(plan.Conflicts);
        Assert.Empty(plan.UnmetDependencies);
    }

    [Fact]
    public void CallerCannotMutateDefinitionsAfterConstruction()
    {
        var profiles = new HashSet<OptimizationProfile> { OptimizationProfile.General };
        var dependencies = new List<string> { Pressure };
        var conflicts = new List<string>();
        var rules = new[]
        {
            Definition(Capacity, RuleConfidence.High) with { CompatibleProfiles = profiles, DependsOn = dependencies, ConflictsWith = conflicts },
            Definition(Pressure, RuleConfidence.Medium)
        };
        var engine = new OptimizationRuleEngine(rules);
        profiles.Clear(); dependencies.Add(Capacity); conflicts.Add(Pressure);
        var stored = engine.GetDefinitions().Single(rule => rule.Id == Capacity);
        Assert.Contains(OptimizationProfile.General, stored.CompatibleProfiles);
        Assert.Equal(new[] { Pressure }, stored.DependsOn);
        Assert.Empty(stored.ConflictsWith!);
        Assert.Throws<NotSupportedException>(() => ((ISet<OptimizationProfile>)stored.CompatibleProfiles).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<string>)stored.DependsOn).Add(Capacity));
        Assert.Equal(new[] { Capacity, Pressure }, Selected(engine.Evaluate(Snapshot(), OptimizationProfile.General, Workload)));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-1)]
    [InlineData(101)]
    public void InvalidCpuNeverBecomesAvailableEvidenceOrATrigger(double cpu)
    {
        foreach (var profile in new[] { OptimizationProfile.Gaming, OptimizationProfile.Work })
        {
            var plan = new OptimizationRuleEngine().Evaluate(Snapshot(), profile, Workload with { CpuPercent = cpu });
            var result = Assert.Single(plan.Rules, rule => rule.Rule.Id == (profile == OptimizationProfile.Gaming ? "gaming.cpu-load" : "workload.cpu-load"));
            Assert.False(result.EvidenceAvailable);
            Assert.False(result.Triggered);
            Assert.Equal(OptimizationPlanStatus.NeedsMoreData, plan.Status);
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(85, true)]
    [InlineData(100, true)]
    public void CpuBoundariesRemainValid(double cpu, bool triggered)
    {
        var plan = new OptimizationRuleEngine().Evaluate(Snapshot(), OptimizationProfile.Work, Workload with { CpuPercent = cpu });
        var result = Assert.Single(plan.Rules, rule => rule.Rule.Id == "workload.cpu-load");
        Assert.True(result.EvidenceAvailable);
        Assert.Equal(triggered, result.Triggered);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-1)]
    [InlineData(9.9)]
    public void InvalidOrInsufficientWindowCannotAssertMemoryOrGpuSignal(double seconds)
    {
        var workload = Workload with { MemoryPressureWindowSeconds = seconds, GpuMemoryOccupancyWindowSeconds = seconds };
        var plan = new OptimizationRuleEngine().Evaluate(Snapshot(), OptimizationProfile.Gaming, workload);
        Assert.All(plan.Rules.Where(rule => rule.Rule.Id is Pressure or "gaming.memory-pressure" or "gaming.gpu-memory-occupancy"), rule =>
        {
            Assert.False(rule.EvidenceAvailable);
            Assert.False(rule.Triggered);
        });
        Assert.Equal(OptimizationPlanStatus.NeedsMoreData, plan.Status);
    }

    [Fact]
    public void MetadataEnumsAndEmptyProfilesAreRejected()
    {
        var definition = Definition(Capacity);
        Assert.Throws<ArgumentException>(() => new OptimizationRuleEngine([definition with { Confidence = (RuleConfidence)99 }]));
        Assert.Throws<ArgumentException>(() => new OptimizationRuleEngine([definition with { Benefit = (OptimizationBenefit)99 }]));
        Assert.Throws<ArgumentException>(() => new OptimizationRuleEngine([definition with { CompatibleProfiles = new HashSet<OptimizationProfile>() }]));
        Assert.Throws<ArgumentException>(() => new OptimizationRuleEngine([definition with { CompatibleProfiles = new HashSet<OptimizationProfile> { (OptimizationProfile)99 } }]));
    }

    private static OptimizationRuleDefinition Definition(string id, RuleConfidence confidence = RuleConfidence.Medium,
        string[]? depends = null, string[]? conflicts = null, OptimizationProfile[]? profiles = null) =>
        new OptimizationRuleEngine().GetDefinitions().Single(rule => rule.Id == id) with
        {
            Confidence = confidence, DependsOn = depends ?? [], ConflictsWith = conflicts ?? [],
            CompatibleProfiles = new HashSet<OptimizationProfile>(profiles ?? [OptimizationProfile.General])
        };

    private static OptimizationPlan Evaluate(IEnumerable<OptimizationRuleDefinition> rules) =>
        new OptimizationRuleEngine(rules).Evaluate(Snapshot(), OptimizationProfile.General, Workload);

    private static string[] Selected(OptimizationPlan plan) =>
        plan.Rules.Where(rule => rule.Triggered).Select(rule => rule.Rule.Id).Order(StringComparer.Ordinal).ToArray();

    private static IEnumerable<OptimizationRuleDefinition[]> Permutations(OptimizationRuleDefinition[] rules) =>
        from first in Enumerable.Range(0, rules.Length)
        from second in Enumerable.Range(0, rules.Length) where first != second
        from third in Enumerable.Range(0, rules.Length) where third != first && third != second
        select new[] { rules[first], rules[second], rules[third] };

    private static HardwareSnapshot Snapshot() => new(
        DateTimeOffset.UnixEpoch, "Microsoft Windows 11 Pro", "fixture", new("fixture CPU", 4, 8),
        new(4 * GiB, 300UL * 1024 * 1024), [], [new("fixture disk", "C:", 100 * GiB, 5 * GiB, "NTFS")],
        [], new(true, true, DateTimeOffset.UnixEpoch, "fixture"), []);
}
