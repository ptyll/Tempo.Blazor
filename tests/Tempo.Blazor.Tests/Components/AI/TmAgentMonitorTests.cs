using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// Plan 3e96f6d8, phase 3 — <c>TmAgentMonitor</c> (Tempo.Blazor.AI): the live strip of one agent run. It
/// answers the four questions a watcher asks without opening anything — what is it doing now, how long has
/// it been going, what has it cost so far, and how did it get here — and it folds into one status bar when
/// the reader needs the room.
/// <para>
/// The timeline draws a glyph and a hidden kind word per step (overridable through <c>IconResolver</c>), the
/// host's <c>PendingApproval</c> slot sits between the metrics and the timeline, and the collapsed bar keeps
/// the state, the title, the current step, the live clock, the cost and a pending-approval marker — the
/// essentials, never the whole timeline.
/// </para>
/// </summary>
public class TmAgentMonitorTests : LocalizationTestBase
{
    private static readonly DateTimeOffset Start = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock;

    /// <summary>
    /// The pinned clock every render reads. It is registered in the constructor because bUnit's service
    /// provider locks after the first retrieval.
    /// </summary>
    public TmAgentMonitorTests()
    {
        _clock = new FakeTimeProvider(Start);
        Services.AddSingleton<TimeProvider>(_clock);
    }

    private FakeTimeProvider UseFakeClock() => _clock;

    private static AgentMonitorStep Step(
        string id,
        string title,
        AgentStepKind kind,
        AgentStepStatus status,
        int? startedSeconds = null,
        int? completedSeconds = null,
        string? detail = null)
        => new(title, id)
        {
            Kind = kind,
            Status = status,
            Detail = detail,
            StartedAt = startedSeconds is { } start ? Start.AddSeconds(start) : null,
            CompletedAt = completedSeconds is { } end ? Start.AddSeconds(end) : null,
        };

    private static AgentMonitorStep[] Run() =>
    [
        Step("s1", "Think about the ledger", AgentStepKind.Thinking, AgentStepStatus.Done, 0, 2),
        Step("s2", "Call the ledger API", AgentStepKind.ToolCall, AgentStepStatus.Running, 2,
            detail: "ledger.read"),
        Step("s3", "Ask for approval before writing", AgentStepKind.Approval, AgentStepStatus.Pending),
    ];

    private IRenderedComponent<TmAgentMonitor> RenderMonitor(
        IReadOnlyList<AgentMonitorStep>? steps = null,
        AgentStepStatus? status = null,
        DateTimeOffset? completedAt = null,
        long? cost = 1_500_000,
        string? model = "gpt-4o",
        Func<AgentStepKind, string>? iconResolver = null,
        RenderFragment? pendingApproval = null,
        bool? collapsed = null,
        bool allowCollapse = true,
        EventCallback<bool> collapsedChanged = default)
    {
        return Render<TmAgentMonitor>(parameters =>
        {
            parameters
                .Add(monitor => monitor.Steps, steps ?? Run())
                .Add(monitor => monitor.Title, "Nightly reconciliation")
                .Add(monitor => monitor.Status, status ?? AgentStepStatus.Running)
                .Add(monitor => monitor.StartedAt, Start)
                .Add(monitor => monitor.CostMicroUnits, cost)
                .Add(monitor => monitor.Model, model)
                .Add(monitor => monitor.Culture, CultureInfo.GetCultureInfo("en-US"))
                .Add(monitor => monitor.AllowCollapse, allowCollapse);

            if (completedAt is { } end)
            {
                parameters.Add(monitor => monitor.CompletedAt, end);
            }

            if (iconResolver is not null)
            {
                parameters.Add(monitor => monitor.IconResolver, iconResolver);
            }

            if (pendingApproval is not null)
            {
                parameters.Add(monitor => monitor.PendingApproval, pendingApproval);
            }

            if (collapsed is { } folded)
            {
                parameters.Add(monitor => monitor.Collapsed, folded);
            }

            if (collapsedChanged.HasDelegate)
            {
                parameters.Add(monitor => monitor.CollapsedChanged, collapsedChanged);
            }
        });
    }

    private static string TextOf(IRenderedComponent<TmAgentMonitor> cut, string testId)
        => cut.Find($"[data-testid='{testId}']").TextContent.Trim();

