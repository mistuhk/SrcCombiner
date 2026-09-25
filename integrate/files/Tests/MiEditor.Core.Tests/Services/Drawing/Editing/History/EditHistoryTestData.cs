using WG.MiEditor.Core.Services.Drawing.Editing.History;

namespace WG.MiEditor.Core.Tests.Services.Drawing.Editing.History;

/// <summary>
/// Builders for the history records. Named and called the same way as ArcGisTestFactory,
/// so a call site always shows that it is test data being constructed.
/// </summary>
internal static class EditHistoryTestData
{
    public const string Layer = "LPIS_Poly";
    public const string CaseKey = "CASE-1";

    public static FeatureIdentity CreateIdentity(long objectId, string? businessKey = null, string layer = Layer) =>
        new(layer, objectId, businessKey is null ? null : "POLYGONID", businessKey);

    public static FeatureSnapshot CreateSnapshot(
        long objectId,
        string? businessKey = null,
        string? geometryJson = "{\"rings\":[]}",
        DateTime? stamp = null,
        string layer = Layer,
        IReadOnlyDictionary<string, object?>? attributes = null) =>
        new(
            CreateIdentity(objectId, businessKey, layer),
            geometryJson,
            "LATESTMOD",
            stamp,
            // Same comparer the production factory uses. Attribute name casing genuinely varies
            // in this codebase, so a case sensitive fixture would not exercise what ships.
            attributes ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["POLYGONID"] = businessKey
            });

    public static EditOperationEntry CreateEntry(
        string name = "Edit Vertex",
        IReadOnlyList<FeatureChange>? changes = null,
        string caseKey = CaseKey) =>
        EditOperationEntry.Create(
            name,
            caseKey,
            changes ?? [FeatureChange.Update(CreateSnapshot(1, "100"), CreateSnapshot(1, "100"))],
            DateTime.UtcNow);

    public static EditOperationEntry CreateEntryTouching(long objectId, string? businessKey = null) =>
        CreateEntry(changes: [FeatureChange.Update(CreateSnapshot(objectId, businessKey), CreateSnapshot(objectId, businessKey))]);
}
