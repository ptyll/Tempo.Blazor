using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Demo.SharedUI.Pages;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// The <c>/ai-agents</c> demo page: ONE scenario across the whole AI package. These facts walk what a reader
/// actually does — review the plan, run it on a fake clock, watch the failure and the retry, decide the pending
/// approval, reorder a step by keyboard, walk the run tree, pick a model and its effort, drop a context entry,
/// compact and use a prompt template — so a page that only renders the components cannot pass.
/// </summary>
public class AiAgentsPageTests : DemoPageLocalizationTestBase
{
    public AiAgentsPageTests()
    {
        // The page injects the demo clock the way every demo host registers it. A test that needs a
        // deterministic walk registers its FakeTimeProvider afterwards, and the last registration wins.
        Services.AddSingleton(TimeProvider.System);
    }

    private FakeTimeProvider RegisterClock()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 12, 20, 0, 0, TimeSpan.Zero));
        Services.AddSingleton<TimeProvider>(clock);
        return clock;
    }

    private IRenderedComponent<AiAgentsPage> RenderPage() => Render<AiAgentsPage>();

    private static void Tick(FakeTimeProvider clock) => clock.Advance(TimeSpan.FromSeconds(1.2));

    private static IElement Section(IRenderedComponent<AiAgentsPage> cut, string testId)
        => cut.Find($"[data-testid='{testId}']");

    private static string Text(IRenderedComponent<AiAgentsPage> cut, string testId)
        => Section(cut, testId).TextContent.Trim();

    private static IReadOnlyList<IElement> PlanRows(IRenderedComponent<AiAgentsPage> cut, string root)
        => cut.FindAll($"[data-testid='{root}'] [data-testid='agent-plan-step']").ToList();

    /// <summary>
    /// Dispatches a key on a FRESH element inside the renderer's dispatcher: the keyboard surfaces re-render
    /// after every step and a held element would carry a stale event-handler id.
    /// </summary>
    private static Task KeyAsync(IRenderedComponent<AiAgentsPage> cut, string selector, int index, string key)
        => cut.InvokeAsync(() => cut.FindAll(selector)[index]
            .KeyDown(new KeyboardEventArgs { Key = key }));

    [Fact]
    public void ThePageRendersItsNineSectionsWithoutRawKeys()
    {
        var cut = RenderPage();

        cut.FindAll("section.demo-section").Should().HaveCount(10);
        foreach (var section in new[]
                 {
                     "ai-agents-plan-section", "ai-agents-tree-section", "ai-agents-lanes-section",
                     "ai-agents-graph-section", "ai-agents-fan-out-section", "ai-agents-attempts-section",
                     "ai-agents-monitor-section", "ai-agents-models-section",
                     "ai-agents-context-section", "ai-agents-prompts-section",
                 })
        {
            Section(cut, section).TextContent.Should().NotContain("[Tm");
        }

        Text(cut, "ai-agents-demo-notice").Should().Contain("Demo data",
            "the page must label its invented data");
    }

    [Fact]
    public void TheLanesSection_RendersFourLanes_AndSelectsABlockIntoTheHostDetail()
    {
        RegisterClock();
        var cut = RenderPage();
        var lanes = Section(cut, "ai-agents-lanes");

        lanes.QuerySelectorAll("[data-testid='agent-lanes-lane']").Should().HaveCount(4);
        lanes.QuerySelectorAll("[data-testid='agent-lanes-block']").Should().HaveCount(7);
        lanes.QuerySelectorAll("[data-testid='agent-lanes-handoff']").Should().HaveCount(4);
        lanes.QuerySelectorAll("[data-testid='agent-lanes-block-marker']").Should().NotBeEmpty(
            "the sample carries an approval and a changes-requested verdict");

        Text(cut, "ai-agents-lanes-selected").Should().Contain("No block selected");
        cut.FindComponents<TmAgentLanes>().Single().Instance.Layout.Lanes
            .Should().OnlyContain(lane => lane.Rows.Count == 1,
                "the sample's work never overlaps inside one lane");

        cut.Find("[data-testid='ai-agents-lanes'] [data-testid='agent-lanes-block'][data-block-id='b-match']").Click();

        cut.WaitForAssertion(() => Text(cut, "ai-agents-lanes-selected")
            .Should().Contain("Match invoices to lines"));
        var detail = cut.Find("[data-testid='ai-agents-lanes-detail']");
        detail.Should().NotBeNull("the page owns the detail surface and the component only reports the selection");
        detail.TextContent.Should().NotContain("TmAgentLanes_",
            "the format key the span is built from is localized, never painted raw");
    }

    [Fact]
    public void TheLanesSection_SwitchesToTheTable_WithTheSameData()
    {
        RegisterClock();
        var cut = RenderPage();

        var tableOption = Section(cut, "ai-agents-lanes")
            .QuerySelectorAll("[data-testid='agent-lanes-view'] button")
            .Single(button => button.TextContent.Trim() == "Table");
        tableOption.Click();

        cut.WaitForAssertion(() => Section(cut, "ai-agents-lanes")
            .QuerySelectorAll("[data-testid='agent-lanes-table-row']").Should().HaveCount(7));
        Section(cut, "ai-agents-lanes")
            .QuerySelectorAll("[data-testid='agent-lanes-handoff-row']").Should().HaveCount(4);
    }

    [Fact]
    public void TheGraphSection_RendersTheNetwork_AndSelectsANodeIntoTheHostDetail()
    {
        RegisterClock();
        var cut = RenderPage();
        var graph = Section(cut, "ai-agents-graph");

        graph.QuerySelectorAll("[data-testid='agent-graph-node']").Should().HaveCount(7,
            "the human, three agents, the ledger and two tools");
        graph.QuerySelectorAll("[data-testid='agent-graph-edge']").Should().HaveCount(6);
        graph.QuerySelectorAll("[data-testid='agent-graph-group']").Should().ContainSingle(
            "the sample groups the two core agents");

        Text(cut, "ai-agents-graph-selected").Should().Contain("No node is selected");

        cut.Find("[data-testid='ai-agents-graph'] [data-testid='agent-graph-node'][data-node-id='reconciler']").Click();

        cut.WaitForAssertion(() => Text(cut, "ai-agents-graph-selected").Should().Contain("Reconciler"));
        var detail = cut.Find("[data-testid='ai-agents-graph-detail']");
        detail.GetAttribute("data-node-id").Should().Be("reconciler");
        detail.TextContent.Should().NotContain("TmAgentGraph_",
            "every sentence the detail prints is localized, never a raw key");
        Section(cut, "ai-agents-graph")
            .QuerySelector("[data-testid='agent-graph-node'][data-node-id='reconciler'] " +
                           ".tm-ai-agent-graph__node-state-text")!
            .TextContent.Should().Contain("Waiting for approval",
                "the node that needs the reader spells its state out with a word, not only a tone");
    }

    [Fact]
    public void TheGraphSection_AppliesTheCallThePagePushes()
    {
        var clock = RegisterClock();
        var cut = RenderPage();

        var before = Section(cut, "ai-agents-graph");
        before.QuerySelector("[data-testid='agent-graph-node'][data-node-id='ledger']")!
            .GetAttribute("aria-label").Should().Contain("Calls: 14");

        Text(cut, "ai-agents-graph-call-log").Should().Contain("0");

        cut.Find("[data-testid='ai-agents-graph-call']").Click();

        // An event-only update is PAINT-gated, so the trailing paint closes the window on the page's own clock.
        clock.Advance(TimeSpan.FromMilliseconds(200));
        cut.WaitForAssertion(() => Text(cut, "ai-agents-graph-call-log").Should().Contain("1"));
        Section(cut, "ai-agents-graph")
            .QuerySelector("[data-testid='agent-graph-node'][data-node-id='search']")!
            .GetAttribute("aria-label").Should().Contain("Calls: 4",
                "the page pushed a real AgentGraphCall and the component folded it into the edge");
    }

    [Fact]
    public void ThePlanStartsInReview_AndTheRunWalksStepByStep()
    {
        var clock = RegisterClock();
        var cut = RenderPage();

        Section(cut, "ai-agents-run-state").TextContent.Trim()
            .Should().Be("The plan is waiting for you to start it.");
        cut.FindAll("[data-testid='ai-agents-plan-run']").Should().BeEmpty();

        cut.Find("[data-testid='ai-agents-plan'] [data-testid='agent-plan-run']").Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='ai-agents-plan-run']").Should().NotBeEmpty(
            "the page switches to the read-only running surface"));
        PlanRows(cut, "ai-agents-plan-run").Select(row => row.GetAttribute("data-status")).Should()
            .Equal("running", "pending", "pending", "pending");

        Tick(clock);
        cut.WaitForAssertion(() => Text(cut, "ai-agents-run-state").Should().Contain("1 of 4"));
        PlanRows(cut, "ai-agents-plan-run").Select(row => row.GetAttribute("data-status")).Should()
            .Equal("done", "running", "pending", "pending");
    }

    [Fact]
    public void OneStepFails_TheRetryResumes_TheLastStepWaitsForApproval_AndApprovingFinishesTheRun()
    {
        var clock = RegisterClock();
        var cut = RenderPage();

        cut.Find("[data-testid='ai-agents-plan'] [data-testid='agent-plan-run']").Click();
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='ai-agents-plan-run']").Should().NotBeEmpty());

        Tick(clock); // step 1 done, step 2 runs
        cut.WaitForAssertion(() => Text(cut, "ai-agents-run-state").Should().Contain("1 of 4"));

        Tick(clock); // step 2 fails
        cut.WaitForAssertion(() => Text(cut, "ai-agents-run-state").Should().Contain("failed"));
        cut.FindAll("[data-testid='ai-agents-plan-run'] [data-testid='agent-plan-step-retry']")
            .Should().ContainSingle("the failed step offers Retry");

        cut.Find("[data-testid='ai-agents-plan-run'] [data-testid='agent-plan-step-retry']").Click();
        cut.WaitForAssertion(() => Text(cut, "ai-agents-run-state").Should().Contain("1 of 4"));

        Tick(clock); // the retried step completes, step 3 runs
        cut.WaitForAssertion(() => Text(cut, "ai-agents-run-state").Should().Contain("2 of 4"));

        Tick(clock); // step 3 completes; the last step is gated by the approval
        cut.WaitForAssertion(() => Text(cut, "ai-agents-run-state").Should()
            .Be("The last step is waiting for your approval."));
        Text(cut, "ai-agents-monitor-decision").Should().Be("The run is waiting for your decision.",
            "the side read-out must not claim nothing is pending while the card is on screen");
        var approve = cut.Find("[data-testid='ai-agents-monitor'] [data-testid='approval-approve']");
        approve.Should().NotBeNull("the host TmApprovalCard lives in the monitor's approval slot");
        cut.Find("[data-testid='ai-agents-monitor'] [data-testid='approval-title']").TextContent.Trim()
            .Should().Be("Publish the summary?");

        approve.Click();
        cut.WaitForAssertion(() => Text(cut, "ai-agents-monitor-decision").Should()
            .Be("The page received Approve and the run is finishing."));

        Tick(clock); // the approved last step runs and finishes
        cut.WaitForAssertion(() => Text(cut, "ai-agents-run-state").Should().Contain("finished all 4 steps"));
        PlanRows(cut, "ai-agents-plan-run").Select(row => row.GetAttribute("data-status")).Should()
            .Equal("done", "done", "done", "done");
    }

    [Fact]
    public void RejectingTheApprovalSkipsTheLastStep()
    {
        var clock = RegisterClock();
        var cut = RenderPage();

        cut.Find("[data-testid='ai-agents-plan'] [data-testid='agent-plan-run']").Click();
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='ai-agents-plan-run']").Should().NotBeEmpty());

        Tick(clock);
        Tick(clock);
        cut.WaitForAssertion(() => Text(cut, "ai-agents-run-state").Should().Contain("failed"));
        cut.Find("[data-testid='ai-agents-plan-run'] [data-testid='agent-plan-step-retry']").Click();
        Tick(clock);
        Tick(clock);
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='ai-agents-monitor'] [data-testid='approval-reject']")
            .Should().NotBeEmpty());

        cut.Find("[data-testid='ai-agents-monitor'] [data-testid='approval-reject']").Click();

        cut.WaitForAssertion(() => Text(cut, "ai-agents-run-state").Should()
            .Be("You rejected the last step, so the run finished without it."));
        PlanRows(cut, "ai-agents-plan-run").Select(row => row.GetAttribute("data-status")).Should()
            .Equal("done", "done", "done", "skipped");
        Text(cut, "ai-agents-monitor-decision").Should()
            .Be("The page received Reject and the last step was skipped.");
    }

    [Fact]
    public async Task AKeyboardReorderIsAppliedByThePage_AndRecorded()
    {
        var cut = RenderPage();

        const string Row = "[data-testid='ai-agents-plan'] .tm-sortable-list__item";
        cut.FindAll(Row).Should().HaveCount(4);
        // Every key re-finds the row inside the dispatcher: the sortable list re-renders after each step,
        // so a held element reference would carry a stale event-handler id.
        await KeyAsync(cut, Row, 0, "Enter");
        await KeyAsync(cut, Row, 0, "ArrowDown");
        await cut.InvokeAsync(() => cut.FindAll(Row)[0].KeyUp(new KeyboardEventArgs { Key = " " }));

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='ai-agents-plan'] [data-testid='agent-plan-step-title']")
            .Select(title => title.TextContent.Trim())
            .Should().Equal(
                "Match the payments", "Collect the invoices", "Reconcile the ledger", "Publish the summary"));
        Section(cut, "ai-agents-plan-last-edit").TextContent.Trim()
            .Should().Be("Moved the step from position 1 to 2.");
    }

    [Fact]
    public async Task TheRunTreeHasThreeSubAgents_RollsUpTheRoot_AndSelectsByKeyboard()
    {
        var cut = RenderPage();

        var rows = cut.FindAll("[data-testid='ai-agents-tree'] [data-testid='agent-run-tree-row']").ToList();
        rows.Should().HaveCount(7);
        rows.Count(row => row.GetAttribute("aria-level") == "2").Should().Be(3,
            "three sub-agents hang under the root");

        var rootCost = rows[0].QuerySelector(".tm-ai-agent-run-tree__cost");
        rootCost.Should().NotBeNull();
        rootCost!.GetAttribute("data-rollup").Should().Be("true");
        rootCost.GetAttribute("aria-label").Should().StartWith(
            "Total for Reconciliation agent including its sub-agents:");

        const string TreeRow = "[data-testid='ai-agents-tree'] [data-testid='agent-run-tree-row']";
        await KeyAsync(cut, TreeRow, 0, "ArrowDown");
        cut.WaitForAssertion(() => cut.FindAll(TreeRow)[1].GetAttribute("tabindex").Should().Be("0"));
        await KeyAsync(cut, TreeRow, 1, "Enter");

        cut.WaitForAssertion(() => Text(cut, "ai-agents-tree-selected")
            .Should().Be("Selected: Collect the invoices"));
    }

    [Fact]
    public void TheModelCatalogueHasSixFictionalModels_AndTheSelectionCarriesTheEffort()
    {
        var cut = RenderPage();

        Text(cut, "ai-agents-models-notice").Should().Contain("fictional",
            "the invented catalogue has to say so");

        cut.Find("[data-testid='ai-agents-model-picker'] [data-testid='model-picker-trigger']").Click();
        // The popup is an overlay panel: under load its first render can land after the click's own
        // dispatcher cycle, so wait for the list instead of reading the tree once.
        cut.WaitForAssertion(() => cut.FindAll(
                "[data-testid='ai-agents-model-picker'] [data-testid='model-picker-option']")
            .Should().HaveCount(6));

        cut.Find("[data-testid='ai-agents-model-picker'] [data-model-id='nimbus-2']").Click();
        cut.WaitForAssertion(() => Text(cut, "ai-agents-model-selection")
            .Should().Contain("Nimbus 2"));
        Text(cut, "ai-agents-model-selection").Should().Contain("extended thinking: off");

        // Re-open the list: the picked model's reasoning options sit above it.
        cut.Find("[data-testid='ai-agents-model-picker'] [data-testid='model-picker-trigger']").Click();
        cut.WaitForAssertion(() => cut.FindAll(
                "[data-testid='ai-agents-model-picker'] [data-testid='model-picker-thinking'] input")
            .Should().ContainSingle());
        cut.Find("[data-testid='ai-agents-model-picker'] [data-testid='model-picker-thinking'] input")
            .Change(true);

        cut.WaitForAssertion(() => Text(cut, "ai-agents-model-selection").Should()
            .Contain("extended thinking: on"));

        cut.Find("[data-testid='ai-agents-model-picker'] [data-testid='model-picker-effort'] [data-value='high']")
            .Click();

        cut.WaitForAssertion(() => Text(cut, "ai-agents-model-selection").Should()
            .Be("Model: Nimbus 2 · extended thinking: on · effort: High"));

        // The monitor's Model metric follows the page's selection.
        cut.Find("[data-testid='ai-agents-monitor'] .tm-ai-agent-monitor__metric--model .tm-ai-agent-monitor__metric-value")
            .TextContent.Trim().Should().Be("Nimbus 2");
    }

    [Fact]
    public void TheThreeContextVariantsShareTheSnapshot_AndRemovalAndCompactionAreApplied()
    {
        var cut = RenderPage();

        Text(cut, "ai-agents-context-readout").Should().Contain("13,800").And.Contain("of 16,000");

        foreach (var variant in new[] { "bar", "grid", "treemap" })
        {
            var root = $"[data-testid='ai-agents-context-{variant}']";
            cut.FindAll($"{root} [data-testid='context-usage-remove-section']").Should().NotBeEmpty(
                $"the {variant} variant binds the removal handler");
            cut.FindAll($"{root} [data-testid='context-usage-compact']").Should().ContainSingle(
                $"the {variant} variant binds the compaction handler");
        }

        // Expand Tools in the bar and drop the Web search entry.
        cut.Find("[data-testid='ai-agents-context-bar'] [data-section-key='tools'] [data-testid='context-usage-toggle']").Click();
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='ai-agents-context-bar'] [data-item-id='search']")
            .Should().NotBeEmpty());
        cut.Find("[data-testid='ai-agents-context-bar'] [data-item-id='search'] [data-testid='context-usage-remove-item']").Click();

        cut.WaitForAssertion(() => Text(cut, "ai-agents-context-readout").Should().Contain("13,300"));
        Text(cut, "ai-agents-context-log").Should().Be("Removed Web search from Tools.");

        // The same snapshot drives the grid: the removed entry is gone there too.
        cut.FindAll("[data-testid='ai-agents-context-grid'] [data-testid='context-usage-row'][data-section-key='tools']")
            .Count.Should().Be(1);
        Text(cut, "ai-agents-context-readout").Should().Contain("sections: 6");

        cut.Find("[data-testid='ai-agents-context-bar'] [data-testid='context-usage-compact']").Click();
        cut.WaitForAssertion(() => Text(cut, "ai-agents-context-log").Should().Be("Compacted the optional sections."));

        cut.Find("[data-testid='ai-agents-context-reset']").Click();
        cut.WaitForAssertion(() => Text(cut, "ai-agents-context-readout").Should().Contain("13,800"));
        Text(cut, "ai-agents-context-log").Should().Be("Nothing has been removed yet.");
    }

    [Fact]
    public void ThePromptLibraryOffersFiveTemplates_AndUseHandsTheAssembledPromptToThePage()
    {
        var cut = RenderPage();

        cut.FindAll("[data-testid='ai-agents-prompts'] [data-testid='prompt-library-template']")
            .Should().HaveCount(5);
        Text(cut, "ai-agents-used-empty").Should().Be("No prompt has been used yet.");
        cut.Find("[data-testid='ai-agents-prompts'] [data-testid='prompt-library-search']")
            .GetAttribute("value").Should().BeNullOrEmpty("the search field starts empty, not with its field name");

        cut.Find("[data-testid='ai-agents-prompts'] [data-testid='prompt-library-variable'][data-variable='product']")
            .Input("Tempo Suite");
        cut.Find("[data-testid='ai-agents-prompts'] [data-testid='prompt-library-use']").Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='ai-agents-used-prompt']").Should().NotBeEmpty());
        Text(cut, "ai-agents-used-prompt").Should()
            .Contain("Tempo Suite version 2.10.0")
            .And.Contain("new features");
        Text(cut, "ai-agents-used-template").Should().Be("Used “Draft release notes”:");
    }

    [Fact]
    public void SelectingAnotherTemplateSwitchesTheGeneratedForm()
    {
        var cut = RenderPage();

        cut.FindAll("[data-testid='ai-agents-prompts'] [data-testid='prompt-library-template']")
            .Single(row => row.GetAttribute("data-template-id") == "code-review").Click();

        cut.WaitForAssertion(() => cut.FindAll(
                "[data-testid='ai-agents-prompts'] [data-testid='prompt-library-variable'][data-variable='diff']")
            .Should().NotBeEmpty());
        cut.Find("[data-testid='ai-agents-prompts'] [data-testid='prompt-library-preview']")
            .TextContent.Should().Contain("Unfilled");
    }

    /// <summary>
    /// The run-state line changes with NO user action (a step fails, an approval is requested, the run
    /// finishes), and the page's own "failed → Retry" instruction is never announced by the monitor. It is
    /// therefore a polite status region; the decision line stays plain so the same change is not announced
    /// twice.
    /// </summary>
    [Fact]
    public void TheRunStateLineIsAPoliteLiveRegion()
    {
        var cut = RenderPage();

        var state = Section(cut, "ai-agents-run-state");
        state.GetAttribute("role").Should().Be("status");
        state.GetAttribute("aria-live").Should().Be("polite");
        Section(cut, "ai-agents-monitor-decision").GetAttribute("role").Should().BeNull(
            "the decision line must not announce the same change a second time");
    }

    /// <summary>
    /// Every instruction on the page has to name the control by the label that is actually on screen:
    /// a hint that tells the reader to press "Start" (or "Retry", or "Use") describes a button that does
    /// not exist, and voice-control users cannot find the control either.
    /// </summary>
    [Fact]
    public void ThePageCopyNamesTheControlsWithTheirRealLabels()
    {
        var cut = RenderPage();

        Text(cut, "ai-agents-plan-hint").Should()
            .Contain("with Run plan")
            .And.Contain("offers Retry from this step");
        Text(cut, "ai-agents-plan-keyboard-hint").Should()
            .Contain("offers Retry from this step");
        Text(cut, "ai-agents-monitor-hint-note").Should()
            .Contain("Approve resumes")
            .And.Contain("Reject skips");
        Text(cut, "ai-agents-prompts-hint").Should()
            .Contain("press Use prompt");
    }

    [Fact]
    public void ApprovingThePlanNamesThePlansOwnApproveButton()
    {
        var cut = RenderPage();

        cut.Find("[data-testid='ai-agents-plan'] [data-testid='agent-plan-approve']").Click();

        cut.WaitForAssertion(() => Text(cut, "ai-agents-plan-decision")
            .Should().Be("The page received Approve plan."));
    }

    /// <summary>
    /// The fan-out section is the SAME run split across four workers, and the retry a reader asks for in a tile's
    /// detail is the PAGE's handler: the unit goes back to the queue with one more attempt, so the component's
    /// callback is proven to reach a host that really reacts.
    /// </summary>
    [Fact]
    public void TheFanOutGridRetriesAFailedUnit_ThroughThePagesOwnHandler()
    {
        var cut = RenderPage();

        var tiles = cut.FindAll("[data-testid='ai-agents-fan-out'] [data-testid='fan-out-tile']");
        tiles.Should().HaveCount(18);
        cut.Find("[data-testid='ai-agents-fan-out'] [data-testid='fan-out-summary-succeeded']")
            .TextContent.Trim().Should().Be("12");

        cut.Find("[data-testid='ai-agents-fan-out'] [data-testid='fan-out-tile'][data-status='failed']").Click();
        cut.WaitForAssertion(
            () => cut.FindAll("[data-testid='ai-agents-fan-out'] [data-testid='fan-out-detail']")
                .Should().ContainSingle(),
            TimeSpan.FromSeconds(10));

        cut.Find("[data-testid='ai-agents-fan-out'] [data-testid='fan-out-detail-retry']").Click();

        cut.WaitForAssertion(() =>
        {
            Text(cut, "ai-agents-fan-out-log").Should().Be("The page re-queued Invoice batch 16.");
            cut.FindAll("[data-testid='ai-agents-fan-out'] [data-testid='fan-out-tile'][data-status='failed']")
                .Should().BeEmpty("the page re-queued the failed unit");
            cut.Find("[data-testid='ai-agents-fan-out'] [data-testid='fan-out-tile'][data-task-id='unit-16']")
                .GetAttribute("data-status").Should().Be("queued");
        }, TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// The attempts section compares three drafts and the page records ONLY the winner the component reports after the
    /// reader confirmed it — the two-step choice is what keeps a stray arrow key from replacing a result.
    /// </summary>
    [Fact]
    public void TheAttemptsConfirmAWinner_AndThePageRecordsIt()
    {
        var cut = RenderPage();

        cut.FindAll("[data-testid='ai-agents-attempts'] [data-testid='parallel-attempt']").Should().HaveCount(3);
        Text(cut, "ai-agents-attempts-log").Should().Be("No winner chosen yet.");
        cut.Find("[data-testid='ai-agents-attempts'] [data-testid='parallel-attempt'][data-best='true']")
            .GetAttribute("data-attempt-id").Should().Be("draft-b", "the highest score is the best attempt");

        cut.Find("[data-testid='ai-agents-attempts'] [data-testid='parallel-attempt-radio'][data-attempt-id='draft-a']")
            .Change(true);
        Text(cut, "ai-agents-attempts-log").Should().Be("No winner chosen yet.",
            "choosing is not confirming");

        cut.Find("[data-testid='ai-agents-attempts'] [data-testid='parallel-attempts-confirm-yes']").Click();

        cut.WaitForAssertion(
            () => Text(cut, "ai-agents-attempts-log").Should().Be("The page accepted Draft A as the result."),
            TimeSpan.FromSeconds(10));
        cut.Find("[data-testid='ai-agents-attempts'] [data-testid='parallel-attempt'][data-selected='true']")
            .GetAttribute("data-attempt-id").Should().Be("draft-a");
    }

    [Fact]
    public void StartingOverRestoresTheReviewPlanAndTheMonitor()
    {
        var clock = RegisterClock();
        var cut = RenderPage();

        cut.Find("[data-testid='ai-agents-plan'] [data-testid='agent-plan-run']").Click();
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='ai-agents-plan-run']").Should().NotBeEmpty());
        Tick(clock);
        cut.WaitForAssertion(() => Text(cut, "ai-agents-run-state").Should().Contain("1 of 4"));

        cut.Find("[data-testid='ai-agents-reset']").Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='ai-agents-plan']").Should().NotBeEmpty());
        Text(cut, "ai-agents-run-state").Should().Be("The plan is waiting for you to start it.");
        PlanRows(cut, "ai-agents-plan").Select(row => row.GetAttribute("data-status")).Should()
            .Equal("pending", "pending", "pending", "pending");
        Text(cut, "ai-agents-monitor-decision").Should().Be("Nothing is waiting for a decision.");
    }
}
