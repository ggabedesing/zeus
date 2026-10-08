using Zeus.Core;

namespace Zeus.Core.Tests;

public sealed class WindowsImageHealthParserTests
{
    [Theory]
    [InlineData("No component store corruption detected.", WindowsImageHealthState.NoCorruptionDetected)]
    [InlineData("The component store is repairable.", WindowsImageHealthState.RepairableCorruptionDetected)]
    [InlineData("The component store cannot be repaired.", WindowsImageHealthState.NonRepairableCorruptionDetected)]
    public void RecognizesExplicitDismScanHealthResults(string output, WindowsImageHealthState expected)
    {
        Assert.Equal(expected, WindowsImageHealthParser.ParseScanHealth(0, output));
    }

    [Theory]
    [InlineData(0, "The operation completed successfully.")]
    [InlineData(87, "No component store corruption detected.")]
    [InlineData(0, "No component store corruption detected. The component store is repairable.")]
    [InlineData(0, null)]
    public void UnknownOrConflictingResultsNeverBecomeHealthy(int exitCode, string? output)
    {
        Assert.Equal(WindowsImageHealthState.Unknown,
            WindowsImageHealthParser.ParseScanHealth(exitCode, output));
    }
}

public sealed class SfcVerificationParserTests
{
    private const string CleanCbsEntries = "2026-10-08 12:00:00, Info CSI [SR] Beginning Verify and Repair transaction\n2026-10-08 12:00:01, Info CSI [SR] Verify complete";

    [Fact]
    public void RecognizesNewCbsEntriesWithNoIntegrityViolations()
    {
        Assert.Equal(SfcVerificationState.NoIntegrityViolationsDetected, SfcVerificationParser.Parse(0, CleanCbsEntries));
    }

    [Fact]
    public void RecognizesFilesThatDifferWithoutClaimingVerifyOnlyRepairedThem()
    {
        var log = CleanCbsEntries + "\n2026-10-08 12:00:02, Info CSI [SR] Repairing corrupted file sample.dll from store";
        Assert.Equal(SfcVerificationState.IntegrityViolationsDetected, SfcVerificationParser.Parse(0, log));
    }

    [Fact]
    public void RecognizesUnrepairableFileAsItsOwnState()
    {
        var log = CleanCbsEntries + "\n2026-10-08 12:00:02, Info CSI [SR] Cannot repair member file sample.dll";
        Assert.Equal(SfcVerificationState.UnrepairableIntegrityViolationsDetected, SfcVerificationParser.Parse(0, log));
    }

    [Theory]
    [InlineData(0, "unrecognized localized CBS entries")]
    [InlineData(1, "[SR] Verify complete")]
    [InlineData(0, "[SR] Beginning Verify and Repair transaction")]
    [InlineData(0, null)]
    public void IncompleteOrUnrecognizedEvidenceRemainsUnknown(int exitCode, string? log)
    {
        Assert.Equal(SfcVerificationState.Unknown, SfcVerificationParser.Parse(exitCode, log));
    }
}