    private static IReadOnlyList<AngleSharp.Dom.IElement> TimelineItems(IRenderedComponent<TmAgentMonitor> cut)
        => cut.FindAll("[data-testid='agent-run-monitor-step']").ToList();

    private static async Task PumpAsync<TComponent>(IRenderedComponent<TComponent> cut, int cycles = 6)
        where TComponent : IComponent
    {
        for (var cycle = 0; cycle < cycles; cycle++)
        {
            await cut.InvokeAsync(() => { });
        }
    }

    // ── Header and metrics ───────────────────────────────────────────────────

    [Fact]
    public void TheMonitor_NamesTheRun_AndStatesItsStatusInWords()
    {
        var cut = RenderMonitor();

        var root = cut.Find("[data-testid='agent-run-monitor']");
        root.GetAttribute("data-status").Should().Be("running");
        root.GetAttribute("role").Should().Be("region");

        var heading = cut.Find(".tm-ai-agent-monitor__title");
        heading.TagName.Should().Be("H3");
        heading.TextContent.Trim().Should().Be("Nightly reconciliation");
        root.GetAttribute("aria-labelledby").Should().Be(heading.Id);

        TextOf(cut, "agent-run-monitor-state").Should().Be("Running");
        TextOf(cut, "agent-run-monitor-headline").Should().Be("The run is going.");
        cut.Find("[data-testid='agent-run-monitor-state'] [aria-hidden='true']").Should().NotBeNull();
    }

    [Fact]
    public void TheMonitor_ShowsTheCurrentStep_TheLiveClockAndTheCost()
    {
        var cut = RenderMonitor();

        TextOf(cut, "agent-run-monitor-current").Should().Be("Call the ledger API");
        TextOf(cut, "agent-run-monitor-elapsed").Should().Be("0:00");
        TextOf(cut, "agent-run-monitor-cost").Should().Be("$1.50");
        TextOf(cut, "agent-run-monitor-model").Should().Be("gpt-4o");
    }

    /// <summary>
    /// The model cell keeps a single-line ellipsis on a narrow strip (the run title wraps), so the cut-off id
    /// stays readable through its <c>title</c>; the step and detail text wrap instead and need no fallback.
    /// </summary>
    [Fact]
    public void TheModelMetric_CarriesItsFullTextInATitle()
    {
        const string model = "openai/gpt-4o-2026-01-01-preview";
        var cut = RenderMonitor(model: model);

        cut.Find("[data-testid='agent-run-monitor-model']").GetAttribute("title").Should().Be(model);
    }

    [Fact]
    public async Task TheClockTicks_WhileTheRunIsRunning_AndStopsWhenItEnds()
    {
        var clock = UseFakeClock();
        var cut = RenderMonitor();

        clock.Advance(TimeSpan.FromSeconds(30));
        await PumpAsync(cut);
        TextOf(cut, "agent-run-monitor-elapsed").Should().Be("0:30");

        cut.Dispose();

        // A finished run reports its recorded duration and does not keep counting.
        var finished = RenderMonitor(status: AgentStepStatus.Done, completedAt: Start.AddSeconds(84));

        TextOf(finished, "agent-run-monitor-elapsed").Should().Be("1:24");
        clock.Advance(TimeSpan.FromSeconds(10));
        await PumpAsync(finished);
        TextOf(finished, "agent-run-monitor-elapsed").Should().Be("1:24");
        finished.Dispose();
    }

    [Fact]
    public void TheCurrentStep_FallsBackToTheLastStep_WhenNothingIsRunning()
    {
        var cut = RenderMonitor(
        [
            Step("s1", "Think", AgentStepKind.Thinking, AgentStepStatus.Done, 0, 2),
            Step("s2", "Call the tool", AgentStepKind.ToolCall, AgentStepStatus.Failed, 2, 5, "boom"),
        ], status: AgentStepStatus.Failed);

        TextOf(cut, "agent-run-monitor-current").Should().Be("Call the tool");
        TextOf(cut, "agent-run-monitor-state").Should().Be("Failed");
        TextOf(cut, "agent-run-monitor-headline").Should().Be("The run failed.");
    }

    [Fact]
    public void ACompletedRun_ReadsAsCompleted_AndShowsTheRecordedTime()
    {
        var cut = RenderMonitor(status: AgentStepStatus.Done, completedAt: Start.AddSeconds(40));

        TextOf(cut, "agent-run-monitor-state").Should().Be("Completed");
        TextOf(cut, "agent-run-monitor-headline").Should().Be("The run completed.");
        TextOf(cut, "agent-run-monitor-elapsed").Should().Be("0:40");
    }

