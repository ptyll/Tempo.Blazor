using FluentAssertions;
using Tempo.Blazor.AI.Models;

namespace Tempo.Blazor.Tests.Components.AI.Agents;

/// <summary>
/// Plan d67b33af, Fáze 1 — the transport-independent half of the agent visualizations:
/// <see cref="AgentEventStream{TEvent}"/> turns the incremental events a host receives (a SignalR hub, a
/// 1 s poll, an <c>IAsyncEnumerable</c>) into the ONE strictly ordered tail a component may apply on top
/// of its snapshot.
/// <para>
/// The contract under test is the whole reason the class exists: applying the same batch twice must change
/// nothing (idempotence), an out-of-order batch must land in <c>Sequence</c> order, an older event must be
/// ignored, a hole must never be papered over (the host has to reload the snapshot instead), and a batch of
/// tens of thousands of events must still be one linear pass — the demo simulator in Fáze 9 pushes 10
/// events per second for two minutes, so any per-event list scan would show up as a growing frame time.
/// </para>
/// </summary>
public sealed class AgentEventStreamTests
{
    /// <summary>A minimal event shape: the class never looks inside TEvent beyond the sequence selector.</summary>
    private sealed record TestEvent(long Sequence, string Payload);

    private static TestEvent Event(long sequence) => new(sequence, $"e{sequence}");

    private static long[] Sequences(IEnumerable<TestEvent> events)
        => events.Select(item => item.Sequence).ToArray();

    [Fact]
    public void ANewStream_HasAppliedNothingYet()
    {
        var stream = new AgentEventStream<TestEvent>(item => item.Sequence);

        stream.LastSequence.Should().BeNull(
            "an empty stream has no baseline, so the FIRST batch may legitimately start at 0, at 1 or at "
            + "whatever sequence the backend restarted from");
        stream.HasGap.Should().BeFalse();
    }

    [Fact]
    public void TheFirstBatch_MayStartAtAnySequence()
    {
        var stream = new AgentEventStream<TestEvent>(item => item.Sequence);

        var applied = stream.Apply([Event(42), Event(43)]);

        Sequences(applied).Should().Equal([42L, 43L]);
        stream.LastSequence.Should().Be(43);
        stream.HasGap.Should().BeFalse("a stream with no baseline cannot be missing anything yet");
    }

    [Fact]
    public void Apply_ReturnsTheNewEvents_InSequenceOrder()
    {
        var stream = new AgentEventStream<TestEvent>(item => item.Sequence, snapshotSequence: 2);

        var applied = stream.Apply([Event(5), Event(3), Event(4)]);

        Sequences(applied).Should().Equal([3L, 4L, 5L],
            "a batch can be delivered out of order (a poll merges two pages), and the component applies the "
            + "tail in the order the run actually happened");
        stream.LastSequence.Should().Be(5);
    }

    [Fact]
    public void Apply_IgnoresDuplicatesAndOlderEvents()
    {
        var stream = new AgentEventStream<TestEvent>(item => item.Sequence);
        stream.Apply([Event(1), Event(2), Event(3)]);

        var applied = stream.Apply([Event(2), Event(3), Event(4)]);

        Sequences(applied).Should().Equal([4L],
            "the first two events were already applied — re-applying them would duplicate a block or a lane");
        stream.LastSequence.Should().Be(4);
    }

    [Fact]
    public void Apply_IsIdempotent_ForTheSameBatch()
    {
        var stream = new AgentEventStream<TestEvent>(item => item.Sequence);
        var batch = new[] { Event(1), Event(2), Event(3) };

        var first = stream.Apply(batch);
        var second = stream.Apply(batch);

        Sequences(first).Should().Equal([1L, 2L, 3L]);
        second.Should().BeEmpty("a redelivered batch is not new information");
        stream.LastSequence.Should().Be(3);
        stream.HasGap.Should().BeFalse("a redelivery is not a hole");
    }

    [Fact]
    public void Apply_AppliesAnEventWithTheSameSequence_OnlyOnce()
    {
        var stream = new AgentEventStream<TestEvent>(item => item.Sequence);

        var applied = stream.Apply([Event(1), Event(1), Event(1)]);

        Sequences(applied).Should().Equal([1L],
            "Sequence is the identity of an event — two copies of it are one fact");
    }

