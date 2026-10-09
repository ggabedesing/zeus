namespace Zeus.Core;

public sealed record EventPatternFinding(string Title, string Detail);
public sealed record EventPatternReport(string Summary, IReadOnlyList<EventPatternFinding> Findings);

/// <summary>Finds repeated event signatures in the bounded supplied sample; it does not infer cause.</summary>
public static class EventPatternAnalyzer
{
    public static EventPatternReport Analyze(IReadOnlyList<WindowsEventInfo>? events, bool sourcesComplete = true)
    {
        if (events is null)
            return new("Eventos indisponíveis; não é possível procurar padrões.", []);

        var repeated = events
            .Where(item => !string.IsNullOrWhiteSpace(item.Log) && !string.IsNullOrWhiteSpace(item.Provider))
            .GroupBy(item => (item.Log, item.Provider, item.Id), EventKeyComparer.Instance)
            .Select(group => new
            {
                group.Key.Log,
                group.Key.Provider,
                group.Key.Id,
                Count = group.Count(),
                First = group.Min(item => item.Time),
                Levels = group.Select(item => item.Level).Where(level => !string.IsNullOrWhiteSpace(level))
                    .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(level => level, StringComparer.OrdinalIgnoreCase).ToArray(),
                Latest = group.Max(item => item.Time)
            })
            .Where(group => group.Count >= 2)
            .OrderByDescending(group => group.Count)
            .ThenByDescending(group => group.Latest)
            .ToArray();

        var sourceNote = sourcesComplete
            ? string.Empty
            : " Uma ou mais fontes não puderam ser consultadas; a amostra está incompleta.";
        var summary = events.Count == 0
            ? sourcesComplete
                ? "Nenhum evento crítico, de erro ou aviso foi retornado pelas fontes consultadas; isso não comprova ausência de problemas no Windows."
                : "Nenhum evento foi retornado e uma ou mais fontes estão indisponíveis; estado desconhecido."
            : $"{events.Count} evento(s) crítico(s), de erro ou aviso na amostra atual · {repeated.Length} assinatura(s) repetida(s). Repetição não prova causa nem impacto." + sourceNote;

        var findings = repeated.Select(group => new EventPatternFinding(
            $"{group.Log} · {group.Provider} · ID {group.Id}",
            $"{group.Count} ocorrências na amostra · nível: {(group.Levels.Length == 0 ? "indisponível" : string.Join(", ", group.Levels))} · período: {group.First.ToLocalTime():dd/MM/yyyy HH:mm:ss} a {group.Latest.ToLocalTime():dd/MM/yyyy HH:mm:ss}. Consulte o Visualizador de Eventos para os detalhes e o contexto."))
            .ToArray();
        return new(summary, findings);
    }

    private sealed class EventKeyComparer : IEqualityComparer<(string Log, string Provider, int Id)>
    {
        public static EventKeyComparer Instance { get; } = new();
        public bool Equals((string Log, string Provider, int Id) left, (string Log, string Provider, int Id) right) =>
            left.Id == right.Id && StringComparer.OrdinalIgnoreCase.Equals(left.Log, right.Log) &&
            StringComparer.OrdinalIgnoreCase.Equals(left.Provider, right.Provider);
        public int GetHashCode((string Log, string Provider, int Id) value) => HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(value.Log),
            StringComparer.OrdinalIgnoreCase.GetHashCode(value.Provider), value.Id);
    }
}
