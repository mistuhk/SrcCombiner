using WG.MiEditor.Configuration.AppSettings;
using WG.MiEditor.Shared.AutoScanning;

namespace WG.MiEditor.Core.Services.Drawing.Editing.History;

[RegisterService(ServiceLifetime.Singleton, As = new[] { typeof(IFeatureIdentityPolicy) })]
public sealed class FeatureIdentityPolicy(Layers layers) : IFeatureIdentityPolicy
{
    private const string PolygonId = "POLYGONID";
    private const string LatestMod = "LATESTMOD";
    private const string ModifiedDate = "MODIFIEDDT";

    /// <summary>
    /// Only the LPIS parcel layer has a single attribute that both identifies one row and
    /// survives a delete and re-insert. The child layers carry POLYGONID or LPISPOLYID, but
    /// those reference the parent parcel and repeat across many rows, so they cannot identify
    /// a row on their own. Those layers return null and rely on object id remapping instead.
    /// </summary>
    public string? BusinessKeyField(string layerName) =>
        Matches(layerName, layers.LPIS_Poly) ? PolygonId : null;

    public string StampField(string layerName) =>
        Matches(layerName, layers.LPIS_Poly) ? LatestMod : ModifiedDate;

    private static bool Matches(string layerName, string? configured) =>
        !string.IsNullOrWhiteSpace(configured)
        && string.Equals(layerName, configured, StringComparison.OrdinalIgnoreCase);
}