    [Fact]
    public void Apply_RejectsTheWholeBatch_WhenASequenceIsMissing()
    {
        var stream = new AgentEventStream<TestEvent>(item => item.Sequence);
        stream.Apply([Event(1), Event(2)]);
        var expected = new List<long>();
        stream.GapDetected += missing => expected.Add(missing);

        var applied = stream.Apply([Event(3), Event(5)]);

        applied.Should().BeEmpty(
            "3 is followed by 5: applying it would leave the state missing 4 forever, and nothing later "
            + "can tell which block, lane or edge was skipped — the host has to reload the snapshot");
        stream.LastSequence.Should().Be(2,
            "nothing from the batch is applied, so the baseline stays where the last complete tail ended");
        stream.HasGap.Should().BeTrue();
        expected.Should().Equal([3L], "the handler is told which sequence the stream expected next");
    }

    [Fact]
    public void GapDetected_IsRaisedOncePerBatch()
    {
        var stream = new AgentEventStream<TestEvent>(item => item.Sequence);
        var raised = 0;
        stream.GapDetected += _ => raised++;

        stream.Apply([Event(1), Event(3), Event(7)]);

        raised.Should().Be(1,
            "one batch is one hole; a handler that reloads the snapshot must not be called three times");
    }

    [Fact]
    public void AGapOnTheVeryFirstEvent_IsDetectedAgainstAnExplicitBaseline()
    {
        var stream = new AgentEventStream<TestEvent>(item => item.Sequence, snapshotSequence: 10);

        var applied = stream.Apply([Event(12)]);

        applied.Should().BeEmpty("11 is missing between the snapshot and the first live event");
        stream.HasGap.Should().BeTrue();
        stream.LastSequence.Should().Be(10);
    }

    [Fact]
    public void TheNextCompleteBatch_ClearsTheGap()
    {
        var stream = new AgentEventStream<TestEvent>(item => item.Sequence);
        stream.Apply([Event(1), Event(3)]);
        stream.HasGap.Should().BeTrue();

        var applied = stream.Apply([Event(1), Event(2), Event(3)]);

        Sequences(applied).Should().Equal([1L, 2L, 3L],
            "the host resends the missed tail (or reloads the snapshot and replays from it)");
        stream.HasGap.Should().BeFalse("the stream is contiguous again");
    }

    [Fact]
    public void AnEmptyBatch_ChangesNothing()
    {
        var stream = new AgentEventStream<TestEvent>(item => item.Sequence);
        stream.Apply([Event(1)]);

        var applied = stream.Apply([]);

        applied.Should().BeEmpty();
        stream.LastSequence.Should().Be(1);
        stream.HasGap.Should().BeFalse();
    }

    [Fact]
    public void ResetToSnapshot_MovesTheBaseline_AndClearsTheGap()
    {
        var stream = new AgentEventStream<TestEvent>(item => item.Sequence);
        stream.Apply([Event(1), Event(5)]);
        stream.HasGap.Should().BeTrue();

        stream.ResetToSnapshot(20);

        stream.LastSequence.Should().Be(20,
            "the snapshot the host reloaded is authoritative: everything up to its own sequence is included");
        stream.HasGap.Should().BeFalse("the reload is exactly the repair the gap asked for");

        Sequences(stream.Apply([Event(21)])).Should().Equal([21L]);
    }

    [Fact]
    public void ResetToSnapshot_MayMoveTheBaselineBackwards()
    {
        var stream = new AgentEventStream<TestEvent>(item => item.Sequence);
        stream.Apply([Event(1), Event(2), Event(3)]);

        stream.ResetToSnapshot(2);

        Sequences(stream.Apply([Event(3)])).Should().Equal([3L],
            "a snapshot taken before the last events must be able to replay them");
    }

