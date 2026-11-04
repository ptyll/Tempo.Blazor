using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Demo.SharedUI.Pages;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// The <c>/agent-plan</c> demo page: the read-out beside the plan is what makes the controlled contract
/// observable, so these facts walk the edit paths and assert the HOST's list — not the component's internal
/// state — moved. The page renders in the demo en table, so a raw localizer key would show up as a literal
/// <c>[key]</c> here.
/// </summary>
public class AgentPlanPageTests : DemoPageLocalizationTestBase
{
    private IRenderedComponent<AgentPlanPage> RenderPage() => Render<AgentPlanPage>();

    private static IReadOnlyList<string> ReviewTitles(IRenderedComponent<AgentPlanPage> cut)
        => cut.FindAll("[data-testid='agent-plan-review'] [data-testid='agent-plan-step-title']")
            .Select(row => row.TextContent.Trim())
            .ToList();

    private static IElement Row(IReadOnlyList<IElement> rows, int index) => rows[index];

    private static IReadOnlyList<IElement> ReviewRows(IRenderedComponent<AgentPlanPage> cut)
        => cut.FindAll("[data-testid='agent-plan-review'] [data-testid='agent-plan-step']").ToList();

    [Fact]
    public void ThePageRendersItsThreeSectionsWithoutRawKeys()
    {
        var cut = RenderPage();

        // The three demo sections; the plans inside them are <section> elements too, so the count is by
        // the demo's own test ids instead of by tag name.
        cut.FindAll("section.demo-section").Should().HaveCount(3);
        foreach (var section in new[] { "agent-plan-review-section", "agent-plan-running-section", "agent-plan-readonly-section" })
        {
            cut.Find($"[data-testid='{section}']").TextContent.Should().NotContain("[Tm");
        }
    }

    [Fact]
    public void TheReviewPlanStartsWithTheFourChecklistStepsAndNoEdit()
    {
        var cut = RenderPage();

        ReviewTitles(cut).Should().Equal(
            "Draft the outline", "Confirm the sources", "Review with the team", "Publish the summary");
        cut.Find("[data-testid='agent-plan-last-edit']").GetAttribute("data-kind").Should().Be("none");
        cut.Find("[data-testid='agent-plan-decision']").TextContent.Trim().Should().Be("No plan decision yet.");
    }

    [Fact]
    public void AKeyboardRenameIsAppliedByThePage()
    {
        var cut = RenderPage();

        ReviewRows(cut)[1].QuerySelector("[data-testid='agent-plan-step-title']")!.Click();
        var input = cut.Find("[data-testid='agent-plan-review'] [data-testid='agent-plan-step-rename']");
        input.Input("Confirm the sources and the numbers");
        input.KeyDown(new KeyboardEventArgs { Key = "Enter" });

        cut.WaitForAssertion(() => ReviewTitles(cut)[1].Should().Be("Confirm the sources and the numbers"));

        var readout = cut.Find("[data-testid='agent-plan-last-edit']");
        readout.GetAttribute("data-kind").Should().Be("Rename");
        readout.GetAttribute("data-index").Should().Be("1");
        readout.TextContent.Should().Contain("Confirm the sources and the numbers");
    }

    [Fact]
    public void AddingAndRemovingAStepChangeTheHostsList()
    {
        var cut = RenderPage();

        cut.FindAll("[data-testid='agent-plan-review'] [data-testid='agent-plan-step-add']")[0].Click();
        cut.WaitForAssertion(() => ReviewRows(cut).Should().HaveCount(5));
        cut.Find("[data-testid='agent-plan-last-edit']").GetAttribute("data-kind").Should().Be("Add");

        // The inserted row starts in its inline editor, so its title is the input's value until the reader
        // leaves (or renames) it.
        var editor = ReviewRows(cut)[1].QuerySelector("input[data-testid='agent-plan-step-rename']");
        editor.Should().NotBeNull();
        editor!.GetAttribute("value").Should().Be("New step");

        cut.Find("[data-testid='agent-plan-step-rename-cancel']").Click();
        cut.WaitForAssertion(() => ReviewTitles(cut)[1].Should().Be("New step"));

        cut.FindAll("[data-testid='agent-plan-review'] [data-testid='agent-plan-step-remove']")[2].Click();
        cut.WaitForAssertion(() => ReviewRows(cut).Should().HaveCount(4));
        cut.Find("[data-testid='agent-plan-last-edit']").GetAttribute("data-kind").Should().Be("Remove");
        cut.Find("[data-testid='agent-plan-review'] [data-testid='agent-plan-undo']").Should().NotBeNull();
    }

    [Fact]
    public void ADragFreeReorderThroughTheRowMenuIsAppliedByThePage()
    {
        var cut = RenderPage();

        cut.FindAll("[data-testid='agent-plan-review'] .tm-context-menu__trigger")[0].Click();
        cut.FindAll(".tm-context-menu__item").Single(item => item.TextContent.Contains("Move to bottom")).Click();

        cut.WaitForAssertion(() => ReviewTitles(cut).Should().Equal(
            "Confirm the sources", "Review with the team", "Publish the summary", "Draft the outline"));
        cut.WaitForAssertion(() => cut.Find("[data-testid='agent-plan-last-edit']")
            .GetAttribute("data-kind").Should().Be("Reorder"));
    }

