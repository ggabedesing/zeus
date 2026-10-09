namespace Zeus.Core;

public sealed record DriverInstallVerificationDecision(
    StepOutcome Outcome,
    MaintenanceVerificationStatus Verification,
    string Message);

/// <summary>Separates a successful WUA command from confirmation of the exact installed update record.</summary>
public static class DriverInstallVerificationPolicy
{
    public static DriverInstallVerificationDecision Resolve(
        bool installSucceeded,
        bool exactUpdateMarkedInstalled,
        bool rebootRequired)
    {
        if (!installSucceeded)
            return new(StepOutcome.Failed, MaintenanceVerificationStatus.ManualReviewRequired,
                "O Windows Update não confirmou a conclusão da instalação. Confira o log e o estado do dispositivo antes de qualquer nova tentativa.");

        if (exactUpdateMarkedInstalled)
            return new(StepOutcome.Succeeded, MaintenanceVerificationStatus.ProviderConfirmed,
                "Uma nova consulta do Windows Update confirmou como instalado o pacote exato selecionado. Isso confirma o registro do pacote, não que o dispositivo já esteja usando o driver após reinicialização.");

        if (rebootRequired)
            return new(StepOutcome.Succeeded, MaintenanceVerificationStatus.Pending,
                "O Windows Update concluiu a instalação e solicitou reinicialização, mas a consulta ainda não marcou o pacote exato como instalado. A verificação fica pendente até revisar o Windows após reiniciar; não repita a instalação agora.");

        return new(StepOutcome.Failed, MaintenanceVerificationStatus.ManualReviewRequired,
            "O Windows Update concluiu a etapa de instalação, mas a consulta posterior não confirmou o pacote exato como instalado. Revise o log e o estado do dispositivo antes de qualquer nova tentativa.");
    }

    public static DriverInstallVerificationDecision ResolvePostRestart(
        bool queryComplete,
        bool sourceSelectionMatches,
        bool? exactUpdateMarkedInstalled)
    {
        if (!queryComplete || !sourceSelectionMatches || exactUpdateMarkedInstalled is null)
            return new(StepOutcome.Succeeded, MaintenanceVerificationStatus.Pending,
                "A reconsulta não confirmou o estado atual do pacote. A pendência foi preservada; confira Windows Update e o dispositivo manualmente. Nenhuma nova instalação foi iniciada.");

        if (exactUpdateMarkedInstalled.Value)
            return new(StepOutcome.Succeeded, MaintenanceVerificationStatus.ProviderConfirmed,
                "A reconsulta do Windows Update confirmou o pacote exato como instalado na seleção lógica registrada. Isso não confirma que o dispositivo esteja usando o driver ativo.");

        return new(StepOutcome.Succeeded, MaintenanceVerificationStatus.Pending,
            "A consulta completa não encontrou o pacote exato como instalado. A pendência foi preservada para revisão; nenhuma nova instalação foi iniciada.");
    }
}
