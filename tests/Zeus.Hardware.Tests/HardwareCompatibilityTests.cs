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
              "Disks": [], "Startup": [], "Security": null, "Warnings": [],
              "PhysicalDisks": [{"Name":"NVMe","MediaType":"SSD","BusType":"NVMe","SizeBytes":512000000000,"HealthStatus":"Healthy","TemperatureCelsius":null,"Wear":null}]
            }
            """;
        var snapshot = JsonSerializer.Deserialize<HardwareSnapshot>(previous)!;
        Assert.Null(snapshot.Board);
        Assert.Null(snapshot.Bios);
        Assert.Null(snapshot.MemoryModules);
        Assert.Null(snapshot.PhysicalDisks![0].PowerOnHours);
        Assert.Null(snapshot.PhysicalDisks[0].ReadErrorsTotal);
        Assert.Null(snapshot.PhysicalDisks[0].WriteErrorsTotal);
        Assert.Null(snapshot.Batteries);
        Assert.Null(snapshot.NetworkAdapters);
        Assert.Null(snapshot.MemoryArraySlotsReported);
        Assert.Null(snapshot.WindowsInventory);
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
        Assert.Null(restored.PhysicalDisks[0].PowerOnHours);
        Assert.Null(restored.PhysicalDisks[0].ReadErrorsTotal);
        Assert.Null(restored.PhysicalDisks[0].WriteErrorsTotal);
        Assert.Equal(42d, restored.PhysicalDisks[1].TemperatureCelsius);
        Assert.Equal(12UL, restored.PhysicalDisks[1].Wear);
        Assert.Null(restored.MemoryModules![0].SpeedMHz);
        Assert.Null(restored.Batteries![0].ChargePercent);
        Assert.Equal(1_000_000_000UL, restored.NetworkAdapters![0].SpeedBitsPerSecond);
        Assert.Null(restored.Bios!.ReleaseDate);
        Assert.Equal(4, restored.MemoryArraySlotsReported);
    }

    [Fact]
    public void ProxyInventoryPreservesDisabledManualProxyAndUnknownAutoDetect()
    {
        var proxy = new ProxyConfigurationInfo(false, "proxy.local:8080", "https://pac.example/proxy.pac", null, "localhost", true);
        var restored = JsonSerializer.Deserialize<ProxyConfigurationInfo>(JsonSerializer.Serialize(proxy))!;
        Assert.False(restored.ManualProxyEnabled);
        Assert.Equal("proxy.local:8080", restored.ManualProxyServer);
        Assert.Equal("https://pac.example/proxy.pac", restored.AutoConfigUrl);
        Assert.Null(restored.AutoDetectEnabled);
        Assert.True(restored.IsAvailable);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(3, true)]
    public void WinHttpProxyInventoryDistinguishesDirectAndNamedProxy(uint accessType, bool enabled)
    {
        var proxy = Zeus.Windows.WinHttpProxyReader.FromNative(accessType,
            "http://user:secret@proxy.local:8080", "localhost;*.internal");

        Assert.True(proxy.IsAvailable);
        Assert.Equal(enabled, proxy.NamedProxyEnabled);
        Assert.DoesNotContain("secret", proxy.ProxyServer);
        Assert.Contains("[redigido]", proxy.ProxyServer);
        Assert.Equal("localhost;*.internal", proxy.BypassList);

        var credentialsWithoutScheme = Zeus.Windows.WinHttpProxyReader.FromNative(accessType,
            "http=user:secret@proxy.local:8080", null);
        Assert.DoesNotContain("secret", credentialsWithoutScheme.ProxyServer);
    }

    [Fact]
    public void WinHttpProxyInventoryKeepsUnknownAccessTypeUnknown()
    {
        var proxy = Zeus.Windows.WinHttpProxyReader.FromNative(999, null, null);

        Assert.True(proxy.IsAvailable);
        Assert.Null(proxy.NamedProxyEnabled);
        Assert.Null(proxy.ProxyServer);
    }

    [Fact]
    public void WinHttpProxyInventoryJsonPreservesUnavailableAsUnknown()
    {
        var source = new WinHttpProxyConfigurationInfo(null, null, null, false);
        var restored = JsonSerializer.Deserialize<WinHttpProxyConfigurationInfo>(JsonSerializer.Serialize(source))!;

        Assert.False(restored.IsAvailable);
        Assert.Null(restored.NamedProxyEnabled);
        Assert.Null(restored.ProxyServer);
    }

    [Theory]
    [InlineData(1, WindowsFirmwareBootMode.LegacyBios, true)]
    [InlineData(2, WindowsFirmwareBootMode.Uefi, true)]
    [InlineData(0, WindowsFirmwareBootMode.Unknown, true)]
    [InlineData(3, WindowsFirmwareBootMode.Unknown, false)]
    public void FirmwareBootParserMapsOnlyDocumentedModes(uint nativeType, WindowsFirmwareBootMode expected, bool available)
    {
        var result = Zeus.Windows.WindowsFirmwareBootReader.FromNative(nativeType);

        Assert.Equal(expected, result.Mode);
        Assert.Equal(available, result.IsAvailable);
    }
}
