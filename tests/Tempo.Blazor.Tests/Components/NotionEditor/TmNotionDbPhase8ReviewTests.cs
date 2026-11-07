using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using NSubstitute;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Dropdowns;
using Tempo.Blazor.Components.NotionEditor.Blocks.Database;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.Components.Overlay;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>
/// Phase-8 review fixes. Each assertion names the finding it pins: missing loc keys, per-trigger
/// panel anchors, one primary-text formatter, coarse-pointer detection, list roles, the mobile
/// More sheet, a stable avatar hash, and the UX code items.
/// </summary>
public class TmNotionDbPhase8ReviewTests : LocalizationTestBase
{
    private const string FocusInvocation = "Blazor._internal.domWrapper.focus";

    public TmNotionDbPhase8ReviewTests()
    {
        var module = JSInterop.SetupModule("./_content/Tempo.Blazor/js/tm-toolbar.js");
        module.SetupVoid("attach", _ => true).SetVoidResult();
        module.SetupVoid("detach", _ => true).SetVoidResult();
        JSInterop.Setup<bool>("tmNotionDb.isCoarsePointer", _ => true).SetResult(false);
    }

    [Fact]
    public void Toolbar_NamesItselfFromTheLocalizedKey()
    {
        var cut = RenderBlock(TmLayoutMode.Desktop);

        cut.WaitForAssertion(() => cut.Find(".tm-toolbar").Should().NotBeNull());
        cut.Find(".tm-toolbar").GetAttribute("aria-label").Should().Be("Database toolbar");
    }

    [Fact]
    public void FilterPanel_AnchorsOnTheFilterTrigger_AndReturnsFocusThere()
    {
        var cut = RenderBlock(TmLayoutMode.Desktop);
        cut.WaitForAssertion(() => cut.Find("[data-testid=notion-db-filter-open]").Should().NotBeNull());

        cut.Find("[data-testid=notion-db-filter-open]").Click();

        var panel = cut.FindComponent<TmOverlayPanel>();
        panel.Instance.Anchor.Id.Should().NotBeNullOrEmpty();
        var trigger = cut.Find("[data-testid=notion-db-filter-open]");
        panel.Instance.Anchor.Id.Should().Be(trigger.Id,
            "the filter popover anchors on the Filter button, not a shared empty span");

        var before = FocusCount();
        cut.Find("[data-testid=notion-db-filter-open]").Click();
        cut.WaitForAssertion(() => FocusCount().Should().BeGreaterThan(before));
        LastFocusId().Should().Be(trigger.Id);
    }

    [Fact]
    public void PropertiesPanel_WhenTheTriggerOverflowed_ReturnsFocusToMore()
    {
        var cut = RenderBlock(TmLayoutMode.Tablet);
        cut.WaitForAssertion(() => cut.Find(".tm-toolbar").Should().NotBeNull());
        cut.InvokeAsync(() => cut.FindComponent<Tempo.Blazor.Components.Toolbar.TmToolbar>().Instance.OnFitChanged(0)).Wait();

        cut.Find("button.tm-toolbar-more").Click();
        cut.WaitForAssertion(() => cut.FindAll("[role='menu'] [role^='menuitem']").Count.Should().BeGreaterThan(0));
        var more = cut.Find("button.tm-toolbar-more");
        var itemTexts = cut.FindAll("[role='menu'] [role^='menuitem']").Select(item => item.TextContent.Trim()).ToList();
        itemTexts.Should().Contain(text => text.Contains("Properties"));
        var properties = cut.FindAll("[role='menu'] [role^='menuitem']")
            .First(item => item.TextContent.Contains("Properties"));
        properties.Click();

        var panel = cut.FindComponents<TmOverlayPanel>()
            .First(p => p.Instance.AriaLabel == "Properties" || p.Instance.SheetTitle == "Properties");
        var before = FocusCount();
        panel.InvokeAsync(() => panel.Instance.IsOpenChanged.InvokeAsync(false)).Wait();

        cut.WaitForAssertion(() => FocusCount().Should().BeGreaterThan(before));
        LastFocusId().Should().Be(more.Id,
            "an overflowed tool is not in the tab order, so focus returns to More");
    }

    [Fact]
    public void PrimaryText_FormatsThePrimaryFieldOnce()
    {
        var title = Field("Title", DatabaseFieldType.Text, primary: true);
        var record = Record(title, new DateTime(2026, 3, 4));

        NotionDatabaseValueFormatter.PrimaryText(record, [title]).Should().Contain("2026");
        NotionDatabaseValueFormatter.PrimaryText(record, []).Should().BeEmpty();
    }

