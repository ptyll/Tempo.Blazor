using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Globalization;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.UI;

public partial class TmNotionMentionMenu : ComponentBase
{
    // ── DI ───────────────────────────────────────────────────────────────────

    [Inject] private IJSRuntime JS { get; set; } = default!;

    // ── Cascaded context ─────────────────────────────────────────────────────

    [CascadingParameter] private NotionEditorContext Context { get; set; } = default!;

    // ── Parameters ───────────────────────────────────────────────────────────

    [Parameter] public bool   Visible   { get; set; }
    [Parameter] public double Top       { get; set; }
    [Parameter] public double Left      { get; set; }

    /// <summary>When true only the Pages tab is shown (triggered by '[[').</summary>
    [Parameter] public bool   PagesOnly { get; set; }

    /// <summary>Raised when the user picks a mention. Args: (type, id, displayText).</summary>
    [Parameter] public EventCallback<(string Type, string Id, string Display)> OnItemSelected { get; set; }

    /// <summary>Raised when the user dismisses the menu.</summary>
    [Parameter] public EventCallback OnClosed { get; set; }

    // ── State ────────────────────────────────────────────────────────────────

    private enum MentionTab { People, Pages, Date }

    private MentionTab _tab;
    private string     _query         = string.Empty;
    private bool       _wasVisible;
    private double     _top;
    private double     _left;
    private bool       _loading;
    private bool       _needsFocus;
    private bool       _needsPositionAdjustment;
    private int        _selectedIndex;

    private IReadOnlyList<TmUser> _people = [];
    private IReadOnlyList<INotionPage>  _pages  = [];

    private ElementReference _menuRef;
    private ElementReference _inputRef;

    private string MenuStyle => string.Create(
        CultureInfo.InvariantCulture,
        $"--tm-nmm-anchor-top:{_top}px;--tm-nmm-anchor-left:{_left}px;top:max(8px,min({_top}px,calc(100vh - 360px - 8px)));left:max(8px,min({_left}px,calc(100vw - 280px - 8px)))");

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override async Task OnParametersSetAsync()
    {
        if (Visible && !_wasVisible)
        {
            _top           = Top;
            _left          = Left;
            _query         = string.Empty;
            _selectedIndex = 0;
            _people        = [];
            _pages         = [];
            _tab           = PagesOnly ? MentionTab.Pages : MentionTab.People;
            _needsFocus    = true;
            _needsPositionAdjustment = true;
            await SearchAsync();
        }
        else if (!Visible && _wasVisible)
        {
            _query  = string.Empty;
            _people = [];
            _pages  = [];
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
                // Not a plain FocusAsync: the keystroke that opened this menu also queues a
                // content update whose render can refocus the block's editable a frame later
                // and silently steal the typing target back. focusMenuInput re-asserts focus
                // across the next frames so the search box wins that race.
                await JS.InvokeVoidAsync("tmNotionEditor.focusMenuInput", _inputRef);
            }
            catch { /* SSR / test */ }
        }

