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
