using System.Globalization;
using System.Text;
using Tempo.Blazor.Localization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>Displays semantic modeling elements grouped by type with live filtering and selection.
/// Implements the APG tree pattern: type groups are <c>role="treeitem"</c> parents
/// (<c>aria-level="1"</c>, <c>aria-expanded</c>) owning a <c>role="group"</c> of level-2 element
/// treeitems, navigated over ONE flat visible list with roving tabindex and arrow keys.</summary>
public partial class TmModelingModelTree : IAsyncDisposable
{
    private string _filterText = string.Empty;
    private string? _lastScrolledElementId;
    private IReadOnlyList<ModelingElementDto>? _filteredGroupsSource;
    private IReadOnlyList<ModelingElementGroup> _filteredGroups = [];
    private string? _filteredGroupsFilterText;
    private string? _filteredGroupsCultureName;
    private readonly HashSet<string> _collapsedGroups = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _filterCollapsedGroups = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyCollection<string>? _lastCollapsedGroupsParameter;
    private string? _lastFilterTextParameter;
    private ModelingElementDto? _lastSelectedElementParameter;
    private string? _activeTreeItemId;
    private bool _pendingCollapsedGroupsChanged;
    private ModelingInteropModule? _interopModule;
    private ElementReference _treeRoot;

    // ── Render gate (final code review, Wireframe render-gate pattern) ───────
    // The tree re-renders only when something RENDER-AFFECTING changed: a parameter of the
    // snapshot below, an internal interaction (filter edit, group toggle, selection, keyboard
    // focus move) or the very first render. Every other parent pass — the editor re-rendering
    // for its own state — diffs to "no change" and is skipped (M26: a 200-row tree must not
    // re-render on every parent pass).
    private bool _firstRender = true;
    private bool _renderPending;
    private (IReadOnlyList<ModelingElementDto>? Elements,
             ModelingElementDto? SelectedElement,
             IReadOnlyCollection<string>? CollapsedGroups,
             IReadOnlyCollection<string>? CollapsedElementIds,
             ModelingTreeGrouping Grouping,
             string FilterText,
             bool AllowNodeDrop,
             bool CanInsert,
             bool ShowTitle,
             string? Class,
             string? TestIdPrefix,
             string? DataTestId,
             string Culture) _renderSnapshot;

    /// <summary>Compares the render-affecting parameter surface against the last snapshot; the
    /// culture rides along because every visible label resolves through it. The test-id pair
    /// rides along too — both rewrite every <c>data-testid</c> the tree paints.</summary>
    private bool RenderSurfaceChanged()
    {
        var snapshot = (Elements, SelectedElement, CollapsedGroups, CollapsedElementIds, Grouping, _filterText,
            AllowNodeDrop, CanInsertIntoPreview, ShowTitle, Class, TestIdPrefix, DataTestId,
            CultureInfo.CurrentUICulture.Name);
        var changed = !snapshot.Equals(_renderSnapshot);
        _renderSnapshot = snapshot;
        return changed;
    }

    /// <summary>Test seam (InternalsVisibleTo): how many passes actually rendered — the render
    /// gate's observable (the Wireframe toolbox pattern).</summary>
    internal int RenderCount { get; private set; }

    /// <inheritdoc />
    protected override bool ShouldRender()
    {
        var render = _firstRender || _renderPending;
        _firstRender = false;
        _renderPending = false;
        return render;
    }

    /// <summary>Per-instance DOM id prefix — two trees on one page must never share content ids
    /// (the old static slug collapsed different group keys onto the same id).</summary>
    private readonly string _idPrefix = $"tm-mmt-{Guid.NewGuid():N}";

    /// <summary>DOM id of the header title, referenced by the <c>role="tree"</c> element's
    /// <c>aria-labelledby</c> (per-instance, so two trees never collide).</summary>
    private string TreeTitleId => $"{_idPrefix}-title";

    /// <summary>Scoped accessor for the shared, disposed-guarded module wrapper (the [Inject]
    /// runtime is only available after construction, so the wrapper is created lazily).</summary>
    private ModelingInteropModule Interop => _interopModule ??= new ModelingInteropModule(JSRuntime);

    /// <summary>Test seam (InternalsVisibleTo): the lazily created module wrapper, or null before first use.</summary>
    internal ModelingInteropModule? TestInteropModule => _interopModule;

