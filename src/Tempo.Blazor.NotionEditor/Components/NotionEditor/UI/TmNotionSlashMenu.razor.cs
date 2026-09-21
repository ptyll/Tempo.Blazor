using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Enums;

namespace Tempo.Blazor.Components.NotionEditor.UI;

public partial class TmNotionSlashMenu : TmComponentBase
{
    // ── DI ───────────────────────────────────────────────────────────────────

    [Inject] private IJSRuntime JS { get; set; } = default!;

    // ── Cascaded context ─────────────────────────────────────────────────────

    [CascadingParameter]
    private NotionEditorContext Context { get; set; } = default!;

    // ── Parameters ───────────────────────────────────────────────────────────

    [Parameter] public bool   Visible { get; set; }
    [Parameter] public double Top     { get; set; }
    [Parameter] public double Left    { get; set; }

    /// <summary>Raised when the user selects a block type.</summary>
    [Parameter] public EventCallback<BlockType> OnItemSelected { get; set; }

    /// <summary>Raised when the user selects a slash-menu item with item metadata.</summary>
    [Parameter] public EventCallback<SlashMenuItem> OnSlashItemSelected { get; set; }

    /// <summary>Raised when the user selects the AI assistant action.</summary>
    [Parameter] public EventCallback OnAISelected { get; set; }

    /// <summary>Raised when the user dismisses the menu (Escape / backdrop click).</summary>
    [Parameter] public EventCallback OnClosed { get; set; }

    // ── State ────────────────────────────────────────────────────────────────

    private string   _query         = string.Empty;
    private int      _selectedIndex;
    private double   _top;
    private double   _left;
    private bool     _wasVisible;
    private bool     _needsFocus;
    private bool     _hoverEnabled;

    private List<BlockType> _recentlyUsed = [];
    private List<(SlashMenuCategory Category, List<SlashMenuItem> Items)> _groups = [];
    private int _totalItems;
    private bool _showAiItem;

    private ElementReference _menuRef;
    private ElementReference _inputRef;
    private ElementReference _listRef;

    private const string AiIcon =
        """<svg width="20" height="20" viewBox="0 0 20 20" fill="none" aria-hidden="true"><path d="M10 2.5l1.35 4.15L15.5 8l-4.15 1.35L10 13.5 8.65 9.35 4.5 8l4.15-1.35L10 2.5z" stroke="currentColor" stroke-width="1.4" stroke-linejoin="round"/><path d="M5.5 12.5l.7 2.1 2.1.7-2.1.7-.7 2.1-.7-2.1-2.1-.7 2.1-.7.7-2.1zM15 11l.55 1.65L17.2 13.2l-1.65.55L15 15.4l-.55-1.65-1.65-.55 1.65-.55L15 11z" fill="currentColor"/></svg>""";

    // ── Lifecycle ────────────────────────────────────────────────────────────

