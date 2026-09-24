namespace WG.MiEditor.Configuration.AppSettings;

/// <summary>
/// Bounds the edit history. MaxUndoDepth is how many operations are kept.
/// MaxTotalBytes bounds what the whole history may hold, across both the undo and the redo
/// side, so a few very large operations cannot sit inside the depth cap and still consume
/// an unbounded amount of memory. The oldest operations are evicted until the total fits.
/// </summary>
public record EditHistoryInfo(int MaxUndoDepth = 10, long MaxTotalBytes = 64L * 1024 * 1024);