    /// <summary>JavaScript runtime used to reveal externally selected tree nodes inside the scrollable tree.</summary>
    [Inject] public IJSRuntime JSRuntime { get; set; } = default!;

    /// <summary>Elements rendered by the tree. They are grouped by <see cref="ModelingElementDto.SemanticType"/>.</summary>
    [Parameter] public IReadOnlyList<ModelingElementDto> Elements { get; set; } = [];

    /// <summary>Currently selected element.</summary>
    [Parameter] public ModelingElementDto? SelectedElement { get; set; }

    /// <summary>Raised when <see cref="SelectedElement"/> changes.</summary>
    [Parameter] public EventCallback<ModelingElementDto?> SelectedElementChanged { get; set; }

    /// <summary>Raised when the user selects an element node.</summary>
    [Parameter] public EventCallback<ModelingElementDto> OnElementSelected { get; set; }

    /// <summary>Raised when the user starts dragging an element node. Reserved for canvas drop support.</summary>
    [Parameter] public EventCallback<ModelingElementDto> OnElementDragStarted { get; set; }

    /// <summary>Raised when the user ends a drag operation from a tree node.</summary>
    [Parameter] public EventCallback OnElementDragEnded { get; set; }

    /// <summary>
    /// Whether the tree offers the non-drag "Insert into preview" action (Fáze 6, WCAG 2.5.7).
    /// Default <c>true</c> mirrors the hosting editor's <see cref="TmModelingEditor.AllowNodeDrop"/>:
    /// when the host opts out of tree reuse the action disappears with the drop zone, so no
    /// alternative path offers what the drop zone refuses.
    /// </summary>
    [Parameter] public bool AllowNodeDrop { get; set; } = true;

    /// <summary>
    /// Raised when the user activates the row-level "Insert into preview" action. The HOST
    /// computes the drop geometry: the tree knows nothing about the document or its viewport,
    /// so it raises the request and the hosting editor routes it through the preview's existing
    /// drop path at the viewport center. Keyboard- and touch-reachable by construction (a
    /// native button), which is the point of the alternative.
    /// </summary>
    [Parameter] public EventCallback<ModelingElementDto> OnInsertIntoPreview { get; set; }

    /// <summary>Optional two-way binding of the collapsed group keys. Seeded from when the instance
    /// is given a new collection; every user toggle raises <see cref="CollapsedGroupsChanged"/> with
    /// a fresh snapshot. The parameter is NEVER assigned inside the component.</summary>
    [Parameter] public IReadOnlyCollection<string>? CollapsedGroups { get; set; }

    /// <summary>Raised when the user collapses or expands a group (not while a filter drives the transient state).</summary>
    [Parameter] public EventCallback<IReadOnlyCollection<string>> CollapsedGroupsChanged { get; set; }

    /// <summary>
    /// Optional two-way binding of the live filter text (Fáze 6 review fix). The parameter SEEDS
    /// the internal filter whenever the host hands over a new value and is never assigned inside
    /// the component — user edits update the working copy, clear the transient filter-collapse
    /// set and raise <see cref="FilterTextChanged"/>. The hosting editor owns the field, so the
    /// filter survives a tree remount (a layout flip).
    /// </summary>
    [Parameter] public string? FilterText { get; set; }

    /// <summary>Raised when the user edits the filter input (not when the host re-seeds the parameter), enabling <c>@bind-FilterText</c>.</summary>
    [Parameter] public EventCallback<string?> FilterTextChanged { get; set; }

    /// <summary>Additional CSS class applied to the tree root.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>
    /// Whether the tree renders its own heading block (title + element count). Fáze 6 review:
    /// the hosting editor hides it inside the tablet drawer, where the shell's sheet title
    /// already says "Model tree" — the search field always renders (it is the live filter).
    /// When the block is absent the <c>role="tree"</c> element is named by <c>aria-label</c>
    /// instead of the (dangling) <c>aria-labelledby</c>. Default <c>true</c>.
    /// </summary>
    [Parameter] public bool ShowTitle { get; set; } = true;

