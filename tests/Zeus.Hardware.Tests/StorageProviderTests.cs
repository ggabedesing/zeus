using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using Zeus.Windows;

namespace Zeus.Hardware.Tests;

public sealed class StorageProviderTests
{
    [Theory]
    [InlineData(DriveType.Fixed, "Local fixo")]
    [InlineData(DriveType.Removable, "Removível")]
    [InlineData(DriveType.Network, null)]
    [InlineData(DriveType.CDRom, null)]
    [InlineData(DriveType.Ram, null)]
    public void VolumeInventoryIncludesFixedAndRemovableTypesOnly(DriveType type, string? expected) =>
        Assert.Equal(expected, WindowsHardwareDiagnostics.GetVolumeType(type));

    [Theory]
    [InlineData("null")]
    [InlineData("0")]
    [InlineData("\"indisponível\"")]
    public void UnreportedCapacityOmitsDiskInsteadOfPresentingZeroBytes(string size)
    {
        using var json = JsonDocument.Parse("{\"Name\":\"Disco\",\"SizeBytes\":" + size + "}");
        var warnings = new ConcurrentQueue<string>();
        Assert.Null(WindowsHardwareDiagnostics.ParsePhysicalDisk(json.RootElement, warnings));
        Assert.Contains("capacidade não fornecida", Assert.Single(warnings));
    }

    [Fact]
    public void MissingCapacityAlsoOmitsDiskAndExplainsTheLimit()
    {
        using var json = JsonDocument.Parse("{\"Name\":\"Disco\"}");
        var warnings = new ConcurrentQueue<string>();
        Assert.Null(WindowsHardwareDiagnostics.ParsePhysicalDisk(json.RootElement, warnings));
        Assert.Contains("componente omitido", Assert.Single(warnings));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0UL)]
    public void MissingModuleOrFallbackCapacityIsRejectedWithAWarning(ulong? capacity)
    {
        var warnings = new ConcurrentQueue<string>();
        Assert.False(WindowsHardwareDiagnostics.TryReadCapacity(capacity, "DIMM 1", warnings, out _));
        Assert.Contains("DIMM 1", Assert.Single(warnings));
    }

    [Theory]
    [InlineData(0UL, false)]
    [InlineData(100UL, false)]
    [InlineData(101UL, true)]
    [InlineData(255UL, true)]
    public void WearBeyondTheEstimatedLimitIsPreservedAndWarned(ulong wear, bool warningExpected)
    {
        using var json = JsonDocument.Parse("{\"Name\":\"Disco\",\"SizeBytes\":512000000000,\"Wear\":" + wear + "}");
        var warnings = new ConcurrentQueue<string>();
        var disk = WindowsHardwareDiagnostics.ParsePhysicalDisk(json.RootElement, warnings);
        Assert.NotNull(disk);
        Assert.Equal(wear, disk.Wear);
        Assert.Equal(warningExpected, warnings.Count == 1);
        if (warningExpected) Assert.Contains("acima do limite estimado", Assert.Single(warnings));
    }

    [Fact]
    public void MissingSensorsStayUnknownEvenWhenCapacityIsValid()
    {
        using var json = JsonDocument.Parse("{\"Name\":\"Disco\",\"SizeBytes\":512000000000,\"Wear\":null,\"TemperatureCelsius\":null}");
        var warnings = new ConcurrentQueue<string>();
        var disk = WindowsHardwareDiagnostics.ParsePhysicalDisk(json.RootElement, warnings)!;
        Assert.Null(disk.Wear);
        Assert.Null(disk.TemperatureCelsius);
        Assert.Equal(512000000000UL, disk.SizeBytes);
        Assert.Empty(warnings);
    }

    [Fact]
    public void ReliabilityCountersArePreservedWithoutInferringMissingValues()
    {
        using var json = JsonDocument.Parse("{\"Name\":\"Disco\",\"SizeBytes\":512000000000,\"TemperatureMaxCelsius\":70,\"PowerOnHours\":1234,\"ReadErrorsTotal\":7,\"ReadErrorsUncorrected\":2,\"WriteErrorsTotal\":0,\"WriteErrorsUncorrected\":null,\"DiskNumber\":2}");
        var disk = WindowsHardwareDiagnostics.ParsePhysicalDisk(json.RootElement, new ConcurrentQueue<string>())!;

        Assert.Equal(70, disk.TemperatureMaxCelsius);
        Assert.Equal(1234UL, disk.PowerOnHours);
        Assert.Equal(7UL, disk.ReadErrorsTotal);
        Assert.Equal(2UL, disk.ReadErrorsUncorrected);
        Assert.Equal(0UL, disk.WriteErrorsTotal);
        Assert.Null(disk.WriteErrorsUncorrected);
        Assert.Equal(2, disk.DiskNumber);
    }

    [Fact]
    public void VolumeDiskMappingsKeepMultiplePhysicalDiskNumbersAndIgnoreInvalidRows()
    {
        using var json = JsonDocument.Parse("""
            {"Mappings":[{"Volume":"C:","DiskNumber":0},{"Volume":"C:","DiskNumber":2},{"Volume":"c:","DiskNumber":2},{"Volume":"D:","DiskNumber":5},{"Volume":"E:","DiskNumber":-1},{"Volume":" ","DiskNumber":4},{"Volume":"F:"}]}
            """);

        var mappings = WindowsHardwareDiagnostics.ParseVolumeDiskNumbers(json.RootElement);

        Assert.Equal(new[] { 0, 2 }, mappings["C:"]);
        Assert.Equal(new[] { 5 }, mappings["D:"]);
        Assert.DoesNotContain("E:", mappings.Keys);
        Assert.DoesNotContain("F:", mappings.Keys);
    }

    [Fact]
    public void MissingVolumeDiskMappingRemainsUnknown()
    {
        using var json = JsonDocument.Parse("{}");

        Assert.Empty(WindowsHardwareDiagnostics.ParseVolumeDiskNumbers(json.RootElement));
    }
}
