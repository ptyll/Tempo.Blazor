using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>
/// Issue filter/sort state of <see cref="TmModelingEditor"/> (Fáze 11): the editor OWNS the
/// issue panel's filter text, severity set and sort mode so the state survives a dock
/// collapse, a tablet sheet open/close and a mobile tab switch (each of which unmounts the
/// panel instance and re-seeds it from these fields). Split out of the main code-behind to
/// keep it under the 1200-line source ceiling (N270/F13).
/// </summary>
public partial class TmModelingEditor
{
    /// <summary>Active severity filter handed to every band's issue panel; null = all severities.</summary>
    private IReadOnlyCollection<ModelingIssueSeverity>? _issueSeverityFilter;

    /// <summary>Issue text filter handed to every band's issue panel (seed/echo two-way contract).</summary>
    private string? _issueFilterText;

    /// <summary>Issue sort mode handed to every band's issue panel.</summary>
    private ModelingIssueSortMode _issueSortMode = ModelingIssueSortMode.Input;

    /// <summary>Receives the panel's <c>SeverityFilterChanged</c> raise (the two-way echo).</summary>
    private Task SetIssueSeverityFilterAsync(IReadOnlyCollection<ModelingIssueSeverity>? severityFilter)
    {
        _issueSeverityFilter = severityFilter;
        return Task.CompletedTask;
    }

    /// <summary>Receives the panel's <c>FilterTextChanged</c> raise (the two-way echo).</summary>
    private Task SetIssueFilterTextAsync(string? filterText)
    {
        _issueFilterText = filterText;
        return Task.CompletedTask;
    }

    /// <summary>Receives the panel's <c>SortModeChanged</c> raise (the two-way echo).</summary>
    private Task SetIssueSortModeAsync(ModelingIssueSortMode sortMode)
    {
        _issueSortMode = sortMode;
        return Task.CompletedTask;
    }
}
