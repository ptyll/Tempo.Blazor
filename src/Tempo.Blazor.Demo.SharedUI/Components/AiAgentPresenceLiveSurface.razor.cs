using System.Threading;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Demo.SharedUI.Services;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Demo.SharedUI.Components;

/// <summary>
/// The LIVE presence section of <c>/ai-agent-visualizations</c>: it owns the marker stream, so a moving cursor never
/// repaints the page or another visualization.
/// <para>
/// The stream comes from this component's OWN <see cref="AiAgentRunSimulator"/>. The page's simulator drives the lanes,
/// the graph and the fan-out; a presence cursor does not depend on any of them, and a shared instance would force the
/// page to host the timer again — exactly what phase 9 moved out of the page for a measured render budget. The run
/// starts PAUSED (a reference screenshot must be reproducible) and the paint is gated by
/// <see cref="AgentRenderThrottle"/>, so ten events per second cost one paint per window.
/// </para>
/// <para>
/// The one announcement channel belongs to the page, and a live region that read a simulated cursor around would drown
/// it, so the overlay renders with <c>AnnounceChanges="false"</c>. The roster is the same presence as text, and the
/// table presentation re-renders the same snapshot one row per agent.
/// </para>
/// </summary>
public partial class AiAgentPresenceLiveSurface
{
    /// <summary>The paint gate: at most one paint per 200 ms, the same window the page's own live surfaces use.</summary>
    private static readonly TimeSpan PaintInterval = TimeSpan.FromMilliseconds(200);

    private AiAgentRunSimulator _simulator = default!;
    private AgentRenderThrottle _paint = default!;
    private ITimer? _trailingPaint;
    private bool _disposed;

    private AiAgentRunState _state = new();
    private AiAgentRunState? _pendingState;
    private bool _showTable;

    [Inject]
    private ITmLocalizer Loc { get; set; } = default!;

    [Inject]
    private TimeProvider Clock { get; set; } = default!;

    /// <summary>The cursors of the current snapshot, in the simulator's order.</summary>
    private IReadOnlyList<PresenceMarker> Markers => _state.Markers;

    /// <summary>Whether the marker stream is running.</summary>
    private bool IsRunning => _simulator.IsRunning;

    /// <summary>Whether the table presentation is the one on screen.</summary>
    private bool ShowTable => _showTable;

    /// <summary>The label of the view toggle: it NAMES the presentation a press shows.</summary>
    private string TextViewLabel
        => _showTable ? Loc["TmAiAgentVizDemo_ShowVisual"] : Loc["TmAiAgentVizDemo_ShowTable"];

    /// <summary>The word of a presence activity.</summary>
    private string ActivityText(PresenceActivity activity) => activity switch
    {
        PresenceActivity.Typing => Loc["TmAgentPresenceOverlay_ActivityTyping"],
        PresenceActivity.Editing => Loc["TmAgentPresenceOverlay_ActivityEditing"],
        PresenceActivity.Commenting => Loc["TmAgentPresenceOverlay_ActivityCommenting"],
        _ => Loc["TmAgentPresenceOverlay_ActivityViewing"],
    };

    /// <summary>Switches the section between its cursor layer and the table alternative.</summary>
    private void ToggleTextView() => _showTable = !_showTable;

    /// <summary>Starts and pauses this section's marker stream. Only THIS subtree repaints per tick.</summary>
    private void ToggleRun() => _simulator.Toggle();

    /// <summary>
    /// The generator's tick: the newest snapshot is kept and the paint is gated. A tick inside the current window is
    /// folded into the trailing paint instead of being dropped, so the section always ends up at the newest state.
    /// The body runs on the Blazor dispatcher, because the simulator raises <c>Ticked</c> from its timer thread on
    /// Blazor Server.
    /// </summary>
    /// <param name="state">The newest snapshot.</param>
    /// <param name="batch">The events the tick applied (unused: the counters are the page's).</param>
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
    /// never throws when the circuit is already gone.
    /// </summary>
    private void OnTrailingPaint()
    {
        try
        {
            _ = InvokeAsync(Paint);
        }
        catch (Exception exception) when (exception is ObjectDisposedException or InvalidOperationException)
        {
            // The renderer (or its dispatcher) is gone: there is nothing left to paint.
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

        StateHasChanged();
    }

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
    }
}
