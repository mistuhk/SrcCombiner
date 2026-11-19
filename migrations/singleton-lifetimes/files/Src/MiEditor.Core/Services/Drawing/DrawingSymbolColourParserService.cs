
using System.Globalization;
using WG.MiEditor.Models.Drawing;
using WG.MiEditor.Shared.AutoScanning;
using Colour = System.Drawing.Color;

namespace WG.MiEditor.Core.Services.Drawing;

public interface IDrawingSymbolColourParser
{
    Colour FromSetting(ColourSetting setting);
}

[RegisterService(ServiceLifetime.Singleton, As = new[] { typeof(IDrawingSymbolColourParser) })]
public class DrawingSymbolColourParserService : IDrawingSymbolColourParser
{
    public Colour FromSetting(ColourSetting setting)
    {
        if (!string.IsNullOrWhiteSpace(setting.Hex))
        {
            return FromHex(setting.Hex);
        }

        if (setting.Argb != null)
        {
            return Colour.FromArgb(setting.Argb.A, setting.Argb.R, setting.Argb.G, setting.Argb.B);
        }

        return Colour.Black;
    }

    private static Colour FromHex(string hex)
    {
        hex = hex.Replace("#", "");
        if (hex.Length == 6)
        {
            return Colour.FromArgb(255,
                int.Parse(hex.Substring(0, 2), NumberStyles.HexNumber),
                int.Parse(hex.Substring(2, 2), NumberStyles.HexNumber),
                int.Parse(hex.Substring(4, 2), NumberStyles.HexNumber));
        }

        return Colour.FromArgb(
            int.Parse(hex.Substring(0, 2), NumberStyles.HexNumber),
            int.Parse(hex.Substring(2, 2), NumberStyles.HexNumber),
            int.Parse(hex.Substring(4, 2), NumberStyles.HexNumber),
            int.Parse(hex.Substring(6, 2), NumberStyles.HexNumber));
    }

}
