# symbol-config-safety

A mistake in `drawingsymbols.json` no longer crashes the tool that reads it. Bad or missing
colours, styles, sizes and whole sections each log a warning naming the setting and fall back to
a default.

## Apply order

Step 3 of 5. Previous: `singleton-lifetimes`. Next: `sketch-point-style`.

Needs first: `singleton-lifetimes`. It changes `DrawingSymbolColourParserService.cs` again, and `AoiControlViewModel.cs` and `MeasurementSymbols.cs` as `measure-tools` left them.

Full order:

1. `measure-tools`
2. `singleton-lifetimes`
3. `symbol-config-safety` (this package)
4. `sketch-point-style`
5. `tool-default-hash`

From the repository root:

    git apply migrations/symbol-config-safety/symbol-config-safety.patch

Copy the patch as a file; never paste its text. If `git apply` reports `trailing whitespace` or
`corrupt patch`, the copy was altered: check it against `../SHA256SUMS` and copy it again. See
`../CHECKPOINT.md` for the full steps and what to do if a patch will not apply.

## Faults fixed

| Fault | Before | After |
| --- | --- | --- |
| Malformed hex, e.g. `#FF00`, `#GG0000` | `ArgumentOutOfRangeException` or `FormatException` | ARGB value if set, otherwise black, with a warning |
| Hex of the wrong length, e.g. 7 or 10 digits | Wrong colour, silently | Rejected, falls back, with a warning |
| Misspelt style, e.g. `"Soild"` | `Enum.Parse` threw from 30 call sites in 13 classes | Default style, with a warning |
| Numeric or comma list style, e.g. `"99"`, `"Solid,Dash"` | Accepted as an undefined value | Rejected, falls back |
| Missing section, e.g. no `Polygon.Outline` | `NullReferenceException` | Default symbol, with a warning naming `Polygon.Outline` |
| Size or width of 0 or missing | Invisible symbol | 10 for points, 2 for lines |
| Missing `FlashFeature` section | Flash failed and logged an error | Flashes green, the same as flash messages |

## Design

`IDrawingSymbolColourParser` is already injected into every class that builds symbols, so it gained
the new methods rather than every constructor changing:

- `FromSetting(setting)` and `FromSetting(setting, fallback)`
- `StyleFromSetting<TStyle>(style, fallback)`
- `SizeFromSetting(size, fallback)`
- `MarkerSymbol(setting, name)`, `LineSymbol(setting, name)`, `FillSymbol(setting, name)`

The three builders replace the same block that was copied into 13 classes. A missing polygon fill
falls back to a translucent colour so it can never hide the map. The parser takes `ILoggerService`
and swallows logger failures.

The name `IDrawingSymbolColourParser` now undersells what it does. A rename to something like
`IDrawingSymbolFactory` touches about 25 files and was left for a separate change.

## Defaults

| Setting | Default |
| --- | --- |
| Point style, size | Circle, 10 |
| Line style, width | Solid, 2 |
| Fill style | Solid |
| Colour | Black |
| Fill colour | ARGB(64, 0, 0, 0) |

## Changes

| File | Change |
| --- | --- |
| `DrawingSymbolColourParserService.cs` | Safe hex, style and size parsing; three symbol builders; takes `ILoggerService` |
| 7 add feature operations | Build `finalGraphicSymbol` with one builder call |
| `AddBufferService`, `IFeatureHighlightService`, `ImportGraphicsService` | `switch` arms use the builders |
| `VertexManagementOperationBase`, `MovePointOperation`, `AoiControlViewModel` | Vertex and line symbols use the builders |
| `DragSelectionStrategy` | Keeps its fixed dashed outline and solid fill; colours and width now null safe |
| `ZoomToFeatureControl.xaml.cs` | Missing `FlashFeature` falls back to green |
| `MeasurementSymbols.cs` | Shares the parser's style check |
| `Tests/.../DrawingSymbolColourParserServiceTests.cs` | New, 46 cases |

## Verification

- Every colour, style, size and section in the shipped `drawingsymbols.json` parses to the same
  value as before, so nothing looks different. The only section the app lacks, `Buffer`, is not
  read anywhere.
- Parser tests pass in the scratch project. The 13 tool classes and `ZoomToFeatureControl` use Esri
  and MAUI types and were not compiled here. Each change is a one line replacement.
