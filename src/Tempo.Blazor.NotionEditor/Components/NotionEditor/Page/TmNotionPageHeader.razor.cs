using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.Localization;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.Page;

/// <summary>
/// Page header: cover image (with drag repositioning), icon (emoji picker),
/// title (contenteditable), and optional description.
/// </summary>
public partial class TmNotionPageHeader : ComponentBase, IDisposable
{
    // ── DI ───────────────────────────────────────────────────────────────────

    [Inject] private IJSRuntime JS { get; set; } = default!;

    [Inject] private IServiceProvider Services { get; set; } = default!;

    private ITmRelativeTimeFormatter? _relativeTime;

    /// <summary>Cached per component — <see cref="TmRelativeTime.Resolve"/> allocates a fallback when none is registered.</summary>
    private ITmRelativeTimeFormatter RelativeTime => _relativeTime ??= TmRelativeTime.Resolve(Services, Loc);

    private string AbsoluteTime(DateTimeOffset dt) => RelativeTime.Absolute(dt);

    // ── Cascaded context ─────────────────────────────────────────────────────

    [CascadingParameter]
    private NotionEditorContext Context { get; set; } = default!;

    [CascadingParameter]
    private TmLayoutContext? Layout { get; set; }

    // ── Parameters ───────────────────────────────────────────────────────────

    [Parameter, EditorRequired]
    public INotionPage Page { get; set; } = default!;

    [Parameter] public bool ReadOnly { get; set; }

    /// <summary>
    /// Opt-in meta row under the title (phase 5, N2): last-edited time (relative + absolute),
    /// the last editor (avatar + resolved name) and the unresolved comment count as a link.
    /// Defaults to <see langword="false"/>; the demo turns it on.
    /// </summary>
    [Parameter] public bool ShowMeta { get; set; }

    /// <summary>When true, displays a visible page-level restricted access indicator in the header.</summary>
    [Parameter] public bool ShowRestrictedIndicator { get; set; }

    [Parameter] public EventCallback<INotionPage> OnPageUpdated { get; set; }

    /// <summary>Fired when Enter is pressed in the title, signalling to add a new block.</summary>
    [Parameter] public EventCallback OnTitleEnterPressed { get; set; }

    /// <summary>Total unresolved comment count for the page (header badge).</summary>
    [Parameter] public int UnresolvedCommentCount { get; set; }

    /// <summary>Fired when the comment badge in the header is clicked.</summary>
    [Parameter] public EventCallback OnCommentBadgeClicked { get; set; }

    // ── State ────────────────────────────────────────────────────────────────

    private string       _titleInput       = string.Empty;
    private bool         _titleDirty;
    private string       _descriptionInput = string.Empty;
    private bool         _descriptionDirty;
    private bool         _showDescription;
    private bool         _showIconPicker;
    private bool         _showCoverDialog;
    private string       _coverUrlInput    = string.Empty;
    private double       _coverPositionY   = 50;
    private string       _emojiSearch      = string.Empty;
    private List<string> _recentEmojis     = [];
    private INotionPage? _lastPage;

    /// <summary>Resolved display name of the last editor (meta row); the raw id is the fallback.</summary>
    private string _lastEditorName = string.Empty;

    private ElementReference _titleRef;
    private ElementReference _descRef;
    private ElementReference _coverRef;

    private TmLayoutMode LayoutMode => Layout?.Resolved ?? TmLayoutMode.Auto;

    private DotNetObjectReference<TmNotionPageHeader>? _coverDragRef;

    // ── Computed ─────────────────────────────────────────────────────────────

    private string CoverStyle
    {
        get
        {
            if (string.IsNullOrEmpty(Page.CoverImageUrl)) return string.Empty;
            if (IsCssGradient(Page.CoverImageUrl))
                return $"background:{Page.CoverImageUrl}";
            var posY = _coverPositionY.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
            return $"background-image:url('{Page.CoverImageUrl}');background-size:cover;background-position-y:{posY}%";
        }
    }

    private IEnumerable<EmojiCategory> FilteredCategories =>
        string.IsNullOrWhiteSpace(_emojiSearch)
            ? AllCategories
            : AllCategories
                .Select(c => new EmojiCategory(
                    c.Name,
                    c.Emojis.Where(e => e.Name.Contains(_emojiSearch, StringComparison.OrdinalIgnoreCase)).ToArray()))
                .Where(c => c.Emojis.Length > 0);

