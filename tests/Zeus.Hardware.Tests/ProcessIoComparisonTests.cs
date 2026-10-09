using System.Text.Json;
using Zeus.Windows;

namespace Zeus.Hardware.Tests;

public sealed class ProcessIoComparisonTests
{
    [Fact]
    public void AveragesEachIoFieldUsingOnlyItsAvailableSamples()
    {
        var comparison = Compare(
            [Sample(Process(read: 100, write: null, other: 0)), Sample(Process(read: 300, write: 40, other: null))],
            [Sample(Process(read: 600, write: 80, other: 10)), Sample(Process(read: null, write: 120, other: 30))]);

        Assert.Equal(200d, comparison.ReferenceIoReadBytesPerSecond);
        Assert.Equal(2, comparison.ReferenceIoReadSamples);
        Assert.Equal(600d, comparison.LaterIoReadBytesPerSecond);
        Assert.Equal(1, comparison.LaterIoReadSamples);
        Assert.Equal(40d, comparison.ReferenceIoWriteBytesPerSecond);
        Assert.Equal(1, comparison.ReferenceIoWriteSamples);
        Assert.Equal(100d, comparison.LaterIoWriteBytesPerSecond);
        Assert.Equal(2, comparison.LaterIoWriteSamples);
        Assert.Equal(0d, comparison.ReferenceIoOtherBytesPerSecond);
        Assert.Equal(1, comparison.ReferenceIoOtherSamples);
        Assert.Equal(20d, comparison.LaterIoOtherBytesPerSecond);
        Assert.Equal(2, comparison.LaterIoOtherSamples);
        Assert.Equal(10d, comparison.ReferenceCpuPercent);
        Assert.Equal(2, comparison.ReferenceCpuSamples);
        Assert.Equal(1_024d, comparison.LaterWorkingSetBytes);
    }

    [Fact]
    public void IgnoresNegativeNonFiniteAndMissingRatesWithoutInventingZero()
    {
        var invalid = new double?[] { null, -1, double.NaN, double.PositiveInfinity, double.NegativeInfinity };
        var samples = invalid.Select(value => Sample(Process(read: value, write: value, other: value))).ToArray();
        var comparison = Compare(samples, [Sample(Process(read: 0, write: 0, other: 0))]);

        Assert.Null(comparison.ReferenceIoReadBytesPerSecond);
        Assert.Null(comparison.ReferenceIoWriteBytesPerSecond);
        Assert.Null(comparison.ReferenceIoOtherBytesPerSecond);
        Assert.Equal(0, comparison.ReferenceIoReadSamples);
        Assert.Equal(0, comparison.ReferenceIoWriteSamples);
        Assert.Equal(0, comparison.ReferenceIoOtherSamples);
        Assert.Equal(0d, comparison.LaterIoReadBytesPerSecond);
        Assert.Equal(1, comparison.LaterIoReadSamples);
    }

    [Fact]
    public void KeepsMissingPeriodUnavailableForProcessPresentInOnlyOnePeriod()
    {
        var comparison = Compare([Sample(Process(read: 40, write: 50, other: 60))], [Sample()]);

        Assert.Equal(40d, comparison.ReferenceIoReadBytesPerSecond);
        Assert.Null(comparison.LaterIoReadBytesPerSecond);
        Assert.Null(comparison.LaterIoWriteBytesPerSecond);
        Assert.Null(comparison.LaterIoOtherBytesPerSecond);
        Assert.Equal(0, comparison.LaterIoReadSamples);
        Assert.Equal(0, comparison.LaterIoWriteSamples);
        Assert.Equal(0, comparison.LaterIoOtherSamples);
    }

    [Fact]
    public void SeparatesReusedPidAndExcludesUnknownStartTime()
    {
        var comparison = PerformanceComparisonBuilder.Compare(
            [Sample(Process(start: 100, read: 40), Process(start: null, read: 999))],
            [Sample(Process(start: 200, read: 80))]);

        Assert.Equal(2, comparison.ProcessUsage!.Count);
        var oldProcess = Assert.Single(comparison.ProcessUsage, value => value.StartTimeUtcTicks == 100);
        var newProcess = Assert.Single(comparison.ProcessUsage, value => value.StartTimeUtcTicks == 200);
        Assert.Equal(40d, oldProcess.ReferenceIoReadBytesPerSecond);
        Assert.Null(oldProcess.LaterIoReadBytesPerSecond);
        Assert.Null(newProcess.ReferenceIoReadBytesPerSecond);
        Assert.Equal(80d, newProcess.LaterIoReadBytesPerSecond);
    }

