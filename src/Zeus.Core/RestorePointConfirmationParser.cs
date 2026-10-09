using System.Globalization;

namespace Zeus.Core;

/// <summary>Accepts restore-point recovery only when the helper emits one complete confirmation record.</summary>
public static class RestorePointConfirmationParser
{
    private const string Marker = "ZEUS_RESTORE_POINT_CONFIRMED ";

    public static int? ParseSequenceNumber(int exitCode, string? standardOutput, string? logError = null)
    {
        if (exitCode != 0 || logError is not null || string.IsNullOrWhiteSpace(standardOutput)) return null;

        int? sequenceNumber = null;
        foreach (var rawLine in standardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith(Marker, StringComparison.Ordinal)) continue;
            if (sequenceNumber is not null ||
                !int.TryParse(line.AsSpan(Marker.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ||
                parsed <= 0)
                return null;
            sequenceNumber = parsed;
        }

        return sequenceNumber;
    }
}
