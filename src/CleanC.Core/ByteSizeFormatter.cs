using System.Globalization;

namespace CleanC.Core;

public static class ByteSizeFormatter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Format(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? bytes.ToString("N0", CultureInfo.InvariantCulture) + " B"
            : value.ToString("0.#", CultureInfo.InvariantCulture) + " " + Units[unit];
    }
}
