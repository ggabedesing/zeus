namespace Zeus.Windows;

public enum UsageProfile { Balanced, Work, Gaming, Creative, Battery }
public enum DesktopTheme { Minimal, Complete, MacInspired }

/// <summary>The usage profile records a preference; it never selects a power plan automatically.</summary>
public sealed record UserOptimizationPreferences(UsageProfile Profile, bool ReduceAnimations, bool ReduceTransparency);
public sealed record StartupEntry(string Id, string Name, string Command, bool IsEnabled, bool IsProtected, string? ProtectionReason);
public sealed record UserChangeSession(Guid Id, DateTimeOffset CreatedAt, string Description, bool Restored);
public sealed record UserChangeResult(Guid SessionId, bool Succeeded, string Message);
public sealed record PowerPlanInfo(Guid Id, string Name, bool IsActive);
