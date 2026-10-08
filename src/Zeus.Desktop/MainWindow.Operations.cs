using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Zeus.Core;
using Zeus.Windows;
using Zeus.Storage;

namespace Zeus.Desktop;

public partial class MainWindow
{
    private async Task LoadLocalSessionsAsync()
    {
        try
        {
            var recovered = _isFixture ? Array.Empty<MaintenanceReport>() : (await _pendingSessions.RecoverAsync()).ToArray();
            foreach (var report in recovered)
            {
                var existing = _reports.FindIndex(r => r.SessionId == report.SessionId);
                if (existing < 0) _reports.Add(report);
                else if (!_reports[existing].IsComplete || report.IsComplete) _reports[existing] = report;
            }
            _reports.Sort((a, b) => b.StartedAt.CompareTo(a.StartedAt));
            RebuildHistory();
            if (_historyReadable && recovered.Length > 0)
            {
                await _storage.SaveHistoryAsync(_reports);
                foreach (var report in recovered.Where(r => r.IsComplete)) await _pendingSessions.ForgetAsync(report.SessionId);
            }
        }
        catch (Exception error) { _startupWarnings.Add($"A recuperação de sessões de manutenção não foi concluída: {error.Message}"); }
        try { await RefreshCleanupSessionsAsync(); }
        catch (Exception error) { _startupWarnings.Add($"A recuperação da limpeza não pôde ser lida: {error.Message}"); }
        try { await RefreshUserChangesAsync(); }
        catch (Exception error) { _startupWarnings.Add($"O histórico de preferências não pôde ser lido: {error.Message}"); }
        try { await RefreshPowerPlansAsync(); }
        catch (Exception error) { _startupWarnings.Add($"Os planos de energia não puderam ser lidos: {error.Message}"); }
        try { await LoadPerformanceSessionsAsync(); }
        catch (Exception error) { _startupWarnings.Add($"O histórico de desempenho não pôde ser lido: {error.Message}"); }
    }

    private async void Execute_Click(object sender, RoutedEventArgs e)
    {
        if (!CanExecute) return;
        var requests = MaintenanceChoices.Where(c => c.IsSelected).Select(c => new MaintenanceRequest(c.Id)).ToArray();
        await ReviewAndExecuteAsync(requests);
    }

