namespace WG.MiEditor.Configuration.AppSettings;

/// <summary>
/// Bounds the edit history. MaxUndoDepth is the number of operations kept, and
/// MaxEntrySizeInBytes guards against a single very large operation, which is recorded but
/// flagged as not undoable rather than held in memory.
/// </summary>
public record EditHistoryInfo(int MaxUndoDepth = 10, long MaxEntrySizeInBytes = 8L * 1024 * 1024);
