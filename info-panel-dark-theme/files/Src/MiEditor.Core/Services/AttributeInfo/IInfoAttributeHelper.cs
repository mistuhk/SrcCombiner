
using CommunityToolkit.Maui.Views;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.UI;

namespace WG.MiEditor.Core.Services.AttributeInfo
{
    public interface IInfoAttributeHelper
    {
        Label CreateLabel(string text, double fontSize = 12, Thickness? margin = null, FontAttributes fontAttributes = FontAttributes.None, Thickness? padding = null);
        Border CreateStyledBorder(View content, Color? strokeColor = null, Thickness? margin = null);
        Expander CreateExpander(View header, View? content = null, Thickness? padding = null);
        TapGestureRecognizer CreateTapGesture(Action tappedAction);
        Border CreateTableCell(string text, bool isHeader, double borderThickness, Brush? background, int row, int column);
        Graphic? CreateGraphic(ArcGISFeature feature, System.Drawing.Color color);
    }
}
