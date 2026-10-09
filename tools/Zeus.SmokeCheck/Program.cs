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
    var restartState = Zeus.Core.WindowsRestartStateParser.Evaluate(snapshot.WindowsInventory?.RestartIndicators);
    Console.WriteLine($"Pending restart indicators: {restartState.IsPending?.ToString() ?? "unknown"}; checked={restartState.CheckedSourceCount}/{restartState.TotalSourceCount}; positive sources={(restartState.Sources.Count == 0 ? "none" : string.Join(", ", restartState.Sources))}. This is registry evidence, not a complete Windows restart guarantee.");
    var winHttpProxy = snapshot.WindowsInventory?.WinHttpProxyConfiguration;
    Console.WriteLine($"Default WinHTTP proxy: {(winHttpProxy is not { IsAvailable: true } ? "unavailable" : winHttpProxy.NamedProxyEnabled switch { true => "named proxy configured", false => "direct access configured", null => "access type unknown" })}; server and bypass values omitted.");
    var firmwareBoot = snapshot.WindowsInventory?.FirmwareBoot;
    Console.WriteLine($"Firmware boot mode: {(firmwareBoot is not { IsAvailable: true } ? "unavailable" : firmwareBoot.Mode switch { Zeus.Core.WindowsFirmwareBootMode.Uefi => "UEFI", Zeus.Core.WindowsFirmwareBootMode.LegacyBios => "legacy BIOS", _ => "unknown" })}; this reports the mode used to start Windows.");
    Console.WriteLine($"GPU memory counters: adapters={performance.GpuMemory?.Count ?? 0}; dedicated usage={performance.GpuMemory?.Count(item => item.DedicatedUsageBytes.HasValue) ?? 0}; capacity={performance.GpuMemory?.Count(item => item.DedicatedCapacityBytes.HasValue) ?? 0}; occupancy={performance.GpuMemory?.Count(item => item.DedicatedOccupancyPercent.HasValue) ?? 0}; occupancy is descriptive and not a standalone pressure diagnosis.");
    var processGpuMemory = performance.GpuProcessMemory ?? [];
    var processCapacityShares = processGpuMemory.Count(item =>
        GpuProcessMemoryShare.GetDedicatedCapacityPercent(item, performance.GpuMemory ?? []) is not null);
    Console.WriteLine($"GPU process-memory counters: instances={processGpuMemory.Count}; dedicated usage={processGpuMemory.Count(item => item.DedicatedUsageBytes.HasValue)}; adapter capacity share available={processCapacityShares}; process name mapped={processGpuMemory.Count(item => item.ProcessName is not null)}; per-process budget is unavailable.");
    Console.WriteLine($"RAM paging counters: committed={performance.MemoryPaging?.CommittedBytes?.ToString() ?? "unavailable"}; limit={performance.MemoryPaging?.CommitLimitBytes?.ToString() ?? "unavailable"}; commit percent={performance.MemoryPaging?.CommitPercent?.ToString("0.0") ?? "unavailable"}; page reads/sec={performance.MemoryPaging?.PageReadsPerSecond?.ToString("0.0") ?? "unavailable"}.");
    var memoryPressure = MemoryPressureAnalyzer.Assess([performance]);
    Console.WriteLine($"RAM review signal: {memoryPressure.State}; valid samples={memoryPressure.ValidSamples}; window={memoryPressure.Window.TotalSeconds:0.#} s.");
    Console.WriteLine($"Activity context: {performance.ActivityContext?.Summary ?? "unavailable"}");
    foreach (var warning in performance.Warnings.Where(warning => warning.Contains("Memória GPU", StringComparison.OrdinalIgnoreCase)))
        Console.WriteLine($"GPU memory detail: {warning}");
    var physicalDisks = snapshot.PhysicalDisks ?? [];
    Console.WriteLine($"Optional inventory: board={snapshot.Board is not null}; BIOS={snapshot.Bios is not null}; physicalDisks={physicalDisks.Count}.");
    var software = snapshot.WindowsInventory?.InstalledSoftware ?? [];
    Console.WriteLine($"Installed software inventory: total={software.Count}; uninstall registry={software.Count(item => item.Source == "Registro de desinstalação")}; current-user Appx/MSIX={software.Count(item => item.Source == "Pacote Appx/MSIX do usuário")}; names and publishers omitted.");
    var scheduledTasks = snapshot.WindowsInventory?.ScheduledTasks ?? [];
    var scheduledTasksTruncated = snapshot.WindowsInventory?.Warnings.Any(warning => warning.StartsWith("Tarefas agendadas: amostra limitada a 500", StringComparison.OrdinalIgnoreCase)) == true;
    Console.WriteLine($"Scheduled task inventory: collected={scheduledTasks.Count}; maximum=500; truncated={scheduledTasksTruncated}; names omitted.");
    var inventoriedProcesses = snapshot.WindowsInventory?.Processes ?? [];
    var processesTruncated = snapshot.WindowsInventory?.Warnings.Any(warning => warning.StartsWith("Processos: amostra limitada aos 200", StringComparison.OrdinalIgnoreCase)) == true;
    var inventoriedRoutes = snapshot.WindowsInventory?.NetworkConfiguration.Sum(network => network.Routes?.Length ?? 0) ?? 0;
    var routesTruncated = snapshot.WindowsInventory?.Warnings.Any(warning => warning.StartsWith("Rotas de rede: amostra limitada a 300", StringComparison.OrdinalIgnoreCase)) == true;
    Console.WriteLine($"Process inventory: collected={inventoriedProcesses.Count}; maximum=200; truncated={processesTruncated}; names omitted.");
    Console.WriteLine($"Route inventory: associated={inventoriedRoutes}; maximum=300; truncated={routesTruncated}; addresses omitted.");
    Console.WriteLine($"Storage provider counters: temperature={physicalDisks.Count(disk => disk.TemperatureCelsius.HasValue)}/{physicalDisks.Count}; wear={physicalDisks.Count(disk => disk.Wear.HasValue)}/{physicalDisks.Count}; power-on hours={physicalDisks.Count(disk => disk.PowerOnHours.HasValue)}/{physicalDisks.Count}; read/write error counters={physicalDisks.Count(disk => disk.ReadErrorsTotal.HasValue || disk.WriteErrorsTotal.HasValue)}/{physicalDisks.Count}. Missing values are unavailable, not zero.");
    Console.WriteLine($"Memory inventory: modules={snapshot.MemoryModules?.Count.ToString() ?? "unavailable"}; firmware-declared slots={snapshot.MemoryArraySlotsReported?.ToString() ?? "unavailable"}; channels=not inferred.");
    Console.WriteLine($"Optional warnings: {snapshot.Warnings.Count}. No repair or restore operation executed.");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"FAIL: {exception.Message}");
    return 1;
}
