namespace Zeus.Core;

public enum WindowsImageHealthState
{
    Unknown,
    NoCorruptionDetected,
    RepairableCorruptionDetected,
    NonRepairableCorruptionDetected
}

/// <summary>Classifies only explicit DISM ScanHealth output; every ambiguous result remains unknown.</summary>
public static class WindowsImageHealthParser
{
    public static WindowsImageHealthState ParseScanHealth(int exitCode, string? output)
    {
        if (exitCode is not 0 and not 3010 || string.IsNullOrWhiteSpace(output))
            return WindowsImageHealthState.Unknown;

        var healthy = output.Contains("No component store corruption detected", StringComparison.OrdinalIgnoreCase);
        var repairable = output.Contains("The component store is repairable", StringComparison.OrdinalIgnoreCase);
        var nonRepairable = output.Contains("The component store is not repairable", StringComparison.OrdinalIgnoreCase) ||
                            output.Contains("The component store cannot be repaired", StringComparison.OrdinalIgnoreCase);
        var matches = (healthy ? 1 : 0) + (repairable ? 1 : 0) + (nonRepairable ? 1 : 0);
        if (matches != 1) return WindowsImageHealthState.Unknown;

        if (healthy) return WindowsImageHealthState.NoCorruptionDetected;
        if (repairable) return WindowsImageHealthState.RepairableCorruptionDetected;
        return WindowsImageHealthState.NonRepairableCorruptionDetected;
    }
}
