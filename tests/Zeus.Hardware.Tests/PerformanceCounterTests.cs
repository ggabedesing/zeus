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
    public void ParsesPerProcessGpuMemoryAndKeepsUnknownProcessNameExplicit()
    {
        var processes = new Dictionary<int, ProcessObservation> { [42] = new(42, "game", 10, 1024, 1_234) };
        var known = WindowsPerformanceProbe.ParseGpuProcessMemoryCounter(
            "pid_42_luid_0x00000000_0x0001057F_phys_0", 100, 20, 15, 80, 120, processes);
        var unknown = WindowsPerformanceProbe.ParseGpuProcessMemoryCounter(
            "pid_99_luid_0x00000000_0x0001057F_phys_0", 50, 10, 8, 40, 60, processes);

        Assert.NotNull(known);
        Assert.Equal("luid_0x00000000_0x0001057F_phys_0", known.AdapterInstance);
        Assert.Equal(42, known.ProcessId);
        Assert.Equal("game", known.ProcessName);
        Assert.Equal(1_234, known.ProcessStartTimeUtcTicks);
        Assert.Equal((ulong)100, known.DedicatedUsageBytes);
        Assert.Equal((ulong)120, known.TotalCommittedBytes);
        Assert.NotNull(unknown);
        Assert.Equal(99, unknown.ProcessId);
        Assert.Null(unknown.ProcessName);
        Assert.Null(WindowsPerformanceProbe.ParseGpuProcessMemoryCounter(
            "invalid-instance", 10, 0, 0, 10, 10, processes));
    }

    [Fact]
    public void GpuProcessMemoryComparisonMatchesPidStartTimeAndAdapterAndReportsCoverage()
    {
        const string adapter = "luid_0x00000000_0x0001057F_phys_0";
        var reference = new[]
        {
            Sample(20) with
            {
                Processes = [new(42, "old-game", 10, 1024, 100), new(7, "editor", 10, 1024, 300)],
                GpuProcessMemory = [new("old", adapter, 42, "old-game", 100, 1000, 0, 0, 1000, 1000),
                    new("editor", adapter, 7, "editor", 300, 2000, 0, 0, 2000, 2000)]
            },
            Sample(30) with
            {
                Processes = [new(7, "editor", 10, 1024, 300)],
                GpuProcessMemory = [new("editor", adapter, 7, "editor", 300, 4000, 0, 0, 4000, 4000)]
            }
        };
        var later = new[]
        {
            Sample(40) with
            {
                Processes = [new(42, "new-game", 10, 1024, 200), new(7, "editor", 10, 1024, 300)],
                GpuProcessMemory = [new("new", adapter, 42, "new-game", 200, 9000, 0, 0, 9000, 9000),
                    new("editor", adapter, 7, "editor", 300, 6000, 0, 0, 6000, 6000)]
            }
        };

        var comparison = PerformanceComparisonBuilder.Compare(reference, later);
        var editor = Assert.Single(comparison.GpuProcessMemoryUsage!, item => item.ProcessId == 7);
        Assert.Equal(2, comparison.GpuProcessMemoryUsage!.Count(item => item.ProcessId == 42));
        var oldGame = Assert.Single(comparison.GpuProcessMemoryUsage!, item => item.ProcessId == 42 && item.ProcessStartTimeUtcTicks == 100);
        var newGame = Assert.Single(comparison.GpuProcessMemoryUsage!, item => item.ProcessId == 42 && item.ProcessStartTimeUtcTicks == 200);

        Assert.Equal(3000, editor.ReferenceDedicatedBytes);
        Assert.Equal(6000, editor.LaterDedicatedBytes);
        Assert.Equal(2, editor.ReferenceAvailableSamples);
        Assert.Equal(1, editor.LaterAvailableSamples);
        Assert.Equal(1000, oldGame.ReferenceDedicatedBytes);
        Assert.Null(oldGame.LaterDedicatedBytes);
        Assert.Null(newGame.ReferenceDedicatedBytes);
        Assert.Equal(9000, newGame.LaterDedicatedBytes);
    }

    [Fact]
    public void InterruptedCounterReadPreservesValidRowsAndMarksTheListPartial()
    {
        var rows = new[] { new ProcessObservation(1, "process", 10, 1024) };
        var warnings = new List<string>();

        var result = WindowsPerformanceProbe.HandleCounterReadFailure("GPU", "counter", rows,
            new TimeoutException(), warnings);

        Assert.Same(rows, result);
        Assert.Contains("dados parciais foram preservados", Assert.Single(warnings));
    }

    [Fact]
    public void InterruptedCounterReadWithoutValidRowsRemainsUnavailable()
    {
        var warnings = new List<string>();

        var result = WindowsPerformanceProbe.HandleCounterReadFailure<ProcessObservation>("GPU", "counter", [],
            new TimeoutException(), warnings);

        Assert.Empty(result);
        Assert.Contains("indisponível", Assert.Single(warnings));
    }

    [Fact]
    public void DedicatedGpuOccupancyRequiresBothUsageAndReportedCapacity()
    {
        Assert.Equal(75d, new GpuMemoryObservation("gpu", 3, 0, 3, 4).DedicatedOccupancyPercent);
        Assert.Null(new GpuMemoryObservation("gpu", 3, 0, 3, 0).DedicatedOccupancyPercent);
        Assert.Null(new GpuMemoryObservation("gpu", null, 0, 3, 4).DedicatedOccupancyPercent);
    }

    [Fact]
    public void GpuOccupancySignalRequiresAtLeastFiveValidSamplesAcrossTenSeconds()
    {
        var samples = Enumerable.Range(0, 5).Select(index => GpuSample(index * 2, 950)).ToArray();

        var assessment = Assert.Single(GpuMemoryOccupancyAnalyzer.Assess(samples));

        Assert.Equal(GpuMemoryOccupancyState.InsufficientEvidence, assessment.State);
        Assert.Equal(5, assessment.ValidSamples);
        Assert.Equal(TimeSpan.FromSeconds(8), assessment.Window);
    }

    [Fact]
    public void GpuOccupancySignalReportsSustainedHighOccupancyWithoutCallingItPressure()
    {
        var samples = new[] { GpuSample(0, 950), GpuSample(3, 950), GpuSample(6, 950), GpuSample(9, 950), GpuSample(12, 700) };

        var assessment = Assert.Single(GpuMemoryOccupancyAnalyzer.Assess(samples));

        Assert.Equal(GpuMemoryOccupancyState.SustainedHighOccupancy, assessment.State);
        Assert.Equal(4, assessment.HighOccupancySamples);
        Assert.Equal(90d, GpuMemoryOccupancyAnalyzer.HighOccupancyThresholdPercent);
    }

    [Fact]
    public void GpuOccupancySignalNeedsSustainedThresholdAndKeepsInvalidReadingsOutOfCoverage()
    {
        var samples = new[]
        {
            GpuSample(0, 950), GpuSample(3, 700), GpuSample(6, 950), GpuSample(9, 700), GpuSample(12, 700),
            GpuSample(15, 1200)
        };

        var assessment = Assert.Single(GpuMemoryOccupancyAnalyzer.Assess(samples));

        Assert.Equal(GpuMemoryOccupancyState.NoSustainedHighOccupancy, assessment.State);
        Assert.Equal(5, assessment.ValidSamples);
        Assert.Equal(2, assessment.HighOccupancySamples);
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
    public void AdaptivePolicyRespondsToGpuAndDiskLoadWhenCpuIsUnavailable()
    {
        var gpuBusy = Sample(null) with { GpuEngines = [new("gpu", null, "3D", 82)] };
        var diskBusy = Sample(null) with { Disks = [new("disk", null, 48, null)] };

        Assert.Equal(TimeSpan.FromSeconds(2), AdaptiveSamplingPolicy.NextInterval(gpuBusy));
        Assert.Equal(TimeSpan.FromSeconds(5), AdaptiveSamplingPolicy.NextInterval(diskBusy));
    }

    [Fact]
    public void AdaptivePolicyUsesNormalizedNetworkRateAndIgnoresUnavailableOrInvalidCounters()
    {
        var networkBusy = Sample(null) with
        {
            Networks = [new("Ethernet", 50_000_000, 1_000_000_000, null, null)]
        };
        var unavailable = Sample(null) with
        {
            GpuEngines = [new("gpu", null, "3D", 130)],
            Disks = [new("disk", null, -1, null)],
            Networks = [new("Ethernet", 50_000_000, null, null, null)]
        };

        Assert.Equal(TimeSpan.FromSeconds(5), AdaptiveSamplingPolicy.NextInterval(networkBusy));
        Assert.Equal(TimeSpan.FromSeconds(10), AdaptiveSamplingPolicy.NextInterval(unavailable));
    }

    [Fact]
    public void AdaptivePolicyTreatsAggregateFullDuplexTrafficAboveLinkRateAsSaturated()
    {
        var fullDuplexAggregate = Sample(null) with
        {
            Networks = [new("Ethernet", 150_000_000, 1_000_000_000, null, null)]
        };

        Assert.Equal(TimeSpan.FromSeconds(2), AdaptiveSamplingPolicy.NextInterval(fullDuplexAggregate));
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
        Assert.Equal(2, comparison.CpuUsage!.ReferenceAvailableSamples);
        Assert.Equal(2, comparison.MemoryUsage!.LaterAvailableSamples);
    }

    [Fact]
    public void CpuAndMemoryComparisonExposeCoverageAndIgnoreInvalidPercentages()
    {
        var reference = new[]
        {
            Sample(20),
            Sample(140),
            Sample(null) with { TotalMemoryBytes = 0, AvailableMemoryBytes = 0 }
        };
        var later = new[]
        {
            Sample(30),
            Sample(40) with { TotalMemoryBytes = 0, AvailableMemoryBytes = 0 },
            Sample(50)
        };

        var comparison = PerformanceComparisonBuilder.Compare(reference, later);

        Assert.Equal(20d, comparison.ReferenceCpuPercent);
        Assert.Equal(40d, comparison.LaterCpuPercent);
        Assert.Equal(1, comparison.CpuUsage!.ReferenceAvailableSamples);
        Assert.Equal(3, comparison.CpuUsage.LaterAvailableSamples);
        Assert.Equal(50d, comparison.ReferenceUsedMemoryPercent);
        Assert.Equal(50d, comparison.LaterUsedMemoryPercent);
        Assert.Equal(2, comparison.MemoryUsage!.ReferenceAvailableSamples);
        Assert.Equal(2, comparison.MemoryUsage.LaterAvailableSamples);
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
    public void ComparisonAveragesNetworkTrafficPerAdapterAndReportsCoverage()
    {
        var reference = new[]
        {
            Sample(20) with { Networks = [new("Ethernet", 100, null, null, null)] },
            Sample(30) with { Networks = [new("Ethernet", 300, null, null, null), new("Wi-Fi", null, null, null, null)] }
        };
        var later = new[]
        {
            Sample(40) with { Networks = [new("Ethernet", 500, null, null, null)] },
            Sample(50) with { Networks = [] }
        };

        var comparison = PerformanceComparisonBuilder.Compare(reference, later);

        var ethernet = Assert.Single(comparison.NetworkTraffic!, item => item.Adapter == "Ethernet");
        Assert.Equal(200, ethernet.ReferenceBytesPerSecond);
        Assert.Equal(500, ethernet.LaterBytesPerSecond);
        Assert.Equal(2, ethernet.ReferenceAvailableSamples);
        Assert.Equal(1, ethernet.LaterAvailableSamples);
        var wifi = Assert.Single(comparison.NetworkTraffic!, item => item.Adapter == "Wi-Fi");
        Assert.Null(wifi.ReferenceBytesPerSecond);
        Assert.Null(wifi.LaterBytesPerSecond);
        Assert.Equal(0, wifi.ReferenceAvailableSamples);
    }

    [Fact]
    public void ComparisonAveragesDiskThroughputAndLatencyPerDeviceWithIndependentCoverage()
    {
        var reference = new[]
        {
            Sample(20) with { Disks = [new("disk0", 100, 20, 2), new("disk1", null, null, null)] },
            Sample(30) with { Disks = [new("disk0", 300, 40, null)] }
        };
        var later = new[]
        {
            Sample(40) with { Disks = [new("disk0", 500, 60, 8)] },
            Sample(50) with { Disks = [] }
        };

        var comparison = PerformanceComparisonBuilder.Compare(reference, later);

        var disk = Assert.Single(comparison.DiskIo!, item => item.InstanceName == "disk0");
        Assert.Equal(200, disk.ReferenceBytesPerSecond);
        Assert.Equal(500, disk.LaterBytesPerSecond);
        Assert.Equal(2, disk.ReferenceThroughputSamples);
        Assert.Equal(1, disk.LaterThroughputSamples);
        Assert.Equal(2, disk.ReferenceReadLatencyMilliseconds);
        Assert.Equal(8, disk.LaterReadLatencyMilliseconds);
        Assert.Equal(1, disk.ReferenceLatencySamples);
        Assert.Equal(1, disk.LaterLatencySamples);
        var unavailableDisk = Assert.Single(comparison.DiskIo!, item => item.InstanceName == "disk1");
        Assert.Null(unavailableDisk.ReferenceBytesPerSecond);
        Assert.Equal(0, unavailableDisk.LaterThroughputSamples);
    }

    [Fact]
    public void ComparisonSummarizesGameObsAndEncoderContextWithSeparateCoverage()
    {
        var reference = new[]
        {
            Sample(20) with { ActivityContext = new(true, true, "FortniteClient-Win64-Shipping", DetectionConfidence.Medium, "fixture", true, 20) },
            Sample(30) with { ActivityContext = new(false, false, null, DetectionConfidence.Low, "fixture", null, null) }
        };
        var later = new[]
        {
            Sample(40) with { ActivityContext = new(true, false, null, DetectionConfidence.High, "fixture", false, 0) },
            Sample(50)
        };

        var comparison = PerformanceComparisonBuilder.Compare(reference, later);

        var context = Assert.IsType<PerformanceActivityContextComparison>(comparison.ActivityContext);
        Assert.Equal(2, context.ReferenceAvailableSamples);
        Assert.Equal(1, context.LaterAvailableSamples);
        Assert.Equal(1, context.ReferenceGameDetectedSamples);
        Assert.Equal(0, context.LaterGameDetectedSamples);
        Assert.Equal(1, context.ReferenceObsDetectedSamples);
        Assert.Equal(1, context.LaterObsDetectedSamples);
        Assert.Equal(1, context.ReferenceObsEncoderKnownSamples);
        Assert.Equal(1, context.LaterObsEncoderKnownSamples);
        Assert.Equal(1, context.ReferenceObsEncoderActiveSamples);
        Assert.Equal(0, context.LaterObsEncoderActiveSamples);
    }

    [Fact]
    public void ComparisonMatchesProcessUsageByPidAndStartTimeWithoutMixingReusedPid()
    {
        const long firstStartTime = 638_900_000_000_000_000;
        const long reusedPidStartTime = firstStartTime + 10_000_000;
        var reference = new[]
        {
            Sample(20) with { Processes = [new(42, "game", 20, 1_000, firstStartTime)] },
            Sample(30) with { Processes = [new(42, "game", 40, 3_000, firstStartTime)] }
        };
        var later = new[]
        {
            Sample(40) with { Processes = [new(42, "game", 50, 5_000, firstStartTime), new(42, "game", 99, 9_000, reusedPidStartTime)] },
            Sample(50) with { Processes = [new(42, "game", null, 7_000, firstStartTime)] }
        };

        var comparison = PerformanceComparisonBuilder.Compare(reference, later);

        var continued = Assert.Single(comparison.ProcessUsage!, process => process.StartTimeUtcTicks == firstStartTime);
        Assert.Equal(30, continued.ReferenceCpuPercent);
        Assert.Equal(50, continued.LaterCpuPercent);
        Assert.Equal(2, continued.ReferenceCpuSamples);
        Assert.Equal(1, continued.LaterCpuSamples);
        Assert.Equal(2_000, continued.ReferenceWorkingSetBytes);
        Assert.Equal(6_000, continued.LaterWorkingSetBytes);
        var reused = Assert.Single(comparison.ProcessUsage!, process => process.StartTimeUtcTicks == reusedPidStartTime);
        Assert.Null(reused.ReferenceCpuPercent);
        Assert.Equal(99, reused.LaterCpuPercent);
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
        Assert.Null(comparison.CpuUsage);
        Assert.Null(comparison.MemoryUsage);
        Assert.Null(comparison.NetworkTraffic);
        Assert.Null(comparison.DiskIo);
        Assert.Null(comparison.ActivityContext);
        Assert.Null(comparison.ProcessUsage);
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

    private static PerformanceObservation GpuSample(int seconds, ulong usage) =>
        Sample(20) with
        {
            CollectedAt = DateTimeOffset.UnixEpoch.AddSeconds(seconds),
            GpuMemory = [new("gpu-test", usage, 100, usage + 100, 1_000)]
        };

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
