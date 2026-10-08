using Zeus.Windows;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("The diagnostic smoke check requires Windows. No maintenance was performed.");
    return 2;
}

try
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
    var snapshot = await new WindowsHardwareDiagnostics().CollectAsync(timeout.Token);
    if (string.IsNullOrWhiteSpace(snapshot.OperatingSystem))
        throw new InvalidOperationException("Operating system information was not collected.");
    if (snapshot.Cpu is null || snapshot.Cpu.LogicalProcessors < 1)
        throw new InvalidOperationException("CPU inventory was not collected.");
    if (snapshot.Memory is null || snapshot.Memory.TotalBytes == 0)
        throw new InvalidOperationException("Physical memory inventory was not collected.");
    if (snapshot.Disks.Count == 0 || snapshot.Disks.All(disk => disk.TotalBytes == 0))
        throw new InvalidOperationException("Storage inventory was not collected.");
    Console.WriteLine("PASS: real operating system, CPU, memory and volume data collected.");
    Console.WriteLine($"Optional warnings: {snapshot.Warnings.Count}. No repair or restore operation executed.");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"FAIL: {exception.Message}");
    return 1;
}
