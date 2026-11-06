using FluentAssertions;
using Tempo.Blazor.AI.Models;

namespace Tempo.Blazor.Tests.Components.AI.Agents;

/// <summary>
/// Plan d67b33af, Fáze 7 — the <c>RunWindow</c> / <c>RunSegment</c> / <c>ScheduledRun</c> models behind
/// <c>TmScheduledRuns</c>.
/// <para>
/// The facts pin what the surface is allowed to claim: a window that closes before it opens offers no time, a negative
/// capacity is zero instead of a negative room, a window the host committed beyond its capacity is OVERLOADED (and says
/// by how much) instead of quietly empty, a run with no stored segments has no preference, and a blank identity on either
/// a window or a run is rejected because the plan and the reader's own placement are keyed by it.
/// </para>
/// </summary>
public class RunWindowTests
{
    private static readonly DateTimeOffset Nine = new(2026, 11, 9, 9, 0, 0, TimeSpan.Zero);

    private static RunWindow Window(string id, int capacity = 4, int used = 0, int hours = 3) =>
        new(id, Nine, Nine.AddHours(hours), capacity, used);

    // ── the window ───────────────────────────────────────────────────────────

    [Fact]
    public void ABlankWindowIdIsRejected_SoThePlanKeepsItsKey()
    {
        var blank = () => new RunWindow("  ", Nine, Nine.AddHours(1), 4);
        var missing = () => new RunWindow(null!, Nine, Nine.AddHours(1), 4);

        blank.Should().Throw<ArgumentException>();
        missing.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AWindowThatClosesBeforeItOpensOffersNoTime()
    {
        var broken = new RunWindow("w1", Nine.AddHours(2), Nine, 4);

        broken.HasDuration.Should().BeFalse();
        broken.Duration.Should().Be(TimeSpan.Zero);
        broken.Contains(Nine).Should().BeFalse("a window with no time contains nothing");
    }

    [Fact]
    public void TheEndOfAWindowIsExclusive_SoTwoAdjacentWindowsPartitionTheTimeline()
    {
        var first = new RunWindow("w1", Nine, Nine.AddHours(1), 4);
        var second = new RunWindow("w2", Nine.AddHours(1), Nine.AddHours(2), 4);

        first.Contains(Nine.AddHours(1)).Should().BeFalse("the closing instant belongs to the next window");
        second.Contains(Nine.AddHours(1)).Should().BeTrue();
        first.Contains(Nine.AddMinutes(59)).Should().BeTrue();
    }

    [Fact]
    public void ANegativeCapacityOrLoadReadsAsZero()
    {
        var window = Window("w1", capacity: -5, used: -2);

        window.EffectiveCapacity.Should().Be(0);
        window.EffectiveUsed.Should().Be(0);
        window.Available.Should().Be(0);
        window.Utilization.Should().Be(0d, "there is no ratio to report when there is no capacity");
        window.IsFull.Should().BeTrue();
    }

    [Fact]
    public void AnOverloadedWindowSaysByHowMuch()
    {
        var window = Window("w1", capacity: 4, used: 7);

        window.IsOverloaded.Should().BeTrue();
        window.Overflow.Should().Be(3);
        window.Available.Should().Be(0, "an overloaded window offers no room at all");
        window.Utilization.Should().Be(1d);
        Window("w1", capacity: 4, used: 1).IsOverloaded.Should().BeFalse();
    }

    [Fact]
    public void AWindowKnowsItsDay_FromItsOwnStart()
    {
        var late = new RunWindow("w1", new DateTimeOffset(2026, 11, 9, 22, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 10, 2, 0, 0, TimeSpan.Zero), 4);

        late.Day.Should().Be(new DateOnly(2026, 11, 9), "a window belongs to the day it starts on");
    }

    // ── the run and its segments ─────────────────────────────────────────────

    [Fact]
    public void ABlankRunIdIsRejected()
    {
        var blank = () => new ScheduledRun("", "Nightly batch");

        blank.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ARunWithoutSegmentsHasNoPreferredWindow()
    {
        var run = new ScheduledRun("r1", "Nightly batch") { EstimatedUnits = 10 };

        run.HasSegments.Should().BeFalse();
        run.PreferredWindowId.Should().BeNull();
        run.NeedsCapacity.Should().BeTrue();
        run.PlannedUnits.Should().Be(10);
    }

    [Fact]
    public void TheFirstStoredSegmentNamesThePreferredWindow()
    {
        var run = new ScheduledRun("r1", "Nightly batch")
        {
            EstimatedUnits = 10,
            Segments =
            [
                new RunSegment("w1", Nine, Nine.AddHours(1), 4),
                new RunSegment("w2", Nine.AddHours(3), Nine.AddHours(4), 4),
            ],
        };

        // The FIRST segment names the preference; the ones after it only describe the rest of the placement.
        run.PreferredWindowId.Should().Be("w1");
        run.HasSegments.Should().BeTrue();
    }

    [Fact]
    public void TheStatusDefaultsToPlanned_AndAnUnknownStatusIsNotAState()
    {
        var run = new ScheduledRun("r1", "Nightly batch");

        run.Status.Should().Be(ScheduledRunStatus.Planned, "an unclassified run is planned work, never finished work");
        run.HasKnownStatus.Should().BeTrue();
        run.IsSettled.Should().BeFalse();

        (run with { Status = ScheduledRunStatus.Succeeded }).IsSettled.Should().BeTrue();
        (run with { Status = ScheduledRunStatus.Failed }).IsSettled.Should().BeTrue();
        (run with { Status = ScheduledRunStatus.Running }).IsRunning.Should().BeTrue();
        (run with { Status = ScheduledRunStatus.Paused }).IsPaused.Should().BeTrue();
        (run with { Status = (ScheduledRunStatus)42 }).HasKnownStatus.Should().BeFalse();
    }

    [Fact]
    public void ANegativeEstimateIsNoCapacityAtAll()
    {
        var run = new ScheduledRun("r1", "Nightly batch") { EstimatedUnits = -5 };

        run.PlannedUnits.Should().Be(0);
        run.NeedsCapacity.Should().BeFalse();
        new ScheduledRun("r1", "x") { EstimatedUnits = 0 }.NeedsCapacity.Should().BeFalse();
    }

    [Fact]
    public void ASegmentNeedsAWindow_AndItsUnitsAreNeverNegative()
    {
        var blank = () => new RunSegment(" ", Nine, Nine.AddHours(1), 2);
        blank.Should().Throw<ArgumentException>();

        var segment = new RunSegment("w1", Nine, Nine.AddHours(2), -3);
        segment.EffectiveUnits.Should().Be(0);
        segment.Duration.Should().Be(TimeSpan.FromHours(2));
        new RunSegment("w1", Nine.AddHours(2), Nine, 1).Duration.Should().Be(TimeSpan.Zero);
    }

    // ── the move request ─────────────────────────────────────────────────────

    [Fact]
    public void TheMoveRequestNamesBothWindowsAndNeedsADestination()
    {
        var run = new ScheduledRun("r1", "Nightly batch");
        var from = Window("w1");
        var to = Window("w2", hours: 3);

        var move = new ScheduledRunMoveRequest(run, from, to);
        move.WindowId.Should().Be("w2");
        move.IsChange.Should().BeTrue();

        new ScheduledRunMoveRequest(run, null, to).IsChange.Should().BeTrue("a run with no window always changes");
        new ScheduledRunMoveRequest(run, to, to).IsChange.Should().BeFalse("moving a run onto its own window is no move");

        var noDestination = () => new ScheduledRunMoveRequest(run, from, null!);
        noDestination.Should().Throw<ArgumentNullException>();
        var noRun = () => new ScheduledRunMoveRequest(null!, from, to);
        noRun.Should().Throw<ArgumentNullException>();
    }
}
