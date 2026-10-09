namespace Zeus.Core;

/// <summary>Describes reported scheduler metadata without executing tasks or inferring root cause.</summary>
public static class ScheduledTaskDiagnostics
{
    public static string Describe(ScheduledTaskInfo task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (task.RuntimeInfoAvailable != true)
            return "Informações de execução indisponíveis; o estado da tarefa não comprova sucesso ou falha.";

        var result = task.LastTaskResult is { } code
            ? $"Último resultado: 0x{code:X8} · " + (code switch
            {
                0 => task.LastRunTime.HasValue
                    ? "código zero reportado; confira o efeito esperado da tarefa."
                    : "código zero reportado sem horário de execução; não comprova que a tarefa executou.",
                0x00041300 => "tarefa pronta para executar conforme o agendamento.",
                0x00041301 => "tarefa em execução.",
                0x00041302 => "tarefa desativada.",
                0x00041303 => "tarefa ainda não executou.",
                0x00041304 => "nenhuma execução adicional agendada.",
                0x00041305 => "sem agendamento definido ou propriedades de agendamento incompletas.",
                0x00041306 => "última execução encerrada pelo usuário.",
                0x00041307 => "sem gatilhos válidos ativos.",
                0x00041308 => "gatilho por evento, sem horário fixo de execução.",
                _ => "sem interpretação automática; consulte o histórico da tarefa e a documentação do programa executado."
            })
            : "Último resultado: indisponível.";
        static string Time(DateTimeOffset? time) => time is { } value
            ? value.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") : "não informado";
        var missed = task.MissedRuns is { } count
            ? $"Execuções perdidas reportadas: {count}." : "Execuções perdidas: indisponíveis.";
        return $"{result}\nÚltima execução: {Time(task.LastRunTime)} · Próxima execução: {Time(task.NextRunTime)}\n{missed} Os dados descrevem a consulta atual; não identificam a causa nem autorizam desativar ou executar a tarefa.";
    }
}
