namespace Zeus.Core;

public static class MaintenanceCatalog
{
    public static IReadOnlyList<MaintenanceActionDefinition> All { get; } = Array.AsReadOnly(
        new[]
        {
            new MaintenanceActionDefinition(
                MaintenanceActionId.DefenderQuickScan,
                "Verificação rápida do Defender",
                "Inicia a verificação rápida do Microsoft Defender quando ele está disponível e ativo. Não habilita nem substitui outro antivírus.",
                RequiresRestorePoint: false,
                MayRequireRestart: false),
            new MaintenanceActionDefinition(
                MaintenanceActionId.ScanWindowsImage,
                "Verificar imagem do Windows",
                "Investiga corrupção na imagem do Windows com DISM. Pode levar vários minutos; não representa um teste de desempenho.",
                RequiresRestorePoint: false,
                MayRequireRestart: false),
            new MaintenanceActionDefinition(
                MaintenanceActionId.RepairWindowsImage,
                "Reparar imagem do Windows",
                "Tenta reparar corrupção identificada com DISM. Pode baixar arquivos pelo Windows Update e requer proteção de recuperação confirmada.",
                RequiresRestorePoint: true,
                MayRequireRestart: true),
            new MaintenanceActionDefinition(
                MaintenanceActionId.VerifySystemFiles,
                "Verificar arquivos do sistema",
                "Verifica os arquivos protegidos do Windows com SFC sem solicitar reparos.",
                RequiresRestorePoint: false,
                MayRequireRestart: false),
            new MaintenanceActionDefinition(
                MaintenanceActionId.RepairSystemFiles,
                "Reparar arquivos do sistema",
                "Verifica e tenta reparar arquivos protegidos com SFC. Use quando houver indicação; requer proteção de recuperação confirmada.",
                RequiresRestorePoint: true,
                MayRequireRestart: true),
            new MaintenanceActionDefinition(
                MaintenanceActionId.AnalyzeSystemDrive,
                "Analisar unidade do Windows",
                "Solicita somente a análise de otimização do volume do sistema. Não exclui arquivos nem força desfragmentação ou reparo de setores.",
                RequiresRestorePoint: false,
                MayRequireRestart: false)
        });

    public static MaintenanceActionDefinition Get(MaintenanceActionId id)
    {
        foreach (var definition in All)
        {
            if (definition.Id == id)
            {
                return definition;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(id), id, "A ação não pertence ao catálogo permitido do ZEUS.");
    }
}
