namespace WG.MiEditor.Core.Services.Drawing.Editing.History;

/// <summary>
/// Resolves the per layer fields the history depends on. Both differ by layer: LPIS parcels
/// carry a POLYGONID business key and stamp LATESTMOD, while the other editable layers have
/// no business key of their own and stamp MODIFIEDDT.
/// </summary>
public interface IFeatureIdentityPolicy
{
    /// <summary>The attribute that survives a delete and re-insert, or null when there is none.</summary>
    string? BusinessKeyField(string layerName);

    /// <summary>The attribute written on every edit, used to detect a third party change.</summary>
    string StampField(string layerName);
}
