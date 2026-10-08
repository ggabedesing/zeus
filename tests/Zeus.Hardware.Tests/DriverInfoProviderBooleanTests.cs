using System.Text.Json;
using Zeus.Core;

namespace Zeus.Hardware.Tests;

public sealed class DriverInfoProviderBooleanTests
{
    [Fact]
    public void MissingProviderBooleanRemainsUnknown()
    {
        const string json = "{\"Device\":\"fixture\",\"Provider\":\"fixture\",\"Version\":\"1\",\"Date\":null,\"Signer\":null}";
        var driver = JsonSerializer.Deserialize<DriverInfo>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.Null(Assert.IsType<DriverInfo>(driver).IsSigned);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("null", null)]
    [InlineData("\"not-a-boolean\"", null)]
    [InlineData("2", null)]
    [InlineData("{}", null)]
    public void InvalidOrMissingProviderBooleanRemainsUnknown(string value, bool? expected)
    {
        var json = $"{{\"Device\":\"fixture\",\"Provider\":\"fixture\",\"Version\":\"1\",\"Date\":null,\"Signer\":null,\"IsSigned\":{value}}}";
        var driver = JsonSerializer.Deserialize<DriverInfo>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.Equal(expected, Assert.IsType<DriverInfo>(driver).IsSigned);
    }
}
