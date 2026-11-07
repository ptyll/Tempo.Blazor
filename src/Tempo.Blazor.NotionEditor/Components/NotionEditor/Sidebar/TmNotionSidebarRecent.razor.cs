using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Interfaces;

namespace Tempo.Blazor.Components.NotionEditor.Sidebar;

public partial class TmNotionSidebarRecent : ComponentBase
{
    [Inject] private IServiceProvider Services { get; set; } = default!;

    // ── Cascaded context ──────────────────────────────────────────────────────

    [CascadingParameter] private NotionEditorContext Context { get; set; } = default!;

    // ── Parameters ────────────────────────────────────────────────────────────

    [Parameter, EditorRequired] public IReadOnlyList<INotionPage> Pages        { get; set; } = [];
    [Parameter]                 public string?                    ActivePageId  { get; set; }

    // ── Section expand ────────────────────────────────────────────────────────

    private bool _isExpanded;
    private bool _hasUserToggledExpanded;

    protected override void OnParametersSet()
    {
        if (!_hasUserToggledExpanded && Pages.Count == 0)
            _isExpanded = true;
    }

    // ── Computed ──────────────────────────────────────────────────────────────

    private bool IsActive(INotionPage page) =>
        ActivePageId is not null &&
        page.Id.ToString().Equals(ActivePageId, StringComparison.OrdinalIgnoreCase);

    private string GetTitle(INotionPage page) =>
        string.IsNullOrWhiteSpace(page.Title)
            ? Loc["TmNotionSidebar_Untitled"]
            : page.Title;

    private string GetIcon(INotionPage page) =>
        string.IsNullOrEmpty(page.IconEmoji) ? "📄" : page.IconEmoji;

    private ITmRelativeTimeFormatter? _relativeTime;

    private ITmRelativeTimeFormatter RelativeTime => _relativeTime ??= TmRelativeTime.Resolve(Services, Loc);

    private string GetRelativeTime(DateTime utcTime) => RelativeTime.Format(new DateTimeOffset(DateTime.SpecifyKind(utcTime, DateTimeKind.Utc)));

    private void ToggleExpanded()
    {
        _hasUserToggledExpanded = true;
        _isExpanded = !_isExpanded;
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    private async Task NavigateAsync(INotionPage page)
    {
        if (Context.NavigateTo is not null)
            await Context.NavigateTo(page.Id.ToString());
    }
}