    private async Task ReviewAndExecuteAsync(IReadOnlyCollection<MaintenanceRequest> requests)
    {
        if (_isBusy) return;
        IReadOnlyList<MaintenanceRequest> ordered;
        try { ordered = MaintenancePolicy.ValidateRequests(requests); }
        catch (ArgumentException error)
        {
            MessageBox.Show(this, error.Message, "Plano de manutenção inválido", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var definitions = ordered.Select(r => MaintenanceCatalog.Get(r.Action)).ToArray();
        var text = new StringBuilder("Revise as ações selecionadas:\n\n");
        foreach (var definition in definitions) text.AppendLine($"• {definition.Title}");
        text.Append("\nO Windows solicitará autorização de administrador. A execução é sequencial e pode demorar; mantenha o computador conectado à energia.\n\n");
        if (definitions.Any(d => d.RequiresRestorePoint)) text.Append("Os reparos e a instalação de driver exigem proteção de recuperação confirmada. Se não for possível confirmar, essas ações serão bloqueadas. Restauração do sistema não recupera documentos apagados.\n\n");
        if (ordered.Any(r => r.Action == MaintenanceActionId.InstallDriverUpdate)) text.Append("Somente o candidato exato será consultado novamente e instalado. O auxiliar exportará os drivers existentes antes da alteração. Uma versão mais recente não garante melhoria.\n\n");
        if (ordered.Any(r => r.Action is MaintenanceActionId.DefenderQuickScan or MaintenanceActionId.DefenderFullScan or MaintenanceActionId.DefenderOfflineScan)) text.Append("O Defender segue as políticas de remediação de ameaças do Windows. Consulte os resultados em Segurança do Windows.\n\n");
        if (ordered.Any(r => r.Action == MaintenanceActionId.DefenderOfflineScan)) text.Append("ATENÇÃO: a verificação offline pode reiniciar este computador imediatamente. Salve seu trabalho. Tenha a recuperação do BitLocker disponível se a unidade estiver criptografada.\n\n");
        text.Append("Executar este plano agora?");
        if (!Confirm(text.ToString(), "Revisar manutenção")) return;
        await RunOperationAsync("Manutenção em andamento", "Aguarde o auxiliar e o relatório real da execução.", async _ =>
        {
            await ExecuteMaintenancePlanAsync(ordered);
        }, mutation: true);
    }

    private async Task<bool> ExecuteMaintenancePlanAsync(IReadOnlyList<MaintenanceRequest> ordered, bool resetLog = true)
    {
            if (resetLog) ExecutionLog = string.Empty;
            MaintenanceResultSummary = string.Empty;
            var report = await _executor.ExecuteRequestsAsync(ordered, new Progress<string>(AppendLog));
            foreach (var step in report.Steps) AppendLog($"{MaintenanceCatalog.Get(step.Action).Title}: {step.Message}");
            _reports.Insert(0, report); RebuildHistory();
            var complete = report.IsComplete && report.Error is null && ordered.Count == report.Steps.Count && ordered.All(r => report.Steps.Count(s => s.Action == r.Action && s.TargetId == r.TargetId) == 1);
            MaintenanceResultSummary = !report.IsComplete ? "A conclusão da sessão não foi confirmada. Consulte os resultados parciais e os logs; o histórico será recuperado na próxima abertura."
                : !string.IsNullOrWhiteSpace(report.Error) ? report.Error
                : !complete ? "O relatório não confirma todas as ações do plano. Consulte cada etapa recebida."
                : report.Steps.Any(s => s.Outcome != StepOutcome.Succeeded) ? "Houve falha, cancelamento ou ação não executada. Consulte as etapas no histórico."
                : "As operações terminaram. Consulte seus resultados e reinicie se o Windows solicitar. Nenhum ganho de desempenho foi medido nesta execução.";
            StatusTitle = complete && report.Steps.All(s => s.Outcome == StepOutcome.Succeeded) ? "Sessão concluída" : "Sessão encerrada com avisos";
            StatusDetail = MaintenanceResultSummary;
            try
            {
                if (_historyReadable)
                {
                    await _storage.SaveHistoryAsync(_reports);
                    if (report.IsComplete && !_isFixture) await _pendingSessions.ForgetAsync(report.SessionId);
                }
                else AppendLog("Histórico anterior preservado; exporte o resultado desta sessão.");
            }
            catch (Exception error) when (IsStorageError(error)) { AppendLog($"Não foi possível salvar o histórico: {error.Message}"); StatusDetail += " Exporte o relatório para guardar esta sessão."; }
            foreach (var choice in MaintenanceChoices) choice.IsSelected = false;
            OfflineRestartConfirmed = false; OfflineRecoveryConfirmed = false;
            return complete && report.Steps.All(s => s.Outcome == StepOutcome.Succeeded);
    }

    private async void OfflineScan_Click(object sender, RoutedEventArgs e)
    {
        if (CanOfflineScan) await ReviewAndExecuteAsync([new(MaintenanceActionId.DefenderOfflineScan)]);
    }

    private async void Performance_Click(object sender, RoutedEventArgs e)
    {
        await RunOperationAsync("Medindo carga real", "Amostrando CPU, memória e processos por cinco segundos.", async token =>
        {
            _performanceSessionId = Guid.NewGuid();
            var id = _performanceSessionId;
            var started = DateTimeOffset.UtcNow;
            await BeginPerformanceSessionAsync(id, "Medição manual", started);
            try
            {
                var observation = await _performanceProbe.SampleAsync(TimeSpan.FromSeconds(5), token);
                DisplayPerformanceObservation(observation);
                await StorePerformanceObservationAsync(id, observation);
                BuildPersonalPlan(); Notify(nameof(Performance));
                StatusTitle = "Medição concluída"; StatusDetail = "A amostra registra esta carga. Repita durante a tarefa para comparar condições equivalentes.";
            }
            finally { await FinishPerformanceSessionAsync(id); }
        }, cancellable: true);
    }

    private async void NetworkLatency_Click(object sender, RoutedEventArgs e)
    {
        string target;
        try { target = NetworkLatencyProbe.ValidateTarget(NetworkProbeTarget); }
        catch (ArgumentException error)
        {
            NetworkProbeSummary = error.Message;
            return;
        }

        await RunOperationAsync("Medindo resposta ICMP", $"Resolvendo {target} e enviando até cinco tentativas, com limite de um segundo cada.", async token =>
        {
            var result = await _networkLatencyProbe.MeasureAsync(target, token);
            var min = result.MinimumMilliseconds is { } minimum ? $"{minimum} ms" : "indisponível";
            var average = result.AverageMilliseconds is { } avg ? $"{avg:0.##} ms" : "indisponível";
            var max = result.MaximumMilliseconds is { } maximum ? $"{maximum} ms" : "indisponível";
            var statuses = result.Samples.Where(sample => sample.Status != "Success" && sample.Status != "TimedOut")
                .Select(sample => sample.Status).Distinct().ToArray();
            NetworkProbeSummary = $"{result.Target} → {result.Address} · respostas: {result.Replies}/5 · sem resposta no limite: {result.NoReplies}/5 · latência ICMP mín/média/máx: {min}/{average}/{max}" +
                (statuses.Length == 0 ? string.Empty : $" · outros resultados: {string.Join(", ", statuses)}") +
                $" · {result.CheckedAt.ToLocalTime():dd/MM HH:mm:ss}";
            var correlation = Guid.NewGuid().ToString("N");
            var details = JsonSerializer.Serialize(new { result.Target, result.Address, result.CheckedAt, result.Samples, result.Replies, result.NoReplies, result.MinimumMilliseconds, result.AverageMilliseconds, result.MaximumMilliseconds });
            QueueActivity(new ActivityEntry(result.CheckedAt, "network-diagnostic", "icmp-measurement-completed",
                result.Replies == 0 ? "warning" : "info", NetworkProbeSummary, details, correlation));
            StatusTitle = "Medição ICMP concluída";
            StatusDetail = "O resultado descreve somente respostas ICMP deste destino e horário. Consulte o aviso de interpretação na tela.";
        }, cancellable: true);
    }

    private async void StartObserver_Click(object sender, RoutedEventArgs e)
    {
        _performanceSessionId = Guid.NewGuid();
        await RunOperationAsync("Observando desempenho", "Amostras adaptativas de CPU, memória e processos. Use Cancelar leitura para encerrar.", async token =>
        {
            var id = _performanceSessionId;
            await BeginPerformanceSessionAsync(id, "Observador adaptativo", DateTimeOffset.UtcNow);
            try
            {
                while (true)
                {
                    if (_performanceSessionSequences.GetValueOrDefault(id) >= PerformanceHistoryBuffer.DefaultCapacity)
                    {
                        await FinishPerformanceSessionAsync(id);
                        id = _performanceSessionId = Guid.NewGuid();
                        await BeginPerformanceSessionAsync(id, "Observador adaptativo", DateTimeOffset.UtcNow);
                    }
                    var observation = await _performanceProbe.SampleAsync(TimeSpan.FromSeconds(2), token);
                    DisplayPerformanceObservation(observation);
                    await StorePerformanceObservationAsync(id, observation);
                    var interval = AdaptiveSamplingPolicy.NextInterval(observation);
                    PerformanceSummary += $" · próxima amostra em {interval.TotalSeconds:0} s";
                    StatusDetail = $"Sessão {id:N} · {_performanceHistory.Snapshot().Count} amostras guardadas em memória. Cancele para encerrar.";
                    await Task.Delay(interval, token);
                }
            }
            finally { await FinishPerformanceSessionAsync(id); }
        }, cancellable: true);
    }

    private async Task BeginPerformanceSessionAsync(Guid id, string label, DateTimeOffset startedAt)
    {
        if (!_performanceSessionStartAttempts.Add(id)) return;
        try
        {
            await _storage.StartPerformanceSessionAsync(id, label, startedAt);
            _persistedPerformanceSessions.Add(id);
            _performanceSessionSequences[id] = 0;
        }
        catch (Exception error) when (IsStorageError(error)) { AddPerformanceStorageWarning(error); }
    }

    private async Task StorePerformanceObservationAsync(Guid id, PerformanceObservation observation)
    {
        if (!_persistedPerformanceSessions.Contains(id)) return;
        var sequence = _performanceSessionSequences.GetValueOrDefault(id);
        try
        {
            await _storage.AppendPerformanceObservationAsync(id, sequence, observation);
            _performanceSessionSequences[id] = sequence + 1;
        }
        catch (Exception error) when (IsStorageError(error))
        {
            _persistedPerformanceSessions.Remove(id);
            AddPerformanceStorageWarning(error);
        }
    }

    private async Task FinishPerformanceSessionAsync(Guid id)
    {
        if (!_persistedPerformanceSessions.Contains(id)) return;
        try { await _storage.FinishPerformanceSessionAsync(id, DateTimeOffset.UtcNow); }
        catch (Exception error) when (IsStorageError(error)) { AddPerformanceStorageWarning(error); }
    }

    private void AddPerformanceStorageWarning(Exception error)
    {
        const string warning = "O histórico de desempenho desta leitura não pôde ser gravado no SQLite; os dados ainda podem ser exportados enquanto o ZEUS estiver aberto.";
        if (!Warnings.Contains(warning)) Warnings.Add(warning);
        AppendLog($"Persistência de desempenho não concluída ({error.GetType().Name}).");
    }

    private async Task LoadPerformanceSessionsAsync()
    {
        var sessions = await _storage.ReadPerformanceSessionsAsync();
        var all = new List<(bool IsReference, PerformanceObservation Observation)>();
        foreach (var session in sessions.OrderBy(session => session.StartedAt))
        {
            if (!Guid.TryParseExact(session.SessionId, "D", out var id) || id == Guid.Empty)
            {
                _startupWarnings.Add("Uma sessão de desempenho com identificador inválido foi ignorada; o banco foi preservado.");
                continue;
            }
            _performanceSessionStartAttempts.Add(id);
            _persistedPerformanceSessions.Add(id);
            foreach (var sample in session.Samples.OrderBy(sample => sample.Sequence))
            {
                try
                {
                    var observation = JsonSerializer.Deserialize<PerformanceObservation>(sample.DetailsJson, DesktopStorage.JsonOptions)
                        ?? throw new InvalidDataException("A amostra armazenada está vazia.");
                    if (!session.IsReference) _performanceHistory.Add(new(id, observation));
                    all.Add((session.IsReference, observation));
                    _performanceSessionSequences[id] = Math.Max(_performanceSessionSequences.GetValueOrDefault(id), sample.Sequence + 1);
                }
                catch (Exception error) when (error is JsonException or InvalidDataException or NotSupportedException)
                {
                    _startupWarnings.Add("Uma amostra de desempenho inválida foi ignorada; o registro original permanece no banco.");
                }
            }
            if (session.FinishedAt is null)
            {
                if (session.Samples.Count > 0)
                    _startupWarnings.Add("Uma sessão de desempenho terminou sem fechamento confirmado; as amostras já gravadas foram recuperadas.");
                try { await _storage.FinishPerformanceSessionAsync(id, DateTimeOffset.UtcNow); }
                catch (Exception error) when (IsStorageError(error))
                {
                    _startupWarnings.Add($"O estado encerrado da sessão de desempenho não foi atualizado ({error.GetType().Name}).");
                }
            }
        }

        _performanceBaseline = all.Where(item => item.IsReference).Select(item => item.Observation).TakeLast(5).ToArray();
        var last = _performanceHistory.Snapshot().LastOrDefault();
        if (last is not null)
        {
            _performance = last.Observation;
            PerformanceSummary = $"Histórico recuperado: {_performanceHistory.Snapshot().Count} amostras locais. Última leitura em {last.Observation.CollectedAt.ToLocalTime():dd/MM HH:mm:ss}.";
        }
        if (_performanceBaseline.Length >= 3)
        {
            var baselineEnd = _performanceBaseline[^1].CollectedAt;
            var later = all.Where(item => !item.IsReference && item.Observation.CollectedAt > baselineEnd)
                .Select(item => item.Observation).TakeLast(5).ToArray();
            if (later.Length >= 3) _performanceComparison = PerformanceComparisonBuilder.Compare(_performanceBaseline, later);
        }
        Notify(nameof(Performance)); Notify(nameof(CanSetPerformanceBaseline)); Notify(nameof(CanComparePerformance)); Notify(nameof(PerformanceComparisonSummary));
    }

    private void DisplayPerformanceObservation(PerformanceObservation observation)
    {
        _performance = observation;
        _performanceHistory.Add(new(_performanceSessionId, observation));
        if (_performanceBaseline.Length > 0 && observation.CollectedAt > _performanceBaseline[^1].CollectedAt)
            _performanceComparison = null;
        var memory = observation.TotalMemoryBytes == 0 ? "indisponível" : $"{ByteFormatting.Format(observation.AvailableMemoryBytes)} de {ByteFormatting.Format(observation.TotalMemoryBytes)}";
        PerformanceSummary = $"CPU: {(observation.CpuPercent.HasValue ? $"{observation.CpuPercent:0.#}%" : "indisponível")} · RAM disponível: {memory} · {observation.ActivityContext?.Summary ?? "Contexto de jogo/OBS indisponível."} · Amostra de {observation.SamplingDuration.TotalSeconds:0.#} s em {observation.CollectedAt.ToLocalTime():dd/MM HH:mm:ss}";
        ProcessRows.Clear();
        foreach (var process in observation.Processes)
            ProcessRows.Add(new($"{process.Name} · PID {process.Id}", $"CPU: {(process.CpuPercent.HasValue ? $"{process.CpuPercent:0.#}%" : "indisponível")} · Memória residente: {ByteFormatting.Format(process.WorkingSetBytes)}"));
        PerformanceResourceRows.Clear();
        foreach (var engine in (observation.GpuEngines ?? []).OrderByDescending(engine => engine.UtilizationPercent).Take(20))
        {
            var process = engine.ProcessId is { } pid
                ? engine.ProcessName is { Length: > 0 } name ? $"{name} · PID {pid}" : $"PID {pid} · nome não mapeado"
                : "processo não informado pelo contador";
            PerformanceResourceRows.Add(new($"GPU {engine.EngineType} · {process}", $"Uso desta instância: {engine.UtilizationPercent:0.#}% · não representa uso total da GPU"));
        }
        foreach (var gpuMemory in observation.GpuMemory ?? [])
            PerformanceResourceRows.Add(new($"Memória GPU · {gpuMemory.AdapterInstance}",
                $"Uso dedicado reportado: {FormatBytes(gpuMemory.DedicatedUsageBytes)} de {FormatBytes(gpuMemory.DedicatedCapacityBytes)} · ocupação: {FormatMetric(gpuMemory.DedicatedOccupancyPercent)} · compartilhado: {FormatBytes(gpuMemory.SharedUsageBytes)} · comprometido: {FormatBytes(gpuMemory.TotalCommittedBytes)}"));
        var sessionSamples = _performanceHistory.Snapshot().Where(entry => entry.SessionId == _performanceSessionId)
            .TakeLast(20).Select(entry => entry.Observation).ToArray();
        foreach (var assessment in GpuMemoryOccupancyAnalyzer.Assess(sessionSamples))
            PerformanceResourceRows.Add(new($"Sinal de ocupação GPU · {assessment.AdapterInstance}", FormatGpuOccupancyAssessment(assessment)));
        foreach (var disk in observation.Disks ?? [])
            PerformanceResourceRows.Add(new($"Disco · {disk.InstanceName}", $"Transferência: {FormatBytesPerSecond(disk.BytesPerSecond)} · ativo: {(disk.ActivePercent is { } active ? $"{active:0.#}%" : "indisponível")} · leitura: {(disk.AverageReadLatencyMilliseconds is { } latency ? $"{latency:0.##} ms" : "indisponível")}"));
        foreach (var network in observation.Networks ?? [])
            PerformanceResourceRows.Add(new($"Rede · {network.Adapter}", $"Tráfego: {FormatBytesPerSecond(network.BytesPerSecond)} · enlace reportado: {FormatBitsPerSecond(network.LinkBitsPerSecond)} · erros acumulados: {network.ErrorPackets?.ToString() ?? "indisponível"}"));
        foreach (var warning in observation.Warnings)
            if (!Warnings.Contains(warning)) Warnings.Add(warning);
        Notify(nameof(Performance)); Notify(nameof(CanSetPerformanceBaseline)); Notify(nameof(CanComparePerformance)); Notify(nameof(PerformanceComparisonSummary));
    }

    private static string FormatGpuOccupancyAssessment(GpuMemoryOccupancyAssessment assessment) => assessment.State switch
    {
        GpuMemoryOccupancyState.InsufficientEvidence => $"Evidência insuficiente: {assessment.ValidSamples} leitura(s) válida(s) em {assessment.Window.TotalSeconds:0.#} s; exigidos {GpuMemoryOccupancyAnalyzer.MinimumSamples} leituras e pelo menos {GpuMemoryOccupancyAnalyzer.MinimumWindow.TotalSeconds:0} s.",
        GpuMemoryOccupancyState.SustainedHighOccupancy => $"Ocupação dedicada ≥{GpuMemoryOccupancyAnalyzer.HighOccupancyThresholdPercent:0}% em {assessment.HighOccupancySamples}/{assessment.ValidSamples} leituras; média {assessment.AverageOccupancyPercent:0.#}%. Sinal para investigar; não confirma pressão, gargalo ou impacto no jogo.",
        _ => $"Sem ocupação dedicada ≥{GpuMemoryOccupancyAnalyzer.HighOccupancyThresholdPercent:0}% sustentada nesta janela ({assessment.ValidSamples} leituras). Isso não exclui pressão ou gargalo por outra causa."
    };

    private async void SetPerformanceBaseline_Click(object sender, RoutedEventArgs e)
    {
        var observations = _performanceHistory.Snapshot().TakeLast(5).Select(entry => entry.Observation).ToArray();
        if (observations.Length < 3) return;
        _performanceBaseline = observations;
        _performanceComparison = null;
        Notify(nameof(CanComparePerformance)); Notify(nameof(PerformanceComparisonSummary));
        StatusDetail = $"Referência definida com {observations.Length} amostras. Execute a mesma tarefa em condições semelhantes e colete ao menos três amostras posteriores.";

        var referenceId = Guid.NewGuid();
        await BeginPerformanceSessionAsync(referenceId, "Referência de desempenho", observations[0].CollectedAt);
        for (var index = 0; index < observations.Length; index++)
            await StorePerformanceObservationAsync(referenceId, observations[index]);
        if (_persistedPerformanceSessions.Contains(referenceId))
        {
            try { await _storage.MarkPerformanceReferenceAsync(referenceId); }
            catch (Exception error) when (IsStorageError(error)) { AddPerformanceStorageWarning(error); }
        }
        await FinishPerformanceSessionAsync(referenceId);
    }

    private static string FormatBytesPerSecond(ulong? bytes) => bytes is { } value
        ? $"{ByteFormatting.Format(value)}/s" : "indisponível";

    private static string FormatBytes(ulong? bytes) => bytes is { } value ? ByteFormatting.Format(value) : "indisponível";

    private static string FormatBitsPerSecond(ulong? bits) => bits is not { } value ? "indisponível"
        : value >= 1_000_000_000 ? $"{value / 1_000_000_000d:0.#} Gbps"
        : value >= 1_000_000 ? $"{value / 1_000_000d:0.#} Mbps"
        : $"{value / 1_000d:0.#} Kbps";

    private void ComparePerformance_Click(object sender, RoutedEventArgs e)
    {
        var baselineEnd = _performanceBaseline.LastOrDefault()?.CollectedAt;
        if (_performanceBaseline.Length < 3 || baselineEnd is null) return;
        var later = _performanceHistory.Snapshot()
            .Where(entry => entry.Observation.CollectedAt > baselineEnd.Value)
            .TakeLast(5).Select(entry => entry.Observation).ToArray();
        if (later.Length < 3) return;
        _performanceComparison = PerformanceComparisonBuilder.Compare(_performanceBaseline, later);
        Notify(nameof(PerformanceComparisonSummary));
        StatusDetail = "Comparação descritiva entre médias de CPU e uso de RAM. Ela não identifica a causa das diferenças nem garante ganho de desempenho.";
    }

    private async void ScanCleanup_Click(object sender, RoutedEventArgs e)
    {
        await RunOperationAsync("Analisando temporários", "Consultando somente temporários do seu usuário, com mais de sete dias.", async token =>
        {
            _cleanupScan = await _cleanup.ScanAsync(token); CleanupFiles.Clear();
            foreach (var file in _cleanupScan.Files)
            {
                var row = new CleanupFileChoice(file.Id, file.RelativePath, file.SizeBytes, file.LastWriteTime);
                row.PropertyChanged += (_, _) => NotifyActionState(); CleanupFiles.Add(row);
            }
            CleanupSummary = $"{CleanupFiles.Count} candidato(s) em {_cleanupScan.RootPath}. A análise não excluiu arquivos. " + string.Join(" ", _cleanupScan.Warnings);
            NotifyActionState(); StatusTitle = "Análise de temporários concluída"; StatusDetail = "Selecione individualmente o que deseja guardar na recuperação antes de excluir.";
        }, cancellable: true);
    }

    private async void Quarantine_Click(object sender, RoutedEventArgs e)
    {
        if (!CanQuarantine || _cleanupScan is null) return;
        var selected = CleanupFiles.Where(f => f.IsSelected).Select(f => f.Id).ToArray();
        if (!Confirm($"Mover {CleanupSelectedText} para a recuperação do ZEUS?\n\nOs arquivos deixam a pasta temporária e podem ser restaurados. O movimento para a recuperação não libera espaço em disco. A exclusão definitiva é uma operação separada.", "Guardar antes de excluir")) return;
        await RunOperationAsync("Movendo temporários selecionados", "Os arquivos serão revalidados antes de cada movimento.", async token =>
        {
            var result = await _cleanup.QuarantineAsync(_cleanupScan, selected, token);
            CleanupSummary = $"{result.MovedFiles} arquivo(s) guardado(s) · {result.SkippedFiles} ignorado(s) · {ByteFormatting.Format(result.QuarantinedBytes)} na recuperação. Nenhum espaço foi recuperado pelo movimento. " + string.Join(" ", result.Warnings);
            _cleanupScan = null; CleanupFiles.Clear(); await RefreshCleanupSessionsAsync();
            StatusTitle = "Temporários guardados"; StatusDetail = CleanupSummary;
        }, mutation: true);
    }

    private async Task RefreshCleanupSessionsAsync()
    {
        var sessions = await _cleanup.ListSessionsAsync(); CleanupSessions.Clear();
        foreach (var s in sessions)
        {
            var recoverable = s.FileCount > 0 && s.Status is "Quarantined" or "Incomplete";
            CleanupSessions.Add(new(s.Id, s.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss"), $"{s.FileCount} arquivo(s) · {ByteFormatting.Format(s.TotalBytes)} ainda guardados · {CleanupStatus(s.Status)}", recoverable, recoverable));
        }
    }
    private static string CleanupStatus(string status) => status switch { "Quarantined" => "Guardado", "Incomplete" => "Parcial · consulte o resultado", "Deleted" => "Excluído definitivamente", "Restored" => "Restaurado", _ => status };
    private async void RestoreCleanup_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy || sender is not FrameworkElement { Tag: Guid id } || !Confirm("Restaurar os temporários desta sessão? Arquivos que já existem no destino serão preservados.", "Restaurar arquivos")) return;
        await RunOperationAsync("Restaurando temporários", "O ZEUS preserva arquivos novos existentes no destino.", async token =>
        {
            var result = await _cleanup.RestoreAsync(id, token); CleanupSummary = $"{result.RestoredFiles} restaurado(s) · {result.SkippedFiles} ignorado(s). " + string.Join(" ", result.Warnings);
            await RefreshCleanupSessionsAsync(); StatusTitle = "Restauração de arquivos encerrada"; StatusDetail = CleanupSummary;
        }, mutation: true);
    }
    private async void PurgeCleanup_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy || sender is not FrameworkElement { Tag: Guid id }) return;
        var session = (await _cleanup.ListSessionsAsync()).SingleOrDefault(s => s.Id == id);
        if (session is null || session.FileCount == 0) return;
        if (!Confirm($"Excluir definitivamente {session.FileCount} arquivo(s), total atual de {ByteFormatting.Format(session.TotalBytes)} ({session.TotalBytes:N0} bytes), guardados nesta sessão?\n\nESTA AÇÃO NÃO PODE SER DESFEITA pelo ZEUS ou por um ponto de restauração. Os arquivos não serão enviados para a Lixeira.\n\nConfirme apenas após revisar os arquivos selecionados.", "Exclusão definitiva · irreversível")) return;
        await RunOperationAsync("Excluindo recuperação selecionada", "Exclusão definitiva somente dos arquivos guardados nesta sessão.", async token =>
        {
            var deleted = await _cleanup.PermanentlyDeleteAsync(id, token); CleanupSummary = $"{ByteFormatting.Format(deleted)} ({deleted:N0} bytes) de arquivos efetivamente excluídos. O espaço livre observado pode variar com outras atividades do Windows.";
            await RefreshCleanupSessionsAsync(); StatusTitle = "Exclusão definitiva encerrada"; StatusDetail = CleanupSummary;
        }, mutation: true);
    }

    private async void ReadStartup_Click(object sender, RoutedEventArgs e) => await RunOperationAsync("Consultando inicialização", "Lendo somente HKCU Run do seu usuário.", async token => { await RefreshStartupAsync(token); StatusTitle = "Inicialização atualizada"; StatusDetail = StartupSummary; }, cancellable: true);
    private async Task RefreshStartupAsync(CancellationToken token = default)
    {
        var entries = await _userOptimization.ReadStartupAsync(token); StartupChoices.Clear();
        foreach (var entry in entries) { var choice = new StartupChoice(entry); choice.PropertyChanged += (_, _) => NotifyActionState(); StartupChoices.Add(choice); }
        StartupSummary = $"{entries.Count} entrada(s) em HKCU Run deste usuário. A quantidade não mede o impacto. Entradas protegidas não podem ser desativadas pelo ZEUS."; Notify(nameof(StartupEmptyText)); NotifyActionState();
    }
    private async void DisableStartup_Click(object sender, RoutedEventArgs e)
    {
        if (!CanDisableStartup) return;
        var entries = StartupChoices.Where(c => c.IsSelected && c.CanSelect).ToArray();
        if (!Confirm($"Desativar o início automático de:\n\n{string.Join("\n", entries.Select(e => "• " + e.Name))}\n\nOs aplicativos continuam instalados. O estado anterior ficará disponível em Histórico de alterações.", "Revisar inicialização")) return;
        await RunOperationAsync("Ajustando inicialização", "Guardando o estado anterior de cada entrada selecionada.", async token =>
        {
            var messages = new List<string>(); foreach (var entry in entries) messages.Add((await _userOptimization.DisableStartupAsync(entry.Id, token)).Message);
            await RefreshStartupAsync(token); await RefreshUserChangesAsync(); StartupSummary = string.Join(" ", messages);
            StatusTitle = "Ajustes de inicialização encerrados"; StatusDetail = StartupSummary;
        }, mutation: true);
    }
    private async Task RefreshUserChangesAsync()
    {
        var changes = await _userOptimization.ListChangesAsync(); UserChanges.Clear();
        foreach (var s in changes) UserChanges.Add(new(s.Id, s.Description, $"{s.CreatedAt.ToLocalTime():dd/MM/yyyy HH:mm:ss} · {s.StatusText}", !s.Restored));
    }
    private async void RestoreChange_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy || sender is not FrameworkElement { Tag: Guid id } || !Confirm("Restaurar o estado anterior desta alteração? O ZEUS verificará se houve mudanças posteriores antes de restaurar.", "Desfazer alteração")) return;
        await RunOperationAsync("Restaurando configuração", "Verificando o estado atual antes de restaurar.", async token =>
        {
            var result = await _userOptimization.RestoreAsync(id, token); ProfileSummary = result.Message;
            await RefreshUserChangesAsync(); await RefreshPowerPlansAsync(); await RefreshStartupAsync(token);
            StatusTitle = result.Succeeded ? "Configuração restaurada" : "Restauração não concluída"; StatusDetail = result.Message;
        }, mutation: true);
    }
    private async void ApplyPreferences_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy || !Confirm($"Aplicar suas preferências visuais ao Windows?\n\nAnimações: {(ReduceAnimations ? "reduzir" : "ativar")}\nTransparência: {(ReduceTransparency ? "reduzir" : "ativar")}\n\nO estado anterior será guardado. O Windows pode exigir novo início de sessão para refletir todos os efeitos.", "Aplicar preferências do Windows")) return;
        await RunOperationAsync("Aplicando preferências visuais", "Guardando configurações anteriores do usuário.", async token =>
        {
            var result = await _userOptimization.ApplyPreferencesAsync(new(SelectedProfile, ReduceAnimations, ReduceTransparency), token);
            await RefreshUserChangesAsync(); ProfileSummary = result.Message;
            StatusTitle = result.Succeeded ? "Preferências aplicadas" : "Preferências não concluídas"; StatusDetail = result.Message;
        }, mutation: true);
    }

    private void ChooseWallpaper_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Escolher imagem de papel de parede",
            Filter = "Imagens (BMP, JPEG, PNG)|*.bmp;*.jpg;*.jpeg;*.png",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var file = new FileInfo(dialog.FileName);
            if (file.Length is <= 0 or > 32L * 1024 * 1024) throw new InvalidDataException("Escolha uma imagem de até 32 MiB.");
            var bitmap = new BitmapImage();
            using (var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 960;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
            }
            bitmap.Freeze();
            WallpaperPreview = bitmap;
            SelectedWallpaperPath = Path.GetFullPath(file.FullName);
            ProfileSummary = $"Prévia carregada: {file.Name}. O Windows ainda não foi alterado.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or FormatException or ArgumentException or System.Windows.Markup.XamlParseException)
        {
            WallpaperPreview = null;
            SelectedWallpaperPath = null;
            ProfileSummary = $"Não foi possível abrir essa imagem: {error.Message}";
        }
    }

    private async void ApplyWallpaper_Click(object sender, RoutedEventArgs e)
    {
        if (!CanApplyWallpaper || SelectedWallpaperPath is not { } imagePath) return;
        var fileName = Path.GetFileName(imagePath);
        if (!Confirm($"Aplicar “{fileName}” como papel de parede do Windows?\n\nO ZEUS guardará uma cópia do papel de parede atual no histórico e verificará o resultado. Se o papel de parede for alterado depois fora do ZEUS, a restauração será bloqueada para preservar a escolha mais recente.", "Revisar papel de parede")) return;
        await RunOperationAsync("Aplicando papel de parede", "Guardando e verificando uma cópia local do estado atual antes de alterar o Windows.", async token =>
        {
            var result = await _userOptimization.ApplyWallpaperAsync(imagePath, token);
            await RefreshUserChangesAsync();
            ProfileSummary = result.Message;
            StatusTitle = result.Succeeded ? "Papel de parede aplicado" : "Papel de parede não alterado";
            StatusDetail = result.Message;
        }, mutation: true);
    }

    private async Task RefreshPowerPlansAsync()
    {
        var plans = await _userOptimization.ReadPowerPlansAsync(); PowerPlans.Clear(); foreach (var plan in plans) PowerPlans.Add(plan); SelectedPowerPlan = PowerPlans.FirstOrDefault(p => p.IsActive);
    }
    private async void SetPowerPlan_Click(object sender, RoutedEventArgs e)
    {
        if (!CanSetPowerPlan || SelectedPowerPlan is null) return;
        var plan = SelectedPowerPlan;
        if (!Confirm($"Ativar o plano de energia “{plan.Name}”?\n\nConsumo, autonomia e temperatura podem mudar. O plano atual será guardado para restauração. Nenhum plano é escolhido automaticamente pelo perfil.", "Alterar plano de energia")) return;
        await RunOperationAsync("Aplicando plano de energia", "Guardando a identidade do plano anteriormente ativo.", async token =>
        {
            var result = await _userOptimization.SetPowerPlanAsync(plan.Id, token); await RefreshPowerPlansAsync(); await RefreshUserChangesAsync();
            ProfileSummary = result.Message; StatusTitle = result.Succeeded ? "Plano de energia aplicado" : "Plano não alterado"; StatusDetail = result.Message;
        }, mutation: true);
    }

    private async void SearchDrivers_Click(object sender, RoutedEventArgs e)
    {
        await RunOperationAsync("Consultando drivers oficiais", "A consulta ao Windows Update pode demorar. Nenhum driver será instalado durante a busca.", async token =>
        {
            var search = await _windowsUpdate.SearchDriverUpdatesAsync(token); DriverCandidates.Clear();
            foreach (var candidate in search.Updates) { var choice = new DriverChoice(candidate); choice.PropertyChanged += (_, _) => NotifyActionState(); DriverCandidates.Add(choice); }
            DriverSummary = $"{search.Updates.Count} candidato(s) retornado(s) em {search.CheckedAt.ToLocalTime():dd/MM HH:mm:ss}. " + string.Join(" ", search.Warnings);
            StatusTitle = "Consulta de drivers encerrada"; StatusDetail = DriverSummary;
        }, cancellable: true);
    }
    private async void SearchWingetUpdates_Click(object sender, RoutedEventArgs e)
    {
        await RunOperationAsync("Consultando atualizações de programas", "Consulta somente leitura pela fonte winget; nenhuma licença será aceita e nenhum programa será instalado.", async token =>
        {
            var search = await _wingetUpdates.SearchAsync(token);
            WingetUpdates.Clear();
            foreach (var update in search.Updates)
                WingetUpdates.Add(new(update, !_wingetAuditReadable || _pendingWingetPackages.Contains(update.PackageId)));
            WingetSummary = search.IsComplete
                ? $"Consulta concluída em {search.CheckedAt.ToLocalTime():dd/MM HH:mm:ss}: {search.Updates.Count} atualização(ões). " + string.Join(" ", search.Warnings)
                : string.Join(" ", search.Warnings);
            StatusTitle = search.IsComplete ? "Consulta do WinGet concluída" : "Consulta do WinGet incompleta";
            StatusDetail = WingetSummary;
        }, cancellable: true);
    }

    private async void SearchPendingWindowsUpdates_Click(object sender, RoutedEventArgs e)
    {
        await RunOperationAsync("Consultando Windows Update", "Busca online e somente leitura pela fonte configurada no Windows. Nenhuma atualização será baixada ou instalada.", async token =>
        {
            var search = await _windowsUpdate.SearchPendingSoftwareUpdatesAsync(token);
            QueueActivity(new(DateTimeOffset.UtcNow, "windows-update", "software-search", search.IsComplete ? "info" : "warning",
                $"Busca de atualizações de software {(search.IsComplete ? "concluída" : "incompleta")}: {search.Updates.Count} item(ns)",
                JsonSerializer.Serialize(new { search.CheckedAt, search.IsComplete, count = search.Updates.Count, search.Warnings })));
            PendingWindowsUpdates.Clear();
            foreach (var update in search.Updates)
                PendingWindowsUpdates.Add(new(update.Title, update.KnowledgeBaseIds.Count == 0 ? "KB não informado" : string.Join(", ", update.KnowledgeBaseIds), update.Downloaded ? "Já baixada pelo Windows" : "Ainda não baixada", update.UpdateId));
            WindowsUpdateSummary = $"{(search.IsComplete ? "Busca concluída" : "Resultado incompleto/desconhecido")} · {search.Updates.Count} item(ns) · {search.CheckedAt.ToLocalTime():dd/MM HH:mm:ss}. " + string.Join(" ", search.Warnings);
            StatusTitle = search.IsComplete ? "Diagnóstico do Windows Update concluído" : "Diagnóstico incompleto";
            StatusDetail = WindowsUpdateSummary;
        }, cancellable: true);
    }

    private async void UpgradeWinget_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy || sender is not FrameworkElement { Tag: WingetUpdateRow row } || !row.CanInstall || !_wingetAuditReadable) return;
        var candidate = row.Candidate;
        if (!Confirm($"Atualizar somente este programa?\n\n{candidate.Name}\nID: {candidate.PackageId}\nInstalada: {candidate.InstalledVersion}\nDisponível: {candidate.AvailableVersion}\nFonte: {candidate.Source}\n\nO WinGet abrirá uma janela interativa. Leia e aceite os termos somente se concordar; o ZEUS não aceita termos automaticamente. O instalador pode pedir acesso de administrador. O ZEUS não autorizará reinicialização automática. A reversão da versão anterior depende do fornecedor e não é garantida.\n\nApós iniciar, aguarde a verificação. Se o resultado ficar desconhecido, não repita antes de conferir o programa.", "Confirmar atualização individual")) return;

        var correlationId = Guid.NewGuid().ToString("N");
        var started = new ActivityEntry(DateTimeOffset.UtcNow, "winget", "upgrade-started", "warning",
            $"Atualização individual iniciada: {candidate.PackageId}", JsonSerializer.Serialize(new { packageId = candidate.PackageId, installedVersion = candidate.InstalledVersion, targetVersion = candidate.AvailableVersion }), correlationId);
        await RunOperationAsync("Atualizando um programa", $"WinGet interativo · {candidate.PackageId}", async _ =>
        {
            if (!await PersistActivitySafelyAsync(started))
            {
                WingetSummary = "Registro local indisponível. A atualização não foi iniciada; consulte a saúde do armazenamento antes de tentar novamente.";
                return;
            }
            _pendingWingetPackages.Add(candidate.PackageId);
            _pendingWingetCorrelations[candidate.PackageId] = correlationId;
            WingetUpdates.Clear();
            WingetSummary = "Atualização iniciada. A lista foi limpa; consulte novamente após o resultado. Não feche a janela do WinGet sem ler a mensagem final.";
            var result = await _wingetUpdates.UpgradeAsync(candidate);
            var eventType = result.Succeeded ? "upgrade-completed" : "upgrade-result-unknown";
            var terminal = new ActivityEntry(DateTimeOffset.UtcNow, "winget", eventType, result.Succeeded ? "info" : "warning",
                $"Resultado WinGet: {candidate.PackageId}", JsonSerializer.Serialize(new { packageId = candidate.PackageId, targetVersion = candidate.AvailableVersion, result.ExitCode, result.ObservedInstalledVersion, result.InstalledVersionVerified, recovery = WingetUpdateTransactionResult.RecoveryClassification, message = result.Message }), correlationId);
            var persisted = await PersistActivitySafelyAsync(terminal);
            if (result.Succeeded && persisted)
            {
                _pendingWingetPackages.Remove(candidate.PackageId);
                _pendingWingetCorrelations.Remove(candidate.PackageId);
            }
            WingetSummary = result.Message + (persisted ? "" : " O resultado também não pôde ser gravado; confira manualmente antes de outra tentativa.");
            StatusTitle = result.Succeeded ? "Atualização verificada" : "Resultado da atualização não confirmado";
            StatusDetail = WingetSummary;
            WingetUpdates.Clear();
        }, mutation: true);
    }

