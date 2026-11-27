using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using NSubstitute;
using System.Reflection;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Abstractions.WorkItems;
using Tempo.Blazor.Components.NotionEditor.Blocks.Database;
using Tempo.Blazor.Components.NotionEditor.Blocks.Embed;
using Tempo.Blazor.Components.NotionEditor.Blocks.Lists;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>
/// Plan b75365fb Fáze 4k — the boolean ARIA states of the NotionEditor BLOCKS and DATABASE
/// surfaces render as STRINGS in both positions: <c>aria-expanded</c> / <c>aria-pressed</c> /
/// <c>aria-selected</c> / <c>aria-checked</c> carry <c>"true"</c> or <c>"false"</c> — never the
/// invalid empty value a Blazor bool produces, never an omitted attribute. The touched surfaces are
/// the toggle block arrow, the database table group toggle and sub-item row expander, the database
/// view tabs, the import/export dialog tabs, the group bar field listbox, the field editor option
/// color dots and the work item block display-mode buttons (code-behind <c>AddAttribute</c>). These
/// are the bUnit halves of the last ten frozen-list entries — with them fixed,
/// <c>AriaBooleanStateAttributeGuardTests</c> drops the frozen-list mechanism entirely (zero
/// tolerance in the library and the demo).
/// The disclosure buttons (toggle arrow, group toggle, row expander) keep a state-dependent
/// label, which the APG disclosure pattern allows — except the toggle arrow, whose shipped name
/// is the NEUTRAL "Expand / collapse" in both states; the <c>aria-pressed</c>/<c>aria-selected</c>
/// controls keep a STABLE label (the view name, the field name) — a pressed state with a changing
/// name would announce two different controls. The fáze 4k review moved the color dots from
/// toggle buttons to a <c>radiogroup</c> of <c>radio</c>s (<c>aria-checked</c>) — the palette is a
/// single-choice group, not a row of independent toggles. The fáze 4k re-review then wired the
/// remaining APG contract: the database view tabs and the import/export tabs form full
/// tab/tabpanel pairs (<c>aria-controls</c> / <c>aria-labelledby</c>, with an <c>aria-label</c>
/// fallback whenever no tab element can name the panel — an active view in the "more views"
/// overflow, the active tab being renamed, or the mobile layout where the tablist is not
/// rendered at all and the view area is a plainly labelled region, never a dangling
/// <c>role="tabpanel"</c>), the group toggle names the
/// group it collapses, the work item actions are a named <c>role="group"</c>, and the color dots
/// rove their tabindex with the arrows moving focus AND selection together.
/// </summary>
public sealed class TmNotionAriaBooleanStatesBlocksTests : LocalizationTestBase
{
    private const string FocusInvocation = "Blazor._internal.domWrapper.focus";

    public TmNotionAriaBooleanStatesBlocksTests()
    {
        UseCustomLocalization(new Dictionary<string, string>
        {
            ["Tm_Loading"] = "Loading",
            ["TmNotionToggleBlock_Expand"] = "Expand / collapse",
            ["TmNotionToggleBlock_ChildrenLoading"] = "Loading blocks",
            ["TmNotionDb_ExpandGroup"] = "Expand group {0}",
            ["TmNotionDb_CollapseGroup"] = "Collapse group {0}",
            ["TmNotionDbTableView_ExpandRow"] = "Expand row",
            ["TmNotionDbTableView_CollapseRow"] = "Collapse row",
            ["TmNotionDbTableView_SelectAllRows"] = "Select all rows",
            ["TmNotionDbTableView_SelectRow"] = "Select row",
            ["TmNotionDbTableView_GroupNoValue"] = "No value",
            ["TmNotionDbTableView_RecordCount"] = "{0} records",
            ["TmNotionDbTableView_ResizeColumn"] = "Resize column {0}",
            ["TmNotionDbTableView_ResizeColumnHint"] = "Drag or use Alt+Arrow keys",
            ["TmNotionDatabaseBlock_ViewTable"] = "Table",
            ["TmNotionDatabaseBlock_ViewTabs"] = "Views",
            ["TmNotionDatabaseBlock_NoRecords"] = "No records",
            ["TmNotionDatabaseBlock_AddField"] = "Add field",
            ["TmNotionDatabaseBlock_AddRecord"] = "Add record",
            ["TmNotionDatabaseBlock_ViewOptions"] = "View options",
            ["TmNotionDbImportExport_Title"] = "Import / Export",
            ["TmNotionDbImportExport_TabImport"] = "Import",
            ["TmNotionDbImportExport_TabExport"] = "Export",
            ["TmNotionDbGroupBar_Title"] = "Group by",
            ["TmNotionDbGroupBar_Close"] = "Close",
            ["TmNotionDbGroupBar_None"] = "No grouping",
            ["TmNotionDbFieldEditor_Title"] = "Edit field",
            ["TmNotionDbFieldEditor_FieldName"] = "Name",
            ["TmNotionDbFieldEditor_Options"] = "Options",
            ["TmNotionDbFieldEditor_OptionColor"] = "Option color",
            ["TmNotionDbFieldEditor_AddOption"] = "Add option",
            ["TmNotionDbFieldEditor_MoveOptionUp"] = "Move option up",
            ["TmNotionDbFieldEditor_MoveOptionDown"] = "Move option down",
            ["TmNotionDbFieldEditor_RemoveOption"] = "Remove option",
            ["Notion_WorkItem_Mode_Card"] = "Card",
            ["Notion_WorkItem_Mode_List"] = "List",
            ["Notion_WorkItem_Mode_Inline"] = "Inline",
            ["Notion_WorkItem_Refresh"] = "Refresh",
            ["Notion_WorkItem_DisplayMode"] = "Display mode",
            ["Notion_WorkItem_Status"] = "Status"
        });
    }

