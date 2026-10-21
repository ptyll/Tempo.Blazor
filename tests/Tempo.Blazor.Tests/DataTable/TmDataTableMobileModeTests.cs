using System.Text.RegularExpressions;
using FluentAssertions;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Components.Overlay;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.DataTable;

/// <summary>
/// Phase 10 (mobile mode): container queries replace the viewport media queries, the pager states
/// "Page X of Y" without dropping its buttons, cards get a sort/filter toolbar, and pinned offsets
/// travel as custom properties so the stylesheet can flip them in RTL.
/// </summary>
public class TmDataTableMobileModeTests : LocalizationTestBase
{
    private const string Container = "@container tm-data-table (width < 640px)";

    [Fact]
    public void MobileLayout_PagerKeepsItsButtonsAndStatesThePage()
    {
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, People(40))
            .Add(c => c.DefaultPageSize, 10)
            .Add(c => c.MobileLayout, DataTableMobileLayout.Cards)
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c
                .Add(x => x.Title, "Name")
                .Add(x => x.Field, person => person.Name)));

        var pager = cut.FindComponent<TmPagination>();
        pager.Instance.Compact.Should().BeFalse("the compact presentation is not how a narrow container paginates");
        cut.Find(".tm-pagination-center").ClassList.Should().Contain("tm-data-table-pager--responsive");
        cut.Find(".tm-pagination-container").ClassList.Should().NotContain("tm-pagination-compact");
        cut.FindAll(".tm-pagination-prev, .tm-pagination-next").Should().HaveCount(2);
        cut.Markup.Should().Contain("Page 1 of 4");
    }

    [Fact]
    public void StandalonePagination_CompactDoesNotDropTheButtons()
    {
        var cut = Render<TmPagination>(p => p
            .Add(c => c.CurrentPage, 2)
            .Add(c => c.TotalPages, 5)
            .Add(c => c.TotalCount, 50)
            .Add(c => c.Compact, true));

        cut.Markup.Should().Contain("Page 2 of 5");
        cut.FindAll(".tm-pagination-prev, .tm-pagination-next").Should().HaveCount(2);
        cut.FindAll(".tm-page-btn").Should().NotBeEmpty("Compact keeps the page buttons");
    }

    [Fact]
    public void Cards_SortAndFilterLiveInAToolbarThatOpensASheet()
    {
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, People(3))
            .Add(c => c.MobileLayout, DataTableMobileLayout.Cards)
            .Add(c => c.ColumnMenuMode, ColumnMenuMode.Menu)
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c
                .Add(x => x.Title, "Name")
                .Add(x => x.PropertyName, "name")
                .Add(x => x.Sortable, true)
                .Add(x => x.Filterable, true)
                .Add(x => x.Field, person => person.Name))
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c
                .Add(x => x.Title, "Age")
                .Add(x => x.PropertyName, "age")
                .Add(x => x.Sortable, true)
                .Add(x => x.Field, person => person.Age)));

        var sort = cut.Find(".tm-data-table-cards-toolbar__sort");
        sort.ClassList.Should().Contain("tm-data-table-mobile-only", "the buttons stay hidden until the container is narrow");
        sort.TextContent.Should().Contain("Sort");

        // The dropdown lists each sortable column in both directions, plus "Clear sort".
        cut.Find(".tm-dropdown-trigger").Click();
        var choices = cut.FindAll("[role='menuitemradio']");
        choices.Should().HaveCount(4, "two sortable columns, each ascending and descending");
        choices.Should().OnlyContain(item => item.GetAttribute("aria-checked") == "false");
        cut.Markup.Should().Contain("Clear sort");

        choices.First(item => item.TextContent.Contains("Age")).Click();

        // The selection closes the menu before the handler runs, so the sort lands on the re-render
        // after that yield. The checked state only exists while the menu is open.
        cut.WaitForAssertion(() => cut.Find("th[aria-sort='ascending']").TextContent.Should().Contain("Age"));
        cut.Find(".tm-dropdown-trigger").Click();
        cut.FindAll("[role='menuitemradio']")
            .Single(item => item.GetAttribute("aria-checked") == "true")
            .TextContent.Should().Contain("Age");
    }

    [Fact]
    public void Cards_FilterOpensTheColumnFilterInsideTheSheet()
    {
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, People(3))
            .Add(c => c.MobileLayout, DataTableMobileLayout.Cards)
            .Add(c => c.ColumnMenuMode, ColumnMenuMode.Menu)
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c
                .Add(x => x.Title, "Name")
                .Add(x => x.PropertyName, "name")
                .Add(x => x.Sortable, true)
                .Add(x => x.Filterable, true)
                .Add(x => x.Field, person => person.Name)));

        cut.Find(".tm-data-table-cards-toolbar__filter").Click();

        cut.Find(".tm-data-table-cards-sheet").Should().NotBeNull();
        cut.FindComponent<TmColumnFilter>().Should().NotBeNull("the sheet renders the column's own filter");
    }

    [Fact]
    public void Scroll_DoesNotRenderTheCardsToolbar()
    {
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, People(2))
            .Add(c => c.MobileLayout, DataTableMobileLayout.Scroll)
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c
                .Add(x => x.Title, "Name")
                .Add(x => x.Sortable, true)
                .Add(x => x.Field, person => person.Name)));

        cut.Markup.Should().NotContain("tm-data-table-cards-toolbar");
        cut.Markup.Should().NotContain("tm-data-table-mobile-only");
    }

    [Fact]
    public void Cards_ContentOnlyHidesTheMobileSortAndFilters()
    {
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, People(2))
            .Add(c => c.MobileLayout, DataTableMobileLayout.Cards)
            .Add(c => c.ToolbarMode, DataToolbarMode.ContentOnly)
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c
                .Add(x => x.Title, "Name")
                .Add(x => x.Sortable, true)
                .Add(x => x.Filterable, true)
                .Add(x => x.Field, person => person.Name)));

        cut.Markup.Should().NotContain("tm-data-table-mobile-only");
        cut.Markup.Should().NotContain("tm-data-table-toolbar");
    }

    [Fact]
    public void Cards_FilterSheetLabelsEachColumnAndAppliesTheFilter()
    {
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, People(3))
            .Add(c => c.MobileLayout, DataTableMobileLayout.Cards)
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c
                .Add(x => x.Title, "Name")
                .Add(x => x.PropertyName, "name")
                .Add(x => x.Filterable, true)
                .Add(x => x.Field, person => person.Name)));

        cut.Find(".tm-data-table-cards-toolbar__filter").TextContent.Should().Contain("Filters (0)");
        cut.Find(".tm-data-table-cards-toolbar__filter").Click();

        var sheet = cut.Find(".tm-data-table-cards-sheet");
        sheet.TextContent.Should().Contain("Name");
        sheet.TextContent.Should().Contain("Clear all");
        sheet.TextContent.Should().Contain("Done");

        cut.Find(".tm-data-table-cards-sheet input.tm-col-filter-input").Input("Person 1");
        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Should().ContainSingle());
    }

    [Fact]
    public void Cards_FilterSheetFooterDone_IsMarkedSoTheSheetCanDropItsDuplicate()
    {
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, People(3))
            .Add(c => c.MobileLayout, DataTableMobileLayout.Cards)
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c
                .Add(x => x.Title, "Name")
                .Add(x => x.PropertyName, "name")
                .Add(x => x.Filterable, true)
                .Add(x => x.Field, person => person.Name)));

        cut.Find(".tm-data-table-cards-toolbar__filter").Click();

        // The sheet header's own Done (tm-overlay-panel-sheet__done) closes it; the footer's Done carries a
        // class that _data-table.css hides inside a sheet, so a sheet shows exactly one Done.
        cut.Find(".tm-data-table-cards-sheet__footer .tm-btn-primary").ClassList
            .Should().Contain("tm-data-table-cards-sheet__done");
    }
    [Fact]
    public void PinnedColumns_DeclareTheirOffsetAsCustomProperties()
    {
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, People(1))
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c
                .Add(x => x.Title, "Name")
                .Add(x => x.PropertyName, "name")
                .Add(x => x.Pinned, Tempo.Blazor.Models.ColumnPin.Left)
                .Add(x => x.Width, "120px")
                .Add(x => x.Field, person => person.Name))
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c
                .Add(x => x.Title, "Role")
                .Add(x => x.PropertyName, "role")
                .Add(x => x.Pinned, Tempo.Blazor.Models.ColumnPin.Right)
                .Add(x => x.Width, "80px")
                .Add(x => x.Field, person => person.Role)));

        var left = cut.Find("th.tm-col-pinned-left").GetAttribute("style")!;
        left.Should().Contain("--tm-pin-left:var(--tm-pin-left-");
        left.Should().Contain(",0px)");
        left.Should().Contain("position:sticky");

        var right = cut.Find("th.tm-col-pinned-right").GetAttribute("style")!;
        right.Should().Contain("--tm-pin-right:var(--tm-pin-right-");
        right.Should().Contain(",0px)");
    }

    [Fact]
    public void Cards_PinnedColumnAfterAHiddenColumn_ReadsTheRenderedHeaderIndex()
    {
        var cut = Render<TmDataTable<BasicPerson>>(p => p
            .Add(c => c.Items, People(1))
            .Add(c => c.MobileLayout, DataTableMobileLayout.Cards)
            .Add(c => c.Selectable, true)
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c
                .Add(x => x.Title, "Secret")
                .Add(x => x.PropertyName, "secret")
                .Add(x => x.CardRole, DataTableCardRole.Hidden)
                .Add(x => x.Field, person => person.Name))
            .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c
                .Add(x => x.Title, "Name")
                .Add(x => x.PropertyName, "name")
                .Add(x => x.Pinned, Tempo.Blazor.Models.ColumnPin.Left)
                .Add(x => x.Width, "120px")
                .Add(x => x.Field, person => person.Name)));

        // The header is [selection, Name]. The hidden column is not a cell, so the pin script
        // writes --tm-pin-left-1; counting it would read the selection column's --tm-pin-left-2.
        cut.Find("th.tm-col-pinned-left").GetAttribute("style")
            .Should().Contain("--tm-pin-left:var(--tm-pin-left-1,0px)");
    }

    [Fact]
    public void SourceCss_AdaptsFromTheContainerAndDropsTheViewportQueries()
    {
        var css = ThemeCss.ComponentCss("_data-table.css");

        css.Should().Contain("container-type: inline-size");
        css.Should().Contain("container-name: tm-data-table");
        css.Should().Contain(Container);
        css.Should().Contain("@media (width < 768px)", "a default table has no container, so its narrow rules stay on the viewport");
        css.Should().NotContain("@media (max-width: 768px)");
        css.Should().NotContain("@media (max-width: 640px)");

        // ThemeCss.Rules cannot see inside an at-rule, so the container block is read from the text.
        var block = ContainerBlock(css, "tm-data-table");
        block.Should().Contain(".tm-data-table-toolbar");
        block.Should().Contain("flex-direction: column");
        block.Should().Contain(".tm-pagination-center");
    }

    [Fact]
    public void SourceCss_PinnedOffsetAndTextCellsAreLogicalAndClipped()
    {
        ThemeCss.Property("_data-table.css", ".tm-col-pinned-left", "inset-inline-start")
            .Should().Be("var(--tm-pin-left, 0)");
        ThemeCss.Property("_data-table.css", ".tm-col-pinned-right", "inset-inline-end")
            .Should().Be("var(--tm-pin-right, 0)");

        // The clip is scoped: a default table, a card cell and an edit cell keep wrapping.
        var css = ThemeCss.ComponentCss("_data-table.css");
        var clipAt = css.IndexOf(".tm-data-table-wrapper--responsive .tm-data-table td", StringComparison.Ordinal);
        clipAt.Should().BeGreaterThanOrEqualTo(0);
        var clip = AtRuleBody(css, css.LastIndexOf("@container", clipAt, StringComparison.Ordinal));
        clip.Should().Contain("text-overflow: ellipsis");
        clip.Should().Contain(".tm-data-table-wrapper--responsive");
        clip.Should().Contain(":not(.tm-data-table-cell--card)");
        clip.Should().Contain(":not(.tm-data-table-cell--editing)");
        clip.Should().Contain(":not(:has(input)):not(:has(button)):not(:has(select)):not(:has(textarea))");
    }

    [Fact]
    public void SourceCss_CoarsePointerGrowsTheTouchTargets()
    {
        var coarse = AtRuleBlock(ThemeCss.ComponentCss("_data-table.css"), "@media (pointer: coarse)", last: true);
        coarse.Should().Contain(".tm-data-table-group-chip-remove");
        coarse.Should().Contain(".tm-filter-builder-add");
        coarse.Should().Contain(".tm-col-pin-btn");
        coarse.Should().Contain(".tm-data-table-col-menu-btn");
        coarse.Should().Contain(".tm-pagination-btn");
        coarse.Should().Contain("var(--tm-touch-target)");
    }

    [Fact]
    public void SourceJs_RecomputesPinnedOffsetsWhenTheTableResizes()
    {
        var js = File.ReadAllText(Path.Combine(
            ThemeCss.RepositoryRoot().FullName, "src", "Tempo.Blazor", "wwwroot", "js", "data-table.js"));

        js.Should().Contain("ResizeObserver");
        js.Should().Contain("--tm-pin-left");
        js.Should().Contain("100");
    }

    /// <summary>The body of the first <c>@container name … { … }</c> block, braces balanced.</summary>
    private static string ContainerBlock(string css, string name)
    {
        var at = css.IndexOf($"@container {name}", StringComparison.Ordinal);
        at.Should().BeGreaterThanOrEqualTo(0, $"the stylesheet must query the {name} container");
        return AtRuleBody(css, at);
    }

    private static string AtRuleBlock(string css, string atRule, bool last = false)
    {
        var at = last
            ? css.LastIndexOf(atRule, StringComparison.Ordinal)
            : css.IndexOf(atRule, StringComparison.Ordinal);
        at.Should().BeGreaterThanOrEqualTo(0, $"the stylesheet must declare '{atRule}'");
        return AtRuleBody(css, at);
    }

    private static string AtRuleBody(string css, int at)
    {
        var open = css.IndexOf('{', at);
        var depth = 0;
        for (var i = open; i < css.Length; i++)
        {
            if (css[i] == '{') depth++;
            else if (css[i] == '}' && --depth == 0) return css[open..(i + 1)];
        }

        throw new InvalidOperationException("Unbalanced braces in the at-rule.");
    }

    private static List<BasicPerson> People(int count) =>
        Enumerable.Range(1, count).Select(i => new BasicPerson($"Person {i}", i, "Role")).ToList();

    // ── Retrospective review fixes (phase 10) ─────────────────────

    private static Action<ComponentParameterCollectionBuilder<TmDataTable<BasicPerson>>> CardsFilterTable(
        Action<ComponentParameterCollectionBuilder<TmDataTable<BasicPerson>>>? extra = null) => p =>
    {
        p.Add(c => c.Items, People(3))
         .Add(c => c.MobileLayout, DataTableMobileLayout.Cards)
         .Add(c => c.ColumnMenuMode, ColumnMenuMode.Menu)
         .AddChildContent<TmDataTableColumn<BasicPerson>>(c => c
             .Add(x => x.Title, "Name")
             .Add(x => x.PropertyName, "name")
             .Add(x => x.Sortable, true)
             .Add(x => x.Filterable, true)
             .Add(x => x.Field, person => person.Name));
        extra?.Invoke(p);
    };

    [Fact]
    public void Cards_FilterTrigger_AriaExpandedIsARealBooleanString()
    {
        // A bound bool renders as an EMPTY attribute when true and vanishes when false, so a screen
        // reader never hears the state; the attribute must be the literal "true"/"false".
        var cut = Render(CardsFilterTable());

        cut.Find(".tm-data-table-cards-toolbar__filter").GetAttribute("aria-expanded").Should().Be("false");

        cut.Find(".tm-data-table-cards-toolbar__filter").Click();

        cut.Find(".tm-data-table-cards-toolbar__filter").GetAttribute("aria-expanded").Should().Be("true");
    }

    [Fact]
    public void Cards_SortMenuItems_StateColumnAndDirectionWithoutRepeatingTheVerb()
    {
        var cut = Render(CardsFilterTable());

        cut.Find(".tm-dropdown-trigger").Click();

        var choices = cut.FindAll("[role='menuitemradio']").Select(item => item.TextContent.Trim()).ToList();
        choices.Should().BeEquivalentTo("Name (A→Z / ascending)", "Name (Z→A / descending)");
        // The trigger is already labelled "Sort"; the items must not repeat it.
        choices.Should().OnlyContain(text => !text.Contains("Sort", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Cards_SheetMode_RendersExactlyOneDoneButton()
    {
        // In sheet mode the drawer header already carries "Done"; the footer's copy must not exist
        // in the DOM, not merely be hidden. The viewport cascade forces the sheet presentation.
        var cut = Render<TmDataTable<BasicPerson>>(p =>
        {
            CardsFilterTable()(p);
            p.AddCascadingValue(Tempo.Blazor.Abstractions.Layout.TmLayoutScopes.Viewport,
                new Tempo.Blazor.Abstractions.Layout.TmLayoutContext(
                    Tempo.Blazor.Abstractions.Layout.TmLayoutMode.Auto,
                    Tempo.Blazor.Abstractions.Layout.TmLayoutMode.Mobile));
        });

        cut.Find(".tm-data-table-cards-toolbar__filter").Click();

        // Forced mobile: the panel presents as the drawer sheet (whose root carries
        // tm-overlay-panel-sheet; the consumer class stays on the popover).
        cut.Find(".tm-overlay-panel-sheet").Should().NotBeNull();
        cut.FindAll("button").Count(b => b.TextContent.Trim() == "Done")
            .Should().Be(1, "the sheet header's Done is the only one");
        cut.Find(".tm-data-table-cards-sheet__footer").TextContent.Should().Contain("Clear all");
    }

    [Fact]
    public void Cards_SheetOpen_DoesNotDuplicateAnyElementId()
    {
        // The header filter row is only display:none while the sheet is open, so the sheet's inputs
        // need their own id family — a repeated id is invalid HTML and can point a label at the
        // hidden header input.
        var cut = Render(CardsFilterTable());

        cut.Find(".tm-data-table-cards-toolbar__filter").Click();

        var ids = Regex.Matches(cut.Markup, "id=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value)
            .ToList();
        ids.Should().OnlyHaveUniqueItems("the sheet and the hidden header row share the DOM");
        cut.Markup.Should().Contain("-cards-filter-name", "the sheet input carries the cards id family");
    }

    [Fact]
    public void ColumnMenuFilter_FocusesTheHeaderFilterWhenItIsVisible()
    {
        // A Cards table in a WIDE container keeps its header: "Filter…" must focus the header
        // filter, not open a sheet anchored to a hidden button.
        JSInterop.Setup<bool>("tmDataTable.focusColumnFilterOrOpenSheet", _ => true).SetResult(true);

        var cut = Render(CardsFilterTable());
        cut.Find(".tm-data-table-col-menu-btn").Click();
        cut.Find("[data-action='filter']").Click();

        var invocation = JSInterop.Invocations["tmDataTable.focusColumnFilterOrOpenSheet"].Single();
        ((string)invocation.Arguments[0]!).Should().EndWith("-col-filter-name");
        cut.Markup.Should().NotContain("tm-overlay-panel-sheet", "the wide container never opens the sheet from the menu");
    }

    [Fact]
    public void ColumnMenuFilter_OpensTheSheetWhenTheHeaderFilterIsHidden()
    {
        // Below 640px the header is display:none; the browser reports the input invisible and C#
        // falls back to the cards sheet, focusing the sheet's own (cards-family) input.
        var cut = Render(CardsFilterTable());

        cut.Find(".tm-data-table-col-menu-btn").Click();
        cut.Find("[data-action='filter']").Click();

        var routing = JSInterop.Invocations["tmDataTable.focusColumnFilterOrOpenSheet"].Single();
        ((string)routing.Arguments[0]!).Should().EndWith("-col-filter-name");
        cut.WaitForAssertion(() => cut.Find(".tm-data-table-cards-sheet").Should().NotBeNull());
        var focus = JSInterop.Invocations["tmDataTable.focusColumnFilter"].Single();
        ((string)focus.Arguments[0]!).Should().EndWith("-cards-filter-name", "the header row's id would resolve to the hidden input");
    }
}
