using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>Displays validation and generation issues for a modeling document.</summary>
public partial class TmModelingIssuePanel
{
    private string _filterText = string.Empty;
    private string? _lastFilterTextParameter;
    private ModelingIssueSortMode _sortMode = ModelingIssueSortMode.Input;
    private ModelingIssueSortMode _lastSortModeParameter;
    private HashSet<ModelingIssueSeverity>? _activeSeverities;
    private IReadOnlyCollection<ModelingIssueSeverity>? _lastSeverityFilterParameter;

    // ── Render gate (the TmModelingModelTree pattern): the panel re-renders only when a
    // render-affecting parameter or an internal interaction changed — a parent pass that
    // changed nothing (M26: 500-issue list) renders nothing. ────────────────────────────────
    private bool _firstRender = true;
    private bool _renderPending;
    private (IReadOnlyList<ModelingIssueDto> Issues,
             bool ShowFilterBar,
             bool Collapsed,
             bool ShowToggle,
             string? Class,
             IReadOnlyList<ModelingElementDto>? Elements,
             IReadOnlyList<ModelingRelationshipDto>? Relationships,
             string FilterText,
             string? SeverityKey,
             ModelingIssueSortMode SortMode,
             string Culture) _renderSnapshot;

    /// <summary>Test seam (InternalsVisibleTo): how many passes actually rendered.</summary>
    internal int RenderCount { get; private set; }

    /// <summary>Per-instance DOM id of the collapsible body — the toggle's aria-controls target.</summary>
    private readonly string _bodyId = $"tm-modeling-issues-body-{Guid.NewGuid():N}";

    /// <summary>
    /// Issues to render. The panel presents a FILTERED/SORTED projection of this list; the
    /// collection itself is never mutated. With <see cref="SortMode"/> = <see cref="ModelingIssueSortMode.Input"/>
    /// (the default) the rows follow the input order and the panel never re-sorts.
    /// </summary>
    [Parameter] public IReadOnlyList<ModelingIssueDto> Issues { get; set; } = [];

    /// <summary>Raised when the user selects an issue that points to a model element or relationship.</summary>
    [Parameter] public EventCallback<ModelingIssueDto> OnIssueSelected { get; set; }

    /// <summary>
    /// Whether the panel body (the issue list or the empty state) is collapsed to the header.
    /// Two-way bindable — the host owns the dock height around the collapsed/expanded row, so the
    /// state has to reach it. Default false (expanded); the state is the session's and survives
    /// re-renders. A collapsed dock hides the filter bar too (the bar lives inside the body).
    /// </summary>
    [Parameter] public bool Collapsed { get; set; }

    /// <summary>Raised when the user toggles the panel through the header button.</summary>
    [Parameter] public EventCallback<bool> CollapsedChanged { get; set; }

    /// <summary>
    /// Whether the header renders the collapse toggle. Default <c>true</c> (the docked dock).
    /// The mobile Issues TAB (Fáze 7 review) passes <c>false</c>: the tab body IS the whole
    /// scroll surface, so there is nothing to collapse into — the header stays a plain heading.
    /// </summary>
    [Parameter] public bool ShowToggle { get; set; } = true;

    /// <summary>
    /// Whether the filter bar (severity chips with counts, text search, sort select) renders
    /// above the list. Default <c>false</c> — the pre-Fáze 11 presentation; the hosting editor
    /// passes <c>true</c> on every band (desktop dock, tablet sheet, mobile tab). The bar
    /// renders only when there is something to filter (<see cref="Issues"/> is not empty).
    /// </summary>
    [Parameter] public bool ShowFilterBar { get; set; }

    /// <summary>
    /// Optional two-way binding of the active severity filter; <c>null</c> (or empty) means
    /// every severity. The parameter SEEDS the internal filter whenever the host hands over a
    /// new collection and is never assigned inside the component — chip toggles update the
    /// working copy and raise <see cref="SeverityFilterChanged"/>. The hosting editor owns the
    /// state so it survives dock collapse and panel remounts.
    /// </summary>
    [Parameter] public IReadOnlyCollection<ModelingIssueSeverity>? SeverityFilter { get; set; }

