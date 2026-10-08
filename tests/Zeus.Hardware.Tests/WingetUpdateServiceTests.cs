using Zeus.Windows;

namespace Zeus.Hardware.Tests;

public sealed class WingetUpdateServiceTests
{
    [Fact]
    public void ParsesPortugueseWingetTableAndKeepsExactPackageIdentity()
    {
        const string header = "Nome                                       ID                             Versão         Disponível    Origem";
        var output = header + "\r\n" +
                     "--------------------------------------------------------------------------------------------------------------\r\n" +
                     FormatRow("Google Chrome", "Google.Chrome", "151.0.7922.138", "155.0.8059.40", "winget", [0, 43, 74, 89, 103]) + "\r\n" +
                     "13 atualizações disponíveis.\r\n";

        var result = WingetUpdateService.ParseOutput(output);

        Assert.True(result.IsComplete, string.Join(" | ", result.Warnings));
        var update = Assert.Single(result.Updates);
        Assert.Equal("Google.Chrome", update.PackageId);
        Assert.Equal("151.0.7922.138", update.InstalledVersion);
        Assert.Equal("155.0.8059.40", update.AvailableVersion);
        Assert.Equal("winget", update.Source);
    }

    [Fact]
    public void UnknownFormatIsUnavailableAndNeverReportedAsNoUpdates()
    {
        var result = WingetUpdateService.ParseOutput("winget encontrou alguns dados, mas sem cabeçalho reconhecido");

        Assert.False(result.IsComplete);
        Assert.Empty(result.Updates);
        Assert.Contains(result.Warnings, warning => warning.Contains("não reconhecido", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ParsesSourceFilteredWingetTableWithoutSourceColumn()
    {
        var output = "Nome                                       ID                             Versão         Disponível\r\n" +
                     "--------------------------------------------------------------------------------------------------------\r\n" +
                     FormatRowWithoutSource("Google Chrome", "Google.Chrome", "151.0.7922.138", "155.0.8059.40", [0, 43, 74, 89]) + "\r\n";

        var result = WingetUpdateService.ParseOutput(output);

        Assert.True(result.IsComplete, string.Join(" | ", result.Warnings));
        var update = Assert.Single(result.Updates);
        Assert.Equal("Google.Chrome", update.PackageId);
        Assert.Equal("winget", update.Source);
    }

    [Fact]
    public void DuplicatePackageIdentitiesInvalidateTheResult()
    {
        const string header = "Nome         ID             Versão      Disponível  Origem";
        var output = header + "\r\n" +
                     "-----------------------------------------------------\r\n" +
                     FormatRow("Aplicativo", "Vendor.App", "1.0", "2.0", "winget", [0, 13, 28, 39, 51]) + "\r\n" +
                     FormatRow("Outro", "Vendor.App", "1.0", "2.1", "winget", [0, 13, 28, 39, 51]) + "\r\n";

        var result = WingetUpdateService.ParseOutput(output);

        Assert.False(result.IsComplete);
        Assert.Empty(result.Updates);
    }

    private static string FormatRow(string name, string id, string installed, string available, string source, int[] starts) =>
        name.PadRight(starts[1]) + id.PadRight(starts[2] - starts[1]) + installed.PadRight(starts[3] - starts[2]) +
        available.PadRight(starts[4] - starts[3]) + source;

    private static string FormatRowWithoutSource(string name, string id, string installed, string available, int[] starts) =>
        name.PadRight(starts[1]) + id.PadRight(starts[2] - starts[1]) + installed.PadRight(starts[3] - starts[2]) + available;
}
