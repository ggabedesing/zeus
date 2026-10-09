using Zeus.Core;

namespace Zeus.Core.Tests;

public sealed class AccentColorAccessibilityTests
{
    [Theory]
    [InlineData("#00ffff", "#00FFFF")]
    [InlineData("#1D4Ed8", "#1D4ED8")]
    public void ValidSixDigitHexColorIsNormalized(string value, string expected)
    {
        Assert.True(AccentColorAccessibility.TryNormalize(value, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("00FFFF")]
    [InlineData("#0FF")]
    [InlineData("#00FFFF00")]
    [InlineData("#GGFFFF")]
    public void InvalidColorSyntaxIsRejected(string? value)
    {
        Assert.False(AccentColorAccessibility.TryNormalize(value, out var normalized));
        Assert.Empty(normalized);
    }

    [Theory]
    [InlineData("#1D4ED8", "#FFFFFF", true)]
    [InlineData("#00FFFF", "#071623", true)]
    [InlineData("#777777", "#FFFFFF", false)]
    [InlineData("#101010", "#071623", false)]
    public void ContrastPolicyChecksForegroundAgainstAccentSurface(string foreground, string background, bool expected) =>
        Assert.Equal(expected, AccentColorAccessibility.MeetsContrast(foreground, background));

    [Fact]
    public void ContrastRatioIsSymmetricAndAtLeastOne()
    {
        var forward = AccentColorAccessibility.ContrastRatio("#FFFFFF", "#1D4ED8");
        var reverse = AccentColorAccessibility.ContrastRatio("#1D4ED8", "#FFFFFF");

        Assert.Equal(forward, reverse, 10);
        Assert.True(forward >= 1);
    }
}