    // ── TmNotionToggleBlock — the header arrow ─────────────────────────────────────────────

    [Fact]
    public void ToggleBlock_Arrow_AnnouncesExpandedAsString_InBothStates()
    {
        var blockService = Substitute.For<INotionEditorBlockService>();
        blockService.GetChildBlocksAsync(Arg.Any<string>())
            .Returns(Enumerable.Empty<IPageBlock>());

        var cut = Render<TmNotionToggleBlock>(p => p
            .AddCascadingValue(new NotionEditorContext { BlockService = blockService })
            .Add(x => x.Block, new PageBlock
            {
                Id = Guid.NewGuid(),
                PageId = Guid.NewGuid(),
                Type = BlockType.Toggle
            })
            .Add(x => x.Content, new ToggleBlockContent { Html = "Toggle me", IsOpen = false }));

        cut.Find(".tm-notion-toggle__arrow").GetAttribute("aria-expanded").Should().Be("false",
            "a closed toggle keeps the attribute — the string \"false\"");
        cut.Find(".tm-notion-toggle__arrow").GetAttribute("aria-label").Should().Be("Expand / collapse",
            "the disclosure arrow keeps a NEUTRAL name in both states — aria-expanded carries the state");

        cut.Find(".tm-notion-toggle__arrow").Click();
        cut.WaitForAssertion(() =>
            cut.Find(".tm-notion-toggle__arrow").GetAttribute("aria-expanded").Should().Be("true",
                "an open toggle announces the string \"true\""));
        cut.Find(".tm-notion-toggle__arrow").GetAttribute("aria-label").Should().Be("Expand / collapse",
            "the name does not flip with the state");

        cut.Find(".tm-notion-toggle__arrow").Click();
        cut.WaitForAssertion(() =>
            cut.Find(".tm-notion-toggle__arrow").GetAttribute("aria-expanded").Should().Be("false"));
    }

    // ── TmNotionDbTableView — group expand/collapse ────────────────────────────────────────

    [Fact]
    public void TableView_GroupToggle_LabelNamesTheGroup_AndFlipsWithTheState()
    {
        var (fields, records) = SeedGrouped();
        // A record without the Status value forms the "No value" group.
        var noValue = new DatabaseRecord
        {
            Id = Guid.NewGuid(),
            Fields = new Dictionary<string, object?> { [fields[0].Id.ToString()] = "Gamma" }
        };
        var all = records.Append(noValue).ToArray();

        var cut = Render<TmNotionDbTableView>(p => p
            .Add(x => x.Fields, fields)
            .Add(x => x.Records, all)
            .Add(x => x.GroupByField, fields[1]));

        var toggles = cut.FindAll("[data-testid=notion-db-group-toggle]");
        toggles.Should().HaveCount(3, "Open, Done and the No value group");
        toggles[0].GetAttribute("aria-label").Should().Be("Collapse group Open",
            "the disclosure names the group it controls");
        toggles[1].GetAttribute("aria-label").Should().Be("Collapse group Done");
        toggles[2].GetAttribute("aria-label").Should().Be("Collapse group No value",
            "a group without a value announces the localized No value label");

        cut.FindAll("[data-testid=notion-db-group-toggle]")[0].Click();
        cut.FindAll("[data-testid=notion-db-group-toggle]")[0].GetAttribute("aria-label")
            .Should().Be("Expand group Open", "the disclosure label flips with the state");

        cut.FindAll("[data-testid=notion-db-group-toggle]")[0].Click();
        cut.FindAll("[data-testid=notion-db-group-toggle]")[0].GetAttribute("aria-label")
            .Should().Be("Collapse group Open");
    }

    [Fact]
    public void TableView_GroupToggle_AnnouncesExpandedAsString_InBothStates()
    {
        var (fields, records) = SeedGrouped();
        var cut = Render<TmNotionDbTableView>(p => p
            .Add(x => x.Fields, fields)
            .Add(x => x.Records, records)
            .Add(x => x.GroupByField, fields[1]));

        var toggles = cut.FindAll("[data-testid=notion-db-group-toggle]");
        toggles.Should().HaveCount(2, "the two Status values form two groups");
        toggles.Should().OnlyContain(t => t.GetAttribute("aria-expanded") == "true",
            "every group starts expanded — the string \"true\"");

        cut.FindAll("[data-testid=notion-db-group-toggle]")[0].Click();
        cut.FindAll("[data-testid=notion-db-group-toggle]")[0]
            .GetAttribute("aria-expanded").Should().Be("false",
                "a collapsed group keeps the attribute — the string \"false\"");
        cut.FindAll("[data-testid=notion-db-group-toggle]")[1]
            .GetAttribute("aria-expanded").Should().Be("true",
                "the other group stays expanded");

        cut.FindAll("[data-testid=notion-db-group-toggle]")[0].Click();
        cut.FindAll("[data-testid=notion-db-group-toggle]")[0]
            .GetAttribute("aria-expanded").Should().Be("true");
    }

    // ── TmNotionDbTableView — sub-item row expander ────────────────────────────────────────

