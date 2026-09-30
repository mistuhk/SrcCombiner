# Restore the FeatureStateReader tests, and the fixture fidelity they depended on

> **Apply this before `migrations/undo-redo/history-restructure/`.** That delta moves these files
> into new folders, and its patch is built on the state this one leaves. Applied the other way
> round, the files here land at paths the restructure has already retired.

Three test files. No production code changes, so this cannot alter behaviour.

Independent of `migrations/undo-redo/awaited-tool-standdown/`; they share no file and either can go
first.

## Why

`FeatureStateReaderTests.cs` did not survive the move into `StateManagement/`, and its assertions
exist nowhere else. `FeatureStateReader` currently has no test of its own, although its guards are all
still in place and correct.

Those twelve tests pinned the defect that took a full review round to find. The reader queries a batch
of rows with one `IN` clause, which can only name one field, and `FeatureSnapshotFactory` yields a
keyless identity whenever the identifier attribute is null, so one keyed layer produces both kinds. An
earlier version filtered the odd ones out of the clause; they came back absent, and the conflict
detector told the officer that a feature they had just created had been *deleted by someone else*.

The guards that prevent that are `RequireOneKeyField` and its position before the layer lookup.
Nothing currently stops either being removed.

## What the twelve cover

| | |
| --- | --- |
| the clause it builds | keyed on the identifier, keyless on OBJECTID, single row and batch |
| quoting | a non numeric identifier is quoted and its apostrophes doubled |
| the round-4 Major | a batch mixing keyed and keyless identities is **refused** |
| the guard's second half | a batch naming two identifier fields is refused |
| the round-4 Minor | a bad batch is refused **before the layer is looked up**, so a caller mistake is not reported as a missing layer |
| the deliberate asymmetry | `ReadManyAsync` throws on a missing layer, `ReadAsync` returns null, because its caller wants to skip rather than fail |
| nothing asked | an empty batch queries nothing |

Ported to the current vocabulary (`UniqueIdentifierField` and `UniqueIdentifierValue`, and
`HasUniqueIdentifier`) and to `Snake_Case` names, which is the house convention at 137 uses against 18.

## The fixture had drifted from production

`EditHistoryTestData.CreateSnapshot` built its attribute dictionary with the default comparer, while
`FeatureSnapshotFactory` builds both of its dictionaries with `StringComparer.OrdinalIgnoreCase`. So a
fixture snapshot did not behave like a real one, and the test that used to catch this went at the same
time as the comparer.

The comparer is restored, with the reason recorded beside it. Rather than restore the old test, which
asserted a property of the fixture, this adds one asserting the production behaviour the fixture
mirrors:

```csharp
[Fact]
public async Task A_Snapshots_Attributes_Are_Found_Whatever_The_Casing()
{
    // Attribute name casing varies across this codebase, and the applier looks values up by
    // name when writing a row back, so a case sensitive snapshot would silently drop fields.
    var table = ArcGisTestFactory.CreateTable();
    var feature = table.CreateFeature();
    feature.SetAttributeValue("Name", "Parcel A");

    var snapshot = await sut.CreateAsync(Layer, feature);

    Assert.Equal("Parcel A", snapshot.Attributes["NAME"]);
    Assert.Equal("Parcel A", snapshot.Attributes["name"]);
}
```

`FeatureChangeApplier` line 200 is the caller that makes this matter: it decides whether to write a
field back with `existing.Attributes.ContainsKey(pair.Key)`.

## Build and test state

```
123 passed, 0 failed, 0 warnings
```

The harness now compiles the real reorganised code, so the 108 tests already in the repository run
here too, and **`FeatureChangeApplier` is compiled for the first time**. The two guards are confirmed
load bearing by reverting them:

| Mutation | Result |
| --- | --- |
| move the batch guard back below the layer lookup | 1 failed |
| drop the identifier-field half of the guard | 1 failed |

## A compile error this shipped with, and why the harness missed it

The first version of this file imported the whole `Esri.ArcGISRuntime.Mapping` namespace for
`FeatureLayer`. That namespace also declares `Layer`, which collides with the `Layer` constant the file
takes from `EditHistoryTestData` through `using static`, giving 26 instances of:

```
error CS0229: Ambiguity between 'EditHistoryTestData.Layer' and 'Layer'
```

This is the only one of the six test files in this folder that imports that namespace, which is why no
sibling has the problem. Fixed by aliasing the single type instead, matching the `Map` alias already
used in `LpisAttributionControlViewModelTests`:

```csharp
using FeatureLayer = Esri.ArcGISRuntime.Mapping.FeatureLayer;
```

The harness did not catch it because its stub for that namespace declared only `FeatureLayer`, so the
ambiguity could not arise. The stub now declares `Layer` as well, and reverting the alias reproduces
all 26 errors. That is the third time a stub more permissive than the real type has hidden a real
failure, after `EditResult` being constructible and `FeatureCollectionTable` accepting an OBJECTID
field. The pattern is always the same: whatever the stub omits is the thing that breaks.

## How to apply

Either copy the three files from `files/`, or:

```
git apply migrations/undo-redo/state-reader-tests/state-reader-tests.patch
```

Verified: applies cleanly to the files as they stand in the repository, and reproduces `files/` byte
for byte. Nothing is deleted or moved, and no `.csproj` needs touching.
