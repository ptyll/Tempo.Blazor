using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.AI.Formatting;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.Icons;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// One agent run, live: what it is doing right now, how long it has been going, what it has cost, and the
/// timeline of the steps that got it here.
/// <para>
/// The strip answers the four questions a watcher asks without opening anything — the current step, the live
/// clock (from the injected <see cref="TimeProvider"/>, ticking only while the run is
/// <see cref="AgentStepStatus.Running"/>), the cost of the run so far and the steps' timeline with a glyph and
/// a hidden kind word per step. A host that supplies <see cref="PendingApproval"/> gets its decision surface
/// in its own named region between the metrics and the timeline.
/// </para>
/// <para>
/// <see cref="Collapsed"/> folds the strip into one status bar that keeps the essentials — state, title,
/// current step, clock, cost and a marker that an approval is waiting — and drops the timeline. The toggle is
/// a native button with <c>aria-expanded</c>, the bar announces the fold into the component's single polite
/// live region (which also announces a pending approval that appears, so a folded reader is never left waiting
/// silently), and a host that pins <see cref="Collapsed"/> keeps control of it.
/// </para>
/// <para>
/// The monitor is transport-agnostic: it renders the snapshot it is given and never polls anything. A host
/// pushes new steps and totals as the run reports them (SignalR, polling, a server-side channel).
/// </para>
/// </summary>
public partial class TmAgentMonitor : TmComponentBase, IDisposable
{
    private readonly string _id = $"tm-ai-agent-monitor-{Guid.NewGuid():N}";

    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    private TimeProvider _clock = TimeProvider.System;
    private AiElapsedTicker? _ticker;
    private bool _disposed;

    /// <summary>The instant this render paints with: sampled ONCE per render, so the header clock and the
    /// steps' clocks describe the same moment while the client stamps each paint.</summary>
    private DateTimeOffset _now;

    private bool _collapsed;
    private bool _lastCollapsed;
    private bool _hadPendingApproval;
    private bool _hasRendered;
    private string _announcement = string.Empty;
    private bool _announcementMarker;

    // ── Parameters ───────────────────────────────────────────────────────────

    /// <summary>
    /// The steps of the run so far, oldest first. The current step is the last <see
    /// cref="AgentStepStatus.Running"/> one, or the last step when nothing runs. Null or empty renders the
    /// localized empty sentence in place of the timeline; the header still shows the run's state.
    /// </summary>
    [Parameter] public IReadOnlyList<AgentMonitorStep>? Steps { get; set; }

    /// <summary>
    /// The heading above the strip. Null renders the localized default ("Agent run"). The heading also names
    /// the whole region, so the name a reader hears is the one they see.
    /// </summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>Whether the heading is rendered. Default <c>true</c>; the region still keeps its accessible
    /// name either way.</summary>
    [Parameter] public bool ShowTitle { get; set; } = true;

    /// <summary>The heading level of the rendered title, clamped to 2–6. Default 3.</summary>
    [Parameter] public int HeadingLevel { get; set; } = 3;

    /// <summary>
    /// The state of the whole run: it drives the state word and icon in the bar, the headline sentence and
    /// the live clock. Default <see cref="AgentStepStatus.Running"/>.
    /// </summary>
    [Parameter] public AgentStepStatus Status { get; set; } = AgentStepStatus.Running;

    /// <summary>When the run started, from the host's clock. Null renders no clock at all (rather than an
    /// invented one).</summary>
    [Parameter] public DateTimeOffset? StartedAt { get; set; }

    /// <summary>When the run finished. Null while it is still running; a finished run stops its clock and
    /// keeps its recorded duration.</summary>
    [Parameter] public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// What the run has cost so far, in MICRO-UNITS (1,000,000 is 1.00 of the currency). Null renders the
    /// cost metric with an em dash, so an unreported cost never reads as free. The monitor formats it through
    /// <see cref="CostFormatter"/> or <see cref="AiCostFormatter"/>.
    /// </summary>
    [Parameter] public long? CostMicroUnits { get; set; }

    /// <summary>The model the run is using (an id or a display name). Null hides the model metric.</summary>
    [Parameter] public string? Model { get; set; }

    /// <summary>
    /// The host's decision surface for a pending approval — an approve/reject pair, a link, a short form. It
    /// renders in its own <c>role="group"</c> named by the localized "Waiting for your approval", between the
    /// metrics and the timeline. Null renders no approval region at all. An approval that appears while the
    /// component is showing is announced in its polite live region, because a folded strip only shows a
    /// non-live marker.
    /// </summary>
    [Parameter] public RenderFragment? PendingApproval { get; set; }

