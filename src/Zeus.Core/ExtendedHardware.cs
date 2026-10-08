namespace Zeus.Core;

/// <summary>Inventory exposed by the platform; this does not assess a board's condition.</summary>
public sealed record BoardInfo(string Manufacturer, string Product);

public sealed record BiosInfo(string Manufacturer, string Version, string? ReleaseDate);

/// <summary>Speed is the reported module speed. Slot inventory does not establish channel configuration.</summary>
public sealed record MemoryModuleInfo(string Location, ulong CapacityBytes, uint? SpeedMHz, string Manufacturer);

/// <summary>
/// Status and sensors are those supplied by the storage provider. Unknown sensors
/// stay null; an absent wear reading is not zero wear and a healthy status is not a guarantee.
/// </summary>
public sealed record PhysicalDiskInfo(
    string Name,
    string MediaType,
    string BusType,
    ulong SizeBytes,
    string HealthStatus,
    double? TemperatureCelsius,
    ulong? Wear);

public sealed record BatteryInfo(string Name, int? ChargePercent, string Status);

/// <summary>Reported interface speed is not a measurement of Internet throughput.</summary>
public sealed record NetworkAdapterInfo(string Name, string Status, ulong? SpeedBitsPerSecond);
