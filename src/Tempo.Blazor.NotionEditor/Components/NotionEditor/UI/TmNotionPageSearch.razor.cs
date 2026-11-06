using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.UI;

public partial class TmNotionPageSearch : TmComponentBase, IAsyncDisposable
{
    // ── DI ───────────────────────────────────────────────────────────────────

    [Inject] private IJSRuntime JS { get; set; } = default!;

    // ── Cascaded ─────────────────────────────────────────────────────────────

    [CascadingParameter] private NotionEditorContext Context { get; set; } = default!;

    // ── State ─────────────────────────────────────────────────────────────────

    private bool   _visible;
    private string _query        = string.Empty;
    private bool   _loading;
    private bool   _showFilters;
    private bool   _needsFocus;
    private int    _selectedIndex;

    private IReadOnlyList<INotionPage>       _pages  = [];
    private IReadOnlyList<NotionSearchResult> _blocks = [];

    // ── Filter state ──────────────────────────────────────────────────────────

    private string _filterAuthor       = string.Empty;
    private string _filterLabel        = string.Empty;
    private string _filterType         = string.Empty;
    private string _filterSpace        = string.Empty;
    private string _filterDateFrom     = string.Empty;
    private string _filterDateTo       = string.Empty;
    private string _filterEditedBefore = string.Empty;

    // ── Refs & cleanup ────────────────────────────────────────────────────────

    private ElementReference _inputRef;
    private readonly string _inputId = $"tm-nps-input-{Guid.NewGuid():N}";
    private readonly Dictionary<string, string> _editorNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _filterIds = new()
    {
        ["author"] = $"tm-nps-f-{Guid.NewGuid():N}",
        ["label"] = $"tm-nps-f-{Guid.NewGuid():N}",
        ["type"] = $"tm-nps-f-{Guid.NewGuid():N}",
        ["space"] = $"tm-nps-f-{Guid.NewGuid():N}",
        ["from"] = $"tm-nps-f-{Guid.NewGuid():N}",
        ["to"] = $"tm-nps-f-{Guid.NewGuid():N}",
        ["edited"] = $"tm-nps-f-{Guid.NewGuid():N}"
    };
    private readonly string _instanceId = Guid.NewGuid().ToString("N");

    private string OptionId(int index) => $"tm-nps-opt-{_instanceId}-{index}";

    private string ResultsId => $"tm-nps-results-{_instanceId}";

    private string FiltersId => $"tm-nps-filters-{_instanceId}";

    private string? ActiveOptionId => _flatResults.Count > 0 ? OptionId(_selectedIndex) : null;

    private int ActiveFilterCount =>
        new[] { _filterAuthor, _filterLabel, _filterType, _filterSpace, _filterDateFrom, _filterDateTo, _filterEditedBefore }
            .Count(value => !string.IsNullOrWhiteSpace(value));
    private DotNetObjectReference<TmNotionPageSearch>? _dotNetRef;
    private CancellationTokenSource? _debounceCts;

    // ── Flat result list for unified keyboard nav ─────────────────────────────

