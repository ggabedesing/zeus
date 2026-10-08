using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Management;
using System.Text.RegularExpressions;

[assembly: InternalsVisibleTo("Zeus.Hardware.Tests")]

namespace Zeus.Windows;

public sealed record ProcessObservation(int Id, string Name, double? CpuPercent, ulong WorkingSetBytes, long? StartTimeUtcTicks = null);
public sealed record GpuEngineObservation(string InstanceName, int? ProcessId, string EngineType, double UtilizationPercent, string? ProcessName = null);
public sealed record DiskPerformanceObservation(string InstanceName, ulong? BytesPerSecond, double? ActivePercent, double? AverageReadLatencyMilliseconds);
public sealed record NetworkPerformanceObservation(string Adapter, ulong? BytesPerSecond, ulong? LinkBitsPerSecond, ulong? QueueLength, ulong? ErrorPackets);
public sealed record GpuMemoryObservation(string AdapterInstance, ulong? DedicatedUsageBytes, ulong? SharedUsageBytes,
    ulong? TotalCommittedBytes, ulong? DedicatedCapacityBytes = null)
{
    public double? DedicatedOccupancyPercent => DedicatedCapacityBytes is { } capacity && capacity > 0 && DedicatedUsageBytes is { } usage
        ? usage / (double)capacity * 100 : null;
}
public sealed record GpuProcessMemoryObservation(string InstanceName, string AdapterInstance, int ProcessId, string? ProcessName,
    ulong? DedicatedUsageBytes, ulong? SharedUsageBytes, ulong? NonLocalUsageBytes,
    ulong? LocalUsageBytes, ulong? TotalCommittedBytes);

/// <summary>SamplingDuration is the CPU system-counter interval; GPU, disk, and network counters are read afterward.</summary>
public sealed record PerformanceObservation(
    DateTimeOffset CollectedAt,
    TimeSpan SamplingDuration,
    double? CpuPercent,
    ulong TotalMemoryBytes,
    ulong AvailableMemoryBytes,
    IReadOnlyList<ProcessObservation> Processes,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<GpuEngineObservation>? GpuEngines = null,
    IReadOnlyList<DiskPerformanceObservation>? Disks = null,
    IReadOnlyList<NetworkPerformanceObservation>? Networks = null,
    ActivityContextInfo? ActivityContext = null,
    IReadOnlyList<GpuMemoryObservation>? GpuMemory = null,
    IReadOnlyList<GpuProcessMemoryObservation>? GpuProcessMemory = null);

/// <summary>
/// A bounded, read-only observation, not a benchmark or prediction of performance
/// gains. CPU counters are obtained from Windows rather than localized counters.
/// </summary>
public sealed class WindowsPerformanceProbe
{
    public Task<PerformanceObservation> SampleAsync(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("A amostra de desempenho requer Windows.");
        cancellationToken.ThrowIfCancellationRequested();
        duration = ClampDuration(duration);
        return Task.Run(() => SampleCoreAsync(duration, cancellationToken), cancellationToken);
    }

    internal static TimeSpan ClampDuration(TimeSpan duration) =>
        TimeSpan.FromSeconds(Math.Clamp(duration.TotalSeconds, 2, 30));

