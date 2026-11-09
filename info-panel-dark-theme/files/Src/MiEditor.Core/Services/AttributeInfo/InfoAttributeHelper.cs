

using CommunityToolkit.Maui.Views;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Symbology;
using WG.MiEditor.Shared.AutoScanning;

namespace WG.MiEditor.Core.Services.AttributeInfo
{
    [RegisterService(ServiceLifetime.Singleton, As = new[] { typeof(IInfoAttributeHelper) })]
    public class InfoAttributeHelper : IInfoAttributeHelper
    {
        private const string BorderBrushKey = "BorderStrongBrush";

        public Label CreateLabel(string text, 
            double fontSize = 12, 
            Thickness? margin = null, 
            FontAttributes fontAttributes = FontAttributes.None, 
            Thickness? padding = null)
        {
            return WithThemeText(new Label
            {
                Text = text,
                FontSize = fontSize,
                Padding = padding ?? new Thickness(0),
                Margin = margin ?? new Thickness(0) // Default to no margin if null
            });
        }

        public Border CreateStyledBorder(View content, Color? strokeColor = null, Thickness? margin = null)
        {
            ArgumentNullException.ThrowIfNull(content);

            if (Application.Current == null)
            {
                throw new InvalidOperationException("Application.Current is null.");
            }

            if (Application.Current.Resources == null)
            {
                throw new InvalidOperationException("Application resources are not available.");
            }

            if (!Application.Current.Resources.TryGetValue(BorderBrushKey, out var resourceValue))
            {
                throw new InvalidOperationException($"Resource '{BorderBrushKey}' not found.");
            }

            if (resourceValue is not Brush defaultStroke)
            {
                throw new InvalidOperationException($"Resource '{BorderBrushKey}' is not a Brush.");
            }

            return new Border
            {
                Padding = 5,
                Margin = margin ?? new Thickness(0, 5),
                Stroke = strokeColor is null ? defaultStroke : (Brush)strokeColor,
                StrokeThickness = 1,
                Background = Colors.Transparent,
                Content = content
            };
        }

        public Expander CreateExpander(View header, View? content = null, Thickness? padding = null)
        {
            ArgumentNullException.ThrowIfNull(header);

            var safeContent = content ?? new StackLayout();
            var safePadding = padding ?? new Thickness(0);

            return new Expander
            {
                Header = header,
                Content = safeContent,
                Padding = safePadding
            };
        }

        public TapGestureRecognizer CreateTapGesture(Action tappedAction)
        {
            var tap = new TapGestureRecognizer();
            tap.Tapped += (s, e) => tappedAction();
            return tap;
        }

        public Border CreateTableCell(string text, bool isHeader, double borderThickness, Brush? background, int row, int column)
        {
            var cell = new Border
            {
                Stroke = (Brush)Application.Current!.Resources[BorderBrushKey],
                StrokeThickness = borderThickness,
                Background = background ?? Colors.Transparent,
                Content = WithThemeText(new Label
                {
                    Text = text,
                    FontAttributes = isHeader ? FontAttributes.Bold : FontAttributes.None,
                    FontSize = 12,
                    Padding = 5
                })
            };
            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, column);
            return cell;
        }

        public Esri.ArcGISRuntime.UI.Graphic? CreateGraphic(ArcGISFeature feature, System.Drawing.Color color)
        {
            Symbol highlightSymbol;
            if (feature.Geometry != null)
            {
                if (feature.Geometry.GeometryType == GeometryType.Point)
                {
                    highlightSymbol = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, color, 15);
                }
                else
                {
                    highlightSymbol = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, color, 3);
                }

                var graphic = new Esri.ArcGISRuntime.UI.Graphic(feature.Geometry, highlightSymbol);
                return graphic;
            }
            else
            {
                return null;
            }
        }

        private static Label WithThemeText(Label label)
        {
            var resources = Application.Current!.Resources;
            label.SetAppThemeColor(Label.TextColorProperty, (Color)resources["OnSurfaceLight"], (Color)resources["OnSurfaceDark"]);
            return label;
        }
    }
}
