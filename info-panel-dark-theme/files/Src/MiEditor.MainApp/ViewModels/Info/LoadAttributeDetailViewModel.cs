
using CommunityToolkit.Mvvm.ComponentModel;
using Esri.ArcGISRuntime.Data;
using System.Collections.ObjectModel;
using WG.MiEditor.Core.Services;
using WG.MiEditor.Core.Services.AttributeInfo;
using WG.MiEditor.Shared.Abstraction;
using WG.MiEditor.Shared.AutoScanning;
using WG.MiEditor.Shared.Models.Info;

namespace WG.MiEditor.MainApp.ViewModels.Info;

[RegisterService(ServiceLifetime.Singleton)]
public partial class LoadAttributeDetailViewModel(
    IInfoAttributeHelper infoAttributeHelper,
    IAttachmentViewModelFactory attachmentFactory,
    IJsonUtilityService jsonUtilityService) : ObservableObject
{
    private const string Info_Table_Grid_Background_Brush = "SubformBackgroundBrush";
    private const string Info_Table_Header_Background_Brush = "ToolStripBrush";

    private readonly TimeZoneInfo BstZone = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");

    public ObservableCollection<LoadAttachmentViewModel> AttachmentItems { get; } = [];

    [ObservableProperty] private bool isAttributeLoading;

    public async Task<StackLayout> LoadAttributesToRightPanel(ArcGISFeature feature, string layerName, string displayId)
    {
        // Step 1: Get visible attributes for display
        var attributesToDisplay = ExtractVisibleAttributes(feature);

        // Step 2: Load attachments asynchronously
        IReadOnlyList<Attachment> attachments = await feature.GetAttachmentsAsync();

        // Step 3: Build the attribute grid UI
        var attributeGrid = LoadAttributeDetails(attributesToDisplay, attachments);

        // Step 4: Build the header and section layout
        var attributeHeader = infoAttributeHelper.CreateLabel(
            string.Format("{0} : {1}", layerName, displayId),
            margin: new Thickness(5, 0, 0, 0)
        );

        var attributeSection = new StackLayout
        {
            Children = { attributeHeader, attributeGrid }
        };

        return attributeSection;
    }

    /// <summary>
    /// Extracts visible attributes from the feature, using popup definition if available.
    /// </summary>
    private List<KeyValuePair<string, object?>> ExtractVisibleAttributes(ArcGISFeature feature)
    {
        var visibleAttributes = new List<KeyValuePair<string, object?>>();

        // Try to extract visible attributes using the popup definition if available
        if (feature.FeatureTable is ArcGISFeatureTable arcGISTable && arcGISTable.PopupDefinition != null)
        {
            AddPopupVisibleAttributes(feature, arcGISTable, visibleAttributes);
        }

        // Fallback: if no visible attributes, use all attributes
        if (visibleAttributes.Count == 0)
        {
            visibleAttributes = [.. feature.Attributes.Select(a => new KeyValuePair<string, object>(a.Key, a.Value!))];
        }

        return visibleAttributes;
    }

    /// <summary>
    /// Adds visible popup attributes to the list, converting DateTime values to BST.
    /// </summary>
    private void AddPopupVisibleAttributes(
        ArcGISFeature feature,
        ArcGISFeatureTable arcGISTable,
        List<KeyValuePair<string, object?>> visibleAttributes)
    {
        foreach (var popupField in arcGISTable.PopupDefinition!.Fields!)
        {
            // Skip invisible or expression fields
            if (!popupField.IsVisible || popupField.FieldName.StartsWith("expression/"))
                continue;

            var fieldName = popupField.FieldName;
            var label = popupField.Label ?? fieldName;

            if (feature.Attributes.TryGetValue(fieldName, out object? value))
            {
                // Convert DateTime to BST if needed
                value = (value is DateTime) ? ConvertToBstIfDateTime(value) : value;
                visibleAttributes.Add(new KeyValuePair<string, object?>(label, value));
            }
        }
    }

    /// <summary>
    /// Converts a value to BST if it is a DateTime, otherwise returns the value unchanged.
    /// </summary>
    private object ConvertToBstIfDateTime(object value)
    {
        if (value is DateTime dateValue)
        {
            return TimeZoneInfo.ConvertTime(dateValue, BstZone);
        }
        return value;
    }

    private Border LoadAttributeDetails(IEnumerable<KeyValuePair<string, object?>> attributes, IReadOnlyList<Attachment>? attachments = null)
    {
        List<AdditionalInfoModel>? additionalInfos = null;
        var tableGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(150) },
                new ColumnDefinition { Width = new GridLength(200) }
            },
            RowSpacing = 0,
            Padding = new Thickness(0),
            Background = GetBrushByResource(Info_Table_Grid_Background_Brush)
        };

        int rowIndex = 0;

        // Header row
        tableGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        tableGrid.Children.Add(infoAttributeHelper.CreateTableCell("Name", true, 0.2, GetBrushByResource(Info_Table_Header_Background_Brush), rowIndex, 0));
        tableGrid.Children.Add(infoAttributeHelper.CreateTableCell("Value", true, 0.2, GetBrushByResource(Info_Table_Header_Background_Brush), rowIndex, 1));
        rowIndex++;

        // Data rows
        foreach (var att in attributes)
        {
            tableGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            
            if (att.Key == "AdditionalInfo" && IsJson(att.Value?.ToString()))
            {
                additionalInfos = jsonUtilityService.Deserialize<List<AdditionalInfoModel>>(att.Value?.ToString() ?? string.Empty);                
            }
            else
            {
                tableGrid.Children.Add(infoAttributeHelper.CreateTableCell(att.Key, false, 0.2, null, rowIndex, 0));
                tableGrid.Children.Add(infoAttributeHelper.CreateTableCell(att.Value?.ToString() ?? "(null)", false, 0.2, null, rowIndex, 1));
            }
            rowIndex++;
        }

        if (additionalInfos != null)
        {
            var headerRow = infoAttributeHelper.CreateTableCell("Additional Attributes", true, 0.2, GetBrushByResource(Info_Table_Header_Background_Brush), tableGrid.RowDefinitions.Count + 1, 0);

            AddContentRow(headerRow, tableGrid, tableGrid.RowDefinitions.Count + 1);

            var additionalInfoGrid = GetAdditionalInfo(additionalInfos);
            if (additionalInfoGrid != null)
            {
                AddContentRow(additionalInfoGrid, tableGrid, tableGrid.RowDefinitions.Count + 1);
            }
        }

        if (attachments != null && attachments.Count > 0)
        {
            AttachmentItems.Clear();
            foreach (var attachment in attachments)
            {
                var vm = attachmentFactory.Create(attachment);
                AttachmentItems.Add(vm);
            }
            var headerRow = infoAttributeHelper.CreateTableCell($"Attachments ({attachments.Count})", true, 0.2, GetBrushByResource(Info_Table_Header_Background_Brush), tableGrid.RowDefinitions.Count + 1, 0);

            AddContentRow(headerRow, tableGrid, tableGrid.RowDefinitions.Count + 1);

            var attchmentGridRow = LoadAttachmentViewModel.CreateAttachmentRows(AttachmentItems);

            AddContentRow(attchmentGridRow, tableGrid, tableGrid.RowDefinitions.Count + 1);
        }

        return new Border
        {
            Padding = new Thickness(5),
            Content = tableGrid
        };
    }

    private bool IsJson(string? v)
    {
        if(string.IsNullOrWhiteSpace(v)) return false;

        return jsonUtilityService.CanDeserialize<List<AdditionalInfoModel>>(v);
    }

    private static void AddContentRow(Microsoft.Maui.Controls.View content, Grid tableGrid, int rowIndex)
    {
        tableGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid.SetRow(content, rowIndex);

        Grid.SetColumn(content, 0);

        Grid.SetColumnSpan(content, 2);

        tableGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        tableGrid.Children.Add(content);
        
    }
    private Grid? GetAdditionalInfo(List<AdditionalInfoModel>? items)
    {        
        if (items == null || items.Count == 0) return default;

        var additionaInfoGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(150) },
                new ColumnDefinition { Width = new GridLength(200) }
            },
            RowSpacing = 0,
            Padding = new Thickness(0),
            Background = GetBrushByResource(Info_Table_Grid_Background_Brush)
        };

        additionaInfoGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        int rowIndex1 = 1;
        foreach (var item in items)
        {
            additionaInfoGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            additionaInfoGrid.Children.Add(infoAttributeHelper.CreateTableCell(item.FieldName, false, 0.2, null, rowIndex1, 0));
            additionaInfoGrid.Children.Add(infoAttributeHelper.CreateTableCell(item.FieldValue, false, 0.2, null, rowIndex1, 1));
            rowIndex1++;
        }
        return additionaInfoGrid;
    }

    private static Brush GetBrushByResource(string resourceName) =>
        (Brush)Application.Current!.Resources[resourceName];
}
