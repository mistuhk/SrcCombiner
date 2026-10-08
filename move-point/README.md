# Undo and redo for Move Point

Three files. Apply after everything in the checkpoint.

## Why this is smaller than Create Point was

Move routes to one strategy only: `MoveFeaturePersistenceStrategyFactory` throws for any layer but
Canopy Point. And `UpdateMaxUseAreaFromFeatureAsync` skips it, by its own `else` branch:

```csharp
else
{
    // Unsupported layer
    loggerService.Info($"MaxUseArea update not supported for layer: {layerName}");
    return true; // Not an error, just not applicable
}
```

So a move is a single `Update` to a single row. No second row, no recomputed `MAXUAREA` to reverse,
no object id remapping, because an update keeps the id it had.

## Where the recording had to go, and why not where Create Point's went

Create Point records entirely inside `AttributionModelService`. Move cannot, because the geometry is
already changed by the time the persistence code sees the feature:

```csharp
activeFeature.Geometry = e.Location;   // MovePointOperation

await CommitAsync();                   // then validate, persist, ApplyEdits
```

Capturing in `CanopyPointPersistenceStrategy` would snapshot the new position and the undo would put
the point back where it already is. So the capture sits in `MovePointOperation`, immediately before
that assignment, which is the last moment the original position exists:

```csharp
editScope = editRecorder.Begin($"Move {layerName}");
await editScope.CaptureUpdateBeforeAsync(layerName!, activeFeature);

activeFeature.Geometry = e.Location;

await CommitAsync();
```

The scope is a field because the capture and the commit are in different methods. That is safe here:
`CommitAsync` has exactly one caller, and its `finally` disposes the scope on every exit, so a
validation failure, a cancelled case request or an exception records nothing.

The commit goes last inside the success branch, after the notification and `Clear()`, so that a
problem writing the history cannot make a move that did go through look as though it failed. Last is
necessary but not sufficient: the commit reads the row back through the service, so it can throw, and
it needs its own catch or the exception reaches the outer one and reports the move as failed. See
below.

## The swallowed ApplyEdits error, fixed

`CanopyPointPersistenceStrategy` logged every result and returned `true` regardless:

```csharp
var editResults = await serviceFeatureTable.ApplyEditsAsync();

foreach (var result in editResults)
{
    loggerService.Info($"Apply Edits Result: Completed={result.CompletedWithErrors}, Error={result.Error?.Message}");
}
```

That mattered little while nothing was watching. Now the history records the move, and a rejected one
would leave an undo offering to put the point back somewhere it never left. It now follows
`DeleteFeatureService.ApplyEditsOrThrow`, which is the pattern already in the codebase.

This is one of the six swallowing sites the original design flagged as a prerequisite. The other five
are untouched.

**The throw also needed somewhere to be reported.** It lands in `CommitAsync`'s catch, which only
logged, and every other notification in that file is for validation, selection or success. So the
first version of this change traded a false success message for silence: the officer tapped, the
graphic was cleared as though it had worked, and nothing was said. The catch now tells them:

```csharp
notificationManager.Show(
    MovePoint,
    "The canopy point could not be moved. Please try again.",
    NotificationType.Error);
```

The message is generic because that catch covers the whole commit, not only the send.

## The success message and the failure message could both appear

Found reviewing this change a second time, and it falsified the comment written on the very line that
caused it. `editScope.CommitAsync` reads the row back through `IFeatureStateReader`, which calls
`GetFeatureLayerAsync` and `GetArcGISFeatureAsync`, so a network fault there throws, reaches
`CommitAsync`'s catch, and the officer sees:

```
"Canopy point feature moved successfully."     then
"The canopy point could not be moved. Please try again."
```

The second is the wrong one. The move was saved. `Clear()` would also run twice, which is harmless,
but the contradictory pair is not. The scope commit now has its own catch:

```csharp
try
{
    await editScope!.CommitAsync();
}
catch (Exception historyEx)
{
    loggerService.Error(historyEx, "Move point feature was saved but could not be recorded in the edit history.");

    notificationManager.Show(
        MovePoint,
        "The canopy point was moved, but this move cannot be undone.",
        NotificationType.Warning);
}
```

A warning rather than silence, because the officer's next action may depend on it: they would
otherwise reach for an undo that is not there. The wording confirms the move and names exactly what
is missing, so it cannot be read as the move having failed.

## Tests

Four, on the recorder's update path, which **had no positive coverage at all**: every other test in
that file is about an insert. The one that matters most:

