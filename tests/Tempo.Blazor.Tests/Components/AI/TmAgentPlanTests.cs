using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// Plan 3e96f6d8, phase 2 — <c>TmAgentPlan</c> (Tempo.Blazor.AI), the plan of steps a reader reviews and
/// edits BEFORE a run and then watches while it runs.
/// <para>
/// Review mode is the editable surface: inline renaming (F2 or a click on the title, Enter saves, Escape
/// cancels), adding a step below a row or at the end, removing a step with an Undo strip, and reordering
/// through the core <c>TmSortableList</c>. The component NEVER mutates <c>Steps</c> — every edit raises
/// <c>OnPlanChanged(newSteps, edit)</c> and the host applies it, exactly like <c>TmSortableList</c> does
/// with its order.
/// </para>
/// <para>
/// Running mode is read-only: no rename, no add, no remove, no reorder, and a running step counts its
/// live elapsed time from the injected <see cref="TimeProvider"/> while the summary adds the plan total
/// ("4 of 7 done · 1:24"). A failed step shows its error and offers "Retry from this step".
/// </para>
/// </summary>
public class TmAgentPlanTests : LocalizationTestBase
{
    private static readonly DateTimeOffset Start = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

    private const string FocusInvocation = "Blazor._internal.domWrapper.focus";

    private readonly List<(IReadOnlyList<AgentPlanStep> Steps, AgentPlanEdit Edit)> _changes = [];
    private readonly List<AgentPlanStep> _retried = [];

    private FakeTimeProvider UseFakeClock()
    {
        var time = new FakeTimeProvider(Start);
        Services.AddSingleton<TimeProvider>(time);
        return time;
    }

    /// <summary>Four reviewable steps, in order, every one of them editable.</summary>
    private static AgentPlanStep[] ReviewSteps() =>
    [
        Step("s1", "Draft the outline"),
        Step("s2", "Confirm the sources"),
        Step("s3", "Review with the team"),
        Step("s4", "Publish the summary"),
    ];

    private static AgentPlanStep Step(string id, string title, AgentStepStatus status = AgentStepStatus.Pending)
        => new(title, id) { Status = status };

    private IRenderedComponent<TmAgentPlan> RenderPlan(
        IReadOnlyList<AgentPlanStep>? steps = null,
        Action<ComponentParameterCollectionBuilder<TmAgentPlan>>? configure = null,
        bool withHandler = true)
    {
        _changes.Clear();
        _retried.Clear();

        return Render<TmAgentPlan>(parameters =>
        {
            parameters
                .Add(plan => plan.Steps, steps ?? ReviewSteps())
                .Add(plan => plan.OnRetryFrom, step => _retried.Add(step));

            // A host that owns no plan at all passes no handler: every edit the component reports there
            // would be dropped on the floor, so the component must not pretend it happened.
            if (withHandler)
            {
                parameters.Add(plan => plan.OnPlanChanged,
                    (Func<IReadOnlyList<AgentPlanStep>, AgentPlanEdit, Task>)OnPlanChangedAsync);
            }

            configure?.Invoke(parameters);
        });
    }

    private Task OnPlanChangedAsync(IReadOnlyList<AgentPlanStep> steps, AgentPlanEdit edit)
    {
        _changes.Add((steps, edit));
        return Task.CompletedTask;
    }

    private static IReadOnlyList<string> StepTitles(IRenderedComponent<TmAgentPlan> cut)
        => cut.FindAll("[data-testid='agent-plan-step-title']").Select(row => row.TextContent.Trim()).ToList();

    private static IReadOnlyList<IElement> Rows(IRenderedComponent<TmAgentPlan> cut)
        => cut.FindAll("[data-testid='agent-plan-step']").ToList();

    private static string Announcement(IRenderedComponent<TmAgentPlan> cut)
        => cut.Find("[data-testid='agent-plan-announcement']").TextContent.Trim();

    private static string Summary(IRenderedComponent<TmAgentPlan> cut)
        => cut.Find("[data-testid='agent-plan-summary']").TextContent.Trim();

