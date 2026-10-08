namespace Zeus.Core;

/// <summary>
/// Combines known Windows restart indicators without treating an unreadable source as false.
/// These registry signals are heuristic evidence, not a single authoritative Windows API.
/// </summary>
public static class WindowsRestartStateParser
{
    public static WindowsRestartState Evaluate(WindowsRestartIndicators? indicators)
    {
        if (indicators is null)
            return new(null, 0, 3, []);

        bool? hasPendingFileRenames = indicators.PendingFileRenameCount is { } count && count >= 0
            ? count > 0
            : null;
        var sources = new (string Name, bool? Pending)[]
        {
            ("Component-Based Servicing", indicators.ComponentServicing),
            ("Windows Update", indicators.WindowsUpdate),
            ("renomeações pendentes de arquivo", hasPendingFileRenames)
        };
        var checkedCount = sources.Count(source => source.Pending.HasValue);
        var detected = sources.Where(source => source.Pending == true).Select(source => source.Name).ToArray();
        bool? pending = detected.Length > 0 ? true : checkedCount == sources.Length ? false : null;
        return new(pending, checkedCount, sources.Length, detected);
    }
}
