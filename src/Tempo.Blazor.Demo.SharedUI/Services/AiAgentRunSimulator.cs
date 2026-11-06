using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Demo.SharedUI.Pages;

namespace Tempo.Blazor.Demo.SharedUI.Services;

/// <summary>What one generated change does to the simulated run.</summary>
public enum AiAgentRunEventKind
{
    /// <summary>A block of work starts on an agent's lane.</summary>
    BlockStarted,

    /// <summary>An agent calls a tool along one edge of the graph.</summary>
    ToolCalled,

    /// <summary>The running block finishes.</summary>
    BlockCompleted,

    /// <summary>The oldest finished block leaves the lane, so the surface stays bounded.</summary>
    BlockRetired,
}

/// <summary>
/// One applied change of the simulated run, stamped with the monotonic <see cref="Sequence"/> every live agent
/// surface of <c>Tempo.Blazor.AI</c> expects its increments to carry.
/// </summary>
public sealed record AiAgentRunEvent
{
    /// <summary>Creates an event.</summary>
    /// <param name="sequence">The event's position in the run's change log; it grows by ONE per applied change.</param>
    public AiAgentRunEvent(long sequence) => Sequence = sequence;

    /// <summary>The event's position in the run's change log; strictly increasing per applied change.</summary>
    public long Sequence { get; init; }

    /// <summary>What the event does.</summary>
    public AiAgentRunEventKind Kind { get; init; }

    /// <summary>The agent the change belongs to.</summary>
    public string AgentId { get; init; } = string.Empty;

    /// <summary>The block the change addresses, when it addresses one.</summary>
    public string? BlockId { get; init; }

    /// <summary>The graph edge a tool call travelled along, when the change is a call.</summary>
    public string? EdgeId { get; init; }

    /// <summary>The block's label, when the change opens one.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>When the change happened, on the simulator's clock.</summary>
    public DateTimeOffset At { get; init; }
}

/// <summary>
/// The state of the simulated run after a batch of events: everything the demo page hands to the agent surfaces as a
/// SNAPSHOT. Every list is bounded (see the constants on <see cref="AiAgentRunSimulator"/>), so a two-minute run at
/// ten events per second does not grow the heap.
/// </summary>
public sealed record AiAgentRunState
{
    /// <summary>The sequence of the last applied change; zero for the seeded snapshot.</summary>
    public long Sequence { get; init; }

    /// <summary>How many changes this run has applied since the last reset.</summary>
    public int EventCount { get; init; }

    /// <summary>The lane blocks, oldest first — at most <see cref="AiAgentRunSimulator.MaxBlocks"/> of them.</summary>
    public IReadOnlyList<AgentLaneBlock> Blocks { get; init; } = [];

    /// <summary>The handoffs between the lanes, oldest first — at most <see cref="AiAgentRunSimulator.MaxHandoffs"/>.</summary>
    public IReadOnlyList<AgentHandoffLink> Handoffs { get; init; } = [];

    /// <summary>The forty units of fan-out work, in queue order.</summary>
    public IReadOnlyList<FanOutTask> Tasks { get; init; } = [];

    /// <summary>The presence cursors of the three collaborating agents.</summary>
    public IReadOnlyList<PresenceMarker> Markers { get; init; } = [];

    /// <summary>The graph calls of the LAST tick; empty on a tick that called nothing.</summary>
    public IReadOnlyList<AgentGraphCall> Calls { get; init; } = [];

    /// <summary>The change the last tick applied, or null before the first one.</summary>
    public AiAgentRunEvent? LastEvent { get; init; }

    /// <summary>How many fan-out units are finished.</summary>
    public int FinishedTasks => Tasks.Count(task => task.IsFinished);
}