    [Fact]
    public void TableView_RowExpander_AnnouncesExpandedAsString_InBothStates()
    {
        var field = new DatabaseField
        {
            Id = Guid.NewGuid(),
            Name = "Name",
            Type = DatabaseFieldType.Text,
            IsPrimary = true,
            IsVisible = true
        };
        var parent = new DatabaseRecord
        {
            Id = Guid.NewGuid(),
            Fields = new Dictionary<string, object?> { [field.Id.ToString()] = "Parent" }
        };
        var child = new DatabaseRecord
        {
            Id = Guid.NewGuid(),
            ParentRecordId = parent.Id,
            Fields = new Dictionary<string, object?> { [field.Id.ToString()] = "Child" }
        };

        var cut = Render<TmNotionDbTableView>(p => p
            .Add(x => x.Fields, new[] { field })
            .Add(x => x.Records, new[] { parent, child })
            .Add(x => x.ShowSubItems, true));

        var expander = cut.Find(".tm-dbt__expand-btn");
        expander.GetAttribute("aria-expanded").Should().Be("false",
            "a collapsed row keeps the attribute — the string \"false\"");
        cut.FindAll(".tm-dbt__row--sub").Should().BeEmpty();

        cut.Find(".tm-dbt__expand-btn").Click();
        cut.Find(".tm-dbt__expand-btn").GetAttribute("aria-expanded").Should().Be("true",
            "an expanded row announces the string \"true\"");
        cut.FindAll(".tm-dbt__row--sub").Should().NotBeEmpty("the child row renders");

        cut.Find(".tm-dbt__expand-btn").Click();
        cut.Find(".tm-dbt__expand-btn").GetAttribute("aria-expanded").Should().Be("false");
    }

    // ── TmNotionDatabaseBlock — view tabs ──────────────────────────────────────────────────

    [Fact]
    public void DatabaseBlock_ViewTabs_AnnounceSelectedAsString_InBothStates()
    {
        var dbId = Guid.NewGuid();
        var table = new DatabaseView { Id = Guid.NewGuid(), Name = "Table", Type = DatabaseViewType.Table };
        var list = new DatabaseView { Id = Guid.NewGuid(), Name = "List", Type = DatabaseViewType.List };
        var provider = Substitute.For<INotionDatabaseProvider>();
        provider.GetFieldsAsync(Arg.Any<string>()).Returns(Array.Empty<IDatabaseField>());
        provider.GetViewsAsync(Arg.Any<string>()).Returns(new IDatabaseView[] { table, list });
        provider.GetRecordsAsync(Arg.Any<string>(), Arg.Any<INotionDatabaseFilter?>(),
                Arg.Any<IEnumerable<NotionDatabaseSort>?>(), Arg.Any<NotionDatabaseGrouping?>(),
                Arg.Any<int>(), Arg.Any<int>())
            .Returns(new PagedResult<IDatabaseRecord> { Items = [], TotalCount = 0, Page = 1, PageSize = 50 });

        var cut = Render<TmNotionDatabaseBlock>(p => p
            .Add(x => x.Block, new PageBlock
            {
                Id = Guid.NewGuid(),
                Type = BlockType.InlineDatabase,
                Content = new InlineDatabaseBlockContent { DatabaseId = dbId }
            })
            .AddCascadingValue(new NotionEditorContext
            {
                DataProvider = Substitute.For<INotionDataProvider>(),
                BlockService = Substitute.For<INotionEditorBlockService>(),
                DatabaseProvider = provider
            }));

        cut.WaitForAssertion(() => cut.FindAll("[data-testid=notion-db-view-tab]").Should().HaveCount(2));
        var tabs = cut.FindAll("[data-testid=notion-db-view-tab]");
        tabs[0].GetAttribute("aria-selected").Should().Be("true",
            "the active view tab announces the string \"true\"");
        tabs[1].GetAttribute("aria-selected").Should().Be("false",
            "an inactive view tab keeps the attribute — the string \"false\"");

        cut.FindAll("[data-testid=notion-db-view-tab]")[1].Click();
        cut.WaitForAssertion(() =>
        {
            cut.FindAll("[data-testid=notion-db-view-tab]")[0]
                .GetAttribute("aria-selected").Should().Be("false");
            cut.FindAll("[data-testid=notion-db-view-tab]")[1]
                .GetAttribute("aria-selected").Should().Be("true");
        });
    }