    [Fact]
    public void ApproveAndRunReachThePage()
    {
        var cut = RenderPage();

        cut.Find("[data-testid='agent-plan-review'] [data-testid='agent-plan-approve']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='agent-plan-decision']").TextContent.Trim()
            .Should().Be("The page received Approve."));

        cut.Find("[data-testid='agent-plan-review'] [data-testid='agent-plan-run']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='agent-plan-decision']").TextContent.Trim()
            .Should().Be("The page received Run."));
    }

    [Fact]
    public void TheRunningPlanIsReadOnlyAndOffersRetryOnTheFailedStep()
    {
        var cut = RenderPage();

        var running = cut.Find("[data-testid='agent-plan-running']");
        running.GetAttribute("data-mode").Should().Be("running");
        cut.FindAll("[data-testid='agent-plan-running'] [data-testid='agent-plan-step-remove']")
            .Should().BeEmpty("a running plan is read-only");
        cut.FindAll("[data-testid='agent-plan-running'] [data-testid='agent-plan-add']").Should().BeEmpty();
        cut.FindAll("[data-testid='agent-plan-running'] [data-testid='agent-plan-approve']").Should().BeEmpty();

        var rows = cut.FindAll("[data-testid='agent-plan-running'] [data-testid='agent-plan-step']").ToList();
        rows.Should().HaveCount(7);
        rows.Select(row => row.GetAttribute("data-status")).Should()
            .Equal("done", "done", "done", "done", "running", "failed", "pending");

        // The summary states the progress and the plan's total elapsed time; the running step keeps a live
        // clock, which is not a live region.
        var summary = cut.Find("[data-testid='agent-plan-running'] [data-testid='agent-plan-summary']").TextContent.Trim();
        summary.Should().StartWith("4 of 7 done · ");
        cut.Find("[data-testid='agent-plan-running'] [data-testid='agent-plan-step-time']")
            .GetAttribute("aria-live").Should().Be("off");

        cut.FindAll("[data-testid='agent-plan-running'] [data-testid='agent-plan-step-retry']").Should().HaveCount(1);
        cut.Find("[data-testid='agent-plan-running'] [data-testid='agent-plan-step-retry']").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid='agent-plan-retried']").TextContent.Trim()
            .Should().Be("The page restarted the run from “Notify the finance team”."));
        cut.FindAll("[data-testid='agent-plan-running'] [data-testid='agent-plan-step']")[5]
            .GetAttribute("data-status").Should().Be("running", "the retried step is running again");
    }

    [Fact]
    public void TheLockedPlanKeepsItsRowsAndApproveButNoEditPath()
    {
        var cut = RenderPage();

        var locked = cut.Find("[data-testid='agent-plan-locked']");
        locked.GetAttribute("data-editable").Should().Be("false");
        cut.FindAll("[data-testid='agent-plan-locked'] [data-testid='agent-plan-step']").Should().HaveCount(3);
        cut.FindAll("[data-testid='agent-plan-locked'] button[data-testid='agent-plan-step-title']").Should().BeEmpty();
        cut.FindAll("[data-testid='agent-plan-locked'] [data-testid='agent-plan-add']").Should().BeEmpty();
        cut.Find("[data-testid='agent-plan-locked'] [data-testid='agent-plan-approve']").Should().NotBeNull();
    }

    [Fact]
    public void TheResetRestoresTheChecklist()
    {
        var cut = RenderPage();

        cut.Find("[data-testid='agent-plan-review'] [data-testid='agent-plan-add']").Click();
        cut.WaitForAssertion(() => ReviewRows(cut).Should().HaveCount(5));

        cut.Find("[data-testid='agent-plan-reset']").Click();

        cut.WaitForAssertion(() => ReviewRows(cut).Should().HaveCount(4));
        ReviewTitles(cut).Should().Equal(
            "Draft the outline", "Confirm the sources", "Review with the team", "Publish the summary");
        cut.Find("[data-testid='agent-plan-last-edit']").GetAttribute("data-kind").Should().Be("none");
    }

    [Fact]
    public void TheRunControlsMoveTheSimulatedRun()
    {
        var cut = RenderPage();

        cut.Find("[data-testid='agent-plan-complete-step']").Click();
        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("[data-testid='agent-plan-running'] [data-testid='agent-plan-step']").ToList();
            rows[4].GetAttribute("data-status").Should().Be("done");
            rows[6].GetAttribute("data-status").Should().Be("running");
        });

        cut.Find("[data-testid='agent-plan-fail-step']").Click();
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='agent-plan-running'] [data-testid='agent-plan-step']")
            .Count(row => row.GetAttribute("data-status") == "failed").Should().Be(2));
    }
}