/// <summary>
/// The multi-agent run simulator of the <c>/ai-agent-visualizations</c> demo page: a <see cref="TimeProvider"/>-driven
/// generator that stamps one monotonic sequence per applied change and folds it into a bounded
/// <see cref="AiAgentRunState"/>.
/// <para>
/// The simulator is a HOST. It is exactly what an application would write around a real orchestration backend: it owns
/// the transport (here a timer instead of a hub), the change log and the reducer, while every component on the page
/// only renders the snapshot it is handed. Nothing here reaches into the library, and no component knows this class
/// exists.
/// </para>
/// <para>
/// The change log is a four-beat cycle per agent: a block OPENS, a tool is CALLED, that block COMPLETES and a
/// housekeeping beat RETIRES the oldest finished block once the lane is over its cap. Every completion also advances
/// the fan-out queue by one unit, which starts the next queued unit, finishes the running one, and — every third
/// cycle — retries a failed unit with an incremented attempt. The lists stay bounded: the lane keeps at most
/// <see cref="MaxBlocks"/> blocks, the handoff row at most <see cref="MaxHandoffs"/> links, and the queue LOOPS over
/// the same forty units instead of piling new ones up.
/// </para>
/// <para>
/// THREADING: the timer callback runs on a thread-pool thread while Play, Pause, the speed, Reset and Retry arrive
/// from the circuit's UI thread on Blazor Server. Every field of the run is therefore reached through ONE gate: the
/// reducer holds it, the readers hold it, and the <see cref="Ticked"/> event is raised after it is released, so a
/// subscriber can call back into the simulator without deadlocking the timer.
/// </para>
/// </summary>
public sealed class AiAgentRunSimulator : IDisposable
{
    /// <summary>The event rate at 1× speed: ten changes per second.</summary>
    public const int BaseEventsPerSecond = 10;

    /// <summary>The most lane blocks a snapshot carries; the newest are kept.</summary>
    public const int MaxBlocks = 12;

    /// <summary>The most handoff links a snapshot carries; the newest are kept.</summary>
    public const int MaxHandoffs = 8;

    /// <summary>The fan-out queue size — the number of units the run loops over.</summary>
    public const int QueueSize = 40;

    /// <summary>The speed multipliers the page offers.</summary>
    public static readonly IReadOnlyList<double> Speeds = [0.5d, 1d, 2d];

    /// <summary>How many events the four-beat cycle has.</summary>
    private const int BeatsPerCycle = 4;

    /// <summary>
    /// Guards every mutable field of the run. On Blazor Server the timer callback runs on a thread-pool thread while
    /// Reset, Retry and the speed arrive from the circuit's UI thread, so the reducer and the readers must never touch
    /// the lists, the queue or the snapshot at the same time. The gate is held ONLY for the reducer: the change event
    /// is raised after it is released.
    /// </summary>
    private readonly object _gate = new();

    private readonly TimeProvider _clock;
    private readonly IReadOnlyList<AgentRef> _agents = AiAgentVisualizationsSamples.Agents();
    private readonly IReadOnlyList<string> _labels = AiAgentVisualizationsSamples.BlockLabels();
    private readonly IReadOnlyList<string> _edges = AiAgentVisualizationsSamples.CallEdges();
    private readonly IReadOnlyList<string> _anchors = AiAgentVisualizationsSamples.DocumentAnchors();

    private List<AgentLaneBlock> _blocks = [];
    private List<AgentHandoffLink> _handoffs = [];
    private List<FanOutTask> _tasks = [];
    private IReadOnlyList<PresenceMarker> _markers = [];
    private IReadOnlyList<AgentGraphCall> _calls = [];

    private ITimer? _timer;
    private long _sequence;
    private long _callSequence;
    private int _eventCount;
    private int _beat;
    private int _cycle;
    private Queue<string> _openBlocks = new();
    private AiAgentRunEvent? _lastEvent;
    private AiAgentRunState _state = new();
    private Exception? _lastTickError;
    private double _speed = 1d;
    private bool _disposed;

    /// <summary>Creates a simulator on a clock, seeded with the page's opening snapshot and paused.</summary>
    /// <param name="timeProvider">The clock the timer and every timestamp come from; null uses the system clock.</param>
    public AiAgentRunSimulator(TimeProvider? timeProvider = null)
    {
        _clock = timeProvider ?? TimeProvider.System;
        Seed();
    }

