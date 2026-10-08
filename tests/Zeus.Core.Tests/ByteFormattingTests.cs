using Zeus.Core;

namespace Zeus.Core.Tests;

public sealed class ByteFormattingTests
{
    [Theory]
    [InlineData(0UL, "0 B")]
    [InlineData(1023UL, "1023 B")]
    [InlineData(1024UL, "1 KiB")]
    [InlineData(1536UL, "1,5 KiB")]
    [InlineData(1073741824UL, "1 GiB")]
    public void BinaryUnitsAreLabeledCorrectlyAndFormattedInPortuguese(ulong bytes, string expected)
    {
        Assert.Equal(expected, ByteFormatting.Format(bytes));
    }
}
