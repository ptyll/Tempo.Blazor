using System.Globalization;
using System.Text.Json;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using NSubstitute;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Dropdowns;
using Tempo.Blazor.Components.Toolbar;
using Tempo.Blazor.Components.NotionEditor.Blocks.Database;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>
/// Phase 8 (DB-2, DB-5, DB-6, NO2). The shared value renderer and the field-icon map replace the
/// per-view copies; the database toolbar rides the core <c>TmToolbar</c> overflow; search filters
/// the records already loaded (NO2, the provider contract is unchanged); the three reading views
/// stay reachable on a coarse pointer.
/// </summary>
public class TmNotionDbPhase8Tests : LocalizationTestBase
{
    public TmNotionDbPhase8Tests()
    {
        // The toolbar overflow menu imports tm-toolbar.js; loose mode still needs the module stub.
        var module = JSInterop.SetupModule("./_content/Tempo.Blazor/js/tm-toolbar.js");
        module.SetupVoid("attach", _ => true).SetVoidResult();
        module.SetupVoid("detach", _ => true).SetVoidResult();
    }

    // ── TmNotionDbPropertyValue ──────────────────────────────────────────────

    [Fact]
    public void PropertyValue_Select_RendersAColoredPill()
    {
        var field = new DatabaseField
        {
            Id = Guid.NewGuid(),
            Name = "Status",
            Type = DatabaseFieldType.Select,
            Config = new SelectFieldConfig
            {
                Options = [new SelectFieldOption("a", "Active", "green")]
            }
        };

        var cut = Render<TmNotionDbPropertyValue>(p => p
            .Add(x => x.Field, field)
            .Add(x => x.Value, "Active"));

        var pill = cut.Find("[data-testid=notion-db-value-pill]");
        pill.TextContent.Should().Contain("Active");
        pill.GetAttribute("data-color").Should().Be("green");
    }

    [Fact]
    public void PropertyValue_Person_RendersInitials()
    {
        var field = new DatabaseField { Id = Guid.NewGuid(), Name = "Owner", Type = DatabaseFieldType.Person };

        var cut = Render<TmNotionDbPropertyValue>(p => p
            .Add(x => x.Field, field)
            .Add(x => x.Value, "Ada Lovelace"));

        cut.Find("[data-testid=notion-db-value-person]").TextContent.Should().Contain("AL");
    }

    [Fact]
    public void PropertyValue_Date_RendersTheFormattedDate()
    {
        var field = new DatabaseField { Id = Guid.NewGuid(), Name = "When", Type = DatabaseFieldType.Date };

        var cut = Render<TmNotionDbPropertyValue>(p => p
            .Add(x => x.Field, field)
            .Add(x => x.Value, new DateTime(2026, 3, 4)));

        cut.Find("[data-testid=notion-db-value]").TextContent.Trim().Should().NotBeNullOrEmpty();
        cut.Markup.Should().Contain("2026");
    }

