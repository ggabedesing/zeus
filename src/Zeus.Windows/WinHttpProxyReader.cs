using System.Runtime.InteropServices;
using Zeus.Core;

namespace Zeus.Windows;

/// <summary>Reads the machine's default WinHTTP proxy; this is not a per-session or per-app view.</summary>
internal static class WinHttpProxyReader
{
    private const uint NoProxy = 1;
    private const uint NamedProxy = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct WinHttpProxyInfo
    {
        public uint AccessType;
        public IntPtr Proxy;
        public IntPtr ProxyBypass;
    }

    [DllImport("winhttp.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinHttpGetDefaultProxyConfiguration(out WinHttpProxyInfo proxyInfo);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr GlobalFree(IntPtr memory);

    internal static WinHttpProxyConfigurationInfo Read()
    {
        if (!OperatingSystem.IsWindows()) return new(null, null, null, false);
        WinHttpProxyInfo info;
        try
        {
            if (!WinHttpGetDefaultProxyConfiguration(out info)) return new(null, null, null, false);
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return new(null, null, null, false);
        }
        try
        {
            bool? enabled = info.AccessType switch
            {
                NoProxy => false,
                NamedProxy => true,
                _ => null
            };
            return new(enabled, Sanitize(ReadString(info.Proxy)), Sanitize(ReadString(info.ProxyBypass)), true);
        }
        finally
        {
            if (info.Proxy != IntPtr.Zero) _ = GlobalFree(info.Proxy);
            if (info.ProxyBypass != IntPtr.Zero) _ = GlobalFree(info.ProxyBypass);
        }
    }

    internal static WinHttpProxyConfigurationInfo FromNative(uint accessType, string? proxy, string? bypass)
    {
        bool? enabled = accessType switch { NoProxy => false, NamedProxy => true, _ => null };
        return new(enabled, Sanitize(proxy), Sanitize(bypass), true);
    }

    private static string? ReadString(IntPtr value) => value == IntPtr.Zero ? null : Marshal.PtrToStringUni(value);

    private static string? Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var sanitized = System.Text.RegularExpressions.Regex.Replace(value, @"(?i)[^:/;,\s=]+:[^/@;,\s]+@", "[redigido]@");
        return sanitized.Length > 2048 ? sanitized[..2048] : sanitized;
    }
}