    /// <summary>
    /// Whether the strip is folded into its status bar, two-way bound with <see cref="CollapsedChanged"/>.
    /// Default <c>false</c>. The bar keeps the state, the title, the current step, the clock, the cost and a
    /// pending-approval marker; the timeline and the approval slot need the room. A host that pins the
    /// parameter keeps control: the reader's toggle reports the change and the next render from the host
    /// decides.
    /// </summary>
    [Parameter] public bool Collapsed { get; set; }

    /// <summary>Fires with the new collapsed state when the reader presses the toggle.</summary>
    [Parameter] public EventCallback<bool> CollapsedChanged { get; set; }

    /// <summary>
    /// Whether the strip may be folded at all. Default <c>true</c>. <c>false</c> removes the toggle and keeps
    /// the strip expanded even when <see cref="Collapsed"/> is pinned <c>true</c> — a bar a host cannot reopen
    /// is not a feature.
    /// </summary>
    [Parameter] public bool AllowCollapse { get; set; } = true;

    /// <summary>Whether the steps' timeline is rendered. Default <c>true</c>.</summary>
    [Parameter] public bool ShowTimeline { get; set; } = true;

    /// <summary>Whether the elapsed-time metric is rendered. Default <c>true</c>.</summary>
    [Parameter] public bool ShowElapsed { get; set; } = true;

    /// <summary>Whether the cost metric is rendered. Default <c>true</c>.</summary>
    [Parameter] public bool ShowCost { get; set; } = true;

    /// <summary>Whether the model metric is rendered (it is hidden anyway when <see cref="Model"/> is
    /// null). Default <c>true</c>.</summary>
    [Parameter] public bool ShowModel { get; set; } = true;

    /// <summary>
    /// Maps a step's <see cref="AgentStepKind"/> to an icon name (see <see cref="IconNames"/>). Null uses the
    /// component's own mapping (thinking → a spark, a tool call → a terminal, an approval → a shield, …). The
    /// kind's WORD is always rendered for a screen reader, so an override never removes the only copy of the
    /// information.
    /// </summary>
    [Parameter] public Func<AgentStepKind, string>? IconResolver { get; set; }

    /// <summary>
    /// Formats the run's cost given in MICRO-UNITS. Null uses <see cref="AiCostFormatter"/> with
    /// <see cref="Culture"/> — the culture's own currency.
    /// </summary>
    [Parameter] public Func<long, string>? CostFormatter { get; set; }

    /// <summary>
    /// The culture that formats the cost. Null (the default) means the current culture at render time. The
    /// clock-style duration is not localized on purpose.
    /// </summary>
    [Parameter] public CultureInfo? Culture { get; set; }

    /// <summary>
    /// The clock the live timer reads and schedules from. Null (the default) uses the registered
    /// <see cref="System.TimeProvider"/> and falls back to <see cref="System.TimeProvider.System"/>.
    /// </summary>
    [Parameter] public TimeProvider? TimeProvider { get; set; }

    /// <summary>Rendered instead of the localized "No steps yet." when <see cref="Steps"/> is empty.</summary>
    [Parameter] public RenderFragment? EmptyContent { get; set; }

    /// <summary>The accessible name of the strip when <see cref="ShowTitle"/> is <c>false</c>. Defaults to
    /// the localized "Agent monitor".</summary>
    [Parameter] public string? AriaLabel { get; set; }

    /// <summary>Additional CSS class(es) for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional HTML attributes applied to the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }

    // ── Derived state ────────────────────────────────────────────────────────

    /// <summary>Latest announcement, mirrored into the component's single live region.</summary>
    public string Announcement => _announcement;

    /// <summary>The step the run is on: the last <see cref="AgentStepStatus.Running"/> step, or the last step
    /// of the list when nothing runs (a finished or failed run ends on its last step). Null when the run has
    /// no steps.</summary>
    public AgentMonitorStep? CurrentStep
        => StepList.LastOrDefault(step => step.Status == AgentStepStatus.Running)
           ?? StepList.LastOrDefault();

    /// <summary>The run's elapsed time: its recorded duration once it finished, otherwise the time since
    /// <see cref="StartedAt"/>. Null when the host supplied no start.</summary>
    public TimeSpan? Elapsed
    {
        get
        {
            if (StartedAt is not { } start)
            {
                return null;
            }

            var end = CompletedAt ?? _now;
            return end > start ? end - start : TimeSpan.Zero;
        }
    }

