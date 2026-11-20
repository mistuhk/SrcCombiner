using System.Globalization;
using WG.MiEditor.MainApp.ViewModels.Measurement;
using Xunit;

namespace WG.MiEditor.MainApp.Tests.ViewModels.Measurement;

public class RadiusInputTests
{
    private static readonly CultureInfo UK = CultureInfo.GetCultureInfo("en-GB");
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    [Theory]
    [InlineData("500", 500)]
    [InlineData(" 500 ", 500)]
    [InlineData("500.5", 500.5)]
    [InlineData("10,000", 10_000)]
    [InlineData("1e3", 1_000)]
    public void UkDevice(string text, double expected)
    {
        Assert.True(RadiusInput.TryParse(text, UK, out var radius));
        Assert.Equal(expected, radius);
    }

    // Regression: double.TryParse with a comma-decimal culture read "500.5" as 5005.
    [Theory]
    [InlineData("500,5", 500.5)]
    [InlineData("500.5", 500.5)]
    [InlineData("10000", 10_000)]
    public void CommaDecimalDevice(string text, double expected)
    {
        Assert.True(RadiusInput.TryParse(text, German, out var radius));
        Assert.Equal(expected, radius);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public void RejectsUnusableInput(string? text)
    {
        Assert.False(RadiusInput.TryParse(text, UK, out _));
    }

    [Theory]
    [InlineData(500.5)]
    [InlineData(10_000)]
    public void FormatRoundTrips_InTheSameCulture(double value)
    {
        foreach (var culture in new[] { UK, German, CultureInfo.InvariantCulture })
        {
            Assert.True(RadiusInput.TryParse(RadiusInput.Format(value, culture), culture, out var parsed));
            Assert.Equal(value, parsed);
        }
    }
}
