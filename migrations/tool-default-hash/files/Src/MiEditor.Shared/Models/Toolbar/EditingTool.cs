
namespace WG.MiEditor.Shared.Models.Toolbar;

public readonly struct EditingTool : ITool, IEquatable<EditingTool>
{
    public string Name { get; }

    private EditingTool(string name) => Name = name;

    public static readonly EditingTool Select = new(nameof(Select));
    public static readonly EditingTool DeSelect = new(nameof(DeSelect));

    public static readonly EditingTool AddPoint = new(nameof(AddPoint));
    public static readonly EditingTool AddLine = new(nameof(AddLine));
    public static readonly EditingTool AddPolygon = new(nameof(AddPolygon));
    public static readonly EditingTool AddSketchPoint = new(nameof(AddSketchPoint));
    public static readonly EditingTool AddSketchLine = new(nameof(AddSketchLine));
    public static readonly EditingTool AddSketchPolygon = new(nameof(AddSketchPolygon));

    public static readonly EditingTool MergePolygon = new(nameof(MergePolygon));
    public static readonly EditingTool DrawLineSplitter = new(nameof(DrawLineSplitter));
    public static readonly EditingTool ImportLineSplitter = new(nameof(ImportLineSplitter));

    public static readonly EditingTool ModifyAttribute = new(nameof(ModifyAttribute));
    public static readonly EditingTool ImportFeatureToEditableLayer = new(nameof(ImportFeatureToEditableLayer));
    public static readonly EditingTool ImportFeatureToSketchLayer = new(nameof(ImportFeatureToSketchLayer));
    public static readonly EditingTool ImportSketchToEditableLayer = new(nameof(ImportSketchToEditableLayer));
    public static readonly EditingTool DeleteFeature = new(nameof(DeleteFeature));
    public static readonly EditingTool MovePoint = new(nameof(MovePoint));
    public static readonly EditingTool DisplayVertex = new(nameof(DisplayVertex));
    public static readonly EditingTool AddVertex = new(nameof(AddVertex));
    public static readonly EditingTool DeleteVertex = new(nameof(DeleteVertex));
    public static readonly EditingTool MoveVertex = new(nameof(MoveVertex));
    public static readonly EditingTool ModifySketchAttribute = new(nameof(ModifySketchAttribute));
    public static readonly EditingTool DeleteSketch = new(nameof(DeleteSketch));
    public static readonly EditingTool Buffer = new(nameof(Buffer));
    public static readonly EditingTool AddTechnicalAssessment = new(nameof(AddTechnicalAssessment));

    public static readonly IEnumerable<EditingTool> All =
    [
        Select,
        DeSelect,
        AddPoint,
        AddLine,
        AddPolygon,
        AddSketchPoint,
        AddSketchLine,
        AddSketchPolygon,
        MergePolygon,
        DrawLineSplitter,
        ImportLineSplitter,
        ModifyAttribute,
        ModifySketchAttribute,
        DeleteFeature,
        DisplayVertex,
        AddVertex,
        DeleteVertex,
        MoveVertex,
        ImportFeatureToEditableLayer,
        ImportFeatureToSketchLayer,
        ImportSketchToEditableLayer,
        DeleteSketch,
        AddTechnicalAssessment,
        Buffer
    ];

    public override string ToString() => Name ?? string.Empty;
    public bool Equals(EditingTool other) => Name == other.Name;
    public override bool Equals(object? obj) => obj is EditingTool other && Equals(other);
    // A default (unselected) tool has a null Name; hashing it must not throw.
    public override int GetHashCode() => Name?.GetHashCode() ?? 0;
    public static bool operator ==(EditingTool left, EditingTool right) => left.Equals(right);
    public static bool operator !=(EditingTool left, EditingTool right) => !left.Equals(right);
}
