using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.AI.Models;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// The LIVE half of <see cref="TmAgentGraph"/>: the incremental calls the host streams in, the pulse window they open,
/// and the paint gate that keeps a burst from becoming a render per event. Split out of the markup part so the surface
/// stays readable, exactly like <c>TmAiComposer.Internals</c> / <c>TmAiComposer.Interop</c> in this package.
/// </summary>
public partial class TmAgentGraph
{
    // ── The live contract ────────────────────────────────────────────────────

    /// <summary>
    /// Folds the host's incremental batch into the view. The shared stream owns ordering, de-duplication and hole
    /// detection; this method only applies what it accepted, ON TOP of the view it already has, forwards a hole to the
    /// host and pulses the edges the accepted calls travelled along.
    /// </summary>
    /// <returns><c>true</c> when at least one call was new.</returns>
    private bool ApplyEvents()
    {
        var snapshotChanged = !ReferenceEquals(Snapshot, _snapshotParameter)
            || !_hasSnapshot
            || (Snapshot is { } current && current.Sequence != _snapshotSequence);

        if (snapshotChanged)
        {
            _snapshotParameter = Snapshot;
            _snapshotSequence = Snapshot?.Sequence ?? 0;
            _hasSnapshot = true;
            _view = Snapshot ?? new AgentGraphSnapshot();
            _stream = new AgentEventStream<AgentGraphCall>(static call => call.Sequence, Snapshot?.Sequence);
            _throttle.Reset();
            _gapAnnounced = false;
            _lastReportedGap = null;
        }

        var applied = 0;
        if (Events is { Count: > 0 } && _stream is not null)
        {
            var accepted = _stream.Apply(Events);
            if (accepted.Count > 0)
            {
                _view = _view.Apply(accepted);
                applied = accepted.Count;

                foreach (var call in accepted)
                {
                    RegisterPulse(call.EdgeId);
                }
            }

            if (_stream.HasGap)
            {
                RaiseGap(_stream.LastSequence);
            }
        }

        return applied > 0;
    }

    /// <summary>
    /// Reports a stream hole once per contiguous run: the host reloads the snapshot, and the surface says so to the
    /// reader instead of silently painting stale data.
    /// </summary>
    private void RaiseGap(long? lastSequence)
    {
        var expected = lastSequence is { } sequence ? sequence + 1 : 0;

        if (!_gapAnnounced)
        {
            _gapAnnounced = true;
            _gapPaintPending = true;
            Announce(Loc["TmAgentGraph_Gap"]);
        }

        if (OnGapDetected.HasDelegate && _lastReportedGap != expected)
        {
            _lastReportedGap = expected;
            _ = InvokeAsync(() => OnGapDetected.InvokeAsync(expected));
        }
    }

    // ── Pulses ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Lights one edge up for <see cref="PulseDuration"/>, retiring the oldest pulse when the cap is exceeded so a
    /// fan-out burst cannot light up the whole picture.
    /// </summary>
    private void RegisterPulse(string? edgeId)
    {
        if (string.IsNullOrWhiteSpace(edgeId) || !HasEdge(edgeId))
        {
            // A pulse names an edge the drawing actually contains; a call for anything else has nothing to light up.
            return;
        }

        var expires = _clock.GetUtcNow() + PositiveDuration();
        var existing = _pulses.FindIndex(entry => string.Equals(entry.EdgeId, edgeId, StringComparison.Ordinal));
        if (existing >= 0)
        {
            _pulses.RemoveAt(existing);
        }

        _pulses.Add(new PulseEntry { EdgeId = edgeId!, Expires = expires });

        var cap = MaxPulses > 0 ? MaxPulses : 1;
        while (_pulses.Count > cap)
        {
            _pulses.RemoveAt(0);
        }

        SchedulePulseSweep();
    }

    private TimeSpan PositiveDuration()
        => PulseDuration > TimeSpan.Zero ? PulseDuration : TimeSpan.FromMilliseconds(600);

    private bool IsPulsing(string edgeId)
        => _pulses.Exists(entry => string.Equals(entry.EdgeId, edgeId, StringComparison.Ordinal));