    [Fact]
    public void DatabaseBlock_ViewTabs_WireThemAsAnApgTabTabpanelPair()
    {
        var dbId = Guid.NewGuid();
        var table = new DatabaseView { Id = Guid.NewGuid(), Name = "Table", Type = DatabaseViewType.Table };
        var list = new DatabaseView { Id = Guid.NewGuid(), Name = "List", Type = DatabaseViewType.List };
        var provider = Substitute.For<INotionDatabaseProvider>();
        provider.GetFieldsAsync(Arg.Any<string>()).Returns(Array.Empty<IDatabaseField>());
        provider.GetViewsAsync(Arg.Any<string>()).Returns(new IDatabaseView[] { table, list });
        provider.GetRecordsAsync(Arg.Any<string>(), Arg.Any<INotionDatabaseFilter?>(),
                Arg.Any<IEnumerable<NotionDatabaseSort>?>(), Arg.Any<NotionDatabaseGrouping?>(),
                Arg.Any<int>(), Arg.Any<int>())
            .Returns(new PagedResult<IDatabaseRecord> { Items = [], TotalCount = 0, Page = 1, PageSize = 50 });

        var cut = Render<TmNotionDatabaseBlock>(p => p
            .Add(x => x.Block, new PageBlock
            {
                Id = Guid.NewGuid(),
                Type = BlockType.InlineDatabase,
                Content = new InlineDatabaseBlockContent { DatabaseId = dbId }
            })
            .AddCascadingValue(new NotionEditorContext
            {
                DataProvider = Substitute.For<INotionDataProvider>(),
                BlockService = Substitute.For<INotionEditorBlockService>(),
                DatabaseProvider = provider
            }));

        cut.WaitForAssertion(() => cut.FindAll("[data-testid=notion-db-view-tab]").Should().HaveCount(2));

        var panel = cut.Find(".tm-db__view-area");
        panel.GetAttribute("role").Should().Be("tabpanel");
        var panelId = panel.GetAttribute("id");
        panelId.Should().NotBeNullOrEmpty("the tabpanel needs an id the tabs can point at");

        var tabs = cut.FindAll("[data-testid=notion-db-view-tab]");
        tabs.Should().OnlyContain(t => t.GetAttribute("aria-controls") == panelId,
            "every view tab controls the one view tabpanel");
        panel.GetAttribute("aria-labelledby").Should().Be(tabs[0].GetAttribute("id"),
            "the panel is named by the ACTIVE view's tab");
        panel.GetAttribute("aria-label").Should().BeNull(
            "a visible tab labels the panel — the name fallback is only for an overflowed active view");

        cut.FindAll("[data-testid=notion-db-view-tab]")[1].Click();
        cut.WaitForAssertion(() =>
            cut.Find(".tm-db__view-area").GetAttribute("aria-labelledby")
                .Should().Be(cut.FindAll("[data-testid=notion-db-view-tab]")[1].GetAttribute("id"),
                    "switching the view moves the panel's label to the newly active tab"));
    }

    [Fact]
    public void DatabaseBlock_ViewTabs_ActiveOverflowView_LabelsThePanelByTheViewName()
    {
        // Five views — the fifth lives in the "more views" overflow (VisibleTabCount is 4).
        var views = Enumerable.Range(1, 5)
            .Select(i => new DatabaseView { Id = Guid.NewGuid(), Name = $"View {i}", Type = DatabaseViewType.Table })
            .ToArray();
        var cut = RenderDbBlock(TmLayoutMode.Desktop, views);

        cut.WaitForAssertion(() => cut.FindAll("[data-testid=notion-db-view-tab]").Should().HaveCount(4,
            "only the first four views render a tab — the fifth waits in the overflow"));
        cut.Find(".tm-db__view-area").GetAttribute("aria-labelledby").Should().NotBeNull(
            "while a visible view is active, its tab names the panel");

        // Activate the fifth view through the overflow dropdown.
        cut.Find("[data-testid=notion-db-more-views] .tm-dropdown-trigger").Click();
        cut.FindAll(".tm-dropdown-item").Single(i => i.TextContent.Contains("View 5")).Click();

        cut.WaitForAssertion(() =>
        {
            var panel = cut.Find(".tm-db__view-area");
            panel.GetAttribute("aria-labelledby").Should().BeNull(
                "the overflowed view has no tab element — a dangling reference would name the panel after nothing");
            panel.GetAttribute("aria-label").Should().Be("View 5",
                "the panel falls back to an aria-label with the active view's name");
            panel.GetAttribute("role").Should().Be("tabpanel",
                "on a desktop layout the tablist exists, so the panel stays a tabpanel");
        });
    }

    [Fact]
    public void DatabaseBlock_Mobile_ViewAreaIsANamedRegion_NotADanglingTabpanel()
    {
        var table = new DatabaseView { Id = Guid.NewGuid(), Name = "Table", Type = DatabaseViewType.Table };
        var list = new DatabaseView { Id = Guid.NewGuid(), Name = "List", Type = DatabaseViewType.List };
        var cut = RenderDbBlock(TmLayoutMode.Mobile, [table, list]);

        cut.WaitForAssertion(() => cut.Find(".tm-db__view-area").Should().NotBeNull());
        cut.FindAll("[role='tablist']").Should().BeEmpty(
            "a mobile layout renders no view tablist — the toolbar dropdown switches the view");
        cut.FindAll("[data-testid=notion-db-view-tab]").Should().BeEmpty();

        var panel = cut.Find(".tm-db__view-area");
        panel.GetAttribute("role").Should().BeNull(
            "without a tablist the panel must not claim role=tabpanel");
        panel.GetAttribute("aria-labelledby").Should().BeNull(
            "no tab element exists on mobile — an aria-labelledby would dangle");
        panel.GetAttribute("aria-label").Should().Be("Table",
            "the panel still has an accessible name — the active view's name");
    }

    // ── TmNotionDbImportExport — dialog tabs ───────────────────────────────────────────────

    [Fact]
    public void ImportExport_Tabs_AnnounceSelectedAsString_InBothStates()
    {
        var cut = Render<TmNotionDbImportExport>(p => p
            .Add(x => x.DatabaseId, Guid.NewGuid())
            .Add(x => x.Fields, Array.Empty<IDatabaseField>())
            .Add(x => x.Views, Array.Empty<IDatabaseView>()));

        var tabs = cut.FindAll(".tm-dbie__tab");
        tabs.Should().HaveCount(2, "Import and Export");
        tabs[0].GetAttribute("aria-selected").Should().Be("true",
            "the Import tab starts active — the string \"true\"");
        tabs[1].GetAttribute("aria-selected").Should().Be("false",
            "the Export tab keeps the attribute — the string \"false\"");

        cut.FindAll(".tm-dbie__tab")[1].Click();
        cut.FindAll(".tm-dbie__tab")[0].GetAttribute("aria-selected").Should().Be("false");
        cut.FindAll(".tm-dbie__tab")[1].GetAttribute("aria-selected").Should().Be("true");
    }

