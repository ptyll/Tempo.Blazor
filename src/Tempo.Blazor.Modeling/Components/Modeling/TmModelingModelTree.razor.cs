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
    private ModelingElementDto? _lastSelectedElementParameter;
    private string? _activeTreeItemId;
    private bool _pendingCollapsedGroupsChanged;
    private ModelingInteropModule? _interopModule;
    private ElementReference _treeRoot;

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

    /// <summary>Optional two-way binding of the collapsed group keys. Seeded from when the instance
    /// is given a new collection; every user toggle raises <see cref="CollapsedGroupsChanged"/> with
    /// a fresh snapshot. The parameter is NEVER assigned inside the component.</summary>
    [Parameter] public IReadOnlyCollection<string>? CollapsedGroups { get; set; }

    /// <summary>Raised when the user collapses or expands a group (not while a filter drives the transient state).</summary>
    [Parameter] public EventCallback<IReadOnlyCollection<string>> CollapsedGroupsChanged { get; set; }

    /// <summary>Additional CSS class applied to the tree root.</summary>
    [Parameter] public string? Class { get; set; }

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

    /// <summary>Number of elements MATCHING the current filter across all groups — the phase-1
    /// contract reported on <c>data-visible-count</c> (M10/M23/M26). Collapsed groups still count:
    /// the value never drops to zero just because the single matching group is collapsed.</summary>
    private int MatchingElementCount => FilteredGroups.Sum(group => group.Elements.Count);

    /// <summary>Number of element treeitems actually RENDERED (nodes of expanded groups), reported
    /// on <c>data-rendered-count</c> — the observable distinction the old single attribute hid.</summary>
    private int RenderedNodeCount
        => FilteredGroups.Where(group => IsGroupExpanded(group.Key)).Sum(group => group.Elements.Count);

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
        => activeTreeItemId == $"element:{elementId}" ? "0" : "-1";

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

            if (!string.IsNullOrWhiteSpace(SelectedElement.Id))
            {
                _activeTreeItemId = $"element:{SelectedElement.Id}";
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
        }
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

        if (SelectedElement is null
            || string.IsNullOrWhiteSpace(SelectedElement.Id)
            || string.Equals(_lastScrolledElementId, SelectedElement.Id, StringComparison.Ordinal))
        {
            return;
        }

        _lastScrolledElementId = SelectedElement.Id;
        await ScrollSelectedElementIntoViewAsync(SelectedElement.Id);
    }

    private void HandleFilterChanged(ChangeEventArgs args)
    {
        _filterText = args.Value?.ToString() ?? string.Empty;
        // The filter view has its own transient collapse state: editing the filter starts a new
        // transient session instead of inheriting the previous one.
        _filterCollapsedGroups.Clear();
    }

    private async Task SelectElementAsync(ModelingElementDto element)
    {
        SelectedElement = element;
        _activeTreeItemId = $"element:{element.Id}";
        await SelectedElementChanged.InvokeAsync(element);
        await OnElementSelected.InvokeAsync(element);
    }

    /// <summary>Toggles a group from any activation path (click, Enter, Space, arrows). While a
    /// filter is active the toggle only moves the TRANSIENT set; the stored state (and the
    /// two-way binding) stay untouched until the filter clears.</summary>
    private async Task ToggleGroupAsync(string groupKey)
    {
        _activeTreeItemId = $"group:{groupKey}";
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

    /// <summary>Keys on a NODE treeitem: Up/Down/Home/End traverse the flat list, Left focuses
    /// the node's group header. Right is a no-op. Enter/Space stay NATIVE (the node is a real
    /// button) — the stopPropagation fence keeps them away from the emulating group treeitem.</summary>
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
        }
    }

    /// <summary>The flat visible list: every header followed by its rendered nodes.</summary>
    private List<TreeNavEntry> GetFlatVisibleEntries()
    {
        var entries = new List<TreeNavEntry>();
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
