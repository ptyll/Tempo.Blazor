using System.Globalization;
using System.Threading;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Demo.SharedUI.Pages;
using Tempo.Blazor.Demo.SharedUI.Services;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Demo.SharedUI.Components;

/// <summary>
/// The LIVE half of <c>/ai-agent-visualizations</c>: the host-side simulator plus the three surfaces whose data it
/// changes on every paint (the lanes, the graph and the fan-out), in their own component.
/// <para>
/// The split exists for one measured reason. A paint of the whole page — thirteen surfaces, two hundred rows and the
/// page's own content — costs the WASM renderer roughly 200 ms, so a paint per event made the run apply only 44 of the
/// 100 events it was asked for in ten seconds. With the live surfaces alone the paint is cheap enough that the
/// simulator holds its ten events per second for the whole run, and the ten decision surfaces below never re-render
/// for a tick they do not care about.
/// </para>
/// </summary>
public partial class AiAgentRunLiveSurfaces
{
    /// <summary>
    /// The paint gate: at most one paint per 200 ms. The STATE keeps absorbing every event the timer applies; only the
    /// paint is folded, so a snapshot read at any moment is complete while the renderer stays responsive.
    /// </summary>
    private static readonly TimeSpan PaintInterval = TimeSpan.FromMilliseconds(200);

    private readonly HashSet<string> _textViews = new(StringComparer.Ordinal);
    private readonly IReadOnlyList<AgentRef> _agents = AiAgentVisualizationsSamples.Agents();

    private AiAgentRunSimulator _simulator = default!;
    private AgentRenderThrottle _paint = default!;
    private ITimer? _trailingPaint;
    private bool _disposed;

    private AiAgentRunState _state = new();
    private AiAgentRunState? _pendingState;
    private IReadOnlyList<AgentGraphCall> _graphCalls = [];
    private AgentGraphSnapshot _graph = new();

    private AgentLanesViewMode _lanesView = AgentLanesViewMode.Timeline;
    private AgentGraphViewMode _graphView = AgentGraphViewMode.Graph;
    private string? _laneBlockId;
    private string? _graphNodeId;

    private FanOutFilter _fanOutFilter = FanOutFilter.All;
    private string _announcement = string.Empty;

    [Inject]
    private ITmLocalizer Loc { get; set; } = default!;

    [Inject]
    private TimeProvider Clock { get; set; } = default!;

    /// <summary>The snapshot the live surfaces render.</summary>
    private AiAgentRunState State => _state;

    /// <summary>Whether the generator is running.</summary>
    private bool IsRunning => _simulator.IsRunning;

    /// <summary>The speed the generator runs at.</summary>
    private double Speed => _simulator.Speed;

    /// <summary>The effective event rate.</summary>
    private int EventsPerSecond => _simulator.EventsPerSecond;

    /// <summary>The speeds the page offers, as a segmented control.</summary>
    private IReadOnlyList<SegmentedOption<double>> SpeedOptions =>
    [
        .. AiAgentRunSimulator.Speeds.Select(speed => new SegmentedOption<double>
        {
            Label = Loc["TmAiAgentVizDemo_SpeedOption", speed.ToString("0.#", CultureInfo.CurrentCulture)],
            Value = speed,
        }),
    ];

    /// <summary>The state word of the generator, as the read-out and the live region say it.</summary>
    private string StateText => IsRunning ? Loc["TmAiAgentVizDemo_StateRunning"] : Loc["TmAiAgentVizDemo_StatePaused"];

    /// <summary>The last applied event as one sentence, or an honest "nothing yet".</summary>
    private string LastEventText => _state.LastEvent is { } change
        ? Loc["TmAiAgentVizDemo_EventLine",
            change.Sequence.ToString(CultureInfo.CurrentCulture),
            EventKindText(change.Kind),
            AgentName(change.AgentId)]
        : Loc["TmAiAgentVizDemo_NoEventYet"];

    /// <summary>The run as lanes: the reduced blocks and handoffs of the simulator's snapshot.</summary>
    private AgentLanesSnapshot LanesSnapshot => new()
    {
        Sequence = _state.Sequence,
        Agents = _agents,
        Blocks = _state.Blocks,
        Handoffs = _state.Handoffs,
    };

