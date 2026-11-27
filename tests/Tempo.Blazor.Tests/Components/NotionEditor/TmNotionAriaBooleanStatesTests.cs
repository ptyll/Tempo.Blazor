using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Abstractions.WorkItems;
using Tempo.Blazor.Components.NotionEditor.Page;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.Components.NotionEditor.Sidebar;
using Tempo.Blazor.Components.NotionEditor.UI;
using Tempo.Blazor.Models;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Helpers;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;
using Tempo.Blazor.NotionEditor.Services;
using Tempo.Blazor.Services;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>
/// Plan b75365fb Fáze 4i — the boolean ARIA states of the NotionEditor page and sidebar surfaces
/// render as STRINGS in both positions: <c>aria-expanded</c> / <c>aria-pressed</c> /
/// <c>aria-selected</c> / <c>aria-checked</c> carry <c>"true"</c> or <c>"false"</c> — never the
/// invalid empty value a Blazor bool produces, never an omitted attribute. Every state is
/// exercised in both positions (disclosure open/closed, checkbox on/off, pressed/unpressed,
/// selected/unselected) and the <c>menuitemcheckbox</c> role is pinned next to
/// <c>aria-checked</c>. Two surfaces carry the state in the NAME instead of a state attribute
/// (APG — a changing name plus a state attribute would announce the state twice): the watch
/// toggle renders NO <c>aria-pressed</c> at all and the settings-menu favorite item keeps the
/// invariant name "Favorite" with <c>aria-checked</c> carrying the state. These are the bUnit
/// halves of the frozen-list entries removed from
/// <c>AriaBooleanStateAttributeGuardTests</c> in this phase.
/// </summary>
public sealed class TmNotionAriaBooleanStatesTests : LocalizationTestBase
{
    public TmNotionAriaBooleanStatesTests()
    {
        Services.AddSingleton<ITmNotificationService, NoOpNotificationService>();
        Services.AddScoped<CommentNotificationOrchestrator>();
        UseCustomLocalization(new Dictionary<string, string>
        {
            ["Tm_Cancel"] = "Cancel",
            ["Tm_Retry"] = "Retry",
            ["Tm_Loading"] = "Loading",
            ["TmNotionPageSettingsMenu_TriggerLabel"] = "Page settings",
            ["TmNotionPageSettingsMenu_FullWidth"] = "Full width",
            ["TmNotionPageSettingsMenu_SmallText"] = "Small text",
            ["TmNotionPageSettingsMenu_LockPage"] = "Lock page",
            ["TmNotionPageSettingsMenu_Export"] = "Export",
            ["TmNotionPageSettingsMenu_Import"] = "Import",
            ["TmNotionPageSettingsMenu_MoveTo"] = "Move to",
            ["TmNotionPageSettingsMenu_Favorite"] = "Favorite",
            ["Notion_Watch_Start"] = "Watch",
            ["Notion_Watch_Stop"] = "Stop watching",
            ["Notion_Watch_IncludeChildren"] = "Include subpages",
            ["TmNotionSidebar_Recent"] = "Recent",
            ["TmNotionSidebar_Favorites"] = "Favorites",
            ["TmNotionSidebar_Pages"] = "Pages",
            ["TmNotionSidebar_Untitled"] = "Untitled",
            ["TmNotionSidebar_NoRecent"] = "No recent pages",
            ["TmNotionSidebar_NoFavorites"] = "No favorites",
            ["TmNotionSidebar_Search"] = "Search",
            ["TmNotionSidebar_SearchPlaceholder"] = "Search pages",
            ["TmNotionSidebar_SearchLoading"] = "Searching",
            ["TmNotionSidebar_SearchNoResults"] = "No pages found",
            ["TmNotionSidebar_CloseSearch"] = "Close search",
            ["TmNotionSidebar_NewPage"] = "New page",
            ["TmNotionSidebar_Loading"] = "Loading",
            ["TmNotionSidebar_LoadError"] = "Sidebar could not be loaded.",
            ["TmNotionSidebar_Trash"] = "Trash",
            ["TmNotionSidebar_TrashOpen"] = "Open trash",
            ["TmNotionEditor_SidebarLabel"] = "Pages",
            ["TmNotionPageTree_Untitled"] = "Untitled",
            ["TmNotionPageTree_OpenMenu"] = "Open menu",
            ["TmNotionPageTree_ExpandNode"] = "Expand",
            ["TmNotionPageTree_CollapseNode"] = "Collapse",
            ["TmNotionPageTree_NewChildPage"] = "New sub-page",
            ["TmNotionPageComment_Toggle"] = "Comments",
            ["TmNotionPageComment_PanelLabel"] = "Page comments",
            ["Notion_Tasks_Title"] = "My Tasks",
            ["Notion_Tasks_Count"] = "{0} tasks",
            ["Notion_Tasks_Filters"] = "Task filters",
            ["Notion_Tasks_FiltersOpen"] = "Filters ({0})",
            ["Notion_Tasks_Filter_Assignee"] = "Assignee filter",
            ["Notion_Tasks_Filter_My"] = "Mine",
            ["Notion_Tasks_Filter_All"] = "All",
            ["Notion_Tasks_Filter_Status"] = "Status filter",
            ["Notion_Tasks_Filter_Open"] = "Open",
            ["Notion_Tasks_Filter_Completed"] = "Done",
            ["Notion_Tasks_Filter_AllStatuses"] = "All statuses",
            ["Notion_Tasks_Filter_Due"] = "Due date filter",
            ["Notion_Tasks_Filter_DueAll"] = "Any due date",
            ["Notion_Tasks_Filter_Overdue"] = "Overdue",
            ["Notion_Tasks_Filter_Upcoming"] = "Upcoming",
            ["Notion_Tasks_GroupBy"] = "Group tasks by",
            ["Notion_Tasks_GroupBy_DueDate"] = "Due date",
            ["Notion_Tasks_GroupBy_Page"] = "Page",
            ["Notion_Tasks_LoadError"] = "Tasks could not be loaded.",
            ["Notion_Tasks_Empty"] = "No tasks match the current filters.",
            ["Notion_Tasks_ToggleComplete"] = "Toggle task completion",
            ["Notion_Tasks_ToggleCompleteFor"] = "Toggle completion for {0}",
            ["Notion_Tasks_Group_Overdue"] = "Overdue",
            ["Notion_Tasks_Group_Today"] = "Today",
            ["Notion_Tasks_Group_Tomorrow"] = "Tomorrow",
            ["Notion_Tasks_Group_Later"] = "Later",
            ["Notion_Tasks_Group_NoDueDate"] = "No due date",
            ["TmNotionPageHistory_DiffModeInline"] = "Inline",
            ["TmNotionPageHistory_DiffModeSideBySide"] = "Side by side",
            ["TmNotionPageHistory_ExitDiff"] = "Exit diff",
        });
    }

