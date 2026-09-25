# Undo/redo: the toolbar

**Apply after `migrations/undo-redo/journal` and its `pr-review-fix`.** Six new files, eleven
modified.

Second delivery of the undo/redo workstream. After this the Undo and Redo buttons exist on
the standard toolbar, the depth and size budget come from configuration, and the history is
forgotten when the case changes. The buttons stay disabled, because nothing records yet.

## What is deliberately not here

Reversing an operation needs the conflict check and the applier, which arrive with the
recording work. `EditHistoryService.UndoAsync` and `RedoAsync` guard and return rather than
pretending to act. That is not a stub for its own sake: moving an entry to the redo list
without reversing anything would leave the journal describing a state the data is not in,
which is worse than doing nothing.

Nothing records, so the journal is always empty, `CanUndo` is always false and the buttons
never enable. That is the expected and correct state at this point, not a fault.

## Previous View and Next View are untouched

Worth saying plainly, because an earlier draft of the plan proposed repurposing them. They
keep their icons, their tooltips, their order and their behaviour. `MapViewsHistoryService`
and the `MapViewNavigation*` messages are not touched. The two histories are separate and
each drives its own pair of buttons through its own message.

## The pieces

### `EditHistoryService`, new

What the buttons talk to. Two things it does completely:

- **Availability.** It subscribes to `IEditHistoryJournal.Changed` and republishes it as
  `EditHistoryChangedMessage(CanUndo, CanRedo)`, which the toolbar receives.
- **Forgetting.** It clears the journal on `SelectedCaseMessage`,
  `SelectedCaseStartEditingMessage`, `SelectedCaseStopEditingMessage` and `ExitCaseMessage`.
  Clearing on those is the primary guard; the case key stamped on every entry is the second,
  for a message that never arrives.

Registered `Scoped`, matching the services the applier and the conflict check will need. No
scope is ever created in this application, so it is a singleton in practice, which is what
the journal subscription relies on. `ToolbarControlViewModel` depends on it, so it is
constructed at startup and the subscription exists from then on.

### `UndoToolDescriptor` and `RedoToolDescriptor`, new

Order 6 and 7, following the view history buttons. `ToolActivation.Action`, so they fire
once and never hold an active state. They self register like the other six, so no XAML and
no registration code changes: the toolbar renders whatever descriptors the scanner finds.

### `StandardTool`

Gains `Undo` and `Redo`, and both are added to `All`.

### `EditHistoryInfo` reaches the journal

`ApplicationSettings` gains `EditHistory`, **not required and defaulted**, so binaries
carrying this change can still read an appsettings file written before the edit history
existed. `MauiProgram` exposes it for injection the same way `Layers` and `Logging` are
exposed, which is what lets the journal take its budget from configuration rather than from
the defaults it falls back to.

The block added to all five appsettings files:

```json
"EditHistory": {
  "MaxUndoDepth": 10,
  "MaxTotalBytes": 67108864
}
```

### `ToolbarControlViewModel`

Takes `IEditHistoryService`, starts both new buttons disabled alongside the view history
pair, dispatches them to `UndoAsync` and `RedoAsync`, and receives
`EditHistoryChangedMessage` to drive their enablement. `OnActivate` becomes `async` as a
result.

The new buttons are added to the existing exclusion that stops the view history buttons
broadcasting `StandardToolsInUseMessage`, for the same reason: pressing them must not
disturb the active Info mode or the AOI control.

## The icons are placeholders

There is no undo or redo artwork in this repository. The two descriptors borrow the view
history arrows, `previous_extent.png` and `next_extent.png`, which is what was agreed rather
than referencing files that do not exist and rendering blank buttons.

**The toolbar will therefore show four near identical arrows**, at order 4, 5, 6 and 7,
distinguished only by tooltip. That is not acceptable for release and is worth deciding
about early. Replacing them is one line in each descriptor and nothing else changes.

## Changes by file

| File | Change |
| --- | --- |
| `Core/.../History/IEditHistoryService.cs` | New. The contract the toolbar depends on. |
| `Core/.../History/EditHistoryService.cs` | New. Availability, clearing, and guarded undo and redo. |
| `MainApp/.../StandardTools/UndoToolDescriptor.cs` | New. Order 6, action, placeholder icon. |
| `MainApp/.../StandardTools/RedoToolDescriptor.cs` | New. Order 7, action, placeholder icon. |
| `Shared/Models/Toolbar/StandardTool.cs` | `Undo` and `Redo` tokens, added to `All`. |
| `Shared/Configuration/AppSettings/ApplicationSettings.cs` | `EditHistory`, not required, defaulted. |
| `MainApp/MauiProgram.cs` | One line exposing `EditHistory` for injection. |
| `MainApp/AutoScanning.cs` | `ToolbarControlViewModel` gains the service, plus the using. |
| `MainApp/ViewModels/Toolbar/ToolbarControlViewModel.cs` | Dispatch, enablement, and the new recipient. |
| `MainApp/appsettings*.json` (five files) | The `EditHistory` block. |
| `Tests/.../History/EditHistoryServiceTests.cs` | New, eight tests. |
| `Tests/.../Toolbar/UndoRedoToolDescriptorTests.cs` | New, nine tests. |
| `Tests/.../Toolbar/ToolbarControlViewModelTests.cs` | Additive. The two descriptors added to the registry, the expected tool order extended, and six new tests. |

## Build

Not built. The projects target net8.0-windows and must be built on Windows.

Verified in a standalone harness. This delivery reaches further than the journal did,
because the whole toolbar chain turned out to carry no MAUI or Esri dependency:
`ToolbarControlViewModel`, `IconButtonControlViewModel`, `ToolActivationController`,
`ToolActivationCoordinator`, `ToolDescriptorRegistry` and all eight descriptors were
compiled as they are, against this repository's real `StandardTool`, `IToolDescriptor`,
`RegisterServiceAttribute`, `Layers`, `EditHistoryInfo` and real message declarations.

```
Passed!  - Failed: 0, Passed: 88, Skipped: 0, Total: 88
```

Up from 49. The 39 new tests cover the service and the toolbar wiring.

**What the harness did stand in for.** Four messages in
`Shared/Messages/Map` are declared in files that also declare messages carrying Esri
geometry types, which the harness cannot reference. `ZoomMapMessage`, `ZoomDirection`,
`ResetMapMessage` and `RefreshMessage` were re-declared with their exact shapes copied from
those files. `CaseModel` and `QaCaseCompletionOpType` were stubbed as bare types, since only
their identity matters to the messages under test. Everything else is the real thing.

Still to do on Windows: build `WG.MiEditor.sln` and run both test projects.

**Then in the running application:**

1. The standard toolbar shows two new buttons after Previous and Next View, with Undo and
   Redo tooltips. They are greyed out.
2. They stay greyed out through a full editing session. Nothing records yet, so this is
   correct.
3. Previous View and Next View still navigate the map extent, and still enable and disable
   as the view history changes. This is the main regression risk, since both pairs are now
   driven by messages into the same view model.
4. Clicking a greyed out button does nothing and throws nothing.
5. No startup binding or configuration error from the new `EditHistory` section. Worth
   checking against an older appsettings file too, by removing the section, since the
   property is defaulted precisely so that still binds.

## Next

`migrations/undo-redo/recording` adds the Esri edge, the conflict check and the applier, and
wires recording into one operation so undo genuinely works end to end for it.
