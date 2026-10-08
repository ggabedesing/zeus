using System.ComponentModel;
using System.Management;
using System.Runtime.InteropServices;
using Zeus.Core;

namespace Zeus.Windows;

/// <summary>Windows-only, exact-device rollback using the previous driver retained by Windows.</summary>
public static class WindowsDriverRollback
{
    private const uint DigcfPresent = 0x2;
    private const uint DigcfAllClasses = 0x4;
    private const uint RollbackFlagNoUi = 0x1;

    public sealed record DriverState(string DeviceName, string InfName, string Version, string Provider);
    public sealed record RollbackResult(bool Succeeded, bool RebootRequired, int ErrorCode,
        DriverState? Before, DriverState? After, string Message);

    public static IReadOnlyList<(string InstanceId, string Name, string Class)> GetPresentDevices()
    {
        var set = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, DigcfPresent | DigcfAllClasses);
        if (set == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var devices = new List<(string, string, string)>();
            for (uint index = 0; ; index++)
            {
                var data = new SpDevinfoData { CbSize = Marshal.SizeOf<SpDevinfoData>() };
                if (!SetupDiEnumDeviceInfo(set, index, ref data))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == 259) break;
                    throw new Win32Exception(error);
                }
                var id = GetInstanceId(set, ref data);
                devices.Add((id, ReadDeviceProperty(set, ref data, 0x0000000C), data.ClassGuid.ToString("D")));
            }
            return devices;
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }

    public static RollbackResult Rollback(string instanceId)
    {
        if (!MaintenanceRequestProtocol.TryParsePnpInstanceId(instanceId))
            throw new ArgumentException("Identidade PnP inválida.", nameof(instanceId));
        var set = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, DigcfPresent | DigcfAllClasses);
        if (set == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            SpDevinfoData? match = null;
            for (uint index = 0; ; index++)
            {
                var data = new SpDevinfoData { CbSize = Marshal.SizeOf<SpDevinfoData>() };
                if (!SetupDiEnumDeviceInfo(set, index, ref data))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == 259) break;
                    throw new Win32Exception(error);
                }
                if (string.Equals(GetInstanceId(set, ref data), instanceId, StringComparison.OrdinalIgnoreCase))
                {
                    match = data;
                    break;
                }
            }
            if (match is null) return new(false, false, 1168, null, null, "O dispositivo exato não está presente; nenhuma alteração foi feita.");
            var device = match.Value;
            var before = ReadDriverState(instanceId);
            if (before is null) return new(false, false, 1168, null, null, "O driver instalado não pôde ser identificado sem ambiguidade; nenhuma alteração foi feita.");
            var ok = DiRollbackDriver(set, ref device, IntPtr.Zero, RollbackFlagNoUi, out var reboot);
            var errorCode = ok ? 0 : Marshal.GetLastWin32Error();
            var after = ReadDriverState(instanceId);
            var changed = after is not null && (!string.Equals(before.InfName, after.InfName, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(before.Version, after.Version, StringComparison.OrdinalIgnoreCase));
            var verified = ok && changed;
            return new(verified, reboot, errorCode, before, after,
                verified ? "O Windows informou a reversão e o inventário confirmou mudança de INF ou versão." :
                ok ? "O Windows aceitou a solicitação, mas a mudança de INF ou versão não foi confirmada." :
                errorCode == 259 ? "O Windows não mantém um driver anterior para este dispositivo; nenhuma alteração foi feita." :
                $"O Windows não confirmou a reversão (erro {errorCode}); confira o log e o estado do dispositivo.");
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }

    public static DriverState? ReadDriverState(string instanceId)
    {
        if (!MaintenanceRequestProtocol.TryParsePnpInstanceId(instanceId)) return null;
        using var searcher = new ManagementObjectSearcher(new ManagementScope("root\\CIMV2"),
            new ObjectQuery("SELECT DeviceID,DeviceName,InfName,DriverVersion,DriverProviderName FROM Win32_PnPSignedDriver"),
            new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(10), ReturnImmediately = true, Rewindable = false });
        using var rows = searcher.Get();
        var matches = new List<DriverState>();
        foreach (ManagementObject row in rows)
        {
            using (row)
            {
                if (!string.Equals(Convert.ToString(row["DeviceID"]), instanceId, StringComparison.OrdinalIgnoreCase)) continue;
                matches.Add(new(Convert.ToString(row["DeviceName"]) ?? "Desconhecido", Convert.ToString(row["InfName"]) ?? "",
                    Convert.ToString(row["DriverVersion"]) ?? "", Convert.ToString(row["DriverProviderName"]) ?? "Desconhecido"));
            }
        }
        return matches.Count == 1 && !string.IsNullOrWhiteSpace(matches[0].InfName) ? matches[0] : null;
    }

    private static string GetInstanceId(IntPtr set, ref SpDevinfoData data)
    {
        var buffer = new char[4096];
        if (!SetupDiGetDeviceInstanceIdW(set, ref data, buffer, buffer.Length, out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return new string(buffer).TrimEnd('\0');
    }

    private static string ReadDeviceProperty(IntPtr set, ref SpDevinfoData data, uint property)
    {
        var buffer = new char[1024];
        if (!SetupDiGetDeviceRegistryPropertyW(set, ref data, property, out _, buffer, (uint)(buffer.Length * sizeof(char)), out _)) return "";
        return new string(buffer).TrimEnd('\0');
    }

    [StructLayout(LayoutKind.Sequential)] private struct SpDevinfoData { public int CbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiGetClassDevsW")] private static extern IntPtr SetupDiGetClassDevsW(IntPtr classGuid, string? enumerator, IntPtr hwndParent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref SpDevinfoData data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiGetDeviceInstanceIdW")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiGetDeviceInstanceIdW(IntPtr set, ref SpDevinfoData data, [Out] char[] instanceId, int size, out int required);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiGetDeviceRegistryPropertyW")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiGetDeviceRegistryPropertyW(IntPtr set, ref SpDevinfoData data, uint property, out uint propertyType, [Out] char[] buffer, uint bufferSize, out uint required);
    [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("newdev.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DiRollbackDriver(IntPtr set, ref SpDevinfoData data, IntPtr hwndParent, uint flags, [MarshalAs(UnmanagedType.Bool)] out bool rebootRequired);
}
