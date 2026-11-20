using Esri.ArcGISRuntime.Symbology;
using Moq;
using WG.MiEditor.Core.Services.Drawing;
using WG.MiEditor.Logging.Abstractions;
using WG.MiEditor.Models.Drawing;
using Colour = System.Drawing.Color;

namespace WG.MiEditor.Core.Tests.Services.Drawing;

// A bad hex in drawingsymbols.json used to throw from int.Parse / Substring inside whichever tool
// built its symbols first. These pin the fall-back behaviour instead.
public class DrawingSymbolColourParserServiceTests
{
    private readonly Mock<ILoggerService> logger = new();
    private readonly DrawingSymbolColourParserService sut;

    public DrawingSymbolColourParserServiceTests() => sut = new DrawingSymbolColourParserService(logger.Object);

    private static ColourSetting Hex(string hex, ArgbSetting? argb = null) => new() { Hex = hex, Argb = argb! };

    [Theory]
    [InlineData("#FF0000", 255, 255, 0, 0)]
    [InlineData("#00a9e6", 255, 0, 169, 230)]
    [InlineData("#FF59B000", 255, 89, 176, 0)]
    [InlineData("#4000FF00", 64, 0, 255, 0)]
    [InlineData("00FFFF", 255, 0, 255, 255)]
    [InlineData("  #FF0000  ", 255, 255, 0, 0)]
    public void ValidHex_IsParsed(string hex, int a, int r, int g, int b)
    {
        Assert.Equal(Colour.FromArgb(a, r, g, b), sut.FromSetting(Hex(hex)));
        logger.Verify(l => l.Warn(It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData("#")]
    [InlineData("#F00")]          // shorthand is not supported by the schema
    [InlineData("#FF00")]         // too short: used to throw ArgumentOutOfRangeException
    [InlineData("#FF00000")]      // 7 digits: used to be read as ARGB with a missing digit
    [InlineData("#GG0000")]       // not hex: used to throw FormatException
    [InlineData("#FF0000FF00")]   // too long: used to silently drop the trailing digits
    [InlineData("#-F0000")]
    [InlineData("red")]
    public void BadHex_FallsBackToBlack_AndWarns(string hex)
    {
        Assert.Equal(DrawingSymbolColourParserService.Fallback, sut.FromSetting(Hex(hex)));
        logger.Verify(l => l.Warn(It.Is<string>(m => m.Contains(hex))), Times.Once);
    }

    [Fact]
    public void BadHex_FallsBackToTheArgbValue_WhenOneIsConfigured()
    {
        var setting = Hex("#ZZZZZZ", new ArgbSetting { A = 255, R = 1, G = 2, B = 3 });

        Assert.Equal(Colour.FromArgb(255, 1, 2, 3), sut.FromSetting(setting));
        logger.Verify(l => l.Warn(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public void BlankHex_UsesTheArgbValue_WithoutWarning()
    {
        var setting = Hex("", new ArgbSetting { A = 128, R = 10, G = 20, B = 30 });

        Assert.Equal(Colour.FromArgb(128, 10, 20, 30), sut.FromSetting(setting));
        logger.Verify(l => l.Warn(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void BlankHex_AndNoArgb_IsBlack()
    {
        Assert.Equal(Colour.Black, sut.FromSetting(Hex("")));
    }

    [Fact]
    public void MissingSetting_IsBlack_AndWarns()
    {
        Assert.Equal(Colour.Black, sut.FromSetting(null));
        logger.Verify(l => l.Warn(It.IsAny<string>()), Times.Once);
    }

    // DayOfWeek stands in for the Esri symbol style enums, which are parsed the same way.
    [Theory]
    [InlineData("Friday", DayOfWeek.Friday)]
    [InlineData("friday", DayOfWeek.Friday)]
    [InlineData("  FRIDAY ", DayOfWeek.Friday)]
    public void Style_DefinedNames_AreParsed(string style, DayOfWeek expected)
    {
        Assert.Equal(expected, sut.StyleFromSetting(style, DayOfWeek.Monday));
        logger.Verify(l => l.Warn(It.IsAny<string>()), Times.Never);
    }

    // Each of these used to throw from Enum.Parse (or, for numbers, produce an undefined style).
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Fridya")]
    [InlineData("5")]
    [InlineData("99")]
    [InlineData("-1")]
    [InlineData("+2")]
    [InlineData("Friday,Saturday")]
    public void Style_BadNames_FallBack_AndWarn(string? style)
    {
        Assert.Equal(DayOfWeek.Monday, sut.StyleFromSetting(style, DayOfWeek.Monday));
        logger.Verify(l => l.Warn(It.Is<string>(m => m.Contains(nameof(DayOfWeek)))), Times.Once);
    }

    [Fact]
    public void Style_StillReturnsTheFallback_WhenLoggingThrows()
    {
        logger.Setup(l => l.Warn(It.IsAny<string>())).Throws<InvalidOperationException>();

        Assert.Equal(DayOfWeek.Monday, sut.StyleFromSetting("nope", DayOfWeek.Monday));
    }

    // ---- Sizes ----

    [Theory]
    [InlineData(null)]
    [InlineData(0d)]
    [InlineData(-3d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Size_Unusable_FallsBack_AndWarns(double? size)
    {
        Assert.Equal(7, sut.SizeFromSetting(size, 7));
        logger.Verify(l => l.Warn(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public void Size_Positive_IsKept()
    {
        Assert.Equal(2.5, sut.SizeFromSetting(2.5, 7));
        logger.Verify(l => l.Warn(It.IsAny<string>()), Times.Never);
    }

    // ---- Symbols: a missing config section used to throw NullReferenceException ----

    private static ColourSetting Red => new() { Hex = "#FF0000", Argb = null! };

    [Fact]
    public void MarkerSymbol_MissingSection_GivesADefault_AndOneWarningNamingIt()
    {
        var symbol = sut.MarkerSymbol(null, "SelectedVertexSymbol");

        Assert.Equal(DrawingSymbolColourParserService.FallbackMarkerStyle, symbol.Style);
        Assert.Equal(DrawingSymbolColourParserService.FallbackMarkerSize, symbol.Size);
        logger.Verify(l => l.Warn(It.Is<string>(m => m.Contains("SelectedVertexSymbol"))), Times.Once);
    }

    [Fact]
    public void MarkerSymbol_ValidSection_IsBuiltFromIt()
    {
        var symbol = sut.MarkerSymbol(new PointSymbolSetting { Style = "Square", Colour = Red, Size = 6 }, "Point");

        Assert.Equal(SimpleMarkerSymbolStyle.Square, symbol.Style);
        Assert.Equal(Colour.FromArgb(255, 255, 0, 0), symbol.Color);
        Assert.Equal(6, symbol.Size);
        logger.Verify(l => l.Warn(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void MarkerSymbol_MissingFields_EachFallBack_AndAreNamedInTheWarnings()
    {
        var symbol = sut.MarkerSymbol(new PointSymbolSetting { Style = null!, Colour = null!, Size = 0 }, "Point");

        Assert.Equal(DrawingSymbolColourParserService.FallbackMarkerStyle, symbol.Style);
        Assert.Equal(DrawingSymbolColourParserService.Fallback, symbol.Color);
        Assert.Equal(DrawingSymbolColourParserService.FallbackMarkerSize, symbol.Size);
        logger.Verify(l => l.Warn(It.Is<string>(m => m.Contains("Point.Style"))), Times.Once);
        logger.Verify(l => l.Warn(It.Is<string>(m => m.Contains("Point.Colour"))), Times.Once);
        logger.Verify(l => l.Warn(It.Is<string>(m => m.Contains("Point.Size"))), Times.Once);
    }

    [Fact]
    public void LineSymbol_MissingSection_GivesADefault()
    {
        var symbol = sut.LineSymbol(null, "Line");

        Assert.Equal(DrawingSymbolColourParserService.FallbackLineStyle, symbol.Style);
        Assert.Equal(DrawingSymbolColourParserService.FallbackLineWidth, symbol.Width);
    }

    [Fact]
    public void FillSymbol_MissingSection_GivesATranslucentDefault_WithAnOutline()
    {
        var symbol = sut.FillSymbol(null, "Polygon");

        Assert.Equal(DrawingSymbolColourParserService.FallbackFill, symbol.Color);
        Assert.True(symbol.Color.A < 255, "A default fill must not hide the map.");
        Assert.IsType<SimpleLineSymbol>(symbol.Outline);
    }

    [Fact]
    public void FillSymbol_MissingOutline_StillBuilds_AndNamesTheOutlineInTheWarning()
    {
        var setting = new PolygonSymbolSetting { FillStyle = "BackwardDiagonal", FillColour = Red, Outline = null! };

        var symbol = sut.FillSymbol(setting, "Polygon");

        Assert.Equal(SimpleFillSymbolStyle.BackwardDiagonal, symbol.Style);
        Assert.Equal(Colour.FromArgb(255, 255, 0, 0), symbol.Color);
        var outline = Assert.IsType<SimpleLineSymbol>(symbol.Outline);
        Assert.Equal(DrawingSymbolColourParserService.FallbackLineWidth, outline.Width);
        logger.Verify(l => l.Warn(It.Is<string>(m => m.Contains("Polygon.Outline"))), Times.Once);
    }

    [Fact]
    public void FillSymbol_MissingFillColour_IsTranslucent_NotOpaqueBlack()
    {
        var setting = new PolygonSymbolSetting
        {
            FillStyle = "Solid",
            FillColour = null!,
            Outline = new LineSymbolSetting { Style = "Solid", Colour = Red, Width = 1 }
        };

        Assert.Equal(DrawingSymbolColourParserService.FallbackFill, sut.FillSymbol(setting, "Polygon").Color);
    }

    [Fact]
    public void ColourWithFallback_UsesTheGivenFallback()
    {
        Assert.Equal(Colour.Lime, sut.FromSetting(null, Colour.Lime));
    }

    [Fact]
    public void BadHex_StillReturnsAColour_WhenLoggingThrows()
    {
        logger.Setup(l => l.Warn(It.IsAny<string>())).Throws<InvalidOperationException>();

        Assert.Equal(Colour.Black, sut.FromSetting(Hex("#nope")));
    }
}