    /// <summary>Raised when the user toggles a severity chip (or clears the filters); enables <c>@bind-SeverityFilter</c>.</summary>
    [Parameter] public EventCallback<IReadOnlyCollection<ModelingIssueSeverity>?> SeverityFilterChanged { get; set; }

    /// <summary>
    /// Optional two-way binding of the text filter. The parameter SEEDS the internal filter
    /// whenever the host hands over a new value and is never assigned inside the component —
    /// the same seed/echo pattern as <see cref="TmModelingModelTree.FilterText"/> (including the
    /// echo-recording <see cref="SetParametersAsync"/> step, so a host RESET to the current
    /// value re-seeds). Matches the message, the category and the source element/relationship
    /// name — all case- and accent-insensitively.
    /// </summary>
    [Parameter] public string? FilterText { get; set; }

    /// <summary>Raised when the user edits the filter input (not when the host re-seeds the parameter), enabling <c>@bind-FilterText</c>.</summary>
    [Parameter] public EventCallback<string?> FilterTextChanged { get; set; }

    /// <summary>
    /// Sort mode of the rendered rows; <see cref="ModelingIssueSortMode.Input"/> (the default)
    /// never re-sorts. The host owns the state through <see cref="SortModeChanged"/>.
    /// </summary>
    [Parameter] public ModelingIssueSortMode SortMode { get; set; } = ModelingIssueSortMode.Input;

    /// <summary>Raised when the user picks a sort mode; enables <c>@bind-SortMode</c>.</summary>
    [Parameter] public EventCallback<ModelingIssueSortMode> SortModeChanged { get; set; }

    /// <summary>
    /// Optional element list of the loaded model — the source of display NAMES for the
    /// text search and the <see cref="ModelingIssueSortMode.Source"/> sort. Without it those
    /// features fall back to the raw source ids.
    /// </summary>
    [Parameter] public IReadOnlyList<ModelingElementDto>? Elements { get; set; }

    /// <summary>Optional relationship list — the source of relationship names (as <see cref="Elements"/>).</summary>
    [Parameter] public IReadOnlyList<ModelingRelationshipDto>? Relationships { get; set; }

    /// <summary>Additional CSS class applied to the panel root.</summary>
    [Parameter] public string? Class { get; set; }

    /// <inheritdoc />
    /// <remarks>
    /// Two-way echo recording (the Fáze 6 review fix contract): when an incoming two-way value
    /// equals the internal working copy it IS the last value the host provided — record it
    /// BEFORE the parameters are applied so a later host RESET to that same value compares
    /// against the fresh record and re-seeds instead of being mistaken for a stale echo.
    /// </remarks>
    public override Task SetParametersAsync(ParameterView parameters)
    {
        if (parameters.TryGetValue<string?>(nameof(FilterText), out var incomingText)
            && string.Equals(incomingText, _filterText, StringComparison.Ordinal))
        {
            _lastFilterTextParameter = incomingText;
        }

        if (parameters.TryGetValue<ModelingIssueSortMode>(nameof(SortMode), out var incomingSort)
            && incomingSort == _sortMode)
        {
            _lastSortModeParameter = incomingSort;
        }

        return base.SetParametersAsync(parameters);
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        base.OnParametersSet();

        // Two-way severity filter: seed the working copy whenever the host hands over a NEW
        // collection (same instance = host rerender, keep the internal state). Never assign back.
        if (!ReferenceEquals(SeverityFilter, _lastSeverityFilterParameter))
        {
            _lastSeverityFilterParameter = SeverityFilter;
            _activeSeverities = SeverityFilter is { Count: > 0 } ? [.. SeverityFilter] : null;
            _renderPending = true;
        }

        // Two-way filter text: seed the working copy whenever the host hands over a NEW value —
        // the comparison runs against the LAST value seen from the host (the echo path records
        // it in SetParametersAsync), so a host RESET to that value re-seeds. A user edit flows
        // OUT through FilterTextChanged and comes back as the same value (no re-seed loop).
        if (!string.Equals(FilterText, _lastFilterTextParameter, StringComparison.Ordinal))
        {
            _lastFilterTextParameter = FilterText;
            _filterText = FilterText ?? string.Empty;
            _renderPending = true;
        }

        if (SortMode != _lastSortModeParameter)
        {
            _lastSortModeParameter = SortMode;
            _sortMode = SortMode;
            _renderPending = true;
        }

        // The render-gate snapshot: every render-affecting parameter in one comparison — a
        // parent pass that changed none of them renders nothing (M26, 500-issue lists).
        if (RenderSurfaceChanged())
        {
            _renderPending = true;
        }
    }

