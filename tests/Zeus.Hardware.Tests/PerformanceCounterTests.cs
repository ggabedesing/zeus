using Zeus.Windows;
using System.Text.Json;
using CpuTimes = Zeus.Windows.WindowsPerformanceProbe.SystemCpuTimes;

namespace Zeus.Hardware.Tests;

public sealed class PerformanceCounterTests
{
    [Fact]
    public void MapsGpuEnginePidToSampledProcessNameWithoutInventingMissingNames()
    {
        var engines = new GpuEngineObservation[]
        {
            new("pid_42_eng_0_engtype_3D", 42, "3D", 60),
            new("pid_99_eng_0_engtype_3D", 99, "3D", 20),
            new("_Total_eng_0_engtype_3D", null, "3D", 10)
        };
        var processes = new ProcessObservation[] { new(42, "game", 30, 1024) };

        var mapped = WindowsPerformanceProbe.MapGpuEnginesToProcesses(engines, processes);

        Assert.Equal("game", mapped[0].ProcessName);
        Assert.Null(mapped[1].ProcessName);
        Assert.Null(mapped[2].ProcessName);
    }

    [Fact]
    public void DedicatedGpuOccupancyRequiresBothUsageAndReportedCapacity()
    {
        Assert.Equal(75d, new GpuMemoryObservation("gpu", 3, 0, 3, 4).DedicatedOccupancyPercent);
        Assert.Null(new GpuMemoryObservation("gpu", 3, 0, 3, 0).DedicatedOccupancyPercent);
        Assert.Null(new GpuMemoryObservation("gpu", null, 0, 3, 4).DedicatedOccupancyPercent);
    }

    [Fact]
    public void DxgiLuidMapsToWindowsGpuCounterInstanceFormat()
    {
        var instance = DxgiAdapterMemoryReader.FormatInstance(new() { HighPart = 0, LowPart = 0x1057F });
        Assert.Equal("luid_0x00000000_0x0001057F_phys_0", instance);
    }

    [Fact]
    public void HistoryBufferRetainsOnlyNewestEntriesAndPreservesSessions()
    {
        var buffer = new PerformanceHistoryBuffer(2);
        var firstSession = Guid.NewGuid();
        var secondSession = Guid.NewGuid();
        buffer.Add(new(firstSession, Sample(10)));
        buffer.Add(new(firstSession, Sample(20)));
        buffer.Add(new(secondSession, Sample(30)));

        var snapshot = buffer.Snapshot();
        Assert.Equal(2, snapshot.Count);
        Assert.Equal(new double?[] { 20d, 30d }, snapshot.Select(entry => entry.Observation.CpuPercent).ToArray());
        Assert.Equal(secondSession, snapshot[1].SessionId);
    }

