# Migration: attribute-error (Browsers batch 2)

Themes the Attribute Error browser (chrome only). Part of BR batch 2 (one migration
per browser). Data grid and lists, no MapView inside. Pure XAML, no testable logic,
verified by running the app.

## Files (modified)
- `Src/MiEditor.MainApp/Controls/Browsers/AttributeErrorBrowserControl.xaml`
- `Src/MiEditor.MainApp/Resources/Styles/Browsers/AttributeErrorBrowserControlStyle.xaml`

## Colour mapping (all via AppThemeBinding)
- Divider BoxViews `LightGray` / `Gray` -> Border strong.
- Heading border and both CollectionView backgrounds `White` -> Surface.
- Row stripes: odd `LightGray` -> Surface variant, even `White` -> Surface.
- Selected rows `Yellow` / `LightBlue` -> Subform background.
- Heading border status stroke `Red` -> Error brush, `Black` -> Border strong brush.
- Header labels and data-row labels: added inline `TextColor` -> On surface (inline
  per element, since Style-based AppThemeBinding on virtualised item labels does not
  live-update). Also the "No data." empty-state label and the by-passable status label.

## Deferred to batch 3 (not changed here)
- `BooleanToColorConverter` (FalseColor Red / TrueColor Black), used for the health
  check value TextColor, is retired onto themed roles in BR batch 3 with tests.

## Verify
Not built here (Windows-only). Well-formed XML confirmed; no literal colour left in
either file except the batch-3 converter params. Pending Windows verify.
