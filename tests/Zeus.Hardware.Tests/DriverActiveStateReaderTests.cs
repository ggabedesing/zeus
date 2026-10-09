using System.Text.Json;
using Zeus.Core;
using Zeus.Windows;

namespace Zeus.Hardware.Tests;

public sealed class DriverActiveStateReaderTests
{
    [Fact]
    public void ExactAssociationRoundTrips()
    {
        var snapshot = new ActiveDriverSnapshot(DateTimeOffset.UtcNow, true,
            [new ActiveDriverDevice("PCI\\TEST\\INSTANCE", "oem42.inf", "1.2.3.4", "Vendor", true)], []);
        var actual = DriverActiveStateReader.Parse(JsonSerializer.Serialize(snapshot));
        Assert.True(actual.IsComplete);
        Assert.Equal("oem42.inf", Assert.Single(actual.Devices).InfName);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{broken")]
    [InlineData("{\"CheckedAt\":\"2026-10-09T00:00:00Z\",\"IsComplete\":\"yes\",\"Devices\":[],\"Warnings\":[]}")]
    [InlineData("{\"CheckedAt\":\"2026-10-09T00:00:00Z\",\"IsComplete\":true,\"Devices\":null,\"Warnings\":[]}")]
    public void MalformedPayloadIsUnknown(string payload)
    {
        var actual = DriverActiveStateReader.Parse(payload);
        Assert.False(actual.IsComplete);
        Assert.Empty(actual.Devices);
        Assert.NotEmpty(actual.Warnings);
    }

    [Fact]
    public void MissingMappingCannotBeComplete()
    {
        var actual = DriverActiveStateReader.Parse(JsonSerializer.Serialize(new ActiveDriverSnapshot(DateTimeOffset.UtcNow, true, [], [])));
        Assert.False(actual.IsComplete);
        Assert.NotEmpty(actual.Warnings);
    }

    [Theory]
    [InlineData(null, "1.0")]
    [InlineData("oem1.inf", null)]
    public void MissingMetadataDowngradesComplete(string? inf, string? version)
    {
        var actual = DriverActiveStateReader.Parse(JsonSerializer.Serialize(new ActiveDriverSnapshot(DateTimeOffset.UtcNow, true,
            [new ActiveDriverDevice("TEST\\1", inf, version, null)], [])));
        Assert.False(actual.IsComplete);
        Assert.Single(actual.Devices);
    }

    [Fact]
    public void PartialSnapshotPreservesKnownMapping()
    {
        var actual = DriverActiveStateReader.Parse(JsonSerializer.Serialize(new ActiveDriverSnapshot(DateTimeOffset.UtcNow, false,
            [new ActiveDriverDevice("TEST\\1", "oem1.inf", "1.0", null)], ["Consulta parcial."])));
        Assert.False(actual.IsComplete);
        Assert.Single(actual.Devices);
        Assert.Equal("Consulta parcial.", Assert.Single(actual.Warnings));
    }

    [Fact]
    public void DuplicateDeviceIdsAreRejectedCaseInsensitively()
    {
        var actual = DriverActiveStateReader.Parse(JsonSerializer.Serialize(new ActiveDriverSnapshot(DateTimeOffset.UtcNow, true,
            [new ActiveDriverDevice("TEST\\1", "a.inf", "1.0", null), new ActiveDriverDevice("test\\1", "b.inf", "2.0", null)], [])));
        Assert.False(actual.IsComplete);
        Assert.Empty(actual.Devices);
    }

    [Fact]
    public void OversizedAndControlCharacterMetadataAreRejected()
    {
        foreach (var id in new[] { new string('x', 1025), "TEST\n1" })
        {
            var actual = DriverActiveStateReader.Parse(JsonSerializer.Serialize(new ActiveDriverSnapshot(DateTimeOffset.UtcNow, true,
                [new ActiveDriverDevice(id, "a.inf", "1.0", null)], [])));
            Assert.False(actual.IsComplete);
            Assert.Empty(actual.Devices);
        }
    }

    [Fact]
    public void DeviceAndPayloadBoundsAreEnforced()
    {
        var devices = Enumerable.Range(0, 65).Select(i => new ActiveDriverDevice($"TEST\\{i}", "a.inf", "1.0", null)).ToArray();
        Assert.Empty(DriverActiveStateReader.Parse(JsonSerializer.Serialize(new ActiveDriverSnapshot(DateTimeOffset.UtcNow, true, devices, []))).Devices);
        Assert.False(DriverActiveStateReader.Parse(new string('x', 128 * 1024 + 1)).IsComplete);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("TEST\n1")]
    public async Task InvalidRequestedIdsFailWithoutLaunching(string id)
    {
        var actual = await DriverActiveStateReader.CaptureAsync(id);
        Assert.False(actual.IsComplete);
        Assert.Empty(actual.Devices);
    }

    [Fact]
    public async Task UserCancellationPropagatesInsteadOfReturningCompletedUnknown()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DriverActiveStateReader.CaptureAsync("TEST\\1", cancellation.Token));
    }

    [Fact]
    public async Task InvalidFrozenIdentitiesNeverLaunch()
    {
        foreach (var ids in new IReadOnlyList<string>[] { [], ["TEST\\1", "test\\1"], ["TEST\n1"], Enumerable.Range(0, 65).Select(i => $"TEST\\{i}").ToArray() })
            Assert.False((await DriverActiveStateReader.CaptureAsync("TEST\\1", deviceInstanceIds: ids)).IsComplete);
    }
}
