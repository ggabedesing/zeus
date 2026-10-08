namespace Zeus.Core;

public sealed record ServiceDependencyFinding(string Name, string Detail);
public sealed record ServiceDependencyReport(string Summary, IReadOnlyList<ServiceDependencyFinding> Findings);

/// <summary>Describes declared service relationships from one inventory snapshot; it never changes service state.</summary>
public static class ServiceDependencyAnalyzer
{
    public static ServiceDependencyReport Analyze(IReadOnlyList<ServiceInfo>? services, bool sourceComplete = true)
    {
        if (services is null)
            return new("Inventário de serviços indisponível; não é possível avaliar dependências.", []);
        if (services.Count == 0)
            return new(sourceComplete ? "Nenhum serviço foi retornado nesta coleta." : "A leitura de serviços está incompleta; dependências desconhecidas.", []);

        var byName = services.Where(s => !string.IsNullOrWhiteSpace(s.Name))
            .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var findings = new List<ServiceDependencyFinding>();
        var unknown = 0;
        foreach (var service in byName.Values.OrderBy(s => s.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            if (service.DependenciesAvailable != true || service.Dependencies is null)
            {
                unknown++;
                findings.Add(new(service.DisplayName, "Dependências não informadas pelo inventário."));
                continue;
            }

            var dependencies = service.Dependencies.Where(d => !string.IsNullOrWhiteSpace(d)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (dependencies.Length == 0)
            {
                findings.Add(new(service.DisplayName, "Nenhuma dependência declarada."));
                continue;
            }

            var details = dependencies.Select(dependency =>
            {
                if (dependency.StartsWith('+')) return $"grupo {dependency} (membros não avaliados)";
                if (!byName.TryGetValue(dependency, out var target)) return $"{dependency}: referência não resolvida neste inventário";
                return $"{target.DisplayName}: estado informado {target.Status}";
            });
            findings.Add(new(service.DisplayName, "Depende de " + string.Join("; ", details)));
        }

        var suffix = unknown > 0 || !sourceComplete ? " Há campos/fontes incompletos; resultados desconhecidos não indicam falha." : " Leitura declarativa; estado parado isolado não é classificado como defeito.";
        return new($"{byName.Count} serviço(s) no inventário · {unknown} com dependências desconhecidas.{suffix}", findings);
    }
}
