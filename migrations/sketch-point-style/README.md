# sketch-point-style

`AddSketchPointOperation` draws with the `InFieldSketchPoint` style instead of `Point`.

Apply after `symbol-config-safety`, which introduced the `MarkerSymbol` call this changes.

## Why

Every other sketch path already uses the in-field sketch styles: `AddSketchLineOperation`,
`AddSketchPolygonOperation`, and the `isSketch` branches of `AddBufferService` and
`ImportGraphicsService`. Sketch points were the only exception, which looked like a copy and paste
mistake.

## Visible effect

With the shipped `drawingsymbols.json`, both styles are a size 10 circle, but:

| Setting | Colour |
| --- | --- |
| `Point` (before) | `#00FFFF` |
| `InFieldSketchPoint` (after) | `#00A9E6` |

Sketch points change from cyan to blue. If cyan was intended, change `InFieldSketchPoint` in the
configuration rather than reverting this.

## Changes

| File | Change |
| --- | --- |
| `Src/MiEditor.Core/Services/Drawing/Editing/AddFeatures/Operation/AddSketchPointOperation.cs` | `MarkerSymbol(symbolSettings.Value.InFieldSketchPoint, "InFieldSketchPoint")` |