    [Fact]
    public void ImportExport_Tabs_WireThemAsAnApgTabTabpanelPair()
    {
        var cut = Render<TmNotionDbImportExport>(p => p
            .Add(x => x.DatabaseId, Guid.NewGuid())
            .Add(x => x.Fields, Array.Empty<IDatabaseField>())
            .Add(x => x.Views, Array.Empty<IDatabaseView>()));

        var panel = cut.Find(".tm-dbie__body");
        panel.GetAttribute("role").Should().Be("tabpanel");
        var panelId = panel.GetAttribute("id");
        panelId.Should().NotBeNullOrEmpty("the tabpanel needs an id the tabs can point at");

        var tabs = cut.FindAll(".tm-dbie__tab");
        tabs.Should().OnlyContain(t => t.GetAttribute("aria-controls") == panelId,
            "every dialog tab controls the one tabpanel");
        tabs[0].GetAttribute("id").Should().NotBeNullOrEmpty();
        panel.GetAttribute("aria-labelledby").Should().Be(tabs[0].GetAttribute("id"),
            "the panel is named by the ACTIVE tab — Import starts active");

        cut.FindAll(".tm-dbie__tab")[1].Click();
        cut.Find(".tm-dbie__body").GetAttribute("aria-labelledby")
            .Should().Be(cut.FindAll(".tm-dbie__tab")[1].GetAttribute("id"),
                "switching to Export moves the panel's label to the Export tab");
    }

    [Fact]
    public void ImportExport_ReadOnly_LabelsThePanelFromTheExportTab()
    {
        var cut = Render<TmNotionDbImportExport>(p => p
            .Add(x => x.DatabaseId, Guid.NewGuid())
            .Add(x => x.Fields, Array.Empty<IDatabaseField>())
            .Add(x => x.Views, Array.Empty<IDatabaseView>())
            .Add(x => x.ReadOnly, true));

        var tabs = cut.FindAll(".tm-dbie__tab");
        tabs.Should().HaveCount(1, "read-only offers the Export tab only");
        tabs[0].GetAttribute("aria-selected").Should().Be("true",
            "the read-only dialog starts on Export");
        cut.Find(".tm-dbie__body").GetAttribute("aria-labelledby")
            .Should().Be(tabs[0].GetAttribute("id"),
                "the panel is labelled by the Export tab — never by the hidden Import tab");
    }

    // ── TmNotionDbGroupBar — field listbox ─────────────────────────────────────────────────

    [Fact]
    public void GroupBar_FieldOptions_AnnounceSelectedAsString_InBothStates()
    {
        var statusField = new DatabaseField
        {
            Id = Guid.NewGuid(),
            Name = "Status",
            Type = DatabaseFieldType.Select
        };

        var cut = Render<TmNotionDbGroupBar>(p => p
            .Add(x => x.Fields, new[] { statusField }));

        var options = cut.FindAll(".tm-dbgb__field-item");
        options.Should().HaveCount(2, "No grouping plus the one groupable field");
        options[0].GetAttribute("aria-selected").Should().Be("true",
            "No grouping starts selected — the string \"true\"");
        options[1].GetAttribute("aria-selected").Should().Be("false",
            "an unselected field keeps the attribute — the string \"false\"");

        cut.FindAll(".tm-dbgb__field-item")[1].Click();
        cut.FindAll(".tm-dbgb__field-item")[0].GetAttribute("aria-selected").Should().Be("false");
        cut.FindAll(".tm-dbgb__field-item")[1].GetAttribute("aria-selected").Should().Be("true");
    }

    // ── TmNotionDbFieldEditor — option color dots (radiogroup) ─────────────────────────────

    [Fact]
    public void FieldEditor_ColorDots_AnnounceCheckedAsString_InBothStates()
    {
        var field = new DatabaseField
        {
            Id = Guid.NewGuid(),
            Name = "Tags",
            Type = DatabaseFieldType.Select,
            Config = new SelectFieldConfig
            {
                Options = [new SelectFieldOption("a", "Alpha", "red")]
            }
        };

        var cut = Render<TmNotionDbFieldEditor>(p => p
            .Add(x => x.Field, field)
            .Add(x => x.Fields, new[] { field }));

        var group = cut.Find(".tm-dbfe__color-dots");
        group.GetAttribute("role").Should().Be("radiogroup",
            "the palette is a single-choice group — exactly one color is checked");
        group.GetAttribute("aria-label").Should().Be("Option color");

        var dots = cut.FindAll(".tm-dbfe__color-dot");
        dots.Should().NotBeEmpty("the option row offers the color palette");
        dots.Should().OnlyContain(d => d.GetAttribute("role") == "radio");
        dots.Count(d => d.GetAttribute("aria-checked") == "true").Should().Be(1,
            "exactly the option's color announces the string \"true\"");
        dots.Count(d => d.GetAttribute("aria-checked") == "false").Should().Be(dots.Count - 1,
            "every other dot keeps the attribute — the string \"false\"");
        cut.Find(".tm-dbfe__color-dot--red").GetAttribute("aria-checked").Should().Be("true");

        var inactiveIndex = dots.ToList().FindIndex(d => d.GetAttribute("aria-checked") == "false");
        cut.FindAll(".tm-dbfe__color-dot")[inactiveIndex].Click();

        var flipped = cut.FindAll(".tm-dbfe__color-dot");
        flipped[inactiveIndex].GetAttribute("aria-checked").Should().Be("true",
            "the picked color announces the string \"true\"");
        flipped.Count(d => d.GetAttribute("aria-checked") == "true").Should().Be(1,
            "the checked state moved — it did not multiply");
        cut.Find(".tm-dbfe__color-dot--red").GetAttribute("aria-checked").Should().Be("false");
    }

