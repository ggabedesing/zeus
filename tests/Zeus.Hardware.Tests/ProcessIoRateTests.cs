using System.Diagnostics;
using System.Text.Json;
using Zeus.Windows;

namespace Zeus.Hardware.Tests;

public sealed class ProcessIoRateTests
{
    [Fact]
    public void RatesUseProcessIntervalAndPreserveValidZero()
    {
        var first = new ProcessIoCounterSnapshot(7, 1234, 100, 200, 300, 1);
        var last = first with { ReadBytes = 4100, WrittenBytes = 8200, ObservedAt = 1 + 2 * Stopwatch.Frequency };
        var rates = ProcessIoReader.CalculateRates(first, last);
        Assert.Equal(2000, rates.ReadBytesPerSecond);
        Assert.Equal(4000, rates.WriteBytesPerSecond);
        Assert.Equal(0, rates.OtherBytesPerSecond);
        Assert.Equal(2, rates.SamplingDurationSeconds);
    }

    [Fact]
    public void MissingEndpointIdentityOrElapsedTimeCannotInventRates()
    {
        var first = new ProcessIoCounterSnapshot(7, 1234, 100, 200, 300, 1);
        var last = first with { ObservedAt = 1 + Stopwatch.Frequency };
        foreach (var rates in new[]
        {
            ProcessIoReader.CalculateRates(null, last), ProcessIoReader.CalculateRates(first, null),
            ProcessIoReader.CalculateRates(first, last with { ProcessId = 8 }),
            ProcessIoReader.CalculateRates(first, last with { StartTimeUtcTicks = 5678 }),
            ProcessIoReader.CalculateRates(first with { StartTimeUtcTicks = 0 }, last with { StartTimeUtcTicks = 0 }),
            ProcessIoReader.CalculateRates(first, first),
            ProcessIoReader.CalculateRates(last, first)
        })
        {
            Assert.Null(rates.ReadBytesPerSecond);
            Assert.Null(rates.WriteBytesPerSecond);
            Assert.Null(rates.OtherBytesPerSecond);
            Assert.Null(rates.SamplingDurationSeconds);
        }
    }

    [Fact]
    public void CounterResetInvalidatesOnlyTheAffectedField()
    {
        var first = new ProcessIoCounterSnapshot(7, 1234, 100, 200, 300, 1);
        var last = first with { ReadBytes = 50, WrittenBytes = 700, ObservedAt = 1 + Stopwatch.Frequency };
        var rates = ProcessIoReader.CalculateRates(first, last);
        Assert.Null(rates.ReadBytesPerSecond);
        Assert.Equal(500, rates.WriteBytesPerSecond);
        Assert.Equal(0, rates.OtherBytesPerSecond);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidElapsedTimeDoesNotProduceZero(double elapsed) =>
        Assert.Null(ProcessIoReader.Rate(0, 1000, elapsed));

    [Fact]
    public void LargeUnsignedCountersDoNotOverflowBeforeSubtraction()
    {
        Assert.Equal(500, ProcessIoReader.Rate(ulong.MaxValue - 1000, ulong.MaxValue, 2));
        Assert.Null(ProcessIoReader.Rate(ulong.MaxValue, 0, 2));
        Assert.Null(ProcessIoReader.Rate(0, ulong.MaxValue, double.Epsilon));
    }

    [Fact]
    public void LegacyProcessJsonHasUnknownIo()
    {
        var process = JsonSerializer.Deserialize<ProcessObservation>("""{"Id":7,"Name":"old","CpuPercent":1,"WorkingSetBytes":1024}""")!;
        Assert.Null(process.IoReadBytesPerSecond);
        Assert.Null(process.IoWriteBytesPerSecond);
        Assert.Null(process.IoOtherBytesPerSecond);
        Assert.Null(process.IoSamplingDurationSeconds);
    }
}