    // ── TmNotionPageSettingsMenu ───────────────────────────────────────────────────────────

    [Fact]
    public void SettingsMenu_Trigger_AnnouncesExpandedAsString_InBothStates()
    {
        var cut = RenderSettingsMenu(SettingsPage());

        var trigger = cut.Find(".tm-npsm-trigger");
        trigger.GetAttribute("aria-expanded").Should().Be("false",
            "a closed menu announces the string \"false\" — never an omitted attribute");

        trigger.Click();
        cut.Find(".tm-npsm-trigger").GetAttribute("aria-expanded").Should().Be("true");

        cut.Find(".tm-npsm-trigger").Click();
        cut.Find(".tm-npsm-trigger").GetAttribute("aria-expanded").Should().Be("false");
    }

    [Fact]
    public void SettingsMenu_Submenus_AnnounceExpandedAsString_InBothStates()
    {
        var cut = RenderSettingsMenu(SettingsPage());
        cut.Find(".tm-npsm-trigger").Click();

        // Export / Import / Move to disclosures, in menu order.
        var disclosures = cut.FindAll(".tm-npsm__item[aria-haspopup='true']");
        disclosures.Should().HaveCount(3);
        disclosures.Should().OnlyContain(item => item.GetAttribute("aria-expanded") == "false",
            "a closed submenu announces the string \"false\"");

        disclosures[0].Click(); // Export
        cut.FindAll(".tm-npsm__item[aria-haspopup='true']")[0]
            .GetAttribute("aria-expanded").Should().Be("true");

        cut.FindAll(".tm-npsm__item[aria-haspopup='true']")[1].Click(); // Import
        cut.FindAll(".tm-npsm__item[aria-haspopup='true']")[0]
            .GetAttribute("aria-expanded").Should().Be("false", "opening Import closes Export");
        cut.FindAll(".tm-npsm__item[aria-haspopup='true']")[1]
            .GetAttribute("aria-expanded").Should().Be("true");

        cut.FindAll(".tm-npsm__item[aria-haspopup='true']")[2].Click(); // Move to
        cut.FindAll(".tm-npsm__item[aria-haspopup='true']")[1]
            .GetAttribute("aria-expanded").Should().Be("false", "opening Move to closes Import");
        cut.FindAll(".tm-npsm__item[aria-haspopup='true']")[2]
            .GetAttribute("aria-expanded").Should().Be("true");
    }