    private static async Task<PerformanceObservation> SampleCoreAsync(TimeSpan duration, CancellationToken token)
    {
        var warnings = new List<string>();
        var start = Stopwatch.GetTimestamp();
        var activeProcessors = GetActiveProcessorCount(ushort.MaxValue);
        var totalCpuSupported = IsTotalCpuScopeSupported(activeProcessors);
        var logicalProcessors = activeProcessors is > 0 and <= int.MaxValue
            ? (int)activeProcessors : Environment.ProcessorCount;
        if (activeProcessors == 0)
            warnings.Add("Contagem de processadores ativos indisponível; CPU total não é informada e processos usam a contagem do runtime.");
        else if (!totalCpuSupported)
            warnings.Add("CPU total indisponível em máquinas com mais de 64 processadores lógicos: GetSystemTimes pode consultar grupos diferentes após a espera.");
        var cpuBefore = ReadSystemTimes(warnings);
        var before = ReadProcesses(token, out _);
        await Task.Delay(duration, token);
        var cpuAfter = ReadSystemTimes(warnings);
        var cpuEnd = Stopwatch.GetTimestamp();
        var after = ReadProcesses(token, out var missing);
        token.ThrowIfCancellationRequested();

        double? cpu = totalCpuSupported && cpuBefore is { } firstSystem && cpuAfter is { } lastSystem
            ? CalculateSystemCpuPercent(firstSystem, lastSystem) : null;
        if (cpu is null && totalCpuSupported)
            warnings.Add("Uso de CPU indisponível: os contadores não produziram um intervalo válido.");
        if (missing > 0) warnings.Add($"{missing} processos encerraram ou restringiram a leitura; valores ausentes não são estimados.");

        var observations = new List<ProcessObservation>();
        foreach (var last in after.Values)
        {
            token.ThrowIfCancellationRequested();
            double? processCpu = null;
            if (last.CpuTicks is { } ticks && last.StartTicks is { } identity &&
                before.TryGetValue(last.Id, out var firstProcess) && firstProcess.StartTicks == identity &&
                firstProcess.CpuTicks is { } priorTicks)
            {
                processCpu = CalculateProcessCpuPercent(priorTicks, ticks,
                    Stopwatch.GetElapsedTime(firstProcess.ObservedAt, last.ObservedAt), logicalProcessors);
            }
            observations.Add(new ProcessObservation(last.Id, last.Name, processCpu, last.WorkingSetBytes, last.StartTicks));
        }
        var allObservedProcesses = observations.ToArray();
        var top = allObservedProcesses.OrderByDescending(process => process.CpuPercent.HasValue)
            .ThenByDescending(process => process.CpuPercent)
            .ThenByDescending(process => process.WorkingSetBytes).ThenBy(process => process.Id).Take(50).ToArray();

        ulong total = 0;
        ulong available = 0;
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (GlobalMemoryStatusEx(ref memory))
        {
            total = memory.TotalPhysical;
            available = memory.AvailablePhysical;
        }
        else warnings.Add("Memória física indisponível via GlobalMemoryStatusEx; zero neste relatório indica ausência de leitura.");
        var gpuEngines = MapGpuEnginesToProcesses(ReadGpuCounters(token, warnings), allObservedProcesses);
        var gpuMemory = ReadGpuMemoryCounters(token, warnings);
        var gpuProcessMemory = ReadGpuProcessMemoryCounters(token, warnings, allObservedProcesses);
        var disks = ReadDiskCounters(token, warnings);
        var networks = ReadNetworkCounters(token, warnings);
        var activityContext = ActivityContextDetector.Detect(allObservedProcesses, gpuEngines);
        warnings.Add("GPU: utilização por instância/engine não é uso total. Ocupação de memória dedicada compara uso reportado com capacidade DXGI correspondente e, sozinha, não diagnostica pressão ou gargalo; sensores ausentes permanecem desconhecidos.");
        warnings.Add("Memória GPU por processo: alocações dedicada/compartilhada são contadores reportados pelo Windows; nome depende do PID observado. Orçamento por processo e pressão não são fornecidos por essa leitura.");
        warnings.Add("Disco e rede: contadores são taxas locais; tráfego não mede latência ou qualidade da Internet e erros são contagens reportadas pelo adaptador.");
        warnings.Add("A lista de processos exibida é limitada aos 50 maiores por CPU/RAM. Heurísticas de jogo/OBS consultam todos os processos acessíveis nesta amostra; processos inacessíveis e engines GPU não informadas permanecem desconhecidos. VideoEncode associado ao PID do OBS não confirma transmissão ao vivo.");
        warnings.Add("A amostra reflete a carga atual. Compare tarefas e condições equivalentes; CPU/RAM livres não medem FPS ou garantem melhorias.");
        return new PerformanceObservation(DateTimeOffset.UtcNow, Stopwatch.GetElapsedTime(start, cpuEnd),
            cpu, total, available, top, warnings.Distinct().ToArray(), gpuEngines, disks, networks, activityContext, gpuMemory, gpuProcessMemory);
    }

    internal readonly record struct SystemCpuTimes(ulong Idle, ulong Kernel, ulong User);

    internal static bool IsTotalCpuScopeSupported(uint activeLogicalProcessors) =>
        activeLogicalProcessors is > 0 and <= 64;

