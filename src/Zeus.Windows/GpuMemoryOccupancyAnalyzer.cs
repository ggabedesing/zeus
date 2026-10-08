namespace Zeus.Windows;

public enum GpuMemoryOccupancyState { InsufficientEvidence, NoSustainedHighOccupancy, SustainedHighOccupancy }

/// <summary>
/// Describes dedicated-memory occupancy over a workload window. High occupancy is
/// a signal for review, not proof of memory pressure, a bottleneck, or lost performance.
/// </summary>
public sealed record GpuMemoryOccupancyAssessment(
    string AdapterInstance,
    GpuMemoryOccupancyState State,
    int ValidSamples,
    TimeSpan Window,
    double? AverageOccupancyPercent,
    double? PeakOccupancyPercent,
    int HighOccupancySamples);

public static class GpuMemoryOccupancyAnalyzer
{
    public const int MinimumSamples = 5;
    public static readonly TimeSpan MinimumWindow = TimeSpan.FromSeconds(10);
    public const double HighOccupancyThresholdPercent = 90;
    private const double RequiredHighSampleFraction = 0.8;

    public static IReadOnlyList<GpuMemoryOccupancyAssessment> Assess(IEnumerable<PerformanceObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        var samples = observations.ToArray();
        var adapters = samples.SelectMany(sample => sample.GpuMemory ?? [])
            .Select(memory => memory.AdapterInstance)
            .Where(adapter => !string.IsNullOrWhiteSpace(adapter))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return adapters.Select(adapter => AssessAdapter(samples, adapter)).ToArray();
    }

    private static GpuMemoryOccupancyAssessment AssessAdapter(PerformanceObservation[] samples, string adapter)
    {
        var values = samples.SelectMany(sample => (sample.GpuMemory ?? [])
                .Where(memory => string.Equals(memory.AdapterInstance, adapter, StringComparison.OrdinalIgnoreCase))
                .Select(memory => (sample.CollectedAt, memory.DedicatedOccupancyPercent)))
            .Where(item => item.DedicatedOccupancyPercent is { } percent && double.IsFinite(percent) && percent is >= 0 and <= 100)
            .OrderBy(item => item.CollectedAt)
            .ToArray();
        var window = values.Length < 2 ? TimeSpan.Zero : values[^1].CollectedAt - values[0].CollectedAt;
        var highSamples = values.Count(item => item.DedicatedOccupancyPercent >= HighOccupancyThresholdPercent);
        var enoughEvidence = values.Length >= MinimumSamples && window >= MinimumWindow;
        var highOccupancyIsSustained = enoughEvidence && highSamples / (double)values.Length >= RequiredHighSampleFraction;
        return new(adapter,
            !enoughEvidence ? GpuMemoryOccupancyState.InsufficientEvidence : highOccupancyIsSustained
                ? GpuMemoryOccupancyState.SustainedHighOccupancy : GpuMemoryOccupancyState.NoSustainedHighOccupancy,
            values.Length,
            window,
            values.Length == 0 ? null : values.Average(item => item.DedicatedOccupancyPercent!.Value),
            values.Length == 0 ? null : values.Max(item => item.DedicatedOccupancyPercent!.Value),
            highSamples);
    }
}