    [Fact]
    public void KeepsLargeFiniteRatesFiniteWhenAveraging()
    {
        var comparison = Compare(
            [Sample(Process(read: double.MaxValue)), Sample(Process(read: double.MaxValue))],
            [Sample(Process())]);

        Assert.Equal(double.MaxValue, comparison.ReferenceIoReadBytesPerSecond);
        Assert.Equal(2, comparison.ReferenceIoReadSamples);
    }

    [Fact]
    public void PrefersIoListWithoutCountingSameIdentityTwicePerSample()
    {
        var reference = Sample(Process(read: 900)) with { IoProcesses = [Process(read: 100), Process(read: 100)] };
        var later = Sample() with { IoProcesses = [Process(read: 200, write: 300)] };
        var comparison = Compare([reference], [later]);

        Assert.Equal(100d, comparison.ReferenceIoReadBytesPerSecond);
        Assert.Equal(1, comparison.ReferenceIoReadSamples);
        Assert.Equal(200d, comparison.LaterIoReadBytesPerSecond);
        Assert.Equal(1, comparison.LaterIoReadSamples);
        Assert.Equal(300d, comparison.LaterIoWriteBytesPerSecond);
        Assert.Null(comparison.LaterCpuPercent);
        Assert.Equal(0, comparison.LaterCpuSamples);
    }

    [Fact]
    public void IncludesProcessPresentOnlyInIoListWhileCpuAndMemoryStayUnavailable()
    {
        var reference = Sample() with { IoProcesses = [Process(read: 20)] };
        var later = Sample() with { IoProcesses = [Process(read: 40)] };
        var comparison = Compare([reference], [later]);

        Assert.Equal("app", comparison.Name);
        Assert.Equal(20d, comparison.ReferenceIoReadBytesPerSecond);
        Assert.Equal(40d, comparison.LaterIoReadBytesPerSecond);
        Assert.Null(comparison.ReferenceCpuPercent);
        Assert.Null(comparison.ReferenceWorkingSetBytes);
        Assert.Equal(0, comparison.ReferenceCpuSamples);
        Assert.Equal(0, comparison.ReferenceWorkingSetSamples);
    }

    [Fact]
    public void LegacyProcessAndComparisonJsonKeepIoUnknown()
    {
        var observation = JsonSerializer.Deserialize<ProcessObservation>(
            """{"Id":42,"Name":"app","CpuPercent":10,"WorkingSetBytes":1024,"StartTimeUtcTicks":100}""")!;
        var comparison = JsonSerializer.Deserialize<PerformanceProcessComparison>(
            """{"ProcessId":42,"Name":"app","StartTimeUtcTicks":100,"ReferenceCpuPercent":10,"LaterCpuPercent":20,"ReferenceCpuSamples":1,"LaterCpuSamples":1,"ReferenceWorkingSetBytes":1024,"LaterWorkingSetBytes":2048,"ReferenceWorkingSetSamples":1,"LaterWorkingSetSamples":1}""")!;

        Assert.Null(observation.IoReadBytesPerSecond);
        Assert.Null(observation.IoWriteBytesPerSecond);
        Assert.Null(observation.IoOtherBytesPerSecond);
        Assert.Null(comparison.ReferenceIoReadBytesPerSecond);
        Assert.Null(comparison.LaterIoReadBytesPerSecond);
        Assert.Null(comparison.ReferenceIoWriteBytesPerSecond);
        Assert.Null(comparison.LaterIoWriteBytesPerSecond);
        Assert.Null(comparison.ReferenceIoOtherBytesPerSecond);
        Assert.Null(comparison.LaterIoOtherBytesPerSecond);
        Assert.Equal(0, comparison.ReferenceIoReadSamples);
        Assert.Equal(0, comparison.LaterIoReadSamples);
        Assert.Equal(0, comparison.ReferenceIoWriteSamples);
        Assert.Equal(0, comparison.LaterIoWriteSamples);
        Assert.Equal(0, comparison.ReferenceIoOtherSamples);
        Assert.Equal(0, comparison.LaterIoOtherSamples);
    }

    private static PerformanceProcessComparison Compare(
        IReadOnlyList<PerformanceObservation> reference, IReadOnlyList<PerformanceObservation> later) =>
        Assert.Single(PerformanceComparisonBuilder.Compare(reference, later).ProcessUsage!);

    private static ProcessObservation Process(long? start = 100, double? read = null,
        double? write = null, double? other = null) =>
        new(42, "app", 10, 1_024, start, 0.5, read, write, other);

    private static PerformanceObservation Sample(params ProcessObservation[] processes) =>
        new(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1), 20, 8_192, 4_096, processes, []);
}
