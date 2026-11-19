using System.Globalization;

namespace WG.MiEditor.MainApp.ViewModels.Measurement;

/// <summary>
/// A display unit for live measurements. Values arrive in base units (metres or square metres)
/// and are converted and formatted here, so unit handling is not spread across string switches.
/// <see cref="ToString"/> returns the label, which is what the unit pickers display.
/// </summary>
public sealed record MeasurementUnit(string Label, double BaseUnitsPerUnit, int Decimals)
{
    public static readonly MeasurementUnit Metres = new("m", 1, 2);
    public static readonly MeasurementUnit Kilometres = new("km", 1_000, 2);
    public static readonly MeasurementUnit SquareMetres = new("sq.m", 1, 2);
    public static readonly MeasurementUnit Hectares = new("ha", 10_000, 4);

    public static IReadOnlyList<MeasurementUnit> LengthUnits { get; } = [Metres, Kilometres];

    public static IReadOnlyList<MeasurementUnit> AreaUnits { get; } = [SquareMetres, Hectares];

    /// <summary>Converts a value in base units to this unit and formats it for display.</summary>
    public string Format(double baseValue, CultureInfo culture) =>
        (baseValue / BaseUnitsPerUnit).ToString($"N{Decimals}", culture);

    public override string ToString() => Label;
}
