# singleton-lifetimes

Registers `MapViewService` and `DrawingSymbolColourParserService` as `Singleton` instead of
`Scoped`.

## Apply order

Step 2 of 5. Previous: `measure-tools`. Next: `symbol-config-safety`.

Needs first: `measure-tools`. Both change `DrawingSymbolColourParserService.cs`.

Full order:

1. `measure-tools`
2. `singleton-lifetimes` (this package)
3. `symbol-config-safety`
4. `sketch-point-style`
5. `tool-default-hash`

From the repository root:

    git apply migrations/singleton-lifetimes/singleton-lifetimes.patch

If it fails on a Windows checkout, retry with `--ignore-whitespace` (the patches use LF line
endings). See `../CHECKPOINT.md` for the full steps and what to do if a patch will not apply.

## Why

The measurement services in `measure-tools` are singletons and depend on both of these. The app
resolves everything from the root provider and creates no scopes, so each `Scoped` service was
already one shared instance. The change makes the registration match that behaviour. It also
means enabling `ValidateScopes` will not fail on a singleton depending on a scoped service.

`MapViewService` hands the map from the page to every service that awaits it. As a singleton, the
page that registers the map and the services that wait for it are guaranteed the same instance.

## Checks made

- Neither class has constructor dependencies, so nothing short lived is captured.
- Nothing in the solution calls `CreateScope`, `CreateAsyncScope` or `IServiceScopeFactory`.
- Other `Scoped` consumers, such as `FeatureHighlightService` and `MapViewsHistoryService`, still
  work: a scoped service may depend on a singleton.

## Changes

| File | Change |
| --- | --- |
| `Src/MiEditor.Core/Services/MapData/MapViewService.cs` | `ServiceLifetime.Scoped` to `Singleton` |
| `Src/MiEditor.Core/Services/Drawing/DrawingSymbolColourParserService.cs` | `ServiceLifetime.Scoped` to `Singleton` |

No behaviour change is expected at runtime.
