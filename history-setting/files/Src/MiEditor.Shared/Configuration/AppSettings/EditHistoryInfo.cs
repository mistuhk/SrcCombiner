
namespace WG.MiEditor.Shared.Configuration.AppSettings;

/// <summary>
/// Defines the limits applied to edit history. MaxUndoDepth caps the number of
/// retained operations, while MaxTotalBytes caps the combined size of the undo
/// and redo history. Oldest entries are evicted as needed to satisfy both.
/// <para>
/// Available says whether this deployment offers the feature at all. It is separate from the user's own
/// setting and takes precedence over it: this is the estate wide withdrawal, and a user cannot turn the
/// feature back on against it. Named Available rather than Enabled so that the combined check reads as
/// what it means, available here and wanted by this user.
/// </para>
/// <para>
/// It defaults to true so that an appsettings file written before this existed leaves the feature on
/// offer. The user setting defaults to off, so the feature is still dark until somebody opts in, and
/// this value only ever needs editing to withdraw the option altogether.
/// </para>
/// </summary>
public record EditHistoryInfo(
    int MaxUndoDepth = 10,
    long MaxTotalBytes = 64L * 1024 * 1024,
    bool Available = true);
