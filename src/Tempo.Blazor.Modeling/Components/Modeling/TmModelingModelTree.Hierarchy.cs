using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>
/// The ParentId hierarchy concern of <see cref="TmModelingModelTree"/> (Fáze 12): the memoized,
/// cycle-safe structure build, the filtered level-annotated view, node toggling and the APG
/// hierarchy keyboard handling. The flat SemanticType view and the shared render gate live in
/// TmModelingModelTree.razor.cs.
/// </summary>
public partial class TmModelingModelTree
{

    // ── Phase 12: hierarchy by ParentId ───────────────────────────────────────
    private readonly HashSet<string> _collapsedElementIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _filterCollapsedElementIds = new(StringComparer.Ordinal);
    private IReadOnlyCollection<string>? _lastCollapsedElementIdsParameter;
    private bool _pendingCollapsedElementIdsChanged;
    private IReadOnlyList<ModelingElementDto>? _hierarchySource;
    private ModelingTreeGrouping? _hierarchyResolvedGrouping;
    private HierarchyStructure? _hierarchy;
    private IReadOnlyList<ModelingElementDto>? _filteredHierarchySource;
    private string? _filteredHierarchyFilterText;
    private string? _filteredHierarchyCultureName;
    private List<HierarchyViewNode> _filteredHierarchy = [];

    /// <summary>
    /// How the tree structures its elements (Fáze 12). <see cref="ModelingTreeGrouping.Auto"/>
    /// (the default) renders the <see cref="ModelingElementDto.ParentId"/> hierarchy when at
    /// least one element resolves to a valid parent and keeps the SemanticType groups for flat
    /// models — a model without parents looks and behaves exactly as before.
    /// </summary>
    [Parameter] public ModelingTreeGrouping Grouping { get; set; } = ModelingTreeGrouping.Auto;

    /// <summary>
    /// Optional two-way binding of the collapsed ELEMENT ids of the hierarchy view (Fáze 12) —
    /// a separate key space from <see cref="CollapsedGroups"/> (group keys are semantic types,
    /// these are element ids; no migration between them). Seeded whenever the host hands over a
    /// new collection; every user toggle raises <see cref="CollapsedElementIdsChanged"/> with a
    /// fresh snapshot. The parameter is NEVER assigned inside the component.
    /// </summary>
    [Parameter] public IReadOnlyCollection<string>? CollapsedElementIds { get; set; }

    /// <summary>Raised when the user collapses or expands a hierarchy node (not while a filter drives the transient state).</summary>
    [Parameter] public EventCallback<IReadOnlyCollection<string>> CollapsedElementIdsChanged { get; set; }

    /// <summary>Toggles a hierarchy node from any activation path (chevron click, Enter, arrows).
    /// Mirrors <see cref="ToggleGroupAsync"/>: a transient set while filtering, the stored
    /// <see cref="CollapsedElementIds"/> state (and its two-way raise) otherwise.</summary>
    private async Task ToggleNodeAsync(string elementId)
    {
        _activeTreeItemId = $"node:{elementId}";
        _renderPending = true;
        if (FilterActive)
        {
            if (!_filterCollapsedElementIds.Remove(elementId))
            {
                _filterCollapsedElementIds.Add(elementId);
            }

            return;
        }

        if (!_collapsedElementIds.Remove(elementId))
        {
            _collapsedElementIds.Add(elementId);
        }

        if (CollapsedElementIdsChanged.HasDelegate)
        {
            await CollapsedElementIdsChanged.InvokeAsync(_collapsedElementIds.ToArray());
        }
    }

    /// <summary>Whether a hierarchy node renders expanded: forced while filtering (matches keep
    /// their ancestors visible) unless the user collapsed it in the transient set.</summary>
    private bool IsNodeExpanded(HierarchyViewNode node)
        => FilterActive
            ? !_filterCollapsedElementIds.Contains(node.Element.Id)
            : !_collapsedElementIds.Contains(node.Element.Id);

    private bool HasChildren(HierarchyViewNode node) => node.Children.Count > 0;

