using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Interfaces;

namespace Tempo.Blazor.Components.NotionEditor.UI;

public partial class TmNotionLabelEditor : ComponentBase, IDisposable
{
    private const int MaxLabelLength = 64;
    private readonly CancellationTokenSource _disposeCts = new();

    /// <summary>Identifier of the page whose labels are edited.</summary>
    [Parameter] public Guid PageId { get; set; }

    /// <summary>Current labels assigned to the page.</summary>
    [Parameter] public IReadOnlyList<string> Labels { get; set; } = [];

    /// <summary>Whether editing controls are hidden.</summary>
    [Parameter] public bool ReadOnly { get; set; }

    /// <summary>Raised after labels are successfully persisted.</summary>
    [Parameter] public EventCallback<IReadOnlyList<string>> OnLabelsChanged { get; set; }

    [CascadingParameter] private NotionEditorContext Context { get; set; } = default!;

    private IReadOnlyList<string> _labels = [];
    private IReadOnlyList<string> _allLabels = [];
    private IReadOnlyList<INotionPage> _filteredPages = [];
    private string _input = string.Empty;
    private string? _filterLabel;
    private bool _filterLoading;
    private bool _suggestionsLoaded;

    private IReadOnlyList<string> FilteredSuggestions
    {
        get
        {
            if (ReadOnly || !_suggestionsLoaded)
                return [];

            var query = NormalizeLabel(_input);
            return _allLabels
                .Where(label => !_labels.Contains(label, StringComparer.OrdinalIgnoreCase))
                .Where(label => query is null || label.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(8)
                .ToArray();
        }
    }

    protected override void OnParametersSet()
        => _labels = NormalizeLabels(Labels);

    private async Task LoadSuggestionsAsync()
    {
        if (_suggestionsLoaded)
            return;

        _allLabels = NormalizeLabels(await Context.DataProvider.GetAllLabelsAsync(_disposeCts.Token));
        _suggestionsLoaded = true;
    }

    private async Task HandleInputAsync(ChangeEventArgs args)
    {
        _input = TrimToMax(args.Value?.ToString() ?? string.Empty);
        await LoadSuggestionsAsync();
    }

    // Escape stays on the section so it works wherever focus sits. Enter->add
    // lives on the text input itself: the chips, remove, add, suggestion and
    // filter-close controls are native <button>s — the browser turns Enter on
    // a focused <button> into a click on keydown, so a section-level Enter
    // case added the typed input on top of the button's own click (e.g. a
    // suggestion click would add BOTH the suggestion and the typed text).
    private void HandleSectionKeyDownAsync(KeyboardEventArgs args)
    {
        if (args.Key == "Escape")
        {
            _input = string.Empty;
            CloseFilter();
        }
    }

    private async Task HandleInputKeyDownAsync(KeyboardEventArgs args)
    {
        if (args.Key == "Enter" && !string.IsNullOrWhiteSpace(_input) && !ReadOnly)
        {
            await AddCurrentInputAsync();
        }
    }

    private Task AddCurrentInputAsync()
        => AddLabelAsync(_input);

    private async Task AddLabelAsync(string label)
    {
        if (ReadOnly)
            return;

        var normalized = NormalizeLabel(label);
        if (normalized is null || _labels.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            _input = string.Empty;
            return;
        }

        var next = NormalizeLabels(_labels.Concat([normalized]));
        await SaveLabelsAsync(next);
        _input = string.Empty;
    }

    private async Task RemoveLabelAsync(string label)
    {
        if (ReadOnly)
            return;

        var next = _labels
            .Where(existing => !string.Equals(existing, label, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        await SaveLabelsAsync(next);
    }

    private async Task SaveLabelsAsync(IReadOnlyList<string> labels)
    {
        var normalized = NormalizeLabels(labels);
        await Context.DataProvider.SetPageLabelsAsync(PageId, normalized, _disposeCts.Token);
        _labels = normalized;
        _allLabels = NormalizeLabels(_allLabels.Concat(normalized));
        await OnLabelsChanged.InvokeAsync(normalized);
    }

    private async Task OpenFilterAsync(string label)
    {
        var normalized = NormalizeLabel(label);
        if (normalized is null)
            return;

        _filterLabel = normalized;
        _filterLoading = true;
        _filteredPages = [];
        StateHasChanged();

        try
        {
            _filteredPages = await Context.DataProvider.GetPagesByLabelAsync(normalized, _disposeCts.Token);
        }
        finally
        {
            _filterLoading = false;
        }
    }

    private void CloseFilter()
    {
        _filterLabel = null;
        _filteredPages = [];
        _filterLoading = false;
    }

    private async Task NavigateToPageAsync(INotionPage page)
    {
        CloseFilter();
        if (Context.NavigateTo is not null)
            await Context.NavigateTo(page.Id.ToString("D"));
    }

    private static IReadOnlyList<string> NormalizeLabels(IEnumerable<string>? labels)
    {
        if (labels is null)
            return [];

        var result = new List<string>();
        foreach (var label in labels)
        {
            var normalized = NormalizeLabel(label);
            if (normalized is null || result.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                continue;

            result.Add(normalized);
        }

        return result;
    }

    private static string? NormalizeLabel(string? label)
    {
        var trimmed = label?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;

        return TrimToMax(trimmed);
    }

    private static string TrimToMax(string value)
        => value.Length <= MaxLabelLength ? value : value[..MaxLabelLength];

    private static string PageIcon(INotionPage page)
        => !string.IsNullOrWhiteSpace(page.IconEmoji) ? page.IconEmoji! : string.Empty;

    private static string PageTitle(INotionPage page)
        => string.IsNullOrWhiteSpace(page.Title) ? page.Id.ToString("D") : page.Title;

    public void Dispose()
        => _disposeCts.Dispose();
}
