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
    IReadOnlyList<PerformanceGpuMemoryComparison>? GpuMemoryUsage = null,
    PerformanceMetricComparison? CpuUsage = null,
    PerformanceMetricComparison? MemoryUsage = null,
    IReadOnlyList<PerformanceNetworkComparison>? NetworkTraffic = null,
    IReadOnlyList<PerformanceDiskComparison>? DiskIo = null,
    PerformanceActivityContextComparison? ActivityContext = null,
    IReadOnlyList<PerformanceProcessComparison>? ProcessUsage = null);

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

public sealed record PerformanceNetworkComparison(
    string Adapter,
    double? ReferenceBytesPerSecond,
    double? LaterBytesPerSecond,
    int ReferenceAvailableSamples,
    int LaterAvailableSamples);

public sealed record PerformanceDiskComparison(
    string InstanceName,
    double? ReferenceBytesPerSecond,
    double? LaterBytesPerSecond,
    int ReferenceThroughputSamples,
    int LaterThroughputSamples,
    double? ReferenceReadLatencyMilliseconds,
    double? LaterReadLatencyMilliseconds,
    int ReferenceLatencySamples,
    int LaterLatencySamples);

public sealed record PerformanceActivityContextComparison(
    int ReferenceAvailableSamples,
    int LaterAvailableSamples,
    int ReferenceGameDetectedSamples,
    int LaterGameDetectedSamples,
    int ReferenceObsDetectedSamples,
    int LaterObsDetectedSamples,
    int ReferenceObsEncoderKnownSamples,
    int LaterObsEncoderKnownSamples,
    int ReferenceObsEncoderActiveSamples,
    int LaterObsEncoderActiveSamples);

public sealed record PerformanceProcessComparison(
    int ProcessId,
    string Name,
    long StartTimeUtcTicks,
    double? ReferenceCpuPercent,
    double? LaterCpuPercent,
    int ReferenceCpuSamples,
    int LaterCpuSamples,
    double? ReferenceWorkingSetBytes,
    double? LaterWorkingSetBytes,
    int ReferenceWorkingSetSamples,
    int LaterWorkingSetSamples);

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

/// <summary>Sampling interval based on the busiest valid CPU, GPU, disk, or network signal.</summary>
public static class AdaptiveSamplingPolicy
{
    public static TimeSpan NextInterval(PerformanceObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        var loadSignals = new List<double>();
        AddPercent(observation.CpuPercent, loadSignals);
        foreach (var process in observation.Processes) AddPercent(process.CpuPercent, loadSignals);
        foreach (var engine in observation.GpuEngines ?? []) AddPercent(engine.UtilizationPercent, loadSignals);
        foreach (var disk in observation.Disks ?? []) AddPercent(disk.ActivePercent, loadSignals);
        foreach (var network in observation.Networks ?? [])
            if (network.BytesPerSecond is { } bytes && network.LinkBitsPerSecond is { } linkBitsPerSecond && linkBitsPerSecond > 0)
                AddPercent(bytes * 8d / linkBitsPerSecond * 100d, loadSignals);

        if (loadSignals.Count == 0) return TimeSpan.FromSeconds(10);
        var busiest = loadSignals.Max();
        if (busiest >= 75) return TimeSpan.FromSeconds(2);
        if (busiest >= 35) return TimeSpan.FromSeconds(5);
        return TimeSpan.FromSeconds(10);
    }