    /// <inheritdoc />
    protected override void OnAfterRender(bool firstRender)
    {
        base.OnAfterRender(firstRender);
        _firstRender = false;
        _renderPending = false;
        RenderCount++;
    }

    /// <summary>Compares the render-affecting parameter surface against the last snapshot; the
    /// culture rides along because every visible label resolves through it.</summary>
    private bool RenderSurfaceChanged()
    {
        var snapshot = (Issues, ShowFilterBar, Collapsed, ShowToggle, Class, Elements, Relationships,
            _filterText, SeverityFilterKey, _sortMode, CultureInfo.CurrentUICulture.Name);
        var changed = !snapshot.Equals(_renderSnapshot);
        _renderSnapshot = snapshot;
        return changed;
    }

    /// <inheritdoc />
    protected override bool ShouldRender()
    {
        var render = _firstRender || _renderPending;
        _firstRender = false;
        _renderPending = false;
        return render;
    }

    private string RootClass => string.Join(" ", new[]
    {
        "tm-modeling-issue-panel",
        Collapsed ? "tm-modeling-issue-panel--collapsed" : null,
        Class
    }.Where(value => !string.IsNullOrWhiteSpace(value)));

    /// <summary>The body's grid: the filter bar gets its own auto row when it renders, the
    /// list keeps the scrolling remainder (without the bar the list is the single row).</summary>
    private string GetContentClass()
        => ShowsFilterBar
            ? "tm-modeling-issue-panel__content tm-modeling-issue-panel__content--with-filterbar"
            : "tm-modeling-issue-panel__content";

    /// <summary>Whether the filter bar renders this pass (opt-in AND something to filter).</summary>
    private bool ShowsFilterBar => ShowFilterBar && Issues.Count > 0;

    /// <summary>The localized accessible name of the header toggle (state-aware).</summary>
    private string ToggleLabel => Collapsed ? Loc["TmModelingIssuePanel_Expand"] : Loc["TmModelingIssuePanel_Collapse"];

    private Task ToggleCollapsedAsync() => CollapsedChanged.InvokeAsync(!Collapsed);

    // ── Filter bar state ─────────────────────────────────────────────────────

    /// <summary>Whether any filter (severity set or text) is currently narrowing the list.</summary>
    private bool IsFiltered => SeverityFilterKey is not null || _filterText.Length > 0;

    /// <summary>Canonical key of the active severity set — the memoization/render-snapshot field;
    /// <c>null</c> when every severity is visible.</summary>
    private string? SeverityFilterKey
        => _activeSeverities is { Count: > 0 } ? string.Join(",", _activeSeverities.OrderBy(severity => severity)) : null;

    /// <summary>Whether the header shows the visible-of-total formulation (some rows hidden).</summary>
    private bool ShowsFilteredCount => VisibleIssues.Count != Issues.Count;

    /// <summary>Visible count for the root <c>data-visible-issue-count</c> attribute.</summary>
    private int VisibleIssueCount => VisibleIssues.Count;

    /// <summary>Total count for the root <c>data-total-issue-count</c> attribute (and the legacy
    /// <c>data-issue-count</c>, which keeps reporting the TOTAL, not the visible count).</summary>
    private int TotalIssueCount => Issues.Count;

