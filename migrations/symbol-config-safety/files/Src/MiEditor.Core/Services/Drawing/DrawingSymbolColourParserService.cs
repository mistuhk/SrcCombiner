using System.Globalization;
using Esri.ArcGISRuntime.Symbology;
using WG.MiEditor.Logging.Abstractions;
using WG.MiEditor.Models.Drawing;
using WG.MiEditor.Shared.AutoScanning;
using Colour = System.Drawing.Color;

namespace WG.MiEditor.Core.Services.Drawing;

/// <summary>
/// Turns the drawing symbol settings (drawingsymbols.json) into colours, styles and symbols.
/// Nothing here throws on bad or missing configuration: each problem logs a warning and a
/// default is used, so a config mistake shows up as an odd-looking symbol, not a broken tool.
/// </summary>
public interface IDrawingSymbolColourParser
{
    /// <summary>
    /// Converts a configured colour. A missing or malformed value logs a warning and falls back to
    /// the setting's ARGB value if it has one, otherwise to black.
    /// </summary>
    Colour FromSetting(ColourSetting? setting);

    /// <inheritdoc cref="FromSetting(ColourSetting?)"/>
    /// <param name="fallback">Used instead of black when nothing usable is configured.</param>
    Colour FromSetting(ColourSetting? setting, Colour fallback);

    /// <summary>
    /// Converts a configured symbol style name (for example "Solid" or "Circle"), ignoring case and
    /// surrounding whitespace. A missing or unknown name logs a warning and returns
    /// <paramref name="fallback"/>.
    /// </summary>
    TStyle StyleFromSetting<TStyle>(string? style, TStyle fallback) where TStyle : struct, Enum;

    /// <summary>
    /// Returns a configured size or width if it is a finite number greater than zero; otherwise
    /// logs a warning and returns <paramref name="fallback"/>. A missing number in the config binds
    /// as 0, which would draw nothing.
    /// </summary>
    double SizeFromSetting(double? size, double fallback);

    /// <summary>Builds a point symbol. A missing section gives a default symbol, with a warning.</summary>
    SimpleMarkerSymbol MarkerSymbol(PointSymbolSetting? setting, string settingName);

    /// <summary>Builds a line symbol. A missing section gives a default symbol, with a warning.</summary>
    SimpleLineSymbol LineSymbol(LineSymbolSetting? setting, string settingName);

    /// <summary>
    /// Builds a polygon symbol, including its outline. A missing section gives a default symbol,
    /// with a warning; the default fill is translucent so it never hides the map underneath.
    /// </summary>
    SimpleFillSymbol FillSymbol(PolygonSymbolSetting? setting, string settingName);
}

[RegisterService(ServiceLifetime.Singleton, As = new[] { typeof(IDrawingSymbolColourParser) })]
public class DrawingSymbolColourParserService(ILoggerService logger) : IDrawingSymbolColourParser
{
    public static readonly Colour Fallback = Colour.Black;
    public static readonly Colour FallbackFill = Colour.FromArgb(64, 0, 0, 0);
    public const SimpleMarkerSymbolStyle FallbackMarkerStyle = SimpleMarkerSymbolStyle.Circle;
    public const SimpleLineSymbolStyle FallbackLineStyle = SimpleLineSymbolStyle.Solid;
    public const SimpleFillSymbolStyle FallbackFillStyle = SimpleFillSymbolStyle.Solid;
    public const double FallbackMarkerSize = 10;
    public const double FallbackLineWidth = 2;

    // ---- Colours ---------------------------------------------------------------------------

    public Colour FromSetting(ColourSetting? setting) => ParseColour(setting, Fallback, settingName: null);

    public Colour FromSetting(ColourSetting? setting, Colour fallback) => ParseColour(setting, fallback, settingName: null);

    private Colour ParseColour(ColourSetting? setting, Colour fallback, string? settingName)
    {
        if (setting is null)
        {
            Warn($"{Describe("colour", settingName)} is missing; using {Name(fallback)}.");
            return fallback;
        }

        if (!string.IsNullOrWhiteSpace(setting.Hex))
        {
            if (TryParseHex(setting.Hex, out var colour))
            {
                return colour;
            }

            Warn($"{Describe("colour", settingName)} '{setting.Hex}' is not a valid #RRGGBB or #AARRGGBB value; " +
                 (setting.Argb is null ? $"using {Name(fallback)}." : "using its ARGB value instead."));
        }

        if (setting.Argb is { } argb)
        {
            return Colour.FromArgb(argb.A, argb.R, argb.G, argb.B);
        }

        return fallback;
    }