    // ── Timeline ─────────────────────────────────────────────────────────────

    [Fact]
    public void TheTimeline_ListsEveryStep_WithItsKindGlyphAndHiddenKindWord()
    {
        var cut = RenderMonitor();

        var steps = TimelineItems(cut);
        steps.Should().HaveCount(3);
        steps.Select(step => step.GetAttribute("data-kind")).Should()
            .Equal("thinking", "tool-call", "approval");
        steps.Select(step => step.GetAttribute("data-status")).Should().Equal("done", "running", "pending");
        steps.Select(step => step.QuerySelector(".tm-ai-agent-monitor__step-title")!.TextContent.Trim()).Should()
            .Equal("Think about the ledger", "Call the ledger API", "Ask for approval before writing");
        steps.Select(step => step.QuerySelector(".tm-ai-agent-monitor__step-kind")!.TextContent.Trim()).Should()
            .Equal("Thinking", "Tool call", "Approval");
        steps.Select(step => step.QuerySelector(".tm-ai-agent-monitor__step-icon")!.GetAttribute("data-icon")).Should()
            .Equal("zap", "terminal", "shield-check");
        steps[1].QuerySelector(".tm-ai-agent-monitor__step-detail")!.TextContent.Trim().Should().Be("ledger.read");
        steps[0].QuerySelector(".tm-ai-agent-monitor__step-time")!.TextContent.Trim().Should().Be("0:02");
        steps[2].QuerySelector(".tm-ai-agent-monitor__step-status")!.TextContent.Trim().Should().Be("Pending");

        cut.Find("[data-testid='agent-run-monitor-timeline']").GetAttribute("aria-label").Should().Be("Steps so far");
    }

    [Fact]
    public void TheIconResolver_OverridesTheGlyphButNotTheKindWord()
    {
        var cut = RenderMonitor(
            iconResolver: kind => "custom-" + kind.ToString().ToLowerInvariant());

        TimelineItems(cut).Select(step => step.QuerySelector(".tm-ai-agent-monitor__step-icon")!.GetAttribute("data-icon"))
            .Should().Equal("custom-thinking", "custom-toolcall", "custom-approval");
        TimelineItems(cut)[1].QuerySelector(".tm-ai-agent-monitor__step-kind")!.TextContent.Trim()
            .Should().Be("Tool call");
    }

    [Fact]
    public void AnEmptyRun_RendersTheEmptyTimelineState()
    {
        var cut = RenderMonitor([]);

        TextOf(cut, "agent-run-monitor-empty").Should().Be("No steps yet.");
        TimelineItems(cut).Should().BeEmpty();
        TextOf(cut, "agent-run-monitor-current").Should().Be("No step yet");

        cut.Dispose();
    }

    // ── The approval slot ────────────────────────────────────────────────────

    [Fact]
    public void ThePendingApprovalSlot_RendersInItsOwnNamedRegion()
    {
        var cut = RenderMonitor(pendingApproval: builder =>
        {
            builder.OpenElement(0, "button");
            builder.AddAttribute(1, "data-testid", "approve-now");
            builder.AddContent(2, "Approve");
            builder.CloseElement();
        });

        var slot = cut.Find("[data-testid='agent-run-monitor-approval']");
        slot.GetAttribute("role").Should().Be("group");
        slot.GetAttribute("aria-label").Should().Be("Waiting for your approval");
        slot.QuerySelector("[data-testid='approve-now']").Should().NotBeNull();
    }

    // ── Collapse ─────────────────────────────────────────────────────────────