    /// <summary>
    /// The rendered rows — a memoized (issue, original-input-index) projection filtered by the
    /// severity/text state and ordered by <see cref="SortMode"/>. The key is the <see cref="Issues"/>
    /// REFERENCE plus the filter/sort state, the element/relationship references and the culture
    /// (M26: recomputing a 500-issue list is a single O(n log n) pass, and the memo keeps
    /// unrelated parent re-renders from paying for it).
    /// </summary>
    private IReadOnlyList<(ModelingIssueDto Issue, int Index)> VisibleIssues
    {
        get
        {
            var key = (Issues, _filterText, SeverityFilterKey, _sortMode,
                CultureInfo.CurrentUICulture.Name, Elements, Relationships);
            if (_visibleIssues is not null && key.Equals(_visibleIssuesKey))
            {
                return _visibleIssues;
            }

            var elementNames = Elements is { Count: > 0 }
                ? Elements.ToDictionary(element => element.Id, element => element.Name, StringComparer.Ordinal)
                : null;
            var relationshipNames = Relationships is { Count: > 0 }
                ? Relationships.ToDictionary(relationship => relationship.Id, relationship => relationship.Name, StringComparer.Ordinal)
                : null;
            var filter = NormalizeSearchText(_filterText);

            var rows = new List<(ModelingIssueDto, int)>(Issues.Count);
            for (var index = 0; index < Issues.Count; index++)
            {
                var issue = Issues[index];
                if (MatchesFilters(issue, elementNames, relationshipNames, filter))
                {
                    rows.Add((issue, index));
                }
            }

            _visibleIssues = SortRows(rows, elementNames, relationshipNames);
            _visibleIssuesKey = key;
            return _visibleIssues;
        }
    }

    private IReadOnlyList<(ModelingIssueDto, int)>? _visibleIssues;
    private (IReadOnlyList<ModelingIssueDto>, string, string?, ModelingIssueSortMode, string, IReadOnlyList<ModelingElementDto>?, IReadOnlyList<ModelingRelationshipDto>?) _visibleIssuesKey;

