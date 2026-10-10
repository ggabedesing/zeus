namespace Zeus.Windows;

public enum ObsObservationState { Disabled, Complete, Partial, Unavailable, TimedOut, Failed }

/// <summary>Read-only local OBS evidence. Null means unavailable; counters and interval percentages are distinct.</summary>
public sealed record ObsObservation(
    DateTimeOffset StartedAt, DateTimeOffset FinishedAt, Guid? ConnectionId,
    ObsObservationState State, string Summary,
    bool? Streaming = null, bool? Recording = null, bool? Reconnecting = null,
    double? ActiveFps = null, double? CpuUsage = null, double? MemoryMegabytes = null,
    double? AverageFrameRenderMilliseconds = null,
    long? RenderSkippedFrames = null, long? RenderTotalFrames = null,
    long? OutputSkippedFrames = null, long? OutputTotalFrames = null,
    long? StreamSkippedFrames = null, long? StreamTotalFrames = null,
    long? StreamDurationMilliseconds = null,
    double? RenderSkippedPercent = null, double? OutputSkippedPercent = null,
    double? StreamSkippedPercent = null,
    DateTimeOffset? StatsReadAt = null, DateTimeOffset? StreamReadAt = null, DateTimeOffset? RecordReadAt = null,
    DateTimeOffset? PreviousStatsReadAt = null, DateTimeOffset? PreviousStreamReadAt = null,
    bool? RecordingPaused = null)
{
    public static ObsObservation Disabled() => new(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
        null, ObsObservationState.Disabled, "Leitura local do OBS desativada. Transmissão, gravação e codificador não verificados.");
}