    private bool HasEdge(string edgeId)
        => _view.Edges.Any(edge => string.Equals(edge.Id, edgeId, StringComparison.Ordinal));

    private void SchedulePulseSweep()
    {
        _pulseTimer?.Dispose();
        _pulseTimer = null;

        if (_disposed || _pulses.Count == 0)
        {
            return;
        }

        var now = _clock.GetUtcNow();
        var next = _pulses.Min(entry => entry.Expires) - now;
        if (next < TimeSpan.Zero)
        {
            next = TimeSpan.Zero;
        }

        _pulseTimer = _clock.CreateTimer(OnPulseSweep, null, next, Timeout.InfiniteTimeSpan);
    }

    private void OnPulseSweep(object? state)
    {
        if (_disposed)
        {
            return;
        }

        var now = _clock.GetUtcNow();
        var removed = _pulses.RemoveAll(entry => entry.Expires <= now);
        SchedulePulseSweep();

        if (removed == 0)
        {
            return;
        }

        _ = InvokeAsync(() =>
        {
            if (!_disposed)
            {
                PaintNow();
                StateHasChanged();
            }
        });
    }


    // ── Paint gate ───────────────────────────────────────────────────────────

    private RenderSignature CurrentSignature() => new(
        Snapshot,
        ViewMode,
        SelectedNodeId,
        Zoom,
        Title,
        ShowTitle,
        HeadingLevel,
        ShowZoom,
        NodePositions,
        MaxPulses,
        PulseDuration,
        DetailTemplate,
        Class,
        Culture);

    private bool ParametersChangedExceptEvents()
    {
        var signature = CurrentSignature();
        if (_signature is { } previous && previous.Equals(signature))
        {
            return false;
        }

        _signature = signature;
        return true;
    }

    private void Paint()
    {
        CancelTrailingPaint();
        PaintNow();
        _ = InvokeAsync(StateHasChanged);
    }

    private void PaintNow()
    {
        _throttle.Reset();
        _throttle.ShouldRender();
        _paintNow = true;
    }

    private void GatePaint()
    {
        if (_throttle.ShouldRender())
        {
            CancelTrailingPaint();
            _paintNow = true;
            return;
        }

        ScheduleTrailingPaint();
    }

    private void ScheduleTrailingPaint()
    {
        if (_disposed)
        {
            return;
        }

        var delay = _throttle.DelayUntilNextRender();
        if (delay <= TimeSpan.Zero)
        {
            _paintNow = true;
            return;
        }

        CancelTrailingPaint();
        _trailingPaint = _clock.CreateTimer(OnTrailingPaint, null, delay, Timeout.InfiniteTimeSpan);
    }

    private void CancelTrailingPaint()
    {
        _trailingPaint?.Dispose();
        _trailingPaint = null;
    }

    private void OnTrailingPaint(object? state)
    {
        CancelTrailingPaint();
        if (_disposed)
        {
            return;
        }

        _throttle.Reset();
        _ = InvokeAsync(() =>
        {
            if (_disposed)
            {
                return;
            }

            _paintNow = true;
            Recompute();
            StateHasChanged();
        });
    }

    /// <summary>
    /// The value signature of every parameter that changes what the surface paints, so an event batch is the ONE thing
    /// the paint gate may hold back while a snapshot, a selection or a zoom change always paints immediately.
    /// </summary>
    private readonly record struct RenderSignature(
        AgentGraphSnapshot? Snapshot,
        AgentGraphViewMode ViewMode,
        string? SelectedNodeId,
        double Zoom,
        string? Title,
        bool ShowTitle,
        int HeadingLevel,
        bool ShowZoom,
        IReadOnlyDictionary<string, AgentGraphPosition>? NodePositions,
        int MaxPulses,
        TimeSpan PulseDuration,
        RenderFragment<AgentGraphNode>? DetailTemplate,
        string? Class,
        CultureInfo? Culture);

    /// <summary>One pulse currently painted on one edge, with the moment it stops.</summary>
    private sealed class PulseEntry
    {
        /// <summary>The edge the pulse is on.</summary>
        public string EdgeId { get; init; } = string.Empty;

        /// <summary>The moment the pulse is retired.</summary>
        public DateTimeOffset Expires { get; init; }
    }
}
