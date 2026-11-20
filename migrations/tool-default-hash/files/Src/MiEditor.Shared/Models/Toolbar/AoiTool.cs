
using System.Diagnostics.CodeAnalysis;

namespace WG.MiEditor.Shared.Models.Toolbar;

public readonly struct AoiTool : ITool, IEquatable<AoiTool>
{
    public string Name { get; }

    private AoiTool(string name) => Name = name;

    public static readonly AoiTool DrawAOI = new(nameof(DrawAOI));
    public static readonly AoiTool AutoAOI = new(nameof(AutoAOI));
    public static readonly AoiTool Save = new(nameof(Save));
    public static readonly AoiTool Clear = new(nameof(Clear));
    public static readonly AoiTool Delete = new(nameof(Delete));

    public static readonly IEnumerable<AoiTool> All = new[] { DrawAOI, AutoAOI, Save, Clear, Delete };

    public override string ToString() => Name ?? string.Empty;
    public bool Equals(AoiTool other) => Name == other.Name;
    public override bool Equals(object? obj) => obj is AoiTool other && Equals(other);
    // A default (unselected) tool has a null Name; hashing it must not throw.
    public override int GetHashCode() => Name?.GetHashCode() ?? 0;
    public static bool operator ==(AoiTool left, AoiTool right) => left.Equals(right);
    public static bool operator !=(AoiTool left, AoiTool right) => !left.Equals(right);
}
