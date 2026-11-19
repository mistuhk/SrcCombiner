# measure-tools

Makes the Distance, Area and Sheep Movement Buffer measure tools resilient in day to day use, and
replaces the `MeasureOperationBase` inheritance hierarchy with small composed parts.

Apply first. Later packages build on these files.

## Faults fixed

| # | Fault | Fix |
| --- | --- | --- |
| 1 | Pan and zoom could stay disabled after a measurement, for example when the tool was switched mid sketch | `MapInteractionLock` captures and restores the map's own options, and every teardown path releases it. Pan and zoom are locked only while a sketch has at least one vertex, so the user can pan between measurements |
| 2 | The first tap after a finished measurement was lost, because clearing the old result also cleared the new vertex | Only the result graphic is cleared; the editor geometry is left alone |
| 3 | Zoom to feature removed the measurement overlay | The context provider re-attaches its overlay (id `measurementOverlay`) on every activation |
| 4 | Activation failures were swallowed silently, leaving a button lit with no tool running | `ToolActivationController` rolls back and reports through an optional `onError` sink; the measurement toolbar reports via `IMeasurementErrorReporter` |
| 5 | `NullReferenceException` when a tool was used before the map was ready | `MapViewMeasurementContextProvider` waits up to 10 s, then raises `MeasurementUnavailableException` with a user facing message |
| 6 | AOI drawing and the measure tools stopped each other's sessions on the shared `GeometryEditor` | Each tool assigns its own `VertexTool` and only touches the editor while it owns the session. AOI stops on `StopAoiInteractionMessage` and `MeasurementInProgressMessage`. AOI Delete deactivates other tools first |
| 7 | Sheep buffer could draw with radius 0, and a tap it used also reached other handlers | Radius starts at the configured minimum, invalid input re-sends the last valid radius, and the buffer tap is marked handled |
| 8 | Overlapping activations could interleave | `MeasureOperationManager` serialises activation with a semaphore |
| 9 | One failing teardown step skipped the rest | `CleanupSteps.RunAll` runs every step and rethrows afterwards |
| 10 | Radius parsing depended on culture ("500.5" became 5005 on comma decimal devices), and Enter then unfocus showed two errors | `RadiusInput` tries the device culture, then invariant, then thousands separators; repeat validation of the same text is skipped |
| 11 | A typo in a measurement symbol setting crashed the toolbar | `MeasurementSymbols` falls back to defaults with a warning |
| 12 | Readings were suppressed at zero, so undoing vertices left a stale value | Readings are always sent; undo resets to zero; the final result stays until the next sketch starts |
| 13 | Lifetimes were declared `Scoped` but resolved from root | Manager, operations, context provider and error reporter are `Singleton` |
| 14 | String switches for units; an unused converter | `MeasurementUnit` record; `MeasureToolToIconConverter` deleted |

## Design

`MeasureOperationBase` is gone. The two sketch tools compose:

- `SketchMeasurement`: the editor session, completion on double tap, and live readings
- `MapInteractionLock`: pan and zoom lock and restore
- `MeasurementResultLayer`: the single result graphic on the shared overlay
- `MeasurementGeometry`: vertex checks and geodesic measuring
- `MeasurementSymbols`: result symbols from configuration, with defaults

`IMeasureOperation` is now `Start(MeasurementContext)` and `Stop()`. `Start` leaves nothing attached
if it throws, and `Stop` is idempotent. The sheep buffer uses the same pieces without the editor.

## Decisions

- Pan and zoom lock only while a sketch has vertices. The user asked that panning between
  measurements stays possible.
- A double tap with no sketch in progress is left to the map, so it still zooms.
- Ownership of the shared editor is decided by reference equality on the tool's own `VertexTool`.

## Files

Deleted. `files/` cannot show deletions, so remove these by hand if you copy files instead of
applying the patch:

- `Src/MiEditor.Core/Services/Drawing/Measurement/Operation/MeasureOperationBase.cs`
- `Src/MiEditor.MainApp/Converters/MeasureToolToIconConverter.cs`

`GlobalSuppressions.cs` loses the 12 CA1416 entries for the deleted class. Seven older entries
under the `Sketching.Measurement` namespace were already stale and are left alone.

Added in Core: `Measurement/Components/*` (7 files) and `Measurement/Context/*` (4 files).
Added in MainApp: `MeasurementUnit.cs` and `RadiusInput.cs`. Also changed: the three operations,
`IMeasureOperation`, the manager, the activation controller and coordinator, the measurement
toolbar and live measurement view models, both AOI view models, `DefineAoiMessages` and
`AutoScanning`.

Tests: 11 new test classes and 4 updated ones under `Tests/`.

## Verification

- 107 tests passed in a scratch `net10.0` project on macOS. It compiles the platform independent
  sources against stubs for the Esri and MAUI types.
- Not compiled here: `SketchMeasurement`, the three operations, `AoiControlViewModel`, and the Esri
  dependent tests (`MapInteractionLockTests`, `MeasurementGeometryTests`,
  `MapViewMeasurementContextProviderTests`, symbol construction). These need the Windows build.
- Every Esri API used is already used elsewhere in the app or was used by `MeasureOperationBase`.

Manual checks on Windows:

1. Start a sketch, switch tool mid sketch: pan and zoom work afterwards.
2. Finish a measurement and tap straight away: the tap places the first vertex of the next sketch.
3. Zoom to a feature, then measure: the result is visible.
4. Undo every vertex: the panel shows 0.
5. Type `abc` in the radius box and press Enter: one error, and the box reverts.
6. Draw an AOI, then start a measurement: the AOI drawing stops and the measurement works.
7. Double tap the map with a measure tool active but no sketch: the map zooms.
