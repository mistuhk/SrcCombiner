using Esri.ArcGISRuntime.Symbology;
using WG.MiEditor.Logging.Abstractions;
using WG.MiEditor.Models.Drawing;
using Colour = System.Drawing.Color;

namespace WG.MiEditor.Core.Services.Drawing.Measurement.Components;

/// <summary>
/// Builds the measurement result symbols from appsettings without ever throwing. A typo or a
/// missing value in configuration logs a warning and falls back to a sensible default, instead
/// of failing service construction and taking the whole measurement toolbar down with it.
/// </summary>
public static class MeasurementSymbols
{
    public static readonly Colour DefaultLineColour = Colour.FromArgb(255, 0, 112, 255);
    public static readonly Colour DefaultFillColour = Colour.FromArgb(64, 0, 112, 255);
    public const double DefaultLineWidth = 2;

    public static SimpleLineSymbol Line(
        LineSymbolSetting? setting,
        IDrawingSymbolColourParser colourParser,
        ILoggerService logger,
        string settingName)
    {
        var style = ParseStyle(setting?.Style, SimpleLineSymbolStyle.Solid, $"{settingName}.Style", logger);
        var colour = ParseColour(setting?.Colour, DefaultLineColour, $"{settingName}.Colour", colourParser, logger);
        var width = ValidWidth(setting?.Width, $"{settingName}.Width", logger);

        return new SimpleLineSymbol(style, colour, width);
    }

    public static SimpleFillSymbol Polygon(
        PolygonSymbolSetting? setting,
        IDrawingSymbolColourParser colourParser,
        ILoggerService logger,
        string settingName)
    {
        var outline = Line(setting?.Outline, colourParser, logger, $"{settingName}.Outline");
        var fillStyle = ParseStyle(setting?.FillStyle, SimpleFillSymbolStyle.Solid, $"{settingName}.FillStyle", logger);
        var fillColour = ParseColour(setting?.FillColour, DefaultFillColour, $"{settingName}.FillColour", colourParser, logger);

        return new SimpleFillSymbol(fillStyle, fillColour, outline);
    }

    /// <summary>
    /// Case-insensitive enum parse that only accepts defined names, so "5" or "Soild" fall back
    /// to <paramref name="fallback"/> with a warning rather than producing an undefined value.
    /// </summary>
    public static TEnum ParseStyle<TEnum>(string? value, TEnum fallback, string settingName, ILoggerService logger)
        where TEnum : struct, Enum
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            !value.Trim().All(c => char.IsDigit(c) || c == '-') &&
            Enum.TryParse<TEnum>(value.Trim(), ignoreCase: true, out var parsed) &&
            Enum.IsDefined(parsed))
        {
            return parsed;
        }

        Warn(logger, $"Measurement symbol setting '{settingName}' has an unrecognised value '{value}'. Using '{fallback}'.");
        return fallback;
    }

    public static Colour ParseColour(
        ColourSetting? setting,
        Colour fallback,
        string settingName,
        IDrawingSymbolColourParser colourParser,
        ILoggerService logger)
    {
        if (setting is not null)
        {
            try
            {
                return colourParser.FromSetting(setting);
            }
            catch (Exception ex)
            {
                Warn(logger, $"Measurement symbol setting '{settingName}' is not a valid colour ({ex.Message}). Using the default.");
                return fallback;
            }
        }

        Warn(logger, $"Measurement symbol setting '{settingName}' is missing. Using the default.");
        return fallback;
    }

    private static double ValidWidth(double? width, string settingName, ILoggerService logger)
    {
        if (width is { } value && double.IsFinite(value) && value > 0)
        {
            return value;
        }

        Warn(logger, $"Measurement symbol setting '{settingName}' has an invalid width '{width}'. Using {DefaultLineWidth}.");
        return DefaultLineWidth;
    }

    private static void Warn(ILoggerService logger, string message)
    {
        try
        {
            logger.Warn(message);
        }
        catch
        {
            // A logging failure must not stop the symbol from being built.
        }
    }
}
