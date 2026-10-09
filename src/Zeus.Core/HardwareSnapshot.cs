namespace Zeus.Core;

public sealed record CpuInfo(string Name, int PhysicalCores, int LogicalProcessors);

public sealed record MemoryInfo(ulong TotalBytes, ulong AvailableBytes);

public sealed record GpuInfo(string Name, string DriverVersion);

public sealed record DiskInfo(
    string Name,
    string DriveLetter,
    ulong TotalBytes,
    ulong FreeBytes,
    string FileSystem,
    string? VolumeType = null,
    IReadOnlyList<int>? PhysicalDiskNumbers = null);

public sealed record StartupInfo(string Name, string Location, string User);

public sealed record SecurityInfo(
    bool? DefenderEnabled,
    bool? RealTimeProtectionEnabled,
    DateTimeOffset? SignatureUpdatedAt,
    string Summary);

/// <summary>
/// An observed inventory, not a hardware health rating. Missing readings stay null
/// or absent; collectors explain their limits in Warnings.
/// </summary>
public sealed record HardwareSnapshot(
    DateTimeOffset CollectedAt,
    string OperatingSystem,
    string ComputerName,
    CpuInfo? Cpu,
    MemoryInfo? Memory,
    IReadOnlyList<GpuInfo> Graphics,
    IReadOnlyList<DiskInfo> Disks,
    IReadOnlyList<StartupInfo> Startup,
    SecurityInfo? Security,
    IReadOnlyList<string> Warnings,
    BoardInfo? Board = null,
    BiosInfo? Bios = null,
    IReadOnlyList<MemoryModuleInfo>? MemoryModules = null,
    IReadOnlyList<PhysicalDiskInfo>? PhysicalDisks = null,
    IReadOnlyList<BatteryInfo>? Batteries = null,
    IReadOnlyList<NetworkAdapterInfo>? NetworkAdapters = null,
    WindowsInventoryInfo? WindowsInventory = null,
    int? MemoryArraySlotsReported = null,
    WindowsVersionInfo? WindowsVersion = null);

public sealed record WindowsVersionInfo(string? Caption, string? Version, string? BuildNumber,
    string? Architecture, bool IsAvailable);

public interface IHardwareDiagnostics
{
    Task<HardwareSnapshot> CollectAsync(CancellationToken cancellationToken = default);
}
