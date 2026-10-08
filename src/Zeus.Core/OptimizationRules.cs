namespace Zeus.Core;

/// <summary>Usage intent used to explain which review rules apply. It does not alter Windows settings.</summary>
public enum OptimizationProfile
{
    General,
    Gaming,
    GamingStreaming,
    Work,
    Editing,
    Development
}

public enum OptimizationBenefit { MemoryCapacity, MemoryPressure, StorageCapacity, StartupReview, SecurityReview, DataQuality }
public enum RuleConfidence { Low, Medium, High }
public enum OptimizationPlanStatus { RecommendationsAvailable, NoOptimizationRequired, NeedsMoreData, PrerequisitesNotMet }

public sealed record OptimizationRuleDefinition(
    string Id,
    string Title,
    OptimizationBenefit Benefit,
    RuleConfidence Confidence,
    IReadOnlySet<OptimizationProfile> CompatibleProfiles,
    string EvidenceRequired,
    string TestPlan,
    IReadOnlyList<string> DependsOn,
    bool IsReviewOnly,
    IReadOnlyList<string>? ConflictsWith = null);

public sealed record OptimizationRuleResult(
    OptimizationRuleDefinition Rule,
    string Reason,
    bool EvidenceAvailable,
    bool Triggered,
    MaintenanceActionId? Action);

public sealed record OptimizationPlan(
    OptimizationProfile Profile,
    OptimizationPlanStatus Status,
    IReadOnlyList<OptimizationRuleResult> Rules,
    IReadOnlyList<string> Conflicts,
    IReadOnlyList<string> UnmetDependencies);

/// <summary>
/// Formal metadata and evaluation for evidence-based review rules. The plan intentionally
/// produces no executable actions; applying maintenance remains in the consent-gated engine.
/// </summary>
public sealed class OptimizationRuleEngine
{
    private static readonly IReadOnlySet<OptimizationProfile> AllProfiles = new HashSet<OptimizationProfile>(Enum.GetValues<OptimizationProfile>());

    private static readonly OptimizationRuleDefinition[] DefaultDefinitions =
    [
        new("memory.pressure", "Observar pressão de memória", OptimizationBenefit.MemoryPressure, RuleConfidence.Medium, AllProfiles,
            "TotalBytes e AvailableBytes válidos; leitura pontual não comprova pressão durante uma tarefa.",
            "Repetir a observação durante a tarefa habitual e conferir uso de memória e paginação.", [], true),
        new("memory.capacity", "Avaliar capacidade de RAM para o seu uso", OptimizationBenefit.MemoryCapacity, RuleConfidence.Low, AllProfiles,
            "Capacidade total de memória válida e tarefa representativa.",
            "Confirmar requisitos do aplicativo, uso durante a tarefa e compatibilidade do equipamento.", [], true),
        new("storage.free-space", "Revisar espaço de armazenamento", OptimizationBenefit.StorageCapacity, RuleConfidence.Medium, AllProfiles,
            "Tamanho total e espaço livre válidos por volume.",
            "Inspecionar categorias e arquivos antes de qualquer limpeza; comparar espaço antes e depois.", [], true),
        new("startup.review", "Revisar programas de inicialização", OptimizationBenefit.StartupReview, RuleConfidence.Low, AllProfiles,
            "Inventário de entradas de inicialização; quantidade não mede impacto.",
            "Identificar cada entrada e confirmar com a pessoa usuária quais aplicativos são necessários.", [], true),
        new("security.provider", "Confirmar proteção instalada", OptimizationBenefit.SecurityReview, RuleConfidence.Low, AllProfiles,
            "Estado do Defender; provedor alternativo pode estar ativo.",
            "Verificar provedor e políticas em Segurança do Windows sem reduzir a proteção.", [], true),
        new("diagnostics.warnings", "Leitura limitada no diagnóstico", OptimizationBenefit.DataQuality, RuleConfidence.High, AllProfiles,
            "Aviso explícito emitido pelo coletor.",
            "Resolver ou reconhecer a limitação e repetir somente a coleta afetada.", [], true)
    ];

