using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Zeus.Cleanup;
using Zeus.Core;
using Zeus.Windows;
using Zeus.Storage;

namespace Zeus.Desktop;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly IHardwareDiagnostics _diagnostics = new WindowsHardwareDiagnostics();
    private readonly ElevatedMaintenanceExecutor _executor = new();
    private readonly WindowsPerformanceProbe _performanceProbe = new();
    private readonly NetworkLatencyProbe _networkLatencyProbe = new();
    private readonly PerformanceHistoryBuffer _performanceHistory = new();
    private Guid _performanceSessionId = Guid.NewGuid();
    private readonly WindowsUpdateService _windowsUpdate = new();
    private readonly WingetUpdateService _wingetUpdates = new();
    private readonly PendingMaintenanceSessions _pendingSessions = new();
    private readonly UserOptimizationService _userOptimization;
    private readonly TemporaryFileCleanup _cleanup;
    private readonly bool _isFixture;
    private readonly OptimizationRuleEngine _ruleEngine = new();
    private readonly DesktopStorage _storage;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _preferenceLock = new(1, 1);
    private readonly List<MaintenanceReport> _reports = [];
    private readonly List<string> _startupWarnings = [];
    private readonly List<Task> _activityWrites = [];
    private readonly HashSet<string> _pendingWingetPackages = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _pendingWingetCorrelations = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Guid> _performanceSessionStartAttempts = [];
    private readonly HashSet<Guid> _persistedPerformanceSessions = [];
    private readonly Dictionary<Guid, int> _performanceSessionSequences = [];
    private CancellationTokenSource? _readCancellation;
    private HardwareSnapshot? _snapshot;
    private OptimizationPlan? _optimizationPlan;
    private PerformanceObservation? _performance;
    private PerformanceObservation[] _performanceBaseline = [];
    private PerformanceComparison? _performanceComparison;
    private CleanupScan? _cleanupScan;
    private bool _isBusy, _isExecuting, _loaded, _historyReadable = true;
    private DesktopTheme _selectedTheme = DesktopTheme.Complete;
    private UsageProfile _selectedProfile = UsageProfile.Balanced;
    private bool _reduceAnimations, _reduceTransparency, _needsBluetooth = true, _needsPrinting = true, _needsCloudSync = true, _needsVirtualization;
    private bool _offlineRestartConfirmed, _offlineRecoveryConfirmed;
    private bool _networkResetReviewed, _networkResetRecoveryReady;
    private bool _wingetAuditReadable = true;
    private bool _closingAfterActivityDrain;
    private int _activityStorageWarningShown;
    private DatabaseHealth? _storageHealth;
    private PowerPlanInfo? _selectedPowerPlan;
    private string? _selectedWallpaperPath;
    private ImageSource? _wallpaperPreview;
    private string _statusTitle = "Preparando diagnóstico", _statusDetail = "As informações serão lidas diretamente neste computador.";
    private string _executionLog = "Nenhuma manutenção executada nesta sessão.", _maintenanceResultSummary = string.Empty;
    private string _cleanupSummary = "Analise temporários com mais de sete dias. Nenhum arquivo será selecionado automaticamente.";
    private string _startupSummary = "Leia os programas do seu usuário para escolher o que precisa iniciar com o Windows.";
    private string _profileSummary = "O perfil orienta o plano. Ajustes do Windows são separados e reversíveis.";
    private string _driverSummary = "Consulte os drivers oferecidos oficialmente pelo Windows Update para este computador.";
    private string _wingetSummary = "Consulte atualizações de programas identificadas pela fonte winget. A consulta não instala nada.";
    private string _performanceSummary = "Meça por cinco segundos durante a tarefa lenta para observar a carga real.";
    private string _networkProbeTarget = string.Empty;
    private string _networkProbeSummary = "A medição só começa quando você informa um IP ou host e solicita o teste.";

    public MainWindow() : this(null) { }

    public MainWindow(string? storageRoot)
    {
        _isFixture = storageRoot is not null;
        _storage = new(storageRoot);
        _userOptimization = new(storageRoot is null ? null : Path.Combine(storageRoot, "Changes"));
        _cleanup = new(storageRoot is null ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp") : Path.Combine(storageRoot, "Temporary"),
            Path.Combine(storageRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zeus"), "Cleanup"));
        InitializeComponent();
        MaxWidth = SystemParameters.WorkArea.Width; MaxHeight = SystemParameters.WorkArea.Height;
        foreach (var definition in MaintenanceCatalog.All.Where(d => d.Id is not MaintenanceActionId.InstallDriverUpdate and not MaintenanceActionId.DefenderOfflineScan))
        {
            var choice = new MaintenanceChoice(definition);
            choice.PropertyChanged += (_, _) => NotifyActionState();
            MaintenanceChoices.Add(choice);
        }
        ShowPendingHardware();
        DataContext = this;
    }

    public HardwareSnapshot? Snapshot => _snapshot;
    public OptimizationPlan? FormalOptimizationPlan => _optimizationPlan;
    public DatabaseHealth? StorageHealth => _storageHealth;
    public PerformanceObservation? Performance => _performance;
    public IReadOnlyList<PerformanceHistoryEntry> PerformanceHistory => _performanceHistory.Snapshot();
    public bool CanSetPerformanceBaseline => !_isBusy && _performanceHistory.Snapshot().Count >= 3;
    public bool CanComparePerformance => !_isBusy && _performanceBaseline.Length >= 3 &&
        _performanceHistory.Snapshot().Count(entry => entry.Observation.CollectedAt > _performanceBaseline[^1].CollectedAt) >= 3;
    public string PerformanceComparisonSummary
    {
        get
        {
            if (_performanceComparison is not { } comparison)
                return "Defina uma referência com pelo menos três amostras e colete outras três para comparar.";
            var gpuMemory = comparison.GpuMemoryUsage is { Count: > 0 } memory
                ? "Memória dedicada GPU por adaptador: " + string.Join("; ", memory.Select(item =>
                    $"{item.AdapterInstance} {FormatByteQuantity(item.ReferenceDedicatedBytes)} / {FormatMetric(item.ReferenceOccupancyPercent)} ({item.ReferenceAvailableSamples}) → {FormatByteQuantity(item.LaterDedicatedBytes)} / {FormatMetric(item.LaterOccupancyPercent)} ({item.LaterAvailableSamples})"))
                : "Memória GPU: indisponível";
            var network = comparison.NetworkTraffic is { Count: > 0 } adapters
                ? "Rede por adaptador: " + string.Join("; ", adapters.Select(item =>
                    $"{item.Adapter} {FormatRate(item.ReferenceBytesPerSecond)} ({item.ReferenceAvailableSamples}/{comparison.ReferenceSampleCount}) → {FormatRate(item.LaterBytesPerSecond)} ({item.LaterAvailableSamples}/{comparison.LaterSampleCount})"))
                : "Tráfego de rede: indisponível";
            var diskIo = comparison.DiskIo is { Count: > 0 } disks
                ? "Disco por unidade: " + string.Join("; ", disks.Select(item =>
                    $"{item.InstanceName} leitura {FormatRate(item.ReferenceBytesPerSecond)} ({item.ReferenceThroughputSamples}/{comparison.ReferenceSampleCount}) → {FormatRate(item.LaterBytesPerSecond)} ({item.LaterThroughputSamples}/{comparison.LaterSampleCount}); latência {FormatLatency(item.ReferenceReadLatencyMilliseconds)} ({item.ReferenceLatencySamples}/{comparison.ReferenceSampleCount}) → {FormatLatency(item.LaterReadLatencyMilliseconds)} ({item.LaterLatencySamples}/{comparison.LaterSampleCount})"))
                : "Disco: transferência/latência indisponível";
            var context = comparison.ActivityContext is { } activity
                ? $"Contexto observado: jogo {activity.ReferenceGameDetectedSamples}/{activity.ReferenceAvailableSamples} → {activity.LaterGameDetectedSamples}/{activity.LaterAvailableSamples}; OBS {activity.ReferenceObsDetectedSamples}/{activity.ReferenceAvailableSamples} → {activity.LaterObsDetectedSamples}/{activity.LaterAvailableSamples}; encoder do OBS {activity.ReferenceObsEncoderActiveSamples}/{activity.ReferenceObsEncoderKnownSamples} → {activity.LaterObsEncoderActiveSamples}/{activity.LaterObsEncoderKnownSamples}"
                : "Contexto de jogo/OBS: indisponível";
            var processes = comparison.ProcessUsage is { Count: > 0 } processUsage
                ? "Até cinco processos acompanhados por PID/início: " + string.Join("; ", processUsage
                    .OrderByDescending(item => Math.Max(item.LaterCpuPercent ?? -1, item.ReferenceCpuPercent ?? -1))
                    .Take(5).Select(item =>
                        $"{item.Name} · PID {item.ProcessId}: CPU {FormatMetric(item.ReferenceCpuPercent)} ({item.ReferenceCpuSamples}/{comparison.ReferenceSampleCount}) → {FormatMetric(item.LaterCpuPercent)} ({item.LaterCpuSamples}/{comparison.LaterSampleCount}); memória {FormatByteQuantity(item.ReferenceWorkingSetBytes)} ({item.ReferenceWorkingSetSamples}/{comparison.ReferenceSampleCount}) → {FormatByteQuantity(item.LaterWorkingSetBytes)} ({item.LaterWorkingSetSamples}/{comparison.LaterSampleCount})"))
                : "Comparação por processo: indisponível (identidade do processo não confirmada nos períodos)";
            return string.Join(Environment.NewLine,
                $"CPU média: {FormatMetricCoverage(comparison.CpuUsage, comparison.ReferenceSampleCount, comparison.LaterSampleCount)} · RAM em uso: {FormatMetricCoverage(comparison.MemoryUsage, comparison.ReferenceSampleCount, comparison.LaterSampleCount)} · pico médio da engine GPU mais ativa: {FormatMetricCoverage(comparison.GpuEnginePeak, comparison.ReferenceSampleCount, comparison.LaterSampleCount)} · pico médio de atividade de disco: {FormatMetricCoverage(comparison.DiskActivityPeak, comparison.ReferenceSampleCount, comparison.LaterSampleCount)}",
                diskIo,
                gpuMemory,
                network,
                context,
                processes,
                "Interpretação: cobertura mostra amostras válidas sobre o total; engines individuais não são uso total da GPU, ocupação não comprova gargalo e o contexto heurístico não confirma partida ou transmissão. Comparação descritiva, sem atribuir causa ou ganho.");
        }
    }

    private static string FormatRate(double? bytesPerSecond) => bytesPerSecond is { } value && double.IsFinite(value) && value >= 0
        ? FormatBytesPerSecond(value >= ulong.MaxValue ? ulong.MaxValue : (ulong)Math.Round(value))
        : "indisponível";

    private static string FormatLatency(double? milliseconds) => milliseconds is { } value && double.IsFinite(value) && value >= 0
        ? $"{value:0.##} ms"
        : "indisponível";
    public ObservableCollection<HardwareCard> HardwareCards { get; } = [];
    public ObservableCollection<RecommendationRow> Recommendations { get; } = [];
    public ObservableCollection<DeviceRow> GraphicsRows { get; } = [];
    public ObservableCollection<DeviceRow> DiskRows { get; } = [];
    public ObservableCollection<DeviceRow> ExtendedHardwareRows { get; } = [];
    public ObservableCollection<DeviceRow> ProcessRows { get; } = [];
    public ObservableCollection<DeviceRow> PerformanceResourceRows { get; } = [];
    public ObservableCollection<DeviceRow> StartupRows { get; } = [];
    public ObservableCollection<DeviceRow> ServiceDependencyRows { get; } = [];
    public ObservableCollection<DeviceRow> DeviceRepairRows { get; } = [];
    public ObservableCollection<DeviceRow> EventDiagnosticRows { get; } = [];
    public ObservableCollection<string> Warnings { get; } = [];
    public ObservableCollection<MaintenanceChoice> MaintenanceChoices { get; } = [];
    public ObservableCollection<HistoryRow> HistoryRows { get; } = [];
    public ObservableCollection<CleanupFileChoice> CleanupFiles { get; } = [];
    public ObservableCollection<CleanupSessionRow> CleanupSessions { get; } = [];
    public ObservableCollection<StartupChoice> StartupChoices { get; } = [];
    public ObservableCollection<ChangeRow> UserChanges { get; } = [];
    public ObservableCollection<PowerPlanInfo> PowerPlans { get; } = [];
    public ObservableCollection<DriverChoice> DriverCandidates { get; } = [];
    public ObservableCollection<WingetUpdateRow> WingetUpdates { get; } = [];
    public ObservableCollection<WindowsUpdateRow> PendingWindowsUpdates { get; } = [];
    public IReadOnlyList<ProfileOption> ProfileOptions { get; } = [new(UsageProfile.Balanced, "Geral"), new(UsageProfile.Gaming, "Jogos"), new(UsageProfile.GamingStreaming, "Jogos e transmissão"), new(UsageProfile.Work, "Trabalho e estudo"), new(UsageProfile.Creative, "Edição e criação"), new(UsageProfile.Development, "Programação"), new(UsageProfile.Battery, "Autonomia no notebook")];
    public IReadOnlyList<ThemeOption> ThemeOptions { get; } = [new(DesktopTheme.Complete, "Completo · ZEUS"), new(DesktopTheme.Minimal, "Mínimo · Foco"), new(DesktopTheme.MacInspired, "Aurora · inspirado no macOS")];
    public event PropertyChangedEventHandler? PropertyChanged;

    public bool CanRefresh => !_isBusy;
    public bool CanChooseActions => !_isBusy;
    public bool CanAnalyzeServiceDependencies => !_isBusy && _snapshot?.WindowsInventory is not null;
    public bool CanOpenNetworkResetSettings => !_isBusy && _networkResetReviewed && _networkResetRecoveryReady;
    public bool CanSaveNetworkResetReference => !_isBusy && _snapshot?.WindowsInventory is not null;
    public bool CanExecute => !_isBusy && MaintenanceSelectionError() is null && MaintenanceChoices.Any(c => c.IsSelected);
    public bool CanExport => !_isBusy && (_snapshot is not null || _reports.Count > 0);
    public bool CanCancel => _isBusy && _readCancellation is not null;
    public bool CanQuarantine => !_isBusy && _cleanupScan is not null && CleanupFiles.Any(f => f.IsSelected);
    public bool CanDisableStartup => !_isBusy && StartupChoices.Any(f => f.IsSelected && f.CanSelect);
    public bool CanSetPowerPlan => !_isBusy && SelectedPowerPlan is { IsActive: false };
    public bool CanApplyWallpaper => !_isBusy && !string.IsNullOrWhiteSpace(SelectedWallpaperPath);
    public bool CanInstallDriver => !_isBusy && DriverCandidates.Any(d => d.IsSelected) && DriverCandidates.Where(d => d.IsSelected).All(d => d.LicenseReady);
    public bool CanOfflineScan => !_isBusy && OfflineRestartConfirmed && OfflineRecoveryConfirmed;
    public string StatusTitle { get => _statusTitle; private set => Set(ref _statusTitle, value); }
    public string StatusDetail { get => _statusDetail; private set => Set(ref _statusDetail, value); }
    public string ExecutionLog { get => _executionLog; private set => Set(ref _executionLog, value); }
    public string ServiceDependencySummary { get; private set; } = "Leia o inventário do Windows para consultar as dependências declaradas dos serviços.";
    public string DeviceRepairSummary { get; private set; } = "Leia o inventário do Windows para consultar os códigos de problema PnP.";
    public string EventDiagnosticSummary { get; private set; } = "Leia os logs locais para procurar assinaturas repetidas de eventos.";
    public string NetworkResetPreparationSummary { get; private set; } = "Leia o inventário de rede antes de considerar uma redefinição.";
    public bool NetworkResetReviewed { get => _networkResetReviewed; set { if (Set(ref _networkResetReviewed, value)) NotifyActionState(); } }
    public bool NetworkResetRecoveryReady { get => _networkResetRecoveryReady; set { if (Set(ref _networkResetRecoveryReady, value)) NotifyActionState(); } }
    public string MaintenanceResultSummary { get => _maintenanceResultSummary; private set => Set(ref _maintenanceResultSummary, value); }
    public string CleanupSummary { get => _cleanupSummary; private set => Set(ref _cleanupSummary, value); }
    public string StartupSummary { get => _startupSummary; private set => Set(ref _startupSummary, value); }
    public string ProfileSummary { get => _profileSummary; private set => Set(ref _profileSummary, value); }
    public string DriverSummary { get => _driverSummary; private set => Set(ref _driverSummary, value); }
    public string WingetSummary { get => _wingetSummary; private set => Set(ref _wingetSummary, value); }
    private string _windowsUpdateSummary = "A busca online só começa quando você solicitar. Não baixa nem instala atualizações.";
    public string WindowsUpdateSummary { get => _windowsUpdateSummary; private set => Set(ref _windowsUpdateSummary, value); }
    public string PerformanceSummary { get => _performanceSummary; private set => Set(ref _performanceSummary, value); }
    public string NetworkProbeTarget { get => _networkProbeTarget; set => Set(ref _networkProbeTarget, value); }
    public string NetworkProbeSummary { get => _networkProbeSummary; private set => Set(ref _networkProbeSummary, value); }
    public string CollectionDate => _snapshot is null ? "Leitura pendente" : _snapshot.CollectedAt.ToLocalTime().ToString("dd/MM HH:mm:ss");
    public string SystemDescription => _snapshot is null ? "Inventário local do Windows" : $"{_snapshot.ComputerName} · {_snapshot.OperatingSystem}";
    public string RecommendationEmptyText => _snapshot is null ? "As recomendações aparecem depois do diagnóstico." : Recommendations.Count == 0 ? "Nenhum alerta pelos critérios desta leitura. Meça a tarefa lenta para investigar." : string.Empty;
    public string FormalPlanSummary => _optimizationPlan?.Status switch
    {
        OptimizationPlanStatus.NeedsMoreData => "Plano preliminar: faltam leituras para avaliar todos os critérios. Os dados indisponíveis aparecem nas recomendações.",
        OptimizationPlanStatus.PrerequisitesNotMet => "Uma sugestão depende de um pré-requisito que não foi atendido. Consulte os detalhes no plano exportado.",
        OptimizationPlanStatus.NoOptimizationRequired => "Nenhuma otimização necessária pelos critérios avaliados nesta coleta.",
        OptimizationPlanStatus.RecommendationsAvailable => "Há pontos para revisar com base nesta coleta. As sugestões não comprovam um gargalo nem aplicam alterações.",
        _ => "O plano formal aparece depois do diagnóstico."
    };
    public string DevicesEmptyText => _snapshot is null ? "Inventário ainda não carregado." : "Leituras fornecidas pelo Windows. Sensores ausentes permanecem indisponíveis.";
    public string StartupEmptyText => StartupChoices.Count == 0 ? "Nenhuma entrada editável foi carregada. Atualize a lista e consulte o resultado." : string.Empty;
    public string HistoryEmptyText => !_historyReadable ? "O histórico anterior foi preservado porque não pôde ser lido. Exporte os novos resultados." : HistoryRows.Count == 0 ? "Ainda não há sessões de manutenção neste usuário." : string.Empty;
    public string SelectedActionsText
    {
        get
        {
            var count = MaintenanceChoices.Count(c => c.IsSelected);
            var error = MaintenanceSelectionError();
            return error is null ? $"{count} ação(ões) selecionada(s)" : $"Plano inválido: {error}";
        }
    }
    public string CleanupSelectedText => $"{CleanupFiles.Count(f => f.IsSelected)} arquivo(s) · {ByteFormatting.Format(CleanupFiles.Where(f => f.IsSelected).Aggregate(0UL, (sum, f) => sum + f.SizeBytes))} selecionados";
    public Visibility DetailedVisibility => IsMinimal ? Visibility.Collapsed : Visibility.Visible;
    public string LayoutDescription => ThemeOptions.First(t => t.Value == SelectedTheme).Name;
    public bool IsMinimal { get => SelectedTheme == DesktopTheme.Minimal; set => SelectedTheme = value ? DesktopTheme.Minimal : DesktopTheme.Complete; }
    public DesktopTheme SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (!Enum.IsDefined(value) || !Set(ref _selectedTheme, value)) return;
            ApplyTheme(); Notify(nameof(IsMinimal)); Notify(nameof(DetailedVisibility)); Notify(nameof(LayoutDescription)); QueuePreferencesSave();
        }
    }
    public UsageProfile SelectedProfile { get => _selectedProfile; set { if (Enum.IsDefined(value) && Set(ref _selectedProfile, value)) ProfileChanged(); } }
    public bool ReduceAnimations { get => _reduceAnimations; set { if (Set(ref _reduceAnimations, value)) ProfileChanged(); } }
    public bool ReduceTransparency { get => _reduceTransparency; set { if (Set(ref _reduceTransparency, value)) ProfileChanged(); } }
    public bool NeedsBluetooth { get => _needsBluetooth; set { if (Set(ref _needsBluetooth, value)) ProfileChanged(); } }
    public bool NeedsPrinting { get => _needsPrinting; set { if (Set(ref _needsPrinting, value)) ProfileChanged(); } }
    public bool NeedsCloudSync { get => _needsCloudSync; set { if (Set(ref _needsCloudSync, value)) ProfileChanged(); } }
    public bool NeedsVirtualization { get => _needsVirtualization; set { if (Set(ref _needsVirtualization, value)) ProfileChanged(); } }
    public bool OfflineRestartConfirmed { get => _offlineRestartConfirmed; set { if (Set(ref _offlineRestartConfirmed, value)) Notify(nameof(CanOfflineScan)); } }
    public bool OfflineRecoveryConfirmed { get => _offlineRecoveryConfirmed; set { if (Set(ref _offlineRecoveryConfirmed, value)) Notify(nameof(CanOfflineScan)); } }
    public PowerPlanInfo? SelectedPowerPlan { get => _selectedPowerPlan; set { if (Set(ref _selectedPowerPlan, value)) Notify(nameof(CanSetPowerPlan)); } }
    public string? SelectedWallpaperPath { get => _selectedWallpaperPath; private set { if (Set(ref _selectedWallpaperPath, value)) Notify(nameof(CanApplyWallpaper)); } }
    public ImageSource? WallpaperPreview { get => _wallpaperPreview; private set => Set(ref _wallpaperPreview, value); }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        SetBusy(true);
        try
        {
            try
            {
                var p = await _storage.ReadPreferencesAsync();
                SelectedTheme = p.IsMinimal ? DesktopTheme.Minimal : p.Theme;
                SelectedProfile = p.Profile; ReduceAnimations = p.ReduceAnimations; ReduceTransparency = p.ReduceTransparency;
                NeedsBluetooth = p.NeedsBluetooth; NeedsPrinting = p.NeedsPrinting; NeedsCloudSync = p.NeedsCloudSync; NeedsVirtualization = p.NeedsVirtualization;
            }
            catch (Exception error) when (IsStorageError(error)) { _startupWarnings.Add("As preferências salvas não puderam ser lidas; os valores padrão serão usados."); }
            try { _reports.AddRange(await _storage.ReadHistoryAsync()); RebuildHistory(); }
            catch (Exception error) when (IsStorageError(error)) { _historyReadable = false; _startupWarnings.Add("O histórico não pôde ser lido e foi preservado. Exporte as novas sessões."); }
            try
            {
                var activities = await _storage.ReadRecentActivityAsync(10_000);
                var wingetEvents = activities.Where(entry => entry.Category == "winget" && entry.CorrelationId is not null).ToArray();
                var terminal = wingetEvents.Where(entry => entry.EventType is "upgrade-completed" or "upgrade-manual-review-cleared")
                    .Select(entry => entry.CorrelationId!).ToHashSet(StringComparer.Ordinal);
                foreach (var entry in wingetEvents.Where(entry => entry.EventType == "upgrade-started" && !terminal.Contains(entry.CorrelationId!)))
                {
                    try
                    {
                        using var details = JsonDocument.Parse(entry.DetailsJson ?? "{}");
                        if (details.RootElement.TryGetProperty("packageId", out var packageId) && packageId.GetString() is { Length: > 0 } id)
                        {
                            _pendingWingetPackages.Add(id);
                            _pendingWingetCorrelations[id] = entry.CorrelationId!;
                        }
                    }
                    catch (JsonException) { }
                }
                if (_pendingWingetPackages.Count > 0)
                    _startupWarnings.Add("Há atualização WinGet anterior sem resultado confirmado. Confira esses programas manualmente antes de tentar novamente.");
            }
            catch (Exception error) when (IsStorageError(error)) { _startupWarnings.Add("Não foi possível verificar tentativas anteriores do WinGet; atualizações ficam indisponíveis nesta sessão por segurança."); _wingetAuditReadable = false; }
            try
            {
                _storageHealth = await _storage.CheckHealthAsync();
                if (!_storageHealth.IsHealthy) _startupWarnings.Add("O banco local informou uma condição degradada. Os dados existentes foram preservados.");
                Notify(nameof(StorageHealth));
            }
            catch (Exception error) when (IsStorageError(error))
            {
                _startupWarnings.Add("A saúde do armazenamento local não pôde ser confirmada; os arquivos antigos foram preservados.");
                _storageHealth = new(false, 0, "unavailable", "unavailable", 0, 0, 0, error.GetType().Name);
            }
            await LoadLocalSessionsAsync();
            ApplyTheme();
            _loaded = true;
            QueueActivity(new(DateTimeOffset.UtcNow, "application", "started", "info", "ZEUS iniciado."));
        }
        finally { SetBusy(false); }
        await RefreshDiagnosticsAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshDiagnosticsAsync();
    private async Task RefreshDiagnosticsAsync()
    {
        await RunOperationAsync("Consultando o computador", "Lendo componentes e proteção do Windows.", async token =>
        {
            _snapshot = await _diagnostics.CollectAsync(token);
            DisplaySnapshot(_snapshot);
            StatusTitle = "Diagnóstico concluído";
            StatusDetail = _snapshot.Warnings.Count == 0 ? "Inventário atualizado. Complete seu perfil e revise o plano individual." : "Inventário atualizado com leituras limitadas. Consulte os avisos abaixo.";
        }, cancellable: true);
    }

    private void DisplaySnapshot(HardwareSnapshot snapshot)
    {
        HardwareCards.Clear();
        var cpu = snapshot.Cpu;
        HardwareCards.Add(new("Processador", cpu?.Name ?? "Indisponível", cpu is null ? "O Windows não retornou esta leitura." : $"{cpu.PhysicalCores} núcleos · {cpu.LogicalProcessors} processadores lógicos"));
        var memory = snapshot.Memory;
        HardwareCards.Add(new("Memória RAM", memory is null ? "Indisponível" : ByteFormatting.Format(memory.TotalBytes), memory is null ? "Leitura indisponível." : $"{ByteFormatting.Format(memory.AvailableBytes)} disponíveis nesta leitura"));
        var gpu = snapshot.Graphics.FirstOrDefault();
        HardwareCards.Add(new("Placas de vídeo", gpu?.Name ?? "Indisponível", gpu is null ? "Nenhum dispositivo retornado." : $"{snapshot.Graphics.Count} dispositivo(s) · Driver {Available(gpu.DriverVersion)}"));
        HardwareCards.Add(new("Armazenamento", $"{snapshot.Disks.Count} volume(s)", "Espaço livre e dispositivos físicos em Hardware."));
        HardwareCards.Add(new("Microsoft Defender", BooleanStatus(snapshot.Security?.DefenderEnabled), snapshot.Security?.Summary ?? "Consulte Segurança do Windows."));
        HardwareCards.Add(new("Placa-mãe", snapshot.Board?.Product ?? "Indisponível", snapshot.Board?.Manufacturer ?? "A placa não foi identificada nesta coleta."));
        var inventory = snapshot.WindowsInventory;
        ServiceDependencyRows.Clear();
        ServiceDependencySummary = inventory is null
            ? "Inventário de serviços indisponível nesta coleta."
            : "Use a consulta para descrever as dependências declaradas no inventário.";
        Notify(nameof(ServiceDependencySummary));
        DeviceRepairRows.Clear();
        if (inventory is null)
            DeviceRepairSummary = "Inventário PnP indisponível nesta coleta; estado dos dispositivos desconhecido.";
        else
        {
            var deviceProblems = inventory.PnpDevices.Where(device => !string.IsNullOrWhiteSpace(device.ProblemCode)).ToArray();
            foreach (var device in deviceProblems.Take(100))
            {
                var interpretation = PnpProblemInterpreter.Interpret(device.ProblemCode);
                DeviceRepairRows.Add(new($"Código {device.ProblemCode} · {Available(device.Name)}",
                    $"{interpretation.Meaning} Estado informado: {Available(device.Status)}. {interpretation.Guidance}"));
            }
            DeviceRepairSummary = deviceProblems.Length == 0
                ? "Nenhum código de problema PnP foi retornado nesta leitura; isso não exclui falhas não reportadas por esta fonte."
                : $"{deviceProblems.Length} dispositivo(s) com código reportado. As interpretações são limitadas ao significado documentado do código; causa física não determinada." +
                    (deviceProblems.Length > 100 ? " Exibindo os primeiros 100; o relatório contém o inventário coletado." : string.Empty);
        }
        Notify(nameof(DeviceRepairSummary));
        EventDiagnosticRows.Clear();
        if (inventory is null)
            EventDiagnosticSummary = "Inventário de eventos indisponível nesta coleta; padrões desconhecidos.";
        else
        {
            var eventSourcesComplete = !inventory.Warnings.Any(warning =>
                warning.StartsWith("Eventos System:", StringComparison.OrdinalIgnoreCase) ||
                warning.StartsWith("Eventos Application:", StringComparison.OrdinalIgnoreCase) ||
                warning.StartsWith("Eventos Microsoft-Windows-WindowsUpdateClient/Operational:", StringComparison.OrdinalIgnoreCase));
            var eventReport = EventPatternAnalyzer.Analyze(inventory.RecentEvents, eventSourcesComplete);
            foreach (var finding in eventReport.Findings.Take(20)) EventDiagnosticRows.Add(new(finding.Title, finding.Detail));
            EventDiagnosticSummary = eventReport.Findings.Count > 20
                ? $"{eventReport.Summary} Exibindo os primeiros 20 padrões."
                : eventReport.Summary;
        }
        Notify(nameof(EventDiagnosticSummary));
        if (inventory is null)
            NetworkResetPreparationSummary = "Configuração de rede indisponível nesta coleta; não use esta tela como cópia dos valores atuais.";
        else if (inventory.NetworkConfiguration.Count == 0)
            NetworkResetPreparationSummary = "Nenhuma configuração de interface foi retornada. A fonte pode estar incompleta; confira manualmente em Configurações do Windows.";
        else
        {
            var networkPreview = inventory.NetworkConfiguration.Take(8).Select(network =>
                $"{Available(network.Adapter)} · IP: {FormatNetworkValues(network.Addresses)} · DNS: {FormatNetworkValues(network.DnsServers)} · Gateway: {FormatNetworkValues(network.Gateways)}").ToArray();
            NetworkResetPreparationSummary = string.Join(Environment.NewLine, networkPreview) +
                (inventory.NetworkConfiguration.Count > 8 ? $"{Environment.NewLine}(mais {inventory.NetworkConfiguration.Count - 8} interface(s) no relatório)" : string.Empty) +
                $"{Environment.NewLine}Proxy observado em HKCU: {FormatProxyConfiguration(inventory.ProxyConfiguration)}" +
                $"{Environment.NewLine}Valores coletados são apenas uma referência; configuração estática e software especializado podem exigir anotação e recuperação próprias.";
        }
        Notify(nameof(NetworkResetPreparationSummary));
        HardwareCards.Add(new("Inventário do Windows", inventory is null ? "Indisponível" : $"{inventory.Processes.Count} processos · {inventory.Services.Count} serviços", inventory is null ? "As fontes do Windows não responderam nesta coleta." : $"{inventory.Drivers.Count} drivers · {inventory.PnpDevices.Count} dispositivos · {inventory.InstalledSoftware.Count} programas"));
        GraphicsRows.Clear(); foreach (var item in snapshot.Graphics) GraphicsRows.Add(new(Available(item.Name), $"Driver {Available(item.DriverVersion)}"));
        DiskRows.Clear(); foreach (var disk in snapshot.Disks) DiskRows.Add(new($"{disk.DriveLetter} · {Available(disk.Name)}", $"{ByteFormatting.Format(disk.FreeBytes)} livres de {ByteFormatting.Format(disk.TotalBytes)} · {Available(disk.FileSystem)}"));
        StartupRows.Clear(); foreach (var entry in snapshot.Startup) StartupRows.Add(new(Available(entry.Name), $"Origem: {Available(entry.Location)} · Usuário: {Available(entry.User)}"));
        ExtendedHardwareRows.Clear();
        if (snapshot.Board is { } board) ExtendedHardwareRows.Add(new("Placa-mãe", $"{Available(board.Manufacturer)} · {Available(board.Product)}"));
        if (snapshot.Bios is { } bios) ExtendedHardwareRows.Add(new("BIOS / UEFI", $"{Available(bios.Manufacturer)} · {Available(bios.Version)} · {Available(bios.ReleaseDate)}"));
        var memoryModules = snapshot.MemoryModules;
        if (memoryModules is not null || snapshot.MemoryArraySlotsReported is not null)
        {
            var moduleCount = memoryModules?.Count.ToString() ?? "indisponível";
            var slots = snapshot.MemoryArraySlotsReported?.ToString() ?? "não informado pelo firmware";
            ExtendedHardwareRows.Add(new("RAM · Slots e canais", $"{moduleCount} módulo(s) reportado(s) · {slots} slot(s) declarados · canais: não informados pelo provedor; não inferidos pela velocidade."));
        }
        foreach (var module in snapshot.MemoryModules ?? []) ExtendedHardwareRows.Add(new($"RAM · {Available(module.Location)}", $"{ByteFormatting.Format(module.CapacityBytes)} · {module.SpeedMHz?.ToString() ?? "Indisponível"} MHz · {Available(module.Manufacturer)}"));
        foreach (var disk in snapshot.PhysicalDisks ?? []) ExtendedHardwareRows.Add(new(Available(disk.Name), $"{disk.MediaType} · {disk.BusType} · {ByteFormatting.Format(disk.SizeBytes)} · Estado informado: {Available(disk.HealthStatus)}\nTemperatura informada: {(disk.TemperatureCelsius.HasValue ? $"{disk.TemperatureCelsius.Value:0.#} °C" : "indisponível")} (máx. {(disk.TemperatureMaxCelsius.HasValue ? $"{disk.TemperatureMaxCelsius.Value:0.#} °C" : "indisponível")}) · Desgaste informado: {disk.Wear?.ToString() ?? "indisponível"}\nHoras ligado: {disk.PowerOnHours?.ToString() ?? "indisponível"} · Erros leitura: {FormatDiskErrors(disk.ReadErrorsTotal, disk.ReadErrorsUncorrected)} · Erros gravação: {FormatDiskErrors(disk.WriteErrorsTotal, disk.WriteErrorsUncorrected)}"));
        foreach (var battery in snapshot.Batteries ?? []) ExtendedHardwareRows.Add(new(Available(battery.Name), $"Carga: {battery.ChargePercent?.ToString() ?? "indisponível"}% · {Available(battery.Status)}"));
        foreach (var network in snapshot.NetworkAdapters ?? []) ExtendedHardwareRows.Add(new(Available(network.Name), $"{Available(network.Status)} · Velocidade de enlace: {(network.SpeedBitsPerSecond.HasValue ? $"{network.SpeedBitsPerSecond.Value / 1_000_000d:0.#} Mbps" : "indisponível")}"));
        if (inventory is not null)
        {
            foreach (var network in inventory.NetworkConfiguration)
                ExtendedHardwareRows.Add(new($"Rede · {Available(network.Adapter)}", $"Estado: {Available(network.Status)} · IP: {FormatNetworkValues(network.Addresses)} · DNS: {FormatNetworkValues(network.DnsServers)} · Gateway: {FormatNetworkValues(network.Gateways)} · Rotas: {FormatNetworkValues(network.Routes)}"));
            ExtendedHardwareRows.Add(new("Proxy do usuário (HKCU)", FormatProxyConfiguration(inventory.ProxyConfiguration)));
            ExtendedHardwareRows.Add(new("Inicialização segura", inventory.SecurityState?.SecureBootEnabled is { } secureBoot ? (secureBoot ? "Ativada" : "Desativada") : "Indisponível"));
            ExtendedHardwareRows.Add(new("TPM", inventory.SecurityState?.TpmPresent is { } tpm ? (tpm ? $"Presente · {(inventory.SecurityState.TpmReady == true ? "pronto" : inventory.SecurityState.TpmReady == false ? "não pronto" : "estado indisponível")}" : "Não detectado") : "Indisponível"));
            ExtendedHardwareRows.Add(new("Tarefas agendadas", $"{inventory.ScheduledTasks.Count} entradas inventariadas; nomes e estados completos ficam no relatório exportado."));
            ExtendedHardwareRows.Add(new("Serviços", $"{inventory.Services.Count} entradas inventariadas; nenhuma foi alterada."));
            ExtendedHardwareRows.Add(new("Windows Update", inventory.UpdateState?.PendingCount is { } pending ? $"{pending} atualização(ões) pendente(s)" : "Atualizações pendentes não consultadas nesta leitura."));
            foreach (var device in inventory.PnpDevices.Where(device => !string.IsNullOrWhiteSpace(device.ProblemCode)).Take(20))
                ExtendedHardwareRows.Add(new($"Dispositivo com código {device.ProblemCode}", $"{Available(device.Name)} · {Available(device.Status)}"));
        }
        Warnings.Clear(); foreach (var warning in _startupWarnings.Concat(snapshot.Warnings)) Warnings.Add(warning);
        BuildPersonalPlan();
        foreach (var property in new[] { nameof(Snapshot), nameof(CollectionDate), nameof(SystemDescription), nameof(RecommendationEmptyText), nameof(FormalPlanSummary), nameof(FormalOptimizationPlan), nameof(DevicesEmptyText), nameof(CanExport), nameof(CanAnalyzeServiceDependencies), nameof(CanSaveNetworkResetReference) }) Notify(property);
    }

    private void BuildPersonalPlan()
    {
        Recommendations.Clear();
        if (_snapshot is null) return;
        _optimizationPlan = _ruleEngine.Evaluate(_snapshot, ToOptimizationProfile(SelectedProfile), ToWorkloadEvidence(_performance));
        foreach (var result in _optimizationPlan.Rules.Where(result => result.Triggered))
            Recommendations.Add(new(result.Rule.Title, result.Reason, result.Action is { } a ? $"Revisar em Manutenção: {MaintenanceCatalog.Get(a).Title}" : ""));
        var profile = SelectedProfile switch
        {
            UsageProfile.Gaming => ("Durante seus jogos", "Meça com o jogo aberto. Compare uso de CPU e RAM; quedas de FPS também podem depender da GPU, temperatura e configurações do jogo. Atualize drivers apenas quando houver compatibilidade e indicação."),
            UsageProfile.GamingStreaming => ("Durante jogos e transmissão", "Meça durante uma partida com OBS. A detecção de processos não confirma transmissão ao vivo; compare carga de CPU, GPU, memória e codificador antes de atribuir uma causa."),
            UsageProfile.Work => ("Durante o trabalho", "Meça com seus aplicativos e abas habituais. Revise inicialização preservando ferramentas de comunicação, segurança e sincronização necessárias."),
            UsageProfile.Creative => ("Durante edição e criação", "Meça durante a tarefa de edição ou exportação. Preserve backups e espaço de trabalho; confirme memória e armazenamento exigidos pelo seu editor antes de comprar componentes."),
            UsageProfile.Development => ("Durante a programação", "Meça durante a compilação, execução local e ferramentas habituais. Compare carga e processos na mesma tarefa; não encerre processos ou serviços necessários ao ambiente."),
            UsageProfile.Battery => ("Priorize autonomia", "Revise o plano de energia disponível em Perfil. Reduzir efeitos visuais pode ajudar a interface; autonomia também depende de brilho, aplicativos e condição da bateria."),
            _ => ("Comece pela tarefa lenta", "Meça durante o uso que apresenta lentidão. Revise temporários e inicialização antes de escolher reparos ou alterações de energia.")
        };
        Recommendations.Add(new(profile.Item1, profile.Item2, "Perfil e medição de carga disponíveis neste aplicativo"));
        if (ReduceAnimations || ReduceTransparency) Recommendations.Add(new("Preferências visuais escolhidas", $"Você escolheu {(ReduceAnimations ? "reduzir animações" : "preservar animações")} e {(ReduceTransparency ? "reduzir transparência" : "preservar transparência")}. Aplique em Perfil; o estado anterior será registrado.", "Aplicar preferências visuais"));
        var required = new List<string>(); if (NeedsBluetooth) required.Add("Bluetooth"); if (NeedsPrinting) required.Add("impressão"); if (NeedsCloudSync) required.Add("sincronização"); if (NeedsVirtualization) required.Add("virtualização");
        Recommendations.Add(new("Recursos necessários ao seu uso", required.Count > 0 ? $"Seu plano preserva {string.Join(", ", required)}. Revise programas relacionados antes de desativar sua inicialização." : "Você não marcou dependências adicionais. As alterações continuam seletivas; nenhum serviço é desativado automaticamente.", "Revise cada entrada em Inicialização"));
        if (_performance is { } sample && sample.CpuPercent >= 85) Recommendations.Add(new("CPU muito ocupada nesta amostra", $"Uso observado de {sample.CpuPercent:0.#}% no intervalo de CPU de {sample.SamplingDuration.TotalSeconds:0.#} segundos. Consulte os processos em Hardware e repita durante a tarefa; uma amostra isolada não comprova um gargalo.", "Medição real de carga"));
        Notify(nameof(RecommendationEmptyText));
        Notify(nameof(FormalPlanSummary));
        Notify(nameof(FormalOptimizationPlan));
    }

    private static OptimizationProfile ToOptimizationProfile(UsageProfile profile) => profile switch
    {
        UsageProfile.Gaming => OptimizationProfile.Gaming,
        UsageProfile.GamingStreaming => OptimizationProfile.GamingStreaming,
        UsageProfile.Work => OptimizationProfile.Work,
        UsageProfile.Creative => OptimizationProfile.Editing,
        UsageProfile.Development => OptimizationProfile.Development,
        UsageProfile.Battery => OptimizationProfile.General,
        _ => OptimizationProfile.General
    };

    private static OptimizationWorkloadEvidence? ToWorkloadEvidence(PerformanceObservation? observation)
    {
        if (observation is not { } sample) return null;
        double? availableMemoryPercent = sample.TotalMemoryBytes > 0 && sample.AvailableMemoryBytes <= sample.TotalMemoryBytes
            ? (double)sample.AvailableMemoryBytes / sample.TotalMemoryBytes * 100
            : null;
        return new(sample.CpuPercent, availableMemoryPercent,
            sample.ActivityContext?.KnownGameProcessDetected,
            sample.ActivityContext?.ObsProcessDetected);
    }

    private void ShowPendingHardware()
    {
        foreach (var title in new[] { "Processador", "Memória RAM", "Placas de vídeo", "Armazenamento", "Microsoft Defender", "Placa-mãe" }) HardwareCards.Add(new(title, "Aguardando leitura", "Dados locais do Windows"));
    }
    private void ProfileChanged() { BuildPersonalPlan(); NotifyActionState(); QueuePreferencesSave(); }
    private DesktopPreferences CurrentPreferences() => new(IsMinimal, SelectedTheme, SelectedProfile, ReduceAnimations, ReduceTransparency, NeedsBluetooth, NeedsPrinting, NeedsCloudSync, NeedsVirtualization);
    private void QueuePreferencesSave() { if (_loaded) _ = SavePreferencesAsync(); }
    private async Task SavePreferencesAsync()
    {
        await _preferenceLock.WaitAsync();
        try { await _storage.SavePreferencesAsync(CurrentPreferences()); }
        catch (Exception error) when (IsStorageError(error)) { StatusDetail = $"As preferências desta sessão não foram salvas: {error.Message}"; }
        finally { _preferenceLock.Release(); }
    }

    private async Task DrainLocalWritesAsync(CancellationToken cancellationToken)
    {
        await _preferenceLock.WaitAsync(cancellationToken);
        _preferenceLock.Release();
        var pending = _activityWrites.ToArray();
        if (pending.Length > 0) await Task.WhenAll(pending).WaitAsync(cancellationToken);
    }

    private void ApplyTheme()
    {
        if (SystemParameters.HighContrast) return;
        var colors = SelectedTheme == DesktopTheme.MacInspired
            ? new[] { "#151625", "#202235", "#3C3E58", "#F5F4FC", "#CBCBDF", "#C5B4FF", "#303248", "#37304F", "#0D0D18" }
            : SelectedTheme == DesktopTheme.Minimal
                ? new[] { "#101216", "#191D22", "#3B424A", "#F5F7FA", "#BEC6D1", "#BFE7D7", "#282F37", "#293C35", "#0D1013" }
                : new[] { "#0A1120", "#131F32", "#2B3F59", "#F0F5FA", "#B1C1D5", "#65E3E0", "#1D3049", "#1A3546", "#080F1B" };
        var keys = new[] { "BackgroundBrush", "PanelBrush", "BorderBrush", "TextBrush", "MutedBrush", "AccentBrush", "ButtonBrush", "SelectedTabBrush", "LogBackgroundBrush" };
        for (var i = 0; i < keys.Length; i++) Application.Current.Resources[keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
        Application.Current.Resources["PrimaryButtonBrush"] = Application.Current.Resources["AccentBrush"];
        Application.Current.Resources["SelectedTabTextBrush"] = Application.Current.Resources["AccentBrush"];
        Application.Current.Resources["ButtonTextBrush"] = Application.Current.Resources["TextBrush"];
    }

    private async Task RunOperationAsync(string title, string detail, Func<CancellationToken, Task> operation, bool cancellable = false, bool mutation = false)
    {
        if (_isBusy) return;
        _isExecuting = mutation;
        if (cancellable) _readCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        SetBusy(true); StatusTitle = title; StatusDetail = detail;
        QueueActivity(new(DateTimeOffset.UtcNow, mutation ? "maintenance" : "diagnostics", "started", "info", title));
        try
        {
            await operation(_readCancellation?.Token ?? _lifetime.Token);
            QueueActivity(new(DateTimeOffset.UtcNow, mutation ? "maintenance" : "diagnostics", "completed", "info", title));
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested || _readCancellation?.IsCancellationRequested == true)
        {
            StatusTitle = "Leitura cancelada"; StatusDetail = "Os últimos dados disponíveis foram preservados.";
            QueueActivity(new(DateTimeOffset.UtcNow, "operation", "cancelled", "info", title));
        }
        catch (Exception error)
        {
            StatusTitle = "Operação não concluída"; StatusDetail = error.Message; AppendLog($"{title}: {error.Message}");
            QueueActivity(new(DateTimeOffset.UtcNow, "operation", "failed", "warning", title,
                JsonSerializer.Serialize(new { errorType = error.GetType().Name })));
        }
        finally { _readCancellation?.Dispose(); _readCancellation = null; _isExecuting = false; SetBusy(false); }
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => _readCancellation?.Cancel();
    private void OpenMaintenance_Click(object sender, RoutedEventArgs e) => WorkspaceTabs.SelectedIndex = 1;
    private void OpenProfile_Click(object sender, RoutedEventArgs e) => WorkspaceTabs.SelectedIndex = 4;
    private void RebuildHistory() { HistoryRows.Clear(); foreach (var report in _reports) HistoryRows.Add(HistoryRow.From(report)); Notify(nameof(HistoryEmptyText)); Notify(nameof(CanExport)); }
    private void AppendLog(string message)
    {
        ExecutionLog += $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
        QueueActivity(new(DateTimeOffset.UtcNow, "activity", "log", "info", message));
    }

    private void QueueActivity(ActivityEntry entry)
    {
        if (entry.Summary.Length > 2_000) entry = entry with { Summary = entry.Summary[..1_997] + "…" };
        _activityWrites.RemoveAll(task => task.IsCompleted);
        _activityWrites.Add(PersistActivitySafelyAsync(entry));
    }

    private async Task<bool> PersistActivitySafelyAsync(ActivityEntry entry)
    {
        try { await _storage.AppendActivityAsync(entry); return true; }
        catch (Exception error) when (IsStorageError(error))
        {
            if (Interlocked.Exchange(ref _activityStorageWarningShown, 1) == 0)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    _startupWarnings.Add("O registro de atividades não pôde ser gravado no banco local.");
                    if (_snapshot is not null) { Warnings.Clear(); foreach (var warning in _startupWarnings.Concat(_snapshot.Warnings)) Warnings.Add(warning); }
                    _storageHealth = new(false, _storageHealth?.SchemaVersion ?? 0, "unavailable", "unavailable", 0, 0, 0, error.GetType().Name);
                    Notify(nameof(StorageHealth));
                });
            }
            return false;
        }
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_isExecuting) { e.Cancel = true; MessageBox.Show(this, "Uma alteração está em andamento. Aguarde o resultado antes de fechar o ZEUS.", "Aguarde a conclusão", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        if (_closingAfterActivityDrain) return;
        QueueActivity(new(DateTimeOffset.UtcNow, "application", "stopping", "info", "ZEUS encerrando."));
        var pending = _activityWrites.ToArray();
        if (pending.Length > 0)
        {
            e.Cancel = true;
            _closingAfterActivityDrain = true;
            IsEnabled = false;
            try { await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (Exception) { }
            _lifetime.Cancel();
            _ = Dispatcher.BeginInvoke(new Action(Close));
            return;
        }
        _lifetime.Cancel();
    }
    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        foreach (var p in new[] { nameof(CanRefresh), nameof(CanChooseActions), nameof(CanAnalyzeServiceDependencies), nameof(CanOpenNetworkResetSettings), nameof(CanSaveNetworkResetReference), nameof(CanExport), nameof(CanCancel), nameof(CanQuarantine), nameof(CanDisableStartup), nameof(CanSetPowerPlan), nameof(CanInstallDriver), nameof(CanOfflineScan), nameof(CanSetPerformanceBaseline), nameof(CanComparePerformance) }) Notify(p);
        NotifyActionState();
    }
    private void NotifyActionState() { Notify(nameof(CanExecute)); Notify(nameof(SelectedActionsText)); Notify(nameof(CanQuarantine)); Notify(nameof(CleanupSelectedText)); Notify(nameof(CanDisableStartup)); Notify(nameof(CanInstallDriver)); Notify(nameof(CanApplyWallpaper)); Notify(nameof(CanGeneralOptimize)); Notify(nameof(GeneralPlanSummary)); Notify(nameof(CanOpenNetworkResetSettings)); }
    private string? MaintenanceSelectionError()
    {
        var selected = MaintenanceChoices.Where(choice => choice.IsSelected)
            .Select(choice => new MaintenanceRequest(choice.Id)).ToArray();
        if (selected.Length == 0) return null;
        try { _ = MaintenancePolicy.ValidateRequests(selected); return null; }
        catch (ArgumentException error) { return error.Message; }
    }
    private static bool IsStorageError(Exception error) => error is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or DbException;
    private static string Available(string? value) => string.IsNullOrWhiteSpace(value) ? "Indisponível" : value;
    private static string FormatDiskErrors(ulong? total, ulong? uncorrected) => total is null && uncorrected is null
        ? "indisponíveis"
        : $"{total?.ToString() ?? "indisponível"} total / {uncorrected?.ToString() ?? "indisponível"} não corrigidos";
    private static string FormatNetworkValues(IEnumerable<string>? values)
    {
        var items = values?.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray() ?? [];
        return items.Length == 0 ? "não informado" : string.Join(", ", items.Take(6)) + (items.Length > 6 ? $" (+{items.Length - 6}; ver relatório)" : "");
    }
    private static string FormatProxyConfiguration(ProxyConfigurationInfo? proxy)
    {
        if (proxy is not { IsAvailable: true }) return "Estado indisponível nesta coleta. Fonte consultada: Registro HKCU Internet Settings.";
        var manual = proxy.ManualProxyEnabled switch
        {
            true => $"manual ativado · servidor: {Available(proxy.ManualProxyServer)}",
            false => $"manual desativado{(string.IsNullOrWhiteSpace(proxy.ManualProxyServer) ? "" : $" · valor armazenado: {proxy.ManualProxyServer}")}",
            null => "manual: estado não informado pelo Registro"
        };
        var pac = string.IsNullOrWhiteSpace(proxy.AutoConfigUrl) ? "PAC: URL não configurada" : $"PAC configurado: {proxy.AutoConfigUrl}";
        var autodetect = proxy.AutoDetectEnabled switch { true => "AutoDetect no Registro: ativado", false => "AutoDetect no Registro: desativado", null => "AutoDetect no Registro: não informado" };
        var bypass = string.IsNullOrWhiteSpace(proxy.BypassList) ? "lista de exceções: não informada" : $"lista de exceções: {proxy.BypassList}";
        return $"{manual} · {pac} · {autodetect} · {bypass}. Fonte: HKCU Internet Settings; WinHTTP e configurações por aplicativo não consultados.";
    }
    private static string FormatMetric(double? value) => value is { } number ? $"{number:0.#}%" : "indisponível";
    private static string FormatByteQuantity(double? bytes) => bytes is { } value && double.IsFinite(value) && value >= 0
        ? $"{value / (1024d * 1024 * 1024):0.##} GiB" : "indisponível";
    private static string FormatMetricCoverage(PerformanceMetricComparison? metric, int referenceTotal, int laterTotal) => metric is null
        ? "indisponível"
        : $"{FormatMetric(metric.ReferencePercent)} ({metric.ReferenceAvailableSamples}/{referenceTotal}) → {FormatMetric(metric.LaterPercent)} ({metric.LaterAvailableSamples}/{laterTotal})";
    private static string BooleanStatus(bool? value) => value switch { true => "Ativo", false => "Desativado", null => "Indisponível" };
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; Notify(name); return true; }
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
