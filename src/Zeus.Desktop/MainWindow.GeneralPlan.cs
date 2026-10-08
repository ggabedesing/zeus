using System.Text;
using System.Windows;
using Zeus.Core;
using Zeus.Windows;

namespace Zeus.Desktop;

public partial class MainWindow
{
    private bool _generalApplyVisual;
    public bool GeneralApplyVisual
    {
        get => _generalApplyVisual;
        set { if (Set(ref _generalApplyVisual, value)) NotifyActionState(); }
    }

    public bool CanGeneralOptimize => !_isBusy && (GeneralApplyVisual ||
        (_cleanupScan is not null && CleanupFiles.Any(f => f.IsSelected)) ||
        StartupChoices.Any(s => s.IsSelected && s.CanSelect) || MaintenanceChoices.Any(m => m.IsSelected));

    public string GeneralPlanSummary =>
        $"Preferências visuais: {(GeneralApplyVisual ? "incluídas" : "não incluídas")} · " +
        $"{(_cleanupScan is null ? 0 : CleanupFiles.Count(f => f.IsSelected))} temporário(s) para recuperação · " +
        $"{StartupChoices.Count(s => s.IsSelected && s.CanSelect)} entrada(s) de inicialização · " +
        $"{MaintenanceChoices.Count(m => m.IsSelected)} ação(ões) de manutenção. " +
        "Escolha arquivos em Limpeza, programas em Inicialização e ações em Manutenção. " +
        "A exclusão definitiva, os drivers e a verificação offline têm revisão própria.";

    private async void GeneralOptimize_Click(object sender, RoutedEventArgs e)
    {
        if (!CanGeneralOptimize) return;
        var preferences = GeneralApplyVisual ? new UserOptimizationPreferences(SelectedProfile, ReduceAnimations, ReduceTransparency) : null;
        var scan = _cleanupScan;
        var files = scan is null ? [] : CleanupFiles.Where(f => f.IsSelected).Select(f => f.Id).ToArray();
        var startup = StartupChoices.Where(s => s.IsSelected && s.CanSelect).ToArray();
        var requests = MaintenanceChoices.Where(m => m.IsSelected).Select(m => new MaintenanceRequest(m.Id)).ToArray();
        var ordered = requests.Length == 0 ? [] : MaintenancePolicy.ValidateRequests(requests);
        var review = new StringBuilder("Seu plano geral:\n\n");
        if (preferences is not null)
            review.AppendLine($"• Animações: {(preferences.ReduceAnimations ? "reduzir" : "ativar")}; transparência: {(preferences.ReduceTransparency ? "reduzir" : "ativar")}. Estado anterior guardado.");
        if (files.Length > 0)
            review.AppendLine($"• Mover {CleanupSelectedText} para recuperação. Esse movimento não libera espaço em disco.");
        foreach (var entry in startup) review.AppendLine($"• Desativar início automático de {entry.Name}. Estado anterior guardado.");
        foreach (var request in ordered) review.AppendLine($"• {MaintenanceCatalog.Get(request.Action).Title}");
        if (ordered.Count > 0)
            review.AppendLine("\nA manutenção exige autorização de administrador e pode demorar. Mantenha o computador conectado à energia.");
        if (ordered.Any(r => MaintenanceCatalog.Get(r.Action).RequiresRestorePoint))
            review.AppendLine("Os reparos exigem um novo ponto de restauração confirmado antes de iniciar. Se a proteção falhar, o auxiliar bloqueará esses reparos.");
        if (ordered.Any(r => r.Action is MaintenanceActionId.DefenderQuickScan or MaintenanceActionId.DefenderFullScan))
            review.AppendLine("O Defender seguirá as políticas de remediação de ameaças do Windows.");
        review.Append("\nExecute apenas depois de revisar suas seleções. Executar agora?");
        if (!Confirm(review.ToString(), "Revisar otimização geral")) return;

        await RunOperationAsync("Executando plano geral", "Aplicando sequencialmente as escolhas revisadas.", async token =>
        {
            ExecutionLog = string.Empty;
            var succeeded = true;
            if (preferences is not null)
            {
                var result = await _userOptimization.ApplyPreferencesAsync(preferences, token);
                ProfileSummary = result.Message; AppendLog(result.Message); succeeded &= result.Succeeded;
                await RefreshUserChangesAsync();
            }
            foreach (var entry in startup)
            {
                var result = await _userOptimization.DisableStartupAsync(entry.Id, token);
                AppendLog($"{entry.Name}: {result.Message}"); succeeded &= result.Succeeded;
            }
            if (startup.Length > 0) { await RefreshStartupAsync(token); await RefreshUserChangesAsync(); }
            if (files.Length > 0 && scan is not null)
            {
                var result = await _cleanup.QuarantineAsync(scan, files, token);
                CleanupSummary = $"{result.MovedFiles} arquivo(s) guardado(s) · {result.SkippedFiles} ignorado(s). Nenhum espaço foi liberado pelo movimento. " + string.Join(" ", result.Warnings);
                AppendLog(CleanupSummary); succeeded &= result.SkippedFiles == 0 && result.MovedFiles == files.Length;
                _cleanupScan = null; CleanupFiles.Clear(); await RefreshCleanupSessionsAsync();
            }
            if (ordered.Count > 0) succeeded &= await ExecuteMaintenancePlanAsync(ordered, resetLog: false);
            GeneralApplyVisual = false;
            var verificationPending = ordered.Count > 0 && _reports.FirstOrDefault()?.Steps.Any(step => step.Verification == MaintenanceVerificationStatus.Pending) == true;
            StatusTitle = verificationPending ? "Plano geral com verificação pendente" : succeeded ? "Plano geral concluído" : "Plano geral encerrado com avisos";
            StatusDetail = verificationPending ? "Uma etapa terminou, mas aguarda verificação após reinicialização. Confira o histórico antes de repetir a ação."
                : succeeded ? "As ações escolhidas terminaram. Consulte o histórico e os estados anteriores. Nenhum ganho de desempenho foi medido nesta execução."
                : "Algumas ações não foram concluídas. Consulte os resultados e os logs antes de iniciar outro plano.";
        }, mutation: true);
    }
}
