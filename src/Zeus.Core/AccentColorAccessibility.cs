namespace Zeus.Core;

/// <summary>Strict color parsing and WCAG contrast math for accessible UI accents.</summary>
public static class AccentColorAccessibility
{
    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (value is not { Length: 7 } || value[0] != '#') return false;
        for (var index = 1; index < value.Length; index++)
            if (!Uri.IsHexDigit(value[index])) return false;
        normalized = value.ToUpperInvariant();
        return true;
    }

    public static double ContrastRatio(string foreground, string background)
    {
        if (!TryNormalize(foreground, out var normalizedForeground) ||
            !TryNormalize(background, out var normalizedBackground))
            throw new ArgumentException("Colors must use the #RRGGBB format.");

        var foregroundLuminance = RelativeLuminance(normalizedForeground);
        var backgroundLuminance = RelativeLuminance(normalizedBackground);
        var lighter = Math.Max(foregroundLuminance, backgroundLuminance);
        var darker = Math.Min(foregroundLuminance, backgroundLuminance);
        return (lighter + 0.05) / (darker + 0.05);
    }

    public static bool MeetsContrast(string foreground, string background, double minimumRatio = 4.5) =>
        double.IsFinite(minimumRatio) && minimumRatio >= 1 && ContrastRatio(foreground, background) >= minimumRatio;

    private static double RelativeLuminance(string normalized)
    {
        var red = ToLinear(Convert.ToByte(normalized.Substring(1, 2), 16) / 255d);
        var green = ToLinear(Convert.ToByte(normalized.Substring(3, 2), 16) / 255d);
        var blue = ToLinear(Convert.ToByte(normalized.Substring(5, 2), 16) / 255d);
        return 0.2126 * red + 0.7152 * green + 0.0722 * blue;
    }

    private static double ToLinear(double channel) => channel <= 0.04045
        ? channel / 12.92
        : Math.Pow((channel + 0.055) / 1.055, 2.4);
}
