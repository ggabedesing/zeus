using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using Zeus.Core;
using Zeus.Windows;

namespace Zeus.Desktop;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly IHardwareDiagnostics _diagnostics = new WindowsHardwareDiagnostics();
    private readonly IMaintenanceExecutor _executor = new ElevatedMaintenanceExecutor();
    private readonly OptimizationPlanner _planner = new();
    private readonly DesktopStorage _storage = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<MaintenanceReport> _reports = [];
    private readonly List<string> _startupWarnings = [];
    private HardwareSnapshot? _snapshot;
    private bool _isBusy;
    private bool _isExecuting;
    private bool _isMinimal;
    private bool _loaded;
    private string _statusTitle = "Preparando diagnóstico";
    private string _statusDetail = "Nenhuma alteração foi solicitada.";
    private string _executionLog = "Nenhuma manutenção executada nesta sessão.";
    private string _maintenanceResultSummary = string.Empty;

    public MainWindow()
    {
        InitializeComponent();
        foreach (var definition in MaintenanceCatalog.All)
        {
            var choice = new MaintenanceChoice(definition);
            choice.PropertyChanged += (_, _) => NotifyActionState();
            MaintenanceChoices.Add(choice);
        }
        ShowPendingHardware();
        DataContext = this;
    }

    public ObservableCollection<HardwareCard> HardwareCards { get; } = [];
    public ObservableCollection<RecommendationRow> Recommendations { get; } = [];
    public ObservableCollection<DeviceRow> GraphicsRows { get; } = [];
    public ObservableCollection<DeviceRow> DiskRows { get; } = [];
    public ObservableCollection<DeviceRow> StartupRows { get; } = [];
    public ObservableCollection<string> Warnings { get; } = [];
    public ObservableCollection<MaintenanceChoice> MaintenanceChoices { get; } = [];
    public ObservableCollection<HistoryRow> HistoryRows { get; } = [];
    public event PropertyChangedEventHandler? PropertyChanged;

    public bool CanRefresh => !_isBusy;
    public bool CanChooseActions => !_isBusy;
    public bool CanExecute => !_isBusy && MaintenanceChoices.Any(choice => choice.IsSelected);
    public bool CanExport => !_isBusy && (_snapshot is not null || _reports.Count > 0);
    public string StatusTitle { get => _statusTitle; private set => Set(ref _statusTitle, value); }
    public string StatusDetail { get => _statusDetail; private set => Set(ref _statusDetail, value); }
    public string ExecutionLog { get => _executionLog; private set => Set(ref _executionLog, value); }
    public string MaintenanceResultSummary { get => _maintenanceResultSummary; private set => Set(ref _maintenanceResultSummary, value); }
    public string CollectionDate => _snapshot is null ? "Leitura pendente" : _snapshot.CollectedAt.ToLocalTime().ToString("dd/MM HH:mm:ss");
    public string SystemDescription => _snapshot is null ? "Os componentes serão consultados diretamente no Windows." : $"{_snapshot.ComputerName} · {_snapshot.OperatingSystem}";
    public string RecommendationEmptyText => _snapshot is null ? "As recomendações aparecem depois do diagnóstico." : Recommendations.Count == 0 ? "Nenhuma recomendação pelos critérios desta leitura. Isso não substitui testes durante a tarefa que apresenta lentidão." : string.Empty;
    public string DevicesEmptyText => _snapshot is null ? "Atualize o diagnóstico para carregar o inventário." : "Leitura local do Windows. Campos ausentes são apresentados como indisponíveis.";
    public string StartupEmptyText => _snapshot is null ? "Inventário ainda não carregado." : StartupRows.Count == 0 ? "Nenhuma entrada foi retornada; confira os avisos do diagnóstico." : string.Empty;
    public string HistoryEmptyText => !_historyReadable
        ? "O histórico anterior não pôde ser lido e foi preservado. Exporte o relatório para guardar novas sessões."
        : HistoryRows.Count == 0 ? "Ainda não há relatórios de manutenção salvos neste usuário." : string.Empty;
    public string SelectedActionsText => $"{MaintenanceChoices.Count(choice => choice.IsSelected)} ação(ões) selecionada(s)";
    public Visibility DetailedVisibility => IsMinimal ? Visibility.Collapsed : Visibility.Visible;
    public string LayoutDescription => IsMinimal ? "Layout mínimo" : "Layout completo";
    public bool IsMinimal
    {
        get => _isMinimal;
        set
        {
            if (!Set(ref _isMinimal, value)) return;
            Notify(nameof(DetailedVisibility));
            Notify(nameof(LayoutDescription));
            if (_loaded) _ = SavePreferencesAsync();
        }
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        SetBusy(true);
        try
        {
            try { IsMinimal = (await _storage.ReadPreferencesAsync()).IsMinimal; }
            catch (Exception error) when (IsStorageError(error))
            {
                _startupWarnings.Add("O layout salvo não pôde ser carregado. O layout completo será usado.");
            }
            try
            {
                _reports.AddRange(await _storage.ReadHistoryAsync());
                RebuildHistory();
            }
            catch (Exception error) when (IsStorageError(error))
            {
                _startupWarnings.Add("O histórico local não pôde ser lido. Ele não será sobrescrito durante esta sessão sem uma nova exportação.");
                _historyReadable = false;
                Notify(nameof(HistoryEmptyText));
            }
            foreach (var warning in _startupWarnings) Warnings.Add(warning);
            _loaded = true;
        }
        finally { SetBusy(false); }
        await RefreshDiagnosticsAsync();
    }

    private bool _historyReadable = true;

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshDiagnosticsAsync();

    private async Task RefreshDiagnosticsAsync()
    {
        if (_isBusy) return;
        SetBusy(true);
        StatusTitle = "Consultando o computador";
        StatusDetail = "Lendo componentes, volumes, inicialização e estado do Defender. Nenhuma configuração é alterada.";
        try
        {
            var snapshot = await _diagnostics.CollectAsync(_lifetime.Token);
            _snapshot = snapshot;
            DisplaySnapshot(snapshot);
            StatusTitle = "Diagnóstico concluído";
            StatusDetail = snapshot.Warnings.Count == 0 ? "Inventário atualizado. Revise as recomendações e escolha as ações desejadas." : "Inventário atualizado com leituras limitadas. Consulte as recomendações e os avisos.";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            StatusTitle = "Não foi possível concluir o diagnóstico";
            StatusDetail = $"{error.Message} Você pode tentar novamente em Atualizar diagnóstico.";
            if (_snapshot is not null) StatusDetail += " Os dados exibidos pertencem à leitura anterior.";
        }
        finally { SetBusy(false); }
    }

    private void DisplaySnapshot(HardwareSnapshot snapshot)
    {
        HardwareCards.Clear();
        var cpu = snapshot.Cpu;
        HardwareCards.Add(new("Processador", cpu?.Name ?? "Indisponível", cpu is null ? "O Windows não retornou esta leitura." : $"{cpu.PhysicalCores} núcleos · {cpu.LogicalProcessors} processadores lógicos"));
        var memory = snapshot.Memory;
        HardwareCards.Add(new("Memória RAM", memory is null ? "Indisponível" : ByteFormatting.Format(memory.TotalBytes), memory is null ? "O Windows não retornou esta leitura." : $"{ByteFormatting.Format(memory.AvailableBytes)} disponíveis neste instante"));
        var gpu = snapshot.Graphics.FirstOrDefault();
        HardwareCards.Add(new("Placas de vídeo", gpu?.Name ?? "Indisponível", gpu is null ? "Nenhuma placa retornada nesta coleta." : $"{snapshot.Graphics.Count} dispositivo(s) · Driver {Available(gpu.DriverVersion)}"));
        HardwareCards.Add(new("Armazenamento", $"{snapshot.Disks.Count} volume(s)", snapshot.Disks.Count == 0 ? "Nenhum volume retornado nesta coleta." : "Capacidade e espaço livre em Dispositivos."));
        var security = snapshot.Security;
        HardwareCards.Add(new("Microsoft Defender", BooleanStatus(security?.DefenderEnabled), security?.Summary ?? "Consulte o provedor em Segurança do Windows."));
        HardwareCards.Add(new("Inicialização", $"{snapshot.Startup.Count} entrada(s)", "Registros encontrados; a quantidade não mede o impacto."));
        Recommendations.Clear();
        foreach (var recommendation in _planner.Build(snapshot))
            Recommendations.Add(new(recommendation.Title, recommendation.Reason,
                recommendation.Action is { } action ? $"Disponível em Manutenção: {MaintenanceCatalog.Get(action).Title}" : string.Empty));
        GraphicsRows.Clear();
        foreach (var item in snapshot.Graphics)
            GraphicsRows.Add(new(Available(item.Name), $"Driver informado pelo Windows: {Available(item.DriverVersion)}"));
        DiskRows.Clear();
        foreach (var disk in snapshot.Disks)
            DiskRows.Add(new($"{disk.DriveLetter} · {Available(disk.Name)}", $"{ByteFormatting.Format(disk.FreeBytes)} livres de {ByteFormatting.Format(disk.TotalBytes)} · {Available(disk.FileSystem)}"));
        StartupRows.Clear();
        foreach (var entry in snapshot.Startup)
            StartupRows.Add(new(Available(entry.Name), $"Origem: {Available(entry.Location)} · Usuário: {Available(entry.User)}"));
        Warnings.Clear();
        foreach (var warning in _startupWarnings.Concat(snapshot.Warnings)) Warnings.Add(warning);
        foreach (var property in new[] { nameof(CollectionDate), nameof(SystemDescription), nameof(RecommendationEmptyText), nameof(DevicesEmptyText), nameof(StartupEmptyText), nameof(CanExport) }) Notify(property);
    }

    private void ShowPendingHardware()
    {
        foreach (var title in new[] { "Processador", "Memória RAM", "Placas de vídeo", "Armazenamento", "Microsoft Defender", "Inicialização" })
            HardwareCards.Add(new(title, "Aguardando leitura", "Nenhum valor estimado."));
    }

    private void OpenMaintenance_Click(object sender, RoutedEventArgs e) => WorkspaceTabs.SelectedIndex = 1;

    private async void Execute_Click(object sender, RoutedEventArgs e)
    {
        if (!CanExecute) return;
        var selected = MaintenancePolicy.ValidateAndOrder(MaintenanceChoices.Where(choice => choice.IsSelected).Select(choice => choice.Id));
        var definitions = selected.Select(MaintenanceCatalog.Get).ToArray();
        var confirmation = new StringBuilder("O ZEUS executará somente estas ações, nesta ordem:\n\n");
        foreach (var definition in definitions) confirmation.AppendLine($"• {definition.Title}");
        confirmation.Append("\nO Windows solicitará autorização de administrador. Aguarde a conclusão e mantenha o computador conectado à energia.\n\n");
        if (selected.Contains(MaintenanceActionId.DefenderQuickScan))
            confirmation.Append("A verificação do Defender segue as políticas de remediação de ameaças configuradas no Windows.\n\n");
        if (definitions.Any(definition => definition.RequiresRestorePoint))
            confirmation.Append("O auxiliar tentará criar e confirmar um ponto de restauração. Se não conseguir, os reparos serão bloqueados. O ponto de restauração não recupera documentos apagados. Reparos podem exigir reinicialização.\n\n");
        else confirmation.Append("Estas ações verificam o sistema; concluir uma verificação não comprova que todos os problemas foram resolvidos.\n\n");
        confirmation.Append("Deseja executar este plano?");
        if (MessageBox.Show(this, confirmation.ToString(), "Revisar plano de manutenção", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;

        _isExecuting = true;
        SetBusy(true);
        ExecutionLog = string.Empty;
        MaintenanceResultSummary = string.Empty;
        StatusTitle = "Manutenção solicitada";
        StatusDetail = "Aguarde a autorização e a conclusão do auxiliar. O aplicativo não interromperá reparos em execução.";
        try
        {
            var progress = new Progress<string>(message => AppendLog(message));
            var report = await _executor.ExecuteAsync(selected, progress);
            foreach (var step in report.Steps) AppendLog($"{MaintenanceCatalog.Get(step.Action).Title}: {step.Message}");
            _reports.Insert(0, report);
            RebuildHistory();
            MaintenanceResultSummary = BuildResultSummary(report, definitions);
            StatusTitle = report.Error is null && ReportCoversPlan(report, definitions) && report.Steps.All(step => step.Outcome == StepOutcome.Succeeded)
                ? "Sessão concluída" : "Sessão encerrada com avisos";
            StatusDetail = MaintenanceResultSummary + " Atualize o diagnóstico para obter uma nova leitura do computador.";
            try
            {
                if (_historyReadable) await _storage.SaveHistoryAsync(_reports);
                else AppendLog("Histórico anterior preservado por erro de leitura. Exporte o relatório para guardar esta sessão.");
            }
            catch (Exception error) when (IsStorageError(error))
            {
                AppendLog($"O resultado está disponível, mas não pôde ser salvo no histórico: {error.Message}");
                StatusDetail += " Exporte o relatório para guardar esta sessão.";
            }
        }
        catch (Exception error)
        {
            AppendLog($"Não foi possível obter o resultado: {error.Message}");
            MaintenanceResultSummary = "A execução não foi confirmada. Consulte o registro; nenhum reparo será apresentado como concluído sem relatório.";
            StatusTitle = "Falha ao obter o resultado";
            StatusDetail = MaintenanceResultSummary;
        }
        finally
        {
            _isExecuting = false;
            foreach (var choice in MaintenanceChoices) choice.IsSelected = false;
            SetBusy(false);
        }
    }

    private static string BuildResultSummary(MaintenanceReport report, IReadOnlyList<MaintenanceActionDefinition> definitions)
    {
        if (!string.IsNullOrWhiteSpace(report.Error)) return report.Error;
        if (report.Steps.Count == 0) return "Nenhum resultado de ação foi recebido.";
        if (!ReportCoversPlan(report, definitions)) return "O relatório não confirma todas as ações selecionadas. Consulte cada resultado recebido; não foi confirmada conclusão do plano completo.";
        if (report.Steps.Any(step => step.Outcome != StepOutcome.Succeeded))
            return "Consulte cada etapa no histórico: houve falha, cancelamento ou ação não executada. Nenhum ganho de desempenho foi medido.";
        return definitions.Any(definition => definition.RequiresRestorePoint)
            ? "Os comandos de reparo terminaram. Consulte os resultados e logs para saber o que foi identificado e corrigido; reinicie se o Windows solicitar."
            : "As verificações terminaram. Consulte seus resultados e logs; esta sessão não solicitou reparos do Windows.";
    }

    private static bool ReportCoversPlan(MaintenanceReport report, IReadOnlyList<MaintenanceActionDefinition> definitions) =>
        report.Steps.Count == definitions.Count &&
        definitions.All(definition => report.Steps.Count(step => step.Action == definition.Id) == 1);

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (!CanExport) return;
        var dialog = new SaveFileDialog
        {
            Title = "Exportar diagnóstico e histórico do ZEUS",
            Filter = "Relatório JSON (*.json)|*.json",
            FileName = $"zeus-relatorio-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            DefaultExt = ".json",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true) return;
        SetBusy(true);
        try
        {
            await DesktopStorage.ExportAsync(dialog.FileName, _snapshot, _reports);
            MessageBox.Show(this, "Relatório exportado. Ele contém o nome do computador, inventário, nomes de usuários da inicialização e resultados de manutenção. Revise essas informações antes de compartilhar.", "Exportação concluída", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception error) when (IsStorageError(error))
        {
            MessageBox.Show(this, $"O relatório não pôde ser exportado: {error.Message}\nEscolha outro destino e tente novamente.", "Exportação não concluída", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { SetBusy(false); }
    }

    private async Task SavePreferencesAsync()
    {
        try { await _storage.SavePreferencesAsync(IsMinimal); }
        catch (Exception error) when (IsStorageError(error))
        {
            StatusDetail = "O layout mudou nesta sessão, mas a preferência não pôde ser salva.";
        }
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: Guid sessionId }) return;
        try
        {
            // Resolve only the protected, validated session directory; never open
            // a path supplied by the user-editable desktop history or a step log.
            var directory = SessionStore.GetSessionDirectory(sessionId);
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception or ArgumentException)
        {
            MessageBox.Show(this, $"A pasta de logs desta sessão não pôde ser aberta: {error.Message}", "Logs indisponíveis", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RebuildHistory()
    {
        HistoryRows.Clear();
        foreach (var report in _reports) HistoryRows.Add(HistoryRow.From(report));
        Notify(nameof(HistoryEmptyText));
        Notify(nameof(CanExport));
    }

    private void AppendLog(string message) => ExecutionLog += $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_isExecuting)
        {
            e.Cancel = true;
            MessageBox.Show(this, "A manutenção ainda está em execução. Aguarde o relatório antes de fechar. O ZEUS não encerrará o auxiliar nem interromperá comandos de reparo.", "Manutenção em execução", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _lifetime.Cancel();
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        Notify(nameof(CanRefresh));
        Notify(nameof(CanChooseActions));
        Notify(nameof(CanExport));
        NotifyActionState();
    }

    private void NotifyActionState()
    {
        Notify(nameof(CanExecute));
        Notify(nameof(SelectedActionsText));
    }

    private static bool IsStorageError(Exception error) => error is IOException or UnauthorizedAccessException or JsonException or NotSupportedException;
    private static string Available(string? value) => string.IsNullOrWhiteSpace(value) ? "Indisponível" : value;
    private static string BooleanStatus(bool? value) => value switch { true => "Ativo", false => "Desativado", null => "Indisponível" };

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Notify(propertyName);
        return true;
    }

    private void Notify([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
