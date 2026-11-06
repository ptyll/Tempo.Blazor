using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Demo.SharedUI.Services;
using Xunit;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// The simulator behind the <c>/ai-agent-visualizations</c> demo page. These facts walk the contract the page and
/// the docs claim: a <see cref="TimeProvider"/>-driven generator that stamps ONE monotonic <c>Sequence</c> per
/// applied change, plays, pauses, changes its event rate, keeps its whole state BOUNDED (so a two-minute run cannot
/// grow the heap) and loops its work queue instead of accumulating one.
/// </summary>
public class AiAgentRunSimulatorTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 12, 20, 0, 0, TimeSpan.Zero);

    private static (AiAgentRunSimulator Simulator, FakeTimeProvider Clock) Create()
    {
        var clock = new FakeTimeProvider(Start);
        return (new AiAgentRunSimulator(clock), clock);
    }

    [Fact]
    public void TheSimulatorStartsPausedOnASeededSnapshot()
    {
        var (simulator, _) = Create();

        simulator.IsRunning.Should().BeFalse("the page must not start a timer before the reader asks for one");
        simulator.Speed.Should().Be(1d);
        simulator.State.EventCount.Should().Be(0);
        simulator.State.Sequence.Should().Be(0);
        simulator.State.Blocks.Should().NotBeEmpty("the lanes open on a history a reader can read straight away");
        simulator.State.Blocks.Should().Contain(block => block.End == null,
            "one block is still RUNNING, so the surface's live now line has something to end");
        simulator.State.Tasks.Should().HaveCount(40);
        simulator.State.Markers.Should().HaveCount(4);
    }

    [Fact]
    public void PlayAppliesExactlyOneEventPerBaseInterval()
    {
        var (simulator, clock) = Create();

        simulator.Play();
        simulator.IsRunning.Should().BeTrue();

        clock.Advance(TimeSpan.FromMilliseconds(100));
        simulator.State.EventCount.Should().Be(1);

        clock.Advance(TimeSpan.FromMilliseconds(900));
        simulator.State.EventCount.Should().Be(10, "the base rate is ten events per second");
    }

    [Fact]
    public void PauseStopsTheGeneratorAndPlayResumesIt()
    {
        var (simulator, clock) = Create();

        simulator.Play();
        clock.Advance(TimeSpan.FromMilliseconds(500));
        simulator.Pause();
        simulator.IsRunning.Should().BeFalse();

        var applied = simulator.State.EventCount;
        clock.Advance(TimeSpan.FromSeconds(2));
        simulator.State.EventCount.Should().Be(applied);

        simulator.Play();
        clock.Advance(TimeSpan.FromMilliseconds(300));
        simulator.State.EventCount.Should().Be(applied + 3);
    }

    [Theory]
    [InlineData(0.5d, 5)]
    [InlineData(1d, 10)]
    [InlineData(2d, 20)]
    public void TheSpeedMultiplierSetsTheEventRate(double speed, int perSecond)
    {
        var (simulator, clock) = Create();

        simulator.SetSpeed(speed);
        simulator.EventsPerSecond.Should().Be(perSecond);

        simulator.Play();
        clock.Advance(TimeSpan.FromSeconds(1));
        simulator.State.EventCount.Should().Be(perSecond);
    }

    [Fact]
    public void AnUnsupportedSpeedIsRejectedInsteadOfSilentlyIgnored()
    {
        var (simulator, _) = Create();

        var act = () => simulator.SetSpeed(0d);
        act.Should().Throw<ArgumentOutOfRangeException>();
        simulator.Speed.Should().Be(1d);
    }

    [Fact]
    public void EveryAppliedEventCarriesTheNextSequence()
    {
        var (simulator, _) = Create();
        var sequences = new List<long>();

        for (var index = 0; index < 40; index++)
        {
            var batch = simulator.Step();
            batch.Should().HaveCount(1, "one tick applies one event at the base rate");
            sequences.Add(batch[0].Sequence);
        }

        sequences.Should().BeEquivalentTo(Enumerable.Range(1, 40).Select(value => (long)value),
            "the change log is contiguous: the shared event stream treats a hole as a broken transport");
        simulator.State.Sequence.Should().Be(40);
    }

    [Fact]
    public void FourAppliedEventsOpenCallThenFinishOneBlockAndRetireTheOldest()
    {
        var (simulator, _) = Create();

        var kinds = Enumerable.Range(0, 24).Select(_ => simulator.Step()[0].Kind).ToList();

        kinds.Take(3).Should().Equal(
            AiAgentRunEventKind.BlockStarted,
            AiAgentRunEventKind.ToolCalled,
            AiAgentRunEventKind.BlockCompleted);
        kinds.Where((_, index) => index % 4 == 2).Should().OnlyContain(kind => kind == AiAgentRunEventKind.BlockCompleted);
        kinds.Should().Contain(AiAgentRunEventKind.BlockRetired,
            "the housekeeping beat retires the oldest finished block once the lane is over its cap");
    }

    [Fact]
    public void TheBlockListStaysBoundedAcrossALongRun()
    {
        var (simulator, _) = Create();

        for (var index = 0; index < 600; index++)
        {
            simulator.Step();
            simulator.State.Blocks.Count.Should().BeLessThanOrEqualTo(AiAgentRunSimulator.MaxBlocks);
        }

        simulator.State.Blocks.Should().Contain(block => block.End == null,
            "the run keeps exactly one open block, which is what a live lane paints");
    }

    [Fact]
    public void TheFanOutQueueAdvancesRetriesAndNeverGrows()
    {
        var (simulator, clock) = Create();

        for (var index = 0; index < 400; index++)
        {
            simulator.Step();
        }

        var tasks = simulator.State.Tasks;
        tasks.Should().HaveCount(40, "the queue loops over the same forty units instead of piling new ones up");
        tasks.Should().Contain(task => task.Status == AgentActivityStatus.Succeeded);
        tasks.Should().Contain(task => task.EffectiveAttempt > 1,
            "a retried unit proves the queue exercises the retry path");
        tasks.Where(task => task.HasError).Should().OnlyContain(task => task.Status == AgentActivityStatus.Failed);
    }

    [Fact]
    public void ALongRunDoesNotGrowTheStateWithoutBound()
    {
        var (simulator, _) = Create();

        var tasksBefore = simulator.State.Tasks.Count;
        var blocksBefore = simulator.State.Blocks.Count;

        for (var index = 0; index < 2_000; index++)
        {
            simulator.Step();
        }

        simulator.State.Tasks.Count.Should().Be(tasksBefore);
        simulator.State.Blocks.Count.Should().BeLessThanOrEqualTo(Math.Max(blocksBefore, AiAgentRunSimulator.MaxBlocks));
        simulator.State.Handoffs.Count.Should().BeLessThanOrEqualTo(AiAgentRunSimulator.MaxHandoffs);
        simulator.State.Markers.Count.Should().Be(4);
    }

    [Fact]
    public void TheGraphCallBatchCarriesTheCallsOfTheLastTickWithTheirOwnContiguousSequence()
    {
        var (simulator, _) = Create();
        var calls = new List<AgentGraphCall>();

        for (var index = 0; index < 12; index++)
        {
            calls.AddRange(simulator.Step()[0].Kind == AiAgentRunEventKind.ToolCalled
                ? simulator.State.Calls
                : []);
        }

        calls.Should().NotBeEmpty();
        calls.Select(call => call.Sequence).Should().BeEquivalentTo(
            Enumerable.Range(1, calls.Count).Select(value => (long)value),
            "the graph's call log is contiguous even though the run's change log interleaves other kinds");
        calls.Should().OnlyContain(call => call.EdgeId != null);
    }

    [Fact]
    public void ThePresenceMarkersCoverEveryAgentOfTheRunAndAnchorToTheDocument()
    {
        var (simulator, _) = Create();

        for (var index = 0; index < 9; index++)
        {
            simulator.Step();
        }

        var markers = simulator.State.Markers;
        markers.Should().HaveCount(4);
        markers.Select(marker => marker.Agent.Id).Should().OnlyHaveUniqueItems();
        markers.Should().OnlyContain(marker => marker.AnchorId != null,
            "the demo document carries a data-presence-anchor per paragraph, so the cursors survive a reflow");
    }

    [Fact]
    public void ResetReturnsTheSeededSnapshotAndRestartsTheChangeLog()
    {
        var (simulator, clock) = Create();

        simulator.Play();
        clock.Advance(TimeSpan.FromSeconds(1));
        simulator.State.EventCount.Should().Be(10);

        simulator.Reset();

        simulator.IsRunning.Should().BeFalse("a reset stops the generator, it does not keep it running");
        simulator.State.EventCount.Should().Be(0);
        simulator.State.Sequence.Should().Be(0);
        simulator.State.Blocks.Should().HaveCount(7);
        simulator.State.Tasks.Should().OnlyContain(task => task.EffectiveAttempt >= 1);
        simulator.Step()[0].Sequence.Should().Be(1, "the next run starts its own contiguous change log");
    }

    [Fact]
    public void TheTickCallbackReportsTheAppliedBatchAndTheNewState()
    {
        var (simulator, clock) = Create();
        var batches = new List<IReadOnlyList<AiAgentRunEvent>>();
        simulator.Ticked += (_, batch) => batches.Add(batch);

        simulator.Play();
        clock.Advance(TimeSpan.FromMilliseconds(300));

        batches.Should().HaveCount(3);
        batches.Should().OnlyContain(batch => batch.Count == 1);
        batches.SelectMany(batch => batch).Select(change => change.Sequence)
            .Should().Equal(1L, 2L, 3L);
    }

    [Fact]
    public void TheActiveAgentsOfTheRunAreTheOnesWithAnOpenBlock()
    {
        var (simulator, _) = Create();

        for (var index = 0; index < 16; index++)
        {
            simulator.Step();
        }

        simulator.RunningAgentIds.Should().NotBeEmpty();
        simulator.RunningAgentIds.Should().BeSubsetOf(simulator.State.Markers.Select(marker => marker.Agent.Id));
    }

    [Fact]
    public void DisposeStopsTheTimer()
    {
        var (simulator, clock) = Create();

        simulator.Play();
        clock.Advance(TimeSpan.FromMilliseconds(200));
        var applied = simulator.State.EventCount;

        simulator.Dispose();
        clock.Advance(TimeSpan.FromSeconds(1));

        simulator.State.EventCount.Should().Be(applied);
        simulator.IsRunning.Should().BeFalse();
    }

    /// <summary>
    /// The timer thread and the circuit's UI thread are two threads on Blazor Server, so a tick can land on the same
    /// list the reader is resetting. Two concurrent tick loops must therefore leave the change log contiguous instead
    /// of throwing out of a <see cref="List{T}"/> or a <see cref="Queue{T}"/> enumeration.
    /// </summary>
    [Fact]
    public void ConcurrentTicksKeepTheChangeLogContiguousAndNeverThrow()
    {
        var (simulator, _) = Create();
        var errors = new ConcurrentQueue<Exception>();

        Parallel.For(0, 2, _ =>
        {
            try
            {
                for (var index = 0; index < 250; index++)
                {
                    simulator.Step();
                }
            }
            catch (Exception exception)
            {
                errors.Enqueue(exception);
            }
        });

        errors.Should().BeEmpty("a tick racing another tick must never corrupt the block list or the open-block queue");
        simulator.State.Sequence.Should().Be(500);
        simulator.State.EventCount.Should().Be(500);
        simulator.State.Blocks.Count.Should().BeLessThanOrEqualTo(AiAgentRunSimulator.MaxBlocks);
        simulator.Step()[0].Sequence.Should().Be(501, "the change log is still contiguous after concurrent ticks");
    }

    /// <summary>
    /// The reader's Reset and the surface's Retry run on the dispatcher while the timer keeps tickling. Nothing may
    /// throw, and the run must be usable straight afterwards — a fresh sequential walk keeps its contiguous sequence.
    /// </summary>
    [Fact]
    public void ATickRacingAResetOrARetryLeavesTheRunUsable()
    {
        var (simulator, _) = Create();
        var errors = new ConcurrentQueue<Exception>();

        Parallel.For(0, 4, worker =>
        {
            try
            {
                for (var index = 0; index < 120; index++)
                {
                    switch ((worker + index) % 4)
                    {
                        case 0:
                            simulator.Step();
                            break;
                        case 1:
                            simulator.RetryTask("unit-04");
                            break;
                        case 2:
                            _ = simulator.State;
                            _ = simulator.RunningAgentIds;
                            break;
                        default:
                            if (index % 24 == 0)
                            {
                                simulator.Reset();
                            }

                            break;
                    }
                }
            }
            catch (Exception exception)
            {
                errors.Enqueue(exception);
            }
        });

        errors.Should().BeEmpty("a reader's reset or retry must never collide with a timer tick");

        var sequences = Enumerable.Range(0, 8).Select(_ => simulator.Step()[0].Sequence).ToList();
        sequences.Should().Equal(Enumerable.Range((int)sequences[0], 8).Select(value => (long)value),
            "the change log restarts contiguous after concurrent ticks, resets and retries");
        simulator.State.Tasks.Should().HaveCount(AiAgentRunSimulator.QueueSize);
        simulator.State.Blocks.Count.Should().BeLessThanOrEqualTo(AiAgentRunSimulator.MaxBlocks);
    }

    /// <summary>
    /// A timer callback is the one place a demo must not throw: an unhandled exception on the thread pool tears the
    /// Server host down. A throwing subscriber is recorded instead, the run keeps absorbing events, and a tick that
    /// arrives after <see cref="AiAgentRunSimulator.Dispose"/> is dropped.
    /// </summary>
    [Fact]
    public void AThrowingSubscriberOrAStaleTickNeverEscapesTheTimerCallback()
    {
        var (simulator, clock) = Create();
        simulator.Ticked += (_, _) => throw new InvalidOperationException("a subscriber blew up");

        simulator.Play();
        var advance = () => clock.Advance(TimeSpan.FromMilliseconds(300));

        advance.Should().NotThrow("an exception in a timer callback would take the whole Server host down");
        simulator.State.EventCount.Should().Be(3, "the reducer keeps folding events even when a subscriber fails");
        simulator.LastTickError.Should().NotBeNull("a swallowed tick is recorded instead of being lost");

        simulator.Dispose();
        var stale = () => clock.Advance(TimeSpan.FromMilliseconds(300));
        stale.Should().NotThrow("a tick that arrives after Dispose is dropped");
    }
}