    /// <summary>Whether the strip is currently folded into its bar (and <see cref="AllowCollapse"/> is
    /// on).</summary>
    public bool IsCollapsed => AllowCollapse && _collapsed;

    private CultureInfo EffectiveCulture => Culture ?? CultureInfo.CurrentCulture;

    private IReadOnlyList<AgentMonitorStep> StepList => Steps ?? [];

    private string TitleId => _id + "-title";

    private string HeadingText => string.IsNullOrWhiteSpace(Title) ? Loc["TmAgentMonitor_Title"] : Title!;

    private int EffectiveHeadingLevel => Math.Clamp(HeadingLevel, 2, 6);

    private string? RegionAriaLabel
        => string.IsNullOrWhiteSpace(AriaLabel) ? Loc["TmAgentMonitor_AriaLabel"] : AriaLabel;

    private bool NeedsTicking => !_disposed
        && Status == AgentStepStatus.Running
        && StartedAt is not null
        && CompletedAt is null;

    private string RootClass
    {
        get
        {
            var classes = new StringBuilder("tm-ai-agent-monitor");
            if (IsCollapsed)
            {
                classes.Append(" tm-ai-agent-monitor--collapsed");
            }

            if (!string.IsNullOrEmpty(Class))
            {
                classes.Append(' ').Append(Class);
            }

            return classes.ToString();
        }
    }

    private string CurrentStepTitle
        => CurrentStep is { } step ? StepTitle(step) : Loc["TmAgentMonitor_NoCurrentStep"];

    private string ElapsedText => Elapsed is { } elapsed ? AiClockFormatter.FormatClock(elapsed) : "—";

    private string CostText => CostMicroUnits is { } microUnits ? FormatCost(microUnits) : "—";

    private string StatusText => Status switch
    {
        AgentStepStatus.Running => Loc["TmAgentRun_StatusRunning"],
        AgentStepStatus.Done => Loc["TmAgentRun_StatusDone"],
        AgentStepStatus.Failed => Loc["TmAgentRun_StatusFailed"],
        AgentStepStatus.Skipped => Loc["TmAgentRun_StatusSkipped"],
        _ => Loc["TmAgentRun_StatusPending"],
    };

    private string HeadlineText => Status switch
    {
        AgentStepStatus.Running => Loc["TmAgentMonitor_RunRunning"],
        AgentStepStatus.Done => Loc["TmAgentMonitor_RunDone"],
        AgentStepStatus.Failed => Loc["TmAgentMonitor_RunFailed"],
        AgentStepStatus.Skipped => Loc["TmAgentMonitor_RunSkipped"],
        _ => Loc["TmAgentMonitor_RunPending"],
    };

    private static string StatusSlug(AgentStepStatus status) => status switch
    {
        AgentStepStatus.Running => "running",
        AgentStepStatus.Done => "done",
        AgentStepStatus.Failed => "failed",
        AgentStepStatus.Skipped => "skipped",
        _ => "pending",
    };

    private static string StatusIcon(AgentStepStatus status) => status switch
    {
        AgentStepStatus.Running => IconNames.Play,
        AgentStepStatus.Done => IconNames.CheckCircle,
        AgentStepStatus.Failed => IconNames.XCircle,
        AgentStepStatus.Skipped => IconNames.SkipForward,
        _ => IconNames.Circle,
    };

    private static string KindSlug(AgentStepKind kind) => kind switch
    {
        AgentStepKind.ToolCall => "tool-call",
        AgentStepKind.Message => "message",
        AgentStepKind.Approval => "approval",
        AgentStepKind.Handoff => "handoff",
        AgentStepKind.Result => "result",
        _ => "thinking",
    };

    private string KindIcon(AgentStepKind kind)
        => IconResolver is { } resolver
            ? resolver(kind)
            : kind switch
            {
                AgentStepKind.ToolCall => IconNames.Terminal,
                AgentStepKind.Message => IconNames.MessageSquare,
                AgentStepKind.Approval => IconNames.ShieldCheck,
                AgentStepKind.Handoff => IconNames.Users,
                AgentStepKind.Result => IconNames.CheckCircle,
                _ => IconNames.Zap,
            };

    private string KindText(AgentStepKind kind) => kind switch
    {
        AgentStepKind.ToolCall => Loc["TmAgentStepKind_ToolCall"],
        AgentStepKind.Message => Loc["TmAgentStepKind_Message"],
        AgentStepKind.Approval => Loc["TmAgentStepKind_Approval"],
        AgentStepKind.Handoff => Loc["TmAgentStepKind_Handoff"],
        AgentStepKind.Result => Loc["TmAgentStepKind_Result"],
        _ => Loc["TmAgentStepKind_Thinking"],
    };

