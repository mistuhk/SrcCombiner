# Fixes for the two failing test classes

Three files. Copy them over the same paths.

## 1. EditHistoryServiceTests: no response was received

`EditHistoryService` awaits the reply:

```csharp
await messenger.Send<ResetToolbarState>();
```

so any test that runs an undo needs something to reply, or that line throws. Every such test now
registers one:

```csharp
private void ReplyToStandDown() =>
    messenger.Register<ResetToolbarState>(this, (_, m) => m.Reply(Task.FromResult(true)));
```

Nine tests call it. Four register their own inline, because they also need to observe that the message
was sent:

```csharp
messenger.Register<ResetToolbarState>(this, (_, m) => { asked = true; m.Reply(Task.FromResult(true)); });
```

Two things to know about the shape of this:

- `Redo_Also_Asks_The_Editing_Toolbar_To_Drop_Its_Active_Tool` registers **before** its first undo, not
  after. It undoes in order to have something to redo, and that undo awaits a reply as well.
- `Undo_Proceeds_When_Nothing_Answers_The_Toolbar_Request` is **deleted**. It asserted that an undo
  survives having no recipient, which is no longer true with the one line form.

Tests that return before the send need no reply: the history being switched off, nothing recorded, and
an entry belonging to another case.

## 2. FeatureStateReaderTests: cannot create a feature layer from a feature collection table

The fixture built the layer from a `FeatureCollectionTable`, which the runtime rejects. It now uses the
pattern already in `CaseManagementServiceTests`:

```csharp
var layer = new FeatureLayer(new ServiceFeatureTable(new Uri("https://test-fs-url.com"))) { Name = Layer };
```

A `ServiceFeatureTable` built from a URI constructs without touching the network, and nothing in these
tests uses the layer for anything: it is handed straight back to the mock that returned it.

## State

```
140 passed, 0 failed, 0 warnings
```

One down from 141, being the deleted test.

## Why these got through

Both are the same failure on my side: the stubs my harness uses were more permissive than the real
ArcGIS and CommunityToolkit types, so code that cannot work compiled and passed. The stubs now mirror
the real behaviour, which is why these two are caught here rather than by you:

- `ServiceFeatureTable` now has the `Uri` constructor, and a `FeatureLayer` can no longer be built from
  a `FeatureCollectionTable`.
- The `AsyncRequestMessage` path is the real CommunityToolkit package, which is why the reply
  requirement shows up at all once the guard is removed.
