using System.Text.Json;
using Zeus.Core;
using Zeus.Windows;

namespace Zeus.Hardware.Tests;

public sealed class DriverActiveWarningsTests
{
    [Fact]
    public void ParserDowngradeKeepsWarningBudgetValidForPersistence()
    {
        var snapshot = new ActiveDriverSnapshot(DateTimeOffset.UtcNow, true,
            [new("PCI\\fixture", null, null, null)], Enumerable.Range(0, 16).Select(index => "warning " + index).ToArray());
        var parsed = DriverActiveStateReader.Parse(JsonSerializer.Serialize(snapshot));
        Assert.False(parsed.IsComplete);
        Assert.Equal(16, parsed.Warnings.Count);
        DriverActiveStatePolicy.Validate(new("PCI\\hardware", parsed));
    }
}
