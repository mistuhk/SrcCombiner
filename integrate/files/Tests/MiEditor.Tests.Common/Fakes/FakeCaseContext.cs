using WG.MiEditor.Models;
using WG.MiEditor.Shared.Helpers;

namespace WG.MiEditor.Tests.Common;

/// <summary>
/// A settable case context. Moq would do for the properties, but GetSelectedCase sends a request
/// message in the real implementation, and a fake makes it obvious that nothing here does.
/// </summary>
public sealed class FakeCaseContext : ICaseContext
{
    public string? CRN { get; set; }
    public string? CaseNo { get; set; }
    public int CaseId { get; set; }
    public string? FileRefNumber { get; set; }

    public CaseModel? SelectedCase { get; set; }

    public Task<CaseModel> GetSelectedCase() =>
        SelectedCase is null
            ? throw new InvalidOperationException("No case has been set on the fake.")
            : Task.FromResult(SelectedCase);

    public void SetSelectedCase(CaseModel? caseModel)
    {
        SelectedCase = caseModel;

        if (caseModel is null)
        {
            CRN = null;
            CaseNo = null;
            CaseId = 0;
            FileRefNumber = null;
        }
    }
}
