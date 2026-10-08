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
    DateTimeOffset LaterEndedAt,
    PerformanceMetricComparison? GpuEnginePeak = null,
    PerformanceMetricComparison? DiskActivityPeak = null,
    IReadOnlyList<PerformanceGpuMemoryComparison>? GpuMemoryUsage = null);

public sealed record PerformanceMetricComparison(
    double? ReferencePercent,
    double? LaterPercent,
    int ReferenceAvailableSamples,
    int LaterAvailableSamples);

public sealed record PerformanceGpuMemoryComparison(
    string AdapterInstance,
    double? ReferenceDedicatedBytes,
    double? LaterDedicatedBytes,
    int ReferenceAvailableSamples,
    int LaterAvailableSamples,
    double? ReferenceOccupancyPercent = null,
    double? LaterOccupancyPercent = null);

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

        static PerformanceMetricComparison CompareSamplePeaks(
            IReadOnlyList<PerformanceObservation> first, IReadOnlyList<PerformanceObservation> second,
            Func<PerformanceObservation, IEnumerable<double?>> values, bool enforceGpuPercentageRange = false)
        {
            static (double? Average, int Count) Summarize(IReadOnlyList<PerformanceObservation> samples,
                Func<PerformanceObservation, IEnumerable<double?>> selector, bool enforceRange)
            {
                var peaks = new List<double>();
                foreach (var sample in samples)
                {
                    var available = selector(sample).Where(value => value is { } number && double.IsFinite(number) && number >= 0 &&
                        (!enforceRange || number <= 100)).Select(value => value!.Value).ToArray();
                    if (available.Length > 0) peaks.Add(available.Max());
                }
                return (peaks.Count == 0 ? null : peaks.Average(), peaks.Count);
            }

            var left = Summarize(first, values, enforceGpuPercentageRange);
            var right = Summarize(second, values, enforceGpuPercentageRange);
            return new(left.Average, right.Average, left.Count, right.Count);
        }

        var gpu = CompareSamplePeaks(reference, later,
            sample => (sample.GpuEngines ?? []).Select(engine => (double?)engine.UtilizationPercent), enforceGpuPercentageRange: true);
        var disk = CompareSamplePeaks(reference, later,
            sample => (sample.Disks ?? []).Select(device => device.ActivePercent));

        var adapterInstances = reference.Concat(later).SelectMany(sample => sample.GpuMemory ?? [])
            .Select(memory => memory.AdapterInstance).Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var gpuMemory = adapterInstances.Select(adapter =>
        {
            static (double? Average, int Count) Summarize(IReadOnlyList<PerformanceObservation> samples, string adapterName)
            {
                var values = samples.SelectMany(sample => sample.GpuMemory ?? [])
                    .Where(memory => string.Equals(memory.AdapterInstance, adapterName, StringComparison.OrdinalIgnoreCase))
                    .Select(memory => memory.DedicatedUsageBytes).Where(value => value.HasValue).Select(value => (double)value!.Value).ToArray();
                return (values.Length == 0 ? null : values.Average(), values.Length);
            }

            static (double? Average, int Count) SummarizeOccupancy(IReadOnlyList<PerformanceObservation> samples, string adapterName)
            {
                var values = samples.SelectMany(sample => sample.GpuMemory ?? [])
                    .Where(memory => string.Equals(memory.AdapterInstance, adapterName, StringComparison.OrdinalIgnoreCase))
                    .Select(memory => memory.DedicatedOccupancyPercent).Where(value => value is { } number && double.IsFinite(number) && number >= 0)
                    .Select(value => value!.Value).ToArray();
                return (values.Length == 0 ? null : values.Average(), values.Length);
            }

            var left = Summarize(reference, adapter);
            var right = Summarize(later, adapter);
            var leftOccupancy = SummarizeOccupancy(reference, adapter);
            var rightOccupancy = SummarizeOccupancy(later, adapter);
            return new PerformanceGpuMemoryComparison(adapter, left.Average, right.Average, left.Count, right.Count,
                leftOccupancy.Average, rightOccupancy.Average);
        }).ToArray();

        return new(reference.Count, later.Count,
            Average(reference.Select(sample => sample.CpuPercent)),
            Average(later.Select(sample => sample.CpuPercent)),
            Average(reference.Select(UsedMemoryPercent)),
            Average(later.Select(UsedMemoryPercent)),
            reference.Max(sample => sample.CollectedAt), later.Max(sample => sample.CollectedAt), gpu, disk, gpuMemory);
    }
}
