
using WG.MiEditor.Configuration.SymbolConfig;
using WG.MiEditor.Shared.Configuration.AppSettings;
using WG.MiInspection.Configuration.AppSettings;

namespace WG.MiEditor.Configuration.AppSettings;

/// <summary>
/// This class & props will get map with appsettings config-ApplicationSettings section
/// </summary>

public record ApplicationSettings
{
    public required PortalResources PortalResources { get; init; }
    public required Logging Logging { get; init; }
    public required Layers Layers { get; init; }
    public required RasterInfo RasterInfo { get; init; }
    public required LandingPageContent LandingPageContent { get; init; }
    public required ErrorMessages ErrorMessages { get; init; }
    public required ApiSettings ApiSettings { get; init; }
    public required EditingAreaSymbolSettings EditingAreaSymbol { get; init; }
    public required SheepMovementBufferSettings SheepMovementBufferSettings { get; init; }
    public required OfflineDataSettings OfflineDataSettings { get; init; }
    public required IEnumerable<CanopyAreaLookup> CanopyAreaLookup { get; set; }
    public required AppVersionInfo AppVersionInfo { get; init; }
    public required MaxUAreaConfig MaxUAreaConfig { get; init; }
    public required MapViewsInfo MapViewsInfo { get; init; }

    // Not required, and defaulted, so binaries carrying this can still read an appsettings
    // file written before the edit history existed.
    public EditHistoryInfo EditHistory { get; init; } = new();
    public required double MinTaAreaInHectares { get; init; }
    public required double CoordinateZoomScale { get; init; }
    public required BufferSettings BufferSettings { get; init; }
    public required double LpisPolyIdBufferDistanceMeters { get; init; }
}
