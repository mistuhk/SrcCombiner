# Migration: sliver-area (Browsers batch 2)

Themes the sliver-area browser (chrome only). Data grid/lists, no MapView. Pure XAML, no testable logic; verified by running the app. Pending Windows verify.

## Mapping (AppThemeBinding)
- Divider BoxViews (LightGray/Gray) -> Border strong.
- Row stripes: LightGray -> Surface variant, White -> Surface. Selected (LightBlue/Yellow) -> Subform background.
- CollectionView/border backgrounds White -> Surface.
- Header + data label text -> On surface (data-row labels inline per element, per the CollectionView live-switch rule; header/top labels via their once-rendered styles).
- Picker label given On surface; the layer Picker follows the themed implicit Picker style.
