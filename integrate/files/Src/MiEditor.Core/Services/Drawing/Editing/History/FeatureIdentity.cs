namespace WG.MiEditor.Core.Services.Drawing.Editing.History;

/// <summary>
/// Identifies a single row for the edit history. The business key is preferred over the
/// object id because a feature re-created by an inverse insert is assigned a new object
/// id by the service, whereas the business key is carried across on the attribute copy.
/// </summary>
public sealed record FeatureIdentity(
    string LayerName,
    long ObjectId,
    string? BusinessKeyField = null,
    string? BusinessKeyValue = null)
{
    public bool HasBusinessKey =>
        !string.IsNullOrWhiteSpace(BusinessKeyField) && !string.IsNullOrWhiteSpace(BusinessKeyValue);

    /// <summary>
    /// Two identities refer to the same row when their business keys match, falling back to
    /// the object id when either side has no business key.
    /// </summary>
    public bool SameRowAs(FeatureIdentity other)
    {
        if (!string.Equals(LayerName, other.LayerName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (HasBusinessKey && other.HasBusinessKey)
        {
            return string.Equals(BusinessKeyField, other.BusinessKeyField, StringComparison.OrdinalIgnoreCase)
                && string.Equals(BusinessKeyValue, other.BusinessKeyValue, StringComparison.OrdinalIgnoreCase);
        }

        return ObjectId == other.ObjectId;
    }
}