    /// <summary>The fan-out units, straight from the simulator.</summary>
    private IReadOnlyList<FanOutTask> Tasks => _state.Tasks;

    /// <summary>The lanes' selection sentence: the block the reader picked, or an honest "nothing selected".</summary>
    private string LanesSelectedText => _laneBlockId is { } blockId
        ? Loc["TmAiAgentsDemo_LanesSelected",
            _state.Blocks.FirstOrDefault(block => string.Equals(block.Id, blockId, StringComparison.Ordinal))?.Label
            ?? blockId]
        : Loc["TmAiAgentsDemo_LanesNoSelection"];

    /// <summary>The graph's selection sentence.</summary>
    private string GraphSelectedText => _graphNodeId is { } nodeId
        ? Loc["TmAiAgentsDemo_GraphSelected", GraphNodeName(nodeId)]
        : Loc["TmAiAgentsDemo_GraphNoSelection"];

    /// <summary>Starts and pauses the generator. The focus stays on the button the reader pressed.</summary>
    private void ToggleRun()
    {
        _simulator.Toggle();
        Announce(StateText);
    }

    /// <summary>Sets the speed multiplier and says so in the page's one live region.</summary>
    private void SetSpeed(double speed)
    {
        _simulator.SetSpeed(speed);
        Announce(Loc["TmAiAgentVizDemo_SpeedAnnounce",
            speed.ToString("0.#", CultureInfo.CurrentCulture),
            EventsPerSecond.ToString(CultureInfo.CurrentCulture)]);
    }

    /// <summary>Clears the run back to its opening snapshot, dropping the calls the graph was fed.</summary>
    private void ResetRun()
    {
        _simulator.Reset();
        _state = _simulator.State;
        _pendingState = null;
        _graphCalls = [];
        _graph = AiAgentVisualizationsSamples.GraphSnapshot(Clock.GetUtcNow());
        _laneBlockId = null;
        _graphNodeId = null;
        Announce(Loc["TmAiAgentVizDemo_ResetAnnounce"]);
    }

    /// <summary>
    /// The generator's tick: the newest snapshot is kept and the paint is gated. A tick inside the current window is
    /// folded into the trailing paint instead of being dropped, so the surface always ends up at the newest state.
    /// <para>
    /// The whole body runs on the Blazor dispatcher, because the simulator raises <c>Ticked</c> from its timer thread
    /// on Blazor Server: the throttle and the trailing timer are dispatcher-owned state, and <see cref="Paint"/> runs
    /// there too, so a tick and a paint can never fight over <c>_pendingState</c> or orphan a timer.
    /// </para>
    /// </summary>
    private void OnTicked(AiAgentRunState state, IReadOnlyList<AiAgentRunEvent> batch) => _ = InvokeAsync(() =>
    {
        if (_disposed)
        {
            return;
        }

        _pendingState = state;

        if (_paint.ShouldRender())
        {
            Paint();
            return;
        }

        _trailingPaint ??= Clock.CreateTimer(
            _ => OnTrailingPaint(),
            null,
            _paint.DelayUntilNextRender(),
            Timeout.InfiniteTimeSpan);
    });

    /// <summary>
    /// The trailing paint's timer callback: it hops to the dispatcher instead of painting on the timer thread, and it
    /// never throws when the circuit is already gone — an exception in a timer callback would take the Server down.
    /// </summary>
    private void OnTrailingPaint()
    {
        try
        {
            _ = InvokeAsync(Paint);
        }
        catch (Exception exception) when (exception is ObjectDisposedException or InvalidOperationException)
        {
            // The renderer (or its dispatcher) is gone: there is nothing left to paint and nothing left to tell.
        }
    }

    /// <summary>Paints the newest snapshot: one paint per window no matter how many events landed in it.</summary>
    private void Paint()
    {
        if (_disposed)
        {
            return;
        }

        _trailingPaint?.Dispose();
        _trailingPaint = null;

        if (_pendingState is { } state)
        {
            _state = state;
            _pendingState = null;
        }

        _graphCalls = _state.Calls;
        StateHasChanged();
    }

