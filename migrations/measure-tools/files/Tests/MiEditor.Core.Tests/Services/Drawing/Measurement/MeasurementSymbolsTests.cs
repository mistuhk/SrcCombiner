using Moq;
using WG.MiEditor.Core.Services.Drawing;
using WG.MiEditor.Core.Services.Drawing.Measurement.Components;
using WG.MiEditor.Logging.Abstractions;
using WG.MiEditor.Models.Drawing;
using Colour = System.Drawing.Color;

namespace WG.MiEditor.Core.Tests.Services.Drawing.Measurement;

// A typo in appsettings used to throw from Enum.Parse inside the operation constructors, which
// broke the whole measurement toolbar. These pin the fall-back behaviour instead.
public class MeasurementSymbolsTests
{
    private readonly Mock<ILoggerService> logger = new();
    private readonly Mock<IDrawingSymbolColourParser> parser = new();

    [Theory]
    [InlineData("Friday", DayOfWeek.Friday)]
    [InlineData("friday", DayOfWeek.Friday)]
    [InlineData("  Friday  ", DayOfWeek.Friday)]
    public void ParseStyle_AcceptsDefinedNames_IgnoringCaseAndWhitespace(string value, DayOfWeek expected)
    {
        Assert.Equal(expected, MeasurementSymbols.ParseStyle(value, DayOfWeek.Monday, "Test", logger.Object));
        logger.Verify(l => l.Warn(It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Fridya")]
    [InlineData("5")]
    [InlineData("99")]
    public void ParseStyle_FallsBackAndWarns_OnBadValues(string? value)
    {
        Assert.Equal(DayOfWeek.Monday, MeasurementSymbols.ParseStyle(value, DayOfWeek.Monday, "Test", logger.Object));
        logger.Verify(l => l.Warn(It.Is<string>(m => m.Contains("Test"))), Times.Once);
    }

    [Fact]
    public void ParseStyle_StillReturnsFallback_WhenLoggingThrows()
    {
        logger.Setup(l => l.Warn(It.IsAny<string>())).Throws<InvalidOperationException>();

        Assert.Equal(DayOfWeek.Monday, MeasurementSymbols.ParseStyle("nope", DayOfWeek.Monday, "Test", logger.Object));
    }

    [Fact]
    public void ParseColour_UsesTheParser_WhenItSucceeds()
    {
        var setting = new ColourSetting { Hex = "#FF0000", Argb = new ArgbSetting() };
        parser.Setup(p => p.FromSetting(setting)).Returns(Colour.Red);

        var colour = MeasurementSymbols.ParseColour(setting, Colour.Blue, "Test", parser.Object, logger.Object);

        Assert.Equal(Colour.Red, colour);
    }

    [Fact]
    public void ParseColour_FallsBackAndWarns_WhenTheParserThrows()
    {
        var setting = new ColourSetting { Hex = "#ZZ", Argb = new ArgbSetting() };
        parser.Setup(p => p.FromSetting(setting)).Throws<FormatException>();

        var colour = MeasurementSymbols.ParseColour(setting, Colour.Blue, "Test", parser.Object, logger.Object);

        Assert.Equal(Colour.Blue, colour);
        logger.Verify(l => l.Warn(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public void ParseColour_FallsBack_WhenTheSettingIsMissing()
    {
        var colour = MeasurementSymbols.ParseColour(null, Colour.Blue, "Test", parser.Object, logger.Object);

        Assert.Equal(Colour.Blue, colour);
    }

    [Fact]
    public void Line_WithMissingSettings_BuildsADefaultSymbol_InsteadOfThrowing()
    {
        var symbol = MeasurementSymbols.Line(null, parser.Object, logger.Object, "Line");

        Assert.Equal(MeasurementSymbols.DefaultLineWidth, symbol.Width);
        Assert.Equal(MeasurementSymbols.DefaultLineColour, symbol.Color);
    }

    [Fact]
    public void Polygon_WithATypoInTheFillStyle_StillBuildsASymbol()
    {
        parser.Setup(p => p.FromSetting(It.IsAny<ColourSetting>())).Returns(Colour.Green);
        var setting = new PolygonSymbolSetting
        {
            FillStyle = "Soild",
            FillColour = new ColourSetting { Hex = "#00FF00", Argb = new ArgbSetting() },
            Outline = new LineSymbolSetting
            {
                Style = "Solid",
                Colour = new ColourSetting { Hex = "#00FF00", Argb = new ArgbSetting() },
                Width = 1
            }
        };

        var symbol = MeasurementSymbols.Polygon(setting, parser.Object, logger.Object, "Polygon");

        Assert.NotNull(symbol);
        Assert.NotNull(symbol.Outline);
    }
}
