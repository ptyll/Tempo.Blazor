using FluentAssertions;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;

namespace Tempo.Blazor.Tests.Components.AI.Agents;

/// <summary>
/// Plan d67b33af, Fáze 7 — <c>RunWindowPlanner</c>, the pure C# half of <c>TmScheduledRuns</c>: which window a run
/// starts in, how its units are split across the windows that follow, how the gaps fill, what happens to a run that no
/// longer fits, and how a window's committed load and its overlarge overflow are reported.
/// <para>
/// The tests drive the planner directly (no renderer, no clock): every case pins concrete windows and concrete unit
/// counts, so a scheduling regression is a number in the failure message rather than a bar nobody measures. The
/// segment TIMES are pinned too, because the bar the reader sees is painted from them.
/// </para>
/// </summary>
public class RunWindowPlannerTests
{
    private static readonly DateTimeOffset Nine = new(2026, 11, 9, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A window of <paramref name="hours"/> hours starting <paramref name="offsetHours"/> after 09:00.</summary>
    private static RunWindow Window(string id, int offsetHours, int hours, int capacity, int used = 0)
        => new(id, Nine.AddHours(offsetHours), Nine.AddHours(offsetHours + hours), capacity, used);

    private static ScheduledRun Run(string id, int units, params string[] preferredWindows)
        => new(id, id)
        {
            EstimatedUnits = units,
            Segments = [.. preferredWindows.Select(windowId => new RunSegment(windowId, Nine, Nine.AddHours(1), 1))],
        };

    // ── window order ─────────────────────────────────────────────────────────

    [Fact]
    public void WindowsAreUsableWhenTheyOfferTimeAndCapacity_AndKeepTheirTimeOrder()
    {
        var late = Window("late", 4, 3, 4);
        var early = Window("early", 0, 3, 4);
        var noTime = new RunWindow("broken", Nine.AddHours(6), Nine.AddHours(5), 4);
        var noCapacity = Window("empty", 0, 3, 0);

        var ordered = RunWindowPlanner.OrderedWindows([late, noTime, early, noCapacity, null!]);

        ordered.Select(window => window.Id).Should().Equal("early", "late");
    }

    [Fact]
    public void TwoWindowsAtTheSameInstantKeepTheHostsOrder()
    {
        var first = Window("first", 0, 3, 4);
        var second = Window("second", 0, 3, 4);

        RunWindowPlanner.OrderedWindows([first, second]).Select(window => window.Id).Should().Equal("first", "second");
    }

    // ── placement ────────────────────────────────────────────────────────────

    [Fact]
    public void ARunTakesTheFirstWindowWhenItHasNoPreference()
    {
        var plan = RunWindowPlanner.Plan(
            [Window("w1", 0, 3, 4), Window("w2", 3, 3, 4)],
            [Run("r1", 3)]);

        plan.Should().HaveCount(1);
        plan[0].Segments.Should().HaveCount(1);
        plan[0].Segments[0].Window.Id.Should().Be("w1");
        plan[0].Segments[0].Units.Should().Be(3);
        plan[0].UnitsPlaced.Should().Be(3);
        plan[0].UnitsUnplaced.Should().Be(0);
        plan[0].IsComplete.Should().BeTrue();
        plan[0].SpansWindows.Should().BeFalse();
    }

    [Fact]
    public void ARunTooLongForOneWindowIsSplitAcrossTheNextOnes()
    {
        var plan = RunWindowPlanner.Plan(
            [Window("w1", 0, 3, 4), Window("w2", 3, 3, 4), Window("w3", 6, 3, 4)],
            [Run("r1", 10)]);

        plan[0].Segments.Select(segment => segment.Window.Id).Should().Equal("w1", "w2", "w3");
        plan[0].Segments.Select(segment => segment.Units).Should().Equal(4, 4, 2);
        plan[0].UnitsPlaced.Should().Be(10);
        plan[0].UnitsUnplaced.Should().Be(0);
        plan[0].SpansWindows.Should().BeTrue();
        plan[0].FirstWindow!.Id.Should().Be("w1");
        plan[0].LastWindow!.Id.Should().Be("w3");
    }

    [Fact]
    public void ARunThatFitsNowhereReportsItsUnplacedUnits_InsteadOfOverflowing()
    {
        var plan = RunWindowPlanner.Plan([Window("w1", 0, 3, 4)], [Run("r1", 10)]);

        plan[0].UnitsPlaced.Should().Be(4);
        plan[0].UnitsUnplaced.Should().Be(6);
        plan[0].IsComplete.Should().BeFalse();
        plan[0].Segments.Should().HaveCount(1, "a window is never filled past its capacity");
        plan[0].Segments[0].Units.Should().Be(4);
    }

    [Fact]
    public void TheGapsFillInDeclarationOrder_SoASmallRunDoesNotWasteAWindow()
    {
        var plan = RunWindowPlanner.Plan(
            [Window("w1", 0, 3, 6), Window("w2", 3, 3, 6)],
            [Run("small", 2), Run("big", 6)]);

        plan[0].Segments.Should().HaveCount(1);
        plan[0].Segments[0].Window.Id.Should().Be("w1");
        plan[0].Segments[0].Units.Should().Be(2);

        plan[1].Segments.Select(segment => segment.Window.Id).Should().Equal("w1", "w2");
        plan[1].Segments.Select(segment => segment.Units).Should().Equal(4, 2);
        plan[1].UnitsUnplaced.Should().Be(0);
    }

    [Fact]
    public void TheCommittedLoadIsTakenOutOfTheSupplyBeforeAnythingIsPlanned()
    {
        var plan = RunWindowPlanner.Plan(
            [Window("w1", 0, 3, 4, used: 3), Window("w2", 3, 3, 4)],
            [Run("r1", 3)]);

        plan[0].Segments.Select(segment => segment.Window.Id).Should().Equal("w1", "w2");
        plan[0].Segments.Select(segment => segment.Units).Should().Equal(1, 2);
    }

    [Fact]
    public void AnOverloadedWindowOffersNoRoomAndThePlanSaysSo()
    {
        var overloaded = Window("w1", 0, 3, 4, used: 6);
        var plan = RunWindowPlanner.Plan([overloaded, Window("w2", 3, 3, 4)], [Run("r1", 4)]);

        overloaded.IsOverloaded.Should().BeTrue();
        plan[0].Segments.Select(segment => segment.Window.Id).Should().Equal("w2");
        plan[0].UnitsPlaced.Should().Be(4);
    }

    [Fact]
    public void APreferredWindowWins_AndTheRunThenContinuesForward()
    {
        var plan = RunWindowPlanner.Plan(
            [Window("w1", 0, 3, 4), Window("w2", 3, 3, 4), Window("w3", 6, 3, 4)],
            [Run("r1", 6, "w2")]);

        plan[0].Segments.Select(segment => segment.Window.Id).Should().Equal("w2", "w3");
        plan[0].Segments.Select(segment => segment.Units).Should().Equal(4, 2);
    }

    [Fact]
    public void TheHostsReadPlacementWinsOverTheRunsOwnPreference()
    {
        var runs = new[] { Run("r1", 4, "w1") };
        var plan = RunWindowPlanner.Plan(
            [Window("w1", 0, 3, 4), Window("w2", 3, 3, 4)],
            runs,
            new Dictionary<string, string> { ["r1"] = "w2" });

        plan[0].Segments[0].Window.Id.Should().Be("w2");
    }

    [Fact]
    public void APreferenceForAWindowThatIsGoneFallsBackToFirstFit()
    {
        var plan = RunWindowPlanner.Plan([Window("w1", 0, 3, 4)], [Run("r1", 2, "vanished")]);

        plan[0].Segments[0].Window.Id.Should().Be("w1", "a run must never silently disappear because the week changed");
        plan[0].UnitsUnplaced.Should().Be(0);
    }

    [Fact]
    public void ARunWithNoUnitsIsPlannedAndPlacedNowhere()
    {
        var plan = RunWindowPlanner.Plan([Window("w1", 0, 3, 4)], [Run("r1", 0)]);

        plan.Should().HaveCount(1);
        plan[0].Segments.Should().BeEmpty();
        plan[0].IsPlaced.Should().BeFalse();
        plan[0].IsComplete.Should().BeTrue("there was nothing to place");
    }

    [Fact]
    public void EveryRunIsPlanned_EvenWhenThereAreNoWindows()
    {
        var plan = RunWindowPlanner.Plan([], [Run("r1", 5)]);

        plan.Should().HaveCount(1);
        plan[0].Segments.Should().BeEmpty();
        plan[0].UnitsUnplaced.Should().Be(5);
    }

    [Fact]
    public void ThePlanKeepsTheHostsRunOrder_AndSkipsNulls()
    {
        var plan = RunWindowPlanner.Plan([Window("w1", 0, 3, 8)], [null!, Run("b", 1), Run("a", 1)]);

        plan.Select(entry => entry.Run.Id).Should().Equal("b", "a");
    }

    [Fact]
    public void PlanOneIsTheSameDecisionForATaskThatIsMovedOnItsOwn()
    {
        var run = Run("r1", 2);
        var plan = RunWindowPlanner.PlanOne([Window("w1", 0, 3, 4), Window("w2", 3, 3, 4)], run, "w2");

        plan.Segments.Should().HaveCount(1);
        plan.Segments[0].Window.Id.Should().Be("w2");
        plan.SegmentIn("w2").Should().NotBeNull();
        plan.SegmentIn("w1").Should().BeNull();
    }

    // ── the slices themselves ────────────────────────────────────────────────

    [Fact]
    public void ASlicesTimeIsProportionalToTheUnitsItTakes()
    {
        // A three-hour window that accepts four units gives each unit 45 minutes.
        var plan = RunWindowPlanner.Plan([Window("w1", 0, 3, 4)], [Run("r1", 2)]);

        var segment = plan[0].Segments[0];
        segment.Start.Should().Be(Nine);
        segment.End.Should().Be(Nine.AddMinutes(90));
        segment.Duration.Should().Be(TimeSpan.FromMinutes(90));
    }

    [Fact]
    public void TheSecondRunInAWindowStartsWhereTheFirstOneEnded()
    {
        var plan = RunWindowPlanner.Plan([Window("w1", 0, 3, 4)], [Run("a", 1), Run("b", 1)]);

        plan[0].Segments[0].Start.Should().Be(Nine);
        plan[0].Segments[0].End.Should().Be(Nine.AddMinutes(45));
        plan[1].Segments[0].Start.Should().Be(Nine.AddMinutes(45));
        plan[1].Segments[0].End.Should().Be(Nine.AddMinutes(90));
    }

    [Fact]
    public void TheCommittedLoadShiftsTheSliceLaterInTheWindow()
    {
        var plan = RunWindowPlanner.Plan([Window("w1", 0, 4, 4, used: 2)], [Run("r1", 1)]);

        plan[0].Segments[0].Start.Should().Be(Nine.AddHours(2), "half the window is already taken");
        plan[0].Segments[0].End.Should().Be(Nine.AddHours(3));
    }

    [Fact]
    public void TheWindowsInAPlanAreListedAndLookedUpById()
    {
        var plan = RunWindowPlanner.Plan(
            [Window("w1", 0, 3, 2), Window("w2", 3, 3, 2)],
            [Run("r1", 3)]);

        plan[0].Windows.Select(window => window.Id).Should().Equal("w1", "w2");
        plan[0].SegmentIn("w1")!.Units.Should().Be(2);
        plan[0].SegmentIn("w2")!.Units.Should().Be(1);
        plan[0].SegmentIn(null).Should().BeNull();
    }

    [Fact]
    public void TheInputsAreNeverTouched()
    {
        var windows = new List<RunWindow> { Window("w2", 3, 3, 4), Window("w1", 0, 3, 4) };
        var runs = new List<ScheduledRun> { Run("b", 2), Run("a", 2) };

        RunWindowPlanner.Plan(windows, runs);

        windows.Select(window => window.Id).Should().Equal("w2", "w1");
        runs.Select(run => run.Id).Should().Equal("b", "a");
    }
}
