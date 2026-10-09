using System.Runtime.InteropServices;
using Zeus.Core;

namespace Zeus.Windows;

/// <summary>Reports the firmware mode used to start this Windows session.</summary>
internal static class WindowsFirmwareBootReader
{
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFirmwareType(out uint firmwareType);

    internal static FirmwareBootInfo Read()
    {
        if (!OperatingSystem.IsWindows()) return new(WindowsFirmwareBootMode.Unknown, false);
        try
        {
            return GetFirmwareType(out var type) ? FromNative(type) : new(WindowsFirmwareBootMode.Unknown, false);
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return new(WindowsFirmwareBootMode.Unknown, false);
        }
    }

    internal static FirmwareBootInfo FromNative(uint type) => type switch
    {
        1 => new(WindowsFirmwareBootMode.LegacyBios, true),
        2 => new(WindowsFirmwareBootMode.Uefi, true),
        0 => new(WindowsFirmwareBootMode.Unknown, true),
        _ => new(WindowsFirmwareBootMode.Unknown, false)
    };
}
