using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Interfaces;

namespace Tempo.Blazor.Components.NotionEditor.Sidebar;

public partial class TmNotionSidebarFavorites : ComponentBase
{
    // ── Cascaded context ──────────────────────────────────────────────────────

    [CascadingParameter] private NotionEditorContext Context { get; set; } = default!;

    // ── Parameters ────────────────────────────────────────────────────────────

    [Parameter, EditorRequired] public IReadOnlyList<INotionPage> Pages        { get; set; } = [];
    [Parameter]                 public string?                    ActivePageId  { get; set; }
    [Parameter]                 public EventCallback              OnChanged     { get; set; }

    // ── Section expand ────────────────────────────────────────────────────────

    private bool _isExpanded = true;

    // ── Ordered items — client-side drag reorder ──────────────────────────────

    private List<INotionPage> _orderedItems = [];
    private HashSet<Guid>     _lastPageIds  = [];
    private int               _dragSource   = -1;
    private int               _dragOver     = -1;

    protected override void OnParametersSet()
    {
        var newIds = Pages.Select(p => p.Id).ToHashSet();
        if (!newIds.SetEquals(_lastPageIds))
        {
            _orderedItems = [..Pages];
            _lastPageIds  = newIds;
        }
    }

    // ── Computed ──────────────────────────────────────────────────────────────

    // ARIA state attributes are strings: a Blazor bool renders the invalid empty value (true)
    // or omits the attribute entirely (false) — never a valid "true"/"false" state.
    private static string BoolText(bool value) => value ? "true" : "false";

    private bool IsActive(INotionPage page) =>
        ActivePageId is not null &&
        page.Id.ToString().Equals(ActivePageId, StringComparison.OrdinalIgnoreCase);

    private string GetTitle(INotionPage page) =>
        string.IsNullOrWhiteSpace(page.Title)
            ? Loc["TmNotionSidebar_Untitled"]
            : page.Title;

    private string GetIcon(INotionPage page) =>
        string.IsNullOrEmpty(page.IconEmoji) ? "📄" : page.IconEmoji;

    // ── Navigation ────────────────────────────────────────────────────────────

    private async Task NavigateAsync(INotionPage page)
    {
        if (Context.NavigateTo is not null)
            await Context.NavigateTo(page.Id.ToString());
    }

    // ── Unfavorite ────────────────────────────────────────────────────────────

    private async Task UnfavoriteAsync(INotionPage page)
    {
        try
        {
            await Context.DataProvider.ToggleFavoriteAsync(page.Id.ToString(), false);
            await OnChanged.InvokeAsync();
        }
        catch { }
    }

    // ── Drag & drop reorder ───────────────────────────────────────────────────

    private void OnDragStart(int index, DragEventArgs e)
    {
        e.DataTransfer.EffectAllowed = "move";
        _dragSource = index;
    }

    private void OnDragEnd(DragEventArgs e)
    {
        _dragSource = -1;
        _dragOver   = -1;
    }

    private void OnDragOver(int index, DragEventArgs e)
    {
        if (_dragSource < 0 || _dragSource == index) return;
        _dragOver = index;
    }

    private void OnDragLeave(int index)
    {
        if (_dragOver == index) _dragOver = -1;
    }

    private void OnDrop(int index, DragEventArgs e)
    {
        if (_dragSource < 0 || _dragSource == index)
        {
            _dragSource = -1;
            _dragOver   = -1;
            return;
        }

        var item     = _orderedItems[_dragSource];
        _orderedItems.RemoveAt(_dragSource);

        var insertAt = index > _dragSource ? index - 1 : index;
        _orderedItems.Insert(Math.Clamp(insertAt, 0, _orderedItems.Count), item);

        _dragSource = -1;
        _dragOver   = -1;
    }
}