    // ── Lifecycle ────────────────────────────────────────────────────────────

    protected override async Task OnParametersSetAsync()
    {
        if (!ReferenceEquals(Page, _lastPage))
        {
            _lastPage         = Page;
            _titleInput       = Page.Title;
            _titleDirty       = false;
            _descriptionInput = Page.Description ?? string.Empty;
            _descriptionDirty = false;
            _showDescription  = !string.IsNullOrEmpty(Page.Description);
            _coverPositionY   = Page.CoverImagePositionY ?? 50;
            _showIconPicker   = false;
            _showCoverDialog  = false;
            _emojiSearch      = string.Empty;
            _lastEditorName   = Page.LastEditedByUserId ?? string.Empty;
            _resolvedEditorId = null;
        }

        if (ShowMeta && !string.IsNullOrWhiteSpace(Page.LastEditedByUserId))
            await ResolveLastEditorAsync();
    }

    private string? _resolvedEditorId;

    /// <summary>Resolves the last editor's display name through the mention provider; the raw id stays as fallback.</summary>
    private async Task ResolveLastEditorAsync()
    {
        var userId = Page.LastEditedByUserId!.Trim();
        if (string.Equals(_resolvedEditorId, userId, StringComparison.Ordinal))
            return;
        _resolvedEditorId = userId;
        _lastEditorName = userId;
        if (Context.MentionProvider is null)
            return;
        try
        {
            var match = await Context.MentionProvider.GetByIdAsync(userId);
            if (match is not null && !string.IsNullOrWhiteSpace(match.DisplayName))
                _lastEditorName = match.DisplayName;
        }
        catch { /* provider unavailable — keep the id fallback */ }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            try
            {
                await JS.InvokeVoidAsync("tmNotionEditor.setHtml", _titleRef, Page.Title);
                if (_showDescription && !string.IsNullOrEmpty(Page.Description))
                    await JS.InvokeVoidAsync("tmNotionEditor.setHtml", _descRef, Page.Description);
            }
            catch { /* SSR / test */ }
        }
    }

    // ── Cover ─────────────────────────────────────────────────────────────────

    private static bool IsCssGradient(string? url) =>
        url is not null && (url.StartsWith("linear-gradient", StringComparison.Ordinal)
                         || url.StartsWith("radial-gradient",  StringComparison.Ordinal));

    private void ToggleCoverDialog()
    {
        _showCoverDialog = !_showCoverDialog;
        if (_showCoverDialog)
        {
            _showIconPicker  = false;
            _coverUrlInput   = IsCssGradient(Page.CoverImageUrl) ? string.Empty : Page.CoverImageUrl ?? string.Empty;
        }
    }

    private void CloseCoverDialog() => _showCoverDialog = false;

    private bool IsCurrentPreset(CoverPreset preset)
        => !string.IsNullOrEmpty(Page.CoverImageUrl)
        && string.Equals(Page.CoverImageUrl, preset.Background, StringComparison.Ordinal);

    private async Task SaveCoverUrlAsync()
    {
        if (string.IsNullOrWhiteSpace(_coverUrlInput)) return;
        var updated = MapToMutable(Page);
        updated.CoverImageUrl       = _coverUrlInput.Trim();
        updated.CoverImagePositionY = 50;
        _coverPositionY = 50;
        try
        {
            await Context.DataProvider.UpdatePageAsync(updated);
            await OnPageUpdated.InvokeAsync(updated);
        }
        catch { }
        _showCoverDialog = false;
    }

    private async Task OnCoverUrlKeyDown(KeyboardEventArgs e)
    {
        // Enter submits; Escape is owned by the TmModal focus trap (document-level, closes from
        // any element) and must not be swallowed here.
        if (e.Key == "Enter") await SaveCoverUrlAsync();
    }

    private async Task ApplyCoverPresetAsync(CoverPreset preset)
    {
        var updated = MapToMutable(Page);
        updated.CoverImageUrl       = preset.Background;
        updated.CoverImagePositionY = 50;
        _coverPositionY = 50;
        try
        {
            await Context.DataProvider.UpdatePageAsync(updated);
            await OnPageUpdated.InvokeAsync(updated);
        }
        catch { }
        _showCoverDialog = false;
    }

    private async Task RemoveCoverAsync()
    {
        if (ReadOnly) return;
        var updated = MapToMutable(Page);
        updated.CoverImageUrl       = null;
        updated.CoverImagePositionY = null;
        try
        {
            await Context.DataProvider.UpdatePageAsync(updated);
            await OnPageUpdated.InvokeAsync(updated);
        }
        catch { }
    }

    private async Task OnCoverMouseDownAsync(MouseEventArgs e)
    {
        if (ReadOnly || e.Button != 0) return;
        _coverDragRef?.Dispose();
        _coverDragRef = DotNetObjectReference.Create(this);
        try
        {
            await JS.InvokeVoidAsync("tmNotionEditor.startCoverDrag",
                _coverRef, _coverDragRef, e.ClientY, _coverPositionY);
        }
        catch { }
    }

    [JSInvokable]
    public async Task OnCoverDragEnded(double positionY)
    {
        _coverPositionY = positionY;
        var updated = MapToMutable(Page);
        updated.CoverImagePositionY = positionY;
        try
        {
            await Context.DataProvider.UpdatePageAsync(updated);
            await OnPageUpdated.InvokeAsync(updated);
        }
        catch { }
        StateHasChanged();
    }

    // ── Icon ──────────────────────────────────────────────────────────────────

    private async Task ToggleIconPickerAsync()
    {
        if (ReadOnly) return;
        _showIconPicker = !_showIconPicker;
        if (_showIconPicker)
        {
            _showCoverDialog = false;
            _emojiSearch     = string.Empty;
            await LoadRecentEmojisAsync();
        }
    }

    /// <summary>
    /// Closes the picker. The TmModal that hosts it owns the trigger focus restore
    /// (<c>NoteOpenerOnMount</c>), so no explicit focus move happens here.
    /// </summary>
    private void CloseIconPicker() => _showIconPicker = false;

    private async Task SelectEmojiAsync(string emoji)
    {
        _showIconPicker = false;
        await AddRecentEmojiAsync(emoji);
        var updated = MapToMutable(Page);
        updated.IconEmoji    = emoji;
        updated.IconImageUrl = null;
        try
        {
            await Context.DataProvider.UpdatePageAsync(updated);
            await OnPageUpdated.InvokeAsync(updated);
        }
        catch { }
    }

    private async Task RemoveIconAsync()
    {
        _showIconPicker = false;
        var updated = MapToMutable(Page);
        updated.IconEmoji    = null;
        updated.IconImageUrl = null;
        try
        {
            await Context.DataProvider.UpdatePageAsync(updated);
            await OnPageUpdated.InvokeAsync(updated);
        }
        catch { }
    }

    // ── Title ─────────────────────────────────────────────────────────────────

    private void OnTitleInput(ChangeEventArgs e)
    {
        _titleInput = e.Value?.ToString() ?? string.Empty;
        _titleDirty = true;
    }

    private async Task OnTitleBlurAsync()
    {
        if (!_titleDirty || ReadOnly) return;
        _titleDirty = false;
        await SaveTitleAsync(_titleInput);
    }

    private async Task OnTitleKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            if (_titleDirty) await SaveTitleAsync(_titleInput);
            await OnTitleEnterPressed.InvokeAsync();
        }
        else if (e.Key == "Escape")
        {
            _titleInput = Page.Title;
            _titleDirty = false;
            try { await JS.InvokeVoidAsync("tmNotionEditor.setHtml", _titleRef, Page.Title); }
            catch { }
        }
    }

    private async Task LoadRecentEmojisAsync()
    {
        try
        {
            var recent = await JS.InvokeAsync<string[]>("tmNotionEditor.getRecentEmojis");
            _recentEmojis = recent
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Distinct(StringComparer.Ordinal)
                .Take(16)
                .ToList();
        }
        catch
        {
            _recentEmojis = [];
        }
    }

    private async Task AddRecentEmojiAsync(string emoji)
    {
        try
        {
            await JS.InvokeVoidAsync("tmNotionEditor.addRecentEmoji", emoji);
            _recentEmojis.RemoveAll(e => string.Equals(e, emoji, StringComparison.Ordinal));
            _recentEmojis.Insert(0, emoji);
            if (_recentEmojis.Count > 16)
                _recentEmojis.RemoveRange(16, _recentEmojis.Count - 16);
        }
        catch { }
    }

    private async Task SaveTitleAsync(string newTitle)
    {
        var updated = MapToMutable(Page);
        updated.Title = newTitle;
        try
        {
            await Context.DataProvider.UpdatePageAsync(updated);
            await OnPageUpdated.InvokeAsync(updated);
        }
        catch { }
    }

    // ── Description ───────────────────────────────────────────────────────────

    private async Task ShowDescriptionAsync()
    {
        _showDescription = true;
        StateHasChanged();
        try { await JS.InvokeVoidAsync("tmNotionEditor.focus", _descRef); }
        catch { }
    }

    private async Task HideDescriptionAsync()
    {
        _showDescription  = false;
        _descriptionInput = string.Empty;
        if (!string.IsNullOrEmpty(Page.Description))
            await SaveDescriptionAsync(null);
        else
            StateHasChanged();
    }

    private void OnDescriptionInput(ChangeEventArgs e)
    {
        _descriptionInput = e.Value?.ToString() ?? string.Empty;
        _descriptionDirty = true;
    }

    private async Task OnDescriptionBlurAsync()
    {
        if (!_descriptionDirty || ReadOnly) return;
        _descriptionDirty = false;
        await SaveDescriptionAsync(_descriptionInput);
    }

    private async Task SaveDescriptionAsync(string? desc)
    {
        var updated = MapToMutable(Page);
        updated.Description = string.IsNullOrWhiteSpace(desc) ? null : desc;
        try
        {
            await Context.DataProvider.UpdatePageAsync(updated);
            await OnPageUpdated.InvokeAsync(updated);
        }
        catch { }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static NotionPage MapToMutable(INotionPage src) => new()
    {
        Id                  = src.Id,
        ParentId            = src.ParentId,
        Title               = src.Title,
        Description         = src.Description,
        IconEmoji           = src.IconEmoji,
        IconImageUrl        = src.IconImageUrl,
        CoverImageUrl       = src.CoverImageUrl,
        CoverImagePositionY = src.CoverImagePositionY,
        IsFullWidth         = src.IsFullWidth,
        IsSmallText         = src.IsSmallText,
        IsLocked            = src.IsLocked,
        CreatedAt           = src.CreatedAt,
        CreatedByUserId     = src.CreatedByUserId,
        LastEditedAt        = src.LastEditedAt,
        LastEditedByUserId  = src.LastEditedByUserId,
        IsDeleted           = src.IsDeleted,
        DeletedAt           = src.DeletedAt,
        IsFavorite          = src.IsFavorite
    };

    // ── Emoji data ────────────────────────────────────────────────────────────

    private static readonly IReadOnlyList<EmojiCategory> AllCategories = BuildEmojiCategories();

    private static EmojiCategory[] BuildEmojiCategories() =>
    [
        new("Smileys", [ new("😀","grinning"), new("😂","joy"), new("😍","heart eyes"),
            new("😎","sunglasses"), new("🤔","thinking"), new("😴","sleeping"),
            new("😱","scream"), new("🥳","partying"), new("🤩","star struck"),
            new("😭","loudly crying"), new("🙂","slightly smiling"), new("😊","blush"),
            new("🤯","exploding head"), new("🥺","pleading"), new("😇","innocent"), new("🤗","hugging") ]),
        new("Gestures", [ new("👍","thumbs up"), new("👎","thumbs down"), new("🙌","raising hands"),
            new("👏","clapping"), new("🤝","handshake"), new("✌️","victory"),
            new("💪","muscle"), new("🖐️","hand"), new("👋","wave"),
            new("🤞","crossed fingers"), new("🤌","pinched fingers"), new("🫶","heart hands") ]),
        new("Hearts", [ new("❤️","red heart"), new("🧡","orange heart"), new("💛","yellow heart"),
            new("💚","green heart"), new("💙","blue heart"), new("💜","purple heart"),
            new("🖤","black heart"), new("🤍","white heart"), new("💔","broken heart"),
            new("💕","two hearts"), new("💗","growing heart"), new("💝","heart ribbon") ]),
        new("Stars & Fire", [ new("⭐","star"), new("🌟","glowing star"), new("✨","sparkles"),
            new("💫","dizzy"), new("🔥","fire"), new("💯","hundred"),
            new("🎯","bullseye"), new("⚡","lightning"), new("💥","collision"), new("🌈","rainbow") ]),
        new("Events", [ new("🎉","party popper"), new("🎊","confetti ball"), new("🎈","balloon"),
            new("🎁","gift"), new("🏆","trophy"), new("🥇","gold medal"),
            new("🎖️","medal"), new("🎗️","reminder ribbon"), new("🎀","ribbon"), new("🎪","circus tent") ]),
        new("Nature", [ new("🌍","earth"), new("🌱","seedling"), new("🌿","herb"),
            new("🌸","cherry blossom"), new("🌺","hibiscus"), new("🌻","sunflower"),
            new("⛅","partly cloudy"), new("🌊","wave"), new("🦋","butterfly"),
            new("🍁","maple leaf"), new("🌴","palm tree"), new("🌙","moon") ]),
        new("Animals", [ new("🐶","dog"), new("🐱","cat"), new("🦊","fox"),
            new("🐻","bear"), new("🐼","panda"), new("🦁","lion"),
            new("🐯","tiger"), new("🐮","cow"), new("🐷","pig"),
            new("🐸","frog"), new("🦄","unicorn"), new("🐉","dragon") ]),
        new("Food", [ new("🍕","pizza"), new("🍔","burger"), new("🎂","cake"),
            new("🍦","ice cream"), new("🍎","apple"), new("🍊","tangerine"),
            new("🍋","lemon"), new("🍇","grapes"), new("🍓","strawberry"),
            new("☕","coffee"), new("🥑","avocado"), new("🍜","noodles") ]),
        new("Objects", [ new("💡","bulb"), new("🔍","search"), new("🔑","key"),
            new("🔒","lock"), new("⚙️","gear"), new("🛠️","tools"),
            new("📱","phone"), new("💻","laptop"), new("🎮","game controller"),
            new("🔬","microscope"), new("🔭","telescope"), new("🧪","test tube") ]),
        new("Documents", [ new("📝","memo"), new("📄","document"), new("📋","clipboard"),
            new("📊","chart"), new("📈","chart up"), new("📉","chart down"),
            new("📌","pushpin"), new("📚","books"), new("📖","open book"),
            new("✏️","pencil"), new("💼","briefcase"), new("🗂️","folder") ]),
        new("Travel", [ new("🚀","rocket"), new("✈️","airplane"), new("🚂","train"),
            new("🚗","car"), new("🏠","house"), new("🏢","office"),
            new("🏛️","building"), new("🗺️","world map"), new("🧭","compass"),
            new("🏖️","beach"), new("⛵","sailboat"), new("🌆","city") ]),
    ];

    // ── Cover presets ─────────────────────────────────────────────────────────

    private static readonly IReadOnlyList<CoverPreset> _coverPresets =
    [
        new("linear-gradient(135deg,#667eea 0%,#764ba2 100%)", "Purple"),
        new("linear-gradient(135deg,#f093fb 0%,#f5576c 100%)", "Pink"),
        new("linear-gradient(135deg,#4facfe 0%,#00f2fe 100%)", "Blue"),
        new("linear-gradient(135deg,#43e97b 0%,#38f9d7 100%)", "Green"),
        new("linear-gradient(135deg,#fa709a 0%,#fee140 100%)", "Sunset"),
        new("linear-gradient(135deg,#a1c4fd 0%,#c2e9fb 100%)", "Sky"),
        new("linear-gradient(135deg,#ffecd2 0%,#fcb69f 100%)", "Peach"),
        new("linear-gradient(135deg,#2d3748 0%,#4a5568 100%)", "Dark"),
    ];

    // ── Records ───────────────────────────────────────────────────────────────

    private sealed record EmojiCategory(string Name, EmojiItem[] Emojis);
    private sealed record EmojiItem(string Char, string Name);
    private sealed record CoverPreset(string Background, string Label);

    // ── Dispose ───────────────────────────────────────────────────────────────

    public void Dispose() => _coverDragRef?.Dispose();
}
