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
                MayRequireRestart: false),
            new MaintenanceActionDefinition(
                MaintenanceActionId.UpdateDefenderSignatures,
                "Atualizar definições do Defender",
                "Solicita as definições oficiais do Microsoft Defender quando ele está ativo no modo normal. Respeita outro antivírus instalado.",
                RequiresRestorePoint: false,
                MayRequireRestart: false),
            new MaintenanceActionDefinition(
                MaintenanceActionId.DefenderFullScan,
                "Verificação completa do Defender",
                "Solicita uma verificação completa pelo Defender ativo. Pode levar horas; resultados e ameaças ficam na Segurança do Windows.",
                RequiresRestorePoint: false,
                MayRequireRestart: false),
            new MaintenanceActionDefinition(
                MaintenanceActionId.DefenderOfflineScan,
                "Verificação offline do Defender",
                "Pode reiniciar o computador imediatamente para verificar fora do Windows. Salve seu trabalho e confirme a recuperação do BitLocker antes de autorizar.",
                RequiresRestorePoint: false,
                MayRequireRestart: true),
            new MaintenanceActionDefinition(
                MaintenanceActionId.OptimizeSystemDrive,
                "Otimizar unidade do Windows",
                "Delega ao Windows a otimização apropriada ao tipo de volume, incluindo TRIM quando aplicável. Não força desfragmentação de SSD nem altera serviços.",
                RequiresRestorePoint: false,
                MayRequireRestart: false),
            new MaintenanceActionDefinition(
                MaintenanceActionId.InstallDriverUpdate,
                "Instalar driver selecionado do Windows Update",
                "Instala somente a identidade selecionada após nova consulta ao Windows Update, novo ponto de restauração e exportação dos drivers existentes. Não instala BIOS ou firmware.",
                RequiresRestorePoint: true,
                MayRequireRestart: true),
            new MaintenanceActionDefinition(
                MaintenanceActionId.RollbackDriver,
                "Reverter driver de um dispositivo",
                "Pede ao Windows para voltar ao driver anterior deste dispositivo. Exige que o Windows ainda mantenha uma cópia anterior; o ZEUS exporta o pacote atual antes da reversão e não reinicia automaticamente.",
                RequiresRestorePoint: false,
                MayRequireRestart: true)
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