    private static void AddPercent(double? value, ICollection<double> values)
    {
        if (value is { } percent && double.IsFinite(percent) && percent is >= 0 and <= 100)
            values.Add(percent);
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

        static PerformanceMetricComparison ComparePercentages(
            IReadOnlyList<PerformanceObservation> first,
            IReadOnlyList<PerformanceObservation> second,
            Func<PerformanceObservation, double?> selector)
        {
            static (double? Average, int Count) Summarize(
                IReadOnlyList<PerformanceObservation> samples,
                Func<PerformanceObservation, double?> valueSelector)
            {
                var valid = samples.Select(valueSelector)
                    .Where(value => value is { } number && double.IsFinite(number) && number is >= 0 and <= 100)
                    .Select(value => value!.Value).ToArray();
                return (valid.Length == 0 ? null : valid.Average(), valid.Length);
            }

            var left = Summarize(first, selector);
            var right = Summarize(second, selector);
            return new(left.Average, right.Average, left.Count, right.Count);
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

        var cpuUsage = ComparePercentages(reference, later, sample => sample.CpuPercent);
        var memoryUsage = ComparePercentages(reference, later, UsedMemoryPercent);

        var networkAdapters = reference.Concat(later).SelectMany(sample => sample.Networks ?? [])
            .Select(network => network.Adapter).Where(adapter => !string.IsNullOrWhiteSpace(adapter))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var networkTraffic = networkAdapters.Select(adapter =>
        {
            static (double? Average, int Count) Summarize(IReadOnlyList<PerformanceObservation> samples, string adapterName)
            {
                var values = samples.SelectMany(sample => sample.Networks ?? [])
                    .Where(network => string.Equals(network.Adapter, adapterName, StringComparison.OrdinalIgnoreCase))
                    .Select(network => network.BytesPerSecond).Where(value => value.HasValue)
                    .Select(value => (double)value!.Value).ToArray();
                return (values.Length == 0 ? null : values.Average(), values.Length);
            }

            var left = Summarize(reference, adapter);
            var right = Summarize(later, adapter);
            return new PerformanceNetworkComparison(adapter, left.Average, right.Average, left.Count, right.Count);
        }).ToArray();

        var diskInstances = reference.Concat(later).SelectMany(sample => sample.Disks ?? [])
            .Select(disk => disk.InstanceName).Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var diskIo = diskInstances.Select(instance =>
        {
            static (double? Average, int Count) Summarize(IReadOnlyList<PerformanceObservation> samples,
                string diskName, Func<DiskPerformanceObservation, double?> selector)
            {
                var values = samples.SelectMany(sample => sample.Disks ?? [])
                    .Where(disk => string.Equals(disk.InstanceName, diskName, StringComparison.OrdinalIgnoreCase))
                    .Select(selector).Where(value => value is { } number && double.IsFinite(number) && number >= 0)
                    .Select(value => value!.Value).ToArray();
                return (values.Length == 0 ? null : values.Average(), values.Length);
            }

            static double? BytesPerSecond(DiskPerformanceObservation disk) => disk.BytesPerSecond is { } bytes ? bytes : null;
            static double? ReadLatency(DiskPerformanceObservation disk) => disk.AverageReadLatencyMilliseconds;
            var referenceThroughput = Summarize(reference, instance, BytesPerSecond);
            var laterThroughput = Summarize(later, instance, BytesPerSecond);
            var referenceLatency = Summarize(reference, instance, ReadLatency);
            var laterLatency = Summarize(later, instance, ReadLatency);
            return new PerformanceDiskComparison(instance,
                referenceThroughput.Average, laterThroughput.Average, referenceThroughput.Count, laterThroughput.Count,
                referenceLatency.Average, laterLatency.Average, referenceLatency.Count, laterLatency.Count);
        }).ToArray();

        static (int Available, int Games, int Obs, int EncoderKnown, int EncoderActive) SummarizeContext(
            IReadOnlyList<PerformanceObservation> samples)
        {
            var contexts = samples.Select(sample => sample.ActivityContext).Where(context => context is not null).ToArray();
            return (contexts.Length,
                contexts.Count(context => context!.KnownGameProcessDetected),
                contexts.Count(context => context!.ObsProcessDetected),
                contexts.Count(context => context!.ObsVideoEncodeEngineActive.HasValue),
                contexts.Count(context => context!.ObsVideoEncodeEngineActive == true));
        }

        var referenceContext = SummarizeContext(reference);
        var laterContext = SummarizeContext(later);
        PerformanceActivityContextComparison? activityContext = referenceContext.Available + laterContext.Available == 0
            ? null
            : new(referenceContext.Available, laterContext.Available,
                referenceContext.Games, laterContext.Games,
                referenceContext.Obs, laterContext.Obs,
                referenceContext.EncoderKnown, laterContext.EncoderKnown,
                referenceContext.EncoderActive, laterContext.EncoderActive);

        var processKeys = reference.Concat(later).SelectMany(sample => sample.Processes)
            .Where(process => process.StartTimeUtcTicks is > 0)
            .Select(process => (process.Id, StartTime: process.StartTimeUtcTicks!.Value))
            .Distinct().ToArray();
        var processUsage = processKeys.Select(key =>
        {
            static (double? Cpu, int CpuCount, double? WorkingSet, int WorkingSetCount, string? Name)
                Summarize(IReadOnlyList<PerformanceObservation> samples, (int Id, long StartTime) identity)
            {
                var values = samples.SelectMany(sample => sample.Processes)
                    .Where(process => process.Id == identity.Id && process.StartTimeUtcTicks == identity.StartTime)
                    .ToArray();
                var cpu = values.Select(process => process.CpuPercent)
                    .Where(value => value is { } percent && double.IsFinite(percent) && percent is >= 0 and <= 100)
                    .Select(value => value!.Value).ToArray();
                var workingSet = values.Select(process => (double)process.WorkingSetBytes).ToArray();
                return (cpu.Length == 0 ? null : cpu.Average(), cpu.Length,
                    workingSet.Length == 0 ? null : workingSet.Average(), workingSet.Length,
                    values.LastOrDefault()?.Name);
            }

            var left = Summarize(reference, key);
            var right = Summarize(later, key);
            return new PerformanceProcessComparison(key.Id, right.Name ?? left.Name ?? "processo desconhecido", key.StartTime,
                left.Cpu, right.Cpu, left.CpuCount, right.CpuCount,
                left.WorkingSet, right.WorkingSet, left.WorkingSetCount, right.WorkingSetCount);
        }).ToArray();

        return new(reference.Count, later.Count,
            cpuUsage.ReferencePercent, cpuUsage.LaterPercent,
            memoryUsage.ReferencePercent, memoryUsage.LaterPercent,
            reference.Max(sample => sample.CollectedAt), later.Max(sample => sample.CollectedAt), gpu, disk, gpuMemory,
            cpuUsage, memoryUsage, networkTraffic, diskIo, activityContext, processUsage);
    }
}