    private static string TextOf(IRenderedComponent<TmAgentPlan> cut, string testId)
        => cut.Find($"[data-testid='{testId}']").TextContent.Trim();

    /// <summary>The ids of the elements the component moved the focus to, in call order.</summary>
    private List<string?> FocusTargets()
        => JSInterop.Invocations
            .Where(invocation => invocation.Identifier == FocusInvocation)
            .Select(invocation => (string?)((ElementReference)invocation.Arguments[0]!).Id)
            .ToList();

    /// <summary>
    /// A fake-clock advance delivers one timer wake per elapsed second and each wake renders on its own
    /// dispatcher cycle, so a live timer needs a few cycles before the label is final.
    /// </summary>
    private static async Task PumpAsync<TComponent>(IRenderedComponent<TComponent> cut, int cycles = 6)
        where TComponent : IComponent
    {
        for (var cycle = 0; cycle < cycles; cycle++)
        {
            await cut.InvokeAsync(() => { });
        }
    }

    /// <summary>
    /// Applies the last reported edit the way a host does — bUnit 2 re-renders an existing instance with
    /// <c>SetParametersAndRenderAsync</c> (the 1.x <c>SetParametersAndRender</c> is gone).
    /// </summary>
    /// <summary>
    /// Applies the reported edit the way a host does: the same instance is re-rendered with the new
    /// <c>Steps</c> value. bUnit 2 dropped <c>SetParametersAndRender</c>, so the view is fed to
    /// <c>SetParametersAsync</c> — a partial view leaves every other parameter alone.
    /// </summary>
    private static Task ApplyLastChangeAsync(IRenderedComponent<TmAgentPlan> cut, IReadOnlyList<AgentPlanStep> steps)
        => cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?> { [nameof(TmAgentPlan.Steps)] = steps })));

    // ── Rendering and the summary ────────────────────────────────────────────

    [Fact]
    public void ThePlan_RendersOneNumberedRowPerStep_WithItsStateAsIconAndWord()
    {
        var cut = RenderPlan(
        [
            Step("s1", "Draft the outline", AgentStepStatus.Done),
            Step("s2", "Confirm the sources", AgentStepStatus.Running),
            Step("s3", "Review with the team", AgentStepStatus.Failed),
            Step("s4", "Publish the summary", AgentStepStatus.Skipped),
            Step("s5", "Archive the thread"),
        ]);

        var rows = Rows(cut);
        rows.Should().HaveCount(5);

        rows.Select(row => row.GetAttribute("data-status")).Should()
            .Equal("done", "running", "failed", "skipped", "pending");
        rows.Select(row => row.QuerySelector("[data-testid='agent-plan-step-index']")!.TextContent.Trim())
            .Should().Equal("1", "2", "3", "4", "5");

        // The state is never colour only: every row names it in words next to an aria-hidden icon.
        rows.Select(row => row.QuerySelector("[data-testid='agent-plan-step-status']")!.TextContent.Trim())
            .Should().Equal("Done", "Running", "Failed", "Skipped", "Pending");
        // The icon is decorative (the word carries the state) and always aria-hidden; an unresolved icon
        // renders the package's fallback span, so the assertion is about the slot, not the SVG.
        rows.Select(row => row.QuerySelector("[data-testid='agent-plan-step-status'] [aria-hidden='true']"))
            .Should().OnlyContain(icon => icon != null);
    }

    [Fact]
    public void TheSummary_CountsTheFinishedSteps()
    {
        var cut = RenderPlan(
        [
            Step("s1", "One", AgentStepStatus.Done),
            Step("s2", "Two", AgentStepStatus.Done),
            Step("s3", "Three", AgentStepStatus.Pending),
        ]);

        Summary(cut).Should().Be("2 of 3 done");
    }

    [Fact]
    public void TheSummary_AddsThePlanElapsedTime_WhenTheStepsCarryTimestamps()
    {
        var cut = RenderPlan(
        [
            Timed("s1", "One", AgentStepStatus.Done, TimeSpan.Zero, TimeSpan.FromSeconds(20)),
            Timed("s2", "Two", AgentStepStatus.Done, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(40)),
            Timed("s3", "Three", AgentStepStatus.Done, TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(64)),
            Timed("s4", "Four", AgentStepStatus.Done, TimeSpan.FromSeconds(64), TimeSpan.FromSeconds(84)),
            Step("s5", "Five"),
            Step("s6", "Six"),
            Step("s7", "Seven"),
        ]);

        // Four finished steps whose recorded durations add up to one minute and twenty-four seconds.
        Summary(cut).Should().Be("4 of 7 done · 1:24");
    }

    [Fact]
    public void AnEmptyPlan_RendersItsEmptyStateAndKeepsTheAddAction()
    {
        var cut = RenderPlan([]);

        TextOf(cut, "agent-plan-empty").Should().Be("No steps yet.");
        cut.FindAll("[data-testid='agent-plan-step']").Should().BeEmpty();
        cut.Find("[data-testid='agent-plan-add']").Should().NotBeNull();
    }

    [Fact]
    public void TheTitleAndHeadingLevel_AreHostConfigurable()
    {
        var cut = RenderPlan(configure: parameters => parameters
            .Add(plan => plan.Title, "Migration plan")
            .Add(plan => plan.HeadingLevel, 2));

        var heading = cut.Find(".tm-ai-agent-plan__title");
        heading.TagName.Should().Be("H2");
        heading.TextContent.Trim().Should().Be("Migration plan");
    }

    // ── Review mode: renaming ────────────────────────────────────────────────

    [Fact]
    public async Task ClickingAStepTitle_OpensAnInlineEditor_AndEnterSavesIt()
    {
        var cut = RenderPlan();

        cut.FindAll("[data-testid='agent-plan-step-title']")[1].Click();

        var input = cut.Find("[data-testid='agent-plan-step-rename']");
        input.GetAttribute("value").Should().Be("Confirm the sources");
        FocusTargets().Should().NotBeEmpty("the editor takes the focus");

        input.Input("Confirm the sources and the numbers");
        input.KeyDown(new KeyboardEventArgs { Key = "Enter" });

        cut.WaitForAssertion(() => _changes.Should().HaveCount(1));
        var (steps, edit) = _changes[0];
        edit.Kind.Should().Be(AgentPlanEditKind.Rename);
        edit.Index.Should().Be(1);
        edit.Title.Should().Be("Confirm the sources and the numbers");
        edit.StepId.Should().Be("s2");
        steps.Select(step => step.Title).Should().Equal(
            "Draft the outline", "Confirm the sources and the numbers", "Review with the team", "Publish the summary");

        await ApplyLastChangeAsync(cut, steps);
        StepTitles(cut)[1].Should().Be("Confirm the sources and the numbers");
        cut.FindAll("[data-testid='agent-plan-step-rename']").Should().BeEmpty("the editor closed on save");
    }

    [Fact]
    public void F2OnAStepTitle_OpensTheEditor_AndEscapeCancelsWithoutAnEdit()
    {
        var cut = RenderPlan();

        cut.FindAll("[data-testid='agent-plan-step-title']")[0]
            .KeyDown(new KeyboardEventArgs { Key = "F2" });

        cut.Find("[data-testid='agent-plan-step-rename']").Input("Something else");
        cut.Find("[data-testid='agent-plan-step-rename']").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        _changes.Should().BeEmpty("Escape cancels the rename");
        cut.FindAll("[data-testid='agent-plan-step-rename']").Should().BeEmpty();
        StepTitles(cut)[0].Should().Be("Draft the outline");
        Announcement(cut).Should().Be("Renaming cancelled.");
        FocusTargets().Should().NotBeEmpty("the focus returns to the title that owned it");
    }

    [Fact]
    public async Task TheSaveAndCancelButtons_AreThePointerPathOfTheSameEdit()
    {
        var cut = RenderPlan();

        cut.FindAll("[data-testid='agent-plan-step-title']")[2].Click();
        cut.Find("[data-testid='agent-plan-step-rename']").Input("Review with the client");
        cut.Find("[data-testid='agent-plan-step-rename-save']").Click();

        cut.WaitForAssertion(() => _changes.Should().HaveCount(1));
        _changes[0].Edit.Title.Should().Be("Review with the client");

        await ApplyLastChangeAsync(cut, _changes[0].Steps);
        cut.FindAll("[data-testid='agent-plan-step-title']")[2].Click();
        cut.Find("[data-testid='agent-plan-step-rename-cancel']").Click();

        _changes.Should().HaveCount(1, "cancel raises nothing");
        StepTitles(cut)[2].Should().Be("Review with the client");
    }

    [Fact]
    public void AnEmptyRename_IsRefused_SoNoStepLosesItsTitle()
    {
        var cut = RenderPlan();

        cut.FindAll("[data-testid='agent-plan-step-title']")[0].Click();
        var input = cut.Find("[data-testid='agent-plan-step-rename']");
        input.Input("   ");
        input.KeyDown(new KeyboardEventArgs { Key = "Enter" });

        _changes.Should().BeEmpty();
        cut.FindAll("[data-testid='agent-plan-step-rename']").Should().NotBeEmpty(
            "the editor stays open so the reader can fix the title");

        // The refusal is not only announced: the field is marked invalid and the reason is on screen, so a
        // sighted reader gets the same message as one using a screen reader (WCAG 3.3.1).
        var refused = cut.Find("[data-testid='agent-plan-step-rename']");
        refused.GetAttribute("aria-invalid").Should().Be("true");
        var describedBy = refused.GetAttribute("aria-describedby");
        describedBy.Should().NotBeNullOrEmpty();
        cut.Find($"#{describedBy}").TextContent.Trim().Should().Be("A step needs a title.");

        // Typing clears the error; so does leaving the editor.
        refused.Input("Confirm the sources");
        cut.Find("[data-testid='agent-plan-step-rename']").GetAttribute("aria-invalid").Should().BeNull();
        cut.FindAll(".tm-ai-agent-plan__rename-error").Should().BeEmpty();
    }

    [Fact]
    public void CancellingARefusedRename_ClearsTheError()
    {
        var cut = RenderPlan();

        cut.FindAll("[data-testid='agent-plan-step-title']")[0].Click();
        cut.Find("[data-testid='agent-plan-step-rename']").Input("   ");
        cut.Find("[data-testid='agent-plan-step-rename']").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.Find("[data-testid='agent-plan-step-rename']").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        cut.FindAll("[data-testid='agent-plan-step-rename']").Should().BeEmpty();
        cut.FindAll(".tm-ai-agent-plan__rename-error").Should().BeEmpty();

        // Reopening starts clean, with no stale invalid state on the fresh input.
        cut.FindAll("[data-testid='agent-plan-step-title']")[0].Click();
        cut.Find("[data-testid='agent-plan-step-rename']").GetAttribute("aria-invalid").Should().BeNull();
    }

    [Fact]
    public void AStepWithoutATitle_ShowsTheUntitledFallbackAsText()
    {
        var cut = RenderPlan(
        [
            Step("s1", "   "),
            Step("s2", "Confirm the sources"),
            new AgentPlanStep(string.Empty, "s3") { IsEditable = false },
        ]);

        // The editable row and the plain-text row both name the step instead of rendering an empty label;
        // the placeholder styling marks it as a stand-in rather than a title the plan actually has.
        var titles = cut.FindAll("[data-testid='agent-plan-step-title']");
        titles[0].TextContent.Trim().Should().Be("Untitled step");
        titles[0].QuerySelector(".tm-ai-agent-plan__title-text--placeholder").Should().NotBeNull(
            "the editable row's title text carries the placeholder modifier");
        titles[2].TextContent.Trim().Should().Be("Untitled step");
        titles[2].ClassList.Should().Contain("tm-ai-agent-plan__title-text--placeholder",
            "the read-only row's title IS the text element");
        titles[1].QuerySelector(".tm-ai-agent-plan__title-text--placeholder").Should().BeNull();
        titles[1].ClassList.Should().NotContain("tm-ai-agent-plan__title-text--placeholder");
    }

    // ── Review mode: adding and removing ─────────────────────────────────────

    [Fact]
    public void AddingAStepBelowARow_InsertsItRightAfterThatRow()
    {
        var cut = RenderPlan();

        cut.FindAll("[data-testid='agent-plan-step-add']")[1].Click();

        cut.WaitForAssertion(() => _changes.Should().HaveCount(1));
        var (steps, edit) = _changes[0];
        edit.Kind.Should().Be(AgentPlanEditKind.Add);
        edit.Index.Should().Be(2);
        edit.Step.Should().NotBeNull();
        edit.Step!.Title.Should().Be("New step");
        steps.Should().HaveCount(5);
        steps[2].Title.Should().Be("New step");
        steps.Select(step => step.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void TheFooterAction_AppendsAStepAtTheEnd()
    {
        var cut = RenderPlan();

        cut.Find("[data-testid='agent-plan-add']").Click();

        cut.WaitForAssertion(() => _changes.Should().HaveCount(1));
        _changes[0].Edit.Kind.Should().Be(AgentPlanEditKind.Add);
        _changes[0].Edit.Index.Should().Be(4);
        _changes[0].Steps[^1].Title.Should().Be("New step");
    }

    [Fact]
    public async Task RemovingAStep_RaisesTheEdit_ShowsTheUndoStrip_AndUndoRestoresIt()
    {
        var cut = RenderPlan();

        cut.FindAll("[data-testid='agent-plan-step-remove']")[2].Click();

        cut.WaitForAssertion(() => _changes.Should().HaveCount(1));
        var (steps, edit) = _changes[0];
        edit.Kind.Should().Be(AgentPlanEditKind.Remove);
        edit.Index.Should().Be(2);
        edit.Step!.Title.Should().Be("Review with the team");
        steps.Select(step => step.Id).Should().Equal("s1", "s2", "s4");
        Announcement(cut).Should().Be("Step 3 removed: Review with the team");

        await ApplyLastChangeAsync(cut, steps);
        var strip = cut.Find("[data-testid='agent-plan-undo']");
        strip.TextContent.Should().Contain("Review with the team",
            "the step is already gone and the rows below were renumbered, so the title is what names it");
        cut.Find("[data-testid='agent-plan-undo-button']").GetAttribute("aria-label").Should().StartWith("Undo",
            "the accessible name starts with the visible word (WCAG 2.5.3)");

        cut.Find("[data-testid='agent-plan-undo-button']").Click();

        cut.WaitForAssertion(() => _changes.Should().HaveCount(2));
        var (restored, undo) = _changes[1];
        undo.Kind.Should().Be(AgentPlanEditKind.Add);
        undo.Index.Should().Be(2, "the step comes back where it was");
        restored.Select(step => step.Id).Should().Equal("s1", "s2", "s3", "s4");
        cut.FindAll("[data-testid='agent-plan-undo']").Should().BeEmpty("the strip is spent");
        Announcement(cut).Should().Be("Step 3 restored: Review with the team");
    }

    [Fact]
    public void ARemovalTheHostNeverReported_OffersNoUndo()
    {
        // A host with no OnPlanChanged handler owns no plan: the component cannot remove anything, so it
        // must not leave an Undo strip hanging over a row that never went away.
        var cut = RenderPlan([Step("s1", "Only step")], withHandler: false);

        cut.Find("[data-testid='agent-plan-step-remove']").Click();

        _changes.Should().BeEmpty("there is no host to apply the edit");
        cut.FindAll("[data-testid='agent-plan-undo']").Should().BeEmpty();
        cut.Instance.CanUndoRemove.Should().BeFalse();
        Rows(cut).Should().HaveCount(1);
    }

    [Fact]
    public async Task APlanThatStillContainsTheRemovedStep_DoesNotOfferUndo()
    {
        var cut = RenderPlan();

        cut.FindAll("[data-testid='agent-plan-step-remove']")[0].Click();
        cut.WaitForAssertion(() => _changes.Should().HaveCount(1));

        // The host refused the removal: its list is unchanged, so there is nothing to undo.
        await ApplyLastChangeAsync(cut, ReviewSteps());

        cut.FindAll("[data-testid='agent-plan-undo']").Should().BeEmpty();
    }

    // ── Review mode: reordering through TmSortableList ───────────────────────

    [Fact]
    public void TheRowsAreReorderable_ThroughTheCoreSortableList()
    {
        var cut = RenderPlan();

        cut.FindAll(".tm-sortable-list__item").Should().HaveCount(4);
        cut.Find(".tm-sortable-list__items[role='list']").Should().NotBeNull();

        // Enter picks the first row up, ArrowDown moves it, Space drops it.
        cut.FindAll(".tm-sortable-list__item")[0].KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.Find(".tm-sortable-list__item[data-grabbed='true']").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        cut.Find(".tm-sortable-list__item[data-grabbed='true']").KeyUp(new KeyboardEventArgs { Key = " " });

        cut.WaitForAssertion(() => _changes.Should().HaveCount(1));
        var (steps, edit) = _changes[0];
        edit.Kind.Should().Be(AgentPlanEditKind.Reorder);
        edit.FromIndex.Should().Be(0);
        edit.ToIndex.Should().Be(1);
        steps.Select(step => step.Id).Should().Equal("s2", "s1", "s3", "s4");
    }

    [Fact]
    public void AReorder_IsAnnouncedByTheList_NotTwiceByThePlan()
    {
        var cut = RenderPlan();

        cut.FindAll(".tm-sortable-list__item")[0].KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.Find(".tm-sortable-list__item[data-grabbed='true']").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        cut.Find(".tm-sortable-list__item[data-grabbed='true']").KeyUp(new KeyboardEventArgs { Key = " " });
        cut.WaitForAssertion(() => _changes.Should().HaveCount(1));

        Announcement(cut).Should().BeEmpty(
            "the nested sortable list owns the move announcement; a second sentence would duplicate it");
    }

    [Fact]
    public void AStepTheHostMarkedUneditable_OffersNoEditActions()
    {
        var cut = RenderPlan(
        [
            Step("s1", "Draft the outline"),
            new AgentPlanStep("Imported from the template", "s2") { IsEditable = false },
            Step("s3", "Publish the summary"),
        ]);

        // The uneditable row keeps its title as plain text and offers no rename, add-below or remove.
        cut.FindAll("button[data-testid='agent-plan-step-title']").Should().HaveCount(2);
        cut.FindAll("span[data-testid='agent-plan-step-title']").Should().HaveCount(1);
        cut.FindAll("[data-testid='agent-plan-step-add']").Should().HaveCount(2);
        cut.FindAll("[data-testid='agent-plan-step-remove']").Should().HaveCount(2);
        Rows(cut)[1].QuerySelector("[data-testid='agent-plan-step-add']").Should().BeNull();
        Rows(cut)[1].TextContent.Should().Contain("Imported from the template");
    }

    // ── Review mode: approve and run ─────────────────────────────────────────

    [Fact]
    public void ApproveAndRun_AreOfferedOnlyWhenTheHostBindsThem()
    {
        var plain = RenderPlan();
        plain.FindAll("[data-testid='agent-plan-approve']").Should().BeEmpty();
        plain.FindAll("[data-testid='agent-plan-run']").Should().BeEmpty();

        var approved = 0;
        var ran = 0;
        var cut = RenderPlan(configure: parameters => parameters
            .Add(plan => plan.OnApprove, () => { approved++; })
            .Add(plan => plan.OnRun, () => { ran++; }));

        cut.Find("[data-testid='agent-plan-approve']").Click();
        cut.Find("[data-testid='agent-plan-run']").Click();

        approved.Should().Be(1);
        ran.Should().Be(1);
    }

    [Fact]
    public void ARunningPlan_HidesEveryEditPathAndEveryPlanAction()
    {
        var approved = 0;
        var cut = RenderPlan(
            [
                Timed("s1", "One", AgentStepStatus.Done, TimeSpan.Zero, TimeSpan.FromSeconds(30)),
                Step("s2", "Two"),
            ],
            parameters => parameters
                .Add(plan => plan.Mode, AgentPlanMode.Running)
                .Add(plan => plan.OnApprove, () => { approved++; })
                .Add(plan => plan.OnRun, () => { }));

        cut.Find(".tm-ai-agent-plan").GetAttribute("data-mode").Should().Be("running");
        cut.FindAll("button[data-testid='agent-plan-step-title']").Should().BeEmpty("a running plan is read-only");
        cut.FindAll("span[data-testid='agent-plan-step-title']").Should().HaveCount(2, "the titles are plain text");
        cut.FindAll("[data-testid='agent-plan-step-rename']").Should().BeEmpty();
        cut.FindAll("[data-testid='agent-plan-step-add']").Should().BeEmpty();
        cut.FindAll("[data-testid='agent-plan-step-remove']").Should().BeEmpty();
        cut.FindAll("[data-testid='agent-plan-add']").Should().BeEmpty();
        cut.FindAll("[data-testid='agent-plan-approve']").Should().BeEmpty();
        cut.FindAll("[data-testid='agent-plan-run']").Should().BeEmpty();
        cut.Find(".tm-sortable-list").GetAttribute("data-disabled").Should().Be("true");
        StepTitles(cut).Should().Equal("One", "Two");
    }

    [Fact]
    public void TheRunningStep_CountsItsLiveElapsedTime_AndTheSummaryFollowsIt()
    {
        var clock = UseFakeClock();
        var cut = RenderPlan(
        [
            Timed("s1", "One", AgentStepStatus.Done, TimeSpan.Zero, TimeSpan.FromSeconds(60)),
            new AgentPlanStep("Two", "s2") { Status = AgentStepStatus.Running, StartedAt = Start },
            Step("s3", "Three"),
        ]);

        static string TimeAt(IRenderedComponent<TmAgentPlan> plan, int index)
            => plan.FindAll("[data-testid='agent-plan-step']")[index]
                .QuerySelector("[data-testid='agent-plan-step-time']")!.TextContent.Trim();

        TimeAt(cut, 0).Should().Be("1:00");
        TimeAt(cut, 1).Should().Be("0:00");
        Summary(cut).Should().Be("1 of 3 done · 1:00");

        clock.Advance(TimeSpan.FromSeconds(84));
        cut.WaitForAssertion(() => TimeAt(cut, 1).Should().Be("1:24"));
        Summary(cut).Should().Be("1 of 3 done · 2:24");

        // The finished step keeps its recorded duration instead of counting on, and the pending step
        // carries no time at all.
        TimeAt(cut, 0).Should().Be("1:00");
        cut.FindAll("[data-testid='agent-plan-step-time']").Should().HaveCount(2);
    }

    [Fact]
    public void TheRowClockAndThePlanTotal_ReadOneInstantPerRender()
    {
        // The clock advances on EVERY read, so a render that samples per row and then again for the
        // summary would print a row time and a plan total that come from two different instants.
        Services.AddSingleton<TimeProvider>(new SamplingClock(Start));

        var cut = RenderPlan(
        [
            new AgentPlanStep("Live", "s1") { Status = AgentStepStatus.Running, StartedAt = Start },
        ]);

        var row = cut.Find("[data-testid='agent-plan-step-time']").TextContent.Trim();
        row.Should().Be("0:00");
        Summary(cut).Should().Be($"0 of 1 done · {row}",
            "the row clock and the total must describe the same instant");
    }

    [Fact]
    public void AFailedStep_ShowsItsErrorAndOffersRetryFromThatStep()
    {
        var cut = RenderPlan(
        [
            Step("s1", "One", AgentStepStatus.Done),
            new AgentPlanStep("Two", "s2")
            {
                Status = AgentStepStatus.Failed,
                Error = "The source returned 503.",
            },
            Step("s3", "Three"),
        ],
            parameters => parameters.Add(plan => plan.Mode, AgentPlanMode.Running));

        TextOf(cut, "agent-plan-step-error").Should().Be("The source returned 503.");
        cut.FindAll("[data-testid='agent-plan-step-retry']").Should().HaveCount(1);

        // WCAG 2.5.3: the visible words ARE the accessible name, and the step's own title is the
        // description — so "click Retry from this step" reaches the button and a screen reader still
        // hears which step it belongs to.
        var retry = cut.Find("[data-testid='agent-plan-step-retry']");
        retry.HasAttribute("aria-label").Should().BeFalse("an aria-label would replace the visible words");
        retry.TextContent.Trim().Should().Be("Retry from this step");
        var describedBy = retry.GetAttribute("aria-describedby");
        describedBy.Should().NotBeNullOrEmpty();
        cut.Find($"#{describedBy}").TextContent.Trim().Should().Be("Two");

        retry.Click();

        _retried.Should().HaveCount(1);
        _retried[0].Id.Should().Be("s2");
        _changes.Should().BeEmpty("retrying is the host's run, not a plan edit");

        // A step that has not failed offers no retry.
        var healthy = RenderPlan(
        [
            Step("s1", "One", AgentStepStatus.Done),
            Step("s2", "Two", AgentStepStatus.Running),
        ],
            parameters => parameters.Add(plan => plan.Mode, AgentPlanMode.Running));
        healthy.FindAll("[data-testid='agent-plan-step-retry']").Should().BeEmpty();
    }

    // ── Accessibility ────────────────────────────────────────────────────────

    [Fact]
    public void ThePlan_RendersOneLiveRegion_AndNamesItsListAndInstructions()
    {
        var cut = RenderPlan();

        cut.FindAll("[data-testid='agent-plan-announcement']").Should().HaveCount(1);
        var region = cut.Find("[data-testid='agent-plan-announcement']");
        region.GetAttribute("role").Should().Be("status");
        region.GetAttribute("aria-live").Should().Be("polite");

        var instructions = cut.Find("[data-testid='agent-plan-instructions']");
        instructions.ClassList.Should().Contain("tm-sr-only");
        instructions.TextContent.Should().Contain("F2");

        // The instructions are the GROUP's description (the nested list describes itself with the
        // sortable keyboard model, which is a different hint).
        var group = cut.Find("[role='group']");
        group.GetAttribute("aria-describedby").Should().Be(instructions.Id);

        // The plan is named by the heading a reader sees, so the name is not a hidden duplicate.
        var labelledBy = group.GetAttribute("aria-labelledby");
        labelledBy.Should().NotBeNullOrEmpty();
        cut.Find($"#{labelledBy}").TextContent.Trim().Should().Be("Plan");

        // The nested list is named by the plan's accessible label so a reader who lands on it hears the
        // same context.
        cut.Find("[data-testid='agent-plan-list'] [role='list']").GetAttribute("aria-label")
            .Should().Be("Agent plan");
    }

    [Fact]
    public void ThePlanGroupsItsRowsUnderItsOwnName()
    {
        var cut = RenderPlan(configure: parameters => parameters
            .Add(plan => plan.AriaLabel, "Release plan"));

        cut.Find("[role='group']").GetAttribute("aria-label").Should().Be("Release plan");
        cut.Find("[data-testid='agent-plan-list'] [role='list']").GetAttribute("aria-label")
            .Should().Be("Release plan");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static AgentPlanStep Timed(
        string id, string title, AgentStepStatus status, TimeSpan startedAt, TimeSpan? completedAt)
        => new(title, id)
        {
            Status = status,
            StartedAt = Start + startedAt,
            CompletedAt = completedAt is { } done ? Start + done : null,
        };

    /// <summary>A clock that advances one second on every read, so two samples inside one render diverge.</summary>
    private sealed class SamplingClock(DateTimeOffset origin) : TimeProvider
    {
        private int _reads;

        public override DateTimeOffset GetUtcNow()
            => origin.AddSeconds(Interlocked.Increment(ref _reads) - 1);
    }
}
