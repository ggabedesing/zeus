using System.Runtime.InteropServices;

namespace Zeus.Windows;

/// <summary>Strict read-only console entry point; never calls the desktop sampler.</summary>
public static class PerformanceCollectorHost
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (!OperatingSystem.IsWindows() || args.Length != 4 || args[0] != "--collector" || args[2] != "--duration" ||
            !Enum.TryParse<PerformanceCollectorCategory>(args[1], false, out var category) || !Enum.IsDefined(category) ||
            category.ToString() != args[1] || !int.TryParse(args[3], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var seconds) || seconds is < 2 or > 30) return 2;
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        var started = DateTimeOffset.UtcNow;
        var sequence = 0;
        var emittedBytes = 0;
        void Emit(PerformanceCollectorEnvelope data, bool completed)
        {
            var line = PerformanceCollectorProtocol.Serialize(new(++sequence, completed, data with { StartedAt = started }));
            var length = System.Text.Encoding.UTF8.GetByteCount(line) + 1;
            if (emittedBytes + length > PerformanceCollectorProtocol.MaximumBytes) throw new InvalidDataException("Limite de saída do coletor atingido.");
            emittedBytes += length;
            Console.Out.WriteLine(line);
            Console.Out.Flush();
        }
        WindowsPerformanceProbe.CollectorProgress.Value = row => Emit(WindowsPerformanceProbe.RowEnvelope(category, started, row), false);
        try
        {
            var data = await WindowsPerformanceProbe.CollectCategoryAsync(category, TimeSpan.FromSeconds(seconds));
            Emit(data, true);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.GetType().Name);
            return 1;
        }
        finally { WindowsPerformanceProbe.CollectorProgress.Value = null; }
    }
}