    [Fact]
    public void SettingsMenu_CheckboxItems_KeepTheRole_AndAnnounceCheckedAsString()
    {
        var cut = RenderSettingsMenu(SettingsPage(isFullWidth: true, isSmallText: false, isLocked: true, isFavorite: false));
        cut.Find(".tm-npsm-trigger").Click();

        var items = cut.FindAll("[role='menuitemcheckbox']");
        items.Should().HaveCount(4, "Full width, Small text, Lock page and the favorite toggle");

        // Menu order: Full width, Small text, Lock page … the favorite toggle.
        items[0].GetAttribute("aria-checked").Should().Be("true");
        items[1].GetAttribute("aria-checked").Should().Be("false",
            "an unchecked menuitemcheckbox announces the string \"false\" — never an omitted attribute");
        items[2].GetAttribute("aria-checked").Should().Be("true");
        items[3].GetAttribute("aria-checked").Should().Be("false");
    }

    [Fact]
    public void SettingsMenu_FavoriteItem_KeepsTheNameInBothStates_AriaCheckedCarriesTheState()
    {
        // APG: a menuitemcheckbox whose NAME changed with the state would announce the state
        // twice — the name stays "Favorite", aria-checked carries the state (the star icon
        // fills visually).
        var cut = Render<SettingsMenuFavoriteHost>(parameters => parameters
            .Add(p => p.Context, new NotionEditorContext
            {
                DataProvider = new StubDataProvider(),
                BlockService = new StubDataProvider(),
                ImportExportProvider = new StubImportExportProvider()
            })
            .Add(p => p.InitialPage, SettingsPage(isFavorite: false)));

        cut.Find(".tm-npsm-trigger").Click();
        var favorite = cut.FindAll("[role='menuitemcheckbox']")[3];
        favorite.QuerySelector(".tm-npsm__item-label")!.TextContent.Trim().Should().Be("Favorite");
        favorite.GetAttribute("aria-checked").Should().Be("false");

        // The click persists through the provider, raises OnPageUpdated and the host binds the
        // flipped page back; the menu closes on success.
        favorite.Click();
        cut.WaitForAssertion(() =>
            cut.FindAll("[role='menuitemcheckbox']").Should().BeEmpty("the menu closed after the toggle"));

        cut.Find(".tm-npsm-trigger").Click();
        var checkedFavorite = cut.FindAll("[role='menuitemcheckbox']")[3];
        checkedFavorite.QuerySelector(".tm-npsm__item-label")!.TextContent.Trim().Should().Be("Favorite",
            "the name is invariant in both states — aria-checked carries the state");
        checkedFavorite.GetAttribute("aria-checked").Should().Be("true");
    }

    // ── TmNotionWatchButton ────────────────────────────────────────────────────────────────

    [Fact]
    public void WatchButton_CarriesNoAriaPressed_TheChangingNameAnnouncesTheState()
    {
        // APG: a toggle whose NAME changes with the state ("Watch page" ↔ "Unwatch") must NOT
        // also carry aria-pressed — the changing name plus the pressed state would announce the
        // state twice (a double negation when unwatching). The state lives in the name alone.
        var cut = Render<TmNotionWatchButton>(parameters => parameters
            .Add(p => p.Provider, new StubWatchProvider())
            .Add(p => p.PageId, "page-1")
            .Add(p => p.UserId, "alice"));

        cut.WaitForAssertion(() =>
        {
            var toggle = cut.Find("[data-testid='notion-watch-toggle']");
            toggle.HasAttribute("aria-pressed").Should().BeFalse(
                "a toggle with a changing name carries no aria-pressed (APG)");
            toggle.GetAttribute("aria-label").Should().Be("Watch",
                "the unwatched page is named by the action it offers");
        });

        cut.Find("[data-testid='notion-watch-toggle']").Click();
        cut.WaitForAssertion(() =>
        {
            var toggle = cut.Find("[data-testid='notion-watch-toggle']");
            toggle.HasAttribute("aria-pressed").Should().BeFalse();
            toggle.GetAttribute("aria-label").Should().Be("Stop watching",
                "the watched page is named by the action it offers");
        });

        cut.Find("[data-testid='notion-watch-toggle']").Click();
        cut.WaitForAssertion(() =>
        {
            var toggle = cut.Find("[data-testid='notion-watch-toggle']");
            toggle.HasAttribute("aria-pressed").Should().BeFalse();
            toggle.GetAttribute("aria-label").Should().Be("Watch");
        });
    }

    // ── Sidebar sections ───────────────────────────────────────────────────────────────────

    [Fact]
    public void SidebarRecent_Header_AnnouncesExpandedAsString_InBothStates()
    {
        var cut = Render<CascadingValue<NotionEditorContext>>(parameters => parameters
            .Add(p => p.Value, new NotionEditorContext { DataProvider = new StubDataProvider() })
            .AddChildContent<TmNotionSidebarRecent>(child => child
                .Add(p => p.Pages, new[] { Page("Recent page") }.AsReadOnly())));

        var header = cut.Find(".tm-nsr-header");
        header.GetAttribute("aria-expanded").Should().Be("false",
            "a collapsed section announces the string \"false\"");

        header.Click();
        cut.Find(".tm-nsr-header").GetAttribute("aria-expanded").Should().Be("true");

        cut.Find(".tm-nsr-header").Click();
        cut.Find(".tm-nsr-header").GetAttribute("aria-expanded").Should().Be("false");
    }

