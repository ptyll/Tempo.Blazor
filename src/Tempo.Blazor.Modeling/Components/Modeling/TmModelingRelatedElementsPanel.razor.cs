using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>
/// Fáze 13: the relations body of the modeling inspector — for the selected element it lists the
/// OUTGOING and INCOMING relationships (the relationship type plus the counterpart element with
/// its semantic-type icon) and for the selected relationship the source and target as navigable
/// rows. Every activation routes through the editor's existing selection paths (the same
/// <c>SelectElementAsync</c>/<c>SelectRelationshipAsync</c> flow an issue or an edge pick uses) —
/// the panel itself never edits the model (MO2). Read-only presentation, like
/// <see cref="TmModelingInspector"/>.
/// </summary>
/// <remarks>
/// Performance (M26): the by-element relationship index is built ONCE per
/// <see cref="Relationships"/> collection reference (memoized in <see cref="EnsureIndex"/>), so
/// re-renders and selection changes on a large model never re-scan the relationship list.
/// </remarks>
public partial class TmModelingRelatedElementsPanel
{
    /// <summary>The selected modeling element. Takes precedence when a relationship is supplied too.</summary>
    [Parameter] public ModelingElementDto? Element { get; set; }

    /// <summary>The selected modeling relationship; renders its source and target as navigable rows.</summary>
    [Parameter] public ModelingRelationshipDto? Relationship { get; set; }

    /// <summary>Elements used to resolve counterpart display names and semantic-type icons.</summary>
    [Parameter] public IReadOnlyList<ModelingElementDto> Elements { get; set; } = [];

    /// <summary>The loaded model's relationships — the source of the outgoing/incoming index.</summary>
    [Parameter] public IReadOnlyList<ModelingRelationshipDto> Relationships { get; set; } = [];

    /// <summary>Raised when the user activates a counterpart (or a source/target row) to select that element.</summary>
    [Parameter] public EventCallback<ModelingElementDto> OnElementSelected { get; set; }

    /// <summary>Raised when the user activates a relationship type to select that relationship.</summary>
    [Parameter] public EventCallback<ModelingRelationshipDto> OnRelationshipSelected { get; set; }

    /// <summary>Additional CSS class applied to the panel root.</summary>
    [Parameter] public string? Class { get; set; }

    private string RootClass => string.Join(" ", new[] { "tm-modeling-related", Class }.Where(value => !string.IsNullOrWhiteSpace(value)));

    /// <summary>What the panel presents: the element groups, the relationship's source/target rows or the empty state.</summary>
    private string Kind => Element is not null ? "element" : Relationship is not null ? "relationship" : "empty";

    // ── Memoized relationship index (M26) ────────────────────────────────────

    private IReadOnlyList<ModelingRelationshipDto>? _indexedRelationships;
    private readonly Dictionary<string, List<ModelingRelationshipDto>> _outgoingIndex = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<ModelingRelationshipDto>> _incomingIndex = new(StringComparer.Ordinal);
    private int _indexBuildCount;

    /// <summary>Test seam (InternalsVisibleTo): how many times the by-element index was built.</summary>
    internal int IndexBuildCount => _indexBuildCount;

    /// <summary>
    /// Builds the outgoing/incoming index once per <see cref="Relationships"/> reference. An
    /// unrelated re-render or a selection change reuses it; only a NEW collection rebuilds it.
    /// </summary>
    private void EnsureIndex()
    {
        if (ReferenceEquals(Relationships, _indexedRelationships))
        {
            return;
        }

        _outgoingIndex.Clear();
        _incomingIndex.Clear();

        foreach (var relationship in Relationships)
        {
            AddToIndex(_outgoingIndex, relationship.SourceElementId, relationship);
            AddToIndex(_incomingIndex, relationship.TargetElementId, relationship);
        }

        _indexedRelationships = Relationships;
        _indexBuildCount++;
    }

    private static void AddToIndex(Dictionary<string, List<ModelingRelationshipDto>> index, string? elementId, ModelingRelationshipDto relationship)
    {
        if (string.IsNullOrWhiteSpace(elementId))
        {
            return;
        }

        if (!index.TryGetValue(elementId, out var bucket))
        {
            bucket = [];
            index[elementId] = bucket;
        }

        bucket.Add(relationship);
    }

