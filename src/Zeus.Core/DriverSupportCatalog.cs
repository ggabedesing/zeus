namespace Zeus.Core;

/// <summary>Fixed official driver lookup destinations. A link is not evidence of an available update.</summary>
public sealed record DriverSupportSource(string Name, Uri Uri);

public static class DriverSupportCatalog
{
    public static DriverSupportSource? Find(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider)) return null;
        var value = provider.Trim();
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
