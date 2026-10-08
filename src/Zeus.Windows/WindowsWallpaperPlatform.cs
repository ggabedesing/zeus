using System.Runtime.InteropServices;

namespace Zeus.Windows;

/// <summary>Supported Windows desktop wallpaper API, limited to one static image shared by all monitors.</summary>
public sealed class WindowsWallpaperPlatform : IWallpaperPlatform
{
    private static readonly Guid DesktopWallpaperClassId = new("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD");

    public bool IsSlideshowConfigured() => WithDesktopWallpaper(api =>
    {
        var result = api.GetStatus(out var state);
        if (result != 0) throw new COMException("O Windows não confirmou o estado da apresentação de slides.", result);
        return (state & 0x02) != 0; // DSS_SLIDESHOW
    });

    public bool HasUniformWallpaper() => WithDesktopWallpaper(api =>
    {
        var result = api.GetWallpaper(null, out var path);
        return result == 0 && !string.IsNullOrWhiteSpace(path);
    });

    public string GetWallpaperPath() => WithDesktopWallpaper(api =>
    {
        var result = api.GetWallpaper(null, out var path);
        if (result < 0) Marshal.ThrowExceptionForHR(result);
        if (result != 0 || string.IsNullOrWhiteSpace(path))
            throw new InvalidDataException("O Windows não informou uma imagem estática única para todos os monitores.");
        return path;
    });

    public bool SetWallpaperPath(string path) => WithDesktopWallpaper(api => api.SetWallpaper(null, path) == 0);

    private static T WithDesktopWallpaper<T>(Func<IDesktopWallpaper, T> action)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("A personalização do papel de parede requer Windows 8 ou posterior.");
        var type = Type.GetTypeFromCLSID(DesktopWallpaperClassId, throwOnError: true)!;
        var instance = (IDesktopWallpaper)(Activator.CreateInstance(type)
            ?? throw new COMException("Não foi possível criar o controlador de papel de parede do Windows."));
        try { return action(instance); }
        finally { Marshal.FinalReleaseComObject(instance); }
    }

    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        [PreserveSig] int GetMonitorDevicePathAt(uint monitorIndex, [MarshalAs(UnmanagedType.LPWStr)] out string monitorId);
        [PreserveSig] int GetMonitorDevicePathCount(out uint count);
        [PreserveSig] int GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorId, IntPtr rect);
        [PreserveSig] int SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        [PreserveSig] int GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] out string wallpaper);
        [PreserveSig] int GetBackgroundColor(out uint color);
        [PreserveSig] int SetBackgroundColor(uint color);
        [PreserveSig] int GetPosition(out uint position);
        [PreserveSig] int SetPosition(uint position);
        [PreserveSig] int SetSlideshow(IntPtr items);
        [PreserveSig] int GetSlideshow(out IntPtr items);
        [PreserveSig] int SetSlideshowOptions(uint options, uint tick);
        [PreserveSig] int GetSlideshowOptions(out uint options, out uint tick);
        [PreserveSig] int AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string monitorId, uint direction);
        [PreserveSig] int GetStatus(out uint state);
        [PreserveSig] int Enable([MarshalAs(UnmanagedType.Bool)] bool enable);
    }
}