    [Fact]
    public void PropertyValue_IsoDateString_RendersByCurrentCulture()
    {
        var field = new DatabaseField { Id = Guid.NewGuid(), Name = "Due", Type = DatabaseFieldType.Date };
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            var czech = CultureInfo.GetCultureInfo("cs-CZ");
            CultureInfo.CurrentCulture = czech;
            CultureInfo.CurrentUICulture = czech;

            var cut = Render<TmNotionDbPropertyValue>(p => p
                .Add(x => x.Field, field)
                .Add(x => x.Value, "2026-06-23T12:00:00Z"));

            var text = cut.Find("[data-testid=notion-db-value]").TextContent.Trim();
            text.Should().Be(new DateTime(2026, 6, 23).ToString("d", czech));
            text.Should().NotContain("T12:00");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    [Fact]
    public void PropertyValue_Checkbox_RendersOnlyWhenChecked()
    {
        var field = new DatabaseField { Id = Guid.NewGuid(), Name = "Done", Type = DatabaseFieldType.Checkbox };

        var on = Render<TmNotionDbPropertyValue>(p => p
            .Add(x => x.Field, field)
            .Add(x => x.Value, true));
        on.Find("[data-testid=notion-db-value-check]").Should().NotBeNull();

        var off = Render<TmNotionDbPropertyValue>(p => p
            .Add(x => x.Field, field)
            .Add(x => x.Value, false));
        off.FindAll("[data-testid=notion-db-value-check]").Should().BeEmpty();
        off.Markup.Trim().Should().BeEmpty();
    }

    [Fact]
    public void PropertyValue_Empty_RendersNothing()
    {
        var field = new DatabaseField { Id = Guid.NewGuid(), Name = "Notes", Type = DatabaseFieldType.Text };

        var cut = Render<TmNotionDbPropertyValue>(p => p
            .Add(x => x.Field, field)
            .Add(x => x.Value, null));

        cut.Markup.Trim().Should().BeEmpty();
    }

    // ── DatabaseFieldIcons ───────────────────────────────────────────────────

    [Fact]
    public void FieldIcons_EveryFieldTypeHasAnIcon()
    {
        foreach (var type in Enum.GetValues<DatabaseFieldType>())
            DatabaseFieldIcons.Get(type).Should().NotBeNullOrWhiteSpace($"field type {type} needs an icon");
    }

    // ── Toolbar (DB-5) ───────────────────────────────────────────────────────

    [Fact]
    public void Toolbar_Compact_MovesSecondaryActionsIntoMore()
    {
        var cut = RenderBlock(TmLayoutMode.Tablet);

        cut.WaitForAssertion(() => cut.Find(".tm-toolbar").Should().NotBeNull());
        // The real measurement is tm-toolbar.js; the test reports that nothing secondary fits.
        cut.InvokeAsync(() => cut.FindComponent<TmToolbar>().Instance.OnFitChanged(0)).Wait();
        // Import/Export, Open as page, Sub-items and Properties leave the bar for the overflow menu.
        cut.FindAll(".tm-db__tool-btn[data-tool='import-export'], .tm-db__tool-btn[data-tool='open-as-page'], .tm-db__tool-btn[data-tool='sub-items'], .tm-db__tool-btn[data-tool='properties']")
            .Should().OnlyContain(b => b.HasAttribute("inert") || b.GetAttribute("aria-hidden") == "true");

        cut.Find("button.tm-toolbar-more").Click();
        cut.WaitForAssertion(() => cut.FindAll("[role='menu'] [role^='menuitem']").Count.Should().BeGreaterThan(1));
        var menu = string.Join(" ", cut.FindAll("[role='menu'] [role^='menuitem']").Select(i => i.TextContent));
        menu.Should().Contain("Import / Export");
        menu.Should().Contain("Properties");
        menu.Should().Contain("Sub-items");
        menu.Should().Contain("Open as full page");
    }

    [Fact]
    public void Toolbar_Mobile_ShowsTheActiveViewAsADropdown()
    {
        var cut = RenderBlock(TmLayoutMode.Mobile, views: TwoViews());

        cut.WaitForAssertion(() => cut.Find("[data-testid=notion-db-view-switch]").Should().NotBeNull());
        cut.Find("[data-testid=notion-db-view-switch]").Should().NotBeNull();
        cut.FindAll(".tm-db__view-tabs").Should().BeEmpty();
    }

    [Fact]
    public void Toolbar_ReadOnly_HidesNew()
    {
        var cut = RenderBlock(TmLayoutMode.Desktop, readOnly: true);

        cut.WaitForAssertion(() => cut.Find(".tm-toolbar").Should().NotBeNull());
        cut.FindAll("[data-testid=notion-db-new]").Should().BeEmpty();
    }

    [Fact]
    public void Toolbar_ShowSearch_FiltersLoadedRecords_AndWarnsWhenMorePagesExist()
    {
        var title = Field("Title", DatabaseFieldType.Text, primary: true);
        var alpha = Record(title, "Alpha");
        var beta = Record(title, "Beta");
        var cut = RenderBlock(TmLayoutMode.Desktop, fields: [title], records: [alpha, beta], total: 8, showSearch: true);

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Alpha"));
        cut.Find("[data-testid=notion-db-search]").Input("alp");

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Alpha");
            cut.Markup.Should().NotContain("Beta");
        });
        cut.Find("[data-testid=notion-db-search-loaded-only]").TextContent
            .Should().Contain("loaded records");
    }

    [Fact]
    public void Toolbar_MobileActionBar_OnlyWhenTheDatabaseIsThePage()
    {
        var inline = RenderBlock(TmLayoutMode.Mobile, asPage: false);
        inline.WaitForAssertion(() => inline.Find(".tm-toolbar").Should().NotBeNull());
        inline.FindAll(".tm-mobile-action-bar__bar").Should().BeEmpty();

        var page = RenderBlock(TmLayoutMode.Mobile, asPage: true);
        page.WaitForAssertion(() => page.Find(".tm-mobile-action-bar__bar").Should().NotBeNull());
        page.Markup.Should().Contain("New record");
    }

    // ── Views (DB-6) ─────────────────────────────────────────────────────────

    [Fact]
    public void TableView_GroupHeader_RendersCollapseAction()
    {
        var status = Field("Status", DatabaseFieldType.Select);
        status.Config = new SelectFieldConfig { Options = [new SelectFieldOption("a", "Open", "blue")] };
        var record = Record(status, "Open");

        var cut = Render<TmNotionDbTableView>(p => p
            .Add(x => x.Fields, new[] { status })
            .Add(x => x.Records, new[] { record })
            .Add(x => x.GroupByField, status));

        var toggle = cut.Find("[data-testid=notion-db-group-toggle]");
        toggle.GetAttribute("aria-label").Should().Contain("Collapse");
        cut.Find(".tm-dbt__group-dot").Should().NotBeNull();
    }

    [Fact]
    public void TableView_RowMenu_ContainsOpenAndDelete()
    {
        var title = Field("Title", DatabaseFieldType.Text, primary: true);
        var record = Record(title, "Alpha");
        var cut = Render<TmNotionDbTableView>(p => p
            .Add(x => x.Fields, new[] { title })
            .Add(x => x.Records, new[] { record })
            .Add(x => x.OnRecordClicked, EventCallback.Factory.Create<IDatabaseRecord>(this, _ => { }))
            .Add(x => x.OnRecordDeleted, EventCallback.Factory.Create<IDatabaseRecord>(this, _ => { })));

        cut.Find("[data-testid=notion-db-table-row-delete] .tm-dropdown-trigger").Click();
        var labels = cut.FindAll(".tm-dropdown-item").Select(i => i.TextContent).ToList();
        labels.Should().Contain(l => l.Contains("Open"));
        labels.Should().Contain(l => l.Contains("Delete"));
    }

    [Fact]
    public void TableView_Coarse_DisablesHeaderDragAndResize()
    {
        var title = Field("Title", DatabaseFieldType.Text, primary: true);
        var cut = Render<TmNotionDbTableView>(p => p
            .Add(x => x.Fields, new[] { title })
            .Add(x => x.Records, new[] { Record(title, "Alpha") })
            .Add(x => x.CoarsePointer, true));

        cut.Find(".tm-dbt__th:not(.tm-dbt__th--check)").GetAttribute("draggable").Should().Be("false");
        cut.FindAll(".tm-dbt__resize-handle").Should().BeEmpty();
    }

    [Fact]
    public void ListView_Row_RendersChipsWithColor()
    {
        var title = Field("Title", DatabaseFieldType.Text, primary: true);
        var status = Field("Status", DatabaseFieldType.Select);
        status.Config = new SelectFieldConfig { Options = [new SelectFieldOption("a", "Open", "blue")] };
        var record = new DatabaseRecord
        {
            Id = Guid.NewGuid(),
            Fields = new Dictionary<string, object?>
            {
                [title.Id.ToString()] = "Alpha",
                [status.Id.ToString()] = "Open"
            }
        };

        var cut = Render<TmNotionDbListView>(p => p
            .Add(x => x.Fields, new[] { title, status })
            .Add(x => x.Records, new[] { record }));

        var chip = cut.Find("[data-testid=notion-db-list-chip]");
        chip.TextContent.Should().Contain("Open");
        chip.GetAttribute("data-color").Should().Be("blue");
    }

    [Fact]
    public void GalleryView_Card_RendersPropertyIcon()
    {
        var title = Field("Title", DatabaseFieldType.Text, primary: true);
        var notes = Field("Notes", DatabaseFieldType.Text);
        var record = new DatabaseRecord
        {
            Id = Guid.NewGuid(),
            Fields = new Dictionary<string, object?>
            {
                [title.Id.ToString()] = "Alpha",
                [notes.Id.ToString()] = "A note"
            }
        };

        var cut = Render<TmNotionDbGalleryView>(p => p
            .Add(x => x.Fields, new[] { title, notes })
            .Add(x => x.Records, new[] { record }));

        cut.Find("[data-testid=notion-db-gallery-prop] [data-icon]").GetAttribute("data-icon")
            .Should().NotBeNullOrWhiteSpace();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

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
        bool readOnly = false,
        bool asPage = false,
        bool showSearch = false,
        IReadOnlyList<IDatabaseField>? fields = null,
        IReadOnlyList<IDatabaseRecord>? records = null,
        int? total = null,
        (DatabaseView Table, DatabaseView List)? views = null)
    {
        var pair = views ?? TwoViews();
        var loaded = records ?? [];
        var provider = Substitute.For<INotionDatabaseProvider>();
        provider.GetFieldsAsync(Arg.Any<string>()).Returns(fields ?? Array.Empty<IDatabaseField>());
        provider.GetViewsAsync(Arg.Any<string>()).Returns(new IDatabaseView[] { pair.Table, pair.List });
        provider.GetRecordsAsync(Arg.Any<string>(), Arg.Any<INotionDatabaseFilter?>(),
                Arg.Any<IEnumerable<NotionDatabaseSort>?>(), Arg.Any<NotionDatabaseGrouping?>(),
                Arg.Any<int>(), Arg.Any<int>())
            .Returns(new PagedResult<IDatabaseRecord>
            {
                Items = loaded.ToList(),
                TotalCount = total ?? loaded.Count,
                Page = 1,
                PageSize = 50
            });

        var content = new InlineDatabaseBlockContent { DatabaseId = Guid.NewGuid() };
        return Render<TmNotionDatabaseBlock>(p => p
            .Add(x => x.Block, new PageBlock
            {
                Id = Guid.NewGuid(),
                Type = BlockType.InlineDatabase,
                Content = content
            })
            .Add(x => x.ReadOnly, readOnly)
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
