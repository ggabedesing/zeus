namespace Zeus.Core;

public sealed record EventTroubleshootingGuide(string Explanation, string NextStep, string SourceUrl);

/// <summary>Read-only investigation guides matched by log, provider and ID, never by ID alone.</summary>
public static class EventTroubleshootingCatalog
{
    public static EventTroubleshootingGuide? Find(string log, string provider, int id)
    {
        if (!string.Equals(log, "System", StringComparison.OrdinalIgnoreCase)) return null;
        if (id == 41 && string.Equals(provider, "Microsoft-Windows-Kernel-Power", StringComparison.OrdinalIgnoreCase))
            return new(
                "O Windows registrou inicialização após encerramento que não foi limpo. O ID isolado não identifica a causa nem comprova defeito da fonte de alimentação.",
                "No Visualizador de Eventos, confira os detalhes do evento (incluindo BugcheckCode e PowerButtonTimestamp), o contexto anterior e o horário do sintoma. Esses campos não foram coletados pelo ZEUS.",
                "https://learn.microsoft.com/en-us/troubleshoot/windows-client/performance/event-id-41-restart");
        if (id == 153 && string.Equals(provider, "disk", StringComparison.OrdinalIgnoreCase))
            return new(
                "O evento corresponde a uma operação de E/S de armazenamento repetida após tempo limite. Ele não identifica sozinho o componente responsável nem comprova falha física do disco.",
                "Confira o disco e os detalhes no evento original, preserve uma cópia dos dados importantes e observe atividade/latência durante o sintoma. O número do disco e o endereço da operação não foram coletados aqui. A referência trata Windows Server; ajustes de controlador exigem avaliação da máquina.",
                "https://learn.microsoft.com/en-us/troubleshoot/windows-server/backup-and-storage/troubleshoot-data-corruption-and-disk-errors");
        if (id == 10016 && string.Equals(provider, "Microsoft-Windows-DistributedCOM", StringComparison.OrdinalIgnoreCase))
            return new(
                "A Microsoft documenta casos de componentes DCOM em que este aviso é esperado e não prejudica o funcionamento. O ZEUS não coletou CLSID/APPID para confirmar se esta ocorrência é um desses casos.",
                "Compare os detalhes com a referência e investigue um sintoma real antes de considerar mudanças. Não altere permissões DCOM ou do Registro apenas para eliminar o aviso.",
                "https://learn.microsoft.com/en-us/troubleshoot/windows-client/application-management/event-10016-logged-when-accessing-dcom");
        return null;
    }
}