    [Fact]
    public void FieldEditor_ColorDots_RoveTheTabStop_AndTheArrowsMoveFocusAndSelection()
    {
        var field = new DatabaseField
        {
            Id = Guid.NewGuid(),
            Name = "Tags",
            Type = DatabaseFieldType.Select,
            Config = new SelectFieldConfig
            {
                Options = [new SelectFieldOption("a", "Alpha", "red")]
            }
        };

        var cut = Render<TmNotionDbFieldEditor>(p => p
            .Add(x => x.Field, field)
            .Add(x => x.Fields, new[] { field }));

        var dots = cut.FindAll(".tm-dbfe__color-dot");
        dots.Count(d => d.GetAttribute("tabindex") == "0").Should().Be(1,
            "the APG radiogroup keeps exactly ONE dot in the tab order");
        cut.Find(".tm-dbfe__color-dot--red").GetAttribute("tabindex").Should().Be("0",
            "the checked dot is the tab stop");
        dots.Where(d => d.GetAttribute("tabindex") != "0")
            .Should().OnlyContain(d => d.GetAttribute("tabindex") == "-1",
                "every other dot is out of the tab order");

        // ArrowRight moves the selection AND the tab stop together (red → orange).
        cut.Find(".tm-dbfe__color-dot--red").KeyDown("ArrowRight");
        cut.Find(".tm-dbfe__color-dot--orange").GetAttribute("aria-checked").Should().Be("true",
            "the arrow key selects the next color — selection follows focus");
        cut.Find(".tm-dbfe__color-dot--orange").GetAttribute("tabindex").Should().Be("0",
            "the tab stop moved with the selection");
        cut.Find(".tm-dbfe__color-dot--red").GetAttribute("aria-checked").Should().Be("false");
        cut.Find(".tm-dbfe__color-dot--red").GetAttribute("tabindex").Should().Be("-1");
        cut.FindAll(".tm-dbfe__color-dot").Count(d => d.GetAttribute("tabindex") == "0")
            .Should().Be(1, "still exactly one tab stop");

        // Home jumps to the first color, End to the last.
        cut.Find(".tm-dbfe__color-dot--orange").KeyDown("Home");
        cut.Find(".tm-dbfe__color-dot--gray").GetAttribute("aria-checked").Should().Be("true");
        cut.Find(".tm-dbfe__color-dot--gray").GetAttribute("tabindex").Should().Be("0");

        // ArrowLeft on the first dot WRAPS to the last color.
        cut.Find(".tm-dbfe__color-dot--gray").KeyDown("ArrowLeft");
        cut.Find(".tm-dbfe__color-dot--brown").GetAttribute("aria-checked").Should().Be("true",
            "the arrows wrap around the ends of the palette");
        cut.Find(".tm-dbfe__color-dot--brown").GetAttribute("tabindex").Should().Be("0");

        // ArrowRight on the last dot wraps back to the first.
        cut.Find(".tm-dbfe__color-dot--brown").KeyDown("ArrowRight");
        cut.Find(".tm-dbfe__color-dot--gray").GetAttribute("aria-checked").Should().Be("true");
    }

    [Fact]
    public void FieldEditor_ColorDots_ArrowKey_MovesTheDomFocusToTheNewlyCheckedDot()
    {
        var field = new DatabaseField
        {
            Id = Guid.NewGuid(),
            Name = "Tags",
            Type = DatabaseFieldType.Select,
            Config = new SelectFieldConfig
            {
                Options = [new SelectFieldOption("a", "Alpha", "red")]
            }
        };

        var cut = Render<TmNotionDbFieldEditor>(p => p
            .Add(x => x.Field, field)
            .Add(x => x.Fields, new[] { field }));

        var before = JSInterop.Invocations.Count(i => i.Identifier == FocusInvocation);
        cut.Find(".tm-dbfe__color-dot--red").KeyDown("ArrowRight");

        cut.WaitForAssertion(() => JSInterop.Invocations.Count(i => i.Identifier == FocusInvocation)
            .Should().Be(before + 1,
                "selection-follows-focus means the arrow key also MOVES the DOM focus — " +
                "the newly checked dot receives an ElementReference.FocusAsync call"));
        var focused = JSInterop.Invocations.Last(i => i.Identifier == FocusInvocation)
            .Arguments.OfType<ElementReference>().First();
        var dotRefs = (Dictionary<string, ElementReference>)typeof(TmNotionDbFieldEditor)
            .GetField("_dotRefs", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance)!;
        focused.Id.Should().Be(dotRefs["a:orange"].Id,
            "the focus lands on the orange dot — the one the arrow just checked");
    }

