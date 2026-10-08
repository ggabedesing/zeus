namespace Zeus.Windows;

public sealed record PerformanceHistoryEntry(Guid SessionId, PerformanceObservation Observation);

public sealed record PerformanceComparison(
    int ReferenceSampleCount,
    int LaterSampleCount,
    double? ReferenceCpuPercent,
    double? LaterCpuPercent,
    double? ReferenceUsedMemoryPercent,
    double? LaterUsedMemoryPercent,
    DateTimeOffset ReferenceEndedAt,
    DateTimeOffset LaterEndedAt);

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

public static class PerformanceComparisonBuilder
{
    public static PerformanceComparison Compare(
        IReadOnlyList<PerformanceObservation> reference,
        IReadOnlyList<PerformanceObservation> later)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(later);
        if (reference.Count == 0 || later.Count == 0)
            throw new ArgumentException("São necessárias amostras dos dois períodos para comparar.");

        static double? Average(IEnumerable<double?> values)
        {
            var valid = values.Where(value => value is { } number && double.IsFinite(number))
                .Select(value => value!.Value).ToArray();
            return valid.Length == 0 ? null : valid.Average();
        }

        static double? UsedMemoryPercent(PerformanceObservation observation) =>
            observation.TotalMemoryBytes == 0 || observation.AvailableMemoryBytes > observation.TotalMemoryBytes
                ? null
                : (observation.TotalMemoryBytes - observation.AvailableMemoryBytes) /
                    (double)observation.TotalMemoryBytes * 100;

        return new(reference.Count, later.Count,
            Average(reference.Select(sample => sample.CpuPercent)),
            Average(later.Select(sample => sample.CpuPercent)),
            Average(reference.Select(UsedMemoryPercent)),
            Average(later.Select(UsedMemoryPercent)),
            reference.Max(sample => sample.CollectedAt), later.Max(sample => sample.CollectedAt));
    }
}