    internal static double? CalculateSystemCpuPercent(SystemCpuTimes before, SystemCpuTimes after)
    {
        // Kernel includes idle time. Counter rollback/overflow means the observation
        // cannot be used; it must not become a fabricated zero or negative load.
        if (after.Idle < before.Idle || after.Kernel < before.Kernel || after.User < before.User) return null;
        var idle = after.Idle - before.Idle;
        var kernel = after.Kernel - before.Kernel;
        var user = after.User - before.User;
        if (ulong.MaxValue - kernel < user) return null;
        var total = kernel + user;
        if (total == 0 || idle > total) return null;
        return ((total - idle) / (double)total) * 100;
    }

    internal static double? CalculateProcessCpuPercent(long beforeTicks, long afterTicks,
        TimeSpan elapsed, int logicalProcessors)
    {
        if (beforeTicks < 0 || afterTicks < beforeTicks || elapsed <= TimeSpan.Zero || logicalProcessors <= 0) return null;
        var load = ((afterTicks - beforeTicks) / (double)TimeSpan.TicksPerSecond) /
            elapsed.TotalSeconds / logicalProcessors * 100;
        return double.IsFinite(load) ? Math.Clamp(load, 0, 100) : null;
    }

    internal static IReadOnlyList<GpuEngineObservation> MapGpuEnginesToProcesses(
        IReadOnlyList<GpuEngineObservation> engines, IReadOnlyList<ProcessObservation> processes)
    {
        ArgumentNullException.ThrowIfNull(engines);
        ArgumentNullException.ThrowIfNull(processes);
        var processNames = processes.ToDictionary(process => process.Id, process => process.Name);
        return engines.Select(engine => engine.ProcessId is { } id && processNames.TryGetValue(id, out var name)
            ? engine with { ProcessName = name }
            : engine with { ProcessName = null }).ToArray();
    }