    private string RootClass => string.Join(" ", new[] { "tm-modeling-model-tree", Class }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private IReadOnlyList<ModelingElementGroup> FilteredGroups
    {
        get
        {
            var cultureName = CultureInfo.CurrentUICulture.Name;
            if (ReferenceEquals(Elements, _filteredGroupsSource)
                && string.Equals(_filterText, _filteredGroupsFilterText, StringComparison.Ordinal)
                && string.Equals(cultureName, _filteredGroupsCultureName, StringComparison.Ordinal))
            {
                return _filteredGroups;
            }

            _filteredGroupsSource = Elements;
            _filteredGroupsFilterText = _filterText;
            _filteredGroupsCultureName = cultureName;
            _filteredGroups = Elements
                .Where(MatchesFilter)
                .GroupBy(element => NormalizeGroupKey(element.SemanticType), StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => new ModelingElementGroup(
                    group.Key,
                    GetGroupLabel(group.Key),
                    group
                        .OrderBy(element => GetDisplayName(element), StringComparer.CurrentCultureIgnoreCase)
                        .ThenBy(element => element.Id, StringComparer.OrdinalIgnoreCase)
                        .ToArray()))
                .ToArray();
            return _filteredGroups;
        }
    }

    /// <summary>Number of elements MATCHING the current filter (M10/M23/M26). Collapsed groups
    /// and collapsed hierarchy nodes still count: the value never drops to zero just because
    /// the only matching subtree is collapsed.</summary>
    private int MatchingElementCount => IsHierarchyMode ? FilteredMatchCount : FilteredGroups.Sum(group => group.Elements.Count);

    /// <summary>Number of element treeitems actually RENDERED (nodes of expanded groups /
    /// expanded hierarchy branches), reported on <c>data-rendered-count</c> — the observable
    /// distinction the old single attribute hid.</summary>
    private int RenderedNodeCount
        => IsHierarchyMode ? CountRenderedHierarchyNodes() : FilteredGroups.Where(group => IsGroupExpanded(group.Key)).Sum(group => group.Elements.Count);

    /// <summary>True while a filter text is active: toggles then operate on a transient set
    /// (<see cref="_filterCollapsedGroups"/>) so the user's stored state comes back when the
    /// filter clears.</summary>
    private bool FilterActive => !string.IsNullOrWhiteSpace(_filterText);

    /// <summary>A group renders expanded while filtering (unless collapsed in the transient set)
    /// or when the user has not collapsed it.</summary>
    private bool IsGroupExpanded(string groupKey)
        => FilterActive
            ? !_filterCollapsedGroups.Contains(groupKey)
            : !_collapsedGroups.Contains(groupKey);

    /// <summary>Roving-tabindex state: only the active treeitem carries <c>tabindex="0"</c>.
    /// Resolved against the CURRENT flat visible list — an entry that disappeared (its group
    /// collapsed) falls back to the first visible entry.</summary>
    private string ResolveActiveTreeItemId()
    {
        var entries = GetFlatVisibleEntries();
        if (entries.Count == 0)
        {
            return string.Empty;
        }

        if (_activeTreeItemId is not null && entries.Any(entry => GetEntryId(entry) == _activeTreeItemId))
        {
            return _activeTreeItemId;
        }

        return GetEntryId(entries[0]);
    }

    private string GetGroupTabIndex(string groupKey, string activeTreeItemId)
        => activeTreeItemId == $"group:{groupKey}" ? "0" : "-1";

    private string GetNodeTabIndex(string elementId, string activeTreeItemId)
        => activeTreeItemId == ElementEntryId(elementId) ? "0" : "-1";

    /// <summary>The roving-tabindex key of an ELEMENT row: the hierarchy view keys its flat
    /// entries <c>node:{id}</c> while the SemanticType groups key <c>element:{id}</c>. One
    /// helper so the click and external-selection paths can never drift from the flat list
    /// (phase 12 review: the external selection keyed <c>element:</c> in hierarchy mode and no
    /// node ever matched the roving state).</summary>
    private string ElementEntryId(string elementId)
        => IsHierarchyMode ? $"node:{elementId}" : $"element:{elementId}";

    /// <inheritdoc />
    /// <remarks>
    /// Fáze 6 review fix (M26): the editor's two-way echo of our own
    /// <see cref="FilterTextChanged"/> raise arrives as a <see cref="FilterText"/> parameter
    /// equal to the internal filter — re-seeding from it would be a no-op, but the value IS
    /// the last one the HOST provided, so it must be recorded as such
    /// (<see cref="_lastFilterTextParameter"/>) BEFORE the parameter is applied. Otherwise a
    /// later host RESET to that same value (clearing the filter after the user typed it)
    /// compares equal to the stale parameter in <see cref="OnParametersSet"/> and never
    /// re-seeds. The parameter itself flows through unchanged: the working copy already
    /// matches, and the property must end up holding the host's value for the comparison
    /// to stay meaningful.
    /// </remarks>
    public override Task SetParametersAsync(ParameterView parameters)
    {
        if (parameters.TryGetValue<string?>(nameof(FilterText), out var incoming)
            && string.Equals(incoming, _filterText, StringComparison.Ordinal))
        {
            _lastFilterTextParameter = incoming;
        }

        return base.SetParametersAsync(parameters);
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        base.OnParametersSet();

        // Two-way collapsed groups: seed the internal state whenever the host hands over a NEW
        // collection (same instance = host rerender, keep the internal state). Never assign back.
        if (!ReferenceEquals(CollapsedGroups, _lastCollapsedGroupsParameter))
        {
            _lastCollapsedGroupsParameter = CollapsedGroups;
            _collapsedGroups.Clear();
            if (CollapsedGroups is not null)
            {
                foreach (var groupKey in CollapsedGroups)
                {
                    if (!string.IsNullOrWhiteSpace(groupKey))
                    {
                        _collapsedGroups.Add(groupKey);
                    }
                }
            }

            _renderPending = true;
        }

        // Two-way collapsed hierarchy nodes: the same seed contract as CollapsedGroups, but a
        // SEPARATE key space (element ids, never group keys — no migration between them).
        if (!ReferenceEquals(CollapsedElementIds, _lastCollapsedElementIdsParameter))
        {
            _lastCollapsedElementIdsParameter = CollapsedElementIds;
            _collapsedElementIds.Clear();
            if (CollapsedElementIds is not null)
            {
                foreach (var elementId in CollapsedElementIds)
                {
                    if (!string.IsNullOrWhiteSpace(elementId))
                    {
                        _collapsedElementIds.Add(elementId);
                    }
                }
            }

            _renderPending = true;
        }

        // Two-way filter text (Fáze 6 review fix): seed the working copy whenever the host hands
        // over a new value — the comparison runs against the LAST value seen from the host (the
        // echo path records it in SetParametersAsync), so a host RESET to that value re-seeds
        // even though the parameter itself was already the stale one. A host edit (or the
        // editor's own field) wins; user edits flow OUT through FilterTextChanged and come back
        // as the same value (no re-seed).
        if (!string.Equals(FilterText, _lastFilterTextParameter, StringComparison.Ordinal))
        {
            _lastFilterTextParameter = FilterText;
            _filterText = FilterText ?? string.Empty;
            _renderPending = true;
        }

        // External selection inside a collapsed group auto-reveals it, so the scroll target can
        // render on the upcoming pass; the scroll itself happens once the node exists. The same
        // new-selection signal moves the roving tabindex onto the selected node and — when the
        // reveal actually changed the stored set — queues a CollapsedGroupsChanged raise (an
        // OnParametersSet cannot await, so the two-way binding is refreshed after render).
        if (!ReferenceEquals(SelectedElement, _lastSelectedElementParameter)
            && SelectedElement is not null)
        {
            _lastSelectedElementParameter = SelectedElement;
            _renderPending = true;

            if (!string.IsNullOrWhiteSpace(SelectedElement.Id))
            {
                _activeTreeItemId = ElementEntryId(SelectedElement.Id);
            }

            if (!string.IsNullOrWhiteSpace(SelectedElement.SemanticType))
            {
                var groupKey = NormalizeGroupKey(SelectedElement.SemanticType);
                if (_collapsedGroups.Remove(groupKey))
                {
                    _pendingCollapsedGroupsChanged = true;
                }

                _filterCollapsedGroups.Remove(groupKey);
            }

            // Hierarchy mode: reveal the WHOLE ancestor chain of the externally selected element
            // so the node can render (and scroll into view) on the upcoming pass. The walk keys
            // off the TREE edges (cycle members render at the root, so they have no tree parent).
            var structure = HierarchyStructureOrNull();
            if (structure is not null
                && !string.IsNullOrWhiteSpace(SelectedElement.Id)
                && structure.ById.ContainsKey(SelectedElement.Id))
            {
                var current = SelectedElement.Id;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                while (structure.TreeParentOf.TryGetValue(current, out var parent)
                       && parent != current
                       && seen.Add(parent))
                {
                    if (_collapsedElementIds.Remove(parent))
                    {
                        _pendingCollapsedElementIdsChanged = true;
                    }

                    _filterCollapsedElementIds.Remove(parent);
                    current = parent;
                }
            }
        }

        // The render-gate snapshot (final code review): every render-affecting parameter in one
        // comparison — a parent pass that changed none of them renders nothing.
        if (RenderSurfaceChanged())
        {
            _renderPending = true;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The render-gate flags reset here — the SYNC callback, which the render pipeline runs
    /// before the next <see cref="SetParametersAsync"/> can arm them again. (ShouldRender is not
    /// consulted for the very first render, so a flag armed by the first OnParametersSet would
    /// otherwise leak into the second pass and force a spurious re-render.)
    /// </remarks>
    protected override void OnAfterRender(bool firstRender)
    {
        base.OnAfterRender(firstRender);
        _firstRender = false;
        _renderPending = false;
        RenderCount++;
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await RegisterDragDataTransferBridgeAsync();
        }

        // Flush the auto-reveal collapsed-groups raise queued by OnParametersSet (which cannot
        // await) — the two-way binding must observe the revealed group too.
        if (_pendingCollapsedGroupsChanged)
        {
            _pendingCollapsedGroupsChanged = false;
            if (CollapsedGroupsChanged.HasDelegate)
            {
                await CollapsedGroupsChanged.InvokeAsync(_collapsedGroups.ToArray());
            }
        }

        if (_pendingCollapsedElementIdsChanged)
        {
            _pendingCollapsedElementIdsChanged = false;
            if (CollapsedElementIdsChanged.HasDelegate)
            {
                await CollapsedElementIdsChanged.InvokeAsync(_collapsedElementIds.ToArray());
            }
        }

        if (SelectedElement is null
            || string.IsNullOrWhiteSpace(SelectedElement.Id)
            || string.Equals(_lastScrolledElementId, SelectedElement.Id, StringComparison.Ordinal))
        {
            return;
        }

        _lastScrolledElementId = SelectedElement.Id;
        await ScrollSelectedElementIntoViewAsync(SelectedElement.Id);
    }

    private async Task HandleFilterChanged(ChangeEventArgs args)
    {
        _filterText = args.Value?.ToString() ?? string.Empty;
        // The filter view has its own transient collapse state: editing the filter starts a new
        // transient session instead of inheriting the previous one (a USER edit only — a host
        // re-seed goes through OnParametersSet and keeps the session). Both key spaces: the
        // flat groups' keys AND the hierarchy's element ids.
        _filterCollapsedGroups.Clear();
        _filterCollapsedElementIds.Clear();
        _renderPending = true;
        await FilterTextChanged.InvokeAsync(_filterText);
    }

    private async Task SelectElementAsync(ModelingElementDto element)
    {
        SelectedElement = element;
        _activeTreeItemId = ElementEntryId(element.Id);
        _renderPending = true;
        await SelectedElementChanged.InvokeAsync(element);
        await OnElementSelected.InvokeAsync(element);
    }

    /// <summary>Toggles a group from any activation path (click, Enter, Space, arrows). While a
    /// filter is active the toggle only moves the TRANSIENT set; the stored state (and the
    /// two-way binding) stay untouched until the filter clears.</summary>
    private async Task ToggleGroupAsync(string groupKey)
    {
        _activeTreeItemId = $"group:{groupKey}";
        _renderPending = true;
        if (FilterActive)
        {
            if (!_filterCollapsedGroups.Remove(groupKey))
            {
                _filterCollapsedGroups.Add(groupKey);
            }

            return;
        }

        if (!_collapsedGroups.Remove(groupKey))
        {
            _collapsedGroups.Add(groupKey);
        }

        if (CollapsedGroupsChanged.HasDelegate)
        {
            await CollapsedGroupsChanged.InvokeAsync(_collapsedGroups.ToArray());
        }
    }

    /// <summary>Keys on a GROUP treeitem over the ONE flat visible list
    /// [header, its nodes if expanded, next header…]: Up/Down/Home/End traverse, Right expands a
    /// collapsed header or focuses its first child, Left collapses an expanded one. Enter
    /// (keydown, guarded against auto-repeat) and Space (keyup — the native button split) toggle.
    /// </summary>
    private async Task HandleGroupKeyDownAsync(string groupKey, KeyboardEventArgs args)
    {
        switch (args.Key)
        {
            case "ArrowDown":
                await FocusRelativeEntryAsync("group", groupKey, 1);
                break;
            case "ArrowUp":
                await FocusRelativeEntryAsync("group", groupKey, -1);
                break;
            case "Home":
                await FocusFlatEntryAsync(0);
                break;
            case "End":
                await FocusFlatEntryAsync(^1);
                break;
            case "ArrowRight":
                if (IsGroupExpanded(groupKey))
                {
                    await FocusFirstNodeOfGroupAsync(groupKey);
                }
                else
                {
                    await ToggleGroupAsync(groupKey);
                }

                break;
            case "ArrowLeft":
                if (IsGroupExpanded(groupKey))
                {
                    await ToggleGroupAsync(groupKey);
                }

                break;
            case "Enter":
                // Emulated activation on a non-native focusable: Enter on keydown, NOT repeated.
                if (!args.Repeat)
                {
                    await ToggleGroupAsync(groupKey);
                }

                break;
        }
    }

    /// <summary>Space emulates the native button split: activation on KEYUP.</summary>
    private async Task HandleGroupKeyUpAsync(string groupKey, KeyboardEventArgs args)
    {
        if (args.Key == " ")
        {
            await ToggleGroupAsync(groupKey);
        }
    }

    /// <summary>Whether the non-drag "Insert into preview" action is offered: the same gate the
    /// row button follows (the host's AllowNodeDrop switch AND a listener for the request).</summary>
    private bool CanInsertIntoPreview => AllowNodeDrop && OnInsertIntoPreview.HasDelegate;

    /// <summary>Keys on a NODE treeitem: Up/Down/Home/End traverse the flat list, Left focuses
    /// the node's group header. Right is a no-op. Enter/Space stay NATIVE (the node is a real
    /// button) — the stopPropagation fence keeps them away from the emulating group treeitem.
    /// Fáze 6 review: Insert (and Ctrl+Enter) on a node is the keyboard path of the non-drag
    /// "Insert into preview" action — the row button is a reveal-on-hover sibling, so a keyboard
    /// user must not have to tab to it (WCAG 2.5.7).</summary>
    private async Task HandleNodeKeyDownAsync(ModelingElementDto element, KeyboardEventArgs args)
    {
        switch (args.Key)
        {
            case "ArrowDown":
                await FocusRelativeEntryAsync("element", element.Id, 1);
                break;
            case "ArrowUp":
                await FocusRelativeEntryAsync("element", element.Id, -1);
                break;
            case "Home":
                await FocusFlatEntryAsync(0);
                break;
            case "End":
                await FocusFlatEntryAsync(^1);
                break;
            case "ArrowLeft":
                await FocusGroupHeaderAsync(NormalizeGroupKey(element.SemanticType));
                break;
            case "Insert":
                if (!args.Repeat && CanInsertIntoPreview)
                {
                    await HandleInsertRequestedAsync(element);
                }

                break;
            case "Enter" when args.CtrlKey:
                // Ctrl+Enter is the mnemonic twin of Insert (a node's plain Enter stays the
                // NATIVE selection click). Emulated here because a native button never
                // activates on Ctrl+Enter.
                if (!args.Repeat && CanInsertIntoPreview)
                {
                    await HandleInsertRequestedAsync(element);
                }

                break;
        }
    }

    /// <summary>The flat visible list: every header followed by its rendered nodes. Hierarchy
    /// mode walks the <see cref="FilteredHierarchy"/> view with the collapse state applied — a
    /// collapsed branch contributes only its OWN treeitem (phase 12 review blocker: the walk
    /// always descended, so ArrowDown from a collapsed parent landed inside the closed branch
    /// and End skipped past visible nodes to hidden ones).</summary>
    private List<TreeNavEntry> GetFlatVisibleEntries()
    {
        var entries = new List<TreeNavEntry>();
        if (IsHierarchyMode)
        {
            AppendVisibleHierarchyEntries(FilteredHierarchy, entries);
            return entries;
        }

        foreach (var group in FilteredGroups)
        {
            entries.Add(new TreeNavEntry("group", group.Key));
            if (IsGroupExpanded(group.Key))
            {
                entries.AddRange(group.Elements.Select(element => new TreeNavEntry("element", element.Id)));
            }
        }

        return entries;
    }

    private void AppendVisibleHierarchyEntries(IReadOnlyList<HierarchyViewNode> nodes, List<TreeNavEntry> entries)
    {
        foreach (var node in nodes)
        {
            entries.Add(new TreeNavEntry("node", node.Element.Id));
            if (IsNodeExpanded(node))
            {
                AppendVisibleHierarchyEntries(node.Children, entries);
            }
        }
    }

    private async Task FocusRelativeEntryAsync(string kind, string key, int offset)
    {
        var entries = GetFlatVisibleEntries();
        var index = entries.FindIndex(entry => entry.Kind == kind && string.Equals(entry.Key, key, StringComparison.Ordinal));
        if (index < 0)
        {
            return;
        }

        await FocusFlatEntryAsync(index + offset);
    }

    private async Task FocusFirstNodeOfGroupAsync(string groupKey)
    {
        var entries = GetFlatVisibleEntries();
        var index = entries.FindIndex(entry => entry.Kind == "group" && string.Equals(entry.Key, groupKey, StringComparison.Ordinal));
        if (index < 0 || index + 1 >= entries.Count || entries[index + 1].Kind != "element")
        {
            return;
        }

        await FocusFlatEntryAsync(index + 1);
    }

    private async Task FocusFlatEntryAsync(Index index)
    {
        var entries = GetFlatVisibleEntries();
        if (entries.Count == 0)
        {
            return;
        }

        var entry = entries[index.GetOffset(entries.Count)];
        _activeTreeItemId = GetEntryId(entry);
        _renderPending = true;
        if (entry.Kind == "group")
        {
            await FocusGroupHeaderAsync(entry.Key);
        }
        else
        {
            await FocusElementAsync(entry.Key);
        }
    }

    private async Task FocusGroupHeaderAsync(string groupKey)
    {
        _activeTreeItemId = $"group:{groupKey}";
        _renderPending = true;
        await Interop.InvokeVoidSafeAsync("focusTreeGroupHeader", _treeRoot, groupKey);
    }

    private async Task FocusElementAsync(string elementId)
        => await Interop.InvokeVoidSafeAsync("focusTreeElement", _treeRoot, elementId);

    /// <summary>Stable DOM id of a group's node list (the treeitem's aria-controls target) —
    /// per-instance prefix plus the group's position, not a lossy slug of the key.</summary>
    private string GetGroupContentId(int groupPosition)
        => $"{_idPrefix}-group-{groupPosition}";

    private Task HandleDragStartedAsync(ModelingElementDto element)
        => OnElementDragStarted.InvokeAsync(element);

    private Task HandleDragEndedAsync()
        => OnElementDragEnded.InvokeAsync();

    /// <summary>The non-drag alternative (Fáze 6): raise the insert request; the hosting editor
    /// owns the drop geometry and routes it through the preview's existing OnNodeDropped path.</summary>
    private Task HandleInsertRequestedAsync(ModelingElementDto element)
        => OnInsertIntoPreview.InvokeAsync(element);

    private async Task RegisterDragDataTransferBridgeAsync()
        => await Interop.InvokeVoidSafeAsync("registerTreeDragBridge");

    private async Task ScrollSelectedElementIntoViewAsync(string elementId)
        => await Interop.InvokeVoidSafeAsync("scrollTreeElementIntoView", _treeRoot, elementId);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_interopModule is not null)
        {
            await _interopModule.DisposeAsync();
        }
    }