    private string StepTitle(AgentMonitorStep step)
        => string.IsNullOrWhiteSpace(step.Title) ? Loc["TmAgentMonitor_Untitled"] : step.Title;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        _clock = TimeProvider
            ?? ServiceProvider.GetService<TimeProvider>()
            ?? System.TimeProvider.System;
        _ticker = new AiElapsedTicker(_clock, OnTickAsync);
        _lastCollapsed = Collapsed;
        _collapsed = Collapsed;
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        var clock = TimeProvider
            ?? ServiceProvider.GetService<TimeProvider>()
            ?? System.TimeProvider.System;
        if (!ReferenceEquals(clock, _clock))
        {
            _clock = clock;
            _ticker?.Dispose();
            _ticker = new AiElapsedTicker(_clock, OnTickAsync);
        }

        if (Collapsed != _lastCollapsed)
        {
            _lastCollapsed = Collapsed;
            _collapsed = Collapsed;
        }

        // A decision that starts waiting while the strip is already showing must reach a screen-reader user
        // even when the strip is folded: the bar's marker is not a live region and the approval slot needs the
        // room the folded strip has not. A first render that already carries one is read in the normal flow.
        var hasPendingApproval = PendingApproval is not null;
        if (_hasRendered && hasPendingApproval && !_hadPendingApproval)
        {
            Announce(Loc["TmAgentMonitor_PendingApprovalAnnounced"]);
        }

        _hadPendingApproval = hasPendingApproval;

        _now = _clock.GetUtcNow();
    }

    /// <inheritdoc />
    protected override Task OnAfterRenderAsync(bool firstRender)
    {
        _hasRendered = true;

        if (NeedsTicking)
        {
            _ticker?.Start();
        }
        else
        {
            _ticker?.Stop();
        }

        return Task.CompletedTask;
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Folds or unfolds the strip — the same path the toggle button takes. A no-op when
    /// <see cref="AllowCollapse"/> is off or the state would not change.
    /// </summary>
    /// <param name="collapsed">Whether the strip should fold into its bar.</param>
    public async Task SetCollapsedAsync(bool collapsed)
    {
        if (!AllowCollapse || collapsed == _collapsed)
        {
            return;
        }

        _collapsed = collapsed;
        _lastCollapsed = collapsed;
        Announce(Loc[collapsed ? "TmAgentMonitor_Collapsed" : "TmAgentMonitor_Expanded"]);

        if (CollapsedChanged.HasDelegate)
        {
            await CollapsedChanged.InvokeAsync(collapsed);
        }

        await InvokeAsync(StateHasChanged);
    }

    // ── Rendering helpers ────────────────────────────────────────────────────

    private Task ToggleAsync() => SetCollapsedAsync(!_collapsed);

    private string ToggleLabel()
        => Loc[IsCollapsed ? "TmAgentMonitor_Expand" : "TmAgentMonitor_Collapse"];

    private string FormatCost(long microUnits)
        => CostFormatter is { } formatter ? formatter(microUnits) : AiCostFormatter.Format(microUnits, EffectiveCulture);

    private string StepTime(AgentMonitorStep step)
        => step.Elapsed(_now) is { } elapsed ? AiClockFormatter.FormatClock(elapsed) : "—";

    // ── Clock ────────────────────────────────────────────────────────────────

    private async Task OnTickAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await InvokeAsync(() =>
            {
                if (!_disposed)
                {
                    _now = _clock.GetUtcNow();
                    StateHasChanged();
                }
            });
        }
        catch (ObjectDisposedException)
        {
            // The renderer went away between the tick and the hop.
        }
        catch (InvalidOperationException)
        {
            // The renderer has already been torn down.
        }
    }

    /// <summary>
    /// Replaces the live region's text. Sticky, with the same invisible marker as the plan and the tree so
    /// two consecutive folds (or unfolds) are still two mutations.
    /// </summary>
    private void Announce(string message)
    {
        if (string.Equals(message, _announcement, StringComparison.Ordinal))
        {
            _announcementMarker = !_announcementMarker;
            _announcement = _announcementMarker ? message + "\u200B" : message;
            return;
        }

        _announcementMarker = false;
        _announcement = message;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ticker?.Dispose();
        _ticker = null;
    }
}
