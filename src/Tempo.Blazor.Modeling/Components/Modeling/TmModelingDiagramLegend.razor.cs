using Microsoft.AspNetCore.Components;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>
/// Fáze 15: the preview legend — a collapsible card explaining the notation shapes (stencil
/// icons + localized type labels) the generated document actually uses, grouped into Elements
/// and Relationships. The host supplies the entries (the shared
/// <see cref="ModelingNotationCatalog"/>, filtered to the document); the card is presentational.
/// Accessible: list semantics, decorative icons (<c>aria-hidden</c>) next to visible text, an
/// <c>aria-expanded</c>/<c>aria-controls</c> collapse toggle (<see cref="Collapsible"/>, off in
/// the mobile sheet). Positioned by the preview's stylesheet as an overlay card in the canvas's
/// top-right corner — it never participates in the canvas column's flow (the canvas keeps its
/// height) and never covers the status bar's zoom group.
/// </summary>
public partial class TmModelingDiagramLegend
{
    /// <summary>The legend entries (already filtered to the types present in the document).
    /// Null renders nothing — the host decided the legend has no data.</summary>
    [Parameter] public IReadOnlyList<ModelingNotationCatalogEntry>? Entries { get; set; }

    /// <summary>Whether the card starts expanded. The hosting editor passes the layout rule
    /// (desktop expanded, tablet collapsed — and collapsed above the editor's 12-node rule).
    /// Seeded ONCE — the user's own toggle owns the state afterwards.</summary>
    [Parameter] public bool DefaultExpanded { get; set; } = true;

    /// <summary>Whether the card renders its collapse toggle. Default <c>true</c>. The mobile
    /// legend sheet passes <c>false</c>: a bottom sheet cannot collapse its own content, and a
    /// toggle that does nothing would be an unusable control.</summary>
    [Parameter] public bool Collapsible { get; set; } = true;

    /// <summary>Additional CSS class.</summary>
    [Parameter] public string? Class { get; set; }

    private bool _expanded;
    private bool _expandedSeeded;
    private bool _lastDefaultExpanded;

    /// <summary>Per-instance id of the collapsible body: the toggle's aria-controls target.</summary>
    private string BodyId { get; } = $"tm-modeling-legend-{Guid.NewGuid():N}";

    /// <summary>Per-instance ids of the two group headings: each list is named by
    /// <c>aria-labelledby</c> (the headings are plain paragraphs, not a heading outline).</summary>
    private string ElementGroupId { get; } = $"tm-modeling-legend-{Guid.NewGuid():N}-elements";

    private string RelationshipGroupId { get; } = $"tm-modeling-legend-{Guid.NewGuid():N}-relationships";

    /// <summary>The element-type entries (Elements group).</summary>
    private IReadOnlyList<ModelingNotationCatalogEntry> ElementEntries
        => Entries?.Where(entry => !entry.IsRelationship).ToArray() ?? [];

    /// <summary>The relationship-type entries (Relationships group).</summary>
    private IReadOnlyList<ModelingNotationCatalogEntry> RelationshipEntries
        => Entries?.Where(entry => entry.IsRelationship).ToArray() ?? [];

    /// <summary>Whether the legend has nothing to explain (the localized empty text shows).</summary>
    private bool IsEmpty => ElementEntries.Count == 0 && RelationshipEntries.Count == 0;

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (!_expandedSeeded || _lastDefaultExpanded != DefaultExpanded)
        {
            _expandedSeeded = true;
            _lastDefaultExpanded = DefaultExpanded;
            _expanded = DefaultExpanded;
        }
    }

    private string RootClass => string.Join(" ", new[]
    {
        "tm-modeling-diagram-legend",
        Class
    }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private void ToggleExpanded() => _expanded = !_expanded;

    private string ToggleAriaLabel => _expanded
        ? Loc["TmModelingDiagramLegend_Hide"]
        : Loc["TmModelingDiagramLegend_Show"];

    private string StencilIconSize => "1rem";
}
