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
        Assert.InRange(observation.Processes.Count, 1, 50);
        Assert.NotNull(observation.GpuEngines);
        Assert.NotNull(observation.GpuMemory);
        Assert.NotNull(observation.Disks);
        Assert.NotNull(observation.Networks);
        Assert.NotNull(observation.ActivityContext);
        Assert.All(observation.GpuEngines, engine =>
        {
            Assert.False(string.IsNullOrWhiteSpace(engine.EngineType));
            Assert.True(double.IsFinite(engine.UtilizationPercent));
            Assert.True(engine.UtilizationPercent >= 0);
        });
        Assert.All(observation.GpuMemory, adapter => Assert.False(string.IsNullOrWhiteSpace(adapter.AdapterInstance)));
        Assert.All(observation.Disks, disk =>
        {
            if (disk.BytesPerSecond is { } rate) Assert.True(rate >= 0);
            if (disk.AverageReadLatencyMilliseconds is { } latency) Assert.True(double.IsFinite(latency) && latency >= 0);
        });
        Assert.All(observation.Networks, network => Assert.False(string.IsNullOrWhiteSpace(network.Adapter)));
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
        Assert.NotNull(snapshot.WindowsInventory);
        Assert.NotEmpty(snapshot.WindowsInventory.Processes);
        Assert.InRange(snapshot.WindowsInventory.Processes.Count, 1, 200);
        Assert.All(snapshot.WindowsInventory.Processes, process =>
        {
            Assert.True(process.Id > 0);
            if (process.WorkingSetBytes is { } memory) Assert.True(memory > 0);
        });
        Assert.NotNull(snapshot.WindowsInventory.SecurityState);
        Assert.NotNull(snapshot.WindowsInventory.Warnings);
        Assert.NotNull(snapshot.WindowsInventory.ProxyConfiguration);
        Assert.Contains(snapshot.WindowsInventory.Warnings, warning => warning.Contains("WinHTTP", StringComparison.OrdinalIgnoreCase));
        Assert.All(snapshot.WindowsInventory.NetworkConfiguration, network => Assert.NotNull(network.Addresses));
        // Defender, physical sensors, OEM inventory, Secure Boot and TPM are optional on CI VMs.
        // The collector must preserve warnings when providers are absent and never invent results.
        Assert.NotNull(snapshot.Warnings);
    }
}
