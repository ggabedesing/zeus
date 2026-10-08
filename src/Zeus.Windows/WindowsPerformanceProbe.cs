using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: InternalsVisibleTo("Zeus.Hardware.Tests")]

namespace Zeus.Windows;

public sealed record ProcessObservation(int Id, string Name, double? CpuPercent, ulong WorkingSetBytes);

public sealed record PerformanceObservation(
    DateTimeOffset CollectedAt,
    TimeSpan SamplingDuration,
    double? CpuPercent,
    ulong TotalMemoryBytes,
    ulong AvailableMemoryBytes,
    IReadOnlyList<ProcessObservation> Processes,
    IReadOnlyList<string> Warnings);

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
            observations.Add(new ProcessObservation(last.Id, last.Name, processCpu, last.WorkingSetBytes));
        }
        var top = observations.OrderByDescending(process => process.CpuPercent.HasValue)
            .ThenByDescending(process => process.CpuPercent)
            .ThenByDescending(process => process.WorkingSetBytes).ThenBy(process => process.Id).Take(10).ToArray();

        ulong total = 0;
        ulong available = 0;
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (GlobalMemoryStatusEx(ref memory))
        {
            total = memory.TotalPhysical;
            available = memory.AvailablePhysical;
        }
        else warnings.Add("Memória física indisponível via GlobalMemoryStatusEx; zero neste relatório indica ausência de leitura.");
        warnings.Add("A amostra reflete a carga atual. Compare tarefas e condições equivalentes; CPU/RAM livres não medem FPS ou garantem melhorias.");
        return new PerformanceObservation(DateTimeOffset.UtcNow, Stopwatch.GetElapsedTime(start, cpuEnd),
            cpu, total, available, top, warnings.Distinct().ToArray());
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
