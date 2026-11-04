using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Demo.SharedUI.Pages;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// The <c>/agent-run</c> demo page: the tree and the monitor of ONE run, with the page as the owner of both
/// snapshots. These facts walk the wiring a reader actually uses — select a row and read the detail the page
/// builds from the same public roll-up helper, filter the tree, advance the run, complete or fail a step,
/// answer the approval slot and fold the monitor — so a page that only renders the components cannot pass.
/// </summary>
public class AgentRunPageTests : DemoPageLocalizationTestBase
{
    private static readonly DateTimeOffset AnchoredAt = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(AnchoredAt);

    public AgentRunPageTests()
    {
        Services.AddSingleton<TimeProvider>(_clock);
    }

    private IRenderedComponent<AgentRunPage> RenderPage() => Render<AgentRunPage>();

    private static IReadOnlyList<IElement> TreeRows(IRenderedComponent<AgentRunPage> cut)
        => cut.FindAll("[data-testid='agent-run-tree'] [data-testid='agent-run-tree-row']").ToList();

    private static string EnabledText(IRenderedComponent<AgentRunPage> cut, string testId)
        => cut.Find($"[data-testid='{testId}']").TextContent.Trim();

    /// <summary>The same read, scoped to a component root (two monitors share their internal test ids).</summary>
    private static string ScopedText(IRenderedComponent<AgentRunPage> cut, string root, string testId)
        => cut.Find($"[data-testid='{root}'] [data-testid='{testId}']").TextContent.Trim();

    [Fact]
    public void ThePageRendersItsFourSectionsWithoutRawKeys()
    {
        var cut = RenderPage();

        cut.FindAll("section.demo-section").Should().HaveCount(4);
        foreach (var section in new[]
                 {
                     "agent-run-tree-section", "agent-run-monitor-section",
                     "agent-run-collapsed-section", "agent-run-edge-section",
                 })
        {
            var element = cut.Find($"[data-testid='{section}']");
            element.TextContent.Should().NotContain("[Tm");
        }
    }

    [Fact]
    public void TheTreeStartsWithTheFourNodeRunAndItsRootSelected()
    {
        var cut = RenderPage();

        TreeRows(cut).Should().HaveCount(4);
        TreeRows(cut).Select(row => row.GetAttribute("aria-level")).Should().Equal("1", "2", "3", "2");
        TreeRows(cut).Select(row => row.GetAttribute("data-status")).Should()
            .Equal("running", "done", "failed", "pending");

        EnabledText(cut, "agent-run-selected-readout").Should().Be("Selected: Reconciliation agent");
        EnabledText(cut, "agent-run-node-detail").Should().Contain("Reconciliation agent");
        EnabledText(cut, "agent-run-detail-status").Should().Be("Running");
        EnabledText(cut, "agent-run-detail-total").Should().Contain("3 sub-agents");
    }

    [Fact]
    public void SelectingARow_ByKeyboard_FillsTheDetailFromThePage()
    {
        var cut = RenderPage();

        TreeRows(cut)[0].KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        TreeRows(cut)[1].KeyDown(new KeyboardEventArgs { Key = "Enter" });

        EnabledText(cut, "agent-run-selected-readout").Should().Be("Selected: Plan the reconciliation");
        EnabledText(cut, "agent-run-node-detail").Should().Contain("Plan the reconciliation");
        EnabledText(cut, "agent-run-detail-status").Should().Be("Completed");
        EnabledText(cut, "agent-run-detail-model").Should().Be("gpt-4o");
    }

    [Fact]
    public void TheFilterKeepsTheChainToTheFailure()
    {
        var cut = RenderPage();

        cut.FindAll("[data-testid='agent-run-tree'] .tm-segmented__option")
            .Single(option => option.TextContent.Contains("Failed"))
            .Click();

        TreeRows(cut).Select(row => row.GetAttribute("data-node-id")).Should()
            .Equal("root", "planner", "lookup");
        cut.Find("[data-testid='agent-run-tree'] [data-testid='agent-run-tree-row']").TextContent
            .Should().Contain("Reconciliation agent");
    }

