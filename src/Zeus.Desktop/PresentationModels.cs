using System.ComponentModel;
using Zeus.Core;

namespace Zeus.Desktop;

public sealed record HardwareCard(string Title, string Value, string Detail);
public sealed record DeviceRow(string Title, string Detail);
public sealed record RecommendationRow(string Title, string Reason, string ActionText);

public sealed class MaintenanceChoice : INotifyPropertyChanged
{
    private bool _isSelected;

    public MaintenanceChoice(MaintenanceActionDefinition definition)
    {
        Definition = definition;
    }

    public MaintenanceActionDefinition Definition { get; }
    public MaintenanceActionId Id => Definition.Id;
    public string Title => Definition.Title;
    public string Description => Definition.Description;
    public string Requirements => Definition.RequiresRestorePoint
        ? "REPARO · Bloqueado sem proteção de recuperação confirmada · Pode exigir reinicialização"
        : Definition.Id == MaintenanceActionId.DefenderQuickScan
            ? "SEGURANÇA · O Defender segue as políticas de remediação de ameaças do Windows"
            : "VERIFICAÇÃO · Não solicita reparos no Windows";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed record HistoryRow(Guid SessionId, string Title, string Summary, string Protection, IReadOnlyList<string> Steps, string Error, bool HasLogFiles)
{
    public static HistoryRow From(MaintenanceReport report)
    {
        var succeeded = report.Steps.Count(step => step.Outcome == StepOutcome.Succeeded);
        var failed = report.Steps.Count(step => step.Outcome == StepOutcome.Failed);
        var skipped = report.Steps.Count(step => step.Outcome == StepOutcome.Skipped);
        var cancelled = report.Steps.Count(step => step.Outcome == StepOutcome.Cancelled);
        var summary = $"{succeeded} concluída(s) · {failed} falha(s) · {skipped} não executada(s) · {cancelled} cancelada(s)";
        if (report.Steps.Count == 0) summary = "Nenhum resultado de ação recebido";
        return new HistoryRow(
            report.SessionId,
            report.StartedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss"),
            summary,
            report.RestorePointConfirmed ? "Proteção de recuperação confirmada pelo auxiliar." : "Não há confirmação de ponto de restauração para esta sessão.",
            report.Steps.Select(step => $"{ActionTitle(step.Action)} — {OutcomeTitle(step.Outcome)}: {step.Message}").ToArray(),
            report.Error ?? string.Empty,
            report.Steps.Any(step => !string.IsNullOrWhiteSpace(step.LogFile)));
    }

    private static string ActionTitle(MaintenanceActionId action) =>
        MaintenanceCatalog.All.FirstOrDefault(definition => definition.Id == action)?.Title ?? "Ação desconhecida";

    private static string OutcomeTitle(StepOutcome outcome) => outcome switch
    {
        StepOutcome.Succeeded => "Concluída",
        StepOutcome.Failed => "Falhou",
        StepOutcome.Skipped => "Não executada",
        StepOutcome.Cancelled => "Cancelada",
        _ => "Estado desconhecido"
    };
}

internal sealed record DesktopPreferences(bool IsMinimal);

// The export contains structured observations and results, never registry startup
// commands, process environment variables, credential files, or raw command logs.
internal sealed record ExportDocument(
    int SchemaVersion,
    DateTimeOffset ExportedAt,
    HardwareSnapshot? Diagnostics,
    IReadOnlyList<MaintenanceReport> Maintenance);