    internal static GpuProcessMemoryObservation? ParseGpuProcessMemoryCounter(string? instanceName,
        ulong? dedicatedUsage, ulong? sharedUsage, ulong? nonLocalUsage, ulong? localUsage, ulong? totalCommitted,
        IReadOnlyDictionary<int, string> processNames)
    {
        ArgumentNullException.ThrowIfNull(processNames);
        if (string.IsNullOrWhiteSpace(instanceName)) return null;
        var match = Regex.Match(instanceName, @"^pid_(?<pid>\d+)_(?<adapter>luid_.+)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        if (!match.Success || !int.TryParse(match.Groups["pid"].Value, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var processId)) return null;
        processNames.TryGetValue(processId, out var processName);
        return new GpuProcessMemoryObservation(instanceName, match.Groups["adapter"].Value, processId, processName,
            dedicatedUsage, sharedUsage, nonLocalUsage, localUsage, totalCommitted);
    }

    private static IReadOnlyList<GpuEngineObservation> ReadGpuCounters(CancellationToken token, List<string> warnings) =>
        ReadCounterRows("GPU", "Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine",
            "SELECT Name,UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine", 200, token, warnings, row =>
            {
                var instance = Convert.ToString(row["Name"], System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                var utilization = CounterDouble(row, "UtilizationPercentage");
                if (string.IsNullOrWhiteSpace(instance) || utilization is null) return null;
                var processMatch = Regex.Match(instance, @"(?:^|_)pid_(\d+)(?:_|$)", RegexOptions.CultureInvariant);
                var engineMatch = Regex.Match(instance, @"_engtype_(.+)$", RegexOptions.CultureInvariant);
                return new GpuEngineObservation(instance,
                    processMatch.Success && int.TryParse(processMatch.Groups[1].Value, out var pid) ? pid : null,
                    engineMatch.Success ? engineMatch.Groups[1].Value : "Desconhecido", utilization.Value);
            });

    private static IReadOnlyList<GpuMemoryObservation> ReadGpuMemoryCounters(CancellationToken token, List<string> warnings)
    {
        var readings = ReadCounterRows("Memória GPU", "Win32_PerfFormattedData_GPUPerformanceCounters_GPUAdapterMemory",
            "SELECT Name,DedicatedUsage,SharedUsage,TotalCommitted FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUAdapterMemory", 128, token, warnings, row =>
            {
                var instance = Convert.ToString(row["Name"], System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(instance)) return null;
                return new GpuMemoryObservation(instance, CounterUInt64(row, "DedicatedUsage"),
                    CounterUInt64(row, "SharedUsage"), CounterUInt64(row, "TotalCommitted"));
            });
        if (readings.Count == 0) return readings;

        try
        {
            var adapters = DxgiAdapterMemoryReader.ReadAdapters();
            var capacities = adapters.ToDictionary(adapter => adapter.AdapterInstance,
                    adapter => adapter.DedicatedCapacityBytes == 0 ? (ulong?)null : adapter.DedicatedCapacityBytes,
                    StringComparer.OrdinalIgnoreCase);
            var unmatched = readings.Count(reading => reading.DedicatedUsageBytes is > 0 &&
                (!capacities.TryGetValue(reading.AdapterInstance, out var capacity) || !capacity.HasValue));
            if (unmatched > 0)
                warnings.Add($"Memória GPU: capacidade dedicada DXGI não correspondeu a {unmatched} instância(s) com uso dedicado positivo; a ocupação dessas instâncias permanece indisponível.");
            return readings.Select(reading => capacities.TryGetValue(reading.AdapterInstance, out var capacity) && capacity.HasValue
                ? reading with { DedicatedCapacityBytes = capacity }
                : reading).ToArray();
        }
        catch (Exception error)
        {
            warnings.Add($"Memória GPU: capacidade dedicada DXGI indisponível ({error.GetType().Name}); contadores de uso permanecem disponíveis.");
            return readings;
        }
    }

    private static IReadOnlyList<GpuProcessMemoryObservation> ReadGpuProcessMemoryCounters(CancellationToken token,
        List<string> warnings, IReadOnlyList<ProcessObservation> processes)
    {
        var processNames = processes.GroupBy(process => process.Id).ToDictionary(group => group.Key, group => group.First().Name);
        return ReadCounterRows("Memória GPU por processo", "Win32_PerfFormattedData_GPUPerformanceCounters_GPUProcessMemory",
            "SELECT Name,DedicatedUsage,SharedUsage,NonLocalUsage,LocalUsage,TotalCommitted FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUProcessMemory",
            512, token, warnings, row => ParseGpuProcessMemoryCounter(
                Convert.ToString(row["Name"], System.Globalization.CultureInfo.InvariantCulture),
                CounterUInt64(row, "DedicatedUsage"), CounterUInt64(row, "SharedUsage"),
                CounterUInt64(row, "NonLocalUsage"), CounterUInt64(row, "LocalUsage"), CounterUInt64(row, "TotalCommitted"), processNames));
    }

    private static IReadOnlyList<DiskPerformanceObservation> ReadDiskCounters(CancellationToken token, List<string> warnings) =>
        ReadCounterRows("Disco", "Win32_PerfFormattedData_PerfDisk_PhysicalDisk",
            "SELECT Name,DiskBytesPersec,PercentDiskTime,AvgDisksecPerRead FROM Win32_PerfFormattedData_PerfDisk_PhysicalDisk WHERE Name <> '_Total'", 64, token, warnings, row =>
            {
                var name = Convert.ToString(row["Name"], System.Globalization.CultureInfo.InvariantCulture);
                if (string.IsNullOrWhiteSpace(name)) return null;
                var latencySeconds = CounterDouble(row, "AvgDisksecPerRead");
                return new DiskPerformanceObservation(name, CounterUInt64(row, "DiskBytesPersec"),
                    CounterDouble(row, "PercentDiskTime"), latencySeconds is { } value ? value * 1000 : null);
            });

    private static IReadOnlyList<NetworkPerformanceObservation> ReadNetworkCounters(CancellationToken token, List<string> warnings) =>
        ReadCounterRows("Rede", "Win32_PerfFormattedData_Tcpip_NetworkInterface",
            "SELECT Name,BytesTotalPersec,CurrentBandwidth,OutputQueueLength,PacketsReceivedErrors,PacketsOutboundErrors FROM Win32_PerfFormattedData_Tcpip_NetworkInterface", 128, token, warnings, row =>
            {
                var name = Convert.ToString(row["Name"], System.Globalization.CultureInfo.InvariantCulture);
                if (string.IsNullOrWhiteSpace(name)) return null;
                var receivedErrors = CounterUInt64(row, "PacketsReceivedErrors");
                var sentErrors = CounterUInt64(row, "PacketsOutboundErrors");
                ulong? errors = receivedErrors is null ? sentErrors : sentErrors is null ? receivedErrors
                    : ulong.MaxValue - receivedErrors.Value < sentErrors.Value ? null : receivedErrors + sentErrors;
                return new NetworkPerformanceObservation(name, CounterUInt64(row, "BytesTotalPersec"),
                    CounterUInt64(row, "CurrentBandwidth"), CounterUInt64(row, "OutputQueueLength"), errors);
            });

    private static IReadOnlyList<T> ReadCounterRows<T>(string label, string className, string query, int limit,
        CancellationToken token, List<string> warnings, Func<ManagementBaseObject, T?> convert) where T : class
    {
        var result = new List<T>();
        try
        {
            using var searcher = new ManagementObjectSearcher(new ManagementScope("root\\CIMV2"), new ObjectQuery(query),
                new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(3), ReturnImmediately = true, Rewindable = false });
            using var rows = searcher.Get();
            foreach (ManagementObject row in rows)
            {
                using (row)
                {
                    token.ThrowIfCancellationRequested();
                    if (convert(row) is { } value) result.Add(value);
                    if (result.Count < limit) continue;
                    warnings.Add($"{label}: limite de {limit} contadores atingido; a lista pode estar incompleta.");
                    break;
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            return HandleCounterReadFailure(label, className, result, error, warnings);
        }
        if (result.Count == 0) warnings.Add($"{label}: o provedor não retornou instâncias; a métrica permanece indisponível.");
        return result;
    }

    internal static IReadOnlyList<T> HandleCounterReadFailure<T>(string label, string className,
        IReadOnlyList<T> validRows, Exception error, ICollection<string> warnings) where T : class
    {
        ArgumentNullException.ThrowIfNull(validRows);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(warnings);
        if (validRows.Count == 0)
        {
            warnings.Add($"{label}: contador {className} indisponível ({error.GetType().Name}).");
            return [];
        }

        warnings.Add($"{label}: leitura interrompida após {validRows.Count} instância(s) válidas ({error.GetType().Name}); os dados parciais foram preservados e a lista pode estar incompleta.");
        return validRows;
    }

    private static double? CounterDouble(ManagementBaseObject row, string property)
    {
        var value = row[property];
        return double.TryParse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) &&
            double.IsFinite(parsed) && parsed >= 0 ? parsed : null;
    }

    private static ulong? CounterUInt64(ManagementBaseObject row, string property) =>
        ulong.TryParse(Convert.ToString(row[property], System.Globalization.CultureInfo.InvariantCulture),
            System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static SystemCpuTimes? ReadSystemTimes(List<string> warnings)
    {
        if (GetSystemTimes(out var idle, out var kernel, out var user))
            return new SystemCpuTimes(idle.Value, kernel.Value, user.Value);
        warnings.Add($"GetSystemTimes indisponível (erro Windows {Marshal.GetLastWin32Error()}).");
        return null;
    }

    private sealed record ProcessCounter(int Id, string Name, long? StartTicks, long? CpuTicks,
        ulong WorkingSetBytes, long ObservedAt);

    private static Dictionary<int, ProcessCounter> ReadProcesses(CancellationToken token, out int missing)
    {
        var result = new Dictionary<int, ProcessCounter>();
        missing = 0;
        var processes = Process.GetProcesses();
        try
        {
            foreach (var process in processes)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var id = process.Id;
                    var name = process.ProcessName;
                    var memory = process.WorkingSet64;
                    long? identity = null;
                    long? cpu = null;
                    try
                    {
                        identity = process.StartTime.ToUniversalTime().Ticks;
                        cpu = process.TotalProcessorTime.Ticks;
                    }
                    catch (Exception error) when (error is Win32Exception or InvalidOperationException or NotSupportedException)
                    {
                        missing++;
                    }
                    result[id] = new ProcessCounter(id, name, identity, cpu,
                        memory >= 0 ? (ulong)memory : 0, Stopwatch.GetTimestamp());
                }
                catch (Exception error) when (error is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    missing++;
                }
            }
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileTime
    {
        internal uint Low;
        internal uint High;
        internal readonly ulong Value => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        internal uint Length;
        internal uint MemoryLoad;
        internal ulong TotalPhysical;
        internal ulong AvailablePhysical;
        internal ulong TotalPageFile;
        internal ulong AvailablePageFile;
        internal ulong TotalVirtual;
        internal ulong AvailableVirtual;
        internal ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out NativeFileTime idle, out NativeFileTime kernel, out NativeFileTime user);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetActiveProcessorCount(ushort groupNumber);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
