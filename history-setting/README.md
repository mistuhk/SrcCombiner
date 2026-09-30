# A History setting that turns undo and redo on, off, and away entirely

18 files, two of them new. Apply after `state-reader-tests`, `awaited-tool-standdown` and
`history-restructure`.

`migrations/undo-redo/editing-settings-rename/` is a separate rename that goes after this one.

## Two answers, and the deployment's wins

```csharp
public bool IsEnabled => IsAvailable && preferences.HistoryEnabled;

public bool IsAvailable => settings.Available;
```

`EditHistoryInfo.Available` says whether this deployment offers the feature. `HistoryEnabled` says
whether this user wants it. Config takes precedence, because a switch a user could override would not
be a switch, and the reason to have one is to withdraw the feature estate wide without shipping new
binaries: during a rollout, or if an audit objects to undo posting a visible compensating edit.

Both are read through one type, `IEditHistorySwitch`, so the precedence lives in a single testable
place rather than being repeated in the recorder, the service and the toolbar.

**The user's stored choice is read and never written.** Withdrawing the feature does not erase what
somebody had chosen, so their choice returns if it is offered again. There is a test asserting the
setter is never called.

**With the feature withdrawn, the History section is not shown at all** rather than shown with a switch
that does nothing. A toggle that flips and changes nothing is the worst of the three options: it looks
broken, and it produces the support call the switch was meant to prevent. That is what `IsAvailable`
exists for.

## Defaults

| | Default | Why |
| --- | --- | --- |
| `EditHistoryInfo.Available` | `true` | An appsettings file written before this existed must leave the feature on offer. Defaulting it false would make new binaries against an older config disable the feature with no way to switch it on: an accidental kill switch rather than a deliberate one. |
| `HistoryEnabled` | `false` | Requested. The feature ships dark and an officer opts in. |

Together those mean `Available` only ever needs editing to withdraw the option, and the feature is off
until somebody asks for it. It is written explicitly into all five `appsettings*.json` anyway, so the
switch is discoverable rather than hidden behind a default.

## Off means off, not merely hidden

**Nothing is recorded.** `Begin` hands back a scope that discards everything, so no snapshot is taken
and no row is read back from the service:

```csharp
public IEditOperationScope Begin(string operationName) =>
    historySwitch.IsEnabled
        ? new EditOperationScope(journal, snapshotFactory, stateReader, caseContext, feedback, loggerService, operationName)
        : DiscardingScope.Instance;
```

The three recording lines at every call site stay exactly as they are and cost nothing. Paying nothing
for a feature that is switched off is most of the point of a switch.

**Switching it off discards what was recorded.** Keeping it would offer, on a later switch back on, an
undo of operations the officer may have built on since, with only the conflict check between them and a
surprise. `EditHistoryService` already owns clearing for the case lifecycle, so this joins it.

**An undo is refused, not merely hidden.** `CanUndo`, `CanRedo` and `RunAsync` all consult the switch.
Undo and redo have no keyboard shortcut today; when one is added it must not leave the feature live
while it looks switched off, and this closes that ahead of time.

**Switching it on does nothing but start recording.** History begins empty, which follows from the
above, and nothing is cleared.

## Where it appears

The History card is the first section inside the existing editing block, so it sits between Theme and
Snapping and inherits "during an edit session only" from the container it is in. No new gating logic.
Its own `IsVisible` adds the withdrawn case on top.

The Undo and Redo buttons are hidden, not disabled, and follow the setting mid session rather than being
decided once at startup:

```csharp
historySwitch.Changed += (_, _) => ApplyHistoryVisibility();
```

`IconButtonControlViewModel` already had `IsVisible` beside `IsEnabled`, so nothing new was needed there.

## Build and test state

```
141 passed, 0 failed, 0 warnings
```

Up from 123, with 13 new tests: eleven on the switch, two on the recorder recording nothing, and four on
the service refusing and clearing. Six mutations, each turning the suite red:

| Mutation | Result |
| --- | --- |
| let the user override the deployment switch | 2 failed |
| report a change when the answer did not move | 1 failed |
| record regardless of the switch | 2 failed |
| do not clear the history when switched off | 1 failed |
| drop the refusal, trusting the hidden buttons | 1 failed |

## What is not compiled here

Five of the eighteen files are MainApp or config and cannot be built on this machine:
`AutoScanning.cs`, `EditingLayerToolbarViewModel.cs`, `SettingsSidebarControlViewModel.cs`,
`SettingsSidebarControl.xaml` and the five `appsettings*.json`.

Checked by hand instead:

- **Every `StaticResource` the new XAML card names exists.** I first wrote `FontSizeCaption`, which does
  not exist and would have thrown at runtime rather than at build; the only font size resources in this
  repository are `FontSizeStandard` and `FontSizeSubHeader`, so the caption uses a literal 12, matching
  the explanatory label in the Theme section.
- **Every binding the card names exists on the view model**: `IsHistoryAvailable`, `ToggleHistoryCommand`,
  `IsHistoryExpanded`, `IsHistoryEnabled`.
- **All five JSON files parse** after the edit, asserted before writing.
- **`EditingLayerToolbarViewModel` is hand registered** in `AutoScanning`, so its new constructor
  argument is added there too. `SettingsSidebarControlViewModel` carries `[RegisterService]` and is
  resolved by convention, so it needed no registration change.

## How to apply

```
git apply migrations/undo-redo/history-setting/history-setting.patch
```

Verified: applies cleanly to the state the three preceding deltas leave, and reproduces `files/` byte for
byte. Nothing is deleted or moved, and no `.csproj` needs touching.

## What to check on Windows

1. With no case being edited, the settings panel shows Theme only. No History section.
2. Start editing. History appears between Theme and Snapping, switched off, and the editing toolbar shows
   no Undo or Redo buttons.
3. Turn History on. Both buttons appear, disabled until something is recorded.
4. Draw a Canopy Point, undo it, redo it. As before.
5. With something undoable recorded, turn History off. The buttons disappear; turn it back on and the
   buttons are there but disabled, because the history was discarded.
6. Restart the app. The setting is remembered, per user.
7. Set `"Available": false` in `appsettings.json` and restart. No History section at all even while
   editing, no buttons, and one line in the log saying the feature is withdrawn. Then confirm that a user
   who had turned it on gets it back when `Available` returns to true.
