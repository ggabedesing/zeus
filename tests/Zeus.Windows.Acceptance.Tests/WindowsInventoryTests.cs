using Zeus.Windows;

namespace Zeus.Windows.Acceptance.Tests;

public sealed class WindowsInventoryTests
{
    [Fact]
    public async Task ActualNativePerformanceObservationContainsBoundedCounters()
    {
        Assert.True(OperatingSystem.IsWindows(), "Run native performance acceptance on Windows.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var observation = await new WindowsPerformanceProbe().SampleAsync(TimeSpan.FromSeconds(2), deadline.Token);
        Assert.NotNull(observation.CpuPercent);
        Assert.InRange(observation.CpuPercent.Value, 0, 100);
        Assert.True(observation.TotalMemoryBytes > 0);
        Assert.InRange(observation.AvailableMemoryBytes, 0UL, observation.TotalMemoryBytes);
        Assert.True(observation.SamplingDuration >= TimeSpan.FromSeconds(2));
        Assert.InRange(observation.Processes.Count, 1, 10);
        Assert.All(observation.Processes, process =>
        {
            Assert.True(process.Id >= 0);
            if (process.CpuPercent is { } cpu) Assert.InRange(cpu, 0, 100);
        });
    }

    [Fact]
    public async Task ActualWindowsInventoryContainsCpuMemoryAndFixedVolumes()
    {
        // This suite is deliberately Windows-only: platform absence is a failure,
        // rather than a passed test that never exercised Windows.
        Assert.True(OperatingSystem.IsWindows(), "Run acceptance tests on a Windows machine.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var started = DateTimeOffset.UtcNow;
        var snapshot = await new WindowsHardwareDiagnostics().CollectAsync(deadline.Token);

        Assert.InRange(snapshot.CollectedAt, started, DateTimeOffset.UtcNow);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.OperatingSystem));
        Assert.NotNull(snapshot.Cpu);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.Cpu.Name));
        Assert.True(snapshot.Cpu.PhysicalCores > 0);
        Assert.True(snapshot.Cpu.LogicalProcessors >= snapshot.Cpu.PhysicalCores);
        Assert.NotNull(snapshot.Memory);
        Assert.True(snapshot.Memory.TotalBytes > 0);
        Assert.InRange(snapshot.Memory.AvailableBytes, 0UL, snapshot.Memory.TotalBytes);
        Assert.NotEmpty(snapshot.Disks);
        Assert.All(snapshot.Disks, disk =>
        {
            Assert.True(disk.TotalBytes > 0);
            Assert.InRange(disk.FreeBytes, 0UL, disk.TotalBytes);
            Assert.False(string.IsNullOrWhiteSpace(disk.DriveLetter));
        });
        // Defender, physical sensors and OEM inventory are optional on CI VMs.
        // The collector must preserve a warning collection when providers are absent.
        Assert.NotNull(snapshot.Warnings);
    }
}