    [Fact]
    public void CalendarDayList_UsesTheSharedValueRenderer()
    {
        var title = Field("Title", DatabaseFieldType.Text, primary: true);
        var when = Field("When", DatabaseFieldType.Date);
        var day = new DateOnly(2026, 3, 4);
        var records = Enumerable.Range(1, 5).Select(i => new DatabaseRecord
        {
            Id = Guid.NewGuid(),
            Fields = new Dictionary<string, object?>
            {
                [title.Id.ToString()] = $"Standup {i}",
                [when.Id.ToString()] = day
            }
        }).ToArray();

        var cut = Render<TmNotionDbCalendarView>(p => p
            .Add(x => x.Fields, new[] { title, when })
            .Add(x => x.Records, records)
            .Add(x => x.DateFieldId, when.Id));

        cut.Find("[data-testid=notion-db-calendar-overflow]").Click();
        cut.WaitForAssertion(() =>
            cut.Find("[data-testid=notion-db-calendar-day-list] [data-testid=notion-db-value]")
                .TextContent.Should().Contain("Standup"));
    }

    [Fact]
    public void CoarsePointerProbe_DisablesHeaderDrag_EvenOnDesktop()
    {
        JSInterop.Setup<bool>("tmNotionDb.isCoarsePointer", _ => true).SetResult(true);
        var cut = RenderBlock(TmLayoutMode.Desktop);

        cut.WaitForAssertion(() =>
            cut.Find(".tm-dbt__th:not(.tm-dbt__th--check)").GetAttribute("draggable").Should().Be("false"));
        cut.FindAll(".tm-dbt__resize-handle").Should().BeEmpty();
    }

    [Fact]
    public void ListView_RowsAreAListOfActivators()
    {
        var title = Field("Title", DatabaseFieldType.Text, primary: true);
        var cut = Render<TmNotionDbListView>(p => p
            .Add(x => x.Fields, new[] { title })
            .Add(x => x.Records, new[] { Record(title, "Alpha") }));

        cut.Find(".tm-dblv").GetAttribute("role").Should().Be("list");
        cut.Find("[data-testid=notion-db-list-row]").GetAttribute("role").Should().Be("listitem");
    }