public sealed partial class WindowsPerformanceProbe
{
    internal static readonly AsyncLocal<Action<object>?> CollectorProgress = new();
    internal static PerformanceCollectorEnvelope RowEnvelope(PerformanceCollectorCategory c, DateTimeOffset started, object row)
    {
        var d = new PerformanceCollectorEnvelope(c, started, DateTimeOffset.UtcNow, PerformanceCollectorState.Partial, []);
        return row switch
        {
            PerformanceCollectorEnvelope p => p with { StartedAt = started },
            ProcessObservation p => d with { Processes = [p] },
            GpuEngineObservation p => d with { GpuEngines = [p] },
            GpuMemoryObservation p => d with { GpuMemory = [p] },
            GpuProcessMemoryObservation p => d with { GpuProcessMemory = [p] },
            DiskPerformanceObservation p => d with { Disks = [p] },
            NetworkPerformanceObservation p => d with { Networks = [p] },
            MemoryPagingObservation p => d with { MemoryPaging = p },
            _ => throw new InvalidDataException("Tipo de linha não permitido.")
        };
    }
    internal static async Task<PerformanceCollectorEnvelope> CollectCategoryAsync(PerformanceCollectorCategory category, TimeSpan duration)
    {
        if (category == PerformanceCollectorCategory.CpuProcesses) return await CollectCpuAsync(duration, CancellationToken.None);
        var started = DateTimeOffset.UtcNow;
        var warnings = new List<string>();
        var d = new PerformanceCollectorEnvelope(category, started, started, PerformanceCollectorState.Complete, warnings);
        switch (category)
        {
            case PerformanceCollectorCategory.GpuEngines: d = d with { GpuEngines = ReadGpuCounters(default, warnings) }; break;
            case PerformanceCollectorCategory.GpuAdapterMemory: d = d with { GpuMemory = ReadGpuMemoryCounters(default, warnings) }; break;
            case PerformanceCollectorCategory.GpuProcessMemory: d = d with { GpuProcessMemory = ReadGpuProcessMemoryCounters(default, warnings, []) }; break;
            case PerformanceCollectorCategory.Disks: d = d with { Disks = ReadDiskCounters(default, warnings) }; break;
            case PerformanceCollectorCategory.Networks: d = d with { Networks = ReadNetworkCounters(default, warnings) }; break;
            case PerformanceCollectorCategory.MemoryPaging:
                var paging = ReadMemoryPagingCounters(default, warnings);
                var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
                if (GlobalMemoryStatusEx(ref memory)) d = d with { MemoryPaging = paging, TotalMemoryBytes = memory.TotalPhysical, AvailableMemoryBytes = memory.AvailablePhysical };
                else { warnings.Add("Memória física indisponível; ausência de leitura não representa zero de capacidade."); d = d with { MemoryPaging = paging }; }
                break;
            default: throw new ArgumentOutOfRangeException(nameof(category));
        }
        var available = d.GpuEngines?.Count > 0 || d.GpuMemory?.Any(r => r.DedicatedUsageBytes.HasValue || r.SharedUsageBytes.HasValue || r.TotalCommittedBytes.HasValue) == true ||
            d.GpuProcessMemory?.Any(r => r.DedicatedUsageBytes.HasValue || r.SharedUsageBytes.HasValue || r.TotalCommittedBytes.HasValue || r.LocalUsageBytes.HasValue || r.NonLocalUsageBytes.HasValue) == true ||
            d.Disks?.Any(r => r.BytesPerSecond.HasValue || r.ActivePercent.HasValue || r.AverageReadLatencyMilliseconds.HasValue) == true ||
            d.Networks?.Any(r => r.BytesPerSecond.HasValue || r.LinkBitsPerSecond.HasValue || r.QueueLength.HasValue || r.ErrorPackets.HasValue) == true ||
            d.TotalMemoryBytes is > 0 || d.MemoryPaging is { } pagingValues && (pagingValues.CommittedBytes.HasValue || pagingValues.CommitLimitBytes.HasValue || pagingValues.PageReadsPerSecond.HasValue || pagingValues.PagesInputPerSecond.HasValue);
        var fieldsMissing = d.GpuMemory?.Any(r => !r.DedicatedUsageBytes.HasValue || !r.SharedUsageBytes.HasValue || !r.TotalCommittedBytes.HasValue || !r.DedicatedCapacityBytes.HasValue) == true ||
            d.GpuProcessMemory?.Any(r => !r.DedicatedUsageBytes.HasValue || !r.SharedUsageBytes.HasValue || !r.TotalCommittedBytes.HasValue || !r.LocalUsageBytes.HasValue || !r.NonLocalUsageBytes.HasValue || r.ProcessStartTimeUtcTicks is null) == true ||
            d.Disks?.Any(r => !r.BytesPerSecond.HasValue || !r.ActivePercent.HasValue || !r.AverageReadLatencyMilliseconds.HasValue) == true ||
            d.Networks?.Any(r => !r.BytesPerSecond.HasValue || !r.LinkBitsPerSecond.HasValue || !r.QueueLength.HasValue || !r.ErrorPackets.HasValue) == true ||
            category == PerformanceCollectorCategory.MemoryPaging && (d.TotalMemoryBytes is null || d.MemoryPaging is null || d.MemoryPaging.CommittedBytes is null || d.MemoryPaging.CommitLimitBytes is null || d.MemoryPaging.PageReadsPerSecond is null || d.MemoryPaging.PagesInputPerSecond is null);
        if(fieldsMissing) warnings.Add("Campos do coletor ausentes ou inválidos permanecem desconhecidos; a leitura é parcial.");
        return d with { FinishedAt = DateTimeOffset.UtcNow, State = !available ? PerformanceCollectorState.Unavailable : warnings.Count > 0 ? PerformanceCollectorState.Partial : PerformanceCollectorState.Complete };
    }
    internal static PerformanceObservation MergeCollectors(IReadOnlyList<PerformanceCollectorEnvelope> results)
    {
        PerformanceCollectorEnvelope? Get(PerformanceCollectorCategory c) => results.SingleOrDefault(r => r.Category == c);
        var cpu = Get(PerformanceCollectorCategory.CpuProcesses);
        var all = cpu?.Processes ?? [];
        // Partial CPU streams may contain repeated observations of the same PID; retain the last exact identity.
        all = all.GroupBy(p => p.Id).Select(g => g.Last()).ToArray();
        var top = all.OrderByDescending(p => p.CpuPercent.HasValue).ThenByDescending(p => p.CpuPercent).ThenByDescending(p => p.WorkingSetBytes).ThenBy(p => p.Id).Take(50).ToArray();
        var io = all.Where(p => p.StartTimeUtcTicks is > 0 && (p.IoReadBytesPerSecond.HasValue || p.IoWriteBytesPerSecond.HasValue || p.IoOtherBytesPerSecond.HasValue))
            .OrderByDescending(p => (p.IoReadBytesPerSecond ?? 0) + (p.IoWriteBytesPerSecond ?? 0) + (p.IoOtherBytesPerSecond ?? 0)).ThenBy(p => p.Id).Take(30).ToArray();
        var engines = MapGpuEnginesToProcesses(Get(PerformanceCollectorCategory.GpuEngines)?.GpuEngines ?? [], all);
        var byId = all.ToDictionary(p => p.Id);
        var gpuProcesses = (Get(PerformanceCollectorCategory.GpuProcessMemory)?.GpuProcessMemory ?? []).Select(p =>
            byId.TryGetValue(p.ProcessId, out var process) && p.ProcessStartTimeUtcTicks is > 0 && p.ProcessStartTimeUtcTicks==process.StartTimeUtcTicks ? p with { ProcessName = process.Name } : p with { ProcessName = null }).ToArray();
        var ram = Get(PerformanceCollectorCategory.MemoryPaging);
        var warnings = results.SelectMany(r => r.Warnings).ToList();
        if(gpuProcesses.Any(p => p.ProcessName is null)) warnings.Add("A identidade do processo GPU não correspondeu à janela CPU; nomes permanecem desconhecidos e inícios registrados pertencem à janela de consulta GPU.");
        warnings.Add("Coletores executam em processos separados, com início/fim e prazo próprios. As leituras não são simultâneas; associações GPU usam PID/início observado na janela CPU e não provam permanência do processo.");
        warnings.Add("GPU: utilização por engine não é uso total. Ocupação dedicada não comprova pressão ou gargalo. VideoEncode do OBS não confirma transmissão ao vivo.");
        warnings.Add("RAM/paginação: hard faults também leem executáveis e arquivos mapeados. Disco/rede e I/O por processo são taxas locais e não medem FPS, latência da Internet ou ganhos.");
        warnings.Add("Processos exibidos: até 50 por CPU/RAM e até 30 por soma dos campos I/O disponíveis. Heurísticas usam o conjunto completo acessível preservado, até 4096; campos ausentes permanecem desconhecidos.");
        if (ram?.TotalMemoryBytes is null) warnings.Add("Memória física indisponível; campos de capacidade zero nesta versão indicam ausência de leitura.");
        return new(DateTimeOffset.UtcNow, cpu?.SamplingDuration ?? TimeSpan.Zero, cpu?.CpuPercent,
            ram?.TotalMemoryBytes ?? 0, ram?.AvailableMemoryBytes ?? 0, top, warnings.Distinct().ToArray(), engines,
            Get(PerformanceCollectorCategory.Disks)?.Disks ?? [], Get(PerformanceCollectorCategory.Networks)?.Networks ?? [],
            ActivityContextDetector.Detect(all, engines), Get(PerformanceCollectorCategory.GpuAdapterMemory)?.GpuMemory ?? [], gpuProcesses, ram?.MemoryPaging, io,
            results.Select(r => new PerformanceCollectorStatus(r.Category,r.StartedAt,r.FinishedAt,r.FinishedAt-r.StartedAt,r.State,r.Warnings)).ToArray());
    }
}
