using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Demo.SharedUI.Pages;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.DataDisplay;

/// <summary>
/// The <c>/sortable-list</c> demo page: the read-out beside the list is what makes every reorder path
/// observable, so these facts walk the keyboard and the row menu and assert the host's own list — not the
/// component's internal state — moved. The page renders in the demo en table, so a raw localizer key would
/// show up as a literal <c>[key]</c> here.
/// </summary>
public class SortableListPageTests : DemoPageLocalizationTestBase
{
    private IRenderedComponent<SortableListPage> RenderPage() => Render<SortableListPage>();

    private static IReadOnlyList<string> Order(IRenderedComponent<SortableListPage> cut)
        => cut.FindAll("[data-testid='sortable-order-item']").Select(item => item.TextContent.Trim()).ToList();

    private static IReadOnlyList<string> Rows(IRenderedComponent<SortableListPage> cut)
        => cut.FindAll("[data-testid='sortable-step-title']").Select(item => item.TextContent.Trim()).ToList();

    [Fact]
    public void ThePageRendersItsThreeSectionsWithoutRawKeys()
    {
        var cut = RenderPage();

        cut.FindAll("section").Should().HaveCount(3);
        cut.Find("[data-testid='sortable-basic-section']").TextContent.Should().NotContain("[Tm");
        cut.Find("[data-testid='sortable-modes-section']").TextContent.Should().NotContain("[Tm");
        cut.Find("[data-testid='sortable-empty-section']").TextContent.Should().NotContain("[Tm");
    }

    [Fact]
    public void TheBasicListStartsWithTheFourPlanSteps()
    {
        var cut = RenderPage();

        Rows(cut).Should().Equal("Draft the outline", "Confirm the sources", "Review with the team", "Publish the summary");
        Order(cut).Should().Equal(Rows(cut));
        cut.Find("[data-testid='sortable-last-move']").TextContent.Should().Contain("No move yet.");
    }

    [Fact]
    public void AKeyboardMoveReordersTheHostsListAndReportsItsSource()
    {
        var cut = RenderPage();

        // Enter picks the first row up, ArrowDown moves it, Space drops it (the emulated keyup). Every
        // key re-renders the list, so the element is re-resolved from the current tree each time.
        cut.FindAll(".tm-sortable-list__item")[0].KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.Find(".tm-sortable-list__item[data-grabbed='true']").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        cut.Find(".tm-sortable-list__item[data-grabbed='true']").KeyUp(new KeyboardEventArgs { Key = " " });

        cut.WaitForAssertion(() => Order(cut).Should().Equal(
            "Confirm the sources", "Draft the outline", "Review with the team", "Publish the summary"));

        var lastMove = cut.Find("[data-testid='sortable-last-move']");
        lastMove.GetAttribute("data-source").Should().Be("Keyboard");
        lastMove.TextContent.Should().Contain("position 1 → position 2");
    }

    [Fact]
    public void ARowMenuMoveReordersTheHostsListToo()
    {
        var cut = RenderPage();

        cut.FindAll(".tm-context-menu__trigger")[0].Click();
        cut.FindAll(".tm-context-menu__item").Single(item => item.TextContent.Contains("Move to bottom")).Click();

        cut.WaitForAssertion(() => Order(cut).Should().Equal(
            "Confirm the sources", "Review with the team", "Publish the summary", "Draft the outline"));
        cut.WaitForAssertion(() => cut.Find("[data-testid='sortable-last-move']").GetAttribute("data-source")
            .Should().Be("Menu"));
    }

    [Fact]
    public void TheResetButtonRestoresTheOrderAndClearsTheReadout()
    {
        var cut = RenderPage();

        cut.FindAll(".tm-context-menu__trigger")[0].Click();
        cut.FindAll(".tm-context-menu__item").Single(item => item.TextContent.Contains("Move down")).Click();
        cut.WaitForAssertion(() => Order(cut)[0].Should().Be("Confirm the sources"));

        cut.Find("[data-testid='sortable-reset']").Click();

        cut.WaitForAssertion(() => Order(cut)[0].Should().Be("Draft the outline"));
        cut.Find("[data-testid='sortable-last-move']").TextContent.Should().Contain("No move yet.");
    }

    [Fact]
    public void TheReadOnlyListOffersNoFocusStopOrMenu()
    {
        var cut = RenderPage();

        var readOnly = cut.Find("[data-testid='sortable-readonly']");
        readOnly.GetAttribute("data-disabled").Should().Be("true");
        readOnly.QuerySelectorAll(".tm-sortable-list__move").Should().BeEmpty();
        readOnly.QuerySelectorAll("[tabindex='0']").Should().BeEmpty();
        readOnly.QuerySelectorAll(".tm-sortable-list__item").Length.Should().Be(3);
    }

    [Fact]
    public void TheWholeItemListRendersTheHostsEmptySlotAndKeepsTheInstructions()
    {
        var cut = RenderPage();

        cut.Find("[data-testid='sortable-empty-slot']").TextContent.Trim()
            .Should().Be("Nothing queued yet — add a step to start.");
        cut.FindAll("[data-testid='sortable-empty'] [data-testid='sortable-step']")
            .Should().BeEmpty("the empty list renders no rows");
    }
}
