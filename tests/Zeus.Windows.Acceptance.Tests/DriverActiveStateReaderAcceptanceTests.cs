using System.Management;
using Zeus.Windows;

namespace Zeus.Windows.Acceptance.Tests;

public sealed class DriverActiveStateReaderAcceptanceTests
{
    [Fact]
    public async Task RealReadOnlyCaptureMatchesInstalledDeviceAndFrozenIdentity()
    {
        var options = new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(8), ReturnImmediately = false };
        var installed = new Dictionary<string, (string Inf, string Version)>(StringComparer.OrdinalIgnoreCase);
        using (var searcher = new ManagementObjectSearcher(new ManagementScope("root\\cimv2"), new ObjectQuery("SELECT DeviceID,InfName,DriverVersion FROM Win32_PnPSignedDriver"), options))
        using (var results = searcher.Get())
        {
            var count = 0;
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    if (++count > 5000) break;
                    if (item["DeviceID"] is string id && item["InfName"] is string inf && item["DriverVersion"] is string version)
                        installed.TryAdd(id, (inf, version));
                }
            }
        }
        string? hardwareId = null, instanceId = null;
        (string Inf, string Version) expected = default;
        using (var searcher = new ManagementObjectSearcher(new ManagementScope("root\\cimv2"), new ObjectQuery("SELECT DeviceID,HardwareID,Present FROM Win32_PnPEntity"), options))
        using (var results = searcher.Get())
        {
            var count = 0;
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    if (++count > 5000) break;
                    if (item["DeviceID"] is not string id || item["Present"] is not true ||
                        item["HardwareID"] is not string[] ids || ids.Length == 0 || !installed.TryGetValue(id, out expected)) continue;
                    hardwareId = ids[0]; instanceId = id; break;
                }
            }
        }
        Assert.True(hardwareId is not null && instanceId is not null, "A maquina precisa disponibilizar uma associacao PnP/driver instalada para esta verificacao.");
        var snapshot = await DriverActiveStateReader.CaptureAsync(hardwareId!);
        Assert.True(snapshot.Devices.Any(d => string.Equals(d.DeviceInstanceId, instanceId, StringComparison.OrdinalIgnoreCase) &&
            d.IsPresent == true && d.InfName == expected.Inf && d.Version == expected.Version), "A captura deve conter a associacao exata observada independentemente via WMI.");
        var frozen = await DriverActiveStateReader.CaptureAsync("ZEUS\\UNMAPPED-HARDWARE-ID", deviceInstanceIds: [instanceId!]);
        Assert.True(frozen.Devices.Count == 1 && string.Equals(frozen.Devices[0].DeviceInstanceId, instanceId, StringComparison.OrdinalIgnoreCase) &&
            frozen.Devices[0].IsPresent == true && frozen.Devices[0].InfName == expected.Inf && frozen.Devices[0].Version == expected.Version,
            "A leitura posterior deve preservar a instancia original, sem remapear pelo hardware ID.");
    }

    [Fact]
    public async Task UnmappedHardwareAndAbsentFrozenDeviceStayExplicit()
    {
        var unmapped = await DriverActiveStateReader.CaptureAsync("ZEUS\\UNMAPPED-HARDWARE-ID");
        Assert.False(unmapped.IsComplete);
        Assert.Empty(unmapped.Devices);
        Assert.NotEmpty(unmapped.Warnings);
        var missing = await DriverActiveStateReader.CaptureAsync("ZEUS\\UNMAPPED-HARDWARE-ID", deviceInstanceIds: ["ZEUS\\ABSENT-DEVICE-INSTANCE"]);
        Assert.False(missing.IsComplete);
        var device = Assert.Single(missing.Devices);
        Assert.False(device.IsPresent);
        Assert.Null(device.InfName);
    }

    [Fact]
    public async Task CancellationDuringIsolatedCapturePropagatesWithinCleanupBound()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var timer = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DriverActiveStateReader.CaptureAsync("ZEUS\\UNMAPPED-HARDWARE-ID", cancellation.Token));
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(8), "Cancelar deve encerrar a consulta isolada dentro do limite de limpeza.");
    }
}
