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
    private async void CheckZeusUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;
        ZeusReleaseSummary = "Consultando a publicação oficial do ZEUS no GitHub…";
        await RunOperationAsync("Consultando atualização do ZEUS", "Somente leitura. Nenhum arquivo será baixado ou instalado.", async token =>
        {
            try
            {
                var result = await _zeusReleaseChecker.CheckAsync(BuildVersion, token);
                ZeusReleaseSummary = result.Summary;
                StatusTitle = "Consulta de versão do ZEUS concluída";
                StatusDetail = result.Summary;
                if (result.ReleasePage is not null && Confirm($"{result.Summary}\n\nAbrir a página oficial de versões do ZEUS no navegador? Nenhum instalador será baixado automaticamente.", "Versões do ZEUS"))
                    _openUri(result.ReleasePage.AbsoluteUri);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                ZeusReleaseSummary = "Consulta de versão cancelada.";
            }
            catch (Exception error)
            {
                ZeusReleaseSummary = $"Não foi possível confirmar a versão mais recente: {error.Message}";
                StatusTitle = "Consulta de versão inconclusiva";
                StatusDetail = ZeusReleaseSummary;
            }
        });
    }

    private void AnalyzeServiceDependencies_Click(object sender, RoutedEventArgs e)
    {
        var inventory = _snapshot?.WindowsInventory;
        if (inventory is null)
        {
            ServiceDependencySummary = "Inventário de serviços indisponível; atualize o diagnóstico.";
            ServiceDependencyRows.Clear();
            Notify(nameof(ServiceDependencySummary));
            return;
        }

        var sourceComplete = !inventory.Warnings.Any(warning => warning.StartsWith("Serviços:", StringComparison.OrdinalIgnoreCase));
        var report = ServiceDependencyAnalyzer.Analyze(inventory.Services, sourceComplete);
        ServiceDependencyRows.Clear();
        foreach (var finding in report.Findings.Take(100)) ServiceDependencyRows.Add(new(finding.Name, finding.Detail));
        ServiceDependencySummary = report.Findings.Count > 100
            ? $"{report.Summary} Exibindo os primeiros 100 itens."
            : report.Summary;
        Notify(nameof(ServiceDependencySummary));
    }

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
        if (ordered.Any(r => r.Action == MaintenanceActionId.RollbackDriver)) text.Append($"Dispositivo: {ordered.Single().TargetId}\nO ZEUS exportará o pacote atualmente instalado antes de pedir ao Windows a reversão deste único dispositivo. O Windows pode não manter uma versão anterior; nesse caso, nada será alterado. A reinicialização, se solicitada, será manual. Confira se o backup exportado está preservado.\n\n");
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
            var presentation = MaintenanceResultPresentation.From(report, ordered);
            MaintenanceResultSummary = presentation.Detail;
            StatusTitle = presentation.Title;
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
            return presentation.IsSuccessful;
    }

    private async void OfflineScan_Click(object sender, RoutedEventArgs e)
    {
        if (CanOfflineScan) await ReviewAndExecuteAsync([new(MaintenanceActionId.DefenderOfflineScan)]);
    }

    private async void Performance_Click(object sender, RoutedEventArgs e)
    {
        var sessionLabel = BuildPerformanceSessionLabel("Medição manual", PerformanceActivityLabel);
        await RunOperationAsync("Medindo carga real", "Amostrando CPU, memória e processos por cinco segundos.", async token =>
        {
            _performanceSessionId = Guid.NewGuid();
            var id = _performanceSessionId;
            var started = DateTimeOffset.UtcNow;
            await BeginPerformanceSessionAsync(id, sessionLabel, started);
            try
            {
                var observation = await _performanceProbe.SampleAsync(TimeSpan.FromSeconds(5), token);
                DisplayPerformanceObservation(observation);
                await StorePerformanceObservationAsync(id, observation);
                BuildPersonalPlan(); Notify(nameof(Performance));
                StatusTitle = "Medição concluída"; StatusDetail = $"Sessão “{sessionLabel}” concluída. A descrição foi informada por você; o rótulo não confirma se um jogo ou transmissão estava ativo. Confira em Sessões recentes se o histórico local foi gravado e repita a mesma atividade em condições semelhantes para comparar.";
            }
            finally { await FinishPerformanceSessionAsync(id); await RefreshPerformanceSessionHistoryAsync(); }
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
            var attempts = result.AttemptCount;
            var timeoutRate = result.TimeoutPercent is { } percent ? $" ({percent:0.#}%)" : string.Empty;
            NetworkProbeSummary = $"{result.Target} → {result.Address} · respostas: {result.Replies}/{attempts} · timeouts observados: {result.NoReplies}/{attempts}{timeoutRate} · latência ICMP mín/média/máx: {min}/{average}/{max}" +
                (statuses.Length == 0 ? string.Empty : $" · outros resultados: {string.Join(", ", statuses)}") +
                $" · {result.CheckedAt.ToLocalTime():dd/MM HH:mm:ss}";
            var correlation = Guid.NewGuid().ToString("N");
            var details = JsonSerializer.Serialize(new { result.Target, result.Address, result.CheckedAt, result.Samples, result.AttemptCount, result.Replies, result.NoReplies, result.TimeoutPercent, result.MinimumMilliseconds, result.AverageMilliseconds, result.MaximumMilliseconds });
            QueueActivity(new ActivityEntry(result.CheckedAt, "network-diagnostic", "icmp-measurement-completed",
                result.Replies == 0 ? "warning" : "info", NetworkProbeSummary, details, correlation));
            StatusTitle = "Medição ICMP concluída";
            StatusDetail = "O resultado descreve somente respostas ICMP deste destino e horário. Consulte o aviso de interpretação na tela.";
        }, cancellable: true);
    }

    private async void StartObserver_Click(object sender, RoutedEventArgs e)
    {
        var sessionLabel = BuildPerformanceSessionLabel("Observador adaptativo", PerformanceActivityLabel);
        _performanceSessionId = Guid.NewGuid();
        await RunOperationAsync("Observando desempenho", "Amostras adaptativas de CPU, memória e processos. Use Cancelar leitura para encerrar.", async token =>
        {
            var id = _performanceSessionId;
            await BeginPerformanceSessionAsync(id, sessionLabel, DateTimeOffset.UtcNow);
            try
            {
                while (true)
                {
                    if (_performanceSessionSequences.GetValueOrDefault(id) >= PerformanceHistoryBuffer.DefaultCapacity)
                    {
                        await FinishPerformanceSessionAsync(id);
                        id = _performanceSessionId = Guid.NewGuid();
                        await BeginPerformanceSessionAsync(id, sessionLabel, DateTimeOffset.UtcNow);
                    }
                    var observation = await _performanceProbe.SampleAsync(TimeSpan.FromSeconds(2), token);
                    DisplayPerformanceObservation(observation);
                    await StorePerformanceObservationAsync(id, observation);
                    BuildPersonalPlan();
                    var interval = AdaptiveSamplingPolicy.NextInterval(observation);
                    PerformanceSummary += $" · próxima amostra em {interval.TotalSeconds:0} s";
                    StatusDetail = $"Sessão “{sessionLabel}” · {_performanceHistory.Snapshot().Count} amostras guardadas em memória. O rótulo é informado por você; não confirma jogo, OBS ou transmissão. Cancele para encerrar.";
                    await Task.Delay(interval, token);
                }
            }
            finally { await FinishPerformanceSessionAsync(id); await RefreshPerformanceSessionHistoryAsync(); }
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

    internal static string BuildPerformanceSessionLabel(string mode, string? activity)
    {
        var normalized = string.Join(' ', (activity ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length > 80) normalized = normalized[..80].TrimEnd();
        return normalized.Length == 0 ? mode : $"{mode} · {normalized}";
    }

    private async Task RefreshPerformanceSessionHistoryAsync()
    {
        try
        {
            var sessions = await _storage.ReadPerformanceSessionsAsync();
            _performanceSessionExports = sessions.OrderByDescending(session => session.StartedAt)
                .Select(session => new PerformanceSessionExport(session.Label, session.StartedAt, session.FinishedAt,
                    session.IsReference, session.Samples.Count)).ToArray();
            var recent = sessions.OrderByDescending(session => session.StartedAt).Take(5).ToArray();
            PerformanceSessionHistorySummary = recent.Length == 0
                ? "Nenhuma sessão de desempenho foi salva ainda."
                : string.Join(Environment.NewLine, recent.Select(session =>
                    $"{session.StartedAt.ToLocalTime():dd/MM HH:mm} · {session.Label} · {session.Samples.Count} amostras"));
        }
        catch (Exception error) when (IsStorageError(error))
        {
            PerformanceSessionHistorySummary = "Histórico de sessões indisponível nesta leitura; os registros locais foram preservados.";
            AddPerformanceStorageWarning(error);
        }
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
        _performanceSessionExports = sessions.OrderByDescending(session => session.StartedAt)
            .Select(session => new PerformanceSessionExport(session.Label, session.StartedAt, session.FinishedAt,
                session.IsReference, session.Samples.Count)).ToArray();
        var recentSessions = sessions.OrderByDescending(session => session.StartedAt).Take(5).ToArray();
        PerformanceSessionHistorySummary = recentSessions.Length == 0
            ? "Nenhuma sessão de desempenho foi salva ainda."
            : string.Join(Environment.NewLine, recentSessions.Select(session =>
                $"{session.StartedAt.ToLocalTime():dd/MM HH:mm} · {session.Label} · {session.Samples.Count} amostras"));
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
        PerformanceSummary = $"CPU: {(observation.CpuPercent.HasValue ? $"{observation.CpuPercent:0.#}%" : "indisponível")} · RAM disponível: {memory} · {observation.ActivityContext?.Summary ?? "Contexto de jogo/OBS indisponível."} · Intervalo medido da CPU: {observation.SamplingDuration.TotalSeconds:0.#} s; GPU, disco e rede são leituras ao final · {observation.CollectedAt.ToLocalTime():dd/MM HH:mm:ss}";
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
        foreach (var gpuMemory in (observation.GpuProcessMemory ?? [])
                     .OrderByDescending(memory => memory.DedicatedUsageBytes ?? 0)
                     .ThenByDescending(memory => memory.SharedUsageBytes ?? 0).Take(20))
        {
            var process = gpuMemory.ProcessName is { Length: > 0 } name ? $"{name} · PID {gpuMemory.ProcessId}" : $"PID {gpuMemory.ProcessId} · nome não mapeado";
            PerformanceResourceRows.Add(new($"Memória GPU · {gpuMemory.AdapterInstance} · {process}",
                $"Alocações reportadas: dedicada {FormatBytes(gpuMemory.DedicatedUsageBytes)} · compartilhada {FormatBytes(gpuMemory.SharedUsageBytes)} · local {FormatBytes(gpuMemory.LocalUsageBytes)} · não local {FormatBytes(gpuMemory.NonLocalUsageBytes)} · comprometida {FormatBytes(gpuMemory.TotalCommittedBytes)}. Isso não informa o orçamento do processo nem confirma pressão."));
        }
        if (observation.MemoryPaging is { } paging)
            PerformanceResourceRows.Add(new("RAM e paginação",
                $"Memória comprometida: {FormatBytes(paging.CommittedBytes)} de {FormatBytes(paging.CommitLimitBytes)} ({FormatMetric(paging.CommitPercent)}) · leituras de páginas: {FormatMetric(paging.PageReadsPerSecond)}/s · páginas lidas: {FormatMetric(paging.PagesInputPerSecond)}/s. Hard faults podem vir de executáveis, DLLs ou arquivos mapeados; não provam falta de RAM."));
        var sessionSamples = _performanceHistory.Snapshot().Where(entry => entry.SessionId == _performanceSessionId)
            .TakeLast(20).Select(entry => entry.Observation).ToArray();
        foreach (var assessment in GpuMemoryOccupancyAnalyzer.Assess(sessionSamples))
            PerformanceResourceRows.Add(new($"Sinal de ocupação GPU · {assessment.AdapterInstance}", FormatGpuOccupancyAssessment(assessment)));
        foreach (var disk in observation.Disks ?? [])
            PerformanceResourceRows.Add(new($"Disco · {disk.InstanceName}", $"Transferência: {FormatBytesPerSecond(disk.BytesPerSecond)} · ativo: {(disk.ActivePercent is { } active ? $"{active:0.#}%" : "indisponível")} · leitura: {(disk.AverageReadLatencyMilliseconds is { } latency ? $"{latency:0.##} ms" : "indisponível")}"));
        foreach (var network in observation.Networks ?? [])
            PerformanceResourceRows.Add(new($"Rede · {network.Adapter}", $"Tráfego: {FormatBytesPerSecond(network.BytesPerSecond)} · enlace reportado: {FormatBitsPerSecond(network.LinkBitsPerSecond)} · erros acumulados: {network.ErrorPackets?.ToString() ?? "indisponível"}"));
        var memoryAssessment = MemoryPressureAnalyzer.Assess(_performanceHistory.Snapshot()
            .Where(entry => entry.SessionId == _performanceSessionId).Select(entry => entry.Observation));
        PerformanceResourceRows.Add(new("Sinal de RAM", FormatMemoryPressureAssessment(memoryAssessment)));
        foreach (var warning in observation.Warnings)
            if (!Warnings.Contains(warning)) Warnings.Add(warning);
        Notify(nameof(Performance)); Notify(nameof(CanSetPerformanceBaseline)); Notify(nameof(CanComparePerformance)); Notify(nameof(PerformanceComparisonSummary));
    }

    private static string FormatGpuOccupancyAssessment(GpuMemoryOccupancyAssessment assessment) => assessment.State switch
    {
        GpuMemoryOccupancyState.InsufficientEvidence => $"Evidência insuficiente: {assessment.ValidSamples} leitura(s) válida(s) em {assessment.Window.TotalSeconds:0.#} s; exigidos {GpuMemoryOccupancyAnalyzer.MinimumSamples} leituras e pelo menos {GpuMemoryOccupancyAnalyzer.MinimumWindow.TotalSeconds:0} s.",
        GpuMemoryOccupancyState.SustainedHighOccupancy => $"Ocupação dedicada ≥{GpuMemoryOccupancyAnalyzer.HighOccupancyThresholdPercent:0}% em {assessment.HighOccupancySamples}/{assessment.ValidSamples} leituras; média {assessment.AverageOccupancyPercent:0.#}%. Sinal para investigar. Pressão de VRAM permanece desconhecida: há alocações agregadas e por processo, mas sem orçamento por processo ou evidência de paginação.",
        _ => $"Sem ocupação dedicada ≥{GpuMemoryOccupancyAnalyzer.HighOccupancyThresholdPercent:0}% sustentada nesta janela ({assessment.ValidSamples} leituras). Pressão de VRAM permanece desconhecida: não há orçamento por processo nem evidência de paginação."
    };

    private static string FormatMemoryPressureAssessment(MemoryPressureAssessment assessment) => assessment.State switch
    {
        MemoryPressureSignalState.InsufficientEvidence => $"Evidência insuficiente: {assessment.ValidSamples} amostra(s) válida(s) em {assessment.Window.TotalSeconds:0.#} s; exigidos {MemoryPressureAnalyzer.MinimumSamples} amostras e pelo menos {MemoryPressureAnalyzer.MinimumWindow.TotalSeconds:0} s.",
        MemoryPressureSignalState.SustainedLowMemoryWithPageReads => $"Memória disponível ≤{MemoryPressureAnalyzer.LowAvailableThresholdPercent:0}% em {assessment.LowAvailableSamples}/{assessment.ValidSamples} amostras e Page Reads/sec > 0 em {assessment.PageReadSamples}/{assessment.ValidSamples}; sinal para revisar a tarefa. Isso não prova falta de RAM; hard faults podem ler executáveis, DLLs e arquivos mapeados.",
        _ => $"Sem sinal combinado sustentado nesta janela: pouca memória disponível em {assessment.LowAvailableSamples}/{assessment.ValidSamples} amostras e leituras de páginas em {assessment.PageReadSamples}/{assessment.ValidSamples}."
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
        var referenceLabel = BuildPerformanceSessionLabel("Referência de desempenho", PerformanceActivityLabel);
        await BeginPerformanceSessionAsync(referenceId, referenceLabel, observations[0].CollectedAt);
        for (var index = 0; index < observations.Length; index++)
            await StorePerformanceObservationAsync(referenceId, observations[index]);
        if (_persistedPerformanceSessions.Contains(referenceId))
        {
            try { await _storage.MarkPerformanceReferenceAsync(referenceId); }
            catch (Exception error) when (IsStorageError(error)) { AddPerformanceStorageWarning(error); }
        }
        await FinishPerformanceSessionAsync(referenceId);
        await RefreshPerformanceSessionHistoryAsync();
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

    private async void CompleteFirstRunSetup_Click(object sender, RoutedEventArgs e)
    {
        if (!_loaded || FirstRunSetupComplete) return;
        await _preferenceLock.WaitAsync();
        try
        {
            await _storage.SavePreferencesAsync(CurrentPreferences() with { FirstRunSetupComplete = true });
            FirstRunSetupComplete = true;
            StatusTitle = "Configuração inicial salva";
            StatusDetail = "Seu perfil foi salvo. O ZEUS continua sem aplicar alterações ao Windows sem sua revisão.";
        }
        catch (Exception error) when (IsStorageError(error))
        {
            StatusDetail = $"A configuração inicial não foi marcada como concluída porque o armazenamento falhou: {error.Message}";
        }
        finally { _preferenceLock.Release(); }
    }

    private async Task RefreshDesktopOrganizationSessionsAsync(CancellationToken cancellationToken = default)
    {
        var sessions = await _desktopOrganizer.ListSessionsAsync(cancellationToken);
        DesktopOrganizationSessions.Clear();
        foreach (var session in sessions) DesktopOrganizationSessions.Add(session);
    }

    private async void PreviewDesktopOrganization_Click(object sender, RoutedEventArgs e)
    {
        DesktopOrganizationPreview = null;
        DesktopOrganizationSummary = "Analisando a Área de Trabalho. Nenhum arquivo foi movido.";
        await RunOperationAsync("Analisando a Área de Trabalho", "Somente leitura: preparando uma lista dos arquivos elegíveis e dos itens preservados.", async token =>
        {
            DesktopOrganizationPreview = await _desktopOrganizer.PreviewAsync(token);
            var preview = DesktopOrganizationPreview;
            if (preview is null) DesktopOrganizationSummary = "A prévia não foi produzida.";
            else
            {
                var counts = preview.Items.GroupBy(item => item.Category).Select(group => $"{group.Key}: {group.Count()}");
                var skipped = preview.Skipped.Take(10).Select(reason => "• " + reason).ToArray();
                var remaining = preview.Skipped.Count - skipped.Length;
                var details = skipped.Length == 0 ? "" : Environment.NewLine + "Preservados:" + Environment.NewLine + string.Join(Environment.NewLine, skipped) +
                    (remaining > 0 ? Environment.NewLine + $"• e mais {remaining} item(ns) preservados" : "");
                DesktopOrganizationSummary = preview.Items.Count == 0
                    ? $"Nenhum arquivo elegível encontrado. {preview.Skipped.Count} item(ns) foram preservados.{details}"
                    : $"{preview.Items.Count} arquivo(s) · {FormatBytes((ulong)Math.Max(0, preview.TotalBytes))} · destinos: {string.Join(", ", counts)}.{details}";
            }
            StatusTitle = "Prévia da Área de Trabalho pronta";
            StatusDetail = DesktopOrganizationSummary;
        }, cancellable: true);
        if (DesktopOrganizationPreview is null && DesktopOrganizationSummary.StartsWith("Analisando", StringComparison.Ordinal))
            DesktopOrganizationSummary = $"{StatusTitle}: {StatusDetail} Nenhum arquivo foi movido.";
    }

    private async void ApplyDesktopOrganization_Click(object sender, RoutedEventArgs e)
    {
        if (!CanApplyDesktopOrganization || DesktopOrganizationPreview is not { } preview) return;
        var names = string.Join("\n", preview.Items.Take(20).Select(item => $"• {item.Name} → ZEUS - {item.Category}"));
        if (preview.Items.Count > 20) names += $"\n• e mais {preview.Items.Count - 20} arquivo(s)";
        if (!Confirm($"Organizar {preview.Items.Count} arquivo(s) da Área de Trabalho em pastas por categoria?\n\n{names}\n\nPastas, atalhos e tipos não reconhecidos não serão movidos. Cada arquivo será verificado contra a prévia e registrado antes da mudança. Se algo mudar, o ZEUS preservará o conflito sem sobrescrever. Você poderá restaurar pelo histórico.", "Revisar organização da Área de Trabalho")) return;
        DesktopOrganizationPreview = null;
        var completed = false;
        await RunOperationAsync("Organizando a Área de Trabalho", "Verificando cada arquivo e registrando a sessão reversível antes dos movimentos.", async token =>
        {
            try
            {
                var result = await _desktopOrganizer.ApplyAsync(preview, token);
                completed = true;
                DesktopOrganizationSummary = result.Message;
                StatusTitle = result.Succeeded ? "Organização concluída" : "Organização não concluída";
                StatusDetail = result.Message;
            }
            finally { await RefreshDesktopOrganizationSessionsAsync(CancellationToken.None); }
        }, mutation: true);
        if (!completed) DesktopOrganizationSummary = "Organização interrompida ou não confirmada. Consulte Estados guardados antes de tentar novamente; nenhum arquivo será sobrescrito.";
    }

    private async void RestoreDesktopOrganization_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy || sender is not FrameworkElement { Tag: Guid id }) return;
        var session = DesktopOrganizationSessions.FirstOrDefault(item => item.Id == id);
        if (session is null || !session.CanRestore) return;
        if (!Confirm($"Restaurar os arquivos desta sessão para a Área de Trabalho? O ZEUS verificará o hash de cada arquivo e não substituirá arquivos que já existam no local original.", "Revisar restauração")) return;
        var completed = false;
        await RunOperationAsync("Restaurando arquivos da Área de Trabalho", "Validando conteúdo, destino e conflitos antes de cada movimento.", async token =>
        {
            try
            {
                var result = await _desktopOrganizer.RestoreAsync(id, token);
                completed = true;
                DesktopOrganizationSummary = result.Message;
                StatusTitle = result.Succeeded ? "Restauração verificada" : "Restauração precisa de revisão";
                StatusDetail = result.Message;
            }
            finally { await RefreshDesktopOrganizationSessionsAsync(CancellationToken.None); }
        }, mutation: true);
        if (!completed) DesktopOrganizationSummary = "Restauração interrompida ou não confirmada. Confira Estados guardados antes de repetir; arquivos modificados foram preservados.";
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
        if (!Confirm($"Aplicar “{fileName}” a todos os monitores conectados?\n\nO ZEUS guardará a imagem anterior de cada monitor no histórico e verificará o resultado. Apresentações de slides ficam intactas. Se algum monitor ou papel de parede mudar depois fora do ZEUS, a restauração será bloqueada para preservar a escolha mais recente.", "Revisar papel de parede")) return;
        await RunOperationAsync("Aplicando papel de parede", "Guardando e verificando o estado por monitor antes de alterar o Windows.", async token =>
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

    private async void VerifyPendingDriverUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (!CanVerifyPendingDriverUpdates) return;
        var pending = _reports.SelectMany((report, reportIndex) => report.Steps.Select((step, stepIndex) =>
                (ReportIndex: reportIndex, StepIndex: stepIndex, SessionId: report.SessionId,
                    InstallationFinishedAt: report.FinishedAt, Step: step)))
            .Where(item => item.Step.Action == MaintenanceActionId.InstallDriverUpdate &&
                item.Step.Verification == MaintenanceVerificationStatus.Pending && item.Step.UpdateServerSelection is not null)
            .ToArray();
        if (pending.Length == 0) return;

        await RunOperationAsync("Verificando instalações pendentes", "Consulta somente leitura do pacote exato na seleção lógica de origem registrada; nenhuma instalação ou reinicialização será solicitada.", async token =>
        {
            var confirmed = 0;
            var stillPending = 0;
            var inconclusive = 0;
            var updatedReports = _reports.ToArray();
            foreach (var item in pending)
            {
                token.ThrowIfCancellationRequested();
                var result = await _windowsUpdate.VerifyInstalledDriverUpdateAsync(item.Step.TargetId!,
                    item.Step.UpdateServerSelection, item.Step.UpdateServiceId, item.InstallationFinishedAt, token);
                foreach (var warning in result.Warnings) AppendLog(warning);
                var decision = DriverInstallVerificationPolicy.ResolvePostRestart(result.IsComplete, result.SourceMatches == true, result.IsInstalled);

                if (decision.Verification != MaintenanceVerificationStatus.ProviderConfirmed)
                {
                    if (result.IsComplete && result.SourceMatches == true && result.IsInstalled == false) stillPending++;
                    else inconclusive++;
                    QueueActivity(new(result.CheckedAt, "windows-update-driver", "post-restart-verification-incomplete", "warning",
                        decision.Message,
                        JsonSerializer.Serialize(new { item.Step.TargetId, item.Step.UpdateServerSelection, item.Step.UpdateServiceId, result.SourceMatches, result.Warnings }),
                        item.SessionId.ToString("D")));
                    AppendLog(decision.Message);
                    continue;
                }

                var steps = updatedReports[item.ReportIndex].Steps.ToArray();
                steps[item.StepIndex] = item.Step with
                {
                    Outcome = decision.Outcome,
                    Verification = decision.Verification,
                    Message = item.Step.Message + $" Reconsulta somente leitura em {result.CheckedAt.ToLocalTime():dd/MM/yyyy HH:mm:ss}: {decision.Message} A seleção padrão não revela o servidor efetivo."
                };
                updatedReports[item.ReportIndex] = updatedReports[item.ReportIndex] with { Steps = steps };
                confirmed++;
                QueueActivity(new(result.CheckedAt, "windows-update-driver", "post-restart-package-confirmed", "info",
                    decision.Message,
                    JsonSerializer.Serialize(new { item.Step.TargetId, item.Step.UpdateServerSelection, item.Step.UpdateServiceId, result.CheckedAt }),
                    item.SessionId.ToString("D")));
            }

            if (confirmed > 0)
            {
                await _storage.SaveHistoryAsync(updatedReports);
                _reports.Clear();
                _reports.AddRange(updatedReports);
                RebuildHistory();
            }
            DriverSummary = $"Verificação encerrada: {confirmed} pacote(s) confirmado(s) pelo Windows Update · {stillPending} ainda não confirmado(s) · {inconclusive} consulta(s) inconclusiva(s). O driver ativo no dispositivo precisa ser conferido separadamente.";
            StatusTitle = inconclusive > 0 || stillPending > 0 ? "Verificação encerrada com pendências" : "Pacotes confirmados pelo Windows Update";
            StatusDetail = DriverSummary;
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
        var candidate = selected[0];
        if (!Confirm($"Instalar este candidato de driver?\n\n{candidate.Title}\nDispositivo: {Available(candidate.DeviceName)} · Fabricante: {Available(candidate.Manufacturer)}\nFornecedor declarado: {candidate.DriverProvider} · Categoria inferida: {candidate.ProviderCategory}\nClasse: {candidate.DriverClass} · Data do driver: {candidate.DriverDate}\nVersão: {Available(candidate.DriverVersion)}\nOrigem: {candidate.DriverSource}\nIdentidade: {candidate.Id}\n\n{candidate.PackageIntegritySummary} Pode haver reinicialização e incompatibilidade; o auxiliar exigirá proteção e exportará os drivers atuais antes desta instalação.", "Revisar candidato de driver")) return;
        await ReviewAndExecuteAsync([new MaintenanceRequest(MaintenanceActionId.InstallDriverUpdate, candidate.Id,
            candidate.RequiresEula && candidate.EulaAccepted, candidate.Candidate.UpdateServerSelection, candidate.Candidate.UpdateServiceId)]);
    }

    private async void RollbackDriver_Click(object sender, RoutedEventArgs e)
    {
        if (!CanRollbackDriver || SelectedRollbackDriver is null) return;
        var device = SelectedRollbackDriver.Device;
        if (!Confirm($"Solicitar ao Windows a reversão do driver deste dispositivo?\n\n{device.Name}\nClasse: {device.Class}\nIdentidade PnP: {device.InstanceId}\n\nO ZEUS exportará primeiro o pacote atual. A reversão depende da cópia anterior mantida pelo Windows e pode falhar sem alterar nada. Se houver solicitação de reinicialização, ela será manual. Revise o resultado e o log antes de qualquer nova tentativa.", "Revisar reversão de driver")) return;
        var priorReportCount = _reports.Count;
        await ReviewAndExecuteAsync([new MaintenanceRequest(MaintenanceActionId.RollbackDriver, device.InstanceId)]);
        if (_reports.Count > priorReportCount) await RefreshDiagnosticsAsync();
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (!CanExport) return;
        var dialog = new SaveFileDialog { Title = "Exportar relatório do ZEUS", Filter = "Relatório JSON (*.json)|*.json", FileName = $"zeus-relatorio-{DateTime.Now:yyyyMMdd-HHmmss}.json", DefaultExt = ".json", AddExtension = true, OverwritePrompt = true };
        if (dialog.ShowDialog(this) != true) return;
        await RunOperationAsync("Exportando relatório", "Guardando inventário, observações e resultados reais.", async _ =>
        {
            await DesktopStorage.ExportAsync(dialog.FileName, CreateExportDocument());
            StatusTitle = "Relatório exportado"; StatusDetail = "O JSON contém nomes de computador, usuários e processos. Revise essas informações antes de compartilhar.";
        });
    }

    private async void ExportDiagnosticPackage_Click(object sender, RoutedEventArgs e)
    {
        if (!CanExport) return;
        var dialog = new SaveFileDialog
        {
            Title = "Salvar pacote de diagnóstico do ZEUS",
            Filter = "Pacote de diagnóstico ZIP (*.zip)|*.zip",
            FileName = $"zeus-diagnostico-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
            DefaultExt = ".zip",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true) return;
        if (!Confirm(
            "O pacote contém o relatório local de diagnóstico, manutenção, limpeza e desempenho, quando disponíveis. Ele pode incluir nomes de computador, dispositivos, programas, processos, serviços, eventos, endereços e configurações de rede.\n\nO ZEUS não inclui o banco SQLite nem arquivos brutos de log e não envia o pacote. Confira o relatorio.json antes de compartilhar. O manifesto contém um hash de integridade, não uma assinatura de origem.\n\nSalvar no caminho escolhido?",
            "Revisar privacidade do pacote")) return;

        await RunOperationAsync("Preparando pacote de diagnóstico", "Montando o ZIP local com relatório, instruções de privacidade e hash de integridade.", async token =>
        {
            await DesktopStorage.ExportDiagnosticPackageAsync(dialog.FileName, CreateExportDocument(), BuildVersion, token);
            StatusTitle = "Pacote de diagnóstico salvo";
            StatusDetail = $"Arquivo salvo em {dialog.FileName}. Revise o conteúdo antes de compartilhar; nenhum dado foi enviado.";
        }, cancellable: true);
    }

    internal ExportDocument CreateExportDocument() =>
        new(8, DateTimeOffset.UtcNow, _snapshot, _reports, _performance, Recommendations.ToArray(),
            new(SelectedProfile, ReduceAnimations, ReduceTransparency), UserChanges.ToArray(), CleanupSessions.ToArray(),
            _performanceHistory.Snapshot(), _performanceBaseline, _performanceComparison, _optimizationPlan,
            _performanceSessionExports);

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
    private void OpenNetworkResetSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!CanOpenNetworkResetSettings || _isBusy) return;
        if (!Confirm("O ZEUS não executará a redefinição. O Windows pode remover e reinstalar adaptadores e retornar suas configurações ao padrão, solicitar reinício e exigir reconfigurar VPN ou switches virtuais. Perfis conhecidos podem passar a públicos. Confirme a redefinição somente na tela oficial do Windows, se ainda for necessária.", "Revisar redefinição de rede")) return;
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:network-status") { UseShellExecute = true });
            StatusTitle = "Configurações de rede abertas";
            StatusDetail = "Nenhuma redefinição foi executada pelo ZEUS. Se você confirmar na tela do Windows, revise a conexão depois do reinício e reconfigure serviços de rede que utiliza.";
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            StatusTitle = "Configurações de rede não foram abertas";
            StatusDetail = $"O Windows não abriu Configurações de Rede: {error.Message}";
        }
    }
    private void SaveNetworkResetReference_Click(object sender, RoutedEventArgs e)
    {
        if (!CanSaveNetworkResetReference || _snapshot?.WindowsInventory is not { } inventory) return;
        var dialog = new SaveFileDialog
        {
            Title = "Salvar referência da configuração de rede",
            Filter = "Arquivo JSON (*.json)|*.json",
            DefaultExt = ".json",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = "referencia-rede-zeus.json"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var reference = new
            {
                SchemaVersion = 1,
                CollectedAt = _snapshot.CollectedAt,
                Notice = "Referência dos valores retornados pelo inventário. Não é backup integral da rede nem restauração automática. Confirme cada valor depois de qualquer redefinição.",
                NetworkConfiguration = inventory.NetworkConfiguration,
                ProxyConfiguration = inventory.ProxyConfiguration
            };
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(reference, new JsonSerializerOptions { WriteIndented = true }));
            StatusTitle = "Referência de rede salva";
            StatusDetail = $"Arquivo salvo em {dialog.FileName}. Pode conter endereços internos e configuração de proxy; mantenha-o privado.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or JsonException or NotSupportedException)
        {
            StatusTitle = "Referência de rede não salva";
            StatusDetail = $"O arquivo não foi gravado: {error.Message}";
        }
    }
    private void OpenVendorSupport_Click(object sender, RoutedEventArgs e) => OpenTrustedUri("https://support.microsoft.com/windows/update-drivers-through-device-manager-in-windows-ec62f46c-ff14-c91d-eead-d7126dc1f7b6");
    private void OpenDriverSupport_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: DriverInventoryRow row }) return;
        var source = DriverSupportCatalog.Find(row.SupportSource);
        if (source is null)
        {
            StatusTitle = "Consulta oficial não mapeada";
            StatusDetail = "O fornecedor deste driver não tem um destino oficial confirmado no catálogo do ZEUS. Use o site do fabricante do computador ou o Windows Update.";
            return;
        }
        OpenTrustedUri(source.Uri.AbsoluteUri);
    }
    private void OpenTrustedUri(string uri)
    {
        if (_isBusy) return;
        try { _openUri(uri); }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException) { StatusDetail = $"O Windows não conseguiu abrir este destino: {error.Message}"; }
    }
    private void OpenWindowsPersonalizationSettings(string uri, string settingName)
    {
        if (_isBusy) return;
        try
        {
            _openUri(uri);
            StatusTitle = "Configurações oficiais do Windows abertas";
            StatusDetail = $"O Windows abriu a página de {settingName}, se ela estiver disponível nesta versão e edição. O ZEUS só encaminhou você às Configurações; não alterou nem guardou estado para reverter essas opções.";
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            StatusTitle = "Configurações do Windows não foram abertas";
            StatusDetail = $"A página de {settingName} não pôde ser aberta: {error.Message}";
        }
    }
    private void OpenWindowsThemes_Click(object sender, RoutedEventArgs e) => OpenWindowsPersonalizationSettings("ms-settings:themes", "temas");
    private void OpenWindowsColors_Click(object sender, RoutedEventArgs e) => OpenWindowsPersonalizationSettings("ms-settings:personalization-colors", "cores");
    private void OpenWindowsStart_Click(object sender, RoutedEventArgs e) => OpenWindowsPersonalizationSettings("ms-settings:personalization-start", "Iniciar");
    private void OpenWindowsTaskbar_Click(object sender, RoutedEventArgs e) => OpenWindowsPersonalizationSettings("ms-settings:taskbar", "barra de tarefas");
    private void OpenWindowsSound_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;
        try
        {
            _openUri("ms-settings:sound");
            StatusTitle = "Configurações de som abertas";
            StatusDetail = "Para pré-visualizar sons de eventos e escolher um esquema, procure “Mais configurações de som” ou “Painel de Controle de Som” e abra a guia Sons, se disponível nesta versão do Windows. O ZEUS só encaminhou você ao Windows; não aplicou nem guardou estado para reverter a escolha.";
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            StatusTitle = "Configurações do Windows não foram abertas";
            StatusDetail = $"A página de som não pôde ser aberta: {error.Message}";
        }
    }
    private void OpenWindowsLockScreen_Click(object sender, RoutedEventArgs e) => OpenWindowsPersonalizationSettings("ms-settings:lockscreen", "tela de bloqueio");
    private bool Confirm(string text, string title) => MessageBox.Show(this, text, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
}
