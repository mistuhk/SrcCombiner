
using WG.MiEditor.Core.Services.Drawing.Editing.History;
using WG.MiEditor.Core.Services.Drawing.Editing.History.Journal;
using WG.MiEditor.Core.Services.Drawing.Editing.History.OperationScope;
using WG.MiEditor.Core.Services.Drawing.Editing.History.StateManagement;

namespace WG.MiEditor.Core.Tests.Services.Drawing.Editing.History.StateManagement;

/// <summary>
/// Creates history records for tests. Kept intentionally small so tests focus
/// on the behaviour being verified rather than the mechanics of building snapshots.
/// </summary>
internal static class EditHistoryTestData
{
    public const string Layer = "LPIS_Poly";
    public const string CaseKey = "CASE-1";

    public static FeatureIdentity CreateIdentity(
        long objectId, 
        string? UniqueIdentifier = null, 
        string layer = Layer) =>
        new(layer, objectId, UniqueIdentifier is null ? null : "POLYGONID", UniqueIdentifier);

    public static FeatureSnapshot CreateSnapshot(
        long objectId,
        string? UniqueIdentifier = null,
        string? geometryJson = "{\"rings\":[]}",
        DateTime? stamp = null,
        string layer = Layer,
        IReadOnlyDictionary<string, object?>? attributes = null) =>
        new(
            CreateIdentity(objectId, UniqueIdentifier, layer),
            geometryJson,
            "LATESTMOD",
            stamp,
            // Same comparer the production factory uses. Attribute name casing genuinely varies in
            // this codebase, so a case sensitive fixture would not exercise what ships.
            attributes ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["POLYGONID"] = UniqueIdentifier
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

    public static EditOperationEntry CreateEntryTouching(long objectId, string? UniqueIdentifier = null) =>
        CreateEntry(changes: [FeatureChange.Update(CreateSnapshot(objectId, UniqueIdentifier), CreateSnapshot(objectId, UniqueIdentifier))]);
}
