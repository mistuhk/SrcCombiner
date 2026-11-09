# Info panel dark theme fix

In Dark theme the MI-Editor Info panel showed light text on light backgrounds: the panel, the
attribute table and the selected node kept fixed light colours while the label text followed the
theme.

## Cause

The panel was not covered by the theming work. Its colours came from `PanelBorderStyle` and four
plain `Color` resources in `Styles.xaml` (`InfoTableGridBackgroundColor`,
`InfoTableHeaderBackgroundColor`, `InfoNodeBackgroundColor`, `TableBorderBackgroundColor`), plus a
hardcoded black cell stroke. They are read in code, so they could not follow the theme.

## Change

Every colour now comes from an existing `Theme.xaml` brush. No new colours.

| Role | Was | Now |
| --- | --- | --- |
| Panel and pane background | WhiteSmoke | `SurfaceVariantBrush` |
| Panel, node and cell strokes | LightGray, Black | `BorderStrongBrush` |
| Attribute table body | #e0e9f8 | `SubformBackgroundBrush` |
| Table header row | LightGray | `ToolStripBrush` |
| Selected or expanded node | #dae3f1 | `ToolButtonActiveBrush` |
| Node and cell text | platform default | `OnSurfaceLight` / `OnSurfaceDark` |

- `Styles.xaml`: `PanelBorderStyle` uses the brushes; the four `Info*` / `TableBorder*` colours are
  removed (no other users).
- `IInfoAttributeHelper` / `InfoAttributeHelper` (Core): `CreateTableCell` takes `Brush?` instead
  of `Color?`. Strokes use `BorderStrongBrush`. Labels from `CreateLabel` and `CreateTableCell` get
  a theme text colour through `SetAppThemeColor`.
- `LoadAttributeDetailViewModel`: table backgrounds are set through `Background` with the brushes.
- `InfoAttributePanel.xaml.cs`: node highlight set and clear both use `Background`, since a
  `Background` brush overrides `BackgroundColor`.

The brushes use `AppThemeBinding`, so an open panel follows a theme change live.

Left as is: the dark header bar and close button (readable in both themes), the map highlight
graphics, and the attachment rows in `LoadAttachmentViewModel` (their text follows the theme and
sits on the themed table body; the white chip keeps the eye icon readable).

## Light theme

Slight shifts: table body #e0e9f8 to #eef4fb, selected node #dae3f1 to #d0e8ff, cell lines black
to #d2d2d2, text black to #222222. Header row is unchanged (#d3d3d3 is LightGray).

## Build

Not built here; the local copy cannot be built on macOS. Build and check on Windows in Light and
Dark: open Info on an LPIS parcel, expand the nodes, select a child, and check a feature with
attachments and Additional Attributes.
