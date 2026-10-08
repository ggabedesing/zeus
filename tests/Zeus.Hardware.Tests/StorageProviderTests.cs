using System.Collections.Concurrent;
using System.Text.Json;
using Zeus.Windows;

namespace Zeus.Hardware.Tests;

public sealed class StorageProviderTests
{
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
        using var json = JsonDocument.Parse("{\"Name\":\"Disco\",\"SizeBytes\":512000000000,\"TemperatureMaxCelsius\":70,\"PowerOnHours\":1234,\"ReadErrorsTotal\":7,\"ReadErrorsUncorrected\":2,\"WriteErrorsTotal\":0,\"WriteErrorsUncorrected\":null}");
        var disk = WindowsHardwareDiagnostics.ParsePhysicalDisk(json.RootElement, new ConcurrentQueue<string>())!;

        Assert.Equal(70, disk.TemperatureMaxCelsius);
        Assert.Equal(1234UL, disk.PowerOnHours);
        Assert.Equal(7UL, disk.ReadErrorsTotal);
        Assert.Equal(2UL, disk.ReadErrorsUncorrected);
        Assert.Equal(0UL, disk.WriteErrorsTotal);
        Assert.Null(disk.WriteErrorsUncorrected);
    }
}
