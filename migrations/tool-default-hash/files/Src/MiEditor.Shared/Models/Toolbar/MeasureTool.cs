
using System.Diagnostics.CodeAnalysis;

namespace WG.MiEditor.Shared.Models.Toolbar;

public readonly struct MeasureTool : ITool, IEquatable<MeasureTool>
{
    public string Name { get; }
    private MeasureTool(string name) => Name = name;

    public static readonly MeasureTool Distance = new(nameof(Distance));
    public static readonly MeasureTool Area = new(nameof(Area));
    public static readonly MeasureTool SheepMovementBuffer = new(nameof(SheepMovementBuffer));

    public static IEnumerable<MeasureTool> All => [Distance, Area, SheepMovementBuffer];

    public override string ToString() => Name ?? string.Empty;
    public bool Equals(MeasureTool other) => Name == other.Name;
    public override bool Equals(object? obj) => obj is MeasureTool other && Equals(other);
    // A default (unselected) tool has a null Name; hashing it must not throw.
    public override int GetHashCode() => Name?.GetHashCode() ?? 0;
    public static bool operator ==(MeasureTool left, MeasureTool right) => left.Equals(right);
    public static bool operator !=(MeasureTool left, MeasureTool right) => !left.Equals(right);
}
