# singleton-lifetimes

Registers `MapViewService` and `DrawingSymbolColourParserService` as `Singleton` instead of
`Scoped`.

Apply after `measure-tools`.

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