    [Fact]
    public void FieldEditor_ColorDots_EnterAndSpaceOnAnUncheckedDot_ChangeNothing()
    {
        var field = new DatabaseField
        {
            Id = Guid.NewGuid(),
            Name = "Tags",
            Type = DatabaseFieldType.Select,
            Config = new SelectFieldConfig
            {
                Options = [new SelectFieldOption("a", "Alpha", "red")]
            }
        };

        var cut = Render<TmNotionDbFieldEditor>(p => p
            .Add(x => x.Field, field)
            .Add(x => x.Fields, new[] { field }));

        // Enter/Space are NOT emulated on the radiogroup — the native button owns them
        // (docs/keyboard-activation-convention.md), so a keydown alone must change nothing.
        var before = JSInterop.Invocations.Count(i => i.Identifier == FocusInvocation);
        cut.Find(".tm-dbfe__color-dot--blue").KeyDown("Enter");
        cut.Find(".tm-dbfe__color-dot--blue").KeyDown(" ");

        cut.Find(".tm-dbfe__color-dot--red").GetAttribute("aria-checked").Should().Be("true",
            "the checked color did not move");
        cut.Find(".tm-dbfe__color-dot--blue").GetAttribute("aria-checked").Should().Be("false");
        cut.Find(".tm-dbfe__color-dot--red").GetAttribute("tabindex").Should().Be("0",
            "the tab stop stayed on the checked dot");
        cut.Find(".tm-dbfe__color-dot--blue").GetAttribute("tabindex").Should().Be("-1");
        JSInterop.Invocations.Count(i => i.Identifier == FocusInvocation).Should().Be(before,
            "no focus move was attempted");
    }

    [Fact]
    public void FieldEditor_ColorDots_WithNoMatchingColor_TheFirstDotIsTheTabStop()
    {
        var field = new DatabaseField
        {
            Id = Guid.NewGuid(),
            Name = "Tags",
            Type = DatabaseFieldType.Select,
            Config = new SelectFieldConfig
            {
                // An imported hex color matches no palette dot.
                Options = [new SelectFieldOption("a", "Alpha", "#123456")]
            }
        };

        var cut = Render<TmNotionDbFieldEditor>(p => p
            .Add(x => x.Field, field)
            .Add(x => x.Fields, new[] { field }));

        var dots = cut.FindAll(".tm-dbfe__color-dot");
        dots.Count(d => d.GetAttribute("aria-checked") == "true").Should().Be(0,
            "no palette dot matches the hex color");
        dots.Count(d => d.GetAttribute("tabindex") == "0").Should().Be(1,
            "the group never leaves the keyboard without a tab stop — the FIRST dot stands in");
        dots[0].GetAttribute("tabindex").Should().Be("0");
    }

    // ── TmNotionWorkItemBlock — display-mode buttons (code-behind AddAttribute) ────────────

    [Fact]
    public void WorkItemBlock_DisplayModes_AnnouncePressedAsString_InBothStates()
    {
        var item = new TmWorkItem
        {
            Id = "DEMO-101",
            SourceKey = "demo",
            ExternalId = "DEMO-101",
            Url = "https://tracker.example/work/DEMO-101",
            Title = "Prepare release checklist",
            StatusLabel = "Done",
            StatusColor = "#22c55e"
        };
        var content = new WorkItemBlockContent
        {
            SourceKey = "demo",
            ExternalId = "DEMO-101",
            DisplayMode = WorkItemDisplayMode.Card,
            CachedSnapshot = item
        };
        var providers = new TmWorkItemProviderRegistry(
            [new StaticWorkItemProvider("demo", "Demo tracker", [item])]);

        // The host binds the raised content back the way a real host does (bUnit 2 dropped
        // SetParametersAndRender, so the host owns the state).
        var host = Render<WorkItemModeHost>(p => p
            .Add(x => x.InitialContent, content)
            .Add(x => x.Providers, providers));

        host.WaitForAssertion(() => host.Find(".tm-work-item--card").Should().NotBeNull());
        var modes = host.FindAll(".tm-work-item__mode");
        modes.Should().HaveCount(3, "Card, List and Inline");
        modes[0].GetAttribute("aria-pressed").Should().Be("true",
            "the current display mode announces the string \"true\"");
        modes[1].GetAttribute("aria-pressed").Should().Be("false",
            "an inactive mode keeps the attribute — the string \"false\"");
        modes[2].GetAttribute("aria-pressed").Should().Be("false");

        // Picking another mode raises OnContentChanged; the host binds the new content back.
        host.FindAll(".tm-work-item__mode")[1].Click();
        host.WaitForAssertion(() =>
        {
            host.Instance.Raised.Should().NotBeNull();
            host.Instance.Raised!.DisplayMode.Should().Be(WorkItemDisplayMode.List);
            var flipped = host.FindAll(".tm-work-item__mode");
            flipped[0].GetAttribute("aria-pressed").Should().Be("false");
            flipped[1].GetAttribute("aria-pressed").Should().Be("true",
                "the newly bound mode announces the string \"true\"");
            flipped[2].GetAttribute("aria-pressed").Should().Be("false");
        });
    }

