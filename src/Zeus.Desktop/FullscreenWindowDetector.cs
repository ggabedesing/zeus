using System.Runtime.InteropServices;
using System.Windows;

namespace Zeus.Desktop;

internal static class FullscreenWindowDetector
{
    private const uint MonitorDefaultToNearest = 2;

    public static bool IsForegroundFullscreenOnClockMonitor(IntPtr clockWindow)
    {
        if (!OperatingSystem.IsWindows()) return false;
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero) return false;
        _ = GetWindowThreadProcessId(window, out var processId);
        if (processId == Environment.ProcessId) return false;
        if (!GetWindowRect(window, out var bounds)) return false;
        var monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
        var clockMonitor = MonitorFromWindow(clockWindow, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero || clockMonitor == IntPtr.Zero || monitor != clockMonitor) return false;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return false;
        return CoversMonitorOnClockDisplay(true, ToRect(bounds), ToRect(info.MonitorBounds));
    }

    internal static bool CoversMonitor(Rect window, Rect monitor, double tolerance = 2) =>
        !window.IsEmpty && !monitor.IsEmpty && monitor.Width > 0 && monitor.Height > 0 &&
        window.Left <= monitor.Left + tolerance && window.Top <= monitor.Top + tolerance &&
        window.Right >= monitor.Right - tolerance && window.Bottom >= monitor.Bottom - tolerance;

    internal static bool CoversMonitorOnClockDisplay(bool sameMonitor, Rect foregroundWindow, Rect foregroundMonitor) =>
        sameMonitor && CoversMonitor(foregroundWindow, foregroundMonitor);

    private static Rect ToRect(NativeRect rect) => new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect MonitorBounds;
        public NativeRect WorkBounds;
        public uint Flags;
    }
}
