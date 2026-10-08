using Zeus.Core;

namespace Zeus.Core.Tests;

public sealed class WindowsRestartStateParserTests
{
    [Fact]
    public void ReportsNoIndicatorOnlyWhenEverySourceWasRead()
    {
        var state = WindowsRestartStateParser.Evaluate(new(false, false, 0));

        Assert.False(state.IsPending);
        Assert.Equal(3, state.CheckedSourceCount);
        Assert.Empty(state.Sources);
    }

    [Fact]
    public void APositiveIndicatorWinsEvenWhenAnotherSourceIsUnavailable()
    {
        var state = WindowsRestartStateParser.Evaluate(new(null, true, null));

        Assert.True(state.IsPending);
        Assert.Equal(1, state.CheckedSourceCount);
        Assert.Contains("Windows Update", Assert.Single(state.Sources));
    }

    [Theory]
    [InlineData(false, false, null, 2)]
    [InlineData(null, null, null, 0)]
    public void IncompleteSourcesRemainUnknownWhenNoPositiveIndicatorExists(
        bool? componentServicing, bool? windowsUpdate, int? pendingFileRenameCount, int checkedSources)
    {
        var state = WindowsRestartStateParser.Evaluate(
            new(componentServicing, windowsUpdate, pendingFileRenameCount));

        Assert.Null(state.IsPending);
        Assert.Equal(checkedSources, state.CheckedSourceCount);
        Assert.Equal(3, state.TotalSourceCount);
    }

    [Fact]
    public void EmptyPendingRenameValueDoesNotCountAsAnIndicator()
    {
        var state = WindowsRestartStateParser.Evaluate(new(null, null, 0));

        Assert.Null(state.IsPending);
        Assert.Equal(1, state.CheckedSourceCount);
        Assert.Empty(state.Sources);
    }

    [Fact]
    public void InvalidNegativeRenameCountRemainsUnknown()
    {
        var state = WindowsRestartStateParser.Evaluate(new(false, false, -1));

        Assert.Null(state.IsPending);
        Assert.Equal(2, state.CheckedSourceCount);
    }

    [Fact]
    public void MissingInventoryIsNotInterpretedAsNoPendingRestart()
    {
        var state = WindowsRestartStateParser.Evaluate(null);

        Assert.Null(state.IsPending);
        Assert.Equal(0, state.CheckedSourceCount);
        Assert.Equal(3, state.TotalSourceCount);
    }
}