    private readonly OptimizationPlanner planner = new();
    private readonly OptimizationRuleDefinition[] definitions;

    public OptimizationRuleEngine() : this(DefaultDefinitions) { }

    public OptimizationRuleEngine(IEnumerable<OptimizationRuleDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        this.definitions = definitions.ToArray();
        ValidateDefinitions(this.definitions);
    }

    public IReadOnlyList<OptimizationRuleDefinition> GetDefinitions() => Array.AsReadOnly(definitions);

    public OptimizationPlan Evaluate(HardwareSnapshot snapshot, OptimizationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!Enum.IsDefined(profile)) throw new ArgumentOutOfRangeException(nameof(profile));

        var recommendations = planner.Build(snapshot);
        var results = new List<OptimizationRuleResult>(definitions.Length);
        foreach (var definition in definitions)
        {
            var matches = recommendations.Where(recommendation => Matches(definition.Id, recommendation)).ToArray();
            var evidenceAvailable = HasEvidence(snapshot, definition.Id);
            var reason = matches.Length > 0
                ? string.Join(" ", matches.Select(match => match.Reason))
                : evidenceAvailable ? "A regra foi avaliada e não atingiu seu critério de revisão." : "Os dados necessários para avaliar esta regra não estão disponíveis.";
            results.Add(new OptimizationRuleResult(definition, reason, evidenceAvailable, matches.Length > 0, matches.FirstOrDefault()?.Action));
        }

        var applicable = results.Where(result => result.Rule.CompatibleProfiles.Contains(profile)).ToArray();
        var (resolved, conflicts, unmetDependencies) = Resolve(applicable, profile);
        var hasRecommendations = resolved.Any(result => result.Triggered);
        var missingEvidence = resolved.Any(result => !result.EvidenceAvailable && result.Rule.Benefit != OptimizationBenefit.DataQuality);
        var status = missingEvidence
            ? OptimizationPlanStatus.NeedsMoreData
            : unmetDependencies.Length > 0 ? OptimizationPlanStatus.PrerequisitesNotMet
            : hasRecommendations ? OptimizationPlanStatus.RecommendationsAvailable : OptimizationPlanStatus.NoOptimizationRequired;