    [Fact]
    public void MobileMore_OpensASheetOfTheRemainingTools()
    {
        var cut = RenderBlock(TmLayoutMode.Mobile, asPage: true);
        cut.WaitForAssertion(() => cut.Find("[data-action-id=more]").Should().NotBeNull());

        cut.Find("[data-action-id=more]").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid=notion-db-mobile-more]").Should().NotBeNull());
        var sheet = cut.Find("[data-testid=notion-db-mobile-more]");
        sheet.TextContent.Should().Contain("Group");
        sheet.TextContent.Should().Contain("Import / Export");
        sheet.TextContent.Should().Contain("Open as full page");
        sheet.TextContent.Should().Contain("Properties");
    }

    [Fact]
    public void AvatarName_IsStableAcrossProcesses()
    {
        TmNotionDbPropertyValue.AvatarName("Ada Lovelace").Should().Be(TmNotionDbPropertyValue.AvatarName("Ada Lovelace"));
        TmNotionDbPropertyValue.AvatarName("Ada Lovelace").Should().BeOneOf(
            "blue", "green", "yellow", "red", "purple", "gray", "orange", "brown", "pink");
    }

    [Fact]
    public void ViewSwitch_DoesNotDrawASecondChevron()
    {
        var cut = RenderBlock(TmLayoutMode.Mobile, views: TwoViews());
        cut.WaitForAssertion(() => cut.Find("[data-testid=notion-db-view-switch]").Should().NotBeNull());

        cut.FindComponent<TmDropdown>().Instance.ShowChevron.Should().BeFalse();
    }

    [Fact]
    public void GalleryPropIcon_IsStyledInTheGalleryStylesheet()
    {
        var css = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "Tempo.Blazor.NotionEditor", "Components", "NotionEditor",
            "Blocks", "Database", "TmNotionDbGalleryView.razor.css"));
        var icon = css.IndexOf(".tm-dbg__card-prop-icon", StringComparison.Ordinal);
        var add = css.IndexOf(".tm-dbg__add-card", StringComparison.Ordinal);
        icon.Should().BeGreaterThan(-1);
        add.Should().BeGreaterThan(icon, "the icon rule belongs with the card props, not under the add-card heading");
    }

    [Fact]
    public async Task MoveField_KeepsThePrimaryFirst_AndCallsReorder()
    {
        var title = Field("Title", DatabaseFieldType.Text, primary: true);
        var notes = Field("Notes", DatabaseFieldType.Text);
        var tags = Field("Tags", DatabaseFieldType.Text);
        var provider = Substitute.For<INotionDatabaseProvider>();
        provider.GetFieldsAsync(Arg.Any<string>()).Returns(new IDatabaseField[] { title, notes, tags });
        provider.GetViewsAsync(Arg.Any<string>()).Returns(new IDatabaseView[]
        {
            new DatabaseView { Id = Guid.NewGuid(), Name = "Table", Type = DatabaseViewType.Table }
        });
        provider.GetRecordsAsync(Arg.Any<string>(), Arg.Any<INotionDatabaseFilter?>(),
                Arg.Any<IEnumerable<NotionDatabaseSort>?>(), Arg.Any<NotionDatabaseGrouping?>(),
                Arg.Any<int>(), Arg.Any<int>())
            .Returns(new PagedResult<IDatabaseRecord> { Items = [], TotalCount = 0, Page = 1, PageSize = 50 });

        var cut = Render<TmNotionDatabaseBlock>(p => p
            .Add(x => x.Block, new PageBlock
            {
                Id = Guid.NewGuid(),
                Type = BlockType.InlineDatabase,
                Content = new InlineDatabaseBlockContent { DatabaseId = Guid.NewGuid() }
            })
            .Add(x => x.LayoutMode, TmLayoutMode.Desktop)
            .AddCascadingValue(new NotionEditorContext
            {
                DataProvider = Substitute.For<INotionDataProvider>(),
                BlockService = Substitute.For<INotionEditorBlockService>(),
                DatabaseProvider = provider
            }));

        cut.WaitForAssertion(() => cut.Find("[data-testid=notion-db-properties-open]").Should().NotBeNull());
        cut.Find("[data-testid=notion-db-properties-open]").Click();

        var up = cut.FindAll("button[aria-label='Move field up']").First(b => !b.HasAttribute("disabled"));
        await cut.InvokeAsync(() => up.Click());

        await provider.Received(1).ReorderFieldsAsync(
            Arg.Any<string>(),
            Arg.Is<IEnumerable<string>>(ids => ids.First() == title.Id.ToString() && ids.ElementAt(1) == tags.Id.ToString()));
    }

    [Fact]
    public void Search_KeepsALiveRegion_AndAFocusRing()
    {
        var cut = RenderBlock(TmLayoutMode.Desktop, showSearch: true);
        cut.WaitForAssertion(() => cut.Find("[data-testid=notion-db-search]").Should().NotBeNull());

        cut.FindAll("[data-testid=notion-db-search-status]").Should().ContainSingle()
            .Which.GetAttribute("aria-live").Should().Be("polite");

        var css = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "Tempo.Blazor.NotionEditor", "Components", "NotionEditor",
            "Blocks", "Database", "TmNotionDatabaseBlock.razor.css"));
        css.Should().Contain(".tm-db__search:focus-within");
        css.Should().Contain("@media (pointer: coarse)");
        css.Should().Contain(".tm-dbt__resize-handle");
    }

    [Fact]
    public void CheckboxValue_ExposesTheStateAsAnImage()
    {
        var field = new DatabaseField { Id = Guid.NewGuid(), Name = "Done", Type = DatabaseFieldType.Checkbox };
        var cut = Render<TmNotionDbPropertyValue>(p => p.Add(x => x.Field, field).Add(x => x.Value, true));

        cut.Find("[data-testid=notion-db-value-check]").GetAttribute("role").Should().Be("img");
    }

    [Fact]
    public void Gallery_SkipsThePrimaryFieldInCardProps()
    {
        var title = Field("Title", DatabaseFieldType.Text, primary: true);
        var record = new DatabaseRecord
        {
            Id = Guid.NewGuid(),
            Fields = new Dictionary<string, object?> { [title.Id.ToString()] = "Alpha" }
        };

        var cut = Render<TmNotionDbGalleryView>(p => p
            .Add(x => x.Fields, new[] { title })
            .Add(x => x.Records, new[] { record })
            .Add(x => x.PreviewFieldIds, new[] { title.Id }));

        cut.FindAll("[data-testid=notion-db-gallery-prop]").Should().BeEmpty();
        cut.Markup.Should().Contain("Alpha");
    }

    [Fact]
    public void Pill_MapsALegacyHexThroughTheTokenName()
    {
        var field = new DatabaseField
        {
            Id = Guid.NewGuid(),
            Name = "Status",
            Type = DatabaseFieldType.Select,
            Config = new SelectFieldConfig { Options = [new SelectFieldOption("a", "Active", "#0f7b6c")] }
        };

        var cut = Render<TmNotionDbPropertyValue>(p => p.Add(x => x.Field, field).Add(x => x.Value, "Active"));

        cut.Find("[data-testid=notion-db-value-pill]").GetAttribute("style")
            .Should().Contain("--tm-notion-option-green-");
    }

    [Fact]
    public void TableView_SelectAndStatus_RenderChipsAtTheRowHeight()
    {
        var title = Field("Title", DatabaseFieldType.Text, primary: true);
        var status = Field("Status", DatabaseFieldType.Status);
        status.Config = new StatusFieldConfig
        {
            Groups = [new StatusGroup("g", "Doing", [new SelectFieldOption("o", "Doing", "blue")])]
        };
        var select = Field("Priority", DatabaseFieldType.Select);
        select.Config = new SelectFieldConfig { Options = [new SelectFieldOption("p", "High", "red")] };
        var record = new DatabaseRecord
        {
            Id = Guid.NewGuid(),
            Fields = new Dictionary<string, object?>
            {
                [title.Id.ToString()] = "Alpha",
                [status.Id.ToString()] = "Doing",
                [select.Id.ToString()] = "High"
            }
        };

        var cut = Render<TmNotionDbTableView>(p => p
            .Add(x => x.Fields, new[] { title, status, select })
            .Add(x => x.Records, new[] { record }));

        cut.FindAll(".tm-dbc__chip").Select(c => c.TextContent.Trim()).Should().BeEquivalentTo(["Doing", "High"]);
        cut.Find(".tm-dbt__row").GetAttribute("style").Should().Contain("--tm-dbt-row-height");
    }

    private int FocusCount() => JSInterop.Invocations.Count(i => i.Identifier == FocusInvocation);

    private string? LastFocusId()
    {
        var invocation = JSInterop.Invocations.Last(i => i.Identifier == FocusInvocation);
        return invocation.Arguments[0] is ElementReference reference ? reference.Id : null;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TempoBlazor.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    private static DatabaseField Field(string name, DatabaseFieldType type, bool primary = false) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Type = type,
        IsPrimary = primary,
        IsVisible = true
    };

    private static DatabaseRecord Record(DatabaseField field, object? value) => new()
    {
        Id = Guid.NewGuid(),
        Fields = new Dictionary<string, object?> { [field.Id.ToString()] = value }
    };

    private static (DatabaseView Table, DatabaseView List) TwoViews() => (
        new DatabaseView { Id = Guid.NewGuid(), Name = "Table", Type = DatabaseViewType.Table },
        new DatabaseView { Id = Guid.NewGuid(), Name = "List", Type = DatabaseViewType.List });

    private IRenderedComponent<TmNotionDatabaseBlock> RenderBlock(
        TmLayoutMode layout,
        bool asPage = false,
        bool showSearch = false,
        (DatabaseView Table, DatabaseView List)? views = null)
    {
        var pair = views ?? TwoViews();
        var provider = Substitute.For<INotionDatabaseProvider>();
        provider.GetFieldsAsync(Arg.Any<string>()).Returns(new[] { Field("Title", DatabaseFieldType.Text, primary: true) });
        provider.GetViewsAsync(Arg.Any<string>()).Returns(new IDatabaseView[] { pair.Table, pair.List });
        provider.GetRecordsAsync(Arg.Any<string>(), Arg.Any<INotionDatabaseFilter?>(),
                Arg.Any<IEnumerable<NotionDatabaseSort>?>(), Arg.Any<NotionDatabaseGrouping?>(),
                Arg.Any<int>(), Arg.Any<int>())
            .Returns(new PagedResult<IDatabaseRecord>
            {
                Items = [Record(Field("Title", DatabaseFieldType.Text, primary: true), "Alpha")],
                TotalCount = 1,
                Page = 1,
                PageSize = 50
            });

        return Render<TmNotionDatabaseBlock>(p => p
            .Add(x => x.Block, new PageBlock
            {
                Id = Guid.NewGuid(),
                Type = BlockType.InlineDatabase,
                Content = new InlineDatabaseBlockContent { DatabaseId = Guid.NewGuid() }
            })
            .Add(x => x.LayoutMode, layout)
            .Add(x => x.ShowSearch, showSearch)
            .Add(x => x.IsPage, asPage)
            .Add(x => x.OnOpenAsPage, EventCallback.Factory.Create(this, () => { }))
            .AddCascadingValue(new NotionEditorContext
            {
                DataProvider = Substitute.For<INotionDataProvider>(),
                BlockService = Substitute.For<INotionEditorBlockService>(),
                DatabaseProvider = provider
            })
            .AddCascadingValue(new TmLayoutContext(layout, layout)));
    }
}