    /// <summary>Whether the issue survives the active severity set and text filter.</summary>
    private bool MatchesFilters(
        ModelingIssueDto issue,
        IReadOnlyDictionary<string, string>? elementNames,
        IReadOnlyDictionary<string, string>? relationshipNames,
        string filter)
    {
        if (_activeSeverities is { Count: > 0 } && !_activeSeverities.Contains(issue.Severity))
        {
            return false;
        }

        if (filter.Length == 0)
        {
            return true;
        }

        return SearchableValues(issue, elementNames, relationshipNames)
            .Select(NormalizeSearchText)
            .Any(value => value.Contains(filter, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The searchable fields: message, category, the raw source ids and the resolved
    /// source display names.</summary>
    private static IEnumerable<string> SearchableValues(
        ModelingIssueDto issue,
        IReadOnlyDictionary<string, string>? elementNames,
        IReadOnlyDictionary<string, string>? relationshipNames)
    {
        yield return issue.Message;
        yield return issue.Category;
        yield return issue.SourceElementId;
        yield return issue.SourceRelationshipId;
        if (TryResolveSourceName(issue, elementNames, relationshipNames, out var name))
        {
            yield return name;
        }
    }

    private IReadOnlyList<(ModelingIssueDto, int)> SortRows(
        List<(ModelingIssueDto Issue, int Index)> rows,
        IReadOnlyDictionary<string, string>? elementNames,
        IReadOnlyDictionary<string, string>? relationshipNames)
        => _sortMode switch
        {
            ModelingIssueSortMode.Severity => rows
                // LINQ OrderBy is stable: rows of the same severity keep their input order.
                .OrderBy(row => SeverityRank(row.Issue.Severity))
                .ToArray(),
            ModelingIssueSortMode.Source => rows
                .OrderBy(row => TryResolveSourceName(row.Issue, elementNames, relationshipNames, out var name) ? name : null, SourceNameComparer.Instance)
                .ThenBy(row => row.Index)
                .ToArray(),
            _ => rows
        };

    /// <summary>Source-name ordering: issues WITH a source come first ordered by the resolved
    /// display name (an unmatched source id falls back to the id itself); sourceless last.</summary>
    private sealed class SourceNameComparer : IComparer<string?>
    {
        public static readonly SourceNameComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            if (x is null && y is null)
            {
                return 0;
            }

            if (x is null)
            {
                return 1;
            }

            if (y is null)
            {
                return -1;
            }

            return string.Compare(x, y, StringComparison.CurrentCultureIgnoreCase);
        }
    }

    private static int SeverityRank(ModelingIssueSeverity severity) => severity switch
    {
        ModelingIssueSeverity.Error => 0,
        ModelingIssueSeverity.Warning => 1,
        _ => 2
    };

    /// <summary>Resolves the display name of the issue's source element/relationship; a source id
    /// without a matching entry falls back to the id itself. <c>false</c> = no source at all.</summary>
    private static bool TryResolveSourceName(
        ModelingIssueDto issue,
        IReadOnlyDictionary<string, string>? elementNames,
        IReadOnlyDictionary<string, string>? relationshipNames,
        out string name)
    {
        if (!string.IsNullOrWhiteSpace(issue.SourceElementId))
        {
            name = elementNames is not null && elementNames.TryGetValue(issue.SourceElementId, out var elementName)
                && !string.IsNullOrWhiteSpace(elementName)
                ? elementName
                : issue.SourceElementId;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(issue.SourceRelationshipId))
        {
            name = relationshipNames is not null && relationshipNames.TryGetValue(issue.SourceRelationshipId, out var relationshipName)
                && !string.IsNullOrWhiteSpace(relationshipName)
                ? relationshipName
                : issue.SourceRelationshipId;
            return true;
        }

        name = string.Empty;
        return false;
    }

    /// <summary>Accent- and case-normalized search form (the model tree's filter contract).</summary>
    private static string NormalizeSearchText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private async Task ToggleSeverityAsync(ModelingIssueSeverity severity)
    {
        var next = new HashSet<ModelingIssueSeverity>(_activeSeverities ?? []);
        if (!next.Remove(severity))
        {
            next.Add(severity);
        }

        _activeSeverities = next.Count > 0 ? next : null;
        _renderPending = true;
        await SeverityFilterChanged.InvokeAsync(_activeSeverities is { Count: > 0 } ? _activeSeverities.ToArray() : null);
    }

    private async Task HandleFilterTextChangedAsync(string? value)
    {
        _filterText = value ?? string.Empty;
        _renderPending = true;
        await FilterTextChanged.InvokeAsync(_filterText);
    }

    private async Task HandleSortModeChangedAsync(ModelingIssueSortMode value)
    {
        _sortMode = value;
        _renderPending = true;
        await SortModeChanged.InvokeAsync(value);
    }

    /// <summary>Clears both filters and reports them through the two-way bindings.</summary>
    private async Task ClearFiltersAsync()
    {
        _filterText = string.Empty;
        _activeSeverities = null;
        _renderPending = true;
        await FilterTextChanged.InvokeAsync(string.Empty);
        await SeverityFilterChanged.InvokeAsync(null);
    }

    private bool IsSeverityActive(ModelingIssueSeverity severity)
        => _activeSeverities is { Count: > 0 } && _activeSeverities.Contains(severity);

    private string GetChipClass(ModelingIssueSeverity severity)
        => $"tm-modeling-issue-panel__chip tm-modeling-issue-panel__chip--{GetSeverityKey(severity)}";
    private int CountBySeverity(ModelingIssueSeverity severity)
    {
        var count = 0;
        foreach (var issue in Issues)
        {
            if (issue.Severity == severity)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>An issue is navigable only when it points at a model element or relationship —
    /// sourceless issues render as non-interactive list items.</summary>
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
        => GetSeverityKey(issue.Severity);

    private static string GetSeverityKey(ModelingIssueSeverity severity)
        => severity switch
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

    /// <summary>The localized chip label for a severity (the row severity vocabulary reused).</summary>
    private string GetSeverityChipLabel(ModelingIssueSeverity severity)
        => severity switch
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
