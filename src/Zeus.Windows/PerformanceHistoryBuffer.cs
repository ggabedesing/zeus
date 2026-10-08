namespace Zeus.Windows;

public sealed record PerformanceHistoryEntry(Guid SessionId, PerformanceObservation Observation);

/// <summary>A process-local, bounded history that preserves observations until exported.</summary>
public sealed class PerformanceHistoryBuffer
{
    public const int DefaultCapacity = 600;
    private readonly object _gate = new();
    private readonly Queue<PerformanceHistoryEntry> _entries = new();

    public PerformanceHistoryBuffer(int capacity = DefaultCapacity)
    {
        if (capacity is < 1 or > 100_000) throw new ArgumentOutOfRangeException(nameof(capacity));
        Capacity = capacity;
    }

    public int Capacity { get; }

    public void Add(PerformanceHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_gate)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > Capacity) _entries.Dequeue();
        }
    }

    public IReadOnlyList<PerformanceHistoryEntry> Snapshot()
    {
        lock (_gate) return _entries.ToArray();
    }
}

/// <summary>Sampling interval based on measured demand; unavailable CPU never means idle.</summary>
public static class AdaptiveSamplingPolicy
{
    public static TimeSpan NextInterval(PerformanceObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (observation.CpuPercent is not { } cpu || !double.IsFinite(cpu)) return TimeSpan.FromSeconds(10);
        if (cpu >= 75) return TimeSpan.FromSeconds(2);
        if (cpu >= 35) return TimeSpan.FromSeconds(5);
        return TimeSpan.FromSeconds(10);
    }
}
