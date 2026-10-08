namespace Zeus.Core;

public sealed record Recommendation(string Title, string Reason, MaintenanceActionId? Action);

/// <summary>
/// Builds review suggestions from observed readings. Thresholds are conservative
/// triage heuristics, not diagnoses or promises of a performance improvement.
/// No repair is selected without an independent diagnosis and explicit review.
/// </summary>
public sealed class OptimizationPlanner
{
    private const ulong GiB = 1024UL * 1024 * 1024;

    public IReadOnlyList<Recommendation> Build(HardwareSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var recommendations = new List<Recommendation>();
        AddMemorySuggestions(snapshot.Memory, recommendations);
        AddStorageSuggestions(snapshot.Disks, recommendations);

        if (snapshot.Startup.Count >= 5)
        {
            recommendations.Add(new Recommendation(
                "Revisar programas de inicialização",
                $"Foram encontradas {snapshot.Startup.Count} entradas. Cinco ou mais é apenas um critério de revisão; a quantidade não mede o impacto. Escolha o que precisa iniciar e preserve programas de segurança e trabalho.",
                null));
        }

        if (snapshot.OperatingSystem.Contains("Windows", StringComparison.OrdinalIgnoreCase))
        {
            AddSecuritySuggestions(snapshot, recommendations);
        }

        foreach (var warning in snapshot.Warnings.Where(warning => !string.IsNullOrWhiteSpace(warning)))
        {
            recommendations.Add(new Recommendation(
                "Leitura limitada no diagnóstico",
                warning,
                null));
        }

        return recommendations.AsReadOnly();
    }

    private static void AddMemorySuggestions(MemoryInfo? memory, List<Recommendation> recommendations)
    {
        if (memory is null || memory.TotalBytes == 0 || memory.AvailableBytes > memory.TotalBytes)
        {
            recommendations.Add(new Recommendation(
                "Medir uso de memória",
                "Não há uma leitura válida de memória disponível. Colete dados durante a tarefa lenta antes de decidir por ajustes ou upgrade.",
                null));
            return;
        }

        var availableRatio = (double)memory.AvailableBytes / memory.TotalBytes;
        if (availableRatio <= 0.10)
        {
            recommendations.Add(new Recommendation(
                "Observar pressão de memória",
                $"Há {ByteFormatting.Format(memory.AvailableBytes)} disponíveis de {ByteFormatting.Format(memory.TotalBytes)} nesta leitura (até 10%). Esse limiar é uma hipótese de pressão de memória: confirme uso e paginação durante a tarefa e revise aplicativos dispensáveis. Não esvazie a RAM automaticamente.",
                null));
        }

        if (memory.TotalBytes < 8 * GiB)
        {
            recommendations.Add(new Recommendation(
                "Avaliar capacidade de RAM para o seu uso",
                $"A máquina possui {ByteFormatting.Format(memory.TotalBytes)} de RAM, abaixo do limiar de revisão de 8 GiB. Isso não comprova um gargalo: observe a tarefa real e consulte compatibilidade antes de considerar mais memória.",
                null));
        }
    }

    private static void AddStorageSuggestions(IReadOnlyList<DiskInfo> disks, List<Recommendation> recommendations)
    {
        foreach (var disk in disks)
        {
            if (disk.TotalBytes == 0 || disk.FreeBytes > disk.TotalBytes)
            {
                continue;
            }

            var freeRatio = (double)disk.FreeBytes / disk.TotalBytes;
            if (freeRatio <= 0.15 || disk.FreeBytes < 10 * GiB)
            {
                recommendations.Add(new Recommendation(
                    $"Revisar espaço em {disk.DriveLetter}",
                    $"Há {ByteFormatting.Format(disk.FreeBytes)} livres de {ByteFormatting.Format(disk.TotalBytes)}. Até 15% livre ou menos de 10 GiB é um critério de revisão, não um diagnóstico. Analise arquivos e categorias antes de apagar; preserve documentos e backups. Espaço recuperado não garante mais desempenho.",
                    null));
            }
        }
    }

    private static void AddSecuritySuggestions(HardwareSnapshot snapshot, List<Recommendation> recommendations)
    {
        var security = snapshot.Security;
        if (security is null || security.DefenderEnabled is null)
        {
            recommendations.Add(new Recommendation(
                "Confirmar proteção instalada",
                "O estado do Microsoft Defender não foi obtido. Verifique Segurança do Windows; outro antivírus pode estar responsável pela proteção.",
                null));
            return;
        }

        if (security.DefenderEnabled == false)
        {
            recommendations.Add(new Recommendation(
                "Revisar o provedor de segurança",
                "O Microsoft Defender aparece desativado. Isso pode ocorrer quando há outro antivírus. Confirme o provedor e as políticas em Segurança do Windows antes de alterar configurações.",
                null));
            return;
        }

        if (security.RealTimeProtectionEnabled == false)
        {
            recommendations.Add(new Recommendation(
                "Verificar proteção em tempo real",
                "O Defender está ativo, mas a proteção em tempo real aparece desativada. Consulte Segurança do Windows e as políticas do computador; uma verificação rápida não reativa a proteção.",
                null));
        }

        if (security.SignatureUpdatedAt is { } signatureDate &&
            snapshot.CollectedAt - signatureDate > TimeSpan.FromDays(7))
        {
            recommendations.Add(new Recommendation(
                "Atualizar definições do Defender",
                "A data das definições é anterior a sete dias nesta coleta. Esse é um limiar de revisão; confirme conectividade e atualize pelo Windows antes de verificar ameaças.",
                null));
        }
    }
}