    [Theory]
    [InlineData(90d, 2)]
    [InlineData(50d, 5)]
    [InlineData(10d, 10)]
    [InlineData(null, 10)]
    public void AdaptivePolicySamplesMoreOftenUnderHigherLoad(double? cpu, int seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), AdaptiveSamplingPolicy.NextInterval(Sample(cpu)));
    }

    [Fact]
    public void ComparisonUsesOnlyValidSamplesAndReportsAverageCpuAndMemoryUse()
    {
        var reference = new[] { Sample(20), Sample(40) };
        var later = new[]
        {
            Sample(50) with { AvailableMemoryBytes = 256 },
            Sample(70) with { AvailableMemoryBytes = 256 }
        };

        var comparison = PerformanceComparisonBuilder.Compare(reference, later);

        Assert.Equal(30d, comparison.ReferenceCpuPercent!.Value);
        Assert.Equal(60d, comparison.LaterCpuPercent!.Value);
        Assert.Equal(50d, comparison.ReferenceUsedMemoryPercent!.Value, 6);
        Assert.Equal(75d, comparison.LaterUsedMemoryPercent!.Value, 6);
        Assert.Equal(2, comparison.ReferenceSampleCount);
        Assert.Equal(2, comparison.LaterSampleCount);
    }

    [Fact]
    public void ComparisonReportsPerSamplePeakGpuEngineAndDiskActivityWithCoverage()
    {
        var reference = new[]
        {
            Sample(20) with { GpuEngines = [new("a", 1, "3D", 40), new("b", 2, "Copy", 70)], Disks = [new("0", null, 20, null)] },
            Sample(30) with { GpuEngines = [], Disks = [new("0", null, 10, null), new("1", null, 30, null)] }
        };
        var later = new[]
        {
            Sample(40) with { GpuEngines = [new("a", 1, "3D", 30)], Disks = [new("0", null, 40, null)] },
            Sample(50) with { GpuEngines = [new("a", 1, "3D", 50), new("b", 2, "Copy", 60)], Disks = [] }
        };

        var comparison = PerformanceComparisonBuilder.Compare(reference, later);

        Assert.Equal(70d, comparison.GpuEnginePeak!.ReferencePercent);
        Assert.Equal(45d, comparison.GpuEnginePeak.LaterPercent);
        Assert.Equal(1, comparison.GpuEnginePeak.ReferenceAvailableSamples);
        Assert.Equal(2, comparison.GpuEnginePeak.LaterAvailableSamples);
        Assert.Equal(25d, comparison.DiskActivityPeak!.ReferencePercent);
        Assert.Equal(40d, comparison.DiskActivityPeak.LaterPercent);
        Assert.Equal(2, comparison.DiskActivityPeak.ReferenceAvailableSamples);
        Assert.Equal(1, comparison.DiskActivityPeak.LaterAvailableSamples);
    }

    [Fact]
    public void ComparisonAveragesGpuDedicatedUsageByAdapterAndKeepsMissingSamplesUnavailable()
    {
        var reference = new[]
        {
            Sample(20) with { GpuMemory = [new("luid_gpu_a", 100, 200, 300, 1_000)] },
            Sample(30) with { GpuMemory = [new("luid_gpu_a", 300, 400, 700, 1_000), new("luid_gpu_b", 50, 60, 110, 100)] }
        };
        var later = new[]
        {
            Sample(40) with { GpuMemory = [new("luid_gpu_a", 500, 600, 1100, 1_000)] },
            Sample(50) with { GpuMemory = [] }
        };

        var comparison = PerformanceComparisonBuilder.Compare(reference, later);

        var gpuA = Assert.Single(comparison.GpuMemoryUsage!, item => item.AdapterInstance == "luid_gpu_a");
        Assert.Equal(200, gpuA.ReferenceDedicatedBytes);
        Assert.Equal(500, gpuA.LaterDedicatedBytes);
        Assert.Equal(2, gpuA.ReferenceAvailableSamples);
        Assert.Equal(1, gpuA.LaterAvailableSamples);
        Assert.Equal(20, gpuA.ReferenceOccupancyPercent);
        Assert.Equal(50, gpuA.LaterOccupancyPercent);
        var gpuB = Assert.Single(comparison.GpuMemoryUsage!, item => item.AdapterInstance == "luid_gpu_b");
        Assert.Equal(50, gpuB.ReferenceDedicatedBytes);
        Assert.Null(gpuB.LaterDedicatedBytes);
        Assert.Null(gpuB.LaterOccupancyPercent);
        Assert.Equal(0, gpuB.LaterAvailableSamples);
    }

    [Fact]
    public void ComparisonKeepsUnavailableCountersUnknown()
    {
        var unavailable = Sample(null) with { TotalMemoryBytes = 0, AvailableMemoryBytes = 0 };
        var comparison = PerformanceComparisonBuilder.Compare([unavailable], [unavailable]);
        Assert.Null(comparison.ReferenceCpuPercent);
        Assert.Null(comparison.LaterCpuPercent);
        Assert.Null(comparison.ReferenceUsedMemoryPercent);
        Assert.Null(comparison.LaterUsedMemoryPercent);
        Assert.Null(comparison.GpuEnginePeak!.ReferencePercent);
        Assert.Equal(0, comparison.GpuEnginePeak.ReferenceAvailableSamples);
        Assert.Null(comparison.DiskActivityPeak!.ReferencePercent);
    }

    [Fact]
    public void OlderPersistedComparisonsWithoutNewMetricsLoadWithMetricsUnavailable()
    {
        const string legacy = """{"ReferenceSampleCount":3,"LaterSampleCount":3,"ReferenceCpuPercent":20,"LaterCpuPercent":30,"ReferenceUsedMemoryPercent":40,"LaterUsedMemoryPercent":50,"ReferenceEndedAt":"2026-10-08T12:00:00Z","LaterEndedAt":"2026-10-08T12:05:00Z"}""";

        var comparison = JsonSerializer.Deserialize<PerformanceComparison>(legacy);

        Assert.NotNull(comparison);
        Assert.Null(comparison.GpuEnginePeak);
        Assert.Null(comparison.DiskActivityPeak);
    }

    [Fact]
    public void ActivityContextDistinguishesProcessPresenceFromLiveGameOrStream()
    {
        var obsAndGame = ActivityContextDetector.Detect([
            new(100, "obs64", null, 10), new(200, "FortniteClient-Win64-Shipping", null, 20)
        ]);
        Assert.True(obsAndGame.ObsProcessDetected);
        Assert.True(obsAndGame.KnownGameProcessDetected);
        Assert.Equal(DetectionConfidence.Medium, obsAndGame.Confidence);
        Assert.Contains("não confirma transmissão ao vivo", obsAndGame.Summary);

        var java = ActivityContextDetector.Detect([new(300, "javaw", null, 30)]);
        Assert.False(java.KnownGameProcessDetected);
        Assert.False(java.ObsProcessDetected);
        Assert.Equal(DetectionConfidence.Low, java.Confidence);
    }

    [Fact]
    public void ActivityContextUsesAllAccessibleProcessesAndReportsMatchingObsVideoEncodeEngine()
    {
        var context = ActivityContextDetector.Detect(
            [new(10, "idle", null, 10), new(20, "obs64", 1, 20), new(30, "FortniteClient-Win64-Shipping", null, 30)],
            [new("pid_20_eng_0_engtype_VideoEncode", 20, "VideoEncode", 12.5)]);

        Assert.True(context.ObsProcessDetected);
        Assert.True(context.KnownGameProcessDetected);
        Assert.True(context.ObsVideoEncodeEngineActive);
        Assert.Equal(12.5, context.ObsVideoEncodeEnginePercent);
        Assert.Contains("não confirma transmissão ao vivo", context.Summary);
    }

    [Fact]
    public void ActivityContextKeepsEncoderUnknownWhenNoMatchingObsEngineIsReported()
    {
        var context = ActivityContextDetector.Detect(
            [new(20, "obs64", null, 20)],
            [new("pid_99_eng_0_engtype_VideoEncode", 99, "VideoEncode", 75)]);

        Assert.True(context.ObsProcessDetected);
        Assert.Null(context.ObsVideoEncodeEngineActive);
        Assert.Null(context.ObsVideoEncodeEnginePercent);
        Assert.Contains("indisponível", context.Summary);
    }

    private static PerformanceObservation Sample(double? cpu) =>
        new(DateTimeOffset.UnixEpoch, TimeSpan.FromSeconds(2), cpu, 1024, 512, [], []);

    [Theory]
    [InlineData(0U, false)]
    [InlineData(1U, true)]
    [InlineData(64U, true)]
    [InlineData(65U, false)]
    [InlineData(128U, false)]
    public void TotalCpuRequiresAProviderScopeWithinOneProcessorGroup(uint activeProcessors, bool supported)
    {
        Assert.Equal(supported, WindowsPerformanceProbe.IsTotalCpuScopeSupported(activeProcessors));
    }

    [Fact]
    public void SystemCpuSubtractsIdleFromKernelAndUserCombined()
    {
        // Delta idle=40, kernel=60 (including idle), user=40 => 60% busy.
        var result = WindowsPerformanceProbe.CalculateSystemCpuPercent(new CpuTimes(200, 400, 100),
            new CpuTimes(240, 460, 140));
        Assert.Equal(60d, result!.Value, 8);
    }

    [Fact]
    public void SystemCpuPreservesTrueIdleAndFullyBusyReadings()
    {
        Assert.Equal(0d, WindowsPerformanceProbe.CalculateSystemCpuPercent(new CpuTimes(0, 0, 0), new CpuTimes(100, 100, 0)));
        Assert.Equal(100d, WindowsPerformanceProbe.CalculateSystemCpuPercent(new CpuTimes(0, 0, 0), new CpuTimes(0, 100, 100)));
    }

    [Theory]
    [InlineData(100UL, 0UL, 0UL)] // More idle than combined CPU delta.
    [InlineData(0UL, 0UL, 0UL)] // No elapsed CPU ticks.
    [InlineData(0UL, ulong.MaxValue, 1UL)] // Combined delta would overflow.
    public void SystemCpuRejectsInvalidCountersInsteadOfInventingZero(ulong idle, ulong kernel, ulong user)
    {
        Assert.Null(WindowsPerformanceProbe.CalculateSystemCpuPercent(new CpuTimes(0, 0, 0), new CpuTimes(idle, kernel, user)));
    }

    [Fact]
    public void SystemCpuRejectsEachCounterRollback()
    {
        var before = new CpuTimes(10, 10, 10);
        Assert.Null(WindowsPerformanceProbe.CalculateSystemCpuPercent(before, new CpuTimes(9, 20, 20)));
        Assert.Null(WindowsPerformanceProbe.CalculateSystemCpuPercent(before, new CpuTimes(10, 9, 20)));
        Assert.Null(WindowsPerformanceProbe.CalculateSystemCpuPercent(before, new CpuTimes(10, 20, 9)));
    }

    [Fact]
    public void ProcessCpuIsNormalizedAcrossLogicalProcessors()
    {
        // One full core for two seconds on four logical CPUs => 25% of machine.
        var result = WindowsPerformanceProbe.CalculateProcessCpuPercent(TimeSpan.TicksPerSecond,
            3 * TimeSpan.TicksPerSecond, TimeSpan.FromSeconds(2), 4);
        Assert.Equal(25d, result!.Value, 8);
    }

    [Theory]
    [InlineData(-1L, 0L, 1, 4)]
    [InlineData(10L, 9L, 1, 4)]
    [InlineData(0L, 10L, 0, 4)]
    [InlineData(0L, 10L, 1, 0)]
    public void ProcessCpuRejectsMissingIntervalOrCounterRollback(long first, long last, int seconds, int processors)
    {
        Assert.Null(WindowsPerformanceProbe.CalculateProcessCpuPercent(first, last, TimeSpan.FromSeconds(seconds), processors));
    }

    [Fact]
    public void ProcessCpuCapsReadingsAtMachineCapacity()
    {
        Assert.Equal(100d, WindowsPerformanceProbe.CalculateProcessCpuPercent(0,
            20 * TimeSpan.TicksPerSecond, TimeSpan.FromSeconds(2), 4));
    }

    [Theory]
    [InlineData(-10, 2)]
    [InlineData(0, 2)]
    [InlineData(5, 5)]
    [InlineData(45, 30)]
    public void SamplingDurationIsBounded(int requested, int expected)
    {
        Assert.Equal(TimeSpan.FromSeconds(expected), WindowsPerformanceProbe.ClampDuration(TimeSpan.FromSeconds(requested)));
    }
}
