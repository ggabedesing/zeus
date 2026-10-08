namespace Zeus.Core;

/// <summary>Inventory exposed by the platform; this does not assess a board's condition.</summary>
public sealed record BoardInfo(string Manufacturer, string Product);

public sealed record BiosInfo(string Manufacturer, string Version, string? ReleaseDate);

/// <summary>Speed is the reported module speed. Slot inventory does not establish channel configuration.</summary>
public sealed record MemoryModuleInfo(string Location, ulong CapacityBytes, uint? SpeedMHz, string Manufacturer);

/// <summary>
/// Status, sensors, and reliability counters are those supplied by the storage provider.
/// Unknown values stay null; an absent wear reading is not zero wear and a healthy status
/// is not a guarantee.
/// </summary>
public sealed record PhysicalDiskInfo(
    string Name,
    string MediaType,
    string BusType,
    ulong SizeBytes,
    string HealthStatus,
    double? TemperatureCelsius,
    ulong? Wear,
    double? TemperatureMaxCelsius = null,
    ulong? PowerOnHours = null,
    ulong? ReadErrorsTotal = null,
    ulong? ReadErrorsUncorrected = null,
    ulong? WriteErrorsTotal = null,
    ulong? WriteErrorsUncorrected = null);

public sealed record BatteryInfo(string Name, int? ChargePercent, string Status);

/// <summary>Reported interface speed is not a measurement of Internet throughput.</summary>
public sealed record NetworkAdapterInfo(string Name, string Status, ulong? SpeedBitsPerSecond);

/// <summary>Extended Windows inventory. Optional collections remain empty when providers are unavailable.</summary>
public sealed record WindowsInventoryInfo(
    IReadOnlyList<NetworkConfigurationInfo> NetworkConfiguration,
    IReadOnlyList<DriverInfo> Drivers,
    IReadOnlyList<PnpDeviceInfo> PnpDevices,
    IReadOnlyList<ProcessInfo> Processes,
    IReadOnlyList<ServiceInfo> Services,
    IReadOnlyList<ScheduledTaskInfo> ScheduledTasks,
    IReadOnlyList<InstalledSoftwareInfo> InstalledSoftware,
    IReadOnlyList<WindowsEventInfo> RecentEvents,
    WindowsSecurityState? SecurityState,
    WindowsUpdateState? UpdateState,
    string? WindowsImageHealth,
    IReadOnlyList<string> Warnings,
    ProxyConfigurationInfo? ProxyConfiguration = null);

public sealed record NetworkConfigurationInfo(string Adapter, string[] Addresses, string[] DnsServers, string[] Gateways, string Status, string[]? Routes = null, string? Proxy = null);
public sealed record ProxyConfigurationInfo(bool? ManualProxyEnabled, string? ManualProxyServer,
    string? AutoConfigUrl, bool? AutoDetectEnabled, string? BypassList, bool IsAvailable);
public sealed record DriverInfo(string Device, string Provider, string Version, string? Date, string? Signer, bool? IsSigned = null);
public sealed record PnpDeviceInfo(string Name, string Class, string Status, string? ProblemCode,
    string? InstanceId = null, bool? IsPresent = null);
public sealed record ProcessInfo(string Name, int Id, double? CpuSeconds, ulong? WorkingSetBytes);
public sealed record ServiceInfo(string Name, string DisplayName, string Status, string StartType,
    string[]? Dependencies = null, bool? DependenciesAvailable = null);
public sealed record ScheduledTaskInfo(string Name, string Path, string State);
public sealed record InstalledSoftwareInfo(string Name, string Version, string Publisher);
public sealed record WindowsEventInfo(DateTimeOffset Time, string Log, string Provider, int Id, string Level, string Message);
public sealed record WindowsSecurityState(bool? SecureBootEnabled, bool? TpmPresent, bool? TpmReady);
public sealed record WindowsUpdateState(int? PendingCount, string Source);