    [Fact]
    public void TheCollapseToggle_FoldsTheMonitorIntoItsBar_AndKeepsTheEssentials()
    {
        var collapsed = new List<bool>();
        var cut = RenderMonitor(
            collapsedChanged: EventCallback.Factory.Create<bool>(this, value => collapsed.Add(value)));

        cut.Find("[data-testid='agent-run-monitor']").GetAttribute("data-collapsed").Should().Be("false");
        cut.Find("[data-testid='agent-run-monitor-toggle']").GetAttribute("aria-expanded").Should().Be("true");
        cut.Find("[data-testid='agent-run-monitor-toggle']").GetAttribute("aria-label").Should().Be("Collapse the monitor");

        cut.Find("[data-testid='agent-run-monitor-toggle']").Click();
        collapsed.Should().Equal(true);

        cut.Find("[data-testid='agent-run-monitor']").GetAttribute("data-collapsed").Should().Be("true");
        cut.Find("[data-testid='agent-run-monitor-toggle']").GetAttribute("aria-expanded").Should().Be("false");
        cut.Find("[data-testid='agent-run-monitor-toggle']").GetAttribute("aria-label").Should().Be("Expand the monitor");

        TextOf(cut, "agent-run-monitor-announcement").Should().Be("Monitor collapsed to its status bar.");

        // The bar keeps the run's essentials...
        TextOf(cut, "agent-run-monitor-state").Should().Be("Running");
        TextOf(cut, "agent-run-monitor-current").Should().Be("Call the ledger API");
        TextOf(cut, "agent-run-monitor-elapsed").Should().Be("0:00");
        TextOf(cut, "agent-run-monitor-cost").Should().Be("$1.50");

        // ...and drops the timeline.
        TimelineItems(cut).Should().BeEmpty();
        cut.FindAll("[data-testid='agent-run-monitor-timeline']").Should().BeEmpty();
    }

    [Fact]
    public void APendingApproval_StaysVisibleInTheCollapsedBar()
    {
        var cut = RenderMonitor(
            collapsed: true,
            pendingApproval: builder => builder.AddContent(0, "approve"));

        TextOf(cut, "agent-run-monitor-approval-marker").Should().Be("Waiting for your approval");
        cut.FindAll("[data-testid='agent-run-monitor-approval']").Should().BeEmpty(
            "the slot itself needs room; the bar only reports that it is waiting");
    }

    /// <summary>
    /// A decision that starts waiting AFTER the strip mounted is otherwise silent: the collapsed marker is not
    /// a live region and the only polite region used to carry the fold/unfold action, so a screen-reader user
    /// with the strip folded was never told. The transition from no approval to an approval announces it.
    /// </summary>
    [Fact]
    public async Task ANewPendingApproval_IsAnnouncedInTheLiveRegion()
    {
        var cut = RenderMonitor();

        TextOf(cut, "agent-run-monitor-announcement").Should().BeEmpty();

        await cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?>
            {
                [nameof(TmAgentMonitor.PendingApproval)] = (RenderFragment)(builder => builder.AddContent(0, "approve")),
            })));

        TextOf(cut, "agent-run-monitor-announcement").Should().Be("A decision is waiting for your approval.");
    }

    [Fact]
    public void AHostPinnedCollapsedValue_IsHonoured_AndTheToggleReportsTheChange()    {
        var collapsed = new List<bool>();
        var cut = RenderMonitor(
            collapsed: true,
            collapsedChanged: EventCallback.Factory.Create<bool>(this, value => collapsed.Add(value)));

        cut.Find("[data-testid='agent-run-monitor']").GetAttribute("data-collapsed").Should().Be("true");

        cut.Find("[data-testid='agent-run-monitor-toggle']").Click();
        collapsed.Should().Equal(false);
        cut.Find("[data-testid='agent-run-monitor']").GetAttribute("data-collapsed").Should().Be("false");
    }

    [Fact]
    public void AllowCollapseFalse_RendersNoToggle_AndKeepsTheMonitorExpanded()
    {
        var cut = RenderMonitor(collapsed: true, allowCollapse: false);

        cut.FindAll("[data-testid='agent-run-monitor-toggle']").Should().BeEmpty();
        cut.Find("[data-testid='agent-run-monitor']").GetAttribute("data-collapsed").Should().Be("false");
        TimelineItems(cut).Should().HaveCount(3);
    }

    // ── Accessibility bookkeeping ────────────────────────────────────────────

    [Fact]
    public void TheMonitor_HasExactlyOneLiveRegion_AndTheClockIsNotLive()
    {
        var cut = RenderMonitor();

        var live = cut.FindAll("[aria-live]").Where(element => element.GetAttribute("aria-live") != "off").ToList();
        live.Should().ContainSingle();
        live[0].GetAttribute("aria-live").Should().Be("polite");
        live[0].GetAttribute("role").Should().Be("status");
        cut.Find("[data-testid='agent-run-monitor-elapsed']").GetAttribute("aria-live").Should().Be("off");
    }
}
