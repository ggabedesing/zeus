using System.Runtime.InteropServices;

namespace Zeus.Windows;

/// <summary>Supported Windows desktop wallpaper API with per-attached-monitor state capture.</summary>
public sealed class WindowsWallpaperPlatform : IWallpaperPlatform
{
    private static readonly Guid DesktopWallpaperClassId = new("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD");

    public bool IsSlideshowConfigured() => WithDesktopWallpaper(api =>
    {
        var result = api.GetStatus(out var state);
        if (result != 0) throw new COMException("O Windows não confirmou o estado da apresentação de slides.", result);
        return (state & 0x02) != 0; // DSS_SLIDESHOW
    });

    public IReadOnlyList<WallpaperMonitorState> GetAttachedMonitorWallpapers() => WithDesktopWallpaper(api =>
    {
        var countResult = api.GetMonitorDevicePathCount(out var count);
        if (countResult < 0) Marshal.ThrowExceptionForHR(countResult);
        if (countResult != 0 || count is 0 or > 64)
            throw new InvalidDataException("O Windows não confirmou uma lista válida de monitores para o papel de parede.");
        var result = new List<WallpaperMonitorState>((int)count);
        for (uint index = 0; index < count; index++)
        {
            var idResult = api.GetMonitorDevicePathAt(index, out var monitorId);
            if (idResult < 0) Marshal.ThrowExceptionForHR(idResult);
            if (idResult != 0 || string.IsNullOrWhiteSpace(monitorId))
                throw new InvalidDataException("O Windows não confirmou a identidade de um monitor.");
            var rectResult = api.GetMonitorRECT(monitorId, out var bounds);
            if (rectResult == 1) continue; // A API também enumera monitores desconectados.
            if (rectResult < 0) Marshal.ThrowExceptionForHR(rectResult);
            if (rectResult != 0 || bounds.Right <= bounds.Left || bounds.Bottom <= bounds.Top)
                throw new InvalidDataException("O Windows não confirmou quais monitores estão conectados.");
            var wallpaperResult = api.GetWallpaper(monitorId, out var path);
            if (wallpaperResult < 0) Marshal.ThrowExceptionForHR(wallpaperResult);
            if (wallpaperResult != 0 || string.IsNullOrWhiteSpace(path))
                throw new InvalidDataException("Um monitor não tem uma imagem estática que possa ser salva para reversão; nenhuma alteração deve ser feita.");
            result.Add(new WallpaperMonitorState(monitorId, path));
        }
        if (result.Count == 0) throw new InvalidDataException("O Windows não informou monitores conectados para o papel de parede.");
        return result;
    });

    public bool SetWallpaperPath(string monitorId, string path)
    {
        if (string.IsNullOrWhiteSpace(monitorId)) throw new ArgumentException("A identidade do monitor é obrigatória.", nameof(monitorId));
        return WithDesktopWallpaper(api => api.SetWallpaper(monitorId, path) == 0);
    }

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
        [PreserveSig] int SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        [PreserveSig] int GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] out string wallpaper);
        [PreserveSig] int GetMonitorDevicePathAt(uint monitorIndex, [MarshalAs(UnmanagedType.LPWStr)] out string monitorId);
        [PreserveSig] int GetMonitorDevicePathCount(out uint count);
        [PreserveSig] int GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorId, out MonitorRect rect);
        [PreserveSig] int SetBackgroundColor(uint color);
        [PreserveSig] int GetBackgroundColor(out uint color);
        [PreserveSig] int SetPosition(uint position);
        [PreserveSig] int GetPosition(out uint position);
        [PreserveSig] int SetSlideshow(IntPtr items);
        [PreserveSig] int GetSlideshow(out IntPtr items);
        [PreserveSig] int SetSlideshowOptions(uint options, uint tick);
        [PreserveSig] int GetSlideshowOptions(out uint options, out uint tick);
        [PreserveSig] int AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string monitorId, uint direction);
        [PreserveSig] int GetStatus(out uint state);
        [PreserveSig] int Enable([MarshalAs(UnmanagedType.Bool)] bool enable);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