    [Fact]
    public void ALargeBatch_IsAppliedInOneOrderedPass()
    {
        const int count = 50_000;
        var stream = new AgentEventStream<TestEvent>(item => item.Sequence);
        var batch = Enumerable.Range(1, count).Select(sequence => Event(sequence)).ToArray();

        var applied = stream.Apply(batch);

        applied.Should().HaveCount(count);
        applied[0].Sequence.Should().Be(1);
        applied[^1].Sequence.Should().Be(count);
        stream.LastSequence.Should().Be(count);
        stream.HasGap.Should().BeFalse();
    }

    [Fact]
    public void ALargeOutOfOrderBatch_IsSortedBeforeItIsCheckedForHoles()
    {
        const int count = 5_000;
        var stream = new AgentEventStream<TestEvent>(item => item.Sequence, snapshotSequence: 0);
        var batch = Enumerable.Range(1, count).Reverse().Select(sequence => Event(sequence)).ToArray();

        var applied = stream.Apply(batch);

        Sequences(applied).Should().BeEquivalentTo(Enumerable.Range(1, count).Select(value => (long)value),
            options => options.WithStrictOrdering(),
            "a reverse-ordered page must not look like a hole at every step");
        stream.HasGap.Should().BeFalse();
    }

    [Fact]
    public void ConcurrentApplies_ApplyEveryEventExactlyOnce()
    {
        const int count = 500;
        var stream = new AgentEventStream<TestEvent>(item => item.Sequence);
        var batch = Enumerable.Range(1, count).Select(sequence => Event(sequence)).ToArray();
        var applied = new System.Collections.Concurrent.ConcurrentBag<long>();

        Parallel.For(0, 8, _ =>
        {
            foreach (var item in stream.Apply(batch))
            {
                applied.Add(item.Sequence);
            }
        });

        applied.Should().HaveCount(count,
            "two transports (a hub and a poll) may feed one stream, and neither may duplicate a block");
        applied.OrderBy(value => value).Should().Equal(Enumerable.Range(1, count).Select(value => (long)value));
    }

    [Fact]
    public void AHandlerThatResetsTheStream_FromInsideTheGapCallback_IsSupported()
    {
        var stream = new AgentEventStream<TestEvent>(item => item.Sequence);
        stream.Apply([Event(1)]);
        stream.GapDetected += _ => stream.ResetToSnapshot(9);

        stream.Apply([Event(5)]);

        stream.LastSequence.Should().Be(9,
            "GapDetected is the host's synchronous cue to reload the snapshot, so the reset has to be legal "
            + "from inside it");
        stream.HasGap.Should().BeFalse("the handler repaired the stream immediately");
    }

    [Fact]
    public void TheGapHandler_RunsAfterTheGateIsReleased_SoItMayWaitForAnotherThread()
    {
        var stream = new AgentEventStream<TestEvent>(item => item.Sequence);
        stream.Apply([Event(1)]);

        var gapWasAlreadySet = false;
        var repaired = false;
        stream.GapDetected += _ =>
        {
            gapWasAlreadySet = stream.HasGap;
            // A handler that hands the repair to another thread — the UI marshalling of a Blazor Server
            // circuit, a background refresh — and waits for it. That is the shape a real host uses, so the
            // gate must not be held while the handler runs: a handler waiting on a thread that calls Apply
            // or LastSequence would otherwise deadlock the stream, and a slow handler would block every
            // other caller for its whole duration.
            repaired = Task.Run(() => stream.Apply([Event(1), Event(2)]))
                .Wait(TimeSpan.FromSeconds(5));
        };

        var apply = Task.Run(() => stream.Apply([Event(4)]));

        apply.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue(
            "GapDetected is raised after the stream's gate is released, so the repair can arrive from "
            + "another thread instead of deadlocking against the applying one");
        gapWasAlreadySet.Should().BeTrue(
            "the hole is published BEFORE the handler runs, so a handler that reads the stream sees the "
            + "state its cue describes");
        repaired.Should().BeTrue("the waiting repair reached the stream while the handler was running");
        stream.LastSequence.Should().Be(2, "the repair applied the missing sequence 2");
        stream.HasGap.Should().BeFalse("the repair closed the hole");
    }

    [Fact]
    public void AStreamWithoutASelector_IsRejected()
    {
        var build = () => new AgentEventStream<TestEvent>(null!);

        build.Should().Throw<ArgumentNullException>("without a selector the class cannot order anything");
    }
}