    private bool IsSelected(ModelingElementDto element)
        => SelectedElement is not null
            && string.Equals(SelectedElement.Id, element.Id, StringComparison.Ordinal);

    private string GetNodeClass(ModelingElementDto element)
        => IsSelected(element)
            ? "tm-modeling-model-tree__node tm-modeling-model-tree__node--selected"
            : "tm-modeling-model-tree__node";

    private bool MatchesFilter(ModelingElementDto element)
    {
        var filter = NormalizeSearchText(_filterText);
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return SearchableValues(element)
            .Select(NormalizeSearchText)
            .Any(value => value.Contains(filter, StringComparison.OrdinalIgnoreCase));
    }

    private IEnumerable<string> SearchableValues(ModelingElementDto element)
    {
        yield return element.Id;
        yield return element.SourceId;
        yield return element.SourceType;
        yield return element.SourcePath;
        yield return element.SemanticType;
        yield return element.Name;
        yield return element.Description;

        foreach (var tag in element.Tags)
        {
            yield return tag;
        }
    }

    private string GetDisplayName(ModelingElementDto element)
        => string.IsNullOrWhiteSpace(element.Name)
            ? Loc["TmModelingModelTree_Unnamed"]
            : element.Name;

    private string GetNodeMetadata(ModelingElementDto element)
    {
        if (!string.IsNullOrWhiteSpace(element.SourcePath))
        {
            return element.SourcePath;
        }

        return string.IsNullOrWhiteSpace(element.Id)
            ? Loc["TmModelingModelTree_UnknownId"]
            : element.Id;
    }

