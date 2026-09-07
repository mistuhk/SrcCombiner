# Migration: parcel-reference-changes (Browsers batch 2)

Themes the parcel-reference-changes browser (chrome only). Data grid/lists, no MapView. Pure XAML, no testable logic; verified by running the app. Pending Windows verify.

## Mapping (AppThemeBinding)
- Divider BoxViews (LightGray/Gray) -> Border strong.
- Row stripes: LightGray -> Surface variant, White -> Surface. Selected (LightBlue/Yellow) -> Subform background.
- CollectionView/border backgrounds White -> Surface.
- Header + data label text -> On surface (data-row labels inline per element, per the CollectionView live-switch rule; header/top labels via their once-rendered styles).
- No style dictionary (all colour was inline in the control). Native DatePicker/Picker follow the themed implicit styles, unchanged.
