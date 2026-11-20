# sketch-point-style

`AddSketchPointOperation` draws with the `InFieldSketchPoint` style instead of `Point`.

## Apply order

Step 4 of 5. Previous: `symbol-config-safety`. Next: `tool-default-hash`.

Needs first: `symbol-config-safety`, which adds the `MarkerSymbol(...)` call in `AddSketchPointOperation.cs` that this patch changes.

Full order:

1. `measure-tools`
2. `singleton-lifetimes`
3. `symbol-config-safety`
4. `sketch-point-style` (this package)
5. `tool-default-hash`

From the repository root:

    git apply migrations/sketch-point-style/sketch-point-style.patch

Copy the patch as a file; never paste its text. If `git apply` reports `trailing whitespace` or
`corrupt patch`, the copy was altered: check it against `../SHA256SUMS` and copy it again. See
`../CHECKPOINT.md` for the full steps and what to do if a patch will not apply.

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