    /// <summary>Accepts RRGGBB or AARRGGBB, with or without a leading '#', ignoring surrounding whitespace.</summary>
    public static bool TryParseHex(string? hex, out Colour colour)
    {
        colour = Fallback;
        if (string.IsNullOrWhiteSpace(hex)) return false;

        var digits = hex.Trim();
        if (digits.StartsWith('#')) digits = digits[1..];

        if (digits.Length is not (6 or 8) ||
            !uint.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        if (digits.Length == 6)
        {
            value |= 0xFF000000; // no alpha given: fully opaque
        }

        colour = Colour.FromArgb(unchecked((int)value));
        return true;
    }

    // ---- Styles ----------------------------------------------------------------------------

    public TStyle StyleFromSetting<TStyle>(string? style, TStyle fallback) where TStyle : struct, Enum =>
        ParseStyle(style, fallback, settingName: null);

    private TStyle ParseStyle<TStyle>(string? style, TStyle fallback, string? settingName) where TStyle : struct, Enum
    {
        if (TryParseStyle<TStyle>(style, out var parsed))
        {
            return parsed;
        }

        Warn($"{Describe("style", settingName)} '{style}' is not a valid {typeof(TStyle).Name}; using {fallback}.");
        return fallback;
    }

    /// <summary>
    /// Accepts only a single defined name. Numbers and comma lists are rejected because
    /// Enum.TryParse would otherwise accept "99" or "Solid,Dash" as some arbitrary value.
    /// </summary>
    public static bool TryParseStyle<TStyle>(string? style, out TStyle parsed) where TStyle : struct, Enum
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(style)) return false;

        var name = style.Trim();
        return !char.IsDigit(name[0]) && name[0] is not ('-' or '+') && !name.Contains(',') &&
               Enum.TryParse(name, ignoreCase: true, out parsed) &&
               Enum.IsDefined(parsed);
    }

    // ---- Sizes -----------------------------------------------------------------------------

    public double SizeFromSetting(double? size, double fallback) => ParseSize(size, fallback, settingName: null);

    private double ParseSize(double? size, double fallback, string? settingName)
    {
        if (size is { } value && double.IsFinite(value) && value > 0)
        {
            return value;
        }

        Warn($"{Describe("size", settingName)} '{size}' is not a positive number; using {fallback}.");
        return fallback;
    }

    // ---- Symbols ---------------------------------------------------------------------------

    public SimpleMarkerSymbol MarkerSymbol(PointSymbolSetting? setting, string settingName)
    {
        if (setting is null)
        {
            WarnMissingSection(settingName);
            return new SimpleMarkerSymbol(FallbackMarkerStyle, Fallback, FallbackMarkerSize);
        }

        return new SimpleMarkerSymbol(
            ParseStyle(setting.Style, FallbackMarkerStyle, $"{settingName}.Style"),
            ParseColour(setting.Colour, Fallback, $"{settingName}.Colour"),
            ParseSize(setting.Size, FallbackMarkerSize, $"{settingName}.Size"));
    }

    public SimpleLineSymbol LineSymbol(LineSymbolSetting? setting, string settingName)
    {
        if (setting is null)
        {
            WarnMissingSection(settingName);
            return new SimpleLineSymbol(FallbackLineStyle, Fallback, FallbackLineWidth);
        }

        return new SimpleLineSymbol(
            ParseStyle(setting.Style, FallbackLineStyle, $"{settingName}.Style"),
            ParseColour(setting.Colour, Fallback, $"{settingName}.Colour"),
            ParseSize(setting.Width, FallbackLineWidth, $"{settingName}.Width"));
    }

    public SimpleFillSymbol FillSymbol(PolygonSymbolSetting? setting, string settingName)
    {
        if (setting is null)
        {
            WarnMissingSection(settingName);
            return new SimpleFillSymbol(
                FallbackFillStyle,
                FallbackFill,
                new SimpleLineSymbol(FallbackLineStyle, Fallback, FallbackLineWidth));
        }

        return new SimpleFillSymbol(
            ParseStyle(setting.FillStyle, FallbackFillStyle, $"{settingName}.FillStyle"),
            ParseColour(setting.FillColour, FallbackFill, $"{settingName}.FillColour"),
            LineSymbol(setting.Outline, $"{settingName}.Outline"));
    }

    // ---- Warnings --------------------------------------------------------------------------

    private void WarnMissingSection(string settingName) =>
        Warn($"Drawing symbol setting '{settingName}' is missing from the configuration; using a default symbol.");

    private static string Describe(string what, string? settingName) =>
        settingName is null ? $"Drawing symbol {what}" : $"Drawing symbol setting '{settingName}'";

    private static string Name(Colour colour) =>
        colour.IsNamedColor ? colour.Name : $"#{colour.A:X2}{colour.R:X2}{colour.G:X2}{colour.B:X2}";

    private void Warn(string message)
    {
        // A failing logger must not turn a bad setting back into a crash.
        try { logger.Warn(message); } catch { /* ignored */ }
    }
}
