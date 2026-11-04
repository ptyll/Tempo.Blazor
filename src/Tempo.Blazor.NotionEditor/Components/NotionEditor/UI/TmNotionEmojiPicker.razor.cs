using System.Globalization;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Tempo.Blazor.Components.NotionEditor.UI;

public partial class TmNotionEmojiPicker : ComponentBase
{
    // ── DI ───────────────────────────────────────────────────────────────────

    [Inject] private IJSRuntime JS { get; set; } = default!;

    // ── Parameters ───────────────────────────────────────────────────────────

    [Parameter] public bool   Visible { get; set; }
    [Parameter] public double Top     { get; set; }
    [Parameter] public double Left    { get; set; }

    /// <summary>Raised when the user selects an emoji. Arg = emoji character string.</summary>
    [Parameter] public EventCallback<string> OnEmojiSelected { get; set; }

    /// <summary>Raised when the picker is dismissed without a selection.</summary>
    [Parameter] public EventCallback OnClosed { get; set; }

    /// <summary>Forced layout. Mobile presents the menu as the core sheet.</summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    // ── Static tab definitions ────────────────────────────────────────────────

    private static readonly (EmojiCategory Category, string Icon, string NameKey)[] _tabs =
    [
        (EmojiCategory.Smileys,    "😀", "TmNotionEmojiPicker_CategorySmileys"),
        (EmojiCategory.People,     "👋", "TmNotionEmojiPicker_CategoryPeople"),
        (EmojiCategory.Animals,    "🐶", "TmNotionEmojiPicker_CategoryAnimals"),
        (EmojiCategory.Food,       "🍎", "TmNotionEmojiPicker_CategoryFood"),
        (EmojiCategory.Travel,     "✈️", "TmNotionEmojiPicker_CategoryTravel"),
        (EmojiCategory.Activities, "⚽", "TmNotionEmojiPicker_CategoryActivities"),
        (EmojiCategory.Objects,    "💡", "TmNotionEmojiPicker_CategoryObjects"),
        (EmojiCategory.Symbols,    "❤️", "TmNotionEmojiPicker_CategorySymbols"),
        (EmojiCategory.Flags,      "🏁", "TmNotionEmojiPicker_CategoryFlags"),
    ];

    // ── Per-category cache ────────────────────────────────────────────────────

    private static readonly IReadOnlyDictionary<EmojiCategory, IReadOnlyList<EmojiEntry>> _byCategory =
        EmojiData.All
            .GroupBy(e => e.Category)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<EmojiEntry>)g.ToList());

    // ── State ─────────────────────────────────────────────────────────────────

    private string              _query          = string.Empty;
    private EmojiCategory       _activeCategory = EmojiCategory.Smileys;
    private bool                _isSearching;
    private bool                _wasVisible;
    private bool                _needsFocus;

    private List<string>        _recentEmojis   = [];
    private List<EmojiEntry>    _searchResults  = [];
    private IReadOnlyList<EmojiEntry> _categoryEmojis = EmojiData.All
        .Where(e => e.Category == EmojiCategory.Smileys).ToList();

    private ElementReference _searchRef;
    private ElementReference _scrollRef;

    private static readonly Random _rng = new();

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override async Task OnParametersSetAsync()
    {
        if (Visible && !_wasVisible)
        {
            _query          = string.Empty;
            _isSearching    = false;
            _activeCategory = EmojiCategory.Smileys;
            _needsFocus     = true;
            RefreshCategoryEmojis();
            await LoadRecentAsync();
        }
        else if (!Visible && _wasVisible)
        {
            _query       = string.Empty;
            _isSearching = false;
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
                await _searchRef.FocusAsync();
            }
            catch { /* SSR / test */ }
        }
    }

    // ── Search ────────────────────────────────────────────────────────────────

    private void HandleQueryInputAsync(ChangeEventArgs e)
    {
        _query       = e.Value?.ToString() ?? string.Empty;
        _isSearching = !string.IsNullOrWhiteSpace(_query);

        if (_isSearching)
        {
            var q = _query.Trim();
            _searchResults = EmojiData.All
                .Where(entry =>
                    entry.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    entry.Keywords.Any(k => k.Contains(q, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }
        else
        {
            _searchResults = [];
        }
    }

    // ── Category ──────────────────────────────────────────────────────────────

    private void SetCategory(EmojiCategory category)
    {
        _activeCategory = category;
        RefreshCategoryEmojis();
    }

    private void RefreshCategoryEmojis()
        => _categoryEmojis = _byCategory.TryGetValue(_activeCategory, out var list)
            ? list
            : [];

    // ── Random ────────────────────────────────────────────────────────────────

    private async Task HandleRandomAsync()
    {
        IReadOnlyList<EmojiEntry> pool = _isSearching && _searchResults.Count > 0
            ? _searchResults
            : (IReadOnlyList<EmojiEntry>)_categoryEmojis;

        if (pool.Count == 0) return;
        var picked = pool[_rng.Next(pool.Count)];
        await SelectEmojiAsync(picked.Emoji);
    }

    // ── Selection ─────────────────────────────────────────────────────────────

    private async Task SelectEmojiAsync(string emoji)
    {
        await SaveRecentAsync(emoji);
        await OnEmojiSelected.InvokeAsync(emoji);
    }

    // ── Close ─────────────────────────────────────────────────────────────────

    private async Task HandleBackdropClickAsync()
        => await OnClosed.InvokeAsync();

    private async Task HandleKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
            await OnClosed.InvokeAsync();
    }

    // The search input stops keydown propagation so typing does not reach the editor, which also
    // swallows Escape before the body handler above can see it. Close here, on the focused input.
    private Task HandleSearchKeyDownAsync(KeyboardEventArgs e) => HandleKeyDownAsync(e);

    // ── Recently used (localStorage) ─────────────────────────────────────────

    private async Task LoadRecentAsync()
    {
        try
        {
            var raw = await JS.InvokeAsync<string[]>("tmNotionEditor.getRecentEmojis");
            _recentEmojis = raw?.Take(16).ToList() ?? [];
        }
        catch
        {
            _recentEmojis = [];
        }
    }

    private async Task SaveRecentAsync(string emoji)
    {
        try { await JS.InvokeVoidAsync("tmNotionEditor.addRecentEmoji", emoji); }
        catch { }
    }

    // ── Category label ────────────────────────────────────────────────────────

    private string CategoryLabel(EmojiCategory cat) => cat switch
    {
        EmojiCategory.Smileys    => Loc["TmNotionEmojiPicker_CategorySmileys"],
        EmojiCategory.People     => Loc["TmNotionEmojiPicker_CategoryPeople"],
        EmojiCategory.Animals    => Loc["TmNotionEmojiPicker_CategoryAnimals"],
        EmojiCategory.Food       => Loc["TmNotionEmojiPicker_CategoryFood"],
        EmojiCategory.Travel     => Loc["TmNotionEmojiPicker_CategoryTravel"],
        EmojiCategory.Activities => Loc["TmNotionEmojiPicker_CategoryActivities"],
        EmojiCategory.Objects    => Loc["TmNotionEmojiPicker_CategoryObjects"],
        EmojiCategory.Symbols    => Loc["TmNotionEmojiPicker_CategorySymbols"],
        EmojiCategory.Flags      => Loc["TmNotionEmojiPicker_CategoryFlags"],
        _                        => cat.ToString()
    };
}
