using System.Text.Json;
using Zeus.Core;

namespace Zeus.Hardware.Tests;

public sealed class HardwareCompatibilityTests
{
    [Fact]
    public void PreviousInventoryReportsStillDeserializeWithUnknownExtendedData()
    {
        const string previous = """
            {
              "CollectedAt": "2026-01-01T00:00:00Z", "OperatingSystem": "Windows",
              "ComputerName": "PC", "Cpu": null, "Memory": null, "Graphics": [],
              "Disks": [], "Startup": [], "Security": null, "Warnings": []
            }
            """;
        var snapshot = JsonSerializer.Deserialize<HardwareSnapshot>(previous)!;
        Assert.Null(snapshot.Board);
        Assert.Null(snapshot.Bios);
        Assert.Null(snapshot.MemoryModules);
        Assert.Null(snapshot.PhysicalDisks);
        Assert.Null(snapshot.Batteries);
        Assert.Null(snapshot.NetworkAdapters);
        Assert.Null(snapshot.MemoryArraySlotsReported);
    }

    [Fact]
    public void InventoryJsonPreservesUnknownSensorsAndObservedValues()
    {
        var snapshot = new HardwareSnapshot(DateTimeOffset.UtcNow, "Windows", "PC", null, null, [], [], [], null, [],
            new BoardInfo("Fabricante", "Modelo"), new BiosInfo("Fabricante", "1.0", null),
            [new MemoryModuleInfo("DIMM 1", 8UL * 1024 * 1024 * 1024, null, "Fabricante")],
            [new PhysicalDiskInfo("NVMe", "SSD", "NVMe", 512UL * 1024 * 1024 * 1024, "Healthy", null, null),
             new PhysicalDiskInfo("SATA", "HDD", "SATA", 1024UL * 1024 * 1024 * 1024, "Warning", 42, 12)],
            [new BatteryInfo("Bateria", null, "Desconhecido")],
            [new NetworkAdapterInfo("Ethernet", "Conectado", 1_000_000_000)], null, 4);
        var restored = JsonSerializer.Deserialize<HardwareSnapshot>(JsonSerializer.Serialize(snapshot))!;
        Assert.Null(restored.PhysicalDisks![0].TemperatureCelsius);
        Assert.Null(restored.PhysicalDisks[0].Wear);
        Assert.Equal(42d, restored.PhysicalDisks[1].TemperatureCelsius);
        Assert.Equal(12UL, restored.PhysicalDisks[1].Wear);
        Assert.Null(restored.MemoryModules![0].SpeedMHz);
        Assert.Null(restored.Batteries![0].ChargePercent);
        Assert.Equal(1_000_000_000UL, restored.NetworkAdapters![0].SpeedBitsPerSecond);
        Assert.Null(restored.Bios!.ReleaseDate);
        Assert.Equal(4, restored.MemoryArraySlotsReported);
    }
}