    // ── Memoized by-id element index (phase 13 review) ───────────────────────

    private IReadOnlyList<ModelingElementDto>? _indexedElements;
    private readonly Dictionary<string, ModelingElementDto> _elementsById = new(StringComparer.Ordinal);
    private int _elementIndexBuildCount;

    /// <summary>Test seam (InternalsVisibleTo): how many times the by-id element index was built.</summary>
    internal int ElementIndexBuildCount => _elementIndexBuildCount;

    /// <summary>
    /// Builds the by-id element lookup ONCE per <see cref="Elements"/> reference, so resolving a
    /// row's counterpart is a dictionary hit instead of a linear <c>FirstOrDefault</c> scan per
    /// row per render (M26: a 500-row model keeps the tab body inside the render budget).
    /// </summary>
    private void EnsureElementIndex()
    {
        if (ReferenceEquals(Elements, _indexedElements))
        {
            return;
        }

        _elementsById.Clear();
        foreach (var element in Elements)
        {
            if (!string.IsNullOrWhiteSpace(element.Id) && !_elementsById.ContainsKey(element.Id))
            {
                _elementsById.Add(element.Id, element);
            }
        }

        _indexedElements = Elements;
        _elementIndexBuildCount++;
    }

    /// <summary>The outgoing and incoming groups of the selected element (in input order, no re-sort).</summary>
    private (IReadOnlyList<ModelingRelationshipDto> Outgoing, IReadOnlyList<ModelingRelationshipDto> Incoming) GroupsForElement
    {
        get
        {
            EnsureIndex();
            var elementId = Element!.Id;
            _outgoingIndex.TryGetValue(elementId, out var outgoing);
            _incomingIndex.TryGetValue(elementId, out var incoming);
            return ((IReadOnlyList<ModelingRelationshipDto>?)outgoing ?? [], (IReadOnlyList<ModelingRelationshipDto>?)incoming ?? []);
        }
    }

    /// <summary>Resolves the counterpart element of <paramref name="relationship"/> for the given
    /// direction: the TARGET of an outgoing relationship, the SOURCE of an incoming one.</summary>
    private ModelingElementDto? ResolveCounterpart(ModelingRelationshipDto relationship, bool outgoing)
    {
        EnsureElementIndex();
        var counterpartId = outgoing ? relationship.TargetElementId : relationship.SourceElementId;
        return string.IsNullOrWhiteSpace(counterpartId) || !_elementsById.TryGetValue(counterpartId, out var element)
            ? null
            : element;
    }

    /// <summary>Display name of a counterpart: the name, the raw id, else the localized Unknown fallback.</summary>
    private string CounterpartDisplay(ModelingElementDto? counterpart)
        => !string.IsNullOrWhiteSpace(counterpart?.Name)
            ? counterpart!.Name
            : !string.IsNullOrWhiteSpace(counterpart?.Id)
                ? counterpart!.Id
                : Loc["TmModelingEditor_Unknown"];

    /// <summary>Display text of a relationship row: the relationship type, falling back to the name and then Unknown.</summary>
    private string RelationshipTypeDisplay(ModelingRelationshipDto relationship)
        => !string.IsNullOrWhiteSpace(relationship.RelationshipType)
            ? relationship.RelationshipType
            : !string.IsNullOrWhiteSpace(relationship.Name)
                ? relationship.Name
                : Loc["TmModelingEditor_Unknown"];

    private string RootAriaLabel => Loc["TmModelingEditor_Relations"];
    private string TitleId { get; } = $"tm-modeling-related-title-{Guid.NewGuid():N}";
    private string OutgoingGroupId { get; } = $"tm-modeling-related-outgoing-{Guid.NewGuid():N}";
    private string IncomingGroupId { get; } = $"tm-modeling-related-incoming-{Guid.NewGuid():N}";
    private string SourceGroupId { get; } = $"tm-modeling-related-source-{Guid.NewGuid():N}";
    private string TargetGroupId { get; } = $"tm-modeling-related-target-{Guid.NewGuid():N}";
}
