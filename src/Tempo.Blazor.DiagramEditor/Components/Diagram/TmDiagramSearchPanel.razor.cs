using Microsoft.AspNetCore.Components;

namespace Tempo.Blazor.Components.Diagram;

/// <summary>Floating search panel for the diagram editor.</summary>
public partial class TmDiagramSearchPanel : ComponentBase
{
    /// <summary>Current search query.</summary>
    [Parameter] public string Query { get; set; } = string.Empty;

    /// <summary>Fires when the query changes.</summary>
    [Parameter] public EventCallback<string> QueryChanged { get; set; }

    /// <summary>Total number of matches.</summary>
    [Parameter] public int ResultsCount { get; set; }

    /// <summary>Zero-based index of the currently highlighted match.</summary>
    [Parameter] public int CurrentIndex { get; set; }

    /// <summary>Fires when the active match index changes.</summary>
    [Parameter] public EventCallback<int> CurrentIndexChanged { get; set; }

    /// <summary>Fires when the panel should close.</summary>
    [Parameter] public EventCallback OnClose { get; set; }

    /// <summary>When true, search spans all pages instead of just the active one.</summary>
    [Parameter] public bool SearchAllPages { get; set; }

    /// <summary>Fires when the "search all pages" toggle changes.</summary>
    [Parameter] public EventCallback<bool> SearchAllPagesChanged { get; set; }

    /// <summary>Current replacement text.</summary>
    [Parameter] public string ReplaceQuery { get; set; } = string.Empty;

    /// <summary>Fires when the replacement text changes.</summary>
    [Parameter] public EventCallback<string> ReplaceQueryChanged { get; set; }

    /// <summary>When true, the query is treated as a regular expression.</summary>
    [Parameter] public bool UseRegex { get; set; }

    /// <summary>Fires when the regex toggle changes.</summary>
    [Parameter] public EventCallback<bool> UseRegexChanged { get; set; }

    /// <summary>Error message displayed when the regex pattern is invalid.</summary>
    [Parameter] public string? RegexError { get; set; }

    /// <summary>When true the panel is search-only: the replace field and the Replace / Replace all
    /// buttons are not rendered (the editor passes its effective read-only state).</summary>
    [Parameter] public bool ReadOnly { get; set; }

    /// <summary>Fires when the user clicks the Replace button.</summary>
    [Parameter] public EventCallback OnReplace { get; set; }

    /// <summary>Fires when the user clicks the Replace All button.</summary>
    [Parameter] public EventCallback OnReplaceAll { get; set; }

    private bool CanReplace => !ReadOnly && ResultsCount > 0 && !string.IsNullOrEmpty(Query) && string.IsNullOrEmpty(RegexError);
    private bool CanReplaceAll => CanReplace;

    /// <summary>Per-instance id of the error span: two panels on one page must never share an
    /// id (a duplicate id breaks the aria-describedby reference for every instance after the first).</summary>
    private string ErrorId { get; } = $"tm-diagram-search-error-{Guid.NewGuid():N}";

    /// <summary>The pattern error state: marks the query input aria-invalid and ties the
    /// role="alert" error span to it through aria-describedby.</summary>
    private bool HasError => !string.IsNullOrEmpty(RegexError);

    private async Task OnQueryChanged(string value)
    {
        Query = value;
        await QueryChanged.InvokeAsync(value);
    }

    private async Task OnReplaceQueryChanged(string value)
    {
        ReplaceQuery = value;
        await ReplaceQueryChanged.InvokeAsync(value);
    }

    private async Task OnUseRegexChanged(bool value)
    {
        UseRegex = value;
        await UseRegexChanged.InvokeAsync(value);
    }

    private async Task OnPreviousClick()
    {
        if (ResultsCount == 0) return;
        var idx = (CurrentIndex - 1 + ResultsCount) % ResultsCount;
        await CurrentIndexChanged.InvokeAsync(idx);
    }

    private async Task OnNextClick()
    {
        if (ResultsCount == 0) return;
        var idx = (CurrentIndex + 1) % ResultsCount;
        await CurrentIndexChanged.InvokeAsync(idx);
    }

    private async Task OnCloseClick()
        => await OnClose.InvokeAsync();

    private async Task OnSearchAllPagesChanged(bool value)
    {
        SearchAllPages = value;
        await SearchAllPagesChanged.InvokeAsync(value);
    }

    private async Task OnReplaceClick()
    {
        if (CanReplace)
            await OnReplace.InvokeAsync();
    }

    private async Task OnReplaceAllClick()
    {
        if (CanReplaceAll)
            await OnReplaceAll.InvokeAsync();
    }
}
