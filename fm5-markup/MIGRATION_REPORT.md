# Migration: fm5-markup (Browsers batch 2)

Themes the FM5 Markup browser (chrome only). Pure XAML, no testable logic; verified by running the app.

## Files (modified)
- Src/MiEditor.MainApp/Controls/Browsers/FM5MarkupBrowserControl.xaml
- Src/MiEditor.MainApp/Resources/Styles/Browsers/FM5MarkupBrowserControlStyle.xaml

## Colour mapping (AppThemeBinding)
- Divider BoxView LightGray -> Border strong.
- Row stripes: LightGray -> Surface variant, White -> Surface. Selected LightBlue -> Subform background.
- Header label style TextColor Black -> On surface (header rendered once, style-based is safe).
- 12 data labels: inline TextColor -> On surface (virtualised item labels, inline per element per the live-switch rule).

No converters in this browser. Pending Windows verify.
