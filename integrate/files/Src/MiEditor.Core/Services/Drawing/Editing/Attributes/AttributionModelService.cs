
using Esri.ArcGISRuntime.Data;
using System.Collections.ObjectModel;
using System.Globalization;
using WG.MiEditor.Core.Services.Drawing.Editing.History;
using WG.MiEditor.Logging.Abstractions;
using WG.MiEditor.MainApp.ViewModels.Editing.Attributes;
using WG.MiEditor.Models.MIEditorLayerConfig;
using WG.MiEditor.Shared.AutoScanning;
using WG.MiEditor.Shared.Helpers;
using WG.MiEditor.Shared.Models.Editing;
using Field = WG.MiEditor.Models.MIEditorLayerConfig.Field;
using Map = Esri.ArcGISRuntime.Mapping.Map;

namespace WG.MiEditor.Core.Services.Drawing.Editing.Attributes;

[RegisterService(ServiceLifetime.Scoped, As =   new[] { typeof(IAttributionModelService) })]
public class AttributionModelService(
    ILoggerService loggerService,
    IFeatureLayerService featureLayerService,
    IEnumerable<IAttributionModelStrategy> strategies,
    ICaseContext caseContext,
    IUserContext userContext,
    IJsonUtilityService jsonUtilityService,
    IEditOperationRecorder editRecorder) : IAttributionModelService
{
    private readonly Dictionary<AttributionMode, IAttributionModelStrategy> strategies = strategies.ToDictionary(s => s.Mode, s => s);
    private const string constPicker = "Picker";
    private const string consMultiPickerCtrl = "MultiPicker";
    public async Task<AttributionModel> GetModelAsync(
        Map map,
        ArcGISFeature feature,
        AttributionMode mode,
        string editableLayer,
        string targetLayer,
        bool useAlterLayer = false)
    {
        if (map == null)
        {
            loggerService.Warn("AttributeFormService cannot build form: Map is not loaded.");
            throw new InvalidOperationException("Map is not loaded.");
        }

        if (strategies.TryGetValue(mode, out var strategy))
        {
            strategy.UseAlterLayer = useAlterLayer;

            var model = await strategy.BuildModelAsync(feature, map, editableLayer, targetLayer);

            var layer = string.IsNullOrWhiteSpace(targetLayer) ? editableLayer : targetLayer;
            model.DisplayLayerName = $"{layer} - Attributes ({mode})";

            return model;
        }

        loggerService.Warn($"Unsupported attribution model mode: {mode}");
        throw new NotSupportedException($"Attribution model mode: '{mode}' is not supported.");
    }

    public async Task<bool> SaveModelAsync(        
        ArcGISFeature feature,
        string targetLayer,
        ObservableCollection<Field> fields,
        ObservableCollection<Field> additionalFields,
        ObservableCollection<FileUpoadInfo> files,
        AttributionMode mode,
        bool isOffline)
    {               
        FeatureTable? featureServiceTable = null;

        // Watches this save so it can be undone. Creating a feature is the only mode recorded so
        // far; a modify has to restore prior values across the related tables too, which is a
        // later delivery. Disposed without a commit if anything below throws, so a failed save
        // records nothing.
        using var editScope = editRecorder.Begin(OperationNameFor(mode, targetLayer));

        try
        {
            await SaveAttributesToFeature(feature, fields, additionalFields, mode);
            await SaveAttachmentsToFeature(feature, files);

             var featureLayer = await featureLayerService.GetFeatureLayerAsync(targetLayer);
             featureServiceTable = featureLayer?.FeatureTable;

            if (featureServiceTable == null)
            {
                loggerService.Warn($"Error occurred while attempting to save model: 'Feature table not found for {targetLayer}'.");
                return false;
            }

            switch (mode)
            {
                case AttributionMode.New:
                case AttributionMode.Import:
                case AttributionMode.ImportSketch:
                    editScope.MarkInsert(targetLayer, feature);
                    await featureServiceTable.AddFeatureAsync(feature);
                    break;

                case AttributionMode.Modify:
                case AttributionMode.ModifySketch:
                    await featureServiceTable.UpdateFeatureAsync(feature);
                    break;

                default:
                    //throw exception?
                    break;
            }

            if (!isOffline && featureServiceTable is ServiceFeatureTable serviceFeatureTable)
            {
                var applyEditsResult = await serviceFeatureTable.ApplyEditsAsync();
  
                var errorResult = applyEditsResult.FirstOrDefault(r => r.CompletedWithErrors && r.Error != null);

                if (errorResult != null)
                {
                    var errorMessage = errorResult.Error?.Message ?? "ApplyEdits failed with an unknown error.";

                    throw new InvalidOperationException(errorMessage);
                }
            }

            // The edit is on the server now, so the record is kept. Reads the row back to pick up
            // the object id the service assigned, which is why this runs after ApplyEdits.
            await editScope.CommitAsync();

            return true;
        }
        catch (Exception ex)
        {
            // Discard any pending local edits and revert the feature table to its last synchronized state.
            if (featureServiceTable is ServiceFeatureTable serviceFeatureTable)
            {
                await serviceFeatureTable.UndoLocalEditsAsync();
            }

            loggerService.Error(ex, $"Error occurred while attempting to save attribution model: {ex.Message}.");
            return false;
        }
    }
    /// <summary>
    /// What the user is told was undone, so it reads as the action they took rather than as the
    /// mode the code was in.
    /// </summary>
    private static string OperationNameFor(AttributionMode mode, string targetLayer) => mode switch
    {
        AttributionMode.New => $"Add {targetLayer}",
        AttributionMode.Import => $"Import {targetLayer}",
        AttributionMode.ImportSketch => $"Import sketch to {targetLayer}",
        AttributionMode.Modify => $"Edit {targetLayer} attributes",
        AttributionMode.ModifySketch => $"Edit sketch attributes",
        _ => $"Edit {targetLayer}"
    };

    private static bool IsModified(AttributionMode mode) => mode == AttributionMode.Modify || mode == AttributionMode.ModifySketch || mode == AttributionMode.Import || mode == AttributionMode.ImportSketch;

    private async Task SaveAttributesToFeature(
        ArcGISFeature feature,
        ObservableCollection<Field> fields,
        ObservableCollection<Field> additionalFields,
        AttributionMode mode)
    {
        // Prepare system-editable fields if in a modified mode
        var systemEditableFields = await GetSystemEditableFieldsAsync(mode);

        // Set main fields
        foreach (var field in fields)
        {
            if (!ShouldProcessField(field, mode, systemEditableFields))
                continue;

            SetFeatureFieldValue(feature, field, mode, systemEditableFields);
        }

        // Always set MICaseId
        feature.SetAttributeValue("MICaseId", Convert.ToInt32(caseContext.CaseNo));

        // Handle additional fields as JSON
        var additionalInfoList = BuildAdditionalInfoList(additionalFields);
        if (additionalInfoList.Count > 0)
        {
            string additionalInfoJson = jsonUtilityService.Serialise<List<AdditionalInfo>>(additionalInfoList);
            feature.SetAttributeValue("AdditionalInfo", additionalInfoJson);
        }
    }

    /// <summary>
    /// Determines if a field should be processed based on mode and system fields.
    /// </summary>
    private static bool ShouldProcessField(Field field, AttributionMode mode, Dictionary<string, string> systemEditableFields)
    {
        bool isModifiedMode = mode == AttributionMode.Modify || mode == AttributionMode.ModifySketch || mode == AttributionMode.Import || mode == AttributionMode.ImportSketch;

        if (string.IsNullOrWhiteSpace(field.Name))
        {
            return false;
        }

        if (isModifiedMode && !field.IsEditable && !systemEditableFields.ContainsKey(field.Name.ToUpper()))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Sets the value of a field on the feature, handling pickers and system fields.
    /// </summary>
    private void SetFeatureFieldValue(
        ArcGISFeature feature,
        Field field,
        AttributionMode mode,
        Dictionary<string, string> systemEditableFields)
    {

        if (feature.FeatureTable == null
            || field?.Name == null
            || !feature.Attributes.ContainsKey(field.Name))
        {
            return;
        }

        var fieldType = feature.FeatureTable.Fields.FirstOrDefault(f => f.Name.Equals(field.Name, StringComparison.OrdinalIgnoreCase));
        if (fieldType == null)
            return;

        // Handle Picker control
        if (field.ControlType == constPicker)
        {
            SetPickerFieldValue(feature, field, fieldType);
        }
        else if(field.ControlType == consMultiPickerCtrl)
        {
            SetMultiPickerFieldValue(feature, field, fieldType);
        }
        else
        {
            SetStandardFieldValue(feature, field, mode, systemEditableFields, fieldType);
        }
    }

    /// <summary>
    /// Sets the value for a Picker field.
    /// </summary>
    private void SetPickerFieldValue(ArcGISFeature feature, Field field, Esri.ArcGISRuntime.Data.Field fieldType)
    {
        if (field?.Name == null) return;

        if (field.SelectedValue != null || field.SelectedIndex >= 0)
        {
            var selectedItem = field.FieldValues[field.SelectedIndex];
            if (selectedItem != null && selectedItem.Value != null)
            {
                var updateSelectedValue = GetUpdatedValueByFieldType(selectedItem.Value, fieldType.FieldType);
                feature.SetAttributeValue(field.Name, updateSelectedValue);
            }
        }
        else
        {
            feature.SetAttributeValue(field.Name, null);
        }
    }
    private void SetMultiPickerFieldValue(ArcGISFeature feature, Field field, Esri.ArcGISRuntime.Data.Field fieldType)
    {
        if (field?.Name == null) return;

        if (field.FieldValues != null)
        {
             var selectionValues = string.Join(";",field.FieldValues.Where(x => x.IsSelected).Select(x => x.Value));
                                    
             var updateSelectedValue = GetUpdatedValueByFieldType(selectionValues, fieldType.FieldType);
             feature.SetAttributeValue(field.Name, updateSelectedValue);
        }
        else
        {
            feature.SetAttributeValue(field.Name, null);
        }
    }

    /// <summary>
    /// Sets the value for a standard (non-picker) field, handling system fields.
    /// </summary>
    private void SetStandardFieldValue(
        ArcGISFeature feature,
        Field field,
        AttributionMode mode,
        Dictionary<string, string> systemEditableFields,
        Esri.ArcGISRuntime.Data.Field fieldType)
    {
        if (string.IsNullOrWhiteSpace(field?.Name))
        {
            return;
        }

        bool hasValue = !string.IsNullOrWhiteSpace(field.Value);
        bool isSystemEditableModified = IsModified(mode) && systemEditableFields.ContainsKey(field.Name.ToUpper());

        if (hasValue || isSystemEditableModified)
        {
            var valueToSave = isSystemEditableModified
                ? systemEditableFields[field.Name.ToUpper()]
                : field.Value;
            if (valueToSave == null) return;
            var updateValue = GetUpdatedValueByFieldType(valueToSave, fieldType.FieldType);
            feature.SetAttributeValue(field.Name, updateValue);
        }
    }

    /// <summary>
    /// Builds the list of additional info objects from additional fields.
    /// </summary>
    private static List<AdditionalInfo> BuildAdditionalInfoList(ObservableCollection<Field> additionalFields)
    {
        var additionalInfoList = new List<AdditionalInfo>();
        foreach (var field in additionalFields)
        {
            if (field.ControlType == "Picker")
            {
                if (field.SelectedValue != null || field.SelectedIndex >= 0)
                {
                    additionalInfoList.Add(new AdditionalInfo
                    {
                        FieldName = field.Name,
                        FieldValue = field.FieldValues[field.SelectedIndex].Value,
                        ControlType = field.ControlType
                    });
                }
            }
            else if (!string.IsNullOrEmpty(field.Value))
            {
                additionalInfoList.Add(new AdditionalInfo
                {
                    FieldName = field.Name,
                    FieldValue = field.Value,
                    ControlType = field.ControlType
                });
            }
        }
        return additionalInfoList;
    }

    /// <summary>
    /// Prepares system-editable fields for modified modes.
    /// </summary>
    private Task<Dictionary<string, string>> GetSystemEditableFieldsAsync(AttributionMode mode)
    {
        var systemEditableFields = new Dictionary<string, string>();
        if (IsModified(mode))
        {            
            systemEditableFields.Add("MODIFIEDBY", userContext.UserId.ToString());
            systemEditableFields.Add("MODIFIEDDT", DateTime.Now.ToString());
        }
        return Task.FromResult(systemEditableFields);
    }

    private async Task SaveAttachmentsToFeature(ArcGISFeature feature, ObservableCollection<FileUpoadInfo> files)
    {
        //Clear before saving 
        var attachments = await feature.GetAttachmentsAsync();
        foreach (var file in attachments)
        {
            await feature.DeleteAttachmentAsync(file);
        }

        foreach (var file in files)
        {
            try
            {
                if (file == null || file.FilePath == null || file.FileName == null) continue;

                string fileExtension = Path.GetExtension(file.FilePath).ToLower();
                string contentType = fileExtension switch
                {
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".png" => "image/png",
                    ".pdf" => "application/pdf",
                    _ => "application/octet-stream",
                };

                byte[] attachmentData;
                using (FileStream fs = new(file.FilePath, FileMode.Open, FileAccess.Read))
                using (MemoryStream ms = new())
                {
                    await fs.CopyToAsync(ms);
                    attachmentData = ms.ToArray();
                }

                await feature.AddAttachmentAsync(file.FileName, contentType, attachmentData);
            }
            catch (Exception ex)
            {
                loggerService.Error(ex, $"Failed to save attachment {file.FileName} for feature.");
                // Should re-throw or just skip the file?
            }
        }
    }

    private object? GetUpdatedValueByFieldType(object value, FieldType fieldType)
    {
        switch (fieldType)
        {
            case FieldType.OID: return value is int ? value : Convert.ToInt32(value);
            case FieldType.Int16: return value is short ? value : Convert.ToInt16(value);
            case FieldType.Int32: return value is int ? value : Convert.ToInt32(value);
            case FieldType.Int64: return value is long ? value : Convert.ToInt64(value);
            case FieldType.Float32: return value is float ? value : Convert.ToSingle(value);
            case FieldType.Float64: return value is double ? value : Convert.ToDouble(value);
            case FieldType.Date: return ConvertToDate(value);
            case FieldType.Text: return value is string ? value : Convert.ToString(value);
            default:
                loggerService.Warn($"Unsupported field type: {fieldType}. Value left as string.");
                return Convert.ToString(value);
        }
    }

    private static object ConvertToDate(object value)
    {
        if (value is DateTimeOffset or DateTime)
        {
            return value;
        }

        if (value is string dateString)
        {
            if (DateTime.TryParse(dateString, CultureInfo.CurrentCulture, DateTimeStyles.None, out DateTime convertedDatetime))
            {
                return convertedDatetime;
            }

            throw new FormatException($"Unable to parse the date string: {dateString}");
        }
        throw new InvalidCastException($"Cannot convert value to DateTime: {value}");
    }
}