    private string GetNodeAriaLabel(ModelingElementDto element)
        => $"{GetDisplayName(element)}, {GetNodeMetadata(element)}, {element.SemanticType}".Trim();

    /// <summary>The per-row "Insert into preview" name: the localized "{0}" pattern carries the
    /// element's display name (Fáze 6 review fix — the generic label could not tell two rows
    /// apart for a screen reader).</summary>
    private string GetInsertAriaLabel(ModelingElementDto element)
        => string.Format(CultureInfo.CurrentCulture, Loc["TmModelingModelTree_InsertIntoPreviewNamed"], GetDisplayName(element));

    private string GetGroupAriaLabel(ModelingElementGroup group)
        => $"{group.Label}, {Loc.Plural("TmModelingModelTree_Count", group.Elements.Count)}";

    private string GetGroupHeaderTitle(bool expanded)
        => Loc[expanded ? "TmModelingModelTree_CollapseGroup" : "TmModelingModelTree_ExpandGroup"];

    /// <summary>Localized semantic-type label when the host provides one
    /// (<c>TmModelingSemanticType_{key}</c>), otherwise the humanized camel/Pascal key
    /// ("applicationComponent" → "Application component").</summary>
    private string GetGroupLabel(string groupKey)
    {
        if (string.IsNullOrWhiteSpace(groupKey))
        {
            return Loc["TmModelingModelTree_UnknownType"];
        }

        return Loc.TryGetString($"TmModelingSemanticType_{groupKey}", out var localized)
            ? localized
            : HumanizeGroupKey(groupKey);
    }