        if (_needsPositionAdjustment && Visible)
        {
            _needsPositionAdjustment = false;
            try { await JS.InvokeVoidAsync("tmNotionEditor.adjustSlashMenuPosition", _menuRef); }
            catch { /* SSR / test */ }
        }
    }

    // ── Tab switching ──────────────────────────────────────────────────────────

    private async Task SwitchTabAsync(MentionTab tab)
    {
        if (_tab == tab) return;
        _tab           = tab;
        _selectedIndex = 0;
        _query         = string.Empty;
        await SearchAsync();
    }

    // ── Search ────────────────────────────────────────────────────────────────

    private async Task HandleQueryInputAsync(ChangeEventArgs e)
    {
        _query         = e.Value?.ToString() ?? string.Empty;
        _selectedIndex = 0;
        await SearchAsync();
    }

    private async Task SearchAsync()
    {
        _loading = true;
        StateHasChanged();
        try
        {
            if (_tab == MentionTab.People && !PagesOnly)
            {
                _people = Context.MentionProvider is null
                    ? []
                    : await Context.MentionProvider.SearchAsync(new TmPeopleQuery { SearchText = _query, Take = 8 });
            }
            else if (_tab == MentionTab.Pages)
            {
                _pages = Context.SearchProvider is null
                    ? []
                    : (await Context.SearchProvider.SearchPagesAsync(_query, null)).ToList();
            }
        }
        catch { }
        finally
        {
            _loading = false;
            _needsPositionAdjustment = true;
            StateHasChanged();
        }
    }

    // ── Keyboard navigation ────────────────────────────────────────────────────

    private async Task HandleKeyDownAsync(KeyboardEventArgs e)
    {
        var total = TotalItems;
        switch (e.Key)
        {
            case "ArrowDown":
                if (total > 0) _selectedIndex = (_selectedIndex + 1) % total;
                break;
            case "ArrowUp":
                if (total > 0) _selectedIndex = (_selectedIndex - 1 + total) % total;
                break;
            case "Enter":
                await SelectCurrentAsync();
                return;
            case "Escape":
                await HandleBackdropAsync();
                return;
        }
    }

    // ── Selection ─────────────────────────────────────────────────────────────

    private int TotalItems => _tab switch
    {
        MentionTab.People => _people.Count,
        MentionTab.Pages  => _pages.Count,
        MentionTab.Date   => 3,
        _                 => 0
    };

    private async Task SelectCurrentAsync()
    {
        if (_tab == MentionTab.People && _selectedIndex < _people.Count)
            await SelectUserAsync(_people[_selectedIndex]);
        else if (_tab == MentionTab.Pages && _selectedIndex < _pages.Count)
            await SelectPageAsync(_pages[_selectedIndex]);
        else if (_tab == MentionTab.Date)
            await SelectDateByIndexAsync(_selectedIndex);
    }

    internal async Task SelectUserAsync(TmUser user) =>
        await OnItemSelected.InvokeAsync(("user", user.Id, "@" + UserDisplayName(user)));

    internal async Task SelectPageAsync(INotionPage page)
    {
        var icon  = string.IsNullOrEmpty(page.IconEmoji) ? "📄" : page.IconEmoji;
        var title = string.IsNullOrWhiteSpace(page.Title) ? Loc["TmNotionMentionMenu_Untitled"] : page.Title;
        await OnItemSelected.InvokeAsync(("page", page.Id.ToString(), icon + " " + title));
    }

    internal async Task SelectDateByIndexAsync(int idx)
    {
        var date = idx switch
        {
            1 => DateTime.Today.AddDays(1),
            2 => DateTime.Today.AddDays(-1),
            _ => DateTime.Today
        };
        await OnItemSelected.InvokeAsync(("date", date.ToString("yyyy-MM-dd"), "📅 " + date.ToString("MMMM d, yyyy")));
    }

    // ── Close ─────────────────────────────────────────────────────────────────

    private async Task HandleBackdropAsync()
    {
        try { await JS.InvokeVoidAsync("tmNotionEditor.cancelMentionTrigger"); }
        catch { /* SSR / test */ }
        await OnClosed.InvokeAsync();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static MarkupString DateIcon => new(
        "<svg class=\"tm-nmm__date-icon\" width=\"14\" height=\"14\" viewBox=\"0 0 14 14\" fill=\"none\" aria-hidden=\"true\">" +
        "<rect x=\"1\" y=\"2.5\" width=\"12\" height=\"11\" rx=\"1.5\" stroke=\"currentColor\" stroke-width=\"1.25\"/>" +
        "<path d=\"M1 5.5h12\" stroke=\"currentColor\" stroke-width=\"1.25\"/>" +
        "<path d=\"M4.5 1v3M9.5 1v3\" stroke=\"currentColor\" stroke-width=\"1.25\" stroke-linecap=\"round\"/>" +
        "</svg>");

    private static string UserDisplayName(TmUser user)
        => string.IsNullOrWhiteSpace(user.DisplayName) ? user.Id : user.DisplayName;

    private static string GetPageIcon(INotionPage page) =>
        string.IsNullOrEmpty(page.IconEmoji) ? "📄" : page.IconEmoji;

    private string GetPageTitle(INotionPage page) =>
        string.IsNullOrWhiteSpace(page.Title) ? Loc["TmNotionMentionMenu_Untitled"] : page.Title;
}