    [Fact]
    public void SidebarRecent_EmptyList_StartsExpanded_WithTheStringTrue()
    {
        var cut = Render<CascadingValue<NotionEditorContext>>(parameters => parameters
            .Add(p => p.Value, new NotionEditorContext { DataProvider = new StubDataProvider() })
            .AddChildContent<TmNotionSidebarRecent>(child => child
                .Add(p => p.Pages, Array.Empty<INotionPage>().AsReadOnly())));

        cut.Find(".tm-nsr-header").GetAttribute("aria-expanded").Should().Be("true");
    }

    [Fact]
    public void SidebarFavorites_Header_AnnouncesExpandedAsString_InBothStates()
    {
        var cut = Render<CascadingValue<NotionEditorContext>>(parameters => parameters
            .Add(p => p.Value, new NotionEditorContext { DataProvider = new StubDataProvider() })
            .AddChildContent<TmNotionSidebarFavorites>(child => child
                .Add(p => p.Pages, Array.Empty<INotionPage>().AsReadOnly())));

        var header = cut.Find(".tm-nsf-header");
        header.GetAttribute("aria-expanded").Should().Be("true",
            "the favorites section starts expanded — the string \"true\"");

        header.Click();
        cut.Find(".tm-nsf-header").GetAttribute("aria-expanded").Should().Be("false");

        cut.Find(".tm-nsf-header").Click();
        cut.Find(".tm-nsf-header").GetAttribute("aria-expanded").Should().Be("true");
    }

    [Fact]
    public void Sidebar_PagesSection_AnnouncesExpandedAsString_InBothStates()
    {
        var provider = new StubDataProvider();
        var cut = Render<CascadingValue<NotionEditorContext>>(parameters => parameters
            .Add(p => p.Value, new NotionEditorContext { DataProvider = provider })
            .AddChildContent<TmNotionSidebar>());

        cut.WaitForAssertion(() =>
            cut.Find(".tm-ns-section__header").GetAttribute("aria-expanded").Should().Be("true",
                "the pages section starts expanded — the string \"true\""));

        cut.Find(".tm-ns-section__header").Click();
        cut.Find(".tm-ns-section__header").GetAttribute("aria-expanded").Should().Be("false");

        cut.Find(".tm-ns-section__header").Click();
        cut.Find(".tm-ns-section__header").GetAttribute("aria-expanded").Should().Be("true");
    }

    [Fact]
    public async Task Sidebar_SearchResults_AnnounceSelectedAsString()
    {
        var provider = new StubDataProvider();
        var cut = Render<CascadingValue<NotionEditorContext>>(parameters => parameters
            .Add(p => p.Value, new NotionEditorContext
            {
                DataProvider = provider,
                SearchProvider = provider
            })
            .AddChildContent<TmNotionSidebar>(child => child
                .Add(p => p.ActivePageId, StubDataProvider.ActivePageId.ToString("D"))));

        cut.WaitForAssertion(() => cut.Find(".tm-ns-btn-search").Should().NotBeNull());
        await cut.Find(".tm-ns-btn-search").ClickAsync(new MouseEventArgs());
        await cut.Find(".tm-ns-search__input").InputAsync(new ChangeEventArgs { Value = "page" });

        cut.WaitForAssertion(() =>
        {
            var options = cut.FindAll(".tm-ns-search__result");
            options.Should().HaveCount(2);
            options[0].GetAttribute("aria-selected").Should().Be("true",
                "the active page announces the string \"true\"");
            options[1].GetAttribute("aria-selected").Should().Be("false",
                "an inactive result keeps the attribute — the string \"false\"");
        });
    }

    // ── TmNotionPageTreeItem ───────────────────────────────────────────────────────────────

    [Fact]
    public void PageTreeItem_AnnouncesSelectedAsString_ForActiveAndInactivePages()
    {
        var provider = new StubDataProvider();
        var cut = Render<CascadingValue<NotionEditorContext>>(parameters => parameters
            .Add(p => p.Value, new NotionEditorContext { DataProvider = provider })
            .AddChildContent<TmNotionPageTree>(child => child
                .Add(p => p.RootPages, provider.RootPages)
                .Add(p => p.ActivePageId, StubDataProvider.ActivePageId.ToString("D"))));

        var items = cut.FindAll("li.tm-npt-item");
        items.Should().HaveCount(2);
        items[0].GetAttribute("aria-selected").Should().Be("true",
            "the active page announces the string \"true\"");
        items[1].GetAttribute("aria-selected").Should().Be("false",
            "an inactive page keeps the attribute — the string \"false\"");
    }

