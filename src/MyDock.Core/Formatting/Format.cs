using System.Globalization;

namespace MyDock.Core.Formatting;

/// <summary>Compact human-readable numbers for the UI. Culture defaults to the current one.</summary>
public static class Format
{
    private static readonly string[] BinaryUnits = ["B", "KiB", "MiB", "GiB", "TiB"];
    private static readonly string[] DecimalUnits = ["B", "kB", "MB", "GB", "TB"];

    /// <summary>Memory sizes in binary units, as <c>docker stats</c> shows them.</summary>
    public static string Memory(ulong bytes, IFormatProvider? culture = null) =>
        Scale(bytes, 1024, BinaryUnits, culture);

    /// <summary>Disk sizes in decimal units, as <c>docker system df</c> shows them.</summary>
    public static string Size(ulong bytes, IFormatProvider? culture = null) =>
        Scale(bytes, 1000, DecimalUnits, culture);

    /// <summary>Transfer rates in decimal units per second.</summary>
    public static string Rate(double bytesPerSecond, IFormatProvider? culture = null) =>
        Scale(Math.Max(0, bytesPerSecond), 1000, DecimalUnits, culture) + "/s";

    public static string Percent(double value, IFormatProvider? culture = null) =>
        value.ToString(value >= 100 ? "0" : "0.0", culture ?? CultureInfo.CurrentCulture) + "%";

    private static string Scale(double value, double step, string[] units, IFormatProvider? culture)
    {
        var unit = 0;
        while (value >= step && unit < units.Length - 1)
        {
            value /= step;
            unit++;
        }

        // Whole bytes never need decimals; above that keep roughly three significant digits.
        var format = unit == 0 || value >= 100 ? "0" : value >= 10 ? "0.#" : "0.##";
        return value.ToString(format, culture ?? CultureInfo.CurrentCulture) + " " + units[unit];
    }
}
