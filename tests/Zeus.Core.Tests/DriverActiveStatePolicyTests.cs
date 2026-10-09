using Zeus.Core;

namespace Zeus.Core.Tests;

public sealed class DriverActiveStatePolicyTests
{
    [Fact]
    public void SizeBudgetPreservesInitialSnapshotsBeforeSummarizingLatest()
    {
        var devices = Enumerable.Range(0, 24).Select(index => new ActiveDriverDevice("PCI\\" + index + new string('x', 990), new string('i', 1000), new string('v', 1000), new string('p', 1000), true)).ToArray();
        var before = Snapshot(0, devices);
        var after = Snapshot(1, devices);
        var evidence = new DriverActiveEvidence("PCI\\compatible", before, after, Snapshot(2, devices));
        var bounded = DriverActiveStatePolicy.BoundForPersistence(evidence);
        Assert.Same(before, bounded.Before);
        Assert.Same(after, bounded.After);
        Assert.False(bounded.Latest!.IsComplete);
        Assert.Empty(bounded.Latest.Devices);
        Assert.Contains("omitida", bounded.Latest.Warnings[0]);
        Assert.True(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(bounded).Length <= DriverActiveStatePolicy.MaximumPersistedBytes);
        Assert.Contains("incompleta", DriverActiveStatePolicy.Describe(bounded));
    }

    private static readonly DateTimeOffset Time = DateTimeOffset.Parse("2026-10-09T10:00:00Z");
    private static ActiveDriverSnapshot Snapshot(int second, params ActiveDriverDevice[] devices) => new(Time.AddSeconds(second), true, devices, []);
    private static readonly ActiveDriverDevice Device = new("PCI\\fixture", "oem1.inf", "1.0", "Vendor", true);

    [Fact]
    public void ExactInstanceChangeDoesNotProveSelectedPackageActivation()
    {
        var evidence = new DriverActiveEvidence("PCI\\compatible", Snapshot(0, Device), Snapshot(1, Device with { InfName = "oem2.inf", Version = "2.0" }));
        DriverActiveStatePolicy.Validate(evidence);
        var summary = DriverActiveStatePolicy.Describe(evidence);
        Assert.Contains("oem1.inf → oem2.inf", summary);
        Assert.Contains("vínculo observado mudou", summary);
        Assert.Contains("não prova", summary);
        Assert.Contains("pacote WUA exato", summary);
    }

    [Fact]
    public void NewDifferentInstanceIsNotSubstitutedForOldOne()
    {
        var evidence = new DriverActiveEvidence("PCI\\compatible", Snapshot(0, Device), Snapshot(1, Device with { DeviceInstanceId = "PCI\\new" }));
        Assert.Contains("não encontrado", DriverActiveStatePolicy.Describe(evidence));
        Assert.DoesNotContain("vínculo observado mudou", DriverActiveStatePolicy.Describe(evidence));
    }

    [Fact]
    public void MissingAbsentOrIncompleteStatesRemainUnknown()
    {
        Assert.Contains("indisponível", DriverActiveStatePolicy.Describe(null));
        var before = Snapshot(0, Device);
        Assert.Contains("somente captura anterior", DriverActiveStatePolicy.Describe(new("PCI\\compatible", before)));
        Assert.Contains("incompleta", DriverActiveStatePolicy.Describe(new("PCI\\compatible", before with { IsComplete = false }, Snapshot(1, Device))));
        Assert.Contains("presença", DriverActiveStatePolicy.Describe(new("PCI\\compatible", before, Snapshot(1, Device with { IsPresent = false }))));
        Assert.Contains("presença", DriverActiveStatePolicy.Describe(new("PCI\\compatible", before, Snapshot(1, Device with { IsPresent = null }))));
        Assert.Contains("INF/versão indisponível", DriverActiveStatePolicy.Describe(new("PCI\\compatible", before, Snapshot(1, Device with { Version = null }))));
    }

    [Fact]
    public void LatestObservationKeepsBaselineAndEarlierAfterEvidence()
    {
        var evidence = new DriverActiveEvidence("PCI\\compatible", Snapshot(0, Device), Snapshot(1, Device), Snapshot(2, Device with { Version = "3.0" }));
        Assert.Contains("1.0 → 3.0", DriverActiveStatePolicy.Describe(evidence));
        Assert.Equal("1.0", evidence.After!.Devices[0].Version);
    }

    [Fact]
    public void MultipleDevicesAreKeptAndUnchangedIsNotHealthy()
    {
        var second = Device with { DeviceInstanceId = "PCI\\second" };
        var evidence = new DriverActiveEvidence("PCI\\compatible", Snapshot(0, Device, second), Snapshot(1, Device, second));
        Assert.Contains("PCI\\fixture", DriverActiveStatePolicy.Describe(evidence));
        Assert.Contains("PCI\\second", DriverActiveStatePolicy.Describe(evidence));
        Assert.Contains("não mudaram", DriverActiveStatePolicy.Describe(evidence));
        Assert.Contains("vários dispositivos", DriverActiveStatePolicy.Describe(evidence));
    }

    [Fact]
    public void InvalidIdentityDuplicatesAndChronologyAreRejected()
    {
        var evidence = new DriverActiveEvidence("PCI\\compatible", Snapshot(0, Device), Snapshot(1, Device));
        Assert.Throws<ArgumentException>(() => DriverActiveStatePolicy.Validate(evidence with { HardwareId = "" }));
        Assert.Throws<ArgumentException>(() => DriverActiveStatePolicy.Validate(evidence with { Before = Snapshot(0, Device, Device with { DeviceInstanceId = Device.DeviceInstanceId.ToLowerInvariant() }) }));
        Assert.Throws<ArgumentException>(() => DriverActiveStatePolicy.Validate(evidence with { After = Snapshot(-1, Device) }));
        Assert.Throws<ArgumentException>(() => DriverActiveStatePolicy.Validate(evidence with { Latest = Snapshot(0, Device) }));
    }
}
