# Migration: qa-attribute (Browsers batch 2)

Themes the qa-attribute browser (chrome only). Data grid/lists, no MapView. Pure XAML, no testable logic; verified by running the app. Pending Windows verify.

## Mapping (AppThemeBinding)
- Divider BoxViews (LightGray/Gray) -> Border strong.
- Row stripes: LightGray -> Surface variant, White -> Surface. Selected (LightBlue/Yellow) -> Subform background.
- CollectionView/border backgrounds White -> Surface.
- Header + data label text -> On surface (data-row labels inline per element, per the CollectionView live-switch rule; header/top labels via their once-rendered styles).
- Layer-info panel Border Stroke #5A7D95 -> Field border (prior off-palette decision).
