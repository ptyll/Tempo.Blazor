using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>Displays validation and generation issues for a modeling document.</summary>
public partial class TmModelingIssuePanel
{
    /// <summary>
    /// Issues to render, in input order. Severity only colors the rows — the panel never
    /// re-sorts the list (severity filtering/sorting is backlog).
    /// </summary>
    [Parameter] public IReadOnlyList<ModelingIssueDto> Issues { get; set; } = [];

    /// <summary>Raised when the user selects an issue that points to a model element or relationship.</summary>
    [Parameter] public EventCallback<ModelingIssueDto> OnIssueSelected { get; set; }

    /// <summary>Additional CSS class applied to the panel root.</summary>
    [Parameter] public string? Class { get; set; }

    private string RootClass => string.Join(" ", new[] { "tm-modeling-issue-panel", Class }.Where(value => !string.IsNullOrWhiteSpace(value)));

    /// <summary>
    /// An issue is navigable only when it points at a model element or relationship —
    /// sourceless issues render as non-interactive list items.
    /// </summary>
    private static bool IsNavigable(ModelingIssueDto issue)
        => !string.IsNullOrWhiteSpace(issue.SourceElementId)
            || !string.IsNullOrWhiteSpace(issue.SourceRelationshipId);

    private Task SelectIssueAsync(ModelingIssueDto issue)
    {
        if (!IsNavigable(issue))
        {
            return Task.CompletedTask;
        }

        return OnIssueSelected.InvokeAsync(issue);
    }

    private string GetIssueClass(ModelingIssueDto issue)
        => $"tm-modeling-issue-panel__item tm-modeling-issue-panel__item--{GetSeverityKey(issue)}";

    private string GetIconClass(ModelingIssueDto issue)
        => $"tm-modeling-issue-panel__icon tm-modeling-issue-panel__icon--{GetSeverityKey(issue)}";

    private string GetSeverityClass(ModelingIssueDto issue)
        => $"tm-modeling-issue-panel__severity tm-modeling-issue-panel__severity--{GetSeverityKey(issue)}";

    private string GetFixClass(ModelingIssueDto issue)
        => $"tm-modeling-issue-panel__fix tm-modeling-issue-panel__fix--{GetSeverityKey(issue)}";

    private static string GetSeverityKey(ModelingIssueDto issue)
        => issue.Severity switch
        {
            ModelingIssueSeverity.Error => "error",
            ModelingIssueSeverity.Warning => "warning",
            _ => "info"
        };

    private string GetSeverityLabel(ModelingIssueDto issue)
        => issue.Severity switch
        {
            ModelingIssueSeverity.Error => Loc["TmModelingIssuePanel_Error"],
            ModelingIssueSeverity.Warning => Loc["TmModelingIssuePanel_Warning"],
            _ => Loc["TmModelingIssuePanel_Info"]
        };

    /// <summary>Distinct icon per severity: alert circle (error), alert triangle (warning), info (info).</summary>
    private static string GetSeverityIcon(ModelingIssueDto issue)
        => issue.Severity switch
        {
            ModelingIssueSeverity.Error => IconNames.AlertCircle,
            ModelingIssueSeverity.Warning => IconNames.AlertTriangle,
            _ => IconNames.Info
        };

    private string GetMessage(ModelingIssueDto issue)
        => string.IsNullOrWhiteSpace(issue.Message)
            ? Loc["TmModelingIssuePanel_UnspecifiedMessage"]
            : issue.Message;
}
