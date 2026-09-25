using WG.MiEditor.Shared.AutoScanning;

namespace WG.MiEditor.Core.Services.Drawing.Editing.History;

[RegisterService(ServiceLifetime.Scoped, As = new[] { typeof(IEditSelectionClearer) })]
public sealed class EditSelectionClearer(IFeatureLayerService featureLayerService) : IEditSelectionClearer
{
    public Task ClearAsync() => featureLayerService.ClearAllSelections();
}