    [Fact]
    public void WorkItemBlock_Actions_AreANamedGroup()
    {
        var item = new TmWorkItem
        {
            Id = "DEMO-101",
            SourceKey = "demo",
            ExternalId = "DEMO-101",
            Url = "https://tracker.example/work/DEMO-101",
            Title = "Prepare release checklist",
            StatusLabel = "Done",
            StatusColor = "#22c55e"
        };
        var content = new WorkItemBlockContent
        {
            SourceKey = "demo",
            ExternalId = "DEMO-101",
            DisplayMode = WorkItemDisplayMode.Card,
            CachedSnapshot = item
        };
        var providers = new TmWorkItemProviderRegistry(
            [new StaticWorkItemProvider("demo", "Demo tracker", [item])]);

        var host = Render<WorkItemModeHost>(p => p
            .Add(x => x.InitialContent, content)
            .Add(x => x.Providers, providers));

        host.WaitForAssertion(() => host.Find(".tm-work-item--card").Should().NotBeNull());
        var actions = host.Find(".tm-work-item__actions");
        actions.GetAttribute("role").Should().Be("group",
            "the display-mode buttons and the refresh action form one named group");
        actions.GetAttribute("aria-label").Should().Be("Display mode");
    }

    // ── Seed helpers ───────────────────────────────────────────────────────────────────────

    /// <summary>Renders the database block with the given views and a pinned layout — the view
    /// tabs / tabpanel wiring differs per layout (desktop tabs, mobile dropdown).</summary>
    private IRenderedComponent<TmNotionDatabaseBlock> RenderDbBlock(
        TmLayoutMode layout, IDatabaseView[] views)
    {
        var provider = Substitute.For<INotionDatabaseProvider>();
        provider.GetFieldsAsync(Arg.Any<string>()).Returns(Array.Empty<IDatabaseField>());
        provider.GetViewsAsync(Arg.Any<string>()).Returns(views);
        provider.GetRecordsAsync(Arg.Any<string>(), Arg.Any<INotionDatabaseFilter?>(),
                Arg.Any<IEnumerable<NotionDatabaseSort>?>(), Arg.Any<NotionDatabaseGrouping?>(),
                Arg.Any<int>(), Arg.Any<int>())
            .Returns(new PagedResult<IDatabaseRecord> { Items = [], TotalCount = 0, Page = 1, PageSize = 50 });

        return Render<TmNotionDatabaseBlock>(p => p
            .Add(x => x.Block, new PageBlock
            {
                Id = Guid.NewGuid(),
                Type = BlockType.InlineDatabase,
                Content = new InlineDatabaseBlockContent { DatabaseId = Guid.NewGuid() }
            })
            .Add(x => x.LayoutMode, layout)
            .AddCascadingValue(new NotionEditorContext
            {
                DataProvider = Substitute.For<INotionDataProvider>(),
                BlockService = Substitute.For<INotionEditorBlockService>(),
                DatabaseProvider = provider
            })
            .AddCascadingValue(new TmLayoutContext(layout, layout)));
    }

    /// <summary>Host binding the work item block's raised content back the way a real host does —
    /// the mode button raises <c>OnContentChanged</c> and the host re-renders with the new
    /// content (bUnit 2 dropped <c>SetParametersAndRender</c>, so the host owns the state).</summary>
    public sealed class WorkItemModeHost : ComponentBase
    {
        [Parameter] public WorkItemBlockContent InitialContent { get; set; } = default!;
        [Parameter] public TmWorkItemProviderRegistry Providers { get; set; } = default!;

        public WorkItemBlockContent? Raised { get; private set; }

        private WorkItemBlockContent? _content;

        protected override void OnInitialized() => _content = InitialContent;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<TmNotionWorkItemBlock>(0);
            builder.AddAttribute(1, "Content", _content);
            builder.AddAttribute(2, "WorkItemProviders", Providers);
            builder.AddAttribute(3, "ReadOnly", false);
            builder.AddAttribute(4, "OnContentChanged", EventCallback.Factory.Create<WorkItemBlockContent>(
                this, updated =>
                {
                    Raised = updated;
                    _content = updated;
                    StateHasChanged();
                }));
            builder.CloseComponent();
        }
    }

    private static (DatabaseField[] Fields, DatabaseRecord[] Records) SeedGrouped()
    {
        var name = new DatabaseField
        {
            Id = Guid.NewGuid(),
            Name = "Name",
            Type = DatabaseFieldType.Text,
            IsPrimary = true,
            IsVisible = true
        };
        var status = new DatabaseField
        {
            Id = Guid.NewGuid(),
            Name = "Status",
            Type = DatabaseFieldType.Text,
            IsVisible = true
        };
        DatabaseRecord Record(string title, string state) => new()
        {
            Id = Guid.NewGuid(),
            Fields = new Dictionary<string, object?>
            {
                [name.Id.ToString()] = title,
                [status.Id.ToString()] = state
            }
        };

        return ([name, status], [Record("Alpha", "Open"), Record("Beta", "Done")]);
    }

    private sealed class StaticWorkItemProvider : TmWorkItemProviderBase
    {
        private readonly IReadOnlyList<TmWorkItem> _items;

        public StaticWorkItemProvider(string sourceKey, string displayName, IReadOnlyList<TmWorkItem> items)
        {
            SourceKey = sourceKey;
            DisplayName = displayName;
            _items = items;
        }

        public override string SourceKey { get; }
        public override string DisplayName { get; }

        public override Task<TmWorkItem?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(_items.FirstOrDefault(item =>
                string.Equals(item.ExternalId ?? item.Id, id, StringComparison.OrdinalIgnoreCase)));

        public override Task<Tempo.Blazor.Models.PagedResult<TmWorkItem>> SearchAsync(
            TmWorkItemQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult(new Tempo.Blazor.Models.PagedResult<TmWorkItem>
            {
                Items = _items,
                TotalCount = _items.Count,
                Page = 1,
                PageSize = _items.Count
            });
    }
}
