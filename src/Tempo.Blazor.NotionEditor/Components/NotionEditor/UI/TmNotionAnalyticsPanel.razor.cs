using System.Globalization;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components;
using Tempo.Blazor.Components.Charts;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.Localization;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.UI;

/// <summary>Analytics summary panel for the active Notion space and page.</summary>
public partial class TmNotionAnalyticsPanel : TmComponentBase
{
    private static readonly IReadOnlyList<(int Days, string Key)> RangeOptions =
    [
        (7, "Notion_Analytics_Range7"),
        (14, "Notion_Analytics_Range14"),
        (30, "Notion_Analytics_Range30"),
    ];

    [CascadingParameter] private NotionEditorContext Context { get; set; } = default!;

    [Inject] private IServiceProvider Services { get; set; } = default!;

    /// <summary>Analytics provider used to query page views and top pages.</summary>
    [Parameter, EditorRequired] public INotionAnalyticsProvider AnalyticsProvider { get; set; } = default!;

    /// <summary>Space whose top pages should be displayed.</summary>
    [Parameter, EditorRequired] public string SpaceId { get; set; } = string.Empty;

    /// <summary>Currently open page id used for the main analytics summary. When null, the page
    /// metric cards stay hidden and only the range chart / top pages render.</summary>
    [Parameter] public Guid? CurrentPageId { get; set; }

    /// <summary>Maximum number of top pages to show.</summary>
    [Parameter] public int TopPageCount { get; set; } = 5;

    /// <summary>Additional CSS class.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Raised when the close button is clicked.</summary>
    [Parameter] public EventCallback OnClosed { get; set; }

    private readonly Dictionary<Guid, string> _pageTitles = [];
    private IReadOnlyList<PageAnalyticsDto> _topPages = [];
    private PageAnalyticsDto? _currentAnalytics;
    private bool _isLoading;
    private string? _loadError;
    private Guid? _loadedPageId;
    private string? _loadedSpaceId;
    private int _rangeDays = 14;

    private ITmRelativeTimeFormatter? _relativeTime;

    /// <summary>Cached per component — <see cref="TmRelativeTime.Resolve"/> allocates a fallback when none is registered.</summary>
    private ITmRelativeTimeFormatter RelativeTime => _relativeTime ??= TmRelativeTime.Resolve(Services, Loc);

    private string AbsoluteTime(DateTime dt) => RelativeTime.Absolute(dt);

    private PageAnalyticsDto CurrentSummary => _currentAnalytics ?? new PageAnalyticsDto
    {
        PageId = CurrentPageId ?? Guid.Empty
    };

    private ChartData ChartData
    {
        get
        {
            var points = CurrentSummary.ViewsByDay;
            return new ChartData
            {
                // Short month/day labels: a culture "d" ("7/14/2026") clips the last tick
                // on a narrow panel. M/d stays inside the plot padding.
                Labels = points.Select(point => point.Date.ToString("M/d", CultureInfo.InvariantCulture)).ToArray(),
                Datasets =
                [
                    new ChartDataset
                    {
                        Label = Loc["Notion_Analytics_Views"],
                        Values = points.Select(point => (double)point.Views).ToArray(),
                        Color = "var(--tm-color-primary)"
                    }
                ]
            };
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        if (_loadedPageId == CurrentPageId && string.Equals(_loadedSpaceId, SpaceId, StringComparison.OrdinalIgnoreCase))
            return;

        _loadedPageId = CurrentPageId;
        _loadedSpaceId = SpaceId;
        await LoadAsync();
    }

    private async Task SetRangeAsync(int days)
    {
        if (_rangeDays == days)
            return;
        _rangeDays = days;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _isLoading = true;
        _loadError = null;
        StateHasChanged();

        try
        {
            _currentAnalytics = CurrentPageId is { } pageId
                ? await AnalyticsProvider.GetPageAnalyticsAsync(pageId)
                : null;

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            _topPages = string.IsNullOrWhiteSpace(SpaceId)
                ? []
                : await AnalyticsProvider.GetTopPagesAsync(
                    SpaceId,
                    new NotionAnalyticsRange
                    {
                        From = today.AddDays(-(_rangeDays - 1)),
                        To = today,
                        Take = Math.Clamp(TopPageCount, 1, 20)
                    });

            await ResolveTopPageTitlesAsync();
        }
        catch
        {
            _currentAnalytics = null;
            _topPages = [];
            _pageTitles.Clear();
            _loadError = Loc["Notion_Analytics_LoadError"];
        }
        finally
        {
            _isLoading = false;
            StateHasChanged();
        }
    }

    /// <summary>
    /// Resolves top-page titles through the data provider, caching per page id so a range switch
    /// or reload never re-fetches an already-resolved title (the old path re-queried on every load).
    /// </summary>
    private async Task ResolveTopPageTitlesAsync()
    {
        foreach (var analytics in _topPages)
        {
            if (_pageTitles.ContainsKey(analytics.PageId))
                continue;

            try
            {
                var page = await Context.DataProvider.GetPageAsync(analytics.PageId.ToString("D"));
                _pageTitles[analytics.PageId] = string.IsNullOrWhiteSpace(page.Title)
                    ? Loc["TmNotionEditor_Untitled"]
                    : page.Title;
            }
            catch
            {
                _pageTitles[analytics.PageId] = Loc["Notion_Analytics_UnknownPage"];
            }
        }
    }

    private string GetPageTitle(Guid pageId)
        => _pageTitles.TryGetValue(pageId, out var title) ? title : Loc["Notion_Analytics_UnknownPage"];

    private static double GetTopPagePercent(PageAnalyticsDto analytics, IReadOnlyList<PageAnalyticsDto> pages)
    {
        var max = pages.Count == 0 ? 0 : pages.Max(page => page.Views);
        return max <= 0 ? 0 : Math.Clamp(analytics.Views / (double)max * 100, 0, 100);
    }
}
