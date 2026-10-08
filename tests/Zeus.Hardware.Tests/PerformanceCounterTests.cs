using Zeus.Windows;
using CpuTimes = Zeus.Windows.WindowsPerformanceProbe.SystemCpuTimes;

namespace Zeus.Hardware.Tests;

public sealed class PerformanceCounterTests
{
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