    [Fact]
    public void PageTreeItem_MenuButton_AnnouncesExpandedAsString_InBothStates()
    {
        var provider = new StubDataProvider();
        var cut = Render<CascadingValue<NotionEditorContext>>(parameters => parameters
            .Add(p => p.Value, new NotionEditorContext { DataProvider = provider })
            .AddChildContent<TmNotionPageTree>(child => child
                .Add(p => p.RootPages, provider.RootPages)));

        var menuButton = cut.FindAll(".tm-npt-btn[aria-haspopup='true']").First();
        menuButton.GetAttribute("aria-expanded").Should().Be("false",
            "a closed item menu announces the string \"false\"");

        menuButton.Click();
        cut.FindAll(".tm-npt-btn[aria-haspopup='true']").First()
            .GetAttribute("aria-expanded").Should().Be("true");

        // Closing through the backdrop returns the string "false".
        cut.Find(".tm-npt-backdrop").Click();
        cut.FindAll(".tm-npt-btn[aria-haspopup='true']").First()
            .GetAttribute("aria-expanded").Should().Be("false");
    }

    // ── TmNotionPageCommentPanel ───────────────────────────────────────────────────────────

    [Fact]
    public void PageCommentPanel_Toggle_AnnouncesExpandedAsString_InBothStates()
    {
        var cut = RenderCommentPanel(expanded: false);
        cut.Find(".tm-npcp__toggle").GetAttribute("aria-expanded").Should().Be("false",
            "a collapsed panel announces the string \"false\"");

        var expanded = RenderCommentPanel(expanded: true);
        expanded.Find(".tm-npcp__toggle").GetAttribute("aria-expanded").Should().Be("true");
    }