    /// <summary>
    /// The graph reported a hole in its own call log (a reset restarted the log): the page reloads the snapshot the
    /// calls re-base on, which is exactly the repair the shared contract asks the host to perform.
    /// </summary>
    private void OnGraphGap(long expected)
    {
        _graph = AiAgentVisualizationsSamples.GraphSnapshot(Clock.GetUtcNow(), _simulator.CallSequence);
        _graphCalls = [];
    }

    /// <summary>Re-queues one failed fan-out unit: the simulator owns the queue, so the retry lands there.</summary>
    private void RetryTask(FanOutTask task)
    {
        _simulator.RetryTask(task.Id);
        _state = _simulator.State;
    }

    /// <summary>Whether the fan-out shows its table presentation instead of its tiles.</summary>
    private bool IsTextView(string key) => _textViews.Contains(key);

    /// <summary>Switches the fan-out between its tiled and its table presentation.</summary>
    private void ToggleTextView(string key)
    {
        if (!_textViews.Remove(key))
        {
            _textViews.Add(key);
        }
    }

    /// <summary>The label of the fan-out toggle: it NAMES the presentation a press shows.</summary>
    private string TextViewLabel(string key)
        => IsTextView(key) ? Loc["TmAiAgentVizDemo_ShowVisual"] : Loc["TmAiAgentVizDemo_ShowTable"];

    /// <summary>The state word of a fan-out unit.</summary>
    private string StatusText(AgentActivityStatus status) => status switch
    {
        AgentActivityStatus.Queued => Loc["TmAgentStatus_Queued"],
        AgentActivityStatus.Running => Loc["TmAgentStatus_Running"],
        AgentActivityStatus.WaitingForApproval => Loc["TmAgentStatus_WaitingForApproval"],
        AgentActivityStatus.Succeeded => Loc["TmAgentStatus_Succeeded"],
        AgentActivityStatus.Failed => Loc["TmAgentStatus_Failed"],
        AgentActivityStatus.Cancelled => Loc["TmAgentStatus_Cancelled"],
        _ => Loc["TmAgentStatus_Idle"],
    };

    /// <summary>The word of one change kind of the change log.</summary>
    private string EventKindText(AiAgentRunEventKind kind) => kind switch
    {
        AiAgentRunEventKind.BlockStarted => Loc["TmAiAgentVizDemo_EventBlockStarted"],
        AiAgentRunEventKind.ToolCalled => Loc["TmAiAgentVizDemo_EventToolCalled"],
        AiAgentRunEventKind.BlockCompleted => Loc["TmAiAgentVizDemo_EventBlockCompleted"],
        _ => Loc["TmAiAgentVizDemo_EventBlockRetired"],
    };

    /// <summary>A graph node's display name: its label, its agent's name, or its id.</summary>
    private string GraphNodeName(string nodeId)
    {
        var node = _graph.Nodes.FirstOrDefault(candidate => string.Equals(candidate.Id, nodeId, StringComparison.Ordinal));
        if (node is null)
        {
            return nodeId;
        }

        return node.HasLabel
            ? node.Label
            : node.Agent is { HasName: true } agent ? agent.Name : node.Id;
    }

    /// <summary>An agent's display name, or its id when the run never named it.</summary>
    private string AgentName(string agentId)
        => _agents.FirstOrDefault(agent => string.Equals(agent.Id, agentId, StringComparison.Ordinal))?.Name ?? agentId;

    /// <summary>
    /// Says one sentence about the reader's own action in the live region. The simulated events are never announced:
    /// the visible counter carries them, and reading the whole run aloud would drown every other announcement.
    /// </summary>
    /// <param name="message">The localized sentence.</param>
    private void Announce(string message) => _announcement = message;

    /// <inheritdoc />
    public void Dispose()
    {
        // First: a tick or a trailing paint that is already in flight must not touch this component again.
        _disposed = true;
        _simulator.Ticked -= OnTicked;
        _simulator.Dispose();
        _trailingPaint?.Dispose();
        _trailingPaint = null;
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        _paint = new AgentRenderThrottle(Clock, PaintInterval);
        _simulator = new AiAgentRunSimulator(Clock);
        _simulator.Ticked += OnTicked;
        _state = _simulator.State;
        _graph = AiAgentVisualizationsSamples.GraphSnapshot(Clock.GetUtcNow());
    }
}
