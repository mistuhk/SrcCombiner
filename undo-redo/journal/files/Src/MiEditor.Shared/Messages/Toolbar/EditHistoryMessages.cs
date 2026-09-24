namespace WG.MiEditor.Shared.Messages.Toolbar;

/// <summary>
/// Broadcast whenever the edit history changes, so the standard toolbar can enable or
/// disable the undo and redo buttons. Mirrors how the map view navigation buttons are driven.
/// </summary>
public sealed record EditHistoryChangedMessage(bool CanUndo, bool CanRedo);