```csharp
[Fact]
public async Task The_Before_State_Is_Fixed_When_It_Is_Captured_Not_When_It_Is_Committed()
{
    // The guarantee a move depends on completely. The point moves between the capture and the
    // commit, and the persistence code is handed the feature only after it has moved, so a before
    // state taken at commit time would be the new position and the undo would do nothing.
    SnapshotTheFeatureAsItStands();

    using var scope = sut.Begin("Move Canopy Point");
    var feature = FeatureAt(Original);

    await scope.CaptureUpdateBeforeAsync(Child, feature);

    feature.Geometry = Geometry.FromJson(Moved);
    feature.SetAttributeValue("MODIFIEDDT", DateTime.UtcNow);

    await scope.CommitAsync();

    Assert.Equal(Original, TheOnlyRecordedChange().Before!.GeometryJson);
}
```

That needed the snapshot factory mock to answer from the feature as it stands when asked, rather than
a fixed value, which is the only way a test can tell *when* the before state was taken.

The other three: an update records the captured before and the stored after; an update whose row
cannot be read back is not recorded; an update reads back the row it captured.

```
144 passed, 0 failed, 0 warnings
```

Up from 140. Two mutations, each turning the suite red:

| Mutation | Result |
| --- | --- |
| take the before state at commit time instead of capture time | 2 failed |
| reuse the captured state as the after state instead of reading the row back | 1 failed |

## What is not verified

`MovePointOperation` uses `Esri.ArcGISRuntime.Maui` with 31 `mapView` references and has no tests of
any kind, so it cannot go in the harness. Its change is four statements and a field, deliberately
with no logic in them, so that what cannot be tested is also what cannot be wrong in an interesting
way.

Two things in it were decided by reading rather than running, and both are worth knowing:

- `await editScope!.CommitAsync()` asserts rather than checks. `CommitAsync` is not on
  `IMoveFeatureOperation`, and its only caller opens the scope two statements earlier, so the null
  cannot happen. An earlier version checked for it, which implied a path that does not exist.
- The capture is not wrapped, because it cannot realistically throw: the first tap loads the feature,
  so `FeatureSnapshotFactory.CreateAsync` makes no service call on the second and only copies
  attributes and serialises the geometry.

A known fragility it does not fix: `MapView_GeoViewTapped` is `async void`, and `activeFeature`,
`featureSelected` and now `editScope` are fields read across awaits. A second tap during a save can
enter the select branch and reassign `activeFeature`, which the in flight `CommitAsync` then
persists. That predates this change and is the larger half of the problem; the scope only joins it.
A second tap cannot re enter the move branch, but no longer for the reason it used to. The branch used
to run unbroken to `CommitAsync`, whose `StopGeometryEditor` is synchronous and precedes every await.
The capture now sits in that region. It is still single entry because `FeatureSnapshotFactory` awaits
only `LoadAsync`, skips it for a feature already loaded, and so completes synchronously without the UI
thread pumping, and the first tap forces the load. Two methods away, untested, and worth knowing
before anything changes either half.

`CanopyPointPersistenceStrategy` is likewise out of reach: `MapAttributesAsync` needs a live
`MapView`. Its change is copied from `DeleteFeatureService.ApplyEditsOrThrow`.

Both are in `MiEditor.Core`, so a `dotnet build` will at least prove they compile.

The new catch around the scope commit is in that same unreachable method, so it is reasoned about
rather than tested. The 144 tests in the harness still pass, but none of them touch
`MovePointOperation`.

## How to apply

```
git apply migrations/undo-redo/move-point/move-point.patch
```

Verified: applies cleanly to the current state and reproduces `files/` byte for byte. Nothing deleted
or moved, no `.csproj` change.

## What to check on Windows

1. Move a canopy point. Undo. It returns to where it started, and the attributes go with it, not just
   the position.
2. Redo. It moves back to the new position.
3. Undo, redo, undo, so the entry survives a round trip.
4. Move a point, then move it again, then undo twice. Both moves come back in order.
5. Move a point with the History setting off. Nothing is recorded and the undo button stays hidden.
6. Make a move fail at the service, if you can force it. The officer should see "The canopy point
   could not be moved", not a success message and not silence, and nothing should be recorded.
7. Make the move succeed but the history write fail, if you can force it, by dropping the network
   between the save and the read back. The officer should see the success message followed by "this
   move cannot be undone", never "could not be moved", and the undo button should not offer the
   move.
