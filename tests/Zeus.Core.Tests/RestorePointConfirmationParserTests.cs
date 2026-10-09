using Zeus.Core;

namespace Zeus.Core.Tests;

public sealed class RestorePointConfirmationParserTests
{
    [Theory]
    [InlineData("ZEUS_RESTORE_POINT_CONFIRMED 1", 1)]
    [InlineData("\r\nPreparação concluída\r\nZEUS_RESTORE_POINT_CONFIRMED 429496729\r\n", 429496729)]
    public void AcceptsOneCompletePositiveSequenceRecord(string output, int expectedSequence)
    {
        Assert.Equal(expectedSequence, RestorePointConfirmationParser.ParseSequenceNumber(0, output));
    }

    [Theory]
    [InlineData(1, "ZEUS_RESTORE_POINT_CONFIRMED 7", null, null)]
    [InlineData(0, "mensagem ZEUS_RESTORE_POINT_CONFIRMED 7", null, null)]
    [InlineData(0, "ZEUS_RESTORE_POINT_CONFIRMED 0", null, null)]
    [InlineData(0, "ZEUS_RESTORE_POINT_CONFIRMED -7", null, null)]
    [InlineData(0, "ZEUS_RESTORE_POINT_CONFIRMED 7 extra", null, null)]
    [InlineData(0, "ZEUS_RESTORE_POINT_CONFIRMED 7\nZEUS_RESTORE_POINT_CONFIRMED 8", null, null)]
    [InlineData(0, "ZEUS_RESTORE_POINT_CONFIRMED 7", "erro ao gravar log", null)]
    [InlineData(0, null, null, null)]
    public void RejectsFailedAmbiguousOrUnloggedConfirmation(int exitCode, string? output, string? logError, int? expected) =>
        Assert.Equal(expected, RestorePointConfirmationParser.ParseSequenceNumber(exitCode, output, logError));
}
