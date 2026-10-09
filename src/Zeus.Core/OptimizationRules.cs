using System.Collections.Frozen;

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

public enum OptimizationBenefit { MemoryCapacity, MemoryPressure, StorageCapacity, StartupReview, SecurityReview, DataQuality, WorkloadDiagnosis }
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

/// <summary>Explicitly sampled workload evidence; false process heuristics do not prove absence.</summary>
public sealed record OptimizationWorkloadEvidence(
    double? CpuPercent,
    double? AvailableMemoryPercent,
    bool? KnownGameProcessDetected,
    bool? ObsProcessDetected,
    bool? SustainedHighGpuMemoryOccupancy = null,
    int? GpuMemoryOccupancyValidSamples = null,
    double? GpuMemoryOccupancyWindowSeconds = null,
    bool? SustainedLowMemoryWithPageReads = null,
    int? MemoryPressureValidSamples = null,
    double? MemoryPressureWindowSeconds = null);

/// <summary>
/// Formal metadata and evaluation for evidence-based review rules. The plan intentionally
/// produces no executable actions; applying maintenance remains in the consent-gated engine.
/// </summary>
public sealed class OptimizationRuleEngine
{
    private const int MinimumGpuOccupancySamples = 5;
    private const double MinimumGpuOccupancyWindowSeconds = 10;
    private static readonly IReadOnlySet<OptimizationProfile> AllProfiles = new HashSet<OptimizationProfile>(Enum.GetValues<OptimizationProfile>());

