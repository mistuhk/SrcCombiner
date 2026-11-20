using Esri.ArcGISRuntime.Maui;
using Esri.ArcGISRuntime.UI;

namespace WG.MiEditor.Core.Services.Drawing.Measurement.Context;

/// <summary>
/// What a measure operation needs for one session: the map to listen to and the overlay to
/// draw results on. A fresh context is handed to every <c>Start</c>, so an operation never
/// holds on to a map or overlay between sessions.
/// </summary>
public sealed record MeasurementContext(MapView MapView, GraphicsOverlay Overlay);