        return new OptimizationPlan(profile, status, Array.AsReadOnly(resolved), Array.AsReadOnly(conflicts), Array.AsReadOnly(unmetDependencies));
    }

    private (OptimizationRuleResult[] Results, string[] Conflicts, string[] UnmetDependencies) Resolve(
        OptimizationRuleResult[] applicable, OptimizationProfile profile)
    {
        var resolved = applicable.ToDictionary(result => result.Rule.Id, StringComparer.Ordinal);
        var unmet = new List<string>();
        var conflicts = new List<string>();

        // Resolve prerequisites to a fixed point so a blocked prerequisite also blocks its dependents.
        bool changed;
        do
        {
            changed = false;
            foreach (var rule in resolved.Values.Where(result => result.Triggered).ToArray())
            {
                foreach (var dependencyId in rule.Rule.DependsOn)
                {
                    if (!resolved.TryGetValue(dependencyId, out var dependency) || !dependency.Triggered || !dependency.EvidenceAvailable)
                    {
                        var detail = $"{rule.Rule.Id} depende de {dependencyId}, que não foi atendida para o perfil {profile}.";
                        if (!unmet.Contains(detail, StringComparer.Ordinal)) unmet.Add(detail);
                        resolved[rule.Rule.Id] = rule with
                        {
                            Triggered = false,
                            Action = null,
                            Reason = rule.Reason + $" Esta sugestão ficou pendente porque falta o pré-requisito {dependencyId}."
                        };
                        changed = true;
                        break;
                    }
                }
            }
        } while (changed);

        var handledPairs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in resolved.Values.Where(result => result.Triggered).OrderBy(result => result.Rule.Id, StringComparer.Ordinal).ToArray())
        {
            foreach (var conflictId in rule.Rule.ConflictsWith ?? [])
            {
                if (!resolved.TryGetValue(conflictId, out var other) || !other.Triggered) continue;
                var pair = string.CompareOrdinal(rule.Rule.Id, conflictId) < 0 ? $"{rule.Rule.Id}|{conflictId}" : $"{conflictId}|{rule.Rule.Id}";
                if (!handledPairs.Add(pair)) continue;

                var winner = ComparePriority(rule, other) >= 0 ? rule : other;
                var loser = winner.Rule.Id == rule.Rule.Id ? other : rule;
                var detail = $"Conflito entre {rule.Rule.Id} e {other.Rule.Id}: mantida {winner.Rule.Id} pela confiança {winner.Rule.Confidence}; {loser.Rule.Id} foi suprimida.";
                conflicts.Add(detail);
                resolved[loser.Rule.Id] = loser with
                {
                    Triggered = false,
                    Action = null,
                    Reason = loser.Reason + $" Sugestão suprimida por conflito com {winner.Rule.Id}."
                };
            }
        }

        return (definitions.Select(definition => resolved.TryGetValue(definition.Id, out var result) ? result : null)
            .Where(result => result is not null).Cast<OptimizationRuleResult>().ToArray(), conflicts.ToArray(), unmet.ToArray());
    }

    private static int ComparePriority(OptimizationRuleResult left, OptimizationRuleResult right)
    {
        var confidence = left.Rule.Confidence.CompareTo(right.Rule.Confidence);
        return confidence != 0 ? confidence : -string.CompareOrdinal(left.Rule.Id, right.Rule.Id);
    }

    private static void ValidateDefinitions(IReadOnlyList<OptimizationRuleDefinition> definitions)
    {
        if (definitions.Any(definition => string.IsNullOrWhiteSpace(definition.Id)) ||
            definitions.Select(definition => definition.Id).Distinct(StringComparer.Ordinal).Count() != definitions.Count)
            throw new ArgumentException("As regras precisam ter IDs únicos e não vazios.", nameof(definitions));

        var ids = definitions.Select(definition => definition.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            if (definition.DependsOn is null || definition.CompatibleProfiles is null || definition.ConflictsWith?.Contains(definition.Id, StringComparer.Ordinal) == true ||
                definition.DependsOn.Contains(definition.Id, StringComparer.Ordinal) ||
                definition.DependsOn.Concat(definition.ConflictsWith ?? []).Any(id => !ids.Contains(id)))
                throw new ArgumentException($"A regra {definition.Id} contém dependência/conflito próprio ou desconhecido.", nameof(definitions));
        }
    }

    private static bool Matches(string ruleId, Recommendation recommendation) => ruleId switch
    {
        "memory.pressure" => recommendation.Title == "Observar pressão de memória",
        "memory.capacity" => recommendation.Title == "Avaliar capacidade de RAM para o seu uso",
        "storage.free-space" => recommendation.Title.StartsWith("Revisar espaço em ", StringComparison.Ordinal),
        "startup.review" => recommendation.Title == "Revisar programas de inicialização",
        "security.provider" => recommendation.Title is "Confirmar proteção instalada" or "Revisar o provedor de segurança" or "Verificar proteção em tempo real" or "Atualizar definições do Defender",
        "diagnostics.warnings" => recommendation.Title == "Leitura limitada no diagnóstico",
        _ => false
    };

    private static bool HasEvidence(HardwareSnapshot snapshot, string ruleId) => ruleId switch
    {
        "memory.pressure" or "memory.capacity" => snapshot.Memory is { TotalBytes: > 0 } memory && memory.AvailableBytes <= memory.TotalBytes,
        "storage.free-space" => snapshot.Disks.Any(disk => disk.TotalBytes > 0 && disk.FreeBytes <= disk.TotalBytes),
        "startup.review" => !SourceUnavailable(snapshot, "Inicialização"),
        "security.provider" => !snapshot.OperatingSystem.Contains("Windows", StringComparison.OrdinalIgnoreCase) || snapshot.Security?.DefenderEnabled is not null,
        "diagnostics.warnings" => true,
        _ => false
    };

    private static bool SourceUnavailable(HardwareSnapshot snapshot, string source) => snapshot.Warnings.Any(warning =>
        warning.StartsWith(source + ":", StringComparison.OrdinalIgnoreCase));
}
