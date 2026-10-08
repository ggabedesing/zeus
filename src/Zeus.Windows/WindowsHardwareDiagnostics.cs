using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Text.Json;
using Zeus.Core;

namespace Zeus.Windows;

/// <summary>Reads Windows observations; missing sensors and providers stay unknown.</summary>
public sealed class WindowsHardwareDiagnostics : IHardwareDiagnostics
{
    public async Task<HardwareSnapshot> CollectAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("O diagnóstico requer Windows.");

        var warnings = new List<string>();
        async Task<T> Read<T>(string label, Func<T> read, T fallback)
        {
            try
            {
                return await Task.Run(read, cancellationToken)
                    .WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error)
            {
                warnings.Add($"{label}: não foi possível obter dados ({error.Message}).");
                return fallback;
            }
        }

        var cpu = await Read<CpuInfo?>("Processador", ReadCpu, null);
        var memory = await Read<MemoryInfo?>("Memória", ReadMemory, null);
        var graphics = await Read<IReadOnlyList<GpuInfo>>("Placas de vídeo", ReadGraphics, []);
        var disks = await Read<IReadOnlyList<DiskInfo>>("Discos", ReadDisks, []);
        var startup = await Read<IReadOnlyList<StartupInfo>>("Inicialização", ReadStartup, []);
        SecurityInfo? security = null;
        try
        {
            security = await ReadSecurityAsync(cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            warnings.Add($"Defender: estado indisponível ({error.Message}). Outro antivírus pode estar ativo.");
        }
        warnings.Add("Temperaturas, desgaste físico e consumo não são inferidos; este diagnóstico não substitui sensores ou inspeção do hardware.");
        return new HardwareSnapshot(DateTimeOffset.UtcNow, Environment.OSVersion.VersionString,
            Environment.MachineName, cpu, memory, graphics, disks, startup, security, warnings);
    }

    private static ManagementObjectCollection Query(string query)
    {
        using var searcher = new ManagementObjectSearcher(new ManagementScope("root\\CIMV2"), new ObjectQuery(query),
            new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(12), ReturnImmediately = false });
        return searcher.Get();
    }

    private static string StringValue(ManagementBaseObject value, string field) =>
        Convert.ToString(value[field], CultureInfo.InvariantCulture)?.Trim() ?? "Desconhecido";

    private static CpuInfo? ReadCpu()
    {
        using var rows = Query("SELECT Name,NumberOfCores,NumberOfLogicalProcessors FROM Win32_Processor");
        var names = new List<string>();
        var cores = 0;
        var logical = 0;
        foreach (ManagementObject row in rows)
        {
            using (row)
            {
                names.Add(StringValue(row, "Name"));
                cores += Convert.ToInt32(row["NumberOfCores"], CultureInfo.InvariantCulture);
                logical += Convert.ToInt32(row["NumberOfLogicalProcessors"], CultureInfo.InvariantCulture);
            }
        }
        return names.Count == 0 ? null : new CpuInfo(string.Join(" / ", names.Distinct()), cores, logical);
    }

    private static MemoryInfo? ReadMemory()
    {
        using var rows = Query("SELECT TotalVisibleMemorySize,FreePhysicalMemory FROM Win32_OperatingSystem");
        foreach (ManagementObject row in rows)
        {
            using (row)
                return new MemoryInfo(Convert.ToUInt64(row["TotalVisibleMemorySize"], CultureInfo.InvariantCulture) * 1024,
                    Convert.ToUInt64(row["FreePhysicalMemory"], CultureInfo.InvariantCulture) * 1024);
        }
        return null;
    }

    private static IReadOnlyList<GpuInfo> ReadGraphics()
    {
        using var rows = Query("SELECT Name,DriverVersion FROM Win32_VideoController");
        var result = new List<GpuInfo>();
        foreach (ManagementObject row in rows)
        {
            using (row) result.Add(new GpuInfo(StringValue(row, "Name"), StringValue(row, "DriverVersion")));
        }
        return result;
    }

    private static IReadOnlyList<DiskInfo> ReadDisks()
    {
        var result = new List<DiskInfo>();
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed))
        {
            if (!drive.IsReady) continue;
            result.Add(new DiskInfo(string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Disco local" : drive.VolumeLabel,
                drive.Name, (ulong)drive.TotalSize, (ulong)drive.AvailableFreeSpace, drive.DriveFormat));
        }
        return result;
    }

    private static IReadOnlyList<StartupInfo> ReadStartup()
    {
        using var rows = Query("SELECT Name,Location,User FROM Win32_StartupCommand");
        var result = new List<StartupInfo>();
        foreach (ManagementObject row in rows)
        {
            using (row)
                result.Add(new StartupInfo(StringValue(row, "Name"), StringValue(row, "Location"), StringValue(row, "User")));
        }
        return result.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private static async Task<SecurityInfo> ReadSecurityAsync(CancellationToken token)
    {
        const string script = "& { $ErrorActionPreference = 'Stop'; $s = Defender\\Get-MpComputerStatus; " +
            "[pscustomobject]@{ DefenderEnabled = [bool]$s.AntivirusEnabled; " +
            "RealTimeProtectionEnabled = [bool]$s.RealTimeProtectionEnabled; " +
            "SignatureUpdatedAt = if ($s.AntivirusSignatureLastUpdated) { $s.AntivirusSignatureLastUpdated.ToUniversalTime().ToString('o') } else { $null }; " +
            "Summary = [string]$s.AMRunningMode } | Microsoft.PowerShell.Utility\\ConvertTo-Json -Compress }";
        var start = TrustedPowerShell.Create(script, WindowsPowerShellModule.Utility, WindowsPowerShellModule.Defender);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("PowerShell não iniciou.");
        var outputTask = process.StandardOutput.ReadToEndAsync(token);
        var errorTask = process.StandardError.ReadToEndAsync(token);
        try
        {
            await process.WaitForExitAsync(token).WaitAsync(TimeSpan.FromSeconds(25), token);
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? $"Código {process.ExitCode}" : error.Trim());
        using var json = JsonDocument.Parse(output.Trim());
        var root = json.RootElement;
        DateTimeOffset? updated = null;
        if (root.TryGetProperty("SignatureUpdatedAt", out var date) && date.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(date.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            updated = parsed;
        return new SecurityInfo(root.GetProperty("DefenderEnabled").GetBoolean(),
            root.GetProperty("RealTimeProtectionEnabled").GetBoolean(), updated,
            root.GetProperty("Summary").GetString() ?? "Estado consultado no Defender");
    }
}
