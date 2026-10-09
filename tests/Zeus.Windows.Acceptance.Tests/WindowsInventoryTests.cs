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
        Assert.NotNull(snapshot.WindowsInventory);
        Assert.NotNull(snapshot.WindowsVersion);
        if (snapshot.WindowsVersion is { IsAvailable: false })
            Assert.Contains(snapshot.Warnings, warning => warning.StartsWith("Versão/edição do Windows:", StringComparison.Ordinal));
        else
            Assert.Contains(new[] { snapshot.WindowsVersion!.Caption, snapshot.WindowsVersion.Version,
                snapshot.WindowsVersion.BuildNumber, snapshot.WindowsVersion.Architecture }, value => !string.IsNullOrWhiteSpace(value));
        Assert.NotNull(snapshot.WindowsInventory.RestartIndicators);
        var restartState = Zeus.Core.WindowsRestartStateParser.Evaluate(snapshot.WindowsInventory.RestartIndicators);
        Assert.InRange(restartState.CheckedSourceCount, 0, restartState.TotalSourceCount);
        if (restartState.IsPending == true) Assert.NotEmpty(restartState.Sources);
        Assert.True(snapshot.Memory.TotalBytes > 0);
        Assert.InRange(snapshot.Memory.AvailableBytes, 0UL, snapshot.Memory.TotalBytes);
        Assert.NotEmpty(snapshot.Disks);
        Assert.All(snapshot.Disks, disk =>
        {
            Assert.True(disk.TotalBytes > 0);
            Assert.InRange(disk.FreeBytes, 0UL, disk.TotalBytes);
            Assert.False(string.IsNullOrWhiteSpace(disk.DriveLetter));
            Assert.Contains(disk.VolumeType, new[] { "Local fixo", "Removível" });
        });
        Assert.True(snapshot.WindowsInventory is not null, string.Join(" | ", snapshot.Warnings));
        Assert.NotEmpty(snapshot.WindowsInventory.Processes);
        Assert.InRange(snapshot.WindowsInventory.Processes.Count, 1, 200);
        if (snapshot.WindowsInventory.Warnings.Any(warning => warning.StartsWith("Processos: amostra limitada aos 200", StringComparison.OrdinalIgnoreCase)))
            Assert.Equal(200, snapshot.WindowsInventory.Processes.Count);
        Assert.All(snapshot.WindowsInventory.Processes, process =>
        {
            Assert.True(process.Id > 0);
            if (process.WorkingSetBytes is { } memory) Assert.True(memory > 0);
        });
        Assert.NotNull(snapshot.WindowsInventory.SecurityState);
        Assert.NotNull(snapshot.WindowsInventory.Warnings);
        Assert.NotNull(snapshot.WindowsInventory.ProxyConfiguration);
        Assert.NotNull(snapshot.WindowsInventory.WinHttpProxyConfiguration);
        Assert.NotNull(snapshot.WindowsInventory.FirmwareBoot);
        if (snapshot.WindowsInventory.FirmwareBoot is { IsAvailable: false })
            Assert.Contains(snapshot.WindowsInventory.Warnings, warning => warning.StartsWith("Modo de inicialização firmware:", StringComparison.Ordinal));
        if (snapshot.WindowsInventory.WinHttpProxyConfiguration is { IsAvailable: false })
            Assert.Contains(snapshot.WindowsInventory.Warnings, warning => warning.StartsWith("Proxy WinHTTP padrão:", StringComparison.Ordinal));
        Assert.NotNull(snapshot.WindowsInventory.RecentEvents);
        Assert.NotNull(snapshot.WindowsInventory.InstalledSoftware);
        Assert.All(snapshot.WindowsInventory.InstalledSoftware, item =>
            Assert.Contains(item.Source, new[] { "Registro de desinstalação", "Pacote Appx/MSIX do usuário" }));
        Assert.All(snapshot.WindowsInventory.RecentEvents, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Log));
            Assert.False(string.IsNullOrWhiteSpace(item.Provider));
            Assert.InRange(item.Id, 0, int.MaxValue);
            Assert.False(string.IsNullOrWhiteSpace(item.Level));
            Assert.Empty(item.Message);
        });
        Assert.NotEmpty(snapshot.WindowsInventory.Services);
        Assert.All(snapshot.WindowsInventory.Services, service =>
        {
            Assert.False(string.IsNullOrWhiteSpace(service.Name));
            if (service.DependenciesAvailable == true) Assert.NotNull(service.Dependencies);
        });
        Assert.InRange(snapshot.WindowsInventory.ScheduledTasks.Count, 0, 500);
        if (snapshot.WindowsInventory.Warnings.Any(warning => warning.StartsWith("Tarefas agendadas: amostra limitada a 500", StringComparison.OrdinalIgnoreCase)))
            Assert.Equal(500, snapshot.WindowsInventory.ScheduledTasks.Count);
        Assert.Contains(snapshot.WindowsInventory.Warnings, warning => warning.Contains("WinHTTP", StringComparison.OrdinalIgnoreCase));
        Assert.All(snapshot.WindowsInventory.NetworkConfiguration, network => Assert.NotNull(network.Addresses));
        var routeCount = snapshot.WindowsInventory.NetworkConfiguration.Sum(network => network.Routes?.Length ?? 0);
        Assert.InRange(routeCount, 0, 300);
        // Defender, physical sensors, OEM inventory, Secure Boot and TPM are optional on CI VMs.
        // The collector must preserve warnings when providers are absent and never invent results.
        Assert.NotNull(snapshot.Warnings);
    }
}