    // ── TmNotionMyTasks ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MyTasks_CheckButton_AnnouncesPressedAsString_InBothStates()
    {
        var today = DateTime.Today;
        var provider = new StubTaskProvider(
        [
            MakeTask("task-open", "Open task", today, isCompleted: false),
            MakeTask("task-done", "Done task", today.AddDays(-1), isCompleted: true),
        ]);

        var cut = Render<TmNotionMyTasks>(parameters => parameters
            .Add(p => p.WorkItemSource, provider)
            .Add(p => p.CurrentUserId, "alice"));

        cut.WaitForAssertion(() =>
            cut.Find("[data-task-id='task-open'] .tm-my-tasks__check")
                .GetAttribute("aria-pressed").Should().Be("false",
                    "an open task announces the string \"false\""));

        // Switch the status filter to Done — the completed task appears pressed.
        await cut.FindAll(".tm-my-tasks__segment")[1].QuerySelectorAll("button")
            .Single(button => button.TextContent.Trim() == "Done")
            .ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
            cut.Find("[data-task-id='task-done'] .tm-my-tasks__check")
                .GetAttribute("aria-pressed").Should().Be("true",
                    "a completed task announces the string \"true\""));
    }

    // ── TmNotionPageHistory ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PageHistory_DiffModeButtons_AnnouncePressedAsString_InBothStates()
    {
        var context = new NotionEditorContext { HistoryProvider = new StubHistoryProvider() };
        var host = Render<CascadingValue<NotionEditorContext>>(parameters => parameters
            .Add(p => p.Value, context)
            .AddChildContent<TmNotionPageHistory>(child => child
                .Add(p => p.Visible, true)
                .Add(p => p.PageId, StubHistoryProvider.PageId.ToString("D"))));

        host.WaitForAssertion(() => host.FindAll(".tm-nph__version-item").Should().HaveCount(2));

        await host.FindAll("[data-testid='notion-history-pick']")[1].ChangeAsync(new ChangeEventArgs { Value = true });
        await host.FindAll("[data-testid='notion-history-pick']")[0].ChangeAsync(new ChangeEventArgs { Value = true });
        await host.Find("[data-testid='notion-history-compare-selected']").ClickAsync(new MouseEventArgs());

        host.WaitForAssertion(() =>
        {
            host.Find("[data-testid='notion-diff-mode-inline']")
                .GetAttribute("aria-pressed").Should().Be("true",
                    "the inline mode is active — the string \"true\"");
            host.Find("[data-testid='notion-diff-mode-side-by-side']")
                .GetAttribute("aria-pressed").Should().Be("false",
                    "the inactive mode keeps the attribute — the string \"false\"");
        });

        await host.Find("[data-testid='notion-diff-mode-side-by-side']").ClickAsync(new MouseEventArgs());

        host.WaitForAssertion(() =>
        {
            host.Find("[data-testid='notion-diff-mode-side-by-side']")
                .GetAttribute("aria-pressed").Should().Be("true");
            host.Find("[data-testid='notion-diff-mode-inline']")
                .GetAttribute("aria-pressed").Should().Be("false");
        });
    }

    // ── Render helpers ─────────────────────────────────────────────────────────────────────

    private IRenderedComponent<CascadingValue<NotionEditorContext>> RenderSettingsMenu(INotionPage page)
        => Render<CascadingValue<NotionEditorContext>>(parameters => parameters
            .Add(p => p.Value, new NotionEditorContext
            {
                DataProvider = new StubDataProvider(),
                BlockService = new StubDataProvider(),
                ImportExportProvider = new StubImportExportProvider()
            })
            .AddChildContent<TmNotionPageSettingsMenu>(child => child
                .Add(p => p.Page, page)));

    private IRenderedComponent<CascadingValue<NotionEditorContext>> RenderCommentPanel(bool expanded)
        => Render<CascadingValue<NotionEditorContext>>(parameters => parameters
            .Add(p => p.Value, new NotionEditorContext { DataProvider = new StubDataProvider() })
            .AddChildContent<TmNotionPageCommentPanel>(child => child
                .Add(p => p.PageId, "page-1")
                .Add(p => p.Expanded, expanded)));

    private static NotionPage SettingsPage(bool isFullWidth = false, bool isSmallText = false,
        bool isLocked = false, bool isFavorite = false)
        => new()
        {
            Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Title = "Settings page",
            IsFullWidth = isFullWidth,
            IsSmallText = isSmallText,
            IsLocked = isLocked,
            IsFavorite = isFavorite,
            CreatedAt = DateTime.UtcNow,
            LastEditedAt = DateTime.UtcNow
        };

    private static NotionPage Page(string title, Guid? id = null)
        => new()
        {
            Id = id ?? Guid.NewGuid(),
            Title = title,
            CreatedAt = DateTime.UtcNow,
            LastEditedAt = DateTime.UtcNow
        };

    private static TmWorkItem MakeTask(string id, string title, DateTime dueDate, bool isCompleted)
        => new()
        {
            Id = id,
            SourceKey = "notion",
            OriginPageId = "page-a",
            OriginPageTitle = "Workspace Launch",
            OriginBlockId = $"block-{id}",
            Title = title,
            Assignees = [new TmWorkItemAssignee { Id = "alice", Name = "Alice" }],
            DueDate = dueDate,
            IsCompleted = isCompleted,
            Status = isCompleted ? TmWorkItemStatus.Done : TmWorkItemStatus.Open,
            CreatedAt = dueDate
        };

    // ── Stubs ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Host binding the settings menu's updated page back the way a real host does —
    /// the favorite toggle raises <c>OnPageUpdated</c> and the host re-renders with the flipped
    /// page (bUnit 2 dropped <c>SetParametersAndRender</c>, so the host owns the state).</summary>
    public sealed class SettingsMenuFavoriteHost : ComponentBase
    {
        [Parameter] public NotionEditorContext Context { get; set; } = default!;
        [Parameter] public INotionPage InitialPage { get; set; } = default!;

        private INotionPage? _page;

        protected override void OnInitialized() => _page = InitialPage;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<CascadingValue<NotionEditorContext>>(0);
            builder.AddAttribute(1, "Value", Context);
            builder.AddAttribute(2, "ChildContent", (RenderFragment)(childBuilder =>
            {
                childBuilder.OpenComponent<TmNotionPageSettingsMenu>(0);
                childBuilder.AddAttribute(1, "Page", _page);
                childBuilder.AddAttribute(2, "OnPageUpdated", EventCallback.Factory.Create<INotionPage>(
                    this, updated =>
                    {
                        _page = updated;
                        StateHasChanged();
                    }));
                childBuilder.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }

    private sealed class StubDataProvider : INotionDataProvider, INotionEditorBlockService, INotionSearchProvider
    {
        public static readonly Guid ActivePageId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        public static readonly Guid OtherPageId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

        private readonly NotionPage _active = Page("Active page", ActivePageId);
        private readonly NotionPage _other = Page("Other page", OtherPageId);

        public IReadOnlyList<INotionPage> RootPages => [_active, _other];

        public Task<INotionPage> GetPageAsync(string pageId) => Task.FromResult<INotionPage>(_active);
        public Task<IEnumerable<INotionPage>> GetChildPagesAsync(string? parentId)
            => Task.FromResult(parentId is null
                ? RootPages
                : Enumerable.Empty<INotionPage>());
        public Task<IEnumerable<INotionPage>> GetFavoritesAsync() => Task.FromResult(Enumerable.Empty<INotionPage>());
        public Task<IEnumerable<INotionPage>> GetRecentPagesAsync(int count) => Task.FromResult(Enumerable.Empty<INotionPage>());
        public Task<IEnumerable<INotionPage>> GetTrashAsync() => Task.FromResult(Enumerable.Empty<INotionPage>());
        public Task<INotionPage> CreatePageAsync(string? parentId, string title)
            => Task.FromResult<INotionPage>(Page(title));
        public Task UpdatePageAsync(INotionPage page) => Task.CompletedTask;
        public Task DeletePageAsync(string pageId) => Task.CompletedTask;
        public Task RestorePageAsync(string pageId) => Task.CompletedTask;
        public Task PermanentlyDeletePageAsync(string pageId) => Task.CompletedTask;
        public Task ToggleFavoriteAsync(string pageId, bool isFavorite) => Task.CompletedTask;
        public Task MovePageAsync(string pageId, string? newParentId) => Task.CompletedTask;
        public Task<INotionPage> DuplicatePageAsync(string pageId) => Task.FromResult<INotionPage>(Page("Duplicate"));
        public Task<IReadOnlyList<INotionPage>> GetPagesByLabelAsync(string label, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<INotionPage>>([]);
        public Task<IReadOnlyList<string>> GetAllLabelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>([]);
        public Task SetPageLabelsAsync(Guid pageId, IReadOnlyList<string> labels, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task<IEnumerable<IPageBlock>> GetBlocksAsync(string pageId) => Task.FromResult<IEnumerable<IPageBlock>>([]);
        public Task<IEnumerable<IPageBlock>> GetChildBlocksAsync(string parentBlockId) => Task.FromResult<IEnumerable<IPageBlock>>([]);
        public Task<IPageBlock> CreateBlockAsync(string pageId, IPageBlock block, string? afterBlockId) => Task.FromResult(block);
        public Task<IEnumerable<IPageBlock>> CreateBlocksAsync(string pageId, IEnumerable<IPageBlock> blocks, string? afterBlockId)
            => Task.FromResult(blocks);
        public Task UpdateBlockAsync(IPageBlock block) => Task.CompletedTask;
        public Task DeleteBlockAsync(string blockId) => Task.CompletedTask;
        public Task ReorderBlocksAsync(string pageId, IEnumerable<string> orderedBlockIds) => Task.CompletedTask;
        public Task MoveBlockAsync(MoveNotionBlockRequest request) => Task.CompletedTask;
        public Task MoveBlockToPageAsync(string blockId, string targetPageId, string? afterBlockId) => Task.CompletedTask;
        public Task<IPageBlock> DuplicateBlockAsync(string blockId)
            => Task.FromResult<IPageBlock>(new PageBlock { Id = Guid.NewGuid(), Type = BlockType.Paragraph, Content = new TextBlockContent() });
        public Task<IPageBlock> ConvertBlockTypeAsync(string blockId, BlockType newType)
            => Task.FromResult<IPageBlock>(new PageBlock { Id = Guid.Parse(blockId), Type = newType, Content = new TextBlockContent() });
        public Task<string> GetBlockLinkAsync(string blockId) => Task.FromResult($"#{blockId}");

        public Task<IEnumerable<INotionPage>> SearchPagesAsync(string query, NotionSearchFilter? filter)
            => Task.FromResult(RootPages.Where(page => page.Title.Contains(query, StringComparison.OrdinalIgnoreCase)));
        public Task<IEnumerable<NotionSearchResult>> SearchBlocksAsync(string query, NotionSearchFilter? filter)
            => Task.FromResult(Enumerable.Empty<NotionSearchResult>());
        public Task<(IEnumerable<INotionPage> Pages, IEnumerable<NotionSearchResult> Blocks)> SearchAllAsync(
            string query, NotionSearchFilter? filter, int maxResults)
            => Task.FromResult<(IEnumerable<INotionPage>, IEnumerable<NotionSearchResult>)>((RootPages, []));
    }

    private sealed class StubImportExportProvider : INotionImportExportProvider
    {
        public Task<Stream> ExportPageAsync(string pageId, NotionExportFormat format)
            => Task.FromResult<Stream>(new MemoryStream([1]));
        public Task<Stream> ExportPageWithSubpagesAsync(string pageId, NotionExportFormat format)
            => Task.FromResult<Stream>(new MemoryStream([1]));
        public Task<INotionPage> ImportAsync(Stream content, NotionImportFormat format, string? targetParentPageId)
            => Task.FromResult<INotionPage>(Page("Imported"));
    }

    private sealed class StubWatchProvider : INotionWatchProvider
    {
        private readonly HashSet<(string PageId, string UserId)> _watching = [];

        public Task WatchAsync(string pageId, string userId, bool includeChildren, CancellationToken cancellationToken = default)
        {
            _watching.Add((pageId, userId));
            return Task.CompletedTask;
        }

        public Task UnwatchAsync(string pageId, string userId, CancellationToken cancellationToken = default)
        {
            _watching.Remove((pageId, userId));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<NotionWatchSubscriptionDto>> GetWatchersAsync(string pageId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<NotionWatchSubscriptionDto>>(
                _watching.Where(w => w.PageId == pageId)
                    .Select(w => new NotionWatchSubscriptionDto { PageId = w.PageId, UserId = w.UserId })
                    .ToList());

        public Task<bool> IsWatchingAsync(string pageId, string userId, CancellationToken cancellationToken = default)
            => Task.FromResult(_watching.Contains((pageId, userId)));
    }

    private sealed class StubTaskProvider : TmWorkItemProviderBase
    {
        private readonly List<TmWorkItem> _tasks;

        public StubTaskProvider(IEnumerable<TmWorkItem> tasks) => _tasks = tasks.ToList();

        public override string SourceKey => "notion";
        public override string DisplayName => "Notion tasks";
        public override TmWorkItemCapabilities Capabilities => TmWorkItemCapabilities.All;

        public override Task<Tempo.Blazor.Models.PagedResult<TmWorkItem>> SearchAsync(TmWorkItemQuery query, CancellationToken cancellationToken = default)
        {
            IEnumerable<TmWorkItem> filtered = _tasks;
            if (!string.IsNullOrWhiteSpace(query.AssigneeId))
                filtered = filtered.Where(task => task.Assignees.Any(a => string.Equals(a.Id, query.AssigneeId, StringComparison.OrdinalIgnoreCase)));
            if (!query.IncludeCompleted)
                filtered = filtered.Where(task => !task.IsCompleted);

            var items = filtered.OrderBy(task => task.DueDate ?? DateTime.MaxValue).ToList();
            return Task.FromResult(new Tempo.Blazor.Models.PagedResult<TmWorkItem>
            {
                Items = items.Skip(query.Skip).Take(query.Take).ToList(),
                TotalCount = items.Count,
                Page = query.Take <= 0 ? 1 : (query.Skip / query.Take) + 1,
                PageSize = query.Take
            });
        }

        public override Task SetCompletedAsync(string id, bool completed, CancellationToken cancellationToken = default)
        {
            var task = _tasks.Single(item => item.Id == id);
            task.IsCompleted = completed;
            return Task.CompletedTask;
        }
    }

    private sealed class StubHistoryProvider : INotionVersionProvider
    {
        public static readonly Guid PageId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        private readonly List<NotionPageVersionDto> _versions;

        public StubHistoryProvider()
        {
            var blockId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
            _versions =
            [
                Version("ffffffff-ffff-ffff-ffff-fffffffffff1", "Current", 0,
                    [Block(blockId, 0, "Updated paragraph")]),
                Version("ffffffff-ffff-ffff-ffff-fffffffffff0", "Previous", 1,
                    [Block(blockId, 0, "Original paragraph")]),
            ];
        }

        public Task<Tempo.Blazor.NotionEditor.Models.PagedResult<IPageVersion>> GetVersionsAsync(string pageId, int page, int pageSize)
            => Task.FromResult(new Tempo.Blazor.NotionEditor.Models.PagedResult<IPageVersion>
            {
                Items = _versions.Cast<IPageVersion>().ToList(),
                TotalCount = _versions.Count,
                Page = page,
                PageSize = pageSize
            });

        public Task<IPageVersion> GetVersionAsync(string pageId, string versionId)
            => Task.FromResult<IPageVersion>(_versions.Single(version => version.Id.ToString("D") == versionId));

        public Task RestoreVersionAsync(string pageId, string versionId) => Task.CompletedTask;

        public Task<IReadOnlyList<BlockDiff>> GetDiffAsync(string pageId, string versionIdA, string versionIdB)
        {
            var before = _versions.Single(version => version.Id.ToString("D") == versionIdA);
            var after = _versions.Single(version => version.Id.ToString("D") == versionIdB);
            return Task.FromResult(NotionBlockDiffService.Compare(before.BlocksSnapshot, after.BlocksSnapshot));
        }

        public Task<IEnumerable<BlockDiff>> CompareVersionsAsync(string versionId1, string versionId2)
            => GetDiffAsync(PageId.ToString("D"), versionId1, versionId2)
                .ContinueWith(task => task.Result.AsEnumerable());

        private static NotionPageVersionDto Version(string id, string description, int hoursAgo, List<PageBlock> blocks) => new()
        {
            Id = Guid.Parse(id),
            PageId = PageId,
            EditedAt = new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc).AddHours(-hoursAgo),
            EditedByUserId = "tester",
            EditedByDisplayName = "Test User",
            ChangeDescription = description,
            BlocksSnapshot = blocks
        };

        private static PageBlock Block(Guid id, int order, string html) => new()
        {
            Id = id,
            PageId = PageId,
            Type = BlockType.Paragraph,
            Order = order,
            Content = new TextBlockContent { Html = html },
            CreatedAt = new DateTime(2026, 1, 10, 8, 0, 0, DateTimeKind.Utc),
            LastEditedAt = new DateTime(2026, 1, 10, 8, 0, 0, DateTimeKind.Utc).AddMinutes(order)
        };
    }
}
