using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using System.Text.RegularExpressions;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Components.Dropdowns;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.DataTable;

/// <summary>
/// Phase 3: visual redesign markup is opt-in. The default render matches the pre-redesign
/// snapshot; <see cref="DataTableAppearance.Card"/> and <see cref="DataTableKeyboardNavigation.Grid"/>
/// bring the new surface and the grid ARIA back.
/// </summary>
public class TmDataTablePhase3OptInTests : LocalizationTestBase
{
    private static List<BasicPerson> People => [new("Ada", 37, "Engineer")];

    private IRenderedComponent<TmDataTable<BasicPerson>> RenderTable(
        DataTableAppearance appearance = DataTableAppearance.Plain,
        DataTableKeyboardNavigation keyboard = DataTableKeyboardNavigation.Rows,
        bool cardMode = false,
        bool showExport = false,
        bool showGrouping = false,
        DataTableScrollMode scroll = DataTableScrollMode.Pagination,
        float? virtualItemSize = null,
        EditActionsMode editActions = EditActionsMode.Icons,
        bool editable = false)
        => Render<TmDataTable<BasicPerson>>(p =>
        {
            p.Add(c => c.Items, People);
            p.Add(c => c.Appearance, appearance);
            p.Add(c => c.KeyboardNavigation, keyboard);
            p.Add(c => c.CardMode, cardMode);
            p.Add(c => c.ShowExport, showExport);
            p.Add(c => c.ShowGrouping, showGrouping);
            p.Add(c => c.ScrollMode, scroll);
            if (virtualItemSize is not null)
                p.Add(c => c.VirtualItemSize, virtualItemSize.Value);
            p.Add(c => c.EditActionsMode, editActions);
            p.Add(c => c.Editable, editable);
            p.AddChildContent<TmDataTableColumn<BasicPerson>>(c => c
                .Add(x => x.Title, "Name")
                .Add(x => x.Field, person => person.Name)
                .Add(x => x.Groupable, true));
        });

    [Fact]
    public void Rows_OmitsGridAria()
    {
        var cut = RenderTable();

        var table = cut.Find("table.tm-data-table");
        table.GetAttribute("role").Should().Be("grid");
        table.HasAttribute("aria-rowcount").Should().BeFalse();
        table.HasAttribute("aria-colcount").Should().BeFalse();
        cut.Find("tbody tr").HasAttribute("role").Should().BeFalse();
        cut.Find("tbody td").HasAttribute("role").Should().BeFalse();
        cut.Find("tbody td").HasAttribute("aria-colindex").Should().BeFalse();
    }

    [Fact]
    public void Grid_EmitsGridAria()
    {
        var cut = RenderTable(keyboard: DataTableKeyboardNavigation.Grid);

        var table = cut.Find("table.tm-data-table");
        table.GetAttribute("aria-rowcount").Should().Be("2");
        table.GetAttribute("aria-colcount").Should().Be("1");
        cut.Find("tbody tr").GetAttribute("role").Should().Be("row");
        cut.Find("tbody td").GetAttribute("role").Should().Be("gridcell");
    }

