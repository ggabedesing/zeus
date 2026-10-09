using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Zeus.Windows;

internal readonly record struct ProcessIoCounterSnapshot(int ProcessId, long StartTimeUtcTicks,
    ulong ReadBytes, ulong WrittenBytes, ulong OtherBytes, long ObservedAt);
internal readonly record struct ProcessIoRates(double? ReadBytesPerSecond, double? WriteBytesPerSecond, double? OtherBytesPerSecond,
    double? SamplingDurationSeconds = null);

/// <summary>Read-only process I/O accounting; these counters are not physical disk attribution.</summary>
internal static class ProcessIoReader
{
    internal static ProcessIoCounterSnapshot? Read(int processId)
    {
        if (!OperatingSystem.IsWindows() || processId <= 0) return null;
        using var handle = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, processId);
        if (handle.IsInvalid || !GetProcessTimes(handle, out var created, out _, out _, out _) ||
            !GetProcessIoCounters(handle, out var counters)) return null;
        if (created.Value > long.MaxValue) return null;
        long startTicks;
        try { startTicks = DateTime.FromFileTimeUtc((long)created.Value).Ticks; }
        catch (ArgumentOutOfRangeException) { return null; }
        return new(processId, startTicks, counters.ReadTransferCount, counters.WriteTransferCount,
            counters.OtherTransferCount, Stopwatch.GetTimestamp());
    }

    internal static ProcessIoRates CalculateRates(ProcessIoCounterSnapshot? before, ProcessIoCounterSnapshot? after)
    {
        if (before is not { } first || after is not { } last || first.ProcessId != last.ProcessId ||
            first.StartTimeUtcTicks <= 0 || first.StartTimeUtcTicks != last.StartTimeUtcTicks || last.ObservedAt <= first.ObservedAt)
            return new(null, null, null);
        var elapsed = Stopwatch.GetElapsedTime(first.ObservedAt, last.ObservedAt).TotalSeconds;
        return new(Rate(first.ReadBytes, last.ReadBytes, elapsed), Rate(first.WrittenBytes, last.WrittenBytes, elapsed),
            Rate(first.OtherBytes, last.OtherBytes, elapsed), elapsed);
    }

    internal static double? Rate(ulong before, ulong after, double elapsedSeconds)
    {
        if (after < before || !double.IsFinite(elapsedSeconds) || elapsedSeconds <= 0) return null;
        var rate = (after - before) / elapsedSeconds;
        return double.IsFinite(rate) && rate >= 0 ? rate : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileTime
    {
        internal uint Low;
        internal uint High;
        internal readonly ulong Value => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        internal ulong ReadOperationCount;
        internal ulong WriteOperationCount;
        internal ulong OtherOperationCount;
        internal ulong ReadTransferCount;
        internal ulong WriteTransferCount;
        internal ulong OtherTransferCount;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessIoCounters(SafeProcessHandle handle, out IoCounters counters);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(SafeProcessHandle handle, out NativeFileTime created,
        out NativeFileTime exited, out NativeFileTime kernel, out NativeFileTime user);
}