    /// <summary>Raised after every applied batch, with the batch and the new state.</summary>
    public event Action<AiAgentRunState, IReadOnlyList<AiAgentRunEvent>>? Ticked;

    /// <summary>Whether the generator is running.</summary>
    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _timer is not null;
            }
        }
    }

    /// <summary>The speed multiplier: <c>0.5</c>, <c>1</c> or <c>2</c>.</summary>
    public double Speed
    {
        get
        {
            lock (_gate)
            {
                return _speed;
            }
        }
    }

    /// <summary>The effective event rate: <see cref="BaseEventsPerSecond"/> times <see cref="Speed"/>.</summary>
    public int EventsPerSecond
    {
        get
        {
            lock (_gate)
            {
                return EventsPerSecondCore;
            }
        }
    }

    /// <summary>The number of graph calls reported so far; the graph's own call log re-bases on it.</summary>
    public long CallSequence
    {
        get
        {
            lock (_gate)
            {
                return _callSequence;
            }
        }
    }

    /// <summary>The agent ids with an OPEN block — the agents working right now.</summary>
    public IReadOnlyList<string> RunningAgentIds
    {
        get
        {
            lock (_gate)
            {
                return
                [
                    .. _blocks.Where(block => block.End is null)
                        .Select(block => block.AgentId)
                        .Distinct(StringComparer.Ordinal),
                ];
            }
        }
    }

    /// <summary>The current snapshot. Every read returns the same immutable instance.</summary>
    public AiAgentRunState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    /// <summary>
    /// The last exception a TIMER tick caught. A direct <see cref="Step"/> call still throws to its caller; the timer
    /// callback swallows the exception (a callback that throws takes the whole Server host down) and records it here.
    /// </summary>
    public Exception? LastTickError
    {
        get
        {
            lock (_gate)
            {
                return _lastTickError;
            }
        }
    }

    /// <summary>
    /// Starts the generator. A running simulator is left alone, so a second call does not stack timers.
    /// </summary>
    public void Play()
    {
        lock (_gate)
        {
            if (_disposed || _timer is not null)
            {
                return;
            }

            var period = TimeSpan.FromMilliseconds(1000d / EventsPerSecondCore);
            _timer = _clock.CreateTimer(static state => ((AiAgentRunSimulator)state!).OnTick(), this, period, period);
        }
    }

    /// <summary>Stops the generator; the state stays where it is.</summary>
    public void Pause()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    /// <summary>Starts a paused generator and pauses a running one.</summary>
    public void Toggle()
    {
        lock (_gate)
        {
            if (_timer is not null)
            {
                Pause();
            }
            else
            {
                Play();
            }
        }
    }

    /// <summary>
    /// Sets the speed multiplier and, while running, rebuilds the timer on the new period so the change is audible
    /// immediately instead of after the next tick.
    /// </summary>
    /// <param name="speed">The multiplier; must be greater than zero.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="speed"/> is not greater than zero.</exception>
    public void SetSpeed(double speed)
    {
        if (speed <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(speed), speed, "A speed multiplier must be greater than zero — a zero rate would look like a " +
                "paused run with no way to tell them apart.");
        }

        lock (_gate)
        {
            _speed = speed;

            if (_timer is not null)
            {
                Pause();
                Play();
            }
        }
    }

    /// <summary>
    /// Applies exactly one event and returns the batch. This is the deterministic tick: the timer calls it, and a
    /// test drives it directly instead of advancing a clock.
    /// </summary>
    /// <returns>The batch of events applied by this tick.</returns>
    public IReadOnlyList<AiAgentRunEvent> Step()
    {
        AiAgentRunState snapshot;
        IReadOnlyList<AiAgentRunEvent> batch;

        lock (_gate)
        {
            var change = NextEventCore();
            _calls = [];
            Apply(change);
            batch = [change];
            snapshot = _state;
        }

        // Raised OUTSIDE the gate: a subscriber paints on the dispatcher and may call back into the simulator (a
        // reset, a speed change), and a paint that blocked the timer thread would stall the run.
        Ticked?.Invoke(snapshot, batch);
        return batch;
    }

    /// <summary>Builds the next change of the four-beat cycle without applying it.</summary>
    /// <returns>The change the next tick would apply.</returns>
    public AiAgentRunEvent NextEvent()
    {
        lock (_gate)
        {
            return NextEventCore();
        }
    }    /// <summary>
    /// Re-queues one fan-out unit, which is what the page's Retry does: the unit keeps the attempts it already spent,
    /// its error is cleared and it waits for the next free worker.
    /// </summary>
    /// <param name="taskId">The unit to re-queue; an unknown id is ignored.</param>
    public void RetryTask(string taskId)
    {
        lock (_gate)
        {
            var index = _tasks.FindIndex(task => string.Equals(task.Id, taskId, StringComparison.Ordinal));
            if (index < 0)
            {
                return;
            }

            _tasks[index] = _tasks[index] with
            {
                Status = AgentActivityStatus.Queued,
                Attempt = _tasks[index].EffectiveAttempt + 1,
                StartedAt = null,
                EstimatedEndAt = null,
                Error = null,
                Worker = null,
            };

            Publish();
        }
    }

    /// <summary>Clears the run back to its seeded snapshot, pauses the generator and restarts the change log.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            Pause();
            _speed = 1d;
            Seed();
        }
    }

    /// <summary>Stops the timer and releases it.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Pause();
        }
    }

    /// <summary>The effective event rate for the current speed; the caller holds the gate.</summary>
    private int EventsPerSecondCore => (int)Math.Round(BaseEventsPerSecond * _speed, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The timer callback: one event per period, unless the reader paused or disposed the run while the tick was in
    /// flight. A callback must never throw — an unhandled exception here is an unhandled thread-pool exception and
    /// takes the Server host down — so a failing subscriber is recorded on <see cref="LastTickError"/> instead.
    /// </summary>
    private void OnTick()
    {
        try
        {
            lock (_gate)
            {
                if (_disposed || _timer is null)
                {
                    return;
                }
            }

            Step();
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                _lastTickError = exception;
            }
        }
    }

    /// <summary>Builds the next change of the four-beat cycle; the caller holds the gate.</summary>
    private AiAgentRunEvent NextEventCore()
    {
        var now = _clock.GetUtcNow();
        var agent = _agents[_cycle % _agents.Count];
        var sequence = _sequence + 1;

        return _beat switch
        {
            0 => new AiAgentRunEvent(sequence)
            {
                Kind = AiAgentRunEventKind.BlockStarted,
                AgentId = agent.Id,
                BlockId = $"blk-{sequence}",
                Label = _labels[_cycle % _labels.Count],
                At = now,
            },
            1 => new AiAgentRunEvent(sequence)
            {
                Kind = AiAgentRunEventKind.ToolCalled,
                AgentId = agent.Id,
                EdgeId = _edges[_cycle % _edges.Count],
                At = now,
            },
            2 => new AiAgentRunEvent(sequence)
            {
                Kind = AiAgentRunEventKind.BlockCompleted,
                AgentId = agent.Id,
                // The block that finishes is the OLDEST pending one, so a block stays open across one cycle and the
                // lane never paints an agent as idle while it works.
                BlockId = _openBlocks.Count >= 2 ? _openBlocks.Peek() : null,
                At = now,
            },
            _ => HousekeepingEvent(sequence, now, agent),
        };
    }

    /// <summary>
    /// The housekeeping beat: retire the oldest finished block when the lane has reached its cap, otherwise report
    /// one more tool call. The lane therefore oscillates around its cap instead of growing. The caller holds the gate.
    /// </summary>
    private AiAgentRunEvent HousekeepingEvent(long sequence, DateTimeOffset now, AgentRef agent)
    {
        var finished = _blocks.FirstOrDefault(block => block.End is not null);

        if (_blocks.Count >= MaxBlocks && finished is not null)
        {
            return new AiAgentRunEvent(sequence)
            {
                Kind = AiAgentRunEventKind.BlockRetired,
                AgentId = finished.AgentId,
                BlockId = finished.Id,
                Label = finished.Label,
                At = now,
            };
        }

        return new AiAgentRunEvent(sequence)
        {
            Kind = AiAgentRunEventKind.ToolCalled,
            AgentId = agent.Id,
            EdgeId = _edges[(_cycle + 1) % _edges.Count],
            At = now,
        };
    }

    /// <summary>Folds one change into the state, advances the cycle and publishes the new snapshot. The caller
    /// holds the gate.</summary>
    private void Apply(AiAgentRunEvent change)
    {
        _sequence = change.Sequence;
        _eventCount++;
        _lastEvent = change;

        switch (change.Kind)
        {
            case AiAgentRunEventKind.BlockStarted:
                _blocks.Add(new AgentLaneBlock(
                    change.BlockId ?? $"blk-{change.Sequence}", change.AgentId, change.At, change.Label)
                {
                    Kind = (change.Sequence % 3) switch
                    {
                        0 => AgentLaneBlockKind.Tool,
                        1 => AgentLaneBlockKind.Message,
                        _ => AgentLaneBlockKind.Task,
                    },
                    Status = AgentActivityStatus.Running,
                });
                _openBlocks.Enqueue(change.BlockId!);
                Trim();
                break;

            case AiAgentRunEventKind.ToolCalled:
                if (!string.IsNullOrWhiteSpace(change.EdgeId))
                {
                    _calls = [new AgentGraphCall(++_callSequence) { EdgeId = change.EdgeId, At = change.At }];
                }

                break;

            case AiAgentRunEventKind.BlockCompleted:
                CompleteBlock(change);
                AdvanceQueue(change.At);
                AddHandoff(change.At);
                break;

            case AiAgentRunEventKind.BlockRetired:
                _blocks.RemoveAll(block => block.Id == change.BlockId);
                break;

            default:
                break;
        }

        _beat = (_beat + 1) % BeatsPerCycle;
        if (_beat == 0)
        {
            _cycle++;
        }

        RefreshPresence(change.At);
        Publish();
    }

    private void CompleteBlock(AiAgentRunEvent change)
    {
        if (change.BlockId is null || _openBlocks.Count < 2)
        {
            return;
        }

        var id = _openBlocks.Dequeue();
        var index = _blocks.FindIndex(block => block.Id == id);
        if (index < 0)
        {
            return;
        }

        _blocks[index] = _blocks[index] with
        {
            End = change.At,
            Status = AgentActivityStatus.Succeeded,
        };
    }

    /// <summary>
    /// Advances the fan-out queue by one unit: finish the running unit, otherwise start the next queued one, otherwise
    /// retry a failed one with an incremented attempt, otherwise LOOP the finished units back into the queue. The
    /// queue therefore stays forty units long for as long as the page runs.
    /// </summary>
    private void AdvanceQueue(DateTimeOffset now)
    {
        var running = _tasks.FindIndex(task => task.Status == AgentActivityStatus.Running);
        if (running >= 0)
        {
            _tasks[running] = _tasks[running] with
            {
                Status = AgentActivityStatus.Succeeded,
                EstimatedEndAt = null,
                CostMicroUnits = 42_000 + (running * 1_500),
            };
            return;
        }

        var queued = _tasks.FindIndex(task => task.Status == AgentActivityStatus.Queued);
        if (queued >= 0)
        {
            var failThisOne = _cycle % 5 == 4;
            _tasks[queued] = _tasks[queued] with
            {
                Status = failThisOne ? AgentActivityStatus.Failed : AgentActivityStatus.Running,
                StartedAt = now,
                EstimatedEndAt = now.AddSeconds(40),
                Error = failThisOne ? "Provider rate limit — the unit is queued for another attempt" : null,
                Attempt = failThisOne ? _tasks[queued].EffectiveAttempt + 1 : _tasks[queued].EffectiveAttempt,
            };
            return;
        }

        var retryable = _tasks.FindIndex(task => task.CanRetry);
        if (retryable >= 0)
        {
            _tasks[retryable] = _tasks[retryable] with
            {
                Status = AgentActivityStatus.Running,
                StartedAt = now,
                EstimatedEndAt = now.AddSeconds(40),
                Error = null,
                Attempt = _tasks[retryable].EffectiveAttempt + 1,
            };
            return;
        }

        // Every unit has been through the queue: loop it back so the demo keeps running at a steady memory cost.
        _tasks = [.. _tasks.Select(task => task with
        {
            Status = AgentActivityStatus.Queued,
            StartedAt = null,
            EstimatedEndAt = null,
            Error = null,
            CostMicroUnits = null,
            Attempt = 1,
            Worker = null,
        })];
    }

    /// <summary>Records a handoff every third cycle, keeping the newest <see cref="MaxHandoffs"/> links.</summary>
    private void AddHandoff(DateTimeOffset now)
    {
        if (_cycle % 3 != 0 || _cycle == 0)
        {
            return;
        }

        var from = _agents[_cycle % _agents.Count];
        var to = _agents[(_cycle + 1) % _agents.Count];
        _handoffs.Add(new AgentHandoffLink(to.Id, now) { FromAgentId = from.Id });
        Trim();
    }

    /// <summary>Rebuilds the presence cursors: each of the three collaborating agents anchors to a paragraph, the
    /// anchor and the activity moving as the run advances.</summary>
    private void RefreshPresence(DateTimeOffset now)
    {
        var agents = _agents;
        var markers = new List<PresenceMarker>(agents.Count);

        for (var index = 0; index < agents.Count; index++)
        {
            var agent = agents[index];
            var isRunning = _blocks.Any(block => block.End is null
                && string.Equals(block.AgentId, agent.Id, StringComparison.Ordinal));

            markers.Add(new PresenceMarker($"presence-{agent.Id}", agent)
            {
                AnchorId = _anchors[(_cycle + index) % _anchors.Count],
                Activity = isRunning
                    ? PresenceActivity.Editing
                    : index switch
                    {
                        0 => PresenceActivity.Typing,
                        1 => PresenceActivity.Commenting,
                        _ => PresenceActivity.Viewing,
                    },
            });
        }

        _markers = markers;
    }

    /// <summary>Trims the bounded lists to their caps, dropping the oldest entries.</summary>
    private void Trim()
    {
        if (_blocks.Count > MaxBlocks)
        {
            var finished = _blocks.FindIndex(block => block.End is not null);
            _blocks.RemoveAt(finished >= 0 ? finished : 0);
        }

        if (_handoffs.Count > MaxHandoffs)
        {
            _handoffs.RemoveRange(0, _handoffs.Count - MaxHandoffs);
        }
    }

    /// <summary>Publishes the immutable snapshot the page renders; the caller holds the gate.</summary>
    private void Publish()
    {
        _state = new AiAgentRunState
        {
            Sequence = _sequence,
            EventCount = _eventCount,
            Blocks = [.. _blocks],
            Handoffs = [.. _handoffs],
            Tasks = [.. _tasks],
            Markers = _markers,
            Calls = _calls,
            LastEvent = _lastEvent,
        };
    }

    /// <summary>Rebuilds the opening snapshot: the seeded history, the forty units and the three cursors. The
    /// caller holds the gate.</summary>
    private void Seed()
    {
        var now = _clock.GetUtcNow();

        _blocks = [.. AiAgentVisualizationsSamples.SeededBlocks(now)];
        _handoffs = [.. AiAgentVisualizationsSamples.SeededHandoffs(now)];
        _tasks = [.. AiAgentVisualizationsSamples.FanOutTasks(now)];
        _calls = [];
        _markers = [];
        _sequence = 0;
        _callSequence = 0;
        _eventCount = 0;
        _beat = 0;
        _cycle = 0;
        _openBlocks = new Queue<string>(_blocks.Where(block => block.End is null).Select(block => block.Id));
        _lastEvent = null;

        RefreshPresence(now);
        Publish();
    }
}
