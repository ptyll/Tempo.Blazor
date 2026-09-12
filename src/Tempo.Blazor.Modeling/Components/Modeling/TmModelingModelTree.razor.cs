using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>Displays semantic modeling elements grouped by type with live filtering and selection.</summary>
public partial class TmModelingModelTree
{
    private string _filterText = string.Empty;
    private string? _lastScrolledElementId;
    private IReadOnlyList<ModelingElementDto>? _filteredGroupsSource;
    private IReadOnlyList<ModelingElementGroup> _filteredGroups = [];
    private string? _filteredGroupsFilterText;
    private string? _filteredGroupsCultureName;

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

    private int VisibleElementCount => FilteredGroups.Sum(group => group.Elements.Count);

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await RegisterDragDataTransferBridgeAsync();
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
    }

    private async Task SelectElementAsync(ModelingElementDto element)
    {
        SelectedElement = element;
        await SelectedElementChanged.InvokeAsync(element);
        await OnElementSelected.InvokeAsync(element);
    }

    private async Task HandleNodeKeyDownAsync(ModelingElementDto element, KeyboardEventArgs args)
    {
        // No Enter/Space activation here: the node is a native <button>, so the
        // browser itself produces the activation click (Enter on keydown, Space
        // on keyup) which reaches SelectElementAsync via @onclick. Selecting
        // here as well fired OnElementSelected twice per key press.
        var visibleElements = FilteredGroups
            .SelectMany(group => group.Elements)
            .ToArray();
        if (visibleElements.Length == 0)
        {
            return;
        }

        var currentIndex = Array.FindIndex(visibleElements, item => string.Equals(item.Id, element.Id, StringComparison.Ordinal));
        if (currentIndex < 0)
        {
            currentIndex = 0;
        }

        var targetIndex = args.Key switch
        {
            "ArrowDown" or "ArrowRight" => Math.Min(currentIndex + 1, visibleElements.Length - 1),
            "ArrowUp" or "ArrowLeft" => Math.Max(currentIndex - 1, 0),
            "Home" => 0,
            "End" => visibleElements.Length - 1,
            _ => currentIndex
        };

        if (targetIndex == currentIndex)
        {
            return;
        }

        var target = visibleElements[targetIndex];
        await FocusElementAsync(target.Id);
    }

    private Task HandleDragStartedAsync(ModelingElementDto element)
        => OnElementDragStarted.InvokeAsync(element);

    private Task HandleDragEndedAsync()
        => OnElementDragEnded.InvokeAsync();

    private async Task RegisterDragDataTransferBridgeAsync()
    {
        await JSRuntime.InvokeVoidAsync("eval", """
            (() => {
                if (window.__tmModelingTreeDragBridgeRegistered) {
                    return;
                }

                window.__tmModelingTreeDragBridgeRegistered = true;
                document.addEventListener('dragstart', event => {
                    const node = event.target?.closest?.('[data-modeling-drag-element-id]');
                    if (!node || !event.dataTransfer) {
                        return;
                    }

                    const elementId = node.getAttribute('data-modeling-drag-element-id') || '';
                    window.__tmModelingDraggedElementId = elementId;
                    event.dataTransfer.effectAllowed = 'copy';
                    event.dataTransfer.setData('text/plain', elementId);
                    event.dataTransfer.setData('application/x-tempo-modeling-element-id', elementId);
                }, true);

                document.addEventListener('dragend', () => {
                    window.__tmModelingDraggedElementId = '';
                }, true);
            })();
            """);
    }

    private async Task ScrollSelectedElementIntoViewAsync(string elementId)
    {
        var serializedElementId = JsonSerializer.Serialize(elementId);
        await JSRuntime.InvokeVoidAsync("eval", $$"""
            (() => {
                const elementId = {{serializedElementId}};
                const escapedElementId = window.CSS?.escape
                    ? CSS.escape(elementId)
                    : elementId.replace(/["\\]/g, "\\$&");
                const node = document.querySelector(`[data-testid="modeling-model-tree"] [data-element-id="${escapedElementId}"]`);
                node?.scrollIntoView({ block: "nearest", inline: "nearest" });
            })();
            """);
    }

    private async Task FocusElementAsync(string elementId)
    {
        var serializedElementId = JsonSerializer.Serialize(elementId);
        await JSRuntime.InvokeVoidAsync("eval", $$"""
            (() => {
                const elementId = {{serializedElementId}};
                const escapedElementId = window.CSS?.escape
                    ? CSS.escape(elementId)
                    : elementId.replace(/["\\]/g, "\\$&");
                const node = document.querySelector(`[data-testid="modeling-model-tree"] [data-element-id="${escapedElementId}"]`);
                node?.scrollIntoView({ block: "nearest", inline: "nearest" });
                node?.focus({ preventScroll: true });
            })();
            """);
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

    private string GetGroupLabel(string groupKey)
        => string.IsNullOrWhiteSpace(groupKey)
            ? Loc["TmModelingModelTree_UnknownType"]
            : groupKey;

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

    private sealed record ModelingElementGroup(string Key, string Label, IReadOnlyList<ModelingElementDto> Elements);
}
