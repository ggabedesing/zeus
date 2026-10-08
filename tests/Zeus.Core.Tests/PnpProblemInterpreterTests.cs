using Zeus.Core;

namespace Zeus.Core.Tests;

public sealed class PnpProblemInterpreterTests
{
    [Theory]
    [InlineData("10", "não pode ser iniciado")]
    [InlineData("28", "não estão instalados")]
    [InlineData("43", "driver informou")]
    [InlineData("52", "assinatura digital")]
    public void ExplainsSelectedDocumentedCodesWithoutAssertingRootCause(string code, string expected)
    {
        var result = PnpProblemInterpreter.Interpret(code);
        Assert.True(result.IsKnown);
        Assert.Equal(uint.Parse(code), result.Code);
        Assert.Contains(expected, result.Meaning, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(result.Guidance);
    }

    [Theory]
    [InlineData("999")]
    [InlineData("not-a-code")]
    [InlineData(null)]
    public void UnknownOrUnavailableCodesStayExplicit(string? code)
    {
        var result = PnpProblemInterpreter.Interpret(code);
        Assert.False(result.IsKnown);
        Assert.Contains("interpretação verificada", result.Meaning, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(result.Guidance);
    }
}