    [Fact]
    public void ResultSummary_Default_PrintsTheRange()
    {
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.ShowPagination, false)
            .Add(c => c.ShowResultSummary, true)
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c
                .Add(x => x.Title, "Name")
                .Add(x => x.Field, (Func<BasicPerson, object?>)(person => person.Name))));

        cut.Find("[data-testid='pagination-summary']").TextContent.Should().Contain("1–1 of 1");
        cut.Find("[data-testid='pagination-summary']").TextContent.Should().NotContain("Total");
    }

    [Fact]
    public void ResultSummary_Card_PrintsTotalRecords()
    {
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.Appearance, DataTableAppearance.Card)
            .Add(c => c.ShowPagination, false)
            .Add(c => c.ShowResultSummary, true)
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c
                .Add(x => x.Title, "Name")
                .Add(x => x.Field, (Func<BasicPerson, object?>)(person => person.Name))));

        cut.Find("[data-testid='pagination-summary']").TextContent.Should().Contain("1 record");
    }

    [Fact]
    public void Export_Default_KeepsPlainTriggerAndCsvFileLabel()
    {
        var cut = RenderTable(showExport: true);
        cut.Find(".tm-data-table__export .tm-dropdown-trigger").Click();

        var trigger = cut.Find(".tm-data-table__export .tm-dropdown-trigger");
        trigger.ClassList.Should().NotContain("tm-btn-outline");
        cut.Find(".tm-data-table__export").HasAttribute("data-export-scope").Should().BeFalse();

        var item = cut.Find("[data-export-format='csv']");
        item.QuerySelector("svg").Should().BeNull();
        item.QuerySelector(".tm-data-table__export-description").Should().BeNull();
        item.TextContent.Should().Contain("CSV file");
    }

    [Fact]
    public void Export_Card_RendersOutlineTriggerIconAndDescription()
    {
        var cut = RenderTable(appearance: DataTableAppearance.Card, showExport: true);
        cut.Find(".tm-data-table__export .tm-dropdown-trigger").Click();

        cut.Find(".tm-data-table__export .tm-dropdown-trigger").ClassList.Should().Contain("tm-btn-outline");
        // Scope is a menu choice now, not a wrapper attribute, and only when AllowExportSelected is set.
        cut.Find(".tm-data-table__export").HasAttribute("data-export-scope").Should().BeFalse();
        cut.FindAll("[data-export-scope]").Should().BeEmpty();
        var item = cut.Find("[data-export-format='csv']");
        item.QuerySelector("svg").Should().NotBeNull();
        item.QuerySelector(".tm-data-table__export-description")!.TextContent.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void GroupZone_Default_HasNoPrefixOrAddGrouping()
    {
        var cut = RenderTable(showGrouping: true);

        cut.Find(".tm-data-table-group-zone").Should().NotBeNull();
        cut.FindAll(".tm-data-table-group-label-prefix").Should().BeEmpty();
        cut.FindAll(".tm-data-table-add-grouping").Should().BeEmpty();
        cut.Find(".tm-data-table-group-placeholder").Should().NotBeNull();
    }

    [Fact]
    public void GroupZone_Card_RendersPrefixAndAddGrouping()
    {
        var cut = RenderTable(appearance: DataTableAppearance.Card, showGrouping: true);

        cut.Find(".tm-data-table-group-label-prefix").TextContent.Should().Be("Group by");
        cut.Find(".tm-data-table-add-grouping").Should().NotBeNull();
    }

    [Fact]
    public void CardMode_DoesNotAddCardsClassOrCardCells()
    {
        var cut = RenderTable(cardMode: true);

        cut.Find(".tm-data-table-wrapper--card").Should().NotBeNull();
        cut.Markup.Should().NotContain("tm-data-table-wrapper--cards");
        cut.Markup.Should().NotContain("tm-data-table-cell--card");
        cut.Markup.Should().Contain("data-label=\"Name\"");
    }

    [Fact]
    public void AppearanceCard_AddsCardSurfaceAndGroupZoneCard()
    {
        var cut = RenderTable(appearance: DataTableAppearance.Card, showGrouping: true);

        cut.Find(".tm-data-table-wrapper--card-surface").Should().NotBeNull();
        cut.Find(".tm-data-table-group-zone").ClassList.Should().Contain("tm-data-table-group-zone--card");
    }

    [Fact]
    public void Virtualized_Default_OmitsRowHeightToken()
    {
        var cut = RenderTable(scroll: DataTableScrollMode.Virtualized);

        cut.Find(".tm-data-table-virtual-scroll").GetAttribute("style")
            .Should().NotContain("--tm-data-table-row-height");
    }

    [Fact]
    public void Virtualized_ExplicitSize_SetsRowHeightToken()
    {
        var cut = RenderTable(scroll: DataTableScrollMode.Virtualized, virtualItemSize: 64f);

        cut.Find(".tm-data-table-virtual-scroll").GetAttribute("style")
            .Should().Contain("--tm-data-table-row-height: 64px");
    }

    [Fact]
    public void CardMode_DoesNotForceTextEditActions()
    {
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.CardMode, true)
            .Add(c => c.Editable, true)
            .Add(c => c.EditActionsMode, EditActionsMode.Icons)
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c
                .Add(x => x.Title, "Name")
                .Add(x => x.Field, (Func<BasicPerson, object?>)(person => person.Name))
                .Add(x => x.Editable, true)
                .Add(x => x.EditTemplate, (RenderFragment<BasicPerson>)(_ => b => b.AddContent(0, "edit")))));

        cut.Find(".tm-data-table__edit-action--start").Click();
        cut.Find(".tm-data-table__edit-actions").TextContent.Should().NotContain("Save");
        cut.Find(".tm-data-table__edit-action--save").Should().NotBeNull();
    }

    [Fact]
    public void Export_Default_ItemDoesNotGetTheCardRichClass()
    {
        var cut = RenderTable(showExport: true);
        cut.Find(".tm-data-table__export .tm-dropdown-trigger").Click();

        cut.Find("[data-export-format='csv']").ClassList
            .Should().NotContain("tm-data-table__export-item--rich");
    }

    [Fact]
    public void Export_Card_ItemGetsTheCardRichClass()
    {
        var cut = RenderTable(appearance: DataTableAppearance.Card, showExport: true);
        cut.Find(".tm-data-table__export .tm-dropdown-trigger").Click();

        cut.Find("[data-export-format='csv']").ClassList
            .Should().Contain("tm-data-table__export-item--rich");
    }

    [Fact]
    public void Stylesheet_ExportItem_DefaultIsNowrapAndNotFlex_CardIsRich()
    {
        var css = ThemeCss.ComponentCss("_data-table.css");
        var baseItem = RuleFor(css, ".tm-data-table__export-item");
        baseItem.Should().Contain("white-space: nowrap");
        baseItem.Should().NotContain("display: flex");

        var rich = RuleFor(css, ".tm-data-table__export-item--rich");
        rich.Should().Contain("display: flex");
        rich.Should().Contain("flex-wrap: wrap");
    }

    [Fact]
    public void Stylesheet_GroupChip_DefaultIsSolidPrimary_CardIsTheSubtlePill()
    {
        var css = ThemeCss.ComponentCss("_data-table.css");
        var chip = RuleFor(css, ".tm-data-table-group-chip");
        chip.Should().Contain("background: var(--tm-color-primary)");
        chip.Should().Contain("color: var(--tm-text-inverse)");
        chip.Should().Contain("border-radius: var(--tm-radius-md)");

        var cardChip = RuleFor(css, ".tm-data-table-group-zone--card .tm-data-table-group-chip");
        cardChip.Should().Contain("background: var(--tm-color-primary-subtle)");

        var expand = RuleFor(css, ".tm-data-table-group-expand-all");
        expand.Should().Contain("border: 1px solid var(--tm-border-color)");
        expand.Should().Contain("background: var(--tm-surface)");
        expand.Should().Contain("font-size: var(--tm-font-size-xs)");
    }

    [Fact]
    public void Stylesheet_MvlGroupedCardHeader_DiffersFromTheBody()
    {
        var css = ThemeCss.ComponentCss("_multi-view-list.css");
        var rule = RuleFor(css, ".tm-mvl-group-card > .tm-mvl-group-header");
        rule.Should().Contain("background: var(--tm-surface-secondary)");
        rule.Should().Contain("margin-bottom: 0");
        rule.Should().Contain("border-bottom: 1px solid var(--tm-border-color)");
    }

    /// <summary>Returns the body of the first rule whose selector contains <paramref name="selectorFragment"/>.</summary>
    private static string RuleFor(string css, string selectorFragment)
    {
        foreach (Match m in Regex.Matches(css, @"(?<sel>[^{}]+)\{(?<body>[^{}]*)}"))
        {
            if (m.Groups["sel"].Value.Contains(selectorFragment, StringComparison.Ordinal))
                return m.Groups["body"].Value;
        }

        throw new Xunit.Sdk.XunitException($"No rule for '{selectorFragment}'");
    }
}
