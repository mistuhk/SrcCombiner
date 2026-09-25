using Esri.ArcGISRuntime.Data;

namespace WG.MiEditor.Core.Services.Drawing.Editing.History;

/// <summary>
/// Opens a scope around one operation so the history can be written down. Obtained by the
/// services that perform edits; they are otherwise unaware of the history.
/// </summary>
public interface IEditOperationRecorder
{
    /// <summary>
    /// Begins watching an operation. The name is what the user is told was undone, so it should
    /// read as the action they took.
    /// <para>
    /// Each call returns an independent scope, so one operation is one entry. A save that spans
    /// several services and must record as a single entry should pass its scope down the call
    /// chain; the recorder deliberately keeps no ambient state of its own.
    /// </para>
    /// </summary>
    IEditOperationScope Begin(string operationName);
}

/// <summary>
/// One operation being watched. Nothing is written to the journal unless CommitAsync runs, so an
/// operation that throws or is cancelled leaves no trace. Committing twice records nothing the
/// second time.
/// </summary>
public interface IEditOperationScope : IDisposable
{
    /// <summary>Notes that a row is about to be created. Its id is not known until it is sent.</summary>
    void MarkInsert(string layerName, Feature feature);

    /// <summary>Copies a row before it is changed, so the previous values can be written back.</summary>
    Task CaptureUpdateBeforeAsync(string layerName, Feature feature);

    /// <summary>Copies a row before it is deleted. Afterwards this is the only copy that exists.</summary>
    Task CaptureDeleteAsync(string layerName, Feature feature);

    /// <summary>
    /// Records the operation. Reads every touched row back from its table, so the recorded state
    /// and any later comparison are produced by the same code.
    /// <para>
    /// Nothing is returned, because there is nothing for a caller to do about the two ways this
    /// falls short. An operation too large for the whole history budget is reported to the user by
    /// the recorder, which is the only place that knows why; a scope already committed or disposed
    /// is a caller mistake, and is logged. Handing back a flag only invited a second message on top
    /// of the first.
    /// </para>
    /// </summary>
    Task CommitAsync();
}
