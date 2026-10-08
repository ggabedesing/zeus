using System.ComponentModel;
using Zeus.Core;
using Zeus.Windows;

namespace Zeus.Desktop;

public sealed record HardwareCard(string Title, string Value, string Detail);
public sealed record DeviceRow(string Title, string Detail);
public sealed record RecommendationRow(string Title, string Reason, string ActionText);
public sealed record ProfileOption(UsageProfile Value, string Name)
{
    public override string ToString() => Name;
}
public sealed record ThemeOption(DesktopTheme Value, string Name)
{
    public override string ToString() => Name;
}

public abstract class SelectableRow : INotifyPropertyChanged
{
    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new(nameof(IsSelected)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise(string name) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class MaintenanceChoice(MaintenanceActionDefinition definition) : SelectableRow
{
    public MaintenanceActionDefinition Definition { get; } = definition;
    public MaintenanceActionId Id => Definition.Id;
    public string Title => Definition.Title;
    public string Description => Definition.Description;
    public string Requirements => Definition.RequiresRestorePoint
        ? "Proteção de recuperação obrigatória · Pode exigir reinicialização"
        : Id is MaintenanceActionId.DefenderQuickScan or MaintenanceActionId.DefenderFullScan or MaintenanceActionId.UpdateDefenderSignatures
            ? "Depende do Defender ativo · Respeita a política de segurança do Windows"
            : Id == MaintenanceActionId.OptimizeSystemDrive
                ? "O Windows escolhe a operação adequada para a unidade"
                : "Verificação local · Nenhum ganho de desempenho é presumido";
}

public sealed class CleanupFileChoice(string id, string relativePath, ulong sizeBytes, DateTimeOffset lastWriteTime) : SelectableRow
{
    public string Id { get; } = id;
    public string RelativePath { get; } = relativePath;
    public ulong SizeBytes { get; } = sizeBytes;
    public string Detail => $"{ByteFormatting.Format(SizeBytes)} · Modificado em {lastWriteTime.ToLocalTime():dd/MM/yyyy HH:mm}";
}

public sealed class StartupChoice(StartupEntry entry) : SelectableRow
{
    public StartupEntry Entry { get; } = entry;
    public string Id => Entry.Id;
    public string Name => Entry.Name;
    public bool CanSelect => Entry.IsEnabled && !Entry.IsProtected;
    public string Detail => Entry.IsProtected ? $"Protegido: {Entry.ProtectionReason}" : Entry.IsEnabled ? "Ativo · A alteração poderá ser desfeita" : "Desativado";
}

public sealed class DriverChoice(DriverUpdateCandidate candidate) : SelectableRow
{
    private bool _eulaAccepted;
    public DriverUpdateCandidate Candidate { get; } = candidate;
    public string Id => Candidate.Id;
    public string Title => Candidate.Title;
    public string? DeviceName => Candidate.DeviceName;
    public string? Manufacturer => Candidate.Manufacturer;
    public string? DriverVersion => Candidate.DriverVersion;
    public string DriverProvider => string.IsNullOrWhiteSpace(Candidate.DriverProvider) ? "indisponível" : Candidate.DriverProvider;
    public string DriverClass => string.IsNullOrWhiteSpace(Candidate.DriverClass) ? "indisponível" : Candidate.DriverClass;
    public string DriverDate => Candidate.DriverDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "indisponível";
    public string ProviderCategory => ClassifyProvider(Candidate.DriverProvider, Candidate.Manufacturer);
    public string DriverSource => "Windows Update · origem configurada no sistema";
    public bool RequiresEula => Candidate.RequiresEula;
    public string EulaText => Candidate.EulaText ?? "A licença não está disponível. Instale este candidato pelo Windows Update para revisar os termos.";
    public bool LicenseReady => !RequiresEula || (EulaAccepted && !string.IsNullOrWhiteSpace(Candidate.EulaText));
    public bool EulaAccepted { get => _eulaAccepted; set { if (_eulaAccepted == value) return; _eulaAccepted = value; Raise(nameof(EulaAccepted)); Raise(nameof(LicenseReady)); } }

    private static string ClassifyProvider(string? provider, string? manufacturer)
    {
        var declaredName = string.IsNullOrWhiteSpace(provider) ? manufacturer : provider;
        if (string.IsNullOrWhiteSpace(declaredName)) return "indisponível";
        if (declaredName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)) return "NVIDIA (heurística pelo nome declarado)";
        if (declaredName.Contains("AMD", StringComparison.OrdinalIgnoreCase) || ContainsToken(declaredName, "ATI") ||
            declaredName.Contains("Advanced Micro Devices", StringComparison.OrdinalIgnoreCase)) return "AMD (heurística pelo nome declarado)";
        if (declaredName.Contains("Intel", StringComparison.OrdinalIgnoreCase)) return "Intel (heurística pelo nome declarado)";
        if (new[] { "Dell", "HP Inc", "Hewlett-Packard", "Hewlett Packard", "Lenovo", "ASUS", "Acer", "MSI", "Samsung", "Gigabyte", "Toshiba" }
            .Any(name => declaredName.Contains(name, StringComparison.OrdinalIgnoreCase)))
            return "OEM (heurística pelo nome declarado)";
        if (ContainsToken(declaredName, "HP")) return "OEM (heurística pelo nome declarado)";
        return "Outro fornecedor (nome declarado)";
    }

    private static bool ContainsToken(string value, string token)
    {
        var index = value.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            var beforeIsWord = index > 0 && char.IsLetterOrDigit(value[index - 1]);
            var afterIndex = index + token.Length;
            var afterIsWord = afterIndex < value.Length && char.IsLetterOrDigit(value[afterIndex]);
            if (!beforeIsWord && !afterIsWord) return true;
            index = value.IndexOf(token, index + token.Length, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }
}

public sealed class DriverRollbackChoice(PnpDeviceInfo device)
{
    public PnpDeviceInfo Device { get; } = device;
    public string InstanceId => Device.InstanceId ?? string.Empty;
    public string Label => $"{Device.Name} · {Device.Class}";
}

public sealed record WingetUpdateRow(WingetUpdateCandidate Candidate, bool PendingReview = false)
{
    public string Name => Candidate.Name;
    public string PackageId => Candidate.PackageId;
    public string InstalledVersion => Candidate.InstalledVersion;
    public string AvailableVersion => Candidate.AvailableVersion;
    public string Source => Candidate.Source;
    public bool CanInstall => Candidate.CanInstall && !PendingReview;
    public string InstallabilityReason => PendingReview ? "Tentativa anterior sem resultado confirmado. Confira manualmente o programa e consulte novamente antes de qualquer nova ação." : Candidate.InstallabilityReason;
}

public sealed record WindowsUpdateRow(string Title, string KnowledgeBase, string DownloadState, string UpdateId);

public sealed record ChangeRow(Guid Id, string Title, string Detail, bool CanRestore);
public sealed record CleanupSessionRow(Guid Id, string Title, string Detail, bool CanRestore, bool CanPurge);
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
        if (!report.IsComplete) summary = "Conclusão não confirmada · " + summary;
        return new(report.SessionId, report.StartedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss"), summary,
            report.RestorePointConfirmed ? "Proteção de recuperação confirmada pelo auxiliar." : "Esta sessão não confirmou um ponto de restauração.",
            report.Steps.Select(step => $"{MaintenanceCatalog.Get(step.Action).Title}{(step.TargetId is null ? "" : $" [{step.TargetId}]")} — {OutcomeTitle(step.Outcome)} · {VerificationTitle(step.Verification)}: {step.Message}").ToArray(),
            report.Error ?? string.Empty, report.Steps.Any(step => !string.IsNullOrWhiteSpace(step.LogFile)));
    }
    private static string OutcomeTitle(StepOutcome outcome) => outcome switch
    {
        StepOutcome.Succeeded => "Concluída", StepOutcome.Failed => "Falhou", StepOutcome.Skipped => "Não executada", StepOutcome.Cancelled => "Cancelada", _ => "Estado desconhecido"
    };
    private static string VerificationTitle(MaintenanceVerificationStatus status) => status switch
    {
        MaintenanceVerificationStatus.NotStarted => "não iniciada",
        MaintenanceVerificationStatus.Pending => "em andamento, sem verificação",
        MaintenanceVerificationStatus.CommandCompleted => "comando concluído; confira a saída",
        MaintenanceVerificationStatus.ProviderConfirmed => "resultado confirmado pelo provedor",
        MaintenanceVerificationStatus.ManualReviewRequired => "revisão manual necessária",
        _ => "verificação não registrada"
    };
}

internal sealed record DesktopPreferences(bool IsMinimal, DesktopTheme Theme = DesktopTheme.Complete,
    UsageProfile Profile = UsageProfile.Balanced, bool ReduceAnimations = false, bool ReduceTransparency = false,
    bool NeedsBluetooth = true, bool NeedsPrinting = true, bool NeedsCloudSync = true, bool NeedsVirtualization = false,
    bool FirstRunSetupComplete = true);

// Export deliberately excludes startup command strings, raw logs and process environment.
internal sealed record ExportDocument(int SchemaVersion, DateTimeOffset ExportedAt, HardwareSnapshot? Diagnostics,
    IReadOnlyList<MaintenanceReport> Maintenance, PerformanceObservation? Performance = null,
    IReadOnlyList<RecommendationRow>? Plan = null, UserOptimizationPreferences? Preferences = null,
    IReadOnlyList<ChangeRow>? Changes = null, IReadOnlyList<CleanupSessionRow>? Cleanup = null,
    IReadOnlyList<PerformanceHistoryEntry>? PerformanceHistory = null,
    IReadOnlyList<PerformanceObservation>? PerformanceBaseline = null,
    PerformanceComparison? PerformanceComparison = null,
    OptimizationPlan? FormalOptimizationPlan = null);
