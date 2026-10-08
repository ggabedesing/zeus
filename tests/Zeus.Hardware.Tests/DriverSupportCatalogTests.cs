using Zeus.Core;

namespace Zeus.Hardware.Tests;

public sealed class DriverSupportCatalogTests
{
    [Theory]
    [InlineData("NVIDIA Corporation", "www.nvidia.com")]
    [InlineData("Advanced Micro Devices, Inc.", "www.amd.com")]
    [InlineData("Intel(R) Corporation", "www.intel.com")]
    [InlineData("Dell Inc.", "www.dell.com")]
    [InlineData("Hewlett-Packard", "support.hp.com")]
    [InlineData("LENOVO", "pcsupport.lenovo.com")]
    [InlineData("ASUSTeK COMPUTER INC.", "www.asus.com")]
    [InlineData("Acer Incorporated", "www.acer.com")]
    [InlineData("Micro-Star International", "us.msi.com")]
    public void RecognizedVendorsHaveFixedOfficialHttpsLookup(string provider, string host)
    {
        var source = Assert.IsType<DriverSupportSource>(DriverSupportCatalog.Find(provider));
        Assert.Equal("https", source.Uri.Scheme);
        Assert.Equal(host, source.Uri.Host);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Unknown Device Company")]
    public void UnknownProviderHasNoSuggestedDestination(string? provider) =>
        Assert.Null(DriverSupportCatalog.Find(provider));

    [Fact]
    public void DeviceManufacturerTakesPriorityOverDriverProvider()
    {
        var source = Assert.IsType<DriverSupportSource>(DriverSupportCatalog.FindForDevice("Dell Inc.", "Intel Corporation"));
        Assert.Equal("Dell", source.Name);
    }

    [Fact]
    public void UnknownDeviceManufacturerFallsBackToDriverProvider()
    {
        var source = Assert.IsType<DriverSupportSource>(DriverSupportCatalog.FindForDevice("Unknown OEM", "Intel Corporation"));
        Assert.Equal("Intel", source.Name);
    }
}
