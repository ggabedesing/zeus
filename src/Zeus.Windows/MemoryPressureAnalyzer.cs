namespace Zeus.Windows;

public enum MemoryPressureSignalState { InsufficientEvidence, NoSustainedCombinedSignal, SustainedLowMemoryWithPageReads }

/// <summary>Describes sustained low available memory together with hard page reads; it is a review signal, not a diagnosis.</summary>
public sealed record MemoryPressureAssessment(
    MemoryPressureSignalState State,
    int ValidSamples,
    TimeSpan Window,
    int LowAvailableSamples,
    int PageReadSamples,
    int CombinedSamples,
    double? AverageAvailablePercent,
    double? AveragePageReadsPerSecond);

public static class MemoryPressureAnalyzer
{
    public const int MinimumSamples = 5;
    public static readonly TimeSpan MinimumWindow = TimeSpan.FromSeconds(10);
    public const double LowAvailableThresholdPercent = 10;
    private const double RequiredCombinedSampleFraction = 0.8;

    public static MemoryPressureAssessment Assess(IEnumerable<PerformanceObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        var values = observations
            .Where(sample => sample.TotalMemoryBytes > 0 && sample.AvailableMemoryBytes <= sample.TotalMemoryBytes &&
                sample.MemoryPaging?.PageReadsPerSecond is { } pageReads && double.IsFinite(pageReads) && pageReads >= 0)
            .Select(sample => (
                sample.CollectedAt,
                AvailablePercent: sample.AvailableMemoryBytes / (double)sample.TotalMemoryBytes * 100,
                PageReadsPerSecond: sample.MemoryPaging!.PageReadsPerSecond!.Value))
            .OrderBy(sample => sample.CollectedAt)
            .ToArray();

        var window = values.Length < 2 ? TimeSpan.Zero : values[^1].CollectedAt - values[0].CollectedAt;
        var lowAvailable = values.Count(sample => sample.AvailablePercent <= LowAvailableThresholdPercent);
        var pageReads = values.Count(sample => sample.PageReadsPerSecond > 0);
        var combined = values.Count(sample => sample.AvailablePercent <= LowAvailableThresholdPercent && sample.PageReadsPerSecond > 0);
        var enoughEvidence = values.Length >= MinimumSamples && window >= MinimumWindow;
        var sustainedSignal = enoughEvidence && combined / (double)values.Length >= RequiredCombinedSampleFraction;

        return new(!enoughEvidence ? MemoryPressureSignalState.InsufficientEvidence : sustainedSignal
                ? MemoryPressureSignalState.SustainedLowMemoryWithPageReads
                : MemoryPressureSignalState.NoSustainedCombinedSignal,
            values.Length,
            window,
            lowAvailable,
            pageReads,
            combined,
            values.Length == 0 ? null : values.Average(sample => sample.AvailablePercent),
            values.Length == 0 ? null : values.Average(sample => sample.PageReadsPerSecond));
    }
}