    protected override async Task OnParametersSetAsync()
    {
        if (Visible && !_wasVisible)
        {
            // Menu just opened — reset state
            _query         = string.Empty;
            _selectedIndex = 0;
            _hoverEnabled  = false;
            _top           = Top;
            _left          = Left;
            _needsFocus    = true;

            await LoadRecentAsync();
            RebuildGroups();
        }
        else if (!Visible && _wasVisible)
        {
            _query  = string.Empty;
            _groups = [];
        }

        _wasVisible = Visible;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_needsFocus && Visible)
        {
            _needsFocus = false;
            try
            {
                await _inputRef.FocusAsync();
                await JS.InvokeVoidAsync("tmNotionEditor.adjustSlashMenuPosition", _menuRef);
            }
            catch { /* SSR / test */ }
        }
    }

    // ── Query handling ────────────────────────────────────────────────────────

    private async Task HandleQueryInputAsync(ChangeEventArgs e)
    {
        _query         = e.Value?.ToString() ?? string.Empty;
        _selectedIndex = 0;
        RebuildGroups();
        await ScrollToSelectedAsync();
    }

    private void RebuildGroups()
    {
        var allowed = BuildEffectiveAllowedTypes();

        var recent = allowed is null
            ? _recentlyUsed
            : _recentlyUsed.Where(t => allowed.Contains(t)).ToList();

        _groups     = SlashMenuRegistry.GetGrouped(_query, recent, ResolveName, ResolveDesc, allowed);
        _showAiItem = Context?.AIProvider is not null && MatchesAI(_query);
        _totalItems = _groups.Sum(g => g.Items.Count) + (_showAiItem ? 1 : 0);
    }

    private string ResolveName(SlashMenuItem item)        => Loc[item.Name];
    private string ResolveDesc(SlashMenuItem item)        => Loc[item.Description];

    private IReadOnlySet<BlockType>? BuildEffectiveAllowedTypes()
    {
        var configured = Context?.AllowedBlockTypes;
        var hasWorkItemProvider = Context?.WorkItemProviders?.GetAll().Count > 0;

        IReadOnlySet<BlockType>? result;
        if (hasWorkItemProvider)
        {
            result = configured;
        }
        else if (configured is null)
        {
            result = SlashMenuRegistry.All
                .Select(i => i.Type)
                .Where(t => t != BlockType.WorkItem)
                .ToHashSet();
        }
        else if (!configured.Contains(BlockType.WorkItem))
        {
            result = configured;
        }
        else
        {
            result = configured
                .Where(t => t != BlockType.WorkItem)
                .ToHashSet();
        }

        // Single enforcement point: also strip context-denied block types (e.g. SinglePageMode multi-page blocks).
        if (Context is { HasDeniedBlockTypes: true } ctx)
        {
            var source = result ?? SlashMenuRegistry.All.Select(i => i.Type);
            result = source.Where(ctx.IsBlockTypeAllowed).ToHashSet();
        }

        return result;
    }

    // ── Pointer / keyboard navigation ─────────────────────────────────────────

    private void EnableHoverSelection() => _hoverEnabled = true;

    private void OnItemMouseEnter(int index)
    {
        // Ignore a resting cursor sitting over the menu at open — only honor hover selection
        // after the pointer has actually moved over the list (EnableHoverSelection).
        if (!_hoverEnabled)
        {
            return;
        }

        _selectedIndex = index;
    }

    private async Task HandleKeyDownAsync(KeyboardEventArgs e)
    {
        switch (e.Key)
        {
            case "ArrowDown":
                if (_totalItems > 0)
                    _selectedIndex = (_selectedIndex + 1) % _totalItems;
                await ScrollToSelectedAsync();
                break;

            case "ArrowUp":
                if (_totalItems > 0)
                    _selectedIndex = (_selectedIndex - 1 + _totalItems) % _totalItems;
                await ScrollToSelectedAsync();
                break;

            case "Enter":
                if (_showAiItem && _selectedIndex == 0)
                {
                    await SelectAiItemAsync();
                }
                else
                {
                    var item = GetFlatItem(_selectedIndex);
                    if (item is not null) await SelectItemAsync(item);
                }
                break;

            case "Escape":
                await HandleBackdropClickAsync();
                break;
        }
    }

    // ── Selection ─────────────────────────────────────────────────────────────

    private async Task SelectItemAsync(SlashMenuItem item)
    {
        if (item.Action == SlashMenuAction.ConvertBlock)
        {
            await SaveRecentAsync(item.Type);
            await JS.InvokeVoidAsync("tmNotionEditor.clearSlashQuery");
        }

        if (OnSlashItemSelected.HasDelegate)
        {
            await OnSlashItemSelected.InvokeAsync(item);
        }
        else
        {
            await OnItemSelected.InvokeAsync(item.Type);
        }
    }

    private async Task SelectAiItemAsync()
    {
        await JS.InvokeVoidAsync("tmNotionEditor.clearSlashQuery");
        await OnAISelected.InvokeAsync();
    }

    // ── Close ─────────────────────────────────────────────────────────────────

    private async Task HandleBackdropClickAsync()
    {
        try { await JS.InvokeVoidAsync("tmNotionEditor.refocusSlashElement"); }
        catch { /* SSR / test */ }
        await OnClosed.InvokeAsync();
    }

    // ── Recently used (localStorage) ─────────────────────────────────────────

    private async Task LoadRecentAsync()
    {
        try
        {
            var raw = await JS.InvokeAsync<int[]>("tmNotionEditor.getRecentSlashItems");
            _recentlyUsed = raw
                .Select(i => (BlockType)i)
                .Where(t => SlashMenuRegistry.FindByType(t) is not null)
                .Distinct()
                .ToList();
        }
        catch
        {
            _recentlyUsed = [];
        }
    }

    private async Task SaveRecentAsync(BlockType type)
    {
        try { await JS.InvokeVoidAsync("tmNotionEditor.addRecentSlashItem", (int)type); }
        catch { }
    }

    // ── Category label mapping ────────────────────────────────────────────────

    private string CategoryLabel(SlashMenuCategory cat) => cat switch
    {
        SlashMenuCategory.Recent   => Loc["TmNotionSlashMenu_CategoryRecent"],
        SlashMenuCategory.Basic    => Loc["TmNotionSlashMenu_CategoryBasic"],
        SlashMenuCategory.Media    => Loc["TmNotionSlashMenu_CategoryMedia"],
        SlashMenuCategory.Embeds   => Loc["TmNotionSlashMenu_CategoryEmbeds"],
        SlashMenuCategory.Page     => Loc["TmNotionSlashMenu_CategoryPage"],
        SlashMenuCategory.Advanced => Loc["TmNotionSlashMenu_CategoryAdvanced"],
        _                          => cat.ToString()
    };

    // ── Helpers ───────────────────────────────────────────────────────────────

    private SlashMenuItem? GetFlatItem(int flatIndex)
    {
        var idx = _showAiItem ? 1 : 0;
        foreach (var (_, items) in _groups)
        {
            foreach (var item in items)
            {
                if (idx == flatIndex) return item;
                idx++;
            }
        }
        return null;
    }

    private bool MatchesAI(string query)
    {
        var q = query.Trim();
        if (q.Length == 0) return true;

        return "ai".Contains(q, StringComparison.OrdinalIgnoreCase)
            || "assistant".Contains(q, StringComparison.OrdinalIgnoreCase)
            || Loc["Notion_AI_Assistant"].Contains(q, StringComparison.OrdinalIgnoreCase)
            || Loc["TmNotionSlashMenu_ItemDesc_AI"].Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private async Task ScrollToSelectedAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("tmNotionEditor.scrollSlashItemIntoView", _listRef, _selectedIndex);
        }
        catch { }
    }
}
