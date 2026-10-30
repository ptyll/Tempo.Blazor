namespace Tempo.Blazor.Components.Signing;

/// <content>
/// Progress presentation of the wizard panel (Signing redesign phase 3): the counts COMPLETED
/// FIELDS, not the step index — the signer may fill the fields in any order through the permanent
/// field list, so "1 of 3 done" is the honest measure. The neutral "{0} of {1}" formulation needs
/// no plural forms.
/// </content>
public partial class TmSigningFormRunner
{
    private int TotalFieldCount => Plan.Steps.SelectMany(step => step.Fields).Count();

    private int CompletedFieldCount => Plan.Steps
        .SelectMany(step => step.Fields)
        .Count(IsFieldComplete);

    private string DoneCountText => Loc["TmSigningFormRunner_DoneCount", CompletedFieldCount, TotalFieldCount];

    private string ProgressAriaLabel => DoneCountText;
}