    private async void ClearWingetReview_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy || sender is not FrameworkElement { Tag: WingetUpdateRow row } || !_pendingWingetPackages.Contains(row.PackageId)) return;
        if (!Confirm($"Você conferiu manualmente que {row.Name} está funcionando e verificou a versão instalada e o histórico do WinGet?\n\nEsta confirmação apenas libera uma nova consulta; não reverte nem altera o programa. A próxima atualização ainda exigirá confirmação separada.", "Registrar revisão manual")) return;
        var correlationId = _pendingWingetCorrelations.GetValueOrDefault(row.PackageId) ?? Guid.NewGuid().ToString("N");
        var entry = new ActivityEntry(DateTimeOffset.UtcNow, "winget", "upgrade-manual-review-cleared", "info",
            $"Revisão manual confirmada: {row.PackageId}", JsonSerializer.Serialize(new { packageId = row.PackageId, reviewedInstalledVersion = row.InstalledVersion }), correlationId);
        if (!await PersistActivitySafelyAsync(entry)) return;
        _pendingWingetPackages.Remove(row.PackageId);
        _pendingWingetCorrelations.Remove(row.PackageId);
        WingetUpdates.Clear();
        WingetSummary = "Revisão manual registrada. Faça uma nova consulta para carregar versões atuais antes de qualquer atualização.";
    }

    private async void InstallDriver_Click(object sender, RoutedEventArgs e)
    {
        if (!CanInstallDriver) return;
        var selected = DriverCandidates.Where(d => d.IsSelected).ToArray();
        if (!Confirm($"Instalar os candidatos selecionados?\n\n{string.Join("\n\n", selected.Select(d => $"• {d.Title}\nDispositivo: {Available(d.DeviceName)} · Fabricante: {Available(d.Manufacturer)}\nVersão: {Available(d.DriverVersion)}\nIdentidade: {d.Id}"))}\n\nConfirme a indicação para cada atualização. Pode haver reinicialização e incompatibilidade; o auxiliar exigirá proteção e exportará os drivers atuais antes do lote.", "Revisar candidatos de drivers")) return;
        await ReviewAndExecuteAsync(selected.Select(d => new MaintenanceRequest(MaintenanceActionId.InstallDriverUpdate, d.Id, d.RequiresEula && d.EulaAccepted)).ToArray());
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (!CanExport) return;
        var dialog = new SaveFileDialog { Title = "Exportar relatório do ZEUS", Filter = "Relatório JSON (*.json)|*.json", FileName = $"zeus-relatorio-{DateTime.Now:yyyyMMdd-HHmmss}.json", DefaultExt = ".json", AddExtension = true, OverwritePrompt = true };
        if (dialog.ShowDialog(this) != true) return;
        await RunOperationAsync("Exportando relatório", "Guardando inventário, observações e resultados reais.", async _ =>
        {
            await DesktopStorage.ExportAsync(dialog.FileName, new(4, DateTimeOffset.UtcNow, _snapshot, _reports, _performance, Recommendations.ToArray(), new(SelectedProfile, ReduceAnimations, ReduceTransparency), UserChanges.ToArray(), CleanupSessions.ToArray(), _performanceHistory.Snapshot(), _performanceBaseline, _performanceComparison, _optimizationPlan));
            StatusTitle = "Relatório exportado"; StatusDetail = "O JSON contém nomes de computador, usuários e processos. Revise essas informações antes de compartilhar.";
        });
    }

    private async void BackupDatabase_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Criar cópia de segurança dos dados do ZEUS",
            Filter = "Banco SQLite do ZEUS (*.db)|*.db",
            FileName = $"zeus-backup-{DateTime.Now:yyyyMMdd-HHmmss}.db",
            DefaultExt = ".db",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true) return;

        await RunOperationAsync("Criando cópia de segurança", "O SQLite copia o banco em uso e verifica a integridade antes de gravar o destino escolhido.", async token =>
        {
            await _storage.BackupDatabaseAsync(dialog.FileName, token);
            StatusTitle = "Cópia de segurança verificada";
            StatusDetail = $"Banco SQLite salvo em {dialog.FileName}. O arquivo contém preferências e histórico local; guarde-o como dado pessoal.";
        }, cancellable: true);
    }

    private async void RestoreDatabase_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Escolher cópia de segurança do ZEUS",
            Filter = "Banco SQLite (*.db)|*.db|Todos os arquivos (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;
        if (!Confirm(
            $"Restaurar esta cópia substituirá as preferências e o histórico local atuais.\n\nArquivo: {dialog.FileName}\n\nAntes da troca, o ZEUS criará e verificará uma cópia de segurança do banco atual na pasta local do aplicativo. Após a restauração, o ZEUS será reiniciado para carregar os dados recuperados.\n\nContinuar?",
            "Confirmar restauração dos dados")) return;

        var restored = false;
        var safetyCopy = string.Empty;
        await RunOperationAsync("Restaurando dados do ZEUS", "Validando a cópia escolhida e protegendo o banco atual antes de substituir os dados.", async token =>
        {
            await DrainLocalWritesAsync(token);
            safetyCopy = await _storage.RestoreDatabaseAsync(dialog.FileName, token);
            restored = true;
            StatusTitle = "Dados restaurados";
            StatusDetail = $"Cópia de segurança do estado anterior: {safetyCopy}. Reiniciando o ZEUS para recarregar as preferências e o histórico.";
        }, cancellable: true, mutation: true);

        if (!restored) return;
        try
        {
            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable)) throw new InvalidOperationException("O caminho do executável atual não está disponível.");
            _ = Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true })
                ?? throw new InvalidOperationException("O Windows não iniciou a nova janela do ZEUS.");
            Application.Current.Shutdown();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            StatusTitle = "Dados restaurados; reinício manual necessário";
            StatusDetail = $"Feche e abra o ZEUS para carregar a cópia. O estado anterior continua guardado em {safetyCopy}. Detalhe: {error.Message}";
        }
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: Guid id }) return;
        try { Process.Start(new ProcessStartInfo(SessionStore.GetSessionDirectory(id)) { UseShellExecute = true }); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception or ArgumentException) { StatusDetail = $"A pasta de logs não pôde ser aberta: {error.Message}"; }
    }
    private void OpenWindowsUpdate_Click(object sender, RoutedEventArgs e) => OpenTrustedUri("ms-settings:windowsupdate");
    private void OpenWindowsSecurity_Click(object sender, RoutedEventArgs e) => OpenTrustedUri("windowsdefender:");
    private void OpenVendorSupport_Click(object sender, RoutedEventArgs e) => OpenTrustedUri("https://support.microsoft.com/windows/update-drivers-through-device-manager-in-windows-ec62f46c-ff14-c91d-eead-d7126dc1f7b6");
    private void OpenTrustedUri(string uri)
    {
        if (_isBusy) return;
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException) { StatusDetail = $"O Windows não conseguiu abrir este destino: {error.Message}"; }
    }
    private bool Confirm(string text, string title) => MessageBox.Show(this, text, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
}