    private async Task HandleHierarchyNodeKeyDownAsync(HierarchyViewNode node, KeyboardEventArgs args)
    {
        switch (args.Key)
        {
            case "ArrowDown":
                await FocusRelativeEntryAsync("node", node.Element.Id, 1);
                break;
            case "ArrowUp":
                await FocusRelativeEntryAsync("node", node.Element.Id, -1);
                break;
            case "Home":
                await FocusFlatEntryAsync(0);
                break;
            case "End":
                await FocusFlatEntryAsync(^1);
                break;
            case "ArrowRight" when HasChildren(node) && !IsNodeExpanded(node):
                await ToggleNodeAsync(node.Element.Id);
                break;
            case "ArrowRight" when HasChildren(node):
                await FocusRelativeEntryAsync("node", node.Element.Id, 1);
                break;
            case "ArrowLeft" when HasChildren(node) && IsNodeExpanded(node):
                await ToggleNodeAsync(node.Element.Id);
                break;
            case "ArrowLeft":
                await FocusParentOfAsync(node);
                break;
            case "Insert":
                if (!args.Repeat && CanInsertIntoPreview)
                {
                    await HandleInsertRequestedAsync(node.Element);
                }

                break;
            case "Enter" when args.CtrlKey:
                if (!args.Repeat && CanInsertIntoPreview)
                {
                    await HandleInsertRequestedAsync(node.Element);
                }

                break;
        }
    }

    /// <summary>Focuses the nearest visible ANCESTOR of the node (the APG "move to parent" on
    /// ArrowLeft when the node cannot collapse) — resolved over the rendered flat list, so a
    /// filtered-out chain skips to the visible ancestor.</summary>
    private async Task FocusParentOfAsync(HierarchyViewNode node)
    {
        var parentId = node.Element.ParentId;
        var structure = HierarchyStructureOrNull();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (!string.IsNullOrWhiteSpace(parentId) && structure is not null && seen.Add(parentId))
        {
            if (GetFlatVisibleEntries().Any(entry => entry.Kind == "node" && entry.Key == parentId))
            {
                await FocusElementAsync(parentId);
                return;
            }

            structure.TreeParentOf.TryGetValue(parentId, out parentId);
        }
    }

    private int CountRenderedHierarchyNodes() => CountRenderedHierarchyNodes(_filteredHierarchy);

    private int CountRenderedHierarchyNodes(IReadOnlyList<HierarchyViewNode> nodes)
    {
        var count = 0;
        foreach (var node in nodes)
        {
            count++;
            if (IsNodeExpanded(node))
            {
                count += CountRenderedHierarchyNodes(node.Children);
            }
        }

        return count;
    }


    // ── Phase 12: hierarchy by ParentId ───────────────────────────────────────

    /// <summary>Whether the tree renders the ParentId hierarchy instead of the SemanticType
    /// groups. <see cref="ModelingTreeGrouping.Auto"/> resolves to hierarchy when at least one
    /// element carries a VALID ParentId (resolvable to another element of the model).</summary>
    private bool IsHierarchyMode => EffectiveGrouping == ModelingTreeGrouping.Hierarchy;

    private ModelingTreeGrouping EffectiveGrouping
    {
        get
        {
            if (Grouping != ModelingTreeGrouping.Auto)
            {
                return Grouping;
            }

            var structure = HierarchyStructureOrNull();
            return structure is { HasValidParent: true }
                ? ModelingTreeGrouping.Hierarchy
                : ModelingTreeGrouping.SemanticType;
        }
    }

    /// <summary>The memoized hierarchy structure (children by parent, cycle-safe roots). Built
    /// ONCE per <see cref="Elements"/> reference; never rebuilt on a parent render pass.</summary>
    private HierarchyStructure? HierarchyStructureOrNull()
    {
        if (Grouping == ModelingTreeGrouping.SemanticType)
        {
            // The flat mode never needs the hierarchy projection — skip the build entirely.
            return null;
        }

        if (ReferenceEquals(Elements, _hierarchySource)
            && _hierarchy is not null
            && _hierarchyResolvedGrouping == Grouping)
        {
            return _hierarchy;
        }

        _hierarchySource = Elements;
        _hierarchyResolvedGrouping = Grouping;
        _hierarchy = BuildHierarchy(Elements);
        return _hierarchy;
    }

