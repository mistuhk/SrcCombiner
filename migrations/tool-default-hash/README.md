# tool-default-hash

A default tool value no longer throws from `GetHashCode()`.

Independent of the other packages; listed last.

## The fault

"No tool selected" is a default struct, for example `AoiControl` resets with
`ActiveTool = new()`. Its `Name` is null, and `GetHashCode()` called `Name.GetHashCode()`, so using
one as a dictionary key or set member threw `NullReferenceException`. `ToString()` also returned
null. Nothing hashes a default tool today, so the fault was latent.

All four tool structs had the same code.

## Changes

| File | Change |
| --- | --- |
| `AoiTool.cs`, `EditingTool.cs`, `MeasureTool.cs`, `StandardTool.cs` in `Src/MiEditor.Shared/Models/Toolbar/` | `GetHashCode() => Name?.GetHashCode() ?? 0`; `ToString() => Name ?? string.Empty` |
| `Tests/MiEditor.MainApp.Tests/ViewModels/Toolbar/ToolDefaultValueTests.cs` | New, 4 tests |

Named tools hash and print as before. Equality was already null safe. No code depended on
`ToString()` returning null.
