using System.Globalization;

namespace AomrModEditor.Application.Services;

/// <summary>Armor XML stores decimal fraction; UI shows percent blocked (REQ-U01).</summary>
public static class ArmorPercentConverter
{
    public static string? ToDisplayPercent(string? fraction)
    {
        if (string.IsNullOrWhiteSpace(fraction)) return null;
        if (!decimal.TryParse(fraction, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return fraction;
        var percent = value * 100m;
        // Trim scale zeros (e.g. 0.0001 → 0.01, not 0.0100)
        return percent.ToString("0.##############", CultureInfo.InvariantCulture);
    }

    public static string ToFraction(string percent, int decimals = 4)
    {
        if (!decimal.TryParse(percent, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            throw new FormatException($"Invalid percent value '{percent}'.");
        var fraction = value / 100m;
        return fraction.ToString("0." + new string('0', decimals), CultureInfo.InvariantCulture);
    }
}