    [Fact]
    public void TheAdvanceButton_ChangesTheRunThePageOwns()
    {
        var cut = RenderPage();

        EnabledText(cut, "agent-run-advance-readout").Should().Be("The run has not moved yet.");

        // The failure's reason is the status cell's accessible name, not a second text column.
        cut.Find("[data-testid='agent-run-tree'] [data-has-error='true']").GetAttribute("aria-label")
            .Should().Be("Failed: The ledger API answered 503.");

        cut.Find("[data-testid='agent-run-advance']").Click();
        TreeRows(cut)[2].GetAttribute("data-status").Should().Be("running");
        EnabledText(cut, "agent-run-advance-readout").Should().Be("The lookup runs again.");

        cut.Find("[data-testid='agent-run-advance']").Click();
        TreeRows(cut)[2].GetAttribute("data-status").Should().Be("done");
        cut.FindAll("[data-testid='agent-run-tree'] [data-has-error='true']").Should().BeEmpty(
            "the reason left with the failure");

        cut.Find("[data-testid='agent-run-advance']").Click();
        TreeRows(cut)[0].GetAttribute("data-status").Should().Be("done", "the root completes with the writer");
        TreeRows(cut)[3].GetAttribute("data-status").Should().Be("running");

        cut.Find("[data-testid='agent-run-reset']").Click();
        TreeRows(cut)[2].GetAttribute("data-status").Should().Be("failed");
        TreeRows(cut)[0].GetAttribute("data-status").Should().Be("running");
    }

    [Fact]
    public void TheMonitorShowsTheRunAndItsApprovalSlot()
    {
        var cut = RenderPage();

        var monitor = cut.FindComponent<TmAgentMonitor>();
        monitor.Instance.CurrentStep!.Title.Should().Be("Look up the ledger");
        ScopedText(cut, "agent-run-monitor", "agent-run-monitor-current").Should().Be("Look up the ledger");
        ScopedText(cut, "agent-run-monitor", "agent-run-monitor-cost").Should().NotBe("—");
        cut.FindAll("[data-testid='agent-run-monitor'] [data-testid='agent-run-monitor-approval']").Should().ContainSingle();
        EnabledText(cut, "agent-run-decision").Should().Be("No decision yet.");

        cut.Find("[data-testid='agent-run-approve']").Click();
        EnabledText(cut, "agent-run-decision").Should().Be("The page received Approve.");
        cut.FindAll("[data-testid='agent-run-monitor'] [data-testid='agent-run-monitor-approval']").Should().BeEmpty();
    }

    [Fact]
    public void CompletingAndFailingTheStep_EndsTheRun()
    {
        var cut = RenderPage();

        cut.Find("[data-testid='agent-run-fail-step']").Click();
        ScopedText(cut, "agent-run-monitor", "agent-run-monitor-state").Should().Be("Failed");
        EnabledText(cut, "agent-run-decision").Should().Be("The current step failed.");
        cut.Find("[data-testid='agent-run-monitor']").GetAttribute("data-status").Should().Be("failed");

        cut.Find("[data-testid='agent-run-reset-run']").Click();
        cut.Find("[data-testid='agent-run-complete-step']").Click();
        EnabledText(cut, "agent-run-decision").Should().Be("The current step completed.");
        cut.Find("[data-testid='agent-run-monitor']").GetAttribute("data-status").Should().Be("running");

        cut.Find("[data-testid='agent-run-complete-step']").Click();
        ScopedText(cut, "agent-run-monitor", "agent-run-monitor-state").Should().Be("Completed");
    }

    [Fact]
    public void TheCollapsedMonitorKeepsItsBar_AndItsPendingMarker()
    {
        var cut = RenderPage();

        var collapsed = cut.Find("[data-testid='agent-run-monitor-collapsed']");
        collapsed.GetAttribute("data-collapsed").Should().Be("true");
        collapsed.TextContent.Should().Contain("Waiting for your approval");
        collapsed.QuerySelector("[data-testid='agent-run-monitor-timeline']").Should().BeNull();
        cut.FindAll("[data-testid='agent-run-monitor-collapsed'] [data-testid='agent-run-monitor-step']")
            .Should().BeEmpty("the folded bar drops the timeline");
    }

    [Fact]
    public void TheMalformedRun_ShowsTheOrphanAndDropsTheCycle()
    {
        var cut = RenderPage();

        var edgeRows = cut.FindAll("[data-testid='agent-run-tree-edge'] [data-testid='agent-run-tree-row']");
        edgeRows.Select(row => row.GetAttribute("data-node-id")).Should()
            .Equal("edge-root", "edge-child", "edge-orphan");
        edgeRows.Should().Contain(row => row.QuerySelector("[data-testid='agent-run-tree-orphan']") != null);
        edgeRows.Should().NotContain(row => row.TextContent.Contains("Loops to A"));
    }
}
