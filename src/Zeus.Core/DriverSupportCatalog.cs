namespace Zeus.Core;

/// <summary>Fixed official driver lookup destinations. A link is not evidence of an available update.</summary>
public sealed record DriverSupportSource(string Name, Uri Uri);

public static class DriverSupportCatalog
{
    public static DriverSupportSource? FindForDevice(string? manufacturer, string? driverProvider) =>
        Find(manufacturer) ?? Find(driverProvider);

    public static DriverSupportSource? Find(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider)) return null;
        var value = provider.Trim();
        if (value.Contains("Dell", StringComparison.OrdinalIgnoreCase))
            return new("Dell", new Uri("https://www.dell.com/support/home/en-us?app=drivers"));
        if (value.Contains("Hewlett-Packard", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("HP", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("HP ", StringComparison.OrdinalIgnoreCase))
            return new("HP", new Uri("https://support.hp.com/us-en/drivers"));
        if (value.Contains("Lenovo", StringComparison.OrdinalIgnoreCase))
            return new("Lenovo", new Uri("https://pcsupport.lenovo.com/us/en/selectproduct?linkto=downloads"));
        if (value.Contains("ASUSTeK", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("ASUS", StringComparison.OrdinalIgnoreCase))
            return new("ASUS", new Uri("https://www.asus.com/us/support/download-center/"));
        if (value.Contains("Acer", StringComparison.OrdinalIgnoreCase))
            return new("Acer", new Uri("https://www.acer.com/us-en/support/drivers-and-manuals"));
        if (value.Contains("Micro-Star", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("MSI", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("MSI ", StringComparison.OrdinalIgnoreCase))
            return new("MSI", new Uri("https://us.msi.com/support/download/"));
        if (value.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
            return new("NVIDIA", new Uri("https://www.nvidia.com/Download/index.aspx"));
        if (value.Contains("Advanced Micro Devices", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("AMD", StringComparison.OrdinalIgnoreCase))
            return new("AMD", new Uri("https://www.amd.com/en/support"));
        if (value.Contains("Intel", StringComparison.OrdinalIgnoreCase))
            return new("Intel", new Uri("https://www.intel.com/content/www/us/en/download-center/home.html"));
        return null;
    }
}
