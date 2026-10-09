namespace Zeus.Windows;

public enum UsageProfile { Balanced = 0, Work = 1, Gaming = 2, Creative = 3, Battery = 4, GamingStreaming = 5, Development = 6 }
public enum DesktopTheme { Minimal, Complete, MacInspired, Light, GamingNeon, Cyberpunk }
public enum AppAccentColor { ThemeDefault, Blue, Violet, Green, Rose, Amber }

/// <summary>The usage profile records a preference; it never selects a power plan automatically.</summary>
public sealed record UserOptimizationPreferences(UsageProfile Profile, bool ReduceAnimations, bool ReduceTransparency);
public sealed record StartupEntry(string Id, string Name, string Command, bool IsEnabled, bool IsProtected, string? ProtectionReason);
public enum UserChangeStatus { Unknown, Prepared, Applying, Applied, NeedsReview, Restoring, RestoreBlocked, Restored }
public sealed record UserChangeSession(Guid Id, DateTimeOffset CreatedAt, string Description, bool Restored, UserChangeStatus Status = UserChangeStatus.Unknown)
{
    public string StatusText => Status switch
    {
        UserChangeStatus.Prepared => "Backup salvo · alteração ainda não confirmada",
        UserChangeStatus.Applying => "Aplicação interrompida ou não confirmada · revise antes de repetir",
        UserChangeStatus.Applied => "Aplicada · restauração disponível",
        UserChangeStatus.NeedsReview => "Verificação incompleta · revise ou restaure",
        UserChangeStatus.Restoring => "Restauração interrompida ou não confirmada · revise o estado atual",
        UserChangeStatus.RestoreBlocked => "Restauração bloqueada para preservar mudanças externas",
        UserChangeStatus.Restored => "Restaurada e verificada",
        _ => "Estado não registrado · confirme no Windows antes de agir"
    };
}
public sealed record UserChangeResult(Guid SessionId, bool Succeeded, string Message);
public sealed record PowerPlanInfo(Guid Id, string Name, bool IsActive);
public sealed record WallpaperMonitorBounds(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}
public sealed record WallpaperMonitorDiscovery(bool IsSlideshowConfigured, IReadOnlyList<WallpaperMonitorState> Monitors);
public sealed record WallpaperMonitorChoice(string? MonitorId, string Name, string TechnicalDetails)
{
    public override string ToString() => Name;
}

/// <summary>Current-user desktop wallpaper access; kept injectable so rollback can be tested without changing the desktop.</summary>
public sealed record WallpaperMonitorState(string MonitorId, string Path, WallpaperMonitorBounds? Bounds = null);

public interface IWallpaperPlatform
{
    bool IsSlideshowConfigured();
    IReadOnlyList<WallpaperMonitorState> GetAttachedMonitorWallpapers();
    bool SetWallpaperPath(string monitorId, string path);
}
