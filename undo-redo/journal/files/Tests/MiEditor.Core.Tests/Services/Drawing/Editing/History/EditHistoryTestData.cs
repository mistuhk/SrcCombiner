using WG.MiEditor.Core.Services.Drawing.Editing.History;

namespace WG.MiEditor.Core.Tests.Services.Drawing.Editing.History;

/// <summary>
/// Builders for the history records. Kept deliberately small so each test reads as the rule
/// it is asserting rather than as snapshot construction.
/// </summary>
internal static class EditHistoryTestData
{
    public const string Layer = "LPIS_Poly";
    public const string CaseKey = "CASE-1";

    public static FeatureIdentity Identity(long objectId, string? businessKey = null, string layer = Layer) =>
        new(layer, objectId, businessKey is null ? null : "POLYGONID", businessKey);

    public static FeatureSnapshot Snapshot(
        long objectId,
        string? businessKey = null,
        string? geometryJson = "{\"rings\":[]}",
        DateTime? stamp = null,
        string layer = Layer,
        IReadOnlyDictionary<string, object?>? attributes = null) =>
        new(
            Identity(objectId, businessKey, layer),
            geometryJson,
            "LATESTMOD",
            stamp,
            attributes ?? new Dictionary<string, object?> { ["POLYGONID"] = businessKey });

    public static EditOperationEntry Entry(
        string name = "Edit Vertex",
        IReadOnlyList<FeatureChange>? changes = null,
        string caseKey = CaseKey) =>
        EditOperationEntry.Create(
            name,
            caseKey,
            changes ?? [FeatureChange.Update(Snapshot(1, "100"), Snapshot(1, "100"))],
            DateTime.UtcNow);

    public static EditOperationEntry EntryTouching(long objectId, string? businessKey = null) =>
        Entry(changes: [FeatureChange.Update(Snapshot(objectId, businessKey), Snapshot(objectId, businessKey))]);
}
