namespace Tempo.Blazor.AI.Models;

/// <summary>
/// Turns the incremental events a host receives — from a SignalR hub, a one-second poll, an
/// <c>IAsyncEnumerable</c>, a server-sent event stream — into the ONE strictly ordered tail an agent visualization
/// may apply on top of its snapshot.
/// <para>
/// The contract is a snapshot PLUS events, never a live collection: the component owns its view of the data and
/// the host owns the transport. This class is the shared half of that contract, so eleven components do not each
/// invent their own de-duplication:
/// </para>
/// <list type="bullet">
/// <item><description><b>Ordered</b>: <see cref="Apply"/> sorts a batch by the sequence selector, so a page merge
/// or a re-delivered page cannot apply a block before the event that opened it.</description></item>
/// <item><description><b>Idempotent</b>: an event whose sequence was already applied is dropped, so applying the
/// same batch twice changes nothing — the same batch genuinely arrives twice when a hub reconnects.</description></item>
/// <item><description><b>Contiguous</b>: a hole is never papered over. When a sequence is missing the WHOLE batch is
/// rejected, <see cref="HasGap"/> turns true and <see cref="GapDetected"/> is raised once with the sequence the
/// stream expected next. The applied tail is therefore always a contiguous run, and the host's repair (reload the
/// snapshot, or resend the missing range) is exact.</description></item>
/// <item><description><b>Transport-neutral</b>: nothing here reads the clock, awaits, or knows what an event means.
/// The only thing the class needs is a sequence number that grows by one per applied event.</description></item>
/// <item><description><b>Thread-safe</b>: a hub callback and a polling loop may feed the same stream; every mutation
/// and read happens under one gate, so no event is applied twice and no batch is spliced into another. The one
/// callback that runs OUTSIDE the gate is <see cref="GapDetected"/> — it fires after the hole has been published,
/// so a handler may wait for another thread to repair the stream instead of deadlocking against the applying
/// one.</description></item>
/// </list>
/// <para>
/// A stream starts with no baseline (<see cref="LastSequence"/> is null), so the first batch may begin at 0, at 1 or
/// at whatever number the backend restarted from, and no hole is reported against a baseline that does not exist.
/// <see cref="ResetToSnapshot"/> installs the baseline of a reloaded snapshot, which is also the documented repair
/// after a gap.
/// </para>
/// </summary>
/// <typeparam name="TEvent">
/// The host's own event shape. The class never looks inside it beyond the sequence selector, so a record, a DTO or a
/// discriminated-union wrapper all work.
/// </typeparam>
public sealed class AgentEventStream<TEvent>
{
    private readonly Func<TEvent, long> _sequenceSelector;
    private readonly object _gate = new();

    private long _lastSequence;
    private bool _hasBaseline;
    private bool _hasGap;

    /// <summary>Creates an empty stream with no baseline.</summary>
    /// <param name="sequenceSelector">
    /// Reads the monotonically increasing sequence of an event. The backend must assign one sequence per applied
    /// change; the stream treats "the next expected sequence is the last applied plus one" as its definition of
    /// contiguity.
    /// </param>
    public AgentEventStream(Func<TEvent, long> sequenceSelector)
        : this(sequenceSelector, null)
    {
    }

    /// <summary>Creates a stream whose baseline is the sequence of an already-applied snapshot.</summary>
    /// <param name="sequenceSelector">Reads the monotonically increasing sequence of an event.</param>
    /// <param name="snapshotSequence">
    /// The sequence the supplied snapshot already includes, or null for a snapshot that has not loaded yet. With a
    /// baseline, the first live event must be exactly the next sequence, or the batch is reported as a gap.
    /// </param>
    public AgentEventStream(Func<TEvent, long> sequenceSelector, long? snapshotSequence)
    {
        _sequenceSelector = sequenceSelector ?? throw new ArgumentNullException(nameof(sequenceSelector));
        if (snapshotSequence is { } baseline)
        {
            _lastSequence = baseline;
            _hasBaseline = true;
        }
    }

    /// <summary>
    /// The sequence of the last applied event, or null when nothing has been applied to a stream without a snapshot
    /// baseline yet. A host compares it with its snapshot's own sequence to decide whether the two are in sync.
    /// </summary>
    public long? LastSequence
    {
        get
        {
            lock (_gate)
            {
                return _hasBaseline ? _lastSequence : null;
            }
        }
    }

    /// <summary>
    /// Whether the LAST <see cref="Apply"/> call found a hole. True means the host must reload the snapshot (or
    /// resend the missing range) before the component can be trusted to be showing the run; the next batch without a
    /// hole, or <see cref="ResetToSnapshot"/>, clears it.
    /// </summary>
    public bool HasGap
    {
        get
        {
            lock (_gate)
            {
                return _hasGap;
            }
        }
    }

