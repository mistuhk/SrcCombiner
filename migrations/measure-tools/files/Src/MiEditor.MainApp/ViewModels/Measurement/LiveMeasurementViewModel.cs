
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Options;
using System.Collections.ObjectModel;
using System.Globalization;
using WG.MiEditor.Core.Services.Notifications;
using WG.MiEditor.Shared.AutoScanning;
using WG.MiEditor.Shared.Configuration.AppSettings;
using WG.MiEditor.Shared.Messages.Measurement;
using WG.MiEditor.Shared.Models.Notifications;
using WG.MiEditor.Shared.Models.Toolbar;

namespace WG.MiEditor.MainApp.ViewModels.Measurement;

[RegisterService(ServiceLifetime.Scoped)]
public partial class LiveMeasurementViewModel : ObservableRecipient,
    IRecipient<SketchingStartedMessage<EditingTool>>,
    IRecipient<SketchingCompletedMessage<EditingTool>>,
    IRecipient<SketchingStartedMessage<MeasureTool>>,
    IRecipient<SketchingCompletedMessage<MeasureTool>>,
    IRecipient<SketchingCompletedMessage<StandardTool>>,
    IRecipient<DrawingProgressMessage>
{
    private readonly INotificationManager notificationManager;
    private readonly SheepMovementBufferSettings bufferSettings;

    private double area;
    private double length;
    private double lastValidRadius;

    // The radius text most recently validated. Completed and Unfocused both fire when the user
    // presses Enter, so without this an invalid entry would raise the same error twice.
    private string? lastValidatedRadiusText;

    private readonly ICollection<ITool> areaTools = [
        MeasureTool.Area,
        EditingTool.AddPolygon,
        EditingTool.AddSketchPolygon, 
        MeasureTool.SheepMovementBuffer
    ];

    private readonly ICollection<ITool> lengthTools = [
        MeasureTool.Distance,
        EditingTool.AddLine,
        EditingTool.AddSketchLine
    ];

    public LiveMeasurementViewModel(IMessenger messenger, INotificationManager notificationManager, IOptions<SheepMovementBufferSettings> bufferOptions) 
        : base(messenger)
    {
        this.IsActive = true;
        this.notificationManager = notificationManager;
        this.bufferSettings = bufferOptions.Value;

        this.InitialiseDefaultRadius();
    }

    [ObservableProperty]
    public partial string AreaValue { get; set; } = "0.0";

    [ObservableProperty]
    public partial string LengthValue { get; set; } = "0.0";

    [ObservableProperty]
    public partial string DefaultRadius { get; set; } = "10000";

    [ObservableProperty]
    public partial bool IsMeasurementActive { get; set; }

    [ObservableProperty]
    public partial bool IsAreaVisible { get; set; }

    [ObservableProperty]
    public partial bool IsLengthVisible { get; set; }

    [ObservableProperty]
    public partial bool IsRadiusInputVisible { get; set; }

    [ObservableProperty]
    public partial string LengthLabel { get; set; } = "Length:";

    [ObservableProperty]
    public partial MeasurementUnit SelectedAreaUnit { get; set; } = MeasurementUnit.SquareMetres;

    [ObservableProperty]
    public partial MeasurementUnit SelectedLengthUnit { get; set; } = MeasurementUnit.Metres;

    public ObservableCollection<MeasurementUnit> AreaUnits { get; } = [.. MeasurementUnit.AreaUnits];
    public ObservableCollection<MeasurementUnit> LengthUnits { get; } = [.. MeasurementUnit.LengthUnits];

    // Bound to the radius box's Completed and Unfocused events.
    [RelayCommand]
    private void ValidateRadius() => ApplyRadius(force: false);

    /// <summary>
    /// Validates the radius text and tells the buffer tool which radius to use. With
    /// <paramref name="force"/> the radius is (re)sent even if the text has not changed, which is
    /// needed whenever the buffer tool starts.
    /// </summary>
    private void ApplyRadius(bool force)
    {
        var text = DefaultRadius;
        if (!force && text == lastValidatedRadiusText) return;

        if (!RadiusInput.TryParse(text, CultureInfo.CurrentCulture, out var radius))
        {
            this.notificationManager.Show("Sheep Movement Buffer", $"Please enter a valid number for the buffer radius.", NotificationType.Error);
            RevertToLastValidRadius();

            return;
        }

        if (!IsSupportedRadius(radius))
        {
            this.notificationManager.Show("Sheep Movement Buffer", $"{RadiusInput.Format(radius, CultureInfo.CurrentCulture)} is not a valid buffer radius. The supported range for the buffer radius is between {bufferSettings.MinRadius}m and {bufferSettings.MaxRadius}m.", NotificationType.Error);
            RevertToLastValidRadius();

            return;
        }

        this.lastValidRadius = radius;
        this.lastValidatedRadiusText = text;
        this.Messenger.Send(new RadiusChangedMessage(radius));
    }

    public void Receive(SketchingStartedMessage<EditingTool> message)
    {
        ToggleComponentsVisibility(message.Tool, true);

        if (this.IsRadiusInputVisible) ApplyRadius(force: true);

        InitDefaultArea();
        InitDefaultLength();
    }

    public void Receive(SketchingCompletedMessage<EditingTool> message)
    {
        ToggleComponentsVisibility(message.Tool, false);
        ResetArea();
        ResetLength();
    }

    public void Receive(SketchingStartedMessage<MeasureTool> message)
    {
        ToggleComponentsVisibility(message.Tool, true);

        if (this.IsRadiusInputVisible) ApplyRadius(force: true);

        InitDefaultArea();
        InitDefaultLength();
    }

    public void Receive(SketchingCompletedMessage<MeasureTool> message)
    {
        ToggleComponentsVisibility(message.Tool, false);
        ResetArea();
        ResetLength();
    }

    public void Receive(SketchingCompletedMessage<StandardTool> message)
    {
        ToggleComponentsVisibility(message.Tool, false);
        ResetArea();
        ResetLength();
    }

    public void Receive(DrawingProgressMessage message)
    {
        SetLengthIfRequired(message);
        SetAreaIfRequired(message);
    }

    partial void OnSelectedAreaUnitChanged(MeasurementUnit value) => this.AreaValue = GetArea(this.area);

    partial void OnSelectedLengthUnitChanged(MeasurementUnit value) => this.LengthValue = GetLength(this.length);

    // The picker can push null (for example while its items are being reset), so fall back to
    // the default unit rather than failing.
    private string GetLength(double value) =>
        (SelectedLengthUnit ?? MeasurementUnit.Metres).Format(value, CultureInfo.CurrentCulture);

    private string GetArea(double value) =>
        (SelectedAreaUnit ?? MeasurementUnit.SquareMetres).Format(value, CultureInfo.CurrentCulture);

    // Zero is a real reading (for example after undoing vertices) and is shown as such; only a
    // missing or non-finite value leaves the field blank.
    private void SetAreaIfRequired(DrawingProgressMessage message)
    {
        if (message.Area is { } newArea && double.IsFinite(newArea) && newArea >= 0)
        {
            this.area = newArea;
            this.AreaValue = GetArea(area);

            return;
        }

        this.AreaValue = string.Empty;
    }

    private void SetLengthIfRequired(DrawingProgressMessage message)
    {
        if (message.Length is { } newLength && double.IsFinite(newLength) && newLength >= 0)
        {
            this.length = newLength;
            this.LengthValue = GetLength(length);

            return;
        }

        this.LengthValue = string.Empty;
    }

    // Put the box back to the last good value and make sure the buffer tool is using that same
    // value, so what is drawn always matches what is shown.
    private void RevertToLastValidRadius()
    {
        DefaultRadius = RadiusInput.Format(this.lastValidRadius, CultureInfo.CurrentCulture);
        this.lastValidatedRadiusText = DefaultRadius;
        this.Messenger.Send(new RadiusChangedMessage(this.lastValidRadius));
    }

    private bool IsSupportedRadius(double value) => value >= this.bufferSettings.MinRadius && value <= this.bufferSettings.MaxRadius;

    private void InitialiseDefaultRadius()
    {
        if (RadiusInput.TryParse(DefaultRadius, CultureInfo.CurrentCulture, out var radius) && IsSupportedRadius(radius))
        {
            lastValidRadius = radius;
            return;
        }

        lastValidRadius = bufferSettings.MinRadius;
        DefaultRadius = RadiusInput.Format(lastValidRadius, CultureInfo.CurrentCulture);
    }

    private void InitDefaultLength()
    {
        this.length = 0;
        this.LengthValue = GetLength(this.length);
    }

    private void ResetLength() => this.LengthValue = string.Empty;

    private void InitDefaultArea()
    {
        this.area = 0;
        this.AreaValue = GetArea(this.area);
    }

    private void ResetArea() => this.AreaValue = string.Empty;

    private void ToggleComponentsVisibility(ITool tool, bool isSketching)
    {
        this.IsMeasurementActive = isSketching;

        if (IsMeasurementActive)
        {
            this.IsAreaVisible = areaTools.Any(t => t.Equals(tool));
            this.IsLengthVisible = lengthTools.Any(t => t.Equals(tool)) || IsAreaVisible;
            this.IsRadiusInputVisible = tool.Equals(MeasureTool.SheepMovementBuffer);
            this.LengthLabel = tool.Equals(MeasureTool.SheepMovementBuffer) ? "Circumference:" : "Length:";

            return;
        }

        this.IsAreaVisible = false;
        this.IsLengthVisible = false;
        this.IsRadiusInputVisible = false;
        this.LengthLabel = string.Empty;
    }
}
