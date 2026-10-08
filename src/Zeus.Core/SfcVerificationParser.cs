namespace Zeus.Core;

public enum SfcVerificationState
{
    Unknown,
    NoIntegrityViolationsDetected,
    IntegrityViolationsDetected,
    UnrepairableIntegrityViolationsDetected
}

/// <summary>Classifies only newly captured, explicit SFC [SR] entries from CBS.log.</summary>
public static class SfcVerificationParser
{
    public static SfcVerificationState Parse(int exitCode, string? appendedCbsLog)
    {
        if (exitCode != 0 || string.IsNullOrWhiteSpace(appendedCbsLog)) return SfcVerificationState.Unknown;
        var entries = appendedCbsLog.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Contains("[SR]", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (entries.Length == 0 || !entries.Any(line => line.Contains("Verify complete", StringComparison.OrdinalIgnoreCase)))
            return SfcVerificationState.Unknown;
        if (entries.Any(line => line.Contains("Cannot repair member file", StringComparison.OrdinalIgnoreCase)))
            return SfcVerificationState.UnrepairableIntegrityViolationsDetected;
        if (entries.Any(line => line.Contains("Repairing corrupted file", StringComparison.OrdinalIgnoreCase) ||
                                line.Contains("Repaired file", StringComparison.OrdinalIgnoreCase)))
            return SfcVerificationState.IntegrityViolationsDetected;
        return SfcVerificationState.NoIntegrityViolationsDetected;
    }
}
