using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
    private readonly ZeusReleaseChecker _zeusReleaseChecker = new();
    private readonly PendingMaintenanceSessions _pendingSessions = new();
    private readonly UserOptimizationService _userOptimization;
    private readonly DesktopFileOrganizer _desktopOrganizer;
    private readonly TemporaryFileCleanup _cleanup;
    private readonly Action<string> _openUri;
    private readonly DatabaseDialogCallbacks? _databaseDialogCallbacks;
    private readonly bool _isFixture;
    private readonly OptimizationRuleEngine _ruleEngine = new();
    private readonly DesktopStorage _storage;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ObservableCollection<VisualLayoutPreset> _visualLayoutPresets = new(VisualLayoutCatalog.Load());
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
    private bool _firstRunSetupComplete = true;
    private bool _isTechnicalMode;
    private DesktopTheme _selectedTheme = DesktopTheme.Complete;
    private DesktopDensity _selectedDensity = DesktopDensity.Comfortable;
    private AppAccentColor _selectedAccentColor = AppAccentColor.ThemeDefault;
    private VisualLayoutPreset? _selectedVisualLayoutPreset;
    private bool _visualLayoutPreviewActive;
    private bool? _visualLayoutReduceMotionPreview;
    private string? _savedVisualLayoutPresetId;
    private string? _customVisualLayoutsJson;
    private string? _customAccentHex, _customAccentPreviewHex;
    private string _customAccentDraftHex = "#00FFFF";
    private string _customAccentValidationSummary = "Informe uma cor #RRGGBB; a prévia fica somente nesta janela até confirmar.";
    private string _visualLayoutCatalogStatus = "Perfis personalizados aceitam somente cores e temas do ZEUS; nenhum código ou imagem será executado.";
    private readonly List<VisualLayoutPreset> _customVisualLayoutPresets = [];
    private UsageProfile _selectedProfile = UsageProfile.Balanced;
    private bool _reduceAnimations, _reduceTransparency, _reduceZeusMotion, _needsBluetooth = true, _needsPrinting = true, _needsCloudSync = true, _needsVirtualization;
    private bool _desktopClockEnabled, _desktopClockShowDate = true, _desktopClockShowSeconds, _desktopClockAlwaysOnTop, _desktopClockHideDuringFullscreen = true, _desktopClockUse24HourFormat = true;
    private DesktopClockSize _desktopClockSize = DesktopClockSize.Medium;
    private DesktopClockStyle _desktopClockStyle = DesktopClockStyle.Glass;
    private double _desktopClockOpacity = 0.88, _desktopClockLeft = 40, _desktopClockTop = 80;
    private DesktopClockPreferences _savedClockPreferences = new();
    private bool _desktopClockSettingsPreviewing;
    private bool _checkZeusUpdatesAutomatically;
    private DateTimeOffset? _lastZeusUpdateCheckUtc;
    private CancellationTokenSource? _automaticZeusUpdateCheckCancellation;
    private DesktopClockWindow? _desktopClock;
    private bool _offlineRestartConfirmed, _offlineRecoveryConfirmed;
    private bool _networkResetReviewed, _networkResetRecoveryReady;
    private bool _wingetAuditReadable = true;
    private int _hardwareCardColumns = 3;
    private bool _closingAfterActivityDrain;
    private bool _isClosing;
    private int _activityStorageWarningShown;
    private DatabaseHealth? _storageHealth;
    private PowerPlanInfo? _selectedPowerPlan;
    private string? _selectedWallpaperPath;
    private WallpaperMonitorChoice? _selectedWallpaperMonitor;
    private WallpaperPosition? _selectedWallpaperPosition;
    private WallpaperPosition? _currentWallpaperPosition;
    private bool _wallpaperSlideshowDetected;
    private ImageSource? _wallpaperPreview;
    private ImageBrush? _wallpaperPreviewBrush;
    private double _wallpaperPreviewWidth = 640;
    private double _wallpaperPreviewHeight = 360;
    private string _wallpaperPreviewSummary = "A prévia usa proporção 16:9 enquanto os limites do destino não estão disponíveis.";
    private DesktopOrganizationPreview? _desktopOrganizationPreview;
    private string _desktopOrganizationSummary = "Gere uma prévia para ver quais arquivos comuns seriam movidos. Pastas, atalhos e itens não reconhecidos ficam onde estão.";
    private string _statusTitle = "Preparando diagnóstico", _statusDetail = "As informações serão lidas diretamente neste computador.";
    private string _executionLog = "Nenhuma manutenção executada nesta sessão.", _maintenanceResultSummary = string.Empty;
    private string _cleanupSummary = "Analise temporários com mais de sete dias. Nenhum arquivo será selecionado automaticamente.";
    private string _startupSummary = "Leia os programas do seu usuário para escolher o que precisa iniciar com o Windows.";
    private string _profileSummary = "O perfil orienta o plano. Ajustes do Windows são separados e reversíveis.";
    private string _driverSummary = "Consulte os drivers oferecidos oficialmente pelo Windows Update para este computador.";
    private string _wingetSummary = "Consulte atualizações de programas identificadas pela fonte winget. A consulta não instala nada.";
    private string _zeusReleaseSummary = "Consulte manualmente se há uma versão mais recente do ZEUS. Nada será baixado ou instalado.";
    private string _performanceSummary = "Meça por cinco segundos durante a tarefa lenta para observar a carga real.";
    private string _performanceActivityLabel = string.Empty;
    private string _performanceSessionHistorySummary = "As sessões salvas aparecem aqui depois da primeira medição.";
    private PerformanceSessionExport[] _performanceSessionExports = [];
    private string _networkProbeTarget = string.Empty;
    private string _networkProbeSummary = "A medição só começa quando você informa um IP ou host e solicita o teste.";

    public MainWindow() : this(null, null, null, null) { }

    public MainWindow(string? storageRoot, Action<string>? openUri = null) : this(storageRoot, openUri, null, null) { }

    internal MainWindow(string? storageRoot, Action<string>? openUri, Size? workAreaOverride,
        DatabaseDialogCallbacks? databaseDialogCallbacks = null)
    {
        _isFixture = storageRoot is not null;
        _openUri = openUri ?? OpenSystemUri;
        _databaseDialogCallbacks = databaseDialogCallbacks;
        _storage = new(storageRoot);
        _userOptimization = new(storageRoot is null ? null : Path.Combine(storageRoot, "Changes"));
        _desktopOrganizer = new(storageRoot is null ? null : Path.Combine(storageRoot, "Desktop"), storageRoot is null ? null : Path.Combine(storageRoot, "DesktopOrganization"));
        _cleanup = new(storageRoot is null ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp") : Path.Combine(storageRoot, "Temporary"),
            Path.Combine(storageRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zeus"), "Cleanup"));
        InitializeComponent();
        var systemWorkArea = SystemParameters.WorkArea;
        var workArea = workAreaOverride ?? new Size(systemWorkArea.Width, systemWorkArea.Height);
        var minimumSize = ClampMinimumWindowSize(new Size(MinWidth, MinHeight), workArea);
        MinWidth = minimumSize.Width; MinHeight = minimumSize.Height;
        MaxWidth = Math.Max(MinWidth, workArea.Width); MaxHeight = Math.Max(MinHeight, workArea.Height);
        foreach (var definition in MaintenanceCatalog.All.Where(d => d.Id is not MaintenanceActionId.InstallDriverUpdate and not MaintenanceActionId.RollbackDriver and not MaintenanceActionId.DefenderOfflineScan))
        {
            var choice = new MaintenanceChoice(definition);
            choice.PropertyChanged += (_, _) => NotifyActionState();
            MaintenanceChoices.Add(choice);
        }
        ShowPendingHardware();
        DataContext = this;
    }

    private static void OpenSystemUri(string uri) =>
        _ = Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })
            ?? throw new InvalidOperationException("O Windows não abriu o destino solicitado.");

    internal static Size ClampMinimumWindowSize(Size configuredMinimum, Size workArea) => new(
        Math.Max(1, Math.Min(configuredMinimum.Width, Math.Max(1, workArea.Width))),
        Math.Max(1, Math.Min(configuredMinimum.Height, Math.Max(1, workArea.Height))));

    internal static int ResolveHardwareCardColumns(double availableWidth) =>
        !double.IsFinite(availableWidth) || availableWidth <= 0 ? 3 : availableWidth < 520 ? 1 : availableWidth < 840 ? 2 : 3;

    internal static HardwareCard CreateGraphicsOverviewCard(IReadOnlyList<GpuInfo> graphics)
    {
        if (graphics.Count == 0)
            return new("Placas de vídeo", "Indisponível", "O Windows não retornou adaptadores nesta coleta.");

        if (graphics.Count == 1)
            return new("Placas de vídeo", Available(graphics[0].Name), $"1 adaptador · Driver {Available(graphics[0].DriverVersion)}");

        var shown = graphics.Take(3)
            .Select(gpu => $"{Available(gpu.Name)} · Driver {Available(gpu.DriverVersion)}")
            .ToList();
        var remaining = graphics.Count - shown.Count;
        if (remaining > 0) shown.Add($"+ {remaining} adaptador(es) na aba Hardware.");
        return new("Placas de vídeo", $"{graphics.Count} adaptadores identificados", string.Join(Environment.NewLine, shown));
    }

    public HardwareSnapshot? Snapshot => _snapshot;
    public string BuildVersion { get; } = typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "Indisponível";
    public OptimizationPlan? FormalOptimizationPlan => _optimizationPlan;
    public DatabaseHealth? StorageHealth => _storageHealth;
    public PerformanceObservation? Performance => _performance;
    public IReadOnlyList<PerformanceHistoryEntry> PerformanceHistory => _performanceHistory.Snapshot();
    public bool CanSetPerformanceBaseline => !_isBusy &&
        PerformanceSessionSelection.LatestSessionSamples(_performanceHistory.Snapshot()).Count >= 3;
    public bool CanComparePerformance => !_isBusy && _performanceBaseline.Length >= 3 &&
        PerformanceSessionSelection.LatestSessionSamples(_performanceHistory.Snapshot(), after: _performanceBaseline[^1].CollectedAt).Count >= 3;
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
                        $"{item.Name} · PID {item.ProcessId}: CPU {FormatMetric(item.ReferenceCpuPercent)} ({item.ReferenceCpuSamples}/{comparison.ReferenceSampleCount}) → {FormatMetric(item.LaterCpuPercent)} ({item.LaterCpuSamples}/{comparison.LaterSampleCount}); núcleos equivalentes {FormatCpuCores(item.ReferenceCpuCoresUsed)} ({item.ReferenceCpuCoresSamples}/{comparison.ReferenceSampleCount}) → {FormatCpuCores(item.LaterCpuCoresUsed)} ({item.LaterCpuCoresSamples}/{comparison.LaterSampleCount}); memória {FormatByteQuantity(item.ReferenceWorkingSetBytes)} ({item.ReferenceWorkingSetSamples}/{comparison.ReferenceSampleCount}) → {FormatByteQuantity(item.LaterWorkingSetBytes)} ({item.LaterWorkingSetSamples}/{comparison.LaterSampleCount})"))
                : "Comparação por processo: indisponível (identidade do processo não confirmada nos períodos)";
            var gpuProcesses = comparison.GpuProcessMemoryUsage is { Count: > 0 } gpuProcessUsage
                ? "Memória GPU dedicada por processo (PID/início/adaptador confirmados): " + string.Join("; ", gpuProcessUsage
                    .OrderByDescending(item => Math.Max(item.LaterDedicatedBytes ?? -1, item.ReferenceDedicatedBytes ?? -1))
                    .Take(5).Select(item => $"{item.ProcessName} · PID {item.ProcessId} · {item.AdapterInstance}: {FormatByteQuantity(item.ReferenceDedicatedBytes)} ({item.ReferenceAvailableSamples}/{comparison.ReferenceSampleCount}; capacidade {FormatMetric(item.ReferenceCapacitySharePercent)} em {item.ReferenceCapacityShareSamples} amostra(s)) → {FormatByteQuantity(item.LaterDedicatedBytes)} ({item.LaterAvailableSamples}/{comparison.LaterSampleCount}; capacidade {FormatMetric(item.LaterCapacitySharePercent)} em {item.LaterCapacityShareSamples} amostra(s))"))
                : "Memória GPU dedicada por processo: indisponível (PID, horário de início e adaptador não confirmados nos períodos)";
            var taskLabels = FormatPerformanceComparisonTaskLabels(comparison, _performanceSessionExports);
            return string.Join(Environment.NewLine,
                $"Sessão de referência: até {comparison.ReferenceEndedAt.ToLocalTime():dd/MM HH:mm:ss} ({comparison.ReferenceSampleCount} amostras) → sessão posterior: até {comparison.LaterEndedAt.ToLocalTime():dd/MM HH:mm:ss} ({comparison.LaterSampleCount} amostras).",
                taskLabels,
                $"CPU média: {FormatMetricCoverage(comparison.CpuUsage, comparison.ReferenceSampleCount, comparison.LaterSampleCount)} · RAM em uso: {FormatMetricCoverage(comparison.MemoryUsage, comparison.ReferenceSampleCount, comparison.LaterSampleCount)} · pico médio da engine GPU mais ativa: {FormatMetricCoverage(comparison.GpuEnginePeak, comparison.ReferenceSampleCount, comparison.LaterSampleCount)} · pico médio de atividade de disco: {FormatMetricCoverage(comparison.DiskActivityPeak, comparison.ReferenceSampleCount, comparison.LaterSampleCount)}",
                diskIo,
                gpuMemory,
                gpuProcesses,
                network,
                context,
                processes,
                "Interpretação: cobertura mostra amostras válidas sobre o total; engines individuais não são uso total da GPU, ocupação não comprova gargalo e o contexto heurístico não confirma partida ou transmissão. Comparação descritiva, sem atribuir causa ou ganho.");
        }
    }

    internal static string FormatPerformanceComparisonTaskLabels(
        PerformanceComparison comparison, IReadOnlyList<PerformanceSessionExport> sessions)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        ArgumentNullException.ThrowIfNull(sessions);

        static string? DeclaredActivity(PerformanceSessionExport? session)
        {
            if (session is null) return null;
            const string separator = " · ";
            var index = session.Label.IndexOf(separator, StringComparison.Ordinal);
            if (index < 0) return null;
            var value = session.Label[(index + separator.Length)..].Trim();
            return value.Length == 0 ? null : value;
        }

        static PerformanceSessionExport? FindSession(
            IReadOnlyList<PerformanceSessionExport> source, DateTimeOffset observedAt, bool reference) => source
            .Where(session => session.IsReference == reference && session.StartedAt <= observedAt &&
                (session.FinishedAt is null || session.FinishedAt.Value >= observedAt))
            .OrderByDescending(session => session.StartedAt)
            .FirstOrDefault();

        var reference = DeclaredActivity(FindSession(sessions, comparison.ReferenceEndedAt, reference: true));
        var later = DeclaredActivity(FindSession(sessions, comparison.LaterEndedAt, reference: false));
        if (reference is null && later is null)
            return "Atividade informada: sem rótulo em ambos os períodos; isso não confirma que as tarefas foram iguais.";
        if (reference is null || later is null)
            return $"Atividade informada: {reference ?? "sem rótulo"} → {later ?? "sem rótulo"}. A equivalência das tarefas não foi confirmada.";
        if (!string.Equals(reference, later, StringComparison.OrdinalIgnoreCase))
            return $"Rótulos de atividade diferentes: “{reference}” → “{later}”. Confira se os períodos são comparáveis.";
        return $"Atividade informada nos dois períodos: “{reference}”. O rótulo não comprova que as condições foram equivalentes.";
    }

    private static string FormatRate(double? bytesPerSecond) => bytesPerSecond is { } value && double.IsFinite(value) && value >= 0
        ? FormatBytesPerSecond(value >= ulong.MaxValue ? ulong.MaxValue : (ulong)Math.Round(value))
        : "indisponível";

    private static string FormatLatency(double? milliseconds) => milliseconds is { } value && double.IsFinite(value) && value >= 0
        ? $"{value:0.##} ms"
        : "indisponível";
    public ObservableCollection<HardwareCard> HardwareCards { get; } = [];
    public int HardwareCardColumns { get => _hardwareCardColumns; private set => Set(ref _hardwareCardColumns, value); }
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
    public ObservableCollection<DesktopOrganizationSession> DesktopOrganizationSessions { get; } = [];
    public ObservableCollection<PowerPlanInfo> PowerPlans { get; } = [];
    public ObservableCollection<WallpaperMonitorChoice> WallpaperMonitorChoices { get; } = [];
    public IReadOnlyList<WallpaperPositionOption> WallpaperPositionOptions { get; } =
    [
        new(null, "Manter ajuste atual", "Preserva o modo definido no Windows."),
        new(WallpaperPosition.Fill, "Preencher · pode cortar bordas", "Preenche a tela mantendo proporção; partes da imagem podem ficar fora da área visível."),
        new(WallpaperPosition.Fit, "Ajustar · sem recorte", "Mantém a imagem inteira; o Windows pode mostrar faixas da cor de fundo."),
        new(WallpaperPosition.Stretch, "Esticar", "Preenche cada tela e pode distorcer a proporção."),
        new(WallpaperPosition.Center, "Centralizar", "Mantém o tamanho original da imagem e a centraliza."),
        new(WallpaperPosition.Tile, "Lado a lado", "Repete a imagem em blocos nas telas."),
        new(WallpaperPosition.Span, "Estender pelos monitores", "Usa uma imagem contínua no conjunto de telas; exige selecionar todos os monitores.")
    ];
    public string CurrentWallpaperPositionSummary => _currentWallpaperPosition is { } position
        ? $"Ajuste atual no Windows: {UserOptimizationService.WallpaperPositionName(position)}."
        : "Ajuste atual no Windows: indisponível nesta leitura.";
    public WallpaperPosition? SelectedWallpaperPosition => _selectedWallpaperPosition;
    public WallpaperPositionOption SelectedWallpaperPositionOption
    {
        get => WallpaperPositionOptions.First(option => option.Value == _selectedWallpaperPosition);
        set
        {
            if (value is null || !WallpaperPositionOptions.Contains(value) || !Set(ref _selectedWallpaperPosition, value.Value)) return;
            Notify(nameof(CanApplyWallpaper));
            RefreshWallpaperPreviewPresentation();
        }
    }
    public ObservableCollection<DriverChoice> DriverCandidates { get; } = [];
    public ObservableCollection<DriverInventoryRow> InstalledDriverRows { get; } = [];
    public ObservableCollection<DriverRollbackChoice> RollbackDriverChoices { get; } = [];
    public ObservableCollection<WingetUpdateRow> WingetUpdates { get; } = [];
    public ObservableCollection<WindowsUpdateRow> PendingWindowsUpdates { get; } = [];
    public ObservableCollection<WindowsUpdateHistoryRow> WindowsUpdateHistory { get; } = [];
    public IReadOnlyList<ProfileOption> ProfileOptions { get; } = [new(UsageProfile.Balanced, "Geral"), new(UsageProfile.Gaming, "Jogos"), new(UsageProfile.GamingStreaming, "Jogos e transmissão"), new(UsageProfile.Work, "Trabalho e estudo"), new(UsageProfile.Creative, "Edição e criação"), new(UsageProfile.Development, "Programação"), new(UsageProfile.Battery, "Autonomia no notebook")];
    public IReadOnlyList<ThemeOption> ThemeOptions { get; } =
    [
        new(DesktopTheme.Complete, "Completo · ZEUS", "Interface escura completa, com navegação e detalhes do diagnóstico disponíveis."),
        new(DesktopTheme.Minimal, "Mínimo · Foco", "Interface compacta que esconde detalhes avançados até você ativar o modo técnico."),
        new(DesktopTheme.MacInspired, "Aurora · inspirado no macOS", "Interface escura com acento violeta e cartões suaves, inspirada em uma estética Aurora."),
        new(DesktopTheme.Light, "Claro · leitura", "Interface clara com contraste ajustado para leitura em superfícies claras."),
        new(DesktopTheme.GamingNeon, "Gamer Neon · foco em jogos", "Interface escura com acento verde neon; não altera jogos, drivers ou configurações de desempenho."),
        new(DesktopTheme.Cyberpunk, "Cyberpunk · criação", "Interface escura com acento rosa; não altera jogos, drivers ou configurações de desempenho."),
        new(DesktopTheme.RetroAmber, "Retrô âmbar", "Paleta escura inspirada em terminais e monitores clássicos, com destaque âmbar."),
        new(DesktopTheme.Monochrome, "Monocromático", "Interface em tons neutros, sem depender de cores fortes para indicar navegação.")
    ];
    public ObservableCollection<VisualLayoutPreset> VisualLayoutPresets => _visualLayoutPresets;
    public IReadOnlyList<AppearanceCapabilityRow> AppearanceCapabilities { get; } =
    [
        new("Tema e cor de destaque", "Neste aplicativo", "Aplica a paleta escolhida na interface ZEUS e salva a preferência localmente."),
        new("Relógio do ZEUS", "Configuração separada", "As opções do relógio só mudam quando você as altera no cartão do relógio."),
        new("Papel de parede", "Ação separada", "Não é incluído no tema. Escolha uma imagem, confira a prévia e revise a aplicação por monitor."),
        new("Animações e transparência do Windows", "Ação separada", "Não são alteradas pelo tema; use a ação própria para revisar, aplicar e restaurar essas preferências."),
        new("Iniciar, barra de tarefas, sons e tela de bloqueio", "Configurações do Windows", "O ZEUS apenas abre páginas oficiais quando solicitado; essa navegação não conta como alteração aplicada ou reversão pelo ZEUS.")
    ];
    public IReadOnlyList<CustomizationResourceOption> CustomizationResources { get; } =
    [
        new("powertoys", "Microsoft PowerToys · FancyZones e Workspaces", "Organiza janelas em áreas ou abre conjuntos de aplicativos em posições salvas.", "Microsoft · referência oficial", "Windows 10/11 conforme a versão do PowerToys.", "MIT", "Integração separada; o ZEUS não verifica instalação nem configura os layouts. A documentação registra telemetria diagnóstica básica.", "https://learn.microsoft.com/windows/powertoys/workspaces"),
        new("translucenttb", "TranslucentTB · taskbar translúcida", "Aplica transparência, cor e aparência dinâmica à barra de tarefas.", "Terceiro · APIs não documentadas", "Windows 10/11; o ZIP portátil do projeto é somente para Windows 11.", "GPL-3.0", "Pode depender de detalhes internos do shell e mudar após atualizações do Windows. Use a distribuição oficial e revise os termos GPL.", "https://github.com/TranslucentTB/TranslucentTB"),
        new("aero-dock", "Aero Dock · dock e widgets", "Dock independente com prévias de janelas, temas, widgets e atalhos.", "Terceiro · projeto pequeno · sem assinatura no release consultado", "Windows 10 1809+/11 x64; requer WebView2, segundo o projeto.", "MIT", "O projeto informa que não exige elevação. Ainda é recente e unsigned; confira hash e código antes de executar. Não substitui a barra do Windows automaticamente.", "https://github.com/TheAgencyMGE/aero-dock"),
        new("taskbar-widgets", "Taskbar Widgets · widgets na barra", "Adiciona widgets de informação sem substituir o shell do Windows.", "Terceiro · beta · usa superfície privada do Windows", "Somente Windows 11 x64, segundo o projeto.", "MIT", "Uma atualização do Windows pode quebrar a integração; o próprio projeto desativa layouts incompatíveis. Não recomendado como dependência essencial.", "https://github.com/pfcdev/TaskbarWidgets"),
        new("lively", "Lively Wallpaper · fundos animados", "Papéis de parede animados e interativos para a área de trabalho.", "Terceiro · aplicativo separado", "Windows 10 1903+ segundo o instalador do projeto.", "GPL-3.0", "O consumo de GPU, memória e energia varia por conteúdo e configuração. Confira a distribuição oficial e a licença antes de redistribuir.", "https://github.com/lively-community/lively"),
        new("rainmeter", "Rainmeter · widgets avançados", "Widgets de relógio, informações do sistema e visualizadores no desktop.", "Terceiro · aplicativo separado", "Windows 7–11 segundo o projeto; confirme cada skin.", "GPL-2.0", "Skins têm licenças próprias e podem executar scripts ou acessar a rede; revise cada arquivo antes de instalar.", "https://github.com/rainmeter/rainmeter"),
        new("mica-for-everyone", "Mica For Everyone · materiais de janela", "Solicita efeitos Mica/Acrylic em janelas Win32 compatíveis.", "Terceiro · regras por aplicativo", "Windows 11 e somente aplicativos compatíveis, segundo o projeto.", "MIT", "Não é um efeito universal nem API do ZEUS. O projeto alerta que há site falso; use somente GitHub oficial ou Microsoft Store.", "https://github.com/MicaForEveryone/MicaForEveryone")
    ];
    public IReadOnlyList<AccentColorOption> AccentColorOptions { get; } = [new(AppAccentColor.ThemeDefault, "Padrão do tema"), new(AppAccentColor.Blue, "Azul oceano"), new(AppAccentColor.Violet, "Violeta"), new(AppAccentColor.Green, "Verde"), new(AppAccentColor.Rose, "Rosa"), new(AppAccentColor.Amber, "Âmbar")];
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
    public bool CanApplyWallpaper => !_isBusy && !_wallpaperSlideshowDetected && !string.IsNullOrWhiteSpace(SelectedWallpaperPath) &&
        SelectedWallpaperMonitor is not null && (_selectedWallpaperPosition is null || Enum.IsDefined(_selectedWallpaperPosition.Value)) &&
        !(_selectedWallpaperPosition == WallpaperPosition.Span && SelectedWallpaperMonitor?.MonitorId is not null);
    public DesktopOrganizationPreview? DesktopOrganizationPreview { get => _desktopOrganizationPreview; private set { if (Set(ref _desktopOrganizationPreview, value)) Notify(nameof(CanApplyDesktopOrganization)); } }
    public string DesktopOrganizationSummary { get => _desktopOrganizationSummary; private set => Set(ref _desktopOrganizationSummary, value); }
    public bool CanApplyDesktopOrganization => !_isBusy && DesktopOrganizationPreview is { Items.Count: > 0 };
    public bool CanInstallDriver => !_isBusy && DriverCandidates.Count(d => d.IsSelected) == 1 && DriverCandidates.Where(d => d.IsSelected).All(d => d.CanSelectForInstall && d.LicenseReady);
    public bool CanVerifyPendingDriverUpdates => !_isBusy && _historyReadable && _reports.Any(report =>
        report.Steps.Any(step => step.Action == MaintenanceActionId.InstallDriverUpdate &&
            step.Verification == MaintenanceVerificationStatus.Pending && step.UpdateServerSelection is not null));
    private DriverRollbackChoice? _selectedRollbackDriver;
    public DriverRollbackChoice? SelectedRollbackDriver { get => _selectedRollbackDriver; set { if (Set(ref _selectedRollbackDriver, value)) NotifyActionState(); } }
    public bool CanRollbackDriver => !_isBusy && SelectedRollbackDriver is not null && MaintenanceRequestProtocol.TryParsePnpInstanceId(SelectedRollbackDriver.InstanceId);
    public string RollbackDriverSummary { get; private set; } = "Leia o diagnóstico para identificar dispositivos presentes que podem ser selecionados.";
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
    public string DriverInventorySummary { get; private set; } = "Inventário de drivers disponível após a coleta do Windows.";
    public string BoardSupportSummary { get; private set; } = "O fabricante da placa-mãe será identificado após a coleta do Windows.";
    public string? BoardSupportSourceName { get; private set; }
    public bool CanOpenBoardSupport => BoardSupportSourceName is not null;
    public string BoardSupportButtonText => BoardSupportSourceName is { } name ? $"Abrir suporte oficial · {name}" : "Portal oficial não identificado";
    public string WingetSummary { get => _wingetSummary; private set => Set(ref _wingetSummary, value); }
    public string ZeusReleaseSummary { get => _zeusReleaseSummary; private set => Set(ref _zeusReleaseSummary, value); }
    private string _windowsUpdateSummary = "A busca online só começa quando você solicitar. Não baixa nem instala atualizações.";
    public string WindowsUpdateSummary { get => _windowsUpdateSummary; private set => Set(ref _windowsUpdateSummary, value); }
    private string _windowsUpdateHistorySummary = "O histórico local só será consultado quando você solicitar.";
    public string WindowsUpdateHistorySummary { get => _windowsUpdateHistorySummary; private set => Set(ref _windowsUpdateHistorySummary, value); }
    public string PerformanceSummary { get => _performanceSummary; private set => Set(ref _performanceSummary, value); }
    public string PerformanceActivityLabel { get => _performanceActivityLabel; set => Set(ref _performanceActivityLabel, value ?? string.Empty); }
    public string PerformanceSessionHistorySummary { get => _performanceSessionHistorySummary; private set => Set(ref _performanceSessionHistorySummary, value); }
    public string NetworkProbeTarget { get => _networkProbeTarget; set => Set(ref _networkProbeTarget, value); }
    public string NetworkProbeSummary { get => _networkProbeSummary; private set => Set(ref _networkProbeSummary, value); }
    public string CollectionDate => _snapshot is null ? "Leitura pendente" : _snapshot.CollectedAt.ToLocalTime().ToString("dd/MM HH:mm:ss");
    public string SystemDescription => _snapshot is null ? "Inventário local do Windows" : FormatSystemDescription(_snapshot);
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
    public Visibility DetailedVisibility => IsMinimal && !IsTechnicalMode ? Visibility.Collapsed : Visibility.Visible;
    public string LayoutDescription => ThemeOptions.First(t => t.Value == SelectedTheme).Name;
    public ThemeOption SelectedThemeOption => ThemeOptions.First(theme => theme.Value == SelectedTheme);
    public VisualLayoutPreset SelectedVisualLayoutPreset
    {
        get => _selectedVisualLayoutPreset ?? VisualLayoutPresets[0];
        set
        {
            if (value is null || !VisualLayoutPresets.Contains(value) || !Set(ref _selectedVisualLayoutPreset, value)) return;
            if (_customAccentPreviewHex is not null)
            {
                _customAccentPreviewHex = null;
                Notify(nameof(IsCustomAccentPreviewing));
                RefreshCustomAccentValidation();
            }
            Notify(nameof(SelectedVisualLayoutPreview));
            if (_loaded)
            {
                _visualLayoutPreviewActive = value.Theme != SelectedTheme || value.Accent != SelectedAccentColor ||
                    value.Density is { } density && density != SelectedDensity ||
                    value.ReduceZeusMotion is { } reduceMotion && reduceMotion != ReduceZeusMotion;
                _visualLayoutReduceMotionPreview = _visualLayoutPreviewActive ? value.ReduceZeusMotion : null;
                ApplyTheme(value.Theme, value.Accent);
                Notify(nameof(IsVisualLayoutPreviewing));
                Notify(nameof(VisualLayoutPreviewState));
                Notify(nameof(CanEditDensity));
            }
            Notify(nameof(CanConfirmVisualLayout));
        }
    }
    public VisualLayoutPreview SelectedVisualLayoutPreview => CreateVisualLayoutPreview(SelectedVisualLayoutPreset);
    public string SelectedVisualLayoutDensitySummary => SelectedVisualLayoutPreset.Density switch
    {
        DesktopDensity.Compact => "Prévia: espaçamento compacto, sem reduzir o tamanho do texto.",
        DesktopDensity.Comfortable => "Prévia: espaçamento confortável.",
        _ => "Este perfil mantém a densidade selecionada atualmente."
    };
    public string SelectedVisualLayoutMotionSummary => SelectedVisualLayoutPreset.ReduceZeusMotion switch
    {
        true => "Este perfil reduz as transições entre áreas do ZEUS.",
        false => "Este perfil mantém as transições breves do ZEUS quando o Windows permite movimento.",
        _ => "Este perfil mantém a preferência de movimento atual."
    };
    public bool EffectiveReduceZeusMotion => _visualLayoutReduceMotionPreview ?? ReduceZeusMotion;
    public string VisualLayoutCatalogStatus => _visualLayoutCatalogStatus;
    public bool IsVisualLayoutPreviewing => _visualLayoutPreviewActive;
    public bool CanEditDensity => CanChooseActions && !_visualLayoutPreviewActive;
    public string CustomAccentDraftHex
    {
        get => _customAccentDraftHex;
        set
        {
            if (!Set(ref _customAccentDraftHex, value ?? string.Empty)) return;
            RefreshCustomAccentValidation();
        }
    }
    public string CustomAccentValidationSummary => _customAccentValidationSummary;
    public bool IsCustomAccentPreviewing => _customAccentPreviewHex is not null;
    public bool CanPreviewCustomAccent => CanChooseActions && !_visualLayoutPreviewActive && !SystemParameters.HighContrast &&
        TryValidateCustomAccent(CustomAccentDraftHex, SelectedTheme, out var normalized, out _) &&
        !string.Equals(normalized, _customAccentPreviewHex ?? _customAccentHex ?? GetAccentHex(SelectedAccentColor, SelectedTheme), StringComparison.OrdinalIgnoreCase);
    public bool CanConfirmCustomAccent => CanChooseActions && _customAccentPreviewHex is not null &&
        TryValidateCustomAccent(CustomAccentDraftHex, SelectedTheme, out var normalized, out _) &&
        string.Equals(normalized, _customAccentPreviewHex, StringComparison.OrdinalIgnoreCase);
    public bool CanCancelCustomAccentPreview => CanChooseActions && _customAccentPreviewHex is not null;
    public bool CanConfirmVisualLayout => CanChooseActions &&
        (_visualLayoutPreviewActive ||
         (SelectedVisualLayoutPreset.Theme == SelectedTheme && SelectedVisualLayoutPreset.Accent == SelectedAccentColor &&
          (SelectedVisualLayoutPreset.Density is null || SelectedVisualLayoutPreset.Density == SelectedDensity) &&
          (SelectedVisualLayoutPreset.ReduceZeusMotion is null || SelectedVisualLayoutPreset.ReduceZeusMotion == ReduceZeusMotion) &&
          !string.Equals(SelectedVisualLayoutPreset.Id, _savedVisualLayoutPresetId, StringComparison.Ordinal)));
    public string VisualLayoutPreviewState => SystemParameters.HighContrast
        ? "O alto contraste do Windows prevalece; confirme o perfil para salvar ou cancele a prévia."
        : _visualLayoutPreviewActive
            ? "Prévia temporária ativa nesta interface. Tema, espaçamento salvo, preferência de movimento, relógio e Windows permanecem sem alteração até confirmar."
            : CanConfirmVisualLayout
                ? "Este perfil corresponde às opções atuais; confirmar salva sua escolha."
            : "Escolha um perfil para pré-visualizar nesta interface; confirme para salvar ou cancele a prévia.";
    public bool IsMinimal { get => SelectedTheme == DesktopTheme.Minimal; set => SelectedTheme = value ? DesktopTheme.Minimal : DesktopTheme.Complete; }
    public bool IsTechnicalMode { get => _isTechnicalMode; set { if (Set(ref _isTechnicalMode, value)) { Notify(nameof(DetailedVisibility)); QueuePreferencesSave(); } } }
    public bool CheckZeusUpdatesAutomatically
    {
        get => _checkZeusUpdatesAutomatically;
        set
        {
            if (!Set(ref _checkZeusUpdatesAutomatically, value)) return;
            if (!value) _automaticZeusUpdateCheckCancellation?.Cancel();
            QueuePreferencesSave();
        }
    }
    public DesktopTheme SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (!Enum.IsDefined(value) || !Set(ref _selectedTheme, value)) return;
            ClearCustomAccentForThemeChange();
            _visualLayoutPreviewActive = false;
            _visualLayoutReduceMotionPreview = null;
            ApplyTheme(); Notify(nameof(IsVisualLayoutPreviewing)); Notify(nameof(VisualLayoutPreviewState)); Notify(nameof(CanConfirmVisualLayout)); Notify(nameof(CanEditDensity)); Notify(nameof(IsMinimal)); Notify(nameof(DetailedVisibility)); Notify(nameof(LayoutDescription)); Notify(nameof(SelectedThemeOption));
            UpdateAppearanceStatus();
            QueuePreferencesSave();
        }
    }
    public DesktopDensity SelectedDensity
    {
        get => _selectedDensity;
        set
        {
            if (!Enum.IsDefined(value) || !Set(ref _selectedDensity, value)) return;
            ApplyDensity(_visualLayoutPreviewActive ? SelectedVisualLayoutPreset.Density : null);
            QueuePreferencesSave();
            Notify(nameof(CanConfirmVisualLayout));
        }
    }
    public DesktopDensityOption[] DensityOptions { get; } =
    [new("Confortável", DesktopDensity.Comfortable), new("Compacta", DesktopDensity.Compact)];
    public AppAccentColor SelectedAccentColor
    {
        get => _selectedAccentColor;
        set
        {
            if (!Enum.IsDefined(value) || !Set(ref _selectedAccentColor, value)) return;
            ClearCustomAccentForThemeChange();
            _visualLayoutPreviewActive = false;
            _visualLayoutReduceMotionPreview = null;
            ApplyTheme();
            Notify(nameof(IsVisualLayoutPreviewing)); Notify(nameof(VisualLayoutPreviewState)); Notify(nameof(CanConfirmVisualLayout)); Notify(nameof(CanEditDensity));
            UpdateAppearanceStatus();
            QueuePreferencesSave();
        }
    }
    public UsageProfile SelectedProfile { get => _selectedProfile; set { if (Enum.IsDefined(value) && Set(ref _selectedProfile, value)) ProfileChanged(); } }
    public bool ReduceAnimations { get => _reduceAnimations; set { if (Set(ref _reduceAnimations, value)) ProfileChanged(); } }
    public bool ReduceTransparency { get => _reduceTransparency; set { if (Set(ref _reduceTransparency, value)) ProfileChanged(); } }
    public bool ReduceZeusMotion { get => _reduceZeusMotion; set { if (Set(ref _reduceZeusMotion, value)) { QueuePreferencesSave(); Notify(nameof(CanConfirmVisualLayout)); } } }
    public bool DesktopClockEnabled { get => _desktopClockEnabled; set { if (Set(ref _desktopClockEnabled, value)) ClockChanged(); } }
    public bool DesktopClockShowDate { get => _desktopClockShowDate; set { if (Set(ref _desktopClockShowDate, value)) ClockChanged(); } }
    public bool DesktopClockShowSeconds { get => _desktopClockShowSeconds; set { if (Set(ref _desktopClockShowSeconds, value)) ClockChanged(); } }
    public bool DesktopClockUse24HourFormat { get => _desktopClockUse24HourFormat; set { if (Set(ref _desktopClockUse24HourFormat, value)) ClockChanged(); } }
    public bool DesktopClockHideDuringFullscreen { get => _desktopClockHideDuringFullscreen; set { if (Set(ref _desktopClockHideDuringFullscreen, value)) ClockChanged(); } }
    public DesktopClockSize SelectedDesktopClockSize { get => _desktopClockSize; set { if (Enum.IsDefined(value) && Set(ref _desktopClockSize, value)) ClockChanged(); } }
    public DesktopClockSizeOption[] DesktopClockSizeOptions { get; } =
    [new("Pequeno", DesktopClockSize.Compact), new("Médio", DesktopClockSize.Medium), new("Grande", DesktopClockSize.Large)];
    public DesktopClockStyle SelectedDesktopClockStyle { get => _desktopClockStyle; set { if (Enum.IsDefined(value) && Set(ref _desktopClockStyle, value)) ClockChanged(); } }
    public DesktopClockStyleOption[] DesktopClockStyleOptions { get; } =
    [new("Vidro", DesktopClockStyle.Glass), new("Minimalista", DesktopClockStyle.Minimal), new("Neon", DesktopClockStyle.Neon), new("Clássico", DesktopClockStyle.Classic)];
    public bool DesktopClockAlwaysOnTop { get => _desktopClockAlwaysOnTop; set { if (Set(ref _desktopClockAlwaysOnTop, value)) ClockChanged(); } }
    public double DesktopClockOpacity { get => _desktopClockOpacity; set { if (Set(ref _desktopClockOpacity, Math.Clamp(value, 0.45, 1))) ClockChanged(); } }
    public bool IsDesktopClockSettingsPreviewing => _desktopClockSettingsPreviewing;
    public bool CanConfirmDesktopClockSettings => CanChooseActions && _desktopClockSettingsPreviewing;
    public bool CanCancelDesktopClockSettingsPreview => CanConfirmDesktopClockSettings;
    public string DesktopClockSettingsPreviewSummary => _desktopClockSettingsPreviewing
        ? "Prévia temporária ativa. Confirme para salvar as opções do relógio ou cancele para restaurar as preferências salvas."
        : "As opções do relógio só serão salvas depois de confirmar a prévia.";
    public bool FirstRunSetupComplete { get => _firstRunSetupComplete; private set { if (Set(ref _firstRunSetupComplete, value)) Notify(nameof(FirstRunSetupVisibility)); } }
    public Visibility FirstRunSetupVisibility => FirstRunSetupComplete ? Visibility.Collapsed : Visibility.Visible;
    public bool NeedsBluetooth { get => _needsBluetooth; set { if (Set(ref _needsBluetooth, value)) ProfileChanged(); } }
    public bool NeedsPrinting { get => _needsPrinting; set { if (Set(ref _needsPrinting, value)) ProfileChanged(); } }
    public bool NeedsCloudSync { get => _needsCloudSync; set { if (Set(ref _needsCloudSync, value)) ProfileChanged(); } }
    public bool NeedsVirtualization { get => _needsVirtualization; set { if (Set(ref _needsVirtualization, value)) ProfileChanged(); } }
    public bool OfflineRestartConfirmed { get => _offlineRestartConfirmed; set { if (Set(ref _offlineRestartConfirmed, value)) Notify(nameof(CanOfflineScan)); } }
    public bool OfflineRecoveryConfirmed { get => _offlineRecoveryConfirmed; set { if (Set(ref _offlineRecoveryConfirmed, value)) Notify(nameof(CanOfflineScan)); } }
    public PowerPlanInfo? SelectedPowerPlan { get => _selectedPowerPlan; set { if (Set(ref _selectedPowerPlan, value)) Notify(nameof(CanSetPowerPlan)); } }
    public WallpaperMonitorChoice? SelectedWallpaperMonitor { get => _selectedWallpaperMonitor; set { if (Set(ref _selectedWallpaperMonitor, value)) { Notify(nameof(CanApplyWallpaper)); RefreshWallpaperPreviewPresentation(); } } }
    public string? SelectedWallpaperPath { get => _selectedWallpaperPath; private set { if (Set(ref _selectedWallpaperPath, value)) Notify(nameof(CanApplyWallpaper)); } }
    public ImageSource? WallpaperPreview { get => _wallpaperPreview; private set { if (Set(ref _wallpaperPreview, value)) RefreshWallpaperPreviewPresentation(); } }
    public ImageBrush? WallpaperPreviewBrush { get => _wallpaperPreviewBrush; private set => Set(ref _wallpaperPreviewBrush, value); }
    public double WallpaperPreviewWidth { get => _wallpaperPreviewWidth; private set => Set(ref _wallpaperPreviewWidth, value); }
    public double WallpaperPreviewHeight { get => _wallpaperPreviewHeight; private set => Set(ref _wallpaperPreviewHeight, value); }
    public string WallpaperPreviewSummary { get => _wallpaperPreviewSummary; private set => Set(ref _wallpaperPreviewSummary, value); }

    private void RefreshWallpaperPreviewPresentation()
    {
        var targetBounds = GetWallpaperPreviewBounds();
        var sourceAspect = _wallpaperPreview is { Height: > 0 } image ? image.Width / image.Height : 16d / 9d;
        var presentation = WallpaperPreviewPresentation.Create(_selectedWallpaperPosition, _currentWallpaperPosition, targetBounds, sourceAspect);
        WallpaperPreviewWidth = presentation.Width;
        WallpaperPreviewHeight = presentation.Height;
        WallpaperPreviewSummary = targetBounds is { Width: > 0, Height: > 0 }
            ? $"{presentation.Description} Formato do destino: {targetBounds.Width} × {targetBounds.Height}."
            : $"{presentation.Description} Formato estimado 16:9; limites do destino indisponíveis.";
        WallpaperPreviewBrush = _wallpaperPreview is { } source ? presentation.CreateBrush(source) : null;
    }

    private WallpaperMonitorBounds? GetWallpaperPreviewBounds()
    {
        if (_selectedWallpaperMonitor?.Bounds is { Width: > 0, Height: > 0 } selectedBounds) return selectedBounds;
        if (_selectedWallpaperMonitor?.MonitorId is not null) return null;
        var bounds = WallpaperMonitorChoices.Where(choice => choice.MonitorId is not null && choice.Bounds is { Width: > 0, Height: > 0 })
            .Select(choice => choice.Bounds!).ToArray();
        if (bounds.Length == 0) return null;
        var left = bounds.Min(item => item.Left);
        var top = bounds.Min(item => item.Top);
        var right = bounds.Max(item => item.Right);
        var bottom = bounds.Max(item => item.Bottom);
        return right > left && bottom > top ? new WallpaperMonitorBounds(left, top, right, bottom) : null;
    }

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
                SelectedDensity = Enum.IsDefined(p.Density) ? p.Density : DesktopDensity.Comfortable;
                SelectedAccentColor = Enum.IsDefined(p.AccentColor) ? p.AccentColor : AppAccentColor.ThemeDefault;
                _customVisualLayoutsJson = p.CustomVisualLayoutsJson;
                if (!string.IsNullOrWhiteSpace(_customVisualLayoutsJson))
                {
                    try
                    {
                        _customVisualLayoutPresets.AddRange(VisualLayoutCatalog.ParseCustom(_customVisualLayoutsJson, _visualLayoutPresets.ToArray()));
                        foreach (var preset in _customVisualLayoutPresets) _visualLayoutPresets.Add(preset);
                    }
                    catch (JsonException)
                    {
                        _visualLayoutCatalogStatus = "Um catálogo personalizado salvo não passou na validação e foi preservado. Importe um arquivo válido para substituí-lo.";
                        Notify(nameof(VisualLayoutCatalogStatus));
                    }
                }
                _customAccentHex = p.CustomAccentHex is { } customHex && TryValidateCustomAccent(customHex, p.IsMinimal ? DesktopTheme.Minimal : p.Theme, out var normalizedCustomHex, out _)
                    ? normalizedCustomHex
                    : null;
                if (_customAccentHex is not null) _customAccentDraftHex = _customAccentHex;
                SelectedVisualLayoutPreset = VisualLayoutPresets.FirstOrDefault(preset =>
                    string.Equals(preset.Id, p.VisualLayoutPresetId, StringComparison.Ordinal)) ?? VisualLayoutPresets[0];
                _savedVisualLayoutPresetId = p.VisualLayoutPresetId;
                SelectedProfile = p.Profile; ReduceAnimations = p.ReduceAnimations; ReduceTransparency = p.ReduceTransparency; ReduceZeusMotion = p.ReduceZeusMotion;
                IsTechnicalMode = p.IsTechnicalMode;
                _checkZeusUpdatesAutomatically = p.CheckZeusUpdatesAutomatically;
                _lastZeusUpdateCheckUtc = p.LastZeusUpdateCheckUtc;
                Notify(nameof(CheckZeusUpdatesAutomatically));
                FirstRunSetupComplete = p.FirstRunSetupComplete;
                NeedsBluetooth = p.NeedsBluetooth; NeedsPrinting = p.NeedsPrinting; NeedsCloudSync = p.NeedsCloudSync; NeedsVirtualization = p.NeedsVirtualization;
                var clock = p.Clock ?? new();
                _desktopClockEnabled = clock.Enabled; _desktopClockShowDate = clock.ShowDate; _desktopClockShowSeconds = clock.ShowSeconds;
                _desktopClockUse24HourFormat = clock.Use24HourFormat;
                _desktopClockHideDuringFullscreen = clock.HideDuringFullscreen;
                _desktopClockAlwaysOnTop = clock.AlwaysOnTop; _desktopClockOpacity = Math.Clamp(clock.Opacity, 0.45, 1);
                _desktopClockSize = ResolveClockSize(clock.Size);
                _desktopClockStyle = ResolveClockStyle(clock.Style);
                _desktopClockLeft = clock.Left; _desktopClockTop = clock.Top;
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
            await RefreshWallpaperMonitorChoicesAsync();
            try { await RefreshDesktopOrganizationSessionsAsync(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
            { _startupWarnings.Add("O histórico de organização da Área de Trabalho não pôde ser lido; nenhum arquivo foi alterado."); }
            ApplyTheme();
            _loaded = true;
            SyncDesktopClock();
            _savedClockPreferences = CaptureDesktopClockPreferences();
            RefreshDesktopClockPreviewState();
            QueueActivity(new(DateTimeOffset.UtcNow, "application", "started", "info", "ZEUS iniciado."));
        }
        finally { SetBusy(false); }
        await RefreshDiagnosticsAsync();
        if (ShouldRunAutomaticZeusUpdateCheck(CheckZeusUpdatesAutomatically, _lastZeusUpdateCheckUtc, DateTimeOffset.UtcNow))
            _ = CheckZeusUpdatesAutomaticallyAsync();
    }

    private void HardwareCardsList_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;
        if (!double.IsFinite(width) || width <= 0) return;
        HardwareCardColumns = ResolveHardwareCardColumns(width);
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
        var boardManufacturer = snapshot.Board?.Manufacturer;
        var boardSupportSource = DriverSupportCatalog.Find(boardManufacturer);
        BoardSupportSourceName = boardSupportSource?.Name;
        BoardSupportSummary = string.IsNullOrWhiteSpace(boardManufacturer)
            ? "Indisponível: o fabricante da placa-mãe não foi identificado nesta coleta."
            : boardSupportSource is null
                ? $"Fabricante reportado: {boardManufacturer}. Não há um portal oficial mapeado; confira o fabricante manualmente."
                : $"Fabricante reportado: {boardManufacturer}. O portal exige que você confirme o modelo exato; o link não identifica nem confirma um driver compatível.";
        Notify(nameof(BoardSupportSummary));
        Notify(nameof(BoardSupportSourceName));
        Notify(nameof(CanOpenBoardSupport));
        Notify(nameof(BoardSupportButtonText));
        InstalledDriverRows.Clear();
        var driverInventory = snapshot.WindowsInventory;
        if (driverInventory is null)
            DriverInventorySummary = "Indisponível: o inventário detalhado do Windows não foi obtido nesta coleta.";
        else if (driverInventory.Warnings.Any(warning => warning.StartsWith("Drivers:", StringComparison.OrdinalIgnoreCase)))
            DriverInventorySummary = "Indisponível: a fonte Win32_PnPSignedDriver não respondeu. Consulte os avisos e atualize o diagnóstico.";
        else
        {
            var orderedDrivers = driverInventory.Drivers.OrderBy(driver => driver.Device, StringComparer.OrdinalIgnoreCase).ToArray();
            var shownDrivers = orderedDrivers.Take(100).ToArray();
            foreach (var driver in shownDrivers)
            {
                var signature = driver.IsSigned switch { true => "Sim (reportado pelo Windows)", false => "Não (reportado pelo Windows)", _ => "Indisponível" };
                var source = DriverSupportCatalog.FindForDevice(driver.Manufacturer, driver.Provider);
                InstalledDriverRows.Add(new(Available(driver.Device),
                    $"Fabricante do dispositivo: {Available(driver.Manufacturer)} · Fornecedor do driver: {Available(driver.Provider)} · Versão: {Available(driver.Version)} · Data: {Available(driver.Date)}\nAssinatura reportada: {signature} · Signatário informado: {Available(driver.Signer)}",
                    source?.Name));
            }
            var signed = orderedDrivers.Count(driver => driver.IsSigned == true);
            var unsigned = orderedDrivers.Count(driver => driver.IsSigned == false);
            var unknown = orderedDrivers.Length - signed - unsigned;
            DriverInventorySummary = $"{orderedDrivers.Length} driver(s) retornado(s) por Win32_PnPSignedDriver · assinatura reportada: {signed} sim, {unsigned} não, {unknown} indisponível" +
                (orderedDrivers.Length > shownDrivers.Length ? $" · exibindo {shownDrivers.Length}; a lista completa está no relatório JSON" : string.Empty) +
                ". Esse campo é o valor informado pelo Windows; não é uma verificação independente da cadeia de confiança ou do arquivo instalado.";
        }
        Notify(nameof(DriverInventorySummary));
        RollbackDriverChoices.Clear();
        var pnpInventory = snapshot.WindowsInventory?.PnpDevices;
        if (pnpInventory is null) RollbackDriverSummary = "Inventário PnP indisponível; não é possível identificar alvos para reversão.";
        else
        {
            foreach (var device in pnpInventory.Where(device => device.IsPresent == true && MaintenanceRequestProtocol.TryParsePnpInstanceId(device.InstanceId)))
                RollbackDriverChoices.Add(new(device));
            var presenceUnknown = pnpInventory.Any(device => device.IsPresent is null);
            RollbackDriverSummary = RollbackDriverChoices.Count > 0
                ? $"{RollbackDriverChoices.Count} dispositivo(s) identificado(s) como presente(s) nesta coleta. A reversão exige uma cópia anterior mantida pelo Windows; dados desatualizados serão revalidados pelo auxiliar."
                : presenceUnknown ? "A fonte de presença está indisponível ou incompleta; nenhum dispositivo foi liberado para reversão. Atualize o diagnóstico."
                : "Nenhum dispositivo presente com identidade válida foi retornado nesta coleta.";
        }
        SelectedRollbackDriver = RollbackDriverChoices.FirstOrDefault();
        Notify(nameof(RollbackDriverSummary));
        HardwareCards.Clear();
        var cpu = snapshot.Cpu;
        HardwareCards.Add(new("Processador", cpu?.Name ?? "Indisponível", cpu is null ? "O Windows não retornou esta leitura." : $"{cpu.PhysicalCores} núcleos · {cpu.LogicalProcessors} processadores lógicos"));
        var memory = snapshot.Memory;
        HardwareCards.Add(new("Memória RAM", memory is null ? "Indisponível" : ByteFormatting.Format(memory.TotalBytes), memory is null ? "Leitura indisponível." : $"{ByteFormatting.Format(memory.AvailableBytes)} disponíveis nesta leitura"));
        HardwareCards.Add(CreateGraphicsOverviewCard(snapshot.Graphics));
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
            var eventReport = EventPatternAnalyzer.AnalyzeInventory(inventory);
            foreach (var finding in eventReport.Findings.Take(20)) EventDiagnosticRows.Add(new(finding.Title, finding.Detail));
            EventDiagnosticSummary = eventReport.Findings.Count > 20
                ? $"{eventReport.Summary} Exibindo os primeiros 20 achados."
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
        DiskRows.Clear(); foreach (var disk in snapshot.Disks) DiskRows.Add(new($"{disk.DriveLetter} · {Available(disk.Name)}", $"{ByteFormatting.Format(disk.FreeBytes)} livres de {ByteFormatting.Format(disk.TotalBytes)} · {Available(disk.FileSystem)} · {Available(disk.VolumeType)} · Discos físicos: {FormatPhysicalDiskNumbers(disk.PhysicalDiskNumbers)}"));
        StartupRows.Clear(); foreach (var entry in snapshot.Startup) StartupRows.Add(new(Available(entry.Name), $"Origem: {Available(entry.Location)} · Usuário: {Available(entry.User)}"));
        ExtendedHardwareRows.Clear();
        if (snapshot.Board is { } board) ExtendedHardwareRows.Add(new("Placa-mãe", $"{Available(board.Manufacturer)} · {Available(board.Product)}"));
        if (snapshot.Bios is { } bios) ExtendedHardwareRows.Add(new("BIOS / UEFI", $"{Available(bios.Manufacturer)} · {Available(bios.Version)} · {Available(bios.ReleaseDate)}"));
        var memoryModules = snapshot.MemoryModules;
        if (memoryModules is not null || snapshot.MemoryArraySlotsReported is not null)
        {
            var moduleCount = memoryModules?.Count.ToString() ?? "indisponível";
            var slots = snapshot.MemoryArraySlotsReported?.ToString() ?? "não informado pelo firmware";
            ExtendedHardwareRows.Add(new("RAM · Slots e canais", $"{moduleCount} módulo(s) reportado(s) · {slots} slot(s) declarados · canais: não certificados; etiquetas e interleave SMBIOS são exibidos quando fornecidos, sem inferir dual channel pela velocidade."));
        }
        foreach (var module in snapshot.MemoryModules ?? []) ExtendedHardwareRows.Add(new($"RAM · {Available(module.Location)}", $"{ByteFormatting.Format(module.CapacityBytes)} · {module.SpeedMHz?.ToString() ?? "Indisponível"} MHz · {Available(module.Manufacturer)}\n{FormatMemoryInterleave(module)}"));
        foreach (var disk in snapshot.PhysicalDisks ?? []) ExtendedHardwareRows.Add(new(Available(disk.Name), $"Disco físico: {(disk.DiskNumber is { } number ? $"#{number}" : "número indisponível")} · {disk.MediaType} · {disk.BusType} · {ByteFormatting.Format(disk.SizeBytes)} · Estado informado: {Available(disk.HealthStatus)}\nTemperatura informada: {(disk.TemperatureCelsius.HasValue ? $"{disk.TemperatureCelsius.Value:0.#} °C" : "indisponível")} (máx. {(disk.TemperatureMaxCelsius.HasValue ? $"{disk.TemperatureMaxCelsius.Value:0.#} °C" : "indisponível")}) · Desgaste informado: {disk.Wear?.ToString() ?? "indisponível"}\nHoras ligado: {disk.PowerOnHours?.ToString() ?? "indisponível"} · Erros leitura: {FormatDiskErrors(disk.ReadErrorsTotal, disk.ReadErrorsUncorrected)} · Erros gravação: {FormatDiskErrors(disk.WriteErrorsTotal, disk.WriteErrorsUncorrected)}"));
        foreach (var battery in snapshot.Batteries ?? []) ExtendedHardwareRows.Add(new(Available(battery.Name), $"Carga: {battery.ChargePercent?.ToString() ?? "indisponível"}% · {Available(battery.Status)}"));
        foreach (var network in snapshot.NetworkAdapters ?? []) ExtendedHardwareRows.Add(new(Available(network.Name), $"{Available(network.Status)} · Velocidade de enlace: {(network.SpeedBitsPerSecond.HasValue ? $"{network.SpeedBitsPerSecond.Value / 1_000_000d:0.#} Mbps" : "indisponível")}"));
        if (inventory is not null)
        {
            foreach (var network in inventory.NetworkConfiguration)
                ExtendedHardwareRows.Add(new($"Rede · {Available(network.Adapter)}", $"Estado: {Available(network.Status)} · IP: {FormatNetworkValues(network.Addresses)} · DNS: {FormatNetworkValues(network.DnsServers)} · Gateway: {FormatNetworkValues(network.Gateways)} · Rotas: {FormatNetworkValues(network.Routes)}"));
            if (inventory.NetworkConfiguration.Count == 0)
                ExtendedHardwareRows.Add(new("Rede · configuração IP/DNS/rotas", "O provedor de configuração não retornou interfaces nesta coleta. IP, DNS, gateway e rotas estão indisponíveis; isso não confirma ausência de adaptadores."));
            ExtendedHardwareRows.Add(new("Proxy do usuário (HKCU)", FormatProxyConfiguration(inventory.ProxyConfiguration)));
            ExtendedHardwareRows.Add(new("Proxy WinHTTP padrão", FormatWinHttpProxyConfiguration(inventory.WinHttpProxyConfiguration)));
            ExtendedHardwareRows.Add(new("Modo de inicialização firmware", FormatFirmwareBoot(inventory.FirmwareBoot)));
            ExtendedHardwareRows.Add(new("Sistema operacional", FormatWindowsVersion(snapshot.WindowsVersion, snapshot.OperatingSystem)));
            ExtendedHardwareRows.Add(new("Inicialização segura", inventory.SecurityState?.SecureBootEnabled is { } secureBoot ? (secureBoot ? "Ativada" : "Desativada") : "Indisponível"));
            ExtendedHardwareRows.Add(new("TPM", inventory.SecurityState?.TpmPresent is { } tpm ? (tpm ? $"Presente · {(inventory.SecurityState.TpmReady == true ? "pronto" : inventory.SecurityState.TpmReady == false ? "não pronto" : "estado indisponível")}" : "Não detectado") : "Indisponível"));
            ExtendedHardwareRows.Add(new("Reinicialização pendente", FormatRestartState(WindowsRestartStateParser.Evaluate(inventory.RestartIndicators))));
            var processInventoryLimited = inventory.Warnings.Any(warning =>
                warning.StartsWith("Processos: amostra limitada aos 200", StringComparison.OrdinalIgnoreCase));
            var processInventoryUnavailable = inventory.Warnings.Any(warning =>
                warning.StartsWith("Processos: fonte indisponível", StringComparison.OrdinalIgnoreCase));
            var processSummary = processInventoryUnavailable
                ? "Fonte indisponível nesta coleta; inventário de processos desconhecido."
                : processInventoryLimited
                    ? "Amostra incompleta: até 200 processos com maior memória de trabalho; outros podem estar fora."
                    : $"{inventory.Processes.Count} processos retornados pela fonte consultada.";
            ExtendedHardwareRows.Add(new("Processos do Windows", processSummary));
            var routeCount = inventory.NetworkConfiguration.Sum(network => network.Routes?.Length ?? 0);
            var routesLimited = inventory.Warnings.Any(warning =>
                warning.StartsWith("Rotas de rede: amostra limitada a 300", StringComparison.OrdinalIgnoreCase));
            var routesUnavailable = inventory.Warnings.Any(warning =>
                warning.StartsWith("Rotas de rede: fonte indisponível", StringComparison.OrdinalIgnoreCase));
            var routeSummary = routesUnavailable
                ? "Fonte de rotas indisponível nesta coleta; cobertura desconhecida."
                : routesLimited
                    ? $"Amostra incompleta: limite de 300 rotas; {routeCount} rotas foram associadas às interfaces retornadas."
                    : $"{routeCount} rotas associadas às interfaces retornadas pela fonte consultada.";
            ExtendedHardwareRows.Add(new("Rotas de rede", routeSummary));
            var scheduledTasks = inventory.ScheduledTasks
                .OrderBy(task => task.Path, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(task => task.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
            var scheduledTasksTruncated = inventory.Warnings.Any(warning =>
                warning.StartsWith("Tarefas agendadas: amostra limitada a 500", StringComparison.OrdinalIgnoreCase));
            var scheduledTasksSummary = scheduledTasksTruncated
                ? $"A coleta atingiu o limite de 500 entradas; há pelo menos {scheduledTasks.Length} tarefas e a amostra está incompleta. Até 30 são exibidas abaixo."
                : $"{scheduledTasks.Length} entradas inventariadas; até 30 são exibidas abaixo.";
            ExtendedHardwareRows.Add(new("Tarefas agendadas", scheduledTasksSummary + " Nenhuma foi alterada."));
            foreach (var task in scheduledTasks.Take(30))
                ExtendedHardwareRows.Add(new($"Tarefa · {Available(task.Name)}", $"Pasta: {Available(task.Path)} · Estado reportado: {Available(task.State)}\n{ScheduledTaskDiagnostics.Describe(task)}"));
            if (scheduledTasks.Length > 30)
                ExtendedHardwareRows.Add(new("Tarefas agendadas · restante", $"Mais {scheduledTasks.Length - 30} entradas permanecem no relatório completo."));
            var services = inventory.Services
                .OrderBy(service => service.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(service => service.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
            ExtendedHardwareRows.Add(new("Serviços", $"{services.Length} entradas inventariadas; até 30 são exibidas abaixo. Nenhum foi iniciado ou parado."));
            foreach (var service in services.Take(30))
                ExtendedHardwareRows.Add(new($"Serviço · {Available(service.DisplayName)}", $"Nome: {Available(service.Name)} · Estado: {Available(service.Status)} · Inicialização: {Available(service.StartType)}"));
            if (services.Length > 30)
                ExtendedHardwareRows.Add(new("Serviços · restante", $"Mais {services.Length - 30} entradas permanecem no relatório completo."));
            var software = inventory.InstalledSoftware.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
            ExtendedHardwareRows.Add(new("Programas instalados", $"{software.Length} entradas Win32/Appx-MSIX; a fonte Winget é consultada separadamente e nada foi instalado."));
            foreach (var item in software.Take(30))
                ExtendedHardwareRows.Add(new($"Programa · {Available(item.Name)}", $"Versão: {Available(item.Version)} · Publicador: {Available(item.Publisher)} · Origem: {Available(item.Source)}"));
            if (software.Length > 30)
                ExtendedHardwareRows.Add(new("Programas instalados · restante", $"Mais {software.Length - 30} entradas permanecem no relatório completo."));
            ExtendedHardwareRows.Add(new("Windows Update", inventory.UpdateState?.PendingCount is { } pending ? $"{pending} atualização(ões) pendente(s)" : "Atualizações pendentes não consultadas nesta leitura."));
            ExtendedHardwareRows.Add(new("Integridade da imagem do Windows", string.IsNullOrWhiteSpace(inventory.WindowsImageHealth)
                ? "Não verificada nesta coleta. Use o Centro de Reparos para uma verificação explícita."
                : inventory.WindowsImageHealth));
            foreach (var device in inventory.PnpDevices.Where(device => !string.IsNullOrWhiteSpace(device.ProblemCode)).Take(20))
                ExtendedHardwareRows.Add(new($"Dispositivo com código {device.ProblemCode}", $"{Available(device.Name)} · {Available(device.Status)}"));
        }
        else
        {
            ExtendedHardwareRows.Add(new("Rede · configuração IP/DNS/rotas", "Inventário do Windows indisponível nesta coleta; IP, DNS, gateway e rotas não foram verificados."));
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

    private OptimizationWorkloadEvidence? ToWorkloadEvidence(PerformanceObservation? observation)
    {
        if (observation is not { } sample) return null;
        double? availableMemoryPercent = sample.TotalMemoryBytes > 0 && sample.AvailableMemoryBytes <= sample.TotalMemoryBytes
            ? (double)sample.AvailableMemoryBytes / sample.TotalMemoryBytes * 100
            : null;
        var currentSession = _performanceHistory.Snapshot()
            .Where(entry => entry.SessionId == _performanceSessionId)
            .Select(entry => entry.Observation)
            .ToArray();
        var gpuAssessments = GpuMemoryOccupancyAnalyzer.Assess(currentSession);
        bool? sustainedGpuOccupancy = gpuAssessments.Any(assessment => assessment.State == GpuMemoryOccupancyState.SustainedHighOccupancy)
            ? true
            : gpuAssessments.Count > 0 && gpuAssessments.All(assessment => assessment.State == GpuMemoryOccupancyState.NoSustainedHighOccupancy)
                ? false
                : null;
        var evidenceAssessments = gpuAssessments.Any(assessment => assessment.State == GpuMemoryOccupancyState.SustainedHighOccupancy)
            ? gpuAssessments.Where(assessment => assessment.State == GpuMemoryOccupancyState.SustainedHighOccupancy).ToArray()
            : gpuAssessments.ToArray();
        int? validGpuSamples = evidenceAssessments.Length > 0 ? evidenceAssessments.Min(assessment => assessment.ValidSamples) : null;
        double? gpuWindowSeconds = evidenceAssessments.Length > 0 ? evidenceAssessments.Min(assessment => assessment.Window.TotalSeconds) : null;
        var memoryAssessment = MemoryPressureAnalyzer.Assess(currentSession);
        bool? sustainedMemorySignal = memoryAssessment.State switch
        {
            MemoryPressureSignalState.SustainedLowMemoryWithPageReads => true,
            MemoryPressureSignalState.NoSustainedCombinedSignal => false,
            _ => null
        };
        return new(sample.CpuPercent, availableMemoryPercent,
            sample.ActivityContext?.KnownGameProcessDetected,
            sample.ActivityContext?.ObsProcessDetected,
            sustainedGpuOccupancy,
            validGpuSamples,
            gpuWindowSeconds,
            sustainedMemorySignal,
            memoryAssessment.ValidSamples,
            memoryAssessment.Window.TotalSeconds);
    }

    private void ShowPendingHardware()
    {
        foreach (var title in new[] { "Processador", "Memória RAM", "Placas de vídeo", "Armazenamento", "Microsoft Defender", "Placa-mãe" }) HardwareCards.Add(new(title, "Aguardando leitura", "Dados locais do Windows"));
    }
    private void ProfileChanged() { BuildPersonalPlan(); NotifyActionState(); QueuePreferencesSave(); }
    private DesktopPreferences CurrentPreferences() => new(IsMinimal, SelectedTheme, SelectedProfile, ReduceAnimations, ReduceTransparency, NeedsBluetooth, NeedsPrinting, NeedsCloudSync, NeedsVirtualization, FirstRunSetupComplete, IsTechnicalMode,
        _desktopClockSettingsPreviewing ? _savedClockPreferences : CaptureDesktopClockPreferences(), SelectedAccentColor, SelectedVisualLayoutPreset.Id, _customVisualLayoutsJson, _customAccentHex,
        CheckZeusUpdatesAutomatically, _lastZeusUpdateCheckUtc, ReduceZeusMotion, SelectedDensity);
    internal static DesktopClockSize ResolveClockSize(DesktopClockSize? savedSize) =>
        savedSize is { } size && Enum.IsDefined(size) ? size : DesktopClockSize.Medium;
    internal static DesktopClockStyle ResolveClockStyle(DesktopClockStyle savedStyle) =>
        Enum.IsDefined(savedStyle) ? savedStyle : DesktopClockStyle.Glass;
    internal static Point ResolveInitialClockPosition(double left, double top, double width, double height, Rect virtualScreen) =>
        DesktopClockWindow.ClampPosition(left, top, width, height, virtualScreen);
    private DesktopClockPreferences CaptureDesktopClockPreferences() => new(DesktopClockEnabled, DesktopClockShowDate, DesktopClockShowSeconds,
        DesktopClockAlwaysOnTop, DesktopClockOpacity, _desktopClock?.Left ?? _desktopClockLeft, _desktopClock?.Top ?? _desktopClockTop,
        SelectedDesktopClockSize, DesktopClockHideDuringFullscreen) { Use24HourFormat = DesktopClockUse24HourFormat, Style = SelectedDesktopClockStyle };

    private void ClockChanged()
    {
        if (!_loaded) return;
        SyncDesktopClock();
        RefreshDesktopClockPreviewState();
    }

    private void RefreshDesktopClockPreviewState()
    {
        _desktopClockSettingsPreviewing = CaptureDesktopClockPreferences() != _savedClockPreferences;
        Notify(nameof(IsDesktopClockSettingsPreviewing));
        Notify(nameof(CanConfirmDesktopClockSettings));
        Notify(nameof(CanCancelDesktopClockSettingsPreview));
        Notify(nameof(DesktopClockSettingsPreviewSummary));
    }
    private void SyncDesktopClock(bool? highContrastOverride = null)
    {
        if (!DesktopClockEnabled) { _desktopClock?.Close(); _desktopClock = null; return; }
        if (_desktopClock is null)
        {
            _desktopClock = new DesktopClockWindow(() =>
            {
                if (_desktopClock is null) return;
                _desktopClockLeft = _desktopClock.Left; _desktopClockTop = _desktopClock.Top; RefreshDesktopClockPreviewState();
            });
            _desktopClock.Closed += (_, _) =>
            {
                if (DesktopClockEnabled && !_isClosing)
                {
                    _desktopClockEnabled = false;
                    Notify(nameof(DesktopClockEnabled));
                    RefreshDesktopClockPreviewState();
                }
                _desktopClock = null;
            };
            var virtualScreen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            var position = ResolveInitialClockPosition(_desktopClockLeft, _desktopClockTop, 220, 90, virtualScreen);
            _desktopClock.Left = position.X;
            _desktopClock.Top = position.Y;
            _desktopClock.Show();
        }
        var accentBrush = Application.Current.Resources["AccentBrush"] as Brush ?? SystemColors.WindowTextBrush;
        _desktopClock.Configure(DesktopClockShowDate, DesktopClockShowSeconds, DesktopClockUse24HourFormat, DesktopClockAlwaysOnTop, DesktopClockOpacity, SelectedDesktopClockSize, SelectedDesktopClockStyle, accentBrush, highContrastOverride ?? SystemParameters.HighContrast, DesktopClockHideDuringFullscreen);
    }
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

    private void ApplyTheme(DesktopTheme? previewTheme = null, AppAccentColor? previewAccent = null, string? customAccentOverride = null)
    {
        ApplyDensity(_visualLayoutPreviewActive ? SelectedVisualLayoutPreset.Density : null);
        if (SystemParameters.HighContrast) return;
        var theme = previewTheme ?? SelectedTheme;
        var colors = GetThemePalette(theme, previewAccent ?? SelectedAccentColor);
        var customAccent = previewAccent is null && previewTheme is null
            ? customAccentOverride ?? _customAccentPreviewHex ?? _customAccentHex
            : customAccentOverride;
        if (customAccent is not null) colors[5] = customAccent;
        var keys = new[] { "BackgroundBrush", "PanelBrush", "BorderBrush", "TextBrush", "MutedBrush", "AccentBrush", "ButtonBrush", "SelectedTabBrush", "LogBackgroundBrush" };
        for (var i = 0; i < keys.Length; i++) Application.Current.Resources[keys[i]] = BrushFromHex(colors[i]);
        Application.Current.Resources["PrimaryButtonBrush"] = Application.Current.Resources["AccentBrush"];
        Application.Current.Resources["SelectedTabTextBrush"] = Application.Current.Resources["AccentBrush"];
        Application.Current.Resources["ButtonTextBrush"] = Application.Current.Resources["TextBrush"];
        if (theme == DesktopTheme.Light)
        {
            Application.Current.Resources["WarningBrush"] = BrushFromHex("#805400");
            Application.Current.Resources["WarningPanelBrush"] = BrushFromHex("#FFF6DF");
            Application.Current.Resources["WarningBorderBrush"] = BrushFromHex("#D5B66D");
            Application.Current.Resources["LogTextBrush"] = BrushFromHex("#263648");
            Application.Current.Resources["PrimaryTextBrush"] = BrushFromHex("#FFFFFF");
        }
        else
        {
            Application.Current.Resources["WarningBrush"] = BrushFromHex("#FFD18B");
            Application.Current.Resources["WarningPanelBrush"] = BrushFromHex("#2B251B");
            Application.Current.Resources["WarningBorderBrush"] = BrushFromHex("#6A532F");
            Application.Current.Resources["LogTextBrush"] = BrushFromHex("#C8D8E8");
            Application.Current.Resources["PrimaryTextBrush"] = BrushFromHex("#071623");
        }
        if (previewTheme is null && _loaded && DesktopClockEnabled) SyncDesktopClock();
    }

    private void ApplyDensity(DesktopDensity? overrideDensity = null)
    {
        if (Application.Current is null) return;
        var compact = (overrideDensity ?? SelectedDensity) == DesktopDensity.Compact;
        Application.Current.Resources["CardContentPadding"] = compact ? new Thickness(14) : new Thickness(20);
        Application.Current.Resources["ButtonContentPadding"] = compact ? new Thickness(12, 7, 12, 7) : new Thickness(17, 10, 17, 10);
        Application.Current.Resources["NavigationItemPadding"] = compact ? new Thickness(11, 9, 11, 9) : new Thickness(15, 13, 15, 13);
        Application.Current.Resources["NavigationItemSpacing"] = compact ? new Thickness(0, 0, 0, 4) : new Thickness(0, 0, 0, 8);
        Application.Current.Resources["InputContentPadding"] = compact ? new Thickness(8) : new Thickness(12);
        Application.Current.Resources["CardCornerRadius"] = compact ? new CornerRadius(8) : new CornerRadius(12);
    }

    private static string[] GetThemePalette(DesktopTheme theme, AppAccentColor accent)
    {
        var colors = theme == DesktopTheme.MacInspired
            ? new[] { "#151625", "#202235", "#3C3E58", "#F5F4FC", "#CBCBDF", "#C5B4FF", "#303248", "#37304F", "#0D0D18" }
            : theme == DesktopTheme.Minimal
                ? new[] { "#101216", "#191D22", "#3B424A", "#F5F7FA", "#BEC6D1", "#BFE7D7", "#282F37", "#293C35", "#0D1013" }
                : theme == DesktopTheme.Light
                    ? new[] { "#F3F6FA", "#FFFFFF", "#D8E0EA", "#17212E", "#4B5A6B", "#176B87", "#EFF4F8", "#E7F1F5", "#F6F8FB" }
                    : theme == DesktopTheme.GamingNeon
                        ? new[] { "#090D16", "#111A2B", "#293650", "#EEF4FF", "#ABB8CD", "#D6FF5F", "#1B2A3E", "#1B2A26", "#070B12" }
                        : theme == DesktopTheme.Cyberpunk
                            ? new[] { "#100B1A", "#1A1230", "#3D2C58", "#F7F1FF", "#C4B5D5", "#FF63D8", "#2E1A43", "#291A39", "#0A0711" }
                            : theme == DesktopTheme.RetroAmber
                                ? new[] { "#171109", "#241A0D", "#51402A", "#FFF1D6", "#D6BA8C", "#FFC857", "#38280E", "#3A2A12", "#100C07" }
                                : theme == DesktopTheme.Monochrome
                                    ? new[] { "#101010", "#1B1B1B", "#414141", "#F4F4F4", "#C4C4C4", "#D9D9D9", "#252525", "#323232", "#090909" }
                                    : new[] { "#0A1120", "#131F32", "#2B3F59", "#F0F5FA", "#B1C1D5", "#65E3E0", "#1D3049", "#1A3546", "#080F1B" };
        colors[5] = GetAccentHex(accent, theme);
        return colors;
    }

    private static VisualLayoutPreview CreateVisualLayoutPreview(VisualLayoutPreset preset)
    {
        if (SystemParameters.HighContrast)
            return new(preset.Name, preset.Description, SystemColors.WindowBrush, SystemColors.ControlBrush,
                SystemColors.WindowFrameBrush, SystemColors.WindowTextBrush, SystemColors.GrayTextBrush, SystemColors.HighlightBrush);

        var colors = GetThemePalette(preset.Theme, preset.Accent);
        return new(preset.Name, preset.Description, BrushFromHex(colors[0]), BrushFromHex(colors[1]),
            BrushFromHex(colors[2]), BrushFromHex(colors[3]), BrushFromHex(colors[4]), BrushFromHex(colors[5]));
    }

    private static SolidColorBrush BrushFromHex(string value) =>
        new((Color)ColorConverter.ConvertFromString(value));

    private void UpdateAppearanceStatus()
    {
        if (!_loaded) return;
        StatusTitle = "Aparência do ZEUS atualizada";
        StatusDetail = $"{SelectedThemeOption.Name} e a cor de destaque escolhida afetam somente a interface do ZEUS. Papel de parede, relógio, animações e configurações do Windows permanecem independentes.";
    }

    internal void RefreshSelectedThemeAfterContrastChange()
    {
        if (SystemParameters.HighContrast && _customAccentPreviewHex is not null)
        {
            _customAccentPreviewHex = null;
            Notify(nameof(IsCustomAccentPreviewing));
        }
        ApplyTheme();
        RefreshCustomAccentValidation();
        Notify(nameof(SelectedVisualLayoutPreview));
    }
    internal void RefreshDesktopClockAppearance(bool highContrast) { if (DesktopClockEnabled) SyncDesktopClock(highContrast); }

    private void WorkspaceTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, WorkspaceTabs)) return;
        if (WorkspaceTabs.SelectedContent is not FrameworkElement content) return;
        if (EffectiveReduceZeusMotion || ReduceAnimations || SystemParameters.HighContrast || !SystemParameters.ClientAreaAnimation)
        {
            content.BeginAnimation(UIElement.OpacityProperty, null);
            return;
        }

        content.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)) { FillBehavior = FillBehavior.Stop },
            HandoffBehavior.SnapshotAndReplace);
    }

    private static string GetAccentHex(AppAccentColor accent, DesktopTheme theme) => (accent, theme == DesktopTheme.Light) switch
    {
        (AppAccentColor.Blue, true) => "#1D4ED8",
        (AppAccentColor.Blue, false) => "#60A5FA",
        (AppAccentColor.Violet, true) => "#6D28D9",
        (AppAccentColor.Violet, false) => "#C4B5FD",
        (AppAccentColor.Green, true) => "#226B45",
        (AppAccentColor.Green, false) => "#86EFAC",
        (AppAccentColor.Rose, true) => "#A02F55",
        (AppAccentColor.Rose, false) => "#FDA4AF",
        (AppAccentColor.Amber, true) => "#8A4B00",
        (AppAccentColor.Amber, false) => "#FCD34D",
        _ when accent == AppAccentColor.ThemeDefault => theme switch
        {
            DesktopTheme.MacInspired => "#C5B4FF",
            DesktopTheme.Minimal => "#BFE7D7",
            DesktopTheme.Light => "#176B87",
            DesktopTheme.GamingNeon => "#D6FF5F",
            DesktopTheme.Cyberpunk => "#FF63D8",
            DesktopTheme.RetroAmber => "#FFC857",
            DesktopTheme.Monochrome => "#D9D9D9",
            _ => "#65E3E0"
        },
        _ => theme == DesktopTheme.Light ? "#176B87" : "#65E3E0"
    };

    internal static bool TryValidateCustomAccent(string? value, DesktopTheme theme, out string normalized, out double minimumContrast)
    {
        normalized = string.Empty;
        minimumContrast = 0;
        if (!AccentColorAccessibility.TryNormalize(value, out normalized)) return false;
        var accent = normalized;
        var surfaces = GetThemePalette(theme, AppAccentColor.ThemeDefault);
        var buttonText = theme == DesktopTheme.Light ? "#FFFFFF" : "#071623";
        var ratios = surfaces.Where((_, index) => index is 0 or 1 or 7 or 8)
            .Select(surface => AccentColorAccessibility.ContrastRatio(accent, surface))
            .Append(AccentColorAccessibility.ContrastRatio(buttonText, accent))
            .ToArray();
        minimumContrast = ratios.Min();
        return ratios.All(ratio => ratio >= 4.5);
    }

    private void RefreshCustomAccentValidation()
    {
        if (SystemParameters.HighContrast)
            _customAccentValidationSummary = "O alto contraste do Windows prevalece; a cor personalizada ficará salva, mas não será exibida enquanto ele estiver ativo.";
        else if (!AccentColorAccessibility.TryNormalize(CustomAccentDraftHex, out var normalized))
            _customAccentValidationSummary = "Use o formato #RRGGBB com seis dígitos hexadecimais.";
        else if (!TryValidateCustomAccent(normalized, SelectedTheme, out _, out var minimumContrast))
            _customAccentValidationSummary = $"Contraste mínimo {minimumContrast:0.00}:1 nesta paleta; escolha uma cor com pelo menos 4,5:1 nos fundos e no texto dos botões.";
        else if (_customAccentPreviewHex is not null && !string.Equals(normalized, _customAccentPreviewHex, StringComparison.OrdinalIgnoreCase))
            _customAccentValidationSummary = "O valor mudou desde a prévia; pré-visualize a nova cor antes de confirmar.";
        else if (_customAccentPreviewHex is not null)
            _customAccentValidationSummary = $"Prévia temporária de {normalized} · contraste mínimo {minimumContrast:0.00}:1. Confirme para salvar ou cancele para restaurar.";
        else
            _customAccentValidationSummary = $"Cor válida · contraste mínimo {minimumContrast:0.00}:1. Pré-visualize, confirme para salvar ou cancele.";
        Notify(nameof(CustomAccentValidationSummary));
        Notify(nameof(CanPreviewCustomAccent));
        Notify(nameof(CanConfirmCustomAccent));
        Notify(nameof(CanCancelCustomAccentPreview));
    }

    private void ClearCustomAccentForThemeChange()
    {
        _customAccentHex = null;
        _customAccentPreviewHex = null;
        Notify(nameof(IsCustomAccentPreviewing));
        RefreshCustomAccentValidation();
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
    private void RebuildHistory() { HistoryRows.Clear(); foreach (var report in _reports) HistoryRows.Add(HistoryRow.From(report)); Notify(nameof(HistoryEmptyText)); Notify(nameof(CanExport)); Notify(nameof(CanVerifyPendingDriverUpdates)); }
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
        if (_closingAfterActivityDrain)
        {
            _isClosing = true;
            _desktopClock?.Close();
            _lifetime.Cancel();
            return;
        }
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
        _isClosing = true;
        _desktopClock?.Close();
        _lifetime.Cancel();
    }
    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        foreach (var p in new[] { nameof(CanRefresh), nameof(CanChooseActions), nameof(CanConfirmVisualLayout), nameof(CanPreviewCustomAccent), nameof(CanConfirmCustomAccent), nameof(CanCancelCustomAccentPreview), nameof(CanConfirmDesktopClockSettings), nameof(CanCancelDesktopClockSettingsPreview), nameof(CanAnalyzeServiceDependencies), nameof(CanOpenNetworkResetSettings), nameof(CanSaveNetworkResetReference), nameof(CanExport), nameof(CanCancel), nameof(CanQuarantine), nameof(CanDisableStartup), nameof(CanSetPowerPlan), nameof(CanInstallDriver), nameof(CanVerifyPendingDriverUpdates), nameof(CanOfflineScan), nameof(CanSetPerformanceBaseline), nameof(CanComparePerformance) }) Notify(p);
        NotifyActionState();
    }
    private void NotifyActionState() { Notify(nameof(CanExecute)); Notify(nameof(SelectedActionsText)); Notify(nameof(CanQuarantine)); Notify(nameof(CleanupSelectedText)); Notify(nameof(CanDisableStartup)); Notify(nameof(CanInstallDriver)); Notify(nameof(CanRollbackDriver)); Notify(nameof(CanApplyWallpaper)); Notify(nameof(CanApplyDesktopOrganization)); Notify(nameof(CanGeneralOptimize)); Notify(nameof(GeneralPlanSummary)); Notify(nameof(CanOpenNetworkResetSettings)); }
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
    internal static string FormatPhysicalDiskNumbers(IReadOnlyList<int>? numbers) => numbers is { Count: > 0 }
        ? string.Join(", ", numbers.Select(number => $"#{number}"))
        : "indisponível";
    internal static string FormatMemoryInterleave(MemoryModuleInfo module) => module.InterleaveDataDepth switch
    {
        0 => "Interleave SMBIOS: não intercalado; canais ativos não confirmados.",
        { } depth when module.InterleavePosition is { } position =>
            $"Interleave SMBIOS: posição {position} · profundidade {depth}; canais ativos não confirmados.",
        { } depth => $"Interleave SMBIOS: profundidade {depth} · posição indisponível; canais ativos não confirmados.",
        _ when module.InterleavePosition is { } position =>
            $"Interleave SMBIOS: posição {position} · profundidade indisponível; canais ativos não confirmados.",
        _ => "Interleave SMBIOS indisponível; canais ativos não confirmados."
    };
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
        return $"{manual} · {pac} · {autodetect} · {bypass}. Fonte: HKCU Internet Settings; configurações por sessão e por aplicativo não consultadas.";
    }

    private static string FormatWinHttpProxyConfiguration(WinHttpProxyConfigurationInfo? proxy)
    {
        if (proxy is not { IsAvailable: true }) return "Estado indisponível nesta coleta; não equivale a proxy desativado.";
        var state = proxy.NamedProxyEnabled switch { true => "proxy nomeado configurado", false => "acesso direto configurado", null => "tipo de acesso desconhecido" };
        var server = string.IsNullOrWhiteSpace(proxy.ProxyServer) ? "servidor não informado" : $"servidor: {proxy.ProxyServer}";
        var bypass = string.IsNullOrWhiteSpace(proxy.BypassList) ? "exceções não informadas" : $"exceções: {proxy.BypassList}";
        return $"{state} · {server} · {bypass}. Fonte: configuração WinHTTP padrão; sessões e aplicativos podem sobrescrever esse valor.";
    }

    private static string FormatFirmwareBoot(FirmwareBootInfo? firmware) => firmware is not { IsAvailable: true }
        ? "Indisponível nesta coleta; o modo de inicialização não foi confirmado."
        : firmware.Mode switch
        {
            WindowsFirmwareBootMode.Uefi => "Inicialização em UEFI, reportada pelo Windows.",
            WindowsFirmwareBootMode.LegacyBios => "Inicialização em BIOS legado, reportada pelo Windows.",
            _ => "Tipo de firmware desconhecido segundo o Windows."
        };

    private static string FormatSystemDescription(HardwareSnapshot snapshot) =>
        $"{snapshot.ComputerName} · {FormatWindowsVersion(snapshot.WindowsVersion, snapshot.OperatingSystem)}";

    private static string FormatWindowsVersion(WindowsVersionInfo? version, string fallback)
    {
        if (version is not { IsAvailable: true }) return $"Detalhes da edição/build indisponíveis · {fallback}";
        var caption = version.Caption ?? "Edição desconhecida";
        var versionText = version.Version ?? "versão desconhecida";
        var build = version.BuildNumber ?? "build desconhecido";
        var architecture = version.Architecture ?? "arquitetura desconhecida";
        return $"{caption} · versão {versionText} · build {build} · {architecture}";
    }

    private static string FormatRestartState(WindowsRestartState state) => state.IsPending switch
    {
        true => $"Indicador detectado: {string.Join(", ", state.Sources)} · {state.CheckedSourceCount}/{state.TotalSourceCount} fontes consultadas; reinicialização indicada, não garantida.",
        false => $"Nenhum indicador encontrado · {state.CheckedSourceCount}/{state.TotalSourceCount} fontes consultadas; isso não garante ausência de reinicialização necessária.",
        _ => $"Estado desconhecido · {state.CheckedSourceCount}/{state.TotalSourceCount} fontes consultadas."
    };
    private static string FormatMetric(double? value) => value is { } number ? $"{number:0.#}%" : "indisponível";
    internal static string FormatCpuCores(double? value)
    {
        if (value is not { } number || !double.IsFinite(number) || number < 0) return "indisponível";
        var unit = number == 1 ? "núcleo equivalente" : "núcleos equivalentes";
        return $"{number.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture)} {unit}";
    }
    private static string FormatByteQuantity(double? bytes) => bytes is { } value && double.IsFinite(value) && value >= 0
        ? $"{value / (1024d * 1024 * 1024):0.##} GiB" : "indisponível";
    private static string FormatMetricCoverage(PerformanceMetricComparison? metric, int referenceTotal, int laterTotal) => metric is null
        ? "indisponível"
        : $"{FormatMetric(metric.ReferencePercent)} ({metric.ReferenceAvailableSamples}/{referenceTotal}) → {FormatMetric(metric.LaterPercent)} ({metric.LaterAvailableSamples}/{laterTotal})";
    private static string BooleanStatus(bool? value) => value switch { true => "Ativo", false => "Desativado", null => "Indisponível" };
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; Notify(name); return true; }
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
