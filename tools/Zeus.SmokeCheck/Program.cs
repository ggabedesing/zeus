using Zeus.Windows;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("The diagnostic smoke check requires Windows. No maintenance was performed.");
    return 2;
}

try
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(150));
    var snapshot = await new WindowsHardwareDiagnostics().CollectAsync(timeout.Token);
    if (string.IsNullOrWhiteSpace(snapshot.OperatingSystem))
        throw new InvalidOperationException("Operating system information was not collected.");
    if (snapshot.Cpu is null || snapshot.Cpu.LogicalProcessors < 1)
        throw new InvalidOperationException("CPU inventory was not collected.");
    if (snapshot.Memory is null || snapshot.Memory.TotalBytes == 0)
        throw new InvalidOperationException("Physical memory inventory was not collected.");
    if (snapshot.Disks.Count == 0 || snapshot.Disks.All(disk => disk.TotalBytes == 0))
        throw new InvalidOperationException("Storage inventory was not collected.");
    var performance = await new WindowsPerformanceProbe().SampleAsync(TimeSpan.FromSeconds(2), timeout.Token);
    if (performance.CpuPercent is not { } cpu || cpu is < 0 or > 100)
        throw new InvalidOperationException("Native Windows CPU observation was not collected.");
    if (performance.TotalMemoryBytes == 0 || performance.AvailableMemoryBytes > performance.TotalMemoryBytes)
        throw new InvalidOperationException("Native Windows physical memory observation was not collected.");
    if (performance.Processes.Count == 0)
        throw new InvalidOperationException("No real Windows process observation was collected.");
    Console.WriteLine("PASS: real operating system, CPU, memory, volume and native performance data collected.");
    Console.WriteLine($"GPU memory counters: adapters={performance.GpuMemory?.Count ?? 0}; dedicated readings={performance.GpuMemory?.Count(item => item.DedicatedUsageBytes.HasValue) ?? 0}; pressure=not inferred because adapter budget is not collected.");
    Console.WriteLine($"Optional inventory: board={snapshot.Board is not null}; BIOS={snapshot.Bios is not null}; physicalDisks={snapshot.PhysicalDisks?.Count ?? 0}.");
    Console.WriteLine($"Memory inventory: modules={snapshot.MemoryModules?.Count.ToString() ?? "unavailable"}; firmware-declared slots={snapshot.MemoryArraySlotsReported?.ToString() ?? "unavailable"}; channels=not inferred.");
    Console.WriteLine($"Optional warnings: {snapshot.Warnings.Count}. No repair or restore operation executed.");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"FAIL: {exception.Message}");
    return 1;
}
