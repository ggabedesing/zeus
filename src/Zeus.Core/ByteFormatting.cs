using System.Globalization;

namespace Zeus.Core;

public static class ByteFormatting
{
    private static readonly string[] Units = ["B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB"];

    public static string Format(ulong bytes)
    {
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value.ToString(unit == 0 ? "0" : "0.#", CultureInfo.GetCultureInfo("pt-BR"))} {Units[unit]}";
    }
}