    /// <summary>
    /// Raised once per batch that contains a hole, with the sequence the stream expected next (the first missing
    /// one). Invoked synchronously on the applying thread AFTER the stream's gate is released, so a handler may
    /// call <see cref="ResetToSnapshot"/>, <see cref="Apply"/> or <see cref="LastSequence"/> — including from
    /// another thread it waits for — without deadlocking the stream. <see cref="HasGap"/> is already true when the
    /// handler starts, but the state it describes is not pinned: another feed may repair the hole while the
    /// handler runs.
    /// </summary>
    public event Action<long>? GapDetected;

    /// <summary>
    /// Applies an incremental batch and returns exactly the events that are NEW, in sequence order. Nothing is
    /// returned for an event that was already applied, for an older one, or for any event of a batch that contains a
    /// hole (see the type's remarks) — a caller therefore appends the returned tail to its snapshot and never has to
    /// de-duplicate itself.
    /// </summary>
    /// <param name="events">The batch, in any order. An empty batch is legal and changes nothing.</param>
    /// <returns>The newly applied events in ascending sequence order; empty when there was nothing to apply.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="events"/> is null.</exception>
    public IReadOnlyList<TEvent> Apply(IEnumerable<TEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        IReadOnlyList<TEvent> applied;
        long? missing;

        lock (_gate)
        {
            (applied, missing) = ApplyCore(events);
        }

        if (missing is { } expected)
        {
            // Raised OUTSIDE the gate: a handler that marshals the repair to another thread and waits for it is
            // the normal host shape, and holding the lock here would deadlock that thread against this one. The
            // hole is already published (HasGap is true) before the handler runs.
            GapDetected?.Invoke(expected);
        }

        return applied;
    }

    /// <summary>
    /// The single-threaded half of <see cref="Apply"/>: sorts, de-duplicates, checks for a hole and commits the
    /// applied tail. Called with the gate held; the caller raises <see cref="GapDetected"/> after releasing it.
    /// </summary>
    /// <param name="events">The batch, in any order; never null and never empty-lazy at this point.</param>
    /// <returns>The new events in sequence order (empty when a hole rejected the batch) and, when there is a hole,
    /// the first sequence the stream does not have.</returns>
    private (IReadOnlyList<TEvent> Applied, long? Missing) ApplyCore(IEnumerable<TEvent> events)
    {
        var ordered = events.OrderBy(_sequenceSelector).ToList();
        if (ordered.Count == 0)
        {
            // An empty poll is not a repair: it neither advances the tail nor hides a known hole.
            return (Array.Empty<TEvent>(), null);
        }

        var applied = new List<TEvent>(ordered.Count);
        var last = _hasBaseline ? _lastSequence : (long?)null;
        long? missing = null;

        foreach (var item in ordered)
        {
            var sequence = _sequenceSelector(item);
            if (last is { } current)
            {
                if (sequence <= current)
                {
                    // Already applied (or older than the baseline): a redelivery is not new information.
                    continue;
                }

                if (sequence != current + 1)
                {
                    // Report the FIRST sequence the stream does not have — with no baseline that is the
                    // batch's own first event, and otherwise the one after the last complete tail. NEVER
                    // current + 1: the batch is rejected whole, so the events walked before the hole are
                    // not applied either, and a host that resends from a "current + 1" would skip them.
                    missing = _hasBaseline ? _lastSequence + 1 : _sequenceSelector(ordered[0]);
                    break;
                }
            }

            applied.Add(item);
            last = sequence;
        }

        if (missing is not null)
        {
            // Set BEFORE returning: the caller raises the event after the gate is released, and the handler's
            // repair must not be overwritten by this batch in the meantime.
            _hasGap = true;
            return (Array.Empty<TEvent>(), missing);
        }

        if (applied.Count > 0 && last is { } baseline)
        {
            // The applied tail is contiguous from the previous baseline, so the stream is in sync again. A batch
            // that applied nothing (a pure redelivery) is NOT a repair and must not hide a hole.
            _lastSequence = baseline;
            _hasBaseline = true;
            _hasGap = false;
        }

        return (applied, null);
    }

    /// <summary>
    /// Re-bases the stream on a reloaded snapshot: everything up to <paramref name="sequence"/> is treated as
    /// already applied, and the gap is cleared. Moving the baseline BACKWARDS is legal and is how a snapshot taken
    /// before the last events replays them.
    /// </summary>
    /// <param name="sequence">The sequence the new snapshot includes. Pass 0 for a snapshot that covers nothing.</param>
    public void ResetToSnapshot(long sequence)
    {
        lock (_gate)
        {
            _lastSequence = sequence;
            _hasBaseline = true;
            _hasGap = false;
        }
    }
}