    private static readonly OptimizationRuleDefinition[] DefaultDefinitions =
    [
        new("memory.pressure", "Observar pressão de memória", OptimizationBenefit.MemoryPressure, RuleConfidence.Medium, AllProfiles,
            "Pelo menos cinco amostras em dez segundos com RAM disponível e Page Reads/sec válidos.",
            "Repetir durante a tarefa real e revisar o uso dos aplicativos; Page Reads/sec pode incluir executáveis, DLLs e arquivos mapeados e não prova falha de RAM.", [], true),
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
            "Resolver ou reconhecer a limitação e repetir somente a coleta afetada.", [], true),
        new("gaming.cpu-load", "Validar carga de CPU durante jogos", OptimizationBenefit.WorkloadDiagnosis, RuleConfidence.Low,
            new HashSet<OptimizationProfile> { OptimizationProfile.Gaming, OptimizationProfile.GamingStreaming },
            "Amostra de CPU e processo de jogo conhecido presente na lista observada; presença não confirma partida.",
            "Repetir a amostra durante uma partida e comparar carga total com processo, GPU, temperatura e limites do jogo.", [], true),
        new("gaming.streaming-context", "Medir jogo e OBS em conjunto", OptimizationBenefit.WorkloadDiagnosis, RuleConfidence.Low,
            new HashSet<OptimizationProfile> { OptimizationProfile.GamingStreaming },
            "Processos conhecidos de jogo e OBS presentes na lista observada; isso não comprova partida ou transmissão ao vivo.",
            "Repetir observações durante a partida e transmissão reais; comparar CPU, GPU, memória e codificador.", [], true),
        new("gaming.memory-pressure", "Revisar memória disponível durante jogos", OptimizationBenefit.MemoryPressure, RuleConfidence.Low,
            new HashSet<OptimizationProfile> { OptimizationProfile.Gaming, OptimizationProfile.GamingStreaming },
            "Memória disponível na amostra e processo de jogo conhecido presente na lista observada.",
            "Repetir durante a partida, conferir paginação e processos que usam memória; não esvaziar RAM automaticamente.", [], true),
        new("gaming.gpu-memory-occupancy", "Investigar ocupação sustentada da memória dedicada da GPU", OptimizationBenefit.WorkloadDiagnosis, RuleConfidence.Low,
            new HashSet<OptimizationProfile> { OptimizationProfile.Gaming, OptimizationProfile.GamingStreaming },
            "Ocupação dedicada válida por adaptador em pelo menos cinco amostras ao longo de dez segundos.",
            "Repetir durante a mesma tarefa e verificar configurações do jogo, uso compartilhado e fluidez; ocupação alta não comprova pressão, gargalo ou perda de desempenho.", [], true),
        new("workload.cpu-load", "Validar carga de CPU durante a tarefa", OptimizationBenefit.WorkloadDiagnosis, RuleConfidence.Low,
            new HashSet<OptimizationProfile> { OptimizationProfile.Work, OptimizationProfile.Editing, OptimizationProfile.Development },
            "Amostra válida de CPU durante a tarefa selecionada.",
            "Repetir durante a tarefa e comparar CPU e processos; carga alta isolada não identifica causa nem prova gargalo.", [], true)
    ];

    private readonly OptimizationPlanner planner = new();
    private readonly OptimizationRuleDefinition[] definitions;

    public OptimizationRuleEngine() : this(DefaultDefinitions) { }

    public OptimizationRuleEngine(IEnumerable<OptimizationRuleDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        this.definitions = definitions.ToArray();
        ValidateDefinitions(this.definitions);
        this.definitions = this.definitions.Select(definition => definition with
        {
            CompatibleProfiles = definition.CompatibleProfiles.ToFrozenSet(),
            DependsOn = Array.AsReadOnly(definition.DependsOn.ToArray()),
            ConflictsWith = Array.AsReadOnly((definition.ConflictsWith ?? []).ToArray())
        }).ToArray();
    }

    public IReadOnlyList<OptimizationRuleDefinition> GetDefinitions() => Array.AsReadOnly(definitions);

    public OptimizationPlan Evaluate(HardwareSnapshot snapshot, OptimizationProfile profile, OptimizationWorkloadEvidence? workload = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!Enum.IsDefined(profile)) throw new ArgumentOutOfRangeException(nameof(profile));

        var recommendations = planner.Build(snapshot);
        var results = new List<OptimizationRuleResult>(definitions.Length);
        foreach (var definition in definitions)
        {
            if (definition.Id == "memory.pressure")
            {
                var samples = workload?.MemoryPressureValidSamples;
                var window = workload?.MemoryPressureWindowSeconds;
                var sufficientWindow = IsSufficientWindow(samples, window, 5, 10);
                var pressureEvidenceAvailable = sufficientWindow && workload?.SustainedLowMemoryWithPageReads is not null;
                var pressureTriggered = pressureEvidenceAvailable && workload!.SustainedLowMemoryWithPageReads == true;
                var evidence = samples is { } count && window is { } seconds
                    ? $" Evidência: {count} amostras válidas em {seconds:0.#} s."
                    : string.Empty;
                var pressureReason = pressureTriggered
                    ? "A janela manteve memória disponível baixa junto com leituras de páginas do disco. É um sinal para investigar a tarefa, não um diagnóstico de falta de RAM." + evidence
                    : pressureEvidenceAvailable
                        ? "A janela não mostrou memória disponível baixa junto com leituras de páginas sustentadas." + evidence
                        : "Ainda não há cinco amostras válidas ao longo de dez segundos com leitura de memória e Page Reads/sec; uma leitura isolada não comprova pressão.";
                results.Add(new(definition, pressureReason, pressureEvidenceAvailable, pressureTriggered, null));
                continue;
            }
            if (definition.Id == "gaming.cpu-load")
            {
                var workloadEvidenceAvailable = IsValidCpuPercent(workload?.CpuPercent) && workload?.KnownGameProcessDetected == true;
                var triggered = workloadEvidenceAvailable && workload!.CpuPercent >= 85;
                results.Add(new(definition,
                    triggered ? $"CPU total em {workload!.CpuPercent:0.#}% com processo de jogo conhecido entre os processos observados. Isso não comprova gargalo de CPU." :
                    workloadEvidenceAvailable ? $"CPU total em {workload!.CpuPercent:0.#}% nesta amostra; o limiar de revisão de 85% não foi atingido." :
                    "Amostra de CPU junto a um processo de jogo conhecido não disponível; ausência na lista observada não prova que o jogo esteja fechado.",
                    workloadEvidenceAvailable, triggered, null));
                continue;
            }
            if (definition.Id == "gaming.streaming-context")
            {
                var workloadEvidenceAvailable = workload is { KnownGameProcessDetected: true, ObsProcessDetected: true };
                results.Add(new(definition,
                    workloadEvidenceAvailable ? "Processos conhecidos de jogo e OBS aparecem na amostra. Isso não confirma partida nem transmissão ao vivo; meça a carga conjunta durante o uso real." :
                    "Amostra não confirmou simultaneamente processos conhecidos de jogo e OBS. A lista é limitada e isso não prova que estejam fechados.",
                    workloadEvidenceAvailable, workloadEvidenceAvailable, null));
                continue;
            }
            if (definition.Id == "gaming.memory-pressure")
            {
                var samples = workload?.MemoryPressureValidSamples;
                var window = workload?.MemoryPressureWindowSeconds;
                var sufficientWindow = IsSufficientWindow(samples, window, 5, 10);
                var workloadEvidenceAvailable = sufficientWindow && workload?.SustainedLowMemoryWithPageReads is not null &&
                    workload?.KnownGameProcessDetected == true;
                var triggered = workloadEvidenceAvailable && workload!.SustainedLowMemoryWithPageReads == true;
                var evidence = samples is { } count && window is { } seconds
                    ? $" Evidência: {count} amostras válidas em {seconds:0.#} s."
                    : string.Empty;
                results.Add(new(definition,
                    triggered ? "Durante a janela com processo de jogo conhecido houve pouca RAM disponível junto a leituras de páginas. Revise o uso da tarefa; isso não comprova gargalo nem identifica qual aplicativo causou a condição." + evidence :
                    workloadEvidenceAvailable ? "A janela não mostrou pouca RAM disponível junto a leituras de páginas sustentadas durante o processo de jogo observado." + evidence :
                    "Não há janela válida de RAM/paginação junto a um processo de jogo conhecido; ausência na lista observada não prova que o jogo esteja fechado.",
                    workloadEvidenceAvailable, triggered, null));
                continue;
            }
            if (definition.Id == "gaming.gpu-memory-occupancy")
            {
                var samples = workload?.GpuMemoryOccupancyValidSamples;
                var window = workload?.GpuMemoryOccupancyWindowSeconds;
                var windowIsValid = IsSufficientWindow(samples, window, MinimumGpuOccupancySamples, MinimumGpuOccupancyWindowSeconds);
                var workloadEvidenceAvailable = windowIsValid && workload?.SustainedHighGpuMemoryOccupancy is not null;
                var triggered = workloadEvidenceAvailable && workload!.SustainedHighGpuMemoryOccupancy == true;
                var measurement = samples is { } count && window is { } seconds
                    ? $" Evidência: {count} amostras válidas em {seconds:0.#} s."
                    : string.Empty;
                var occupancyReason = triggered
                    ? "Ao menos um adaptador apresentou ocupação dedicada alta e sustentada. Isso é um sinal para investigar; não comprova pressão, gargalo ou perda de desempenho." + measurement
                    : workloadEvidenceAvailable
                        ? "A janela observada não atingiu o critério de ocupação dedicada alta e sustentada." + measurement
                        : "Não há amostras válidas suficientes de memória dedicada da GPU; ausência de dados não indica folga.";
                results.Add(new(definition, occupancyReason, workloadEvidenceAvailable, triggered, null));
                continue;
            }
            if (definition.Id == "workload.cpu-load")
            {
                var workloadEvidenceAvailable = IsValidCpuPercent(workload?.CpuPercent);
                var triggered = workloadEvidenceAvailable && workload!.CpuPercent >= 85;
                results.Add(new(definition,
                    triggered ? $"CPU total em {workload!.CpuPercent:0.#}% durante a amostra. Carga alta isolada não identifica causa nem comprova gargalo." :
                    workloadEvidenceAvailable ? $"CPU total em {workload!.CpuPercent:0.#}% nesta amostra; o limiar de revisão de 85% não foi atingido." :
                    "Não há amostra válida de CPU durante a tarefa selecionada.",
                    workloadEvidenceAvailable, triggered, null));
                continue;
            }
            var matches = recommendations.Where(recommendation => Matches(definition.Id, recommendation)).ToArray();
            var evidenceAvailable = HasEvidence(snapshot, definition.Id);
            var reason = matches.Length > 0
                ? string.Join(" ", matches.Select(match => match.Reason))
                : evidenceAvailable ? "A regra foi avaliada e não atingiu seu critério de revisão." : "Os dados necessários para avaliar esta regra não estão disponíveis.";
            // Formal plans explain and prioritize review; executable actions remain in the
            // separate consent-gated transaction engine, even if a planner rule gains an action.
            results.Add(new OptimizationRuleResult(definition, reason, evidenceAvailable, evidenceAvailable && matches.Length > 0, null));
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
        // Greedy selection prioritizes a candidate together with its prerequisite closure.
        // Accepted bundles are indivisible; rejected candidates never suppress alternatives.
        // This is deterministic prioritization, not a maximum-benefit/maximum-count solver.
        var original = applicable.ToDictionary(result => result.Rule.Id, StringComparer.Ordinal);
        var selectedOwners = new Dictionary<string, OptimizationRuleResult>(StringComparer.Ordinal);
        var suppressedReasons = new Dictionary<string, string>(StringComparer.Ordinal);
        var unmet = new List<string>();
        var conflicts = new List<string>();
        foreach (var candidate in applicable.Where(result => result.Triggered && result.EvidenceAvailable)
            .OrderByDescending(result => result.Rule.Confidence).ThenBy(result => result.Rule.Id, StringComparer.Ordinal))
        {
            if (selectedOwners.ContainsKey(candidate.Rule.Id)) continue;
            var bundle = new Dictionary<string, OptimizationRuleResult>(StringComparer.Ordinal);
            var pending = new Stack<string>();
            pending.Push(candidate.Rule.Id);
            string? missing = null;
            while (pending.TryPop(out var id))
            {
                if (bundle.ContainsKey(id)) continue;
                if (!original.TryGetValue(id, out var dependency) || !dependency.Triggered || !dependency.EvidenceAvailable)
                {
                    missing = id;
                    break;
                }
                bundle.Add(id, dependency);
                foreach (var dependencyId in dependency.Rule.DependsOn.Order(StringComparer.Ordinal).Reverse())
                    pending.Push(dependencyId);
            }
            if (missing is not null)
            {
                var detail = $"O conjunto de {candidate.Rule.Id} depende de {missing}, que não foi atendida para o perfil {profile}.";
                unmet.Add(detail);
                suppressedReasons[candidate.Rule.Id] = detail;
                continue;
            }
            var members = bundle.Values.OrderBy(result => result.Rule.Id, StringComparer.Ordinal).ToArray();
            var internalConflict = members.SelectMany((member, index) => members.Skip(index + 1)
                .Where(other => AreConflicting(member.Rule, other.Rule)).Select(other => (member, other))).FirstOrDefault();
            if (internalConflict.member is not null)
            {
                var detail = $"O conjunto de {candidate.Rule.Id} tem pré-requisitos incompatíveis: {internalConflict.member.Rule.Id} e {internalConflict.other.Rule.Id}. Nenhuma regra foi reservada por esta candidata.";
                conflicts.Add(detail);
                unmet.Add(detail);
                suppressedReasons[candidate.Rule.Id] = detail;
                continue;
            }
            var externalConflict = members.SelectMany(member => selectedOwners.Keys.Order(StringComparer.Ordinal)
                .Where(id => id != member.Rule.Id && AreConflicting(member.Rule, original[id].Rule))
                .Select(id => (member, selectedId: id))).FirstOrDefault();
            if (externalConflict.member is not null)
            {
                var owner = selectedOwners[externalConflict.selectedId];
                var detail = $"Conflito no conjunto de {candidate.Rule.Id}: {externalConflict.member.Rule.Id} é incompatível com {externalConflict.selectedId}, mantida como parte do conjunto prioritário de {owner.Rule.Id} (confiança {owner.Rule.Confidence}; prioridade por confiança e, em empate, por ID ordinal). O conjunto de {candidate.Rule.Id} foi suprimido.";
                conflicts.Add(detail);
                suppressedReasons[candidate.Rule.Id] = detail;
                continue;
            }
            foreach (var member in members) selectedOwners.TryAdd(member.Rule.Id, candidate);
        }
        var resolved = applicable.Select(result => result.Triggered && !selectedOwners.ContainsKey(result.Rule.Id)
            ? result with { Triggered = false, Action = null, Reason = result.Reason + " " +
                suppressedReasons.GetValueOrDefault(result.Rule.Id, "Evidência insuficiente para manter a sugestão.") }
            : result).ToArray();
        return (resolved, conflicts.ToArray(), unmet.ToArray());
    }

    private static bool AreConflicting(OptimizationRuleDefinition left, OptimizationRuleDefinition right) =>
        left.ConflictsWith?.Contains(right.Id, StringComparer.Ordinal) == true ||
        right.ConflictsWith?.Contains(left.Id, StringComparer.Ordinal) == true;

    private static bool IsValidCpuPercent(double? value) => value is { } percent && double.IsFinite(percent) && percent is >= 0 and <= 100;

    private static bool IsSufficientWindow(int? samples, double? window, int minimumSamples, double minimumSeconds) =>
        samples >= minimumSamples && window is { } seconds && double.IsFinite(seconds) && seconds >= minimumSeconds;

    private static void ValidateDefinitions(IReadOnlyList<OptimizationRuleDefinition> definitions)
    {
        if (definitions.Any(definition => definition is null || string.IsNullOrWhiteSpace(definition.Id)) ||
            definitions.Select(definition => definition.Id).Distinct(StringComparer.Ordinal).Count() != definitions.Count)
            throw new ArgumentException("As regras precisam ter IDs únicos e não vazios.", nameof(definitions));

        var ids = definitions.Select(definition => definition.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            if (!definition.IsReviewOnly)
                throw new ArgumentException($"A regra {definition.Id} não é somente de revisão; ações devem usar o fluxo de transação com consentimento explícito.", nameof(definitions));

            if (!Enum.IsDefined(definition.Benefit) || !Enum.IsDefined(definition.Confidence) ||
                string.IsNullOrWhiteSpace(definition.Title) || string.IsNullOrWhiteSpace(definition.EvidenceRequired) ||
                string.IsNullOrWhiteSpace(definition.TestPlan) || definition.CompatibleProfiles is not { Count: > 0 } ||
                definition.CompatibleProfiles.Any(profile => !Enum.IsDefined(profile)))
                throw new ArgumentException($"A regra {definition.Id} contém metadados ou perfis inválidos.", nameof(definitions));

            if (definition.DependsOn is null || definition.CompatibleProfiles is null || definition.ConflictsWith?.Contains(definition.Id, StringComparer.Ordinal) == true ||
                definition.DependsOn.Contains(definition.Id, StringComparer.Ordinal) ||
                definition.DependsOn.Concat(definition.ConflictsWith ?? []).Any(id => !ids.Contains(id)))
                throw new ArgumentException($"A regra {definition.Id} contém dependência/conflito próprio ou desconhecido.", nameof(definitions));
        }

        // Kahn's algorithm validates the whole graph, including inactive/profile-incompatible rules.
        var incoming = definitions.ToDictionary(definition => definition.Id,
            definition => definition.DependsOn.Distinct(StringComparer.Ordinal).Count(), StringComparer.Ordinal);
        var dependents = ids.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var definition in definitions)
            foreach (var id in definition.DependsOn.Distinct(StringComparer.Ordinal)) dependents[id].Add(definition.Id);
        var ready = new Queue<string>(incoming.Where(pair => pair.Value == 0).Select(pair => pair.Key));
        var visited = 0;
        while (ready.TryDequeue(out var id))
        {
            visited++;
            foreach (var dependent in dependents[id]) if (--incoming[dependent] == 0) ready.Enqueue(dependent);
        }
        if (visited != definitions.Count)
            throw new ArgumentException("O catálogo contém um ciclo de dependências entre regras.", nameof(definitions));
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
