using System.Globalization;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.Page;

public partial class TmNotionPageInfoPanel : ComponentBase
{
    [CascadingParameter] private NotionEditorContext Context { get; set; } = default!;

    // The viewport scope: the unnamed cascade is the shell container, whose resolved mode can
    // stay on the band the panel opened in. A host that already resolved the viewport passes it.
    [CascadingParameter(Name = TmLayoutScopes.Viewport)] private TmLayoutContext? Layout { get; set; }

    /// <summary>Forced layout. <see cref="TmLayoutMode.Auto"/> reads the viewport scope.</summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    private TmLayoutMode ResolvedLayout => LayoutMode != TmLayoutMode.Auto ? LayoutMode : Layout?.Resolved ?? TmLayoutMode.Auto;

    /// <summary>The page whose metadata should be displayed.</summary>
    [Parameter, EditorRequired]
    public INotionPage Page { get; set; } = default!;

    /// <summary>Currently loaded page blocks. When absent, the component loads them from the block provider.</summary>
    [Parameter]
    public IReadOnlyList<IPageBlock>? Blocks { get; set; }

    /// <summary>Shows the panel when true.</summary>
    [Parameter]
    public bool Visible { get; set; }

    /// <summary>Raised when the visibility changes.</summary>
    [Parameter]
    public EventCallback<bool> VisibleChanged { get; set; }

    private PageStats _stats = PageStats.Empty;
    private PageAnalyticsDto? _analytics;
    private IReadOnlyDictionary<string, string> _userDisplayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, TmUser> _resolvedUsers = new Dictionary<string, TmUser>(StringComparer.OrdinalIgnoreCase);
    private Guid _loadedPageId;
    private IReadOnlyList<IPageBlock>? _loadedBlocksReference;
    private bool _wasVisible;

    protected override async Task OnParametersSetAsync()
    {
        if (!Visible)
        {
            _wasVisible = false;
            return;
        }

        if (_wasVisible && _loadedPageId == Page.Id && ReferenceEquals(_loadedBlocksReference, Blocks))
            return;

        _wasVisible = true;
        _loadedPageId = Page.Id;
        _loadedBlocksReference = Blocks;
        await LoadPanelDataAsync();
    }

    private async Task LoadPanelDataAsync()
    {
        var blocks = Blocks ?? (await Context.BlockService.GetBlocksAsync(Page.Id.ToString("D"))).ToArray();
        var fragments = new List<string?>();
        foreach (var block in blocks.OrderBy(block => block.Order))
        {
            await CollectTextFragmentsAsync(block, fragments);
        }

        _stats = PageStats.Calculate(fragments);
        _analytics = null;
        var resolution = await ResolveUsersAsync(Page.CreatedByUserId, Page.LastEditedByUserId);
        _userDisplayNames = resolution.DisplayNames;
        _resolvedUsers    = resolution.Users;

        if (Context.AnalyticsProvider is null)
            return;

        try
        {
            _analytics = await Context.AnalyticsProvider.GetPageAnalyticsAsync(Page.Id);
        }
        catch
        {
            _analytics = null;
        }
    }

    private async Task CollectTextFragmentsAsync(IPageBlock block, List<string?> fragments)
    {
        fragments.AddRange(ExtractTextFragments(block.Content));

        try
        {
            var children = await Context.BlockService.GetChildBlocksAsync(block.Id.ToString("D"));
            foreach (var child in children.OrderBy(item => item.Order))
                await CollectTextFragmentsAsync(child, fragments);
        }
        catch
        {
        }
    }

    private static IEnumerable<string?> ExtractTextFragments(IBlockContent? content)
    {
        return content switch
        {
            ITextBlockContent text => [text.Html],
            ITableRowBlockContent row => row.RichCells.Select(cell => cell.Html),
            ICodeBlockContent code => [code.Code, code.Caption],
            IChildPageBlockContent child => [child.Title],
            ILinkedPageBlockContent linked => [linked.Title],
            IBookmarkBlockContent bookmark => [bookmark.Title],
            IInlineDatabaseBlockContent database => [database.Title],
            IExcerptBlockContent excerpt => [excerpt.Html],
            IPagePropertiesBlockContent properties => properties.Rows.SelectMany(row => new[] { row.Key, row.ValueHtml }),
            IPagePropertiesReportBlockContent report => report.Labels.Concat(report.Columns),
            IWorkItemBlockContent workItem => [workItem.CachedSnapshot?.Title],
            IImageBlockContent image => [image.Caption, image.AltText],
            IFileBlockContent file => [file.FileName, file.Caption],
            IMediaBlockContent media => [media.Caption],
            _ => []
        };
    }

    private string FormatUser(string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return Loc["Notion_PageInfo_UnknownUser"];

        return _userDisplayNames.TryGetValue(userId.Trim(), out var displayName)
            ? displayName
            : Loc["Notion_PageInfo_UnknownUser"];
    }

    private string ResolveUserName(string? userId)
        => !string.IsNullOrWhiteSpace(userId)
        && _resolvedUsers.TryGetValue(userId.Trim(), out var user)
        && !string.IsNullOrWhiteSpace(user.DisplayName)
            ? user.DisplayName
            : FormatUser(userId);

    private string? ResolveUserAvatar(string? userId)
        => !string.IsNullOrWhiteSpace(userId)
        && _resolvedUsers.TryGetValue(userId.Trim(), out var user)
            ? user.AvatarUrl
            : null;

    private async Task<(IReadOnlyDictionary<string, string> DisplayNames, IReadOnlyDictionary<string, TmUser> Users)> ResolveUsersAsync(params string?[] userIds)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var users = new Dictionary<string, TmUser>(StringComparer.OrdinalIgnoreCase);
        if (Context.MentionProvider is null)
            return (names, users);

        foreach (var userId in userIds.Select(id => id?.Trim()).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var match = await Context.MentionProvider.GetByIdAsync(userId!);
                if (match is null)
                {
                    var found = await Context.MentionProvider.SearchAsync(new TmPeopleQuery { SearchText = userId, Take = 8 });
                    match = found.FirstOrDefault(user =>
                        string.Equals(user.Id, userId, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(user.UserName, userId, StringComparison.OrdinalIgnoreCase));
                }

                if (match is not null && !string.IsNullOrWhiteSpace(match.DisplayName))
                {
                    names[userId!] = match.DisplayName;
                    users[userId!] = match;
                }
            }
            catch
            {
            }
        }

        return (names, users);
    }

    private static string FormatDate(DateTime date)
        => date.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    private async Task CloseAsync()
    {
        if (!Visible)
            return;

        Visible = false;
        await VisibleChanged.InvokeAsync(false);
    }
}