    private record ResultEntry(bool IsPage, INotionPage? Page, NotionSearchResult? Block);
    private IReadOnlyList<ResultEntry> _flatResults = [];

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _dotNetRef = DotNetObjectReference.Create(this);
            try
            {
                await JS.InvokeVoidAsync("tmNotionEditor.registerPageSearch", _dotNetRef);
            }
            catch { /* SSR / test */ }
        }

        if (_needsFocus && _visible)
        {
            _needsFocus = false;
            try { await _inputRef.FocusAsync(); }
            catch { }
        }
    }

    // ── Public JS-invokable ───────────────────────────────────────────────────

    [JSInvokable]
    public void OpenPageSearch()
    {
        if (_visible) return;
        _visible       = true;
        _query         = string.Empty;
        _selectedIndex = 0;
        _pages         = [];
        _blocks        = [];
        _flatResults   = [];
        _needsFocus    = true;
        StateHasChanged();
        _ = LoadInitialResultsAsync();
    }

    // ── Search ────────────────────────────────────────────────────────────────

    private async Task LoadInitialResultsAsync()
    {
        await SearchAsync(_query);
    }

    private async Task HandleQueryInputAsync(ChangeEventArgs e)
    {
        _query         = e.Value?.ToString() ?? string.Empty;
        _selectedIndex = 0;

        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        var token = _debounceCts.Token;

        try
        {
            await Task.Delay(180, token);
            if (!token.IsCancellationRequested)
                await SearchAsync(_query);
        }
        catch (TaskCanceledException) { }
    }

    private async Task SearchAsync(string query)
    {
        if (Context.SearchProvider is null)
        {
            _pages       = [];
            _blocks      = [];
            _flatResults = [];
            StateHasChanged();
            return;
        }

        _loading = true;
        StateHasChanged();

        try
        {
            var filter = BuildFilter();
            var (pages, blocks) = await Context.SearchProvider.SearchAllAsync(query, filter, maxResults: 20);

            _pages  = pages.ToList();
            _blocks = blocks.ToList();
            await ResolvePagePathsAsync();
            RebuildFlatResults();
        }
        catch { }
        finally
        {
            _loading = false;
            StateHasChanged();
        }
    }

    private NotionSearchFilter? BuildFilter()
    {
        var hasFilter =
            !string.IsNullOrWhiteSpace(_filterAuthor)       ||
            !string.IsNullOrWhiteSpace(_filterLabel)        ||
            !string.IsNullOrWhiteSpace(_filterType)         ||
            !string.IsNullOrWhiteSpace(_filterSpace)        ||
            !string.IsNullOrWhiteSpace(_filterDateFrom)     ||
            !string.IsNullOrWhiteSpace(_filterDateTo)       ||
            !string.IsNullOrWhiteSpace(_filterEditedBefore);

        if (!hasFilter) return null;

        return new NotionSearchFilter
        {
            Author           = NormalizeFilterValue(_filterAuthor),
            LabelFilter      = NormalizeFilterValue(_filterLabel),
            ContentType      = NormalizeFilterValue(_filterType),
            SpaceId          = NormalizeFilterValue(_filterSpace),
            BlockType        = ResolveBlockType(_filterType),
            CreatedAfter     = TryParseDate(_filterDateFrom),
            CreatedBefore    = TryParseDate(_filterDateTo),
            LastEditedBefore = TryParseDate(_filterEditedBefore)
        };
    }

    private static string? NormalizeFilterValue(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTime? TryParseDate(string? s) =>
        DateTime.TryParse(s, out var d) ? d : null;

    private static BlockType? ResolveBlockType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType) ||
            contentType.Equals("Page", StringComparison.OrdinalIgnoreCase))
            return null;

        if (Enum.TryParse<BlockType>(contentType, ignoreCase: true, out var parsed))
            return parsed;

        return contentType.Trim().ToLowerInvariant() switch
        {
            "paragraph" => BlockType.Paragraph,
            "heading" => BlockType.Heading1,
            "todo" => BlockType.TodoItem,
            "media" => BlockType.Image,
            "table" => BlockType.Table,
            _ => null
        };
    }

    private void RebuildFlatResults()
    {
        var list = new List<ResultEntry>(_pages.Count + _blocks.Count);
        foreach (var p in _pages)  list.Add(new(true,  p,    null));
        foreach (var b in _blocks) list.Add(new(false, null, b));
        _flatResults   = list;
        _selectedIndex = 0;
    }

    // ── Keyboard navigation ───────────────────────────────────────────────────

    // Escape is the core TmModal's. A card-level handler closed the dialog twice.
    // Arrow/Enter keydowns inside a text input produce no native click, so
    // this input-level handler is where result navigation + selection belong.
    private void StopToggleKey(KeyboardEventArgs _) { }

    private async Task HandleSearchKeyDownAsync(KeyboardEventArgs e)
    {
        switch (e.Key)
        {
            case "ArrowDown":
                if (_flatResults.Count > 0)
                    _selectedIndex = (_selectedIndex + 1) % _flatResults.Count;
                break;

            case "ArrowUp":
                if (_flatResults.Count > 0)
                    _selectedIndex = (_selectedIndex - 1 + _flatResults.Count) % _flatResults.Count;
                break;

            case "Enter":
                await SelectCurrentAsync();
                return;
        }
    }

    private async Task SelectCurrentAsync()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _flatResults.Count) return;
        var entry = _flatResults[_selectedIndex];
        if (entry.IsPage && entry.Page is not null)
            await NavigateToPageAsync(entry.Page);
        else if (!entry.IsPage && entry.Block is not null)
            await NavigateToBlockResultAsync(entry.Block);
    }

    private async Task NavigateToPageAsync(INotionPage page)
    {
        await CloseAsync();
        if (Context.NavigateTo is not null)
            await Context.NavigateTo(page.Id.ToString("D"));
    }

    private async Task NavigateToBlockResultAsync(NotionSearchResult result)
    {
        await CloseAsync();
        if (Context.NavigateTo is not null)
            await Context.NavigateTo(result.PageId.ToString("D"));
    }

    private async Task CloseAsync()
    {
        _visible       = false;
        _query         = string.Empty;
        _pages         = [];
        _blocks        = [];
        _flatResults   = [];
        _showFilters   = false;
        await InvokeAsync(StateHasChanged);
    }

    // ── Filter interactions ───────────────────────────────────────────────────

    private void ToggleFilters()
    {
        _showFilters = !_showFilters;
        StateHasChanged();
    }

    private async Task ApplyFiltersAsync()
    {
        _selectedIndex = 0;
        await SearchAsync(_query);
    }

    private async Task ClearFiltersAsync()
    {
        _filterAuthor       = string.Empty;
        _filterLabel        = string.Empty;
        _filterType         = string.Empty;
        _filterSpace        = string.Empty;
        _filterDateFrom     = string.Empty;
        _filterDateTo       = string.Empty;
        _filterEditedBefore = string.Empty;
        await SearchAsync(_query);
    }

    private bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(_filterAuthor)       ||
        !string.IsNullOrWhiteSpace(_filterLabel)        ||
        !string.IsNullOrWhiteSpace(_filterType)         ||
        !string.IsNullOrWhiteSpace(_filterSpace)        ||
        !string.IsNullOrWhiteSpace(_filterDateFrom)     ||
        !string.IsNullOrWhiteSpace(_filterDateTo)       ||
        !string.IsNullOrWhiteSpace(_filterEditedBefore);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task ResolvePagePathsAsync()
    {
        Context.RememberPages(_pages);
        if (Context.MentionProvider is null)
            return;

        foreach (var userId in _pages
            .Select(page => page.LastEditedByUserId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (_editorNames.ContainsKey(userId!))
                continue;
            try
            {
                var match = await Context.MentionProvider.GetByIdAsync(userId!);
                if (match is not null && !string.IsNullOrWhiteSpace(match.DisplayName))
                    _editorNames[userId!] = match.DisplayName;
            }
            catch { }
        }
    }

    private string? PagePath(INotionPage page)
    {
        if (page.ParentId is not { } parentId)
            return null;
        if (!Context.PageCache.TryGetValue(parentId.ToString(), out var parent))
            return null;
        var parentTitle = string.IsNullOrWhiteSpace(parent.Title)
            ? Loc["TmNotionPageSearch_Untitled"]
            : parent.Title;
        return $"{parentTitle} / {GetPageTitle(page)}";
    }

    private string PageMeta(INotionPage page)
    {
        var editor = EditorDisplayName(page.LastEditedByUserId);
        var edited = page.LastEditedAt == default ? null : page.LastEditedAt.ToString("d");
        return string.Join(" · ", new[] { edited, editor }.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private string? EditorDisplayName(string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return null;
        return _editorNames.TryGetValue(userId, out var name) ? name : null;
    }

    private static string GetPageIcon(INotionPage page) =>
        string.IsNullOrEmpty(page.IconEmoji) ? "📄" : page.IconEmoji;

    private string GetPageTitle(INotionPage page) =>
        string.IsNullOrWhiteSpace(page.Title) ? Loc["TmNotionPageSearch_Untitled"] : page.Title;

    private MarkupString BuildSnippet(NotionSearchResult result)
    {
        if (string.IsNullOrEmpty(result.MatchSnippet))
            return new MarkupString(string.Empty);

        if (result.HighlightRanges is null || result.HighlightRanges.Count == 0)
            return new MarkupString(System.Web.HttpUtility.HtmlEncode(result.MatchSnippet));

        var snippet = result.MatchSnippet;
        var sb = new System.Text.StringBuilder();
        int cursor = 0;

        foreach (var (start, end) in result.HighlightRanges.OrderBy(r => r.Start))
        {
            if (start > cursor)
                sb.Append(System.Web.HttpUtility.HtmlEncode(snippet[cursor..start]));

            var clampedEnd = Math.Min(end, snippet.Length);
            if (clampedEnd > start)
                sb.Append("<mark>").Append(System.Web.HttpUtility.HtmlEncode(snippet[start..clampedEnd])).Append("</mark>");

            cursor = clampedEnd;
        }

        if (cursor < snippet.Length)
            sb.Append(System.Web.HttpUtility.HtmlEncode(snippet[cursor..]));

        return new MarkupString(sb.ToString());
    }

    private bool IsPageSelected(int pageIndex) => pageIndex == _selectedIndex;
    private bool IsBlockSelected(int blockIndex) => (_pages.Count + blockIndex) == _selectedIndex;

    // ── Dispose ───────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();

        try
        {
            await JS.InvokeVoidAsync("tmNotionEditor.destroyPageSearch");
        }
        catch { }

        _dotNetRef?.Dispose();
    }
}
