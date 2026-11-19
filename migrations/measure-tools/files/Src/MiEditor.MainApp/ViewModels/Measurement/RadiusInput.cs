using System.Globalization;

namespace WG.MiEditor.MainApp.ViewModels.Measurement;

/// <summary>
/// Parses the buffer radius the user types. Accepts the device's own number format first, then
/// the invariant one, so "500.5" works on a device that uses a comma as the decimal separator.
/// Thousands separators are only tried last, so they can never silently turn a decimal point
/// into a grouping (for example "500.5" read as 5005).
/// </summary>
public static class RadiusInput
{
    public static bool TryParse(string? text, CultureInfo culture, out double radius)
    {
        radius = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var trimmed = text.Trim();

        var parsed =
            double.TryParse(trimmed, NumberStyles.Float, culture, out radius) ||
            double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out radius) ||
            double.TryParse(trimmed, NumberStyles.Float | NumberStyles.AllowThousands, culture, out radius);

        // NaN and infinity parse successfully but are never a usable radius.
        return parsed && double.IsFinite(radius);
    }

    /// <summary>Formats a radius so that <see cref="TryParse"/> reads it back unchanged.</summary>
    public static string Format(double radius, CultureInfo culture) => radius.ToString(culture);
}