    private static HierarchyStructure BuildHierarchy(IReadOnlyList<ModelingElementDto> elements)
    {
        var byId = new Dictionary<string, ModelingElementDto>(StringComparer.Ordinal);
        foreach (var element in elements)
        {
            if (!string.IsNullOrWhiteSpace(element.Id))
            {
                byId.TryAdd(element.Id, element);
            }
        }

        // Resolvable parent edges only: unknown parents (and self-parents) cannot own a child.
        var parentOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var element in elements)
        {
            if (string.IsNullOrWhiteSpace(element.ParentId)
                || element.ParentId == element.Id
                || !byId.ContainsKey(element.ParentId))
            {
                continue;
            }

            parentOf[element.Id] = element.ParentId;
        }

        // Cycle safety WITHOUT recursion: an element renders at the root when walking up its
        // ancestor chain revisits a node (a cycle a→b→c→a) — deterministically, whatever the
        // input order. Elements hanging off a cycle get the same treatment (their chain never
        // reaches a root), so nothing is rendered twice and nothing can recurse forever.
        var resolvesToRoot = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var id in parentOf.Keys)
        {
            ResolveToRoot(id, parentOf, resolvesToRoot);
        }

        var children = new Dictionary<string, List<ModelingElementDto>>(StringComparer.Ordinal);
        var roots = new List<ModelingElementDto>();
        foreach (var element in elements)
        {
            if (string.IsNullOrWhiteSpace(element.Id))
            {
                continue;
            }

            // A resolvable parent edge (the parent EXISTS) counts as a valid ParentId for the
            // Auto grouping — even when the edge participates in a cycle, which is broken to
            // the root below. Only unknown parents and self-parents are not valid.
            var hasParentEdge = parentOf.TryGetValue(element.Id, out var parent);
            if (hasParentEdge && resolvesToRoot.GetValueOrDefault(element.Id, true))
            {
                if (!children.TryGetValue(parent, out var list))
                {
                    children[parent] = list = [];
                }

                list.Add(element);
            }
            else
            {
                roots.Add(element);
            }
        }

        var hasValidParent = parentOf.Count > 0;

        // The tree EDGES: only edges whose child resolves to a root. Cycle members (and anything
        // hanging off a cycle) keep their declared ParentId in <paramref name="parentOf"/> for
        // diagnostics, but render AT THE ROOT — the view below keys off this map, so the broken
        // elements can never form a nested (unreachable) loop.
        var treeParentOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (childId, parentId) in parentOf)
        {
            if (resolvesToRoot.GetValueOrDefault(childId, true))
            {
                treeParentOf[childId] = parentId;
            }
        }

        return new HierarchyStructure(byId, roots, parentOf, treeParentOf, children, hasValidParent);
    }

    private static bool ResolveToRoot(string id, Dictionary<string, string> parentOf, Dictionary<string, bool> memo)
    {
        if (memo.TryGetValue(id, out var cached))
        {
            return cached;
        }

        var path = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = id;
        bool result;
        while (true)
        {
            if (memo.TryGetValue(current, out var inner))
            {
                result = inner;
                break;
            }

            if (!parentOf.TryGetValue(current, out var parent))
            {
                result = true;
                break;
            }

            if (parent == current || !seen.Add(parent))
            {
                result = false; // a self-parent or a revisited ancestor: a cycle
                break;
            }

            path.Add(current);
            current = parent;
        }

        foreach (var visited in path)
        {
            memo[visited] = result;
        }

        return result;
    }

    /// <summary>Number of elements matching the filter in the hierarchy projection.</summary>
    private int FilteredMatchCount => FilteredHierarchy.Sum(node => CountMatches(node));

    private static int CountMatches(HierarchyViewNode node)
        => (node.IsMatch ? 1 : 0) + node.Children.Sum(CountMatches);

    /// <summary>The filtered, level-annotated hierarchy view: matches PLUS their ancestors, in
    /// model order, with aria setsize/posinset computed among the VISIBLE siblings. Memoized on
    /// (Elements reference, filter text, culture); collapse state does not change the view —
    /// only how much of it renders.</summary>
    private List<HierarchyViewNode> FilteredHierarchy
    {
        get
        {
            var cultureName = CultureInfo.CurrentUICulture.Name;
            if (ReferenceEquals(Elements, _filteredHierarchySource)
                && string.Equals(_filterText, _filteredHierarchyFilterText, StringComparison.Ordinal)
                && string.Equals(cultureName, _filteredHierarchyCultureName, StringComparison.Ordinal))
            {
                return _filteredHierarchy;
            }

            var structure = HierarchyStructureOrNull()
                ?? new HierarchyStructure(new Dictionary<string, ModelingElementDto>(StringComparer.Ordinal), [], [], [], [], HasValidParent: false);
            _filteredHierarchySource = Elements;
            _filteredHierarchyFilterText = _filterText;
            _filteredHierarchyCultureName = cultureName;
            _filteredHierarchy = BuildFilteredHierarchy(structure, Elements);
            return _filteredHierarchy;
        }
    }

    private List<HierarchyViewNode> BuildFilteredHierarchy(HierarchyStructure structure, IReadOnlyList<ModelingElementDto> elements)
    {
        // 1. The visible set: every match PLUS all of its ancestors (walked with the cycle-safe
        //    TREE parent map) — the chain stays rendered and expanded while the filter is active.
        var visible = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in elements)
        {
            if (string.IsNullOrWhiteSpace(element.Id) || !structure.ById.ContainsKey(element.Id))
            {
                continue;
            }

            if (MatchesFilter(element))
            {
                visible.Add(element.Id);
                var current = element.Id;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                while (structure.TreeParentOf.TryGetValue(current, out var parent)
                       && parent != current
                       && seen.Add(parent))
                {
                    visible.Add(parent);
                    current = parent;
                }
            }
        }

        // 2. View nodes in model order, bucketed under their visible parent. The visible set is
        //    closed under ancestors, so a visible node's parent is visible or invalid (view
        //    root). Bucketing keys off the TREE edges — cycle members were demoted to roots.
        var viewById = new Dictionary<string, HierarchyViewNode>(StringComparer.Ordinal);
        var viewRoots = new List<HierarchyViewNode>();
        var childrenByParent = new Dictionary<string, List<HierarchyViewNode>>(StringComparer.Ordinal);
        var levelMemo = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var element in elements)
        {
            if (element.Id is null || !visible.Contains(element.Id))
            {
                continue;
            }

            var node = new HierarchyViewNode
            {
                Element = element,
                IsMatch = MatchesFilter(element),
                Level = LevelOf(element.Id, structure.TreeParentOf, levelMemo)
            };
            viewById[element.Id] = node;
            if (structure.TreeParentOf.TryGetValue(element.Id, out var parent) && visible.Contains(parent))
            {
                if (!childrenByParent.TryGetValue(parent, out var list))
                {
                    childrenByParent[parent] = list = [];
                }

                list.Add(node);
            }
            else
            {
                viewRoots.Add(node);
            }
        }

        // 3. Attach children + sibling metrics (no recursion: the buckets already hold the
        //    node references in model order).
        AttachHierarchyMetrics(viewRoots, childrenByParent);
        foreach (var children in childrenByParent.Values)
        {
            AttachHierarchyMetrics(children, childrenByParent);
        }

        return viewRoots;
    }

    private static void AttachHierarchyMetrics(
        List<HierarchyViewNode> siblings,
        Dictionary<string, List<HierarchyViewNode>> childrenByParent)
    {
        for (var index = 0; index < siblings.Count; index++)
        {
            var node = siblings[index];
            node.PositionInSet = index + 1;
            node.SetSize = siblings.Count;
            node.Children = childrenByParent.TryGetValue(node.Element.Id, out var children)
                ? children
                : [];
        }
    }

    /// <summary>Hierarchy depth (1-based) resolved iteratively with a memo — never recursion,
    /// so a deep (or adversarially deep) hierarchy cannot overflow the stack. A REVISITED
    /// ancestor (a cycle that survived into the view data) cuts the chain: the walk answers 1
    /// instead of looping forever.</summary>
    private static int LevelOf(string id, Dictionary<string, string> parentOf, Dictionary<string, int> memo)
    {
        var path = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { id };
        var current = id;
        var baseLevel = 1;
        while (true)
        {
            if (memo.TryGetValue(current, out var cached))
            {
                baseLevel = cached;
                break;
            }

            if (!parentOf.TryGetValue(current, out var parent) || parent == current || !seen.Add(parent))
            {
                // No parent, a self-parent or a revisited ancestor: the chain ends here.
                baseLevel = 1;
                break;
            }

            path.Add(current);
            current = parent;
        }

        var level = baseLevel;
        for (var index = path.Count - 1; index >= 0; index--)
        {
            level++;
            memo[path[index]] = level;
        }

        return memo.TryGetValue(id, out var resolved) ? resolved : baseLevel;
    }

    /// <summary>Hierarchy nodes with children carry the branch modifier: the button grid gains
    /// the chevron + child-count tracks (leaves keep the flat three-track row).</summary>
    private string GetHierarchyNodeClass(HierarchyViewNode node)
        => HasChildren(node)
            ? $"{GetNodeClass(node.Element)} tm-modeling-model-tree__node--branch"
            : GetNodeClass(node.Element);

    /// <summary>Inline custom property that carries the node's depth to the token-driven
    /// indentation rule — the CSS computes (level − 1) × the --tm-space-4 step, so no pixel is
    /// hard-coded in either language.</summary>
    private static string GetHierarchyLevelStyle(HierarchyViewNode node)
        => $"--tm-modeling-tree-level: {node.Level}";

    /// <summary>Hierarchy parent nodes name their direct child count (the localized plural) on
    /// top of the flat node label; leaves use the flat label unchanged.</summary>
    private string GetHierarchyNodeAriaLabel(HierarchyViewNode node)
    {
        var baseLabel = GetNodeAriaLabel(node.Element);
        return HasChildren(node)
            ? $"{baseLabel}, {Loc.Plural("TmModelingModelTree_ChildCount", node.Children.Count)}"
            : baseLabel;
    }

    /// <summary>The chevron tooltip of a hierarchy disclosure ("Collapse {0}" / "Expand {0}",
    /// {0} = the node's display name).</summary>
    private string GetNodeDisclosureTitle(string displayName, bool expanded)
        => string.Format(CultureInfo.CurrentCulture,
            Loc[expanded ? "TmModelingModelTree_CollapseNode" : "TmModelingModelTree_ExpandNode"],
            displayName);

    /// <summary>Hierarchy DOM id of a node's children container (the disclosure's aria-controls
    /// target) — per-instance prefix plus the element id.</summary>
    private string GetHierarchyChildrenId(string elementId)
        => $"{_idPrefix}-children-{elementId}";


    /// <summary>A node of the memoized hierarchy structure: element, its children in model
    /// order. Built once per <see cref="Elements"/> reference.</summary>
    private sealed record HierarchyStructure(
        Dictionary<string, ModelingElementDto> ById,
        List<ModelingElementDto> Roots,
        Dictionary<string, string> ParentOf,
        Dictionary<string, string> TreeParentOf,
        Dictionary<string, List<ModelingElementDto>> Children,
        bool HasValidParent);

    /// <summary>A rendered node of the filtered hierarchy view with its ARIA metrics. Mutable
    /// because the sibling metrics are assigned in a flat second pass (never recursion).</summary>
    private sealed class HierarchyViewNode
    {
        public required ModelingElementDto Element { get; init; }

        public bool IsMatch { get; init; }

        public int Level { get; init; }

        public int PositionInSet { get; set; }

        public int SetSize { get; set; }

        public IReadOnlyList<HierarchyViewNode> Children { get; set; } = [];
    }
}