    private static string HumanizeGroupKey(string groupKey)
    {
        var trimmed = groupKey.Trim();
        if (trimmed.Length == 0)
        {
            return trimmed;
        }

        var spaced = new StringBuilder(trimmed.Length + 8);
        for (var index = 0; index < trimmed.Length; index++)
        {
            var current = trimmed[index];
            if (index > 0 && char.IsUpper(current))
            {
                var previous = trimmed[index - 1];
                var next = index + 1 < trimmed.Length ? trimmed[index + 1] : default;
                var isBoundary = char.IsLower(previous)
                    || (char.IsUpper(previous) && next != default && char.IsLower(next));
                if (isBoundary)
                {
                    spaced.Append(' ');
                }
            }

            spaced.Append(current);
        }

        // Sentence case: capitalize the first character, lowercase the rest.
        var words = spaced.ToString();
        return string.Concat(words.AsSpan(0, 1).ToString().ToUpperInvariant(), words[1..].ToLowerInvariant());
    }

    private static string NormalizeGroupKey(string semanticType)
        => string.IsNullOrWhiteSpace(semanticType) ? string.Empty : semanticType.Trim();

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

    private static string GetEntryId(TreeNavEntry entry)
        => $"{entry.Kind}:{entry.Key}";

    private sealed record TreeNavEntry(string Kind, string Key);

    private sealed record ModelingElementGroup(string Key, string Label, IReadOnlyList<ModelingElementDto> Elements);
}
