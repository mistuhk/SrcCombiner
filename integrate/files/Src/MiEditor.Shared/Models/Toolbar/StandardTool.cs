
namespace WG.MiEditor.Shared.Models.Toolbar;

public readonly struct StandardTool : ITool, IEquatable<StandardTool>
{
    public string Name { get; }

    private StandardTool(string name) => Name = name;

    public static readonly StandardTool Plus = new(nameof(Plus));
    public static readonly StandardTool Minus = new(nameof(Minus));
    public static readonly StandardTool Info = new(nameof(Info));
    public static readonly StandardTool Refresh = new(nameof(Refresh));
    public static readonly StandardTool PreviousView = new(nameof(PreviousView));
    public static readonly StandardTool NextView = new( nameof(NextView));
    public static readonly StandardTool Undo = new(nameof(Undo));
    public static readonly StandardTool Redo = new(nameof(Redo));

    public static readonly IEnumerable<StandardTool> All =
    [
        Plus,
        Minus,
        Info,
        Refresh,
        PreviousView,
        NextView,
        Undo,
        Redo
    ];

    public override string ToString() => Name;
    public bool Equals(StandardTool other) => Name == other.Name;
    public override bool Equals(object? obj) => obj is StandardTool other && Equals(other);
    public override int GetHashCode() => Name.GetHashCode();
    public static bool operator ==(StandardTool left, StandardTool right) => left.Equals(right);
    public static bool operator !=(StandardTool left, StandardTool right) => !left.Equals(right);
}
