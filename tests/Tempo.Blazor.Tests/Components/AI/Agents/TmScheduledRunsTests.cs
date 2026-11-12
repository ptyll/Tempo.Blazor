using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.AI.Agents;

/// <summary>
/// Plan d67b33af, Fáze 7 — <c>TmScheduledRuns</c>: runs placed into the windows a scheduler offers.
/// <para>
/// These facts pin the surface's promise: the windows in their own time order grouped by day, the plan the strategy
/// computed (a run split over windows, the gaps filled, the units that fit nowhere named), a capacity bar painted from
/// the SAME slices the rows show, an overloaded window highlighted with an icon and a word, the week/day/table switcher
/// (the table being the text alternative and carrying the same move menu), moving a run by drag or by its menu with the
/// reader's pick kept until the host supplies a new instance, and exactly ONE polite live region.
/// </para>
/// </summary>
public class TmScheduledRunsTests : LocalizationTestBase
{
    private static readonly DateTimeOffset Nine = new(2026, 11, 9, 9, 0, 0, TimeSpan.Zero);

    private static RunWindow Window(string id, int dayOffset, int hour, int hours = 3, int capacity = 4, int used = 0)
        => new(id, Nine.AddDays(dayOffset).AddHours(hour - 9), Nine.AddDays(dayOffset).AddHours(hour - 9 + hours),
            capacity, used);

    private static ScheduledRun Run(string id, string title, int units, ScheduledRunStatus status = ScheduledRunStatus.Planned,
        params string[] preferredWindows)
        => new(id, title)
        {
            EstimatedUnits = units,
            Status = status,
            Segments = [.. preferredWindows.Select(windowId => new RunSegment(windowId, Nine, Nine.AddHours(1), 1))],
        };

    private static IReadOnlyList<RunWindow> Windows() =>
    [
        Window("w-mon-am", 0, 9, capacity: 4),
        Window("w-mon-pm", 0, 13, capacity: 4),
        Window("w-tue-am", 1, 9, capacity: 4),
    ];

    private static IReadOnlyList<ScheduledRun> Runs() =>
    [
        Run("r1", "Nightly export", 4),
        Run("r2", "Invoice batch", 3, ScheduledRunStatus.Running),
        Run("r3", "Weekly report", 2, ScheduledRunStatus.Succeeded),
    ];

    private IRenderedComponent<TmScheduledRuns> RenderRuns(
        Action<ComponentParameterCollectionBuilder<TmScheduledRuns>>? configure = null,
        IReadOnlyList<RunWindow>? windows = null,
        IReadOnlyList<ScheduledRun>? runs = null)
        => Render<TmScheduledRuns>(p =>
        {
            p.Add(c => c.Windows, windows ?? Windows());
            p.Add(c => c.Runs, runs ?? Runs());
            configure?.Invoke(p);
        });

    private static IReadOnlyList<AngleSharp.Dom.IElement> WindowsOf(IRenderedComponent<TmScheduledRuns> cut)
        => [.. cut.FindAll(".tm-ai-runs__window")];

    private static string Announcement(IRenderedComponent<TmScheduledRuns> cut)
        => cut.Find("[role='status']").TextContent.Trim();

    private static string Units(int units) => units == 1 ? "1 unit" : $"{units} units";

    private static EventCallback<ScheduledRunMoveRequest> Capture(Action<ScheduledRunMoveRequest> sink)
        => EventCallback.Factory.Create<ScheduledRunMoveRequest>(new object(), sink);

    private static void OpenMenu(AngleSharp.Dom.IElement row)
        => row.QuerySelector(".tm-context-menu__trigger")!.Click();

    // ---------- structure ----------

    [Fact]
    public void TheBoardGroupsTheWindowsByDayInTheirOwnTimeOrder()
    {
        var cut = RenderRuns(windows: [Window("w-tue", 1, 9), Window("w-mon-pm", 0, 13), Window("w-mon-am", 0, 9)]);

        cut.FindAll(".tm-ai-runs__day").Should().HaveCount(2, "two days, not three windows");
        var days = cut.FindAll(".tm-ai-runs__day");
        // The windows of one day stay in time order.
        days[0].QuerySelectorAll(".tm-ai-runs__window").Select(window => window.GetAttribute("data-window-id"))
            .Should().Equal("w-mon-am", "w-mon-pm");
        days[1].QuerySelectorAll(".tm-ai-runs__window").Should().HaveCount(1);
    }

    [Fact]
    public void AWindowStatesItsCapacityAndTheRunsItHolds()
    {
        var cut = RenderRuns();

        var window = WindowsOf(cut)[0];
        window.QuerySelector("[data-testid='runs-window-capacity']")!.TextContent.Trim()
            .Should().Be("Units used: 0 of 4");
        window.QuerySelectorAll("[data-testid='runs-run']").Should().HaveCount(1);
        window.QuerySelector("[data-testid='runs-run-title']")!.TextContent.Trim().Should().Be("Nightly export");
    }

    [Fact]
    public void ThePlanSplitsARunAcrossWindowsAndEachSliceSaysWhichPartItIs()
    {
        var cut = RenderRuns(
            windows: [Window("w1", 0, 9, capacity: 4), Window("w2", 0, 13, capacity: 4)],
            runs: [Run("r1", "Long batch", 6)]);

        var windows = WindowsOf(cut);
        windows[0].QuerySelector("[data-testid='runs-run-units']")!.TextContent.Trim().Should().Be(Units(4));
        windows[1].QuerySelector("[data-testid='runs-run-units']")!.TextContent.Trim().Should().Be(Units(2));
        windows[0].QuerySelector("[data-testid='runs-run-part']")!.TextContent.Trim().Should().Be("Part 1 of 2");
        windows[1].QuerySelector("[data-testid='runs-run-part']")!.TextContent.Trim().Should().Be("Part 2 of 2");
    }

    [Fact]
    public void ARunThatFitsNowhereIsNamedInsteadOfOverflowingAWindow()
    {
        var cut = RenderRuns(
            windows: [Window("w1", 0, 9, capacity: 4)],
            runs: [Run("r1", "Huge batch", 10)]);

        WindowsOf(cut)[0].QuerySelector("[data-testid='runs-run-units']")!.TextContent.Trim().Should().Be(Units(4));

        cut.FindAll("[data-testid='runs-unplaced']").Should().BeEmpty();
        cut.Find("[data-testid='runs-window']").GetAttribute("data-overloaded").Should().Be("false");
    }

    [Fact]
    public void TheCapacityBarIsPaintedFromTheSameSlicesTheRowsShow()
    {
        var cut = RenderRuns(
            windows: [Window("w1", 0, 9, capacity: 4, used: 2)],
            runs: [Run("r1", "Batch", 2)]);

        var bar = WindowsOf(cut)[0].QuerySelector("[data-testid='runs-window-bar']")!;
        bar.GetAttribute("aria-hidden").Should().Be("true", "every number the bar draws is also a sentence");

        var committed = bar.QuerySelector(".tm-ai-runs__bar-committed")!;
        committed.GetAttribute("style").Should().Contain("--tm-runs-slice-size: 50%");

        var slice = bar.QuerySelector(".tm-ai-runs__bar-slice")!;
        slice.GetAttribute("data-run-id").Should().Be("r1");
        slice.GetAttribute("style").Should().Contain("--tm-runs-slice-start: 50%")
            .And.Contain("--tm-runs-slice-size: 50%")
            .And.Contain("var(--tm-agent-color-0)");
    }

    [Fact]
    public void AWindowTheHostOvercommittedIsOverloadedWithAnIconAndAWord()
    {
        var cut = RenderRuns(windows: [Window("w1", 0, 9, capacity: 2, used: 5)], runs: []);

        var window = WindowsOf(cut)[0];
        window.GetAttribute("data-overloaded").Should().Be("true");
        window.QuerySelector("[data-testid='runs-window-overload']")!.TextContent.Trim()
            .Should().Be("Overloaded by " + Units(3));
        window.QuerySelector(".tm-ai-runs__overload-icon").Should().NotBeNull("the state is never colour alone");
    }

    [Fact]
    public void AFullWindowAndAFreeWindowSaySo()
    {
        var cut = RenderRuns(
            windows: [Window("w1", 0, 9, capacity: 2, used: 2), Window("w2", 0, 13, capacity: 4, used: 1)],
            runs: []);

        WindowsOf(cut)[0].QuerySelector("[data-testid='runs-window-full']")!.TextContent.Trim().Should().Be("Full");
        WindowsOf(cut)[1].QuerySelector("[data-testid='runs-window-free']")!.TextContent.Trim()
            .Should().Be("Free: " + Units(3));
    }

    [Fact]
    public void TheSummaryCountsTheWindowsTheCapacityAndTheOverloads()
    {
        var cut = RenderRuns(
            windows: [Window("w1", 0, 9, capacity: 4, used: 5), Window("w2", 0, 13, capacity: 4)],
            runs: []);

        cut.Find(".tm-ai-runs__summary").TextContent.Trim().Should().Be("Windows: 2 · Capacity used: 63 % · Overloaded: 1");
    }

    [Fact]
    public void TheHeadingIsLocalizedAndItsLevelIsClamped()
    {
        var cut = RenderRuns(p => p.Add(c => c.HeadingLevel, 9));

        cut.Find("h6.tm-ai-runs__title").TextContent.Trim().Should().Be("Scheduled runs");
        cut.Find("h6.tm-ai-runs__day-title").Should().NotBeNull();
        cut.Find("h6.tm-ai-runs__window-title").Should().NotBeNull("a window heading never outranks its day");
        RenderRuns(p => p.Add(c => c.Title, "Own heading")).Find(".tm-ai-runs__title").TextContent.Trim()
            .Should().Be("Own heading");
    }

    [Fact]
    public void AnEmptySurfaceSaysSo()
    {
        RenderRuns(windows: [], runs: []).Find("[data-testid='runs-empty']").TextContent.Trim()
            .Should().Be("No run windows to schedule in.");
    }

    [Fact]
    public void AWindowWithNothingInItSaysSo()
    {
        var cut = RenderRuns(runs: []);

        cut.FindAll("[data-testid='runs-window-empty']").Should().HaveCount(3);
        cut.Find("[data-testid='runs-window-empty']").TextContent.Trim()
            .Should().Be("Nothing scheduled in this window.");
    }

    [Fact]
    public void ARunStatesItsStatusAsAIconAndAWord()
    {
        var cut = RenderRuns();

        var running = cut.Find("[data-testid='runs-run'][data-run-id='r2']");
        running.GetAttribute("data-status").Should().Be("running");
        running.QuerySelector(".tm-ai-runs__status-text")!.TextContent.Trim().Should().Be("Running");
        running.QuerySelector(".tm-ai-runs__status-icon").Should().NotBeNull();

        cut.Find("[data-testid='runs-run'][data-run-id='r3'] .tm-ai-runs__status-text").TextContent.Trim()
            .Should().Be("Succeeded");
    }

    // ---------- the views ----------

    [Fact]
    public void TheViewSwitcherOffersWeekDayAndTable()
    {
        var cut = RenderRuns();

        cut.Find("[data-testid='runs-view']").Should().NotBeNull();
        cut.FindAll("[data-testid='runs-view'] .tm-segmented__option, [data-testid='runs-view'] input")
            .Should().NotBeEmpty();
        cut.Find(".tm-ai-runs").GetAttribute("data-view").Should().Be("week");
    }

    [Fact]
    public async Task TheDayViewShowsOneDayAndStepsThroughTheRange()
    {
        var cut = RenderRuns();

        await cut.InvokeAsync(() => cut.Instance.OnViewChangedAsync(RunViewMode.Day));

        cut.Find(".tm-ai-runs").GetAttribute("data-view").Should().Be("day");
        cut.FindAll(".tm-ai-runs__day").Should().HaveCount(1);
        cut.FindAll(".tm-ai-runs__window").Should().HaveCount(2, "only Monday's windows");
        cut.Instance.CurrentDay.Should().Be(new DateOnly(2026, 11, 9));

        cut.Find("[data-testid='runs-current-day']").TextContent.Trim().Should()
            .Be(new DateOnly(2026, 11, 9).ToString("dddd d MMMM"));

        cut.Find("[data-testid='runs-day-previous']").GetAttribute("aria-disabled").Should().Be("true",
            "the first day has nothing before it");
        cut.Find("[data-testid='runs-day-next']").GetAttribute("aria-disabled").Should().BeNull();

        await cut.InvokeAsync(() => cut.Instance.OnDayChangedAsync(1));
        cut.Instance.CurrentDay.Should().Be(new DateOnly(2026, 11, 10));
        cut.FindAll(".tm-ai-runs__window").Should().HaveCount(1);
        cut.Find("[data-testid='runs-day-next']").GetAttribute("aria-disabled").Should().Be("true");
    }

    [Fact]
    public async Task TheDayChangeIsReportedToTheHost()
    {
        DateOnly? raised = null;
        var cut = RenderRuns(p =>
        {
            p.Add(c => c.ViewMode, RunViewMode.Day);
            p.Add(c => c.SelectedDayChanged, EventCallback.Factory.Create<DateOnly>(this, day => raised = day));
        });

        await cut.InvokeAsync(() => cut.Instance.OnDayChangedAsync(1));

        raised.Should().Be(new DateOnly(2026, 11, 10));
    }

    [Fact]
    public async Task TheTableIsTheTextAlternative_WithOneRowPerSliceAndTheSameMoveMenu()
    {
        // The "t"-formatted window range needs the suite's invariant baseline (HH:mm), not the ambient culture.
        using var _ = CultureScope.Use(CultureInfo.InvariantCulture);
        var cut = RenderRuns(
            windows: [Window("w1", 0, 9, capacity: 4), Window("w2", 0, 13, capacity: 4)],
            runs: [Run("r1", "Long batch", 6), Run("r2", "Short", 1)]);

        await cut.InvokeAsync(() => cut.Instance.OnViewChangedAsync(RunViewMode.Table));

        var table = cut.Find("[data-testid='runs-table']");
        table.QuerySelector("caption")!.TextContent.Trim().Should()
            .Be("Runs and the windows they are scheduled in");
        cut.FindAll("[data-testid='runs-table-row']").Should().HaveCount(3, "two slices of the long run and one of the short");

        var first = cut.Find("[data-testid='runs-table-row']");
        first.QuerySelector("[data-testid='runs-run-title']")!.TextContent.Should().Contain("Long batch");
        first.QuerySelector("[data-testid='runs-table-units']")!.TextContent.Trim().Should().Be(Units(4));
        first.QuerySelector("[data-testid='runs-table-window']")!.TextContent.Should().Contain("09:00");
        first.QuerySelectorAll(".tm-context-menu-wrapper").Should().HaveCount(1,
            "the table is as operable as the board");
    }

    [Fact]
    public async Task TheTableNamesARunThatFitsNowhere()
    {
        var cut = RenderRuns(
            windows: [Window("w1", 0, 9, capacity: 4)],
            runs: [Run("r1", "Huge batch", 10)]);

        await cut.InvokeAsync(() => cut.Instance.OnViewChangedAsync(RunViewMode.Table));

        cut.FindAll("[data-testid='runs-table-row']").Should().HaveCount(2, "a placed slice and the unplaced remainder");
        cut.FindAll(".tm-ai-runs__row--unplaced").Should().HaveCount(1);
        cut.Find(".tm-ai-runs__row--unplaced [data-testid='runs-table-window']")!.TextContent.Trim()
            .Should().Be("Not placed");
        cut.Find(".tm-ai-runs__row--unplaced [data-testid='runs-table-time']")!.TextContent.Trim()
            .Should().Be("Not placed: " + Units(6));
    }

    [Fact]
    public async Task TheViewChangeIsReportedAndTheReadersPickWinsUntilTheHostMovesTheParameter()
    {
        RunViewMode? raised = null;
        var cut = RenderRuns(p => p.Add(c => c.ViewModeChanged,
            EventCallback.Factory.Create<RunViewMode>(this, view => raised = view)));

        await cut.InvokeAsync(() => cut.Instance.OnViewChangedAsync(RunViewMode.Table));

        raised.Should().Be(RunViewMode.Table);
        cut.Find(".tm-ai-runs").GetAttribute("data-view").Should().Be("table");

        // The host re-renders the SAME parameter value: the reader's pick stands.
        cut.Render(p =>
        {
            p.Add(c => c.Windows, Windows());
            p.Add(c => c.Runs, Runs());
            p.Add(c => c.ViewMode, RunViewMode.Week);
        });
        cut.Find(".tm-ai-runs").GetAttribute("data-view").Should().Be("table");

        // A MOVED parameter is the host's word and takes over.
        cut.Render(p =>
        {
            p.Add(c => c.Windows, Windows());
            p.Add(c => c.Runs, Runs());
            p.Add(c => c.ViewMode, RunViewMode.Day);
        });
        cut.Find(".tm-ai-runs").GetAttribute("data-view").Should().Be("day");
    }

    [Fact]
    public void AnUnknownViewFallsBackToTheWeek()
    {
        var cut = RenderRuns(p => p.Add(c => c.ViewMode, (RunViewMode)42));

        cut.Find(".tm-ai-runs").GetAttribute("data-view").Should().Be("week");
    }

    // ---------- moving a run ----------

    [Fact]
    public async Task TheMoveMenuOffersEveryWindowAndNamesTheCurrentOne()
    {
        var cut = RenderRuns();

        OpenMenu(cut.Find("[data-testid='runs-run'][data-run-id='r1']"));
        var items = cut.FindAll(".tm-context-menu__item");

        items.Should().HaveCount(3);
        items[0].TextContent.Should().Contain("(current)");
        items[0].HasAttribute("disabled").Should().BeTrue();
        items[2].TextContent.Should().Contain("Move to");
    }

    [Fact]
    public void TheMoveMenuTriggerIsAMenuGlyph_NotASecondDragGrip()
    {
        var cut = RenderRuns();
        var row = cut.FindAll("[data-testid='runs-run']")[0];
        var item = row.Closest(".tm-sortable-list__item");

        // The row already carries the sortable module's drag grip. A second, identical grip that opens a MENU
        // would make the reader grab the wrong one, so the trigger advertises the menu instead.
        var trigger = row.QuerySelector("[data-testid='runs-run-move']")!;
        trigger.GetAttribute("data-icon").Should().Be("more-vertical")
            .And.NotBe("grip-vertical");

        var grip = item!.QuerySelector(".tm-sortable-list__handle svg");
        grip.Should().NotBeNull("the row keeps the shared drag handle");
        trigger.QuerySelector("svg")!.InnerHtml.Should().NotBe(grip!.InnerHtml,
            "two different controls must not paint the same glyph");
    }

    [Fact]
    public async Task MovingARunByItsMenuReportsItAndKeepsItWhereItWasDropped()
    {
        ScheduledRunMoveRequest? raised = null;
        var cut = RenderRuns(p => p.Add(c => c.OnMove, Capture(request => raised = request)));

        OpenMenu(cut.Find("[data-testid='runs-run'][data-run-id='r1']"));
        cut.FindAll(".tm-context-menu__item")[2].Click();
        cut.WaitForAssertion(() => raised.Should().NotBeNull(), TimeSpan.FromSeconds(10));

        raised!.Run.Id.Should().Be("r1");
        raised.From!.Id.Should().Be("w-mon-am", "the first run starts in the first window");
        raised.To.Id.Should().Be("w-tue-am");
        raised.WindowId.Should().Be("w-tue-am");
        raised.IsChange.Should().BeTrue();

        // The reader's pick is kept: the run now sits in the window it was moved to.
        cut.Find("[data-window-id='w-tue-am'] [data-testid='runs-run'][data-run-id='r1']").Should().NotBeNull();
        Announcement(cut).Should().Contain("moved to");
    }

    [Fact]
    public async Task ACommittedTransferMovesTheRunToTheTargetWindow()
    {
        ScheduledRunMoveRequest? raised = null;
        var cut = RenderRuns(p => p.Add(c => c.OnMove, Capture(request => raised = request)));

        await cut.InvokeAsync(() => cut.Instance.OnTransferAsync(
            new SortableTransferArgs("w-mon-am", 0, "w-tue-am", 0, SortableReorderSource.Pointer)));

        raised.Should().NotBeNull();
        raised!.Run.Id.Should().Be("r1");
        raised.From!.Id.Should().Be("w-mon-am");
        raised.To.Id.Should().Be("w-tue-am");
        cut.Find("[data-window-id='w-tue-am'] [data-testid='runs-run'][data-run-id='r1']").Should().NotBeNull();
    }

    [Fact]
    public async Task AMoveToAWindowWithNoRoomIsRefusedWithANoticeAndTheRunStaysPut()
    {
        ScheduledRunMoveRequest? raised = null;
        var cut = RenderRuns(
            p => p.Add(c => c.OnMove, Capture(request => raised = request)),
            windows:
            [
                Window("w-full", 0, 9, capacity: 2, used: 2),
                Window("w-free", 0, 13, capacity: 4),
            ],
            runs: [Run("r1", "Nightly export", 2)]);

        // The window the host already committed to the brim cannot take the run, even after a full re-plan.
        await cut.InvokeAsync(() => cut.Instance.OnTransferAsync(
            new SortableTransferArgs("w-free", 0, "w-full", 0, SortableReorderSource.Pointer)));

        raised.Should().BeNull("the surface never reports a placement the plan did not make");
        cut.Find("[data-window-id='w-free'] [data-testid='runs-run'][data-run-id='r1']").Should().NotBeNull(
            "the run stays where it was");
        cut.Find("[data-testid='runs-notice']").TextContent.Trim().Should().Contain("Not moved");
        Announcement(cut).Should().Be(cut.Find("[data-testid='runs-notice']").TextContent.Trim(),
            "the refusal is announced through the same one region");
    }

    [Fact]
    public async Task ATransferOfAnIndexOrWindowThatDoesNotExistChangesNothing()
    {
        ScheduledRunMoveRequest? raised = null;
        var cut = RenderRuns(p => p.Add(c => c.OnMove, Capture(request => raised = request)));

        await cut.InvokeAsync(() => cut.Instance.OnTransferAsync(
            new SortableTransferArgs("w-tue-am", 4, "w-mon-pm", 0, SortableReorderSource.Pointer)));
        await cut.InvokeAsync(() => cut.Instance.OnTransferAsync(
            new SortableTransferArgs("w-tue-am", 0, "ghost", 0, SortableReorderSource.Pointer)));
        await cut.InvokeAsync(() => cut.Instance.OnTransferAsync(null!));

        raised.Should().BeNull();
    }

    [Fact]
    public async Task TheReadersMoveSurvivesASameInstanceRerender_AndDropsOnANewOne()
    {
        var windows = Windows();
        var runs = new List<ScheduledRun> { Run("r1", "Nightly export", 2) };
        var cut = RenderRuns(windows: windows, runs: runs);

        await cut.InvokeAsync(() => cut.Instance.OnTransferAsync(
            new SortableTransferArgs("w-mon-am", 0, "w-tue-am", 0, SortableReorderSource.Pointer)));
        cut.Find("[data-window-id='w-tue-am'] [data-testid='runs-run'][data-run-id='r1']").Should().NotBeNull();

        // The host re-renders the SAME instance: the pick stands.
        cut.Render(p =>
        {
            p.Add(c => c.Windows, windows);
            p.Add(c => c.Runs, runs);
        });
        cut.Find("[data-window-id='w-tue-am'] [data-testid='runs-run'][data-run-id='r1']").Should().NotBeNull();

        // A NEW instance is the host's word (here it refused), so the surface follows it again.
        cut.Render(p =>
        {
            p.Add(c => c.Windows, windows);
            p.Add(c => c.Runs, new List<ScheduledRun> { new("r1", "Nightly export") { EstimatedUnits = 2 } });
        });
        cut.Find("[data-window-id='w-mon-am'] [data-testid='runs-run'][data-run-id='r1']").Should().NotBeNull();
    }

    [Fact]
    public void TheWindowListsShareOneDragGroup()
    {
        var cut = RenderRuns();

        cut.FindAll("[data-testid='runs-window-list']").Should().HaveCount(3);
        cut.FindAll("[data-testid='runs-window-list']").Select(list => list.GetAttribute("data-group"))
            .Distinct().Should().HaveCount(1, "every window of one surface is in the same drag group");
        cut.FindAll("[data-testid='runs-window-list']").Select(list => list.GetAttribute("data-list-id"))
            .Should().Equal("w-mon-am", "w-mon-pm", "w-tue-am");
        cut.FindAll(".tm-sortable-list__item").Should().OnlyContain(item => item.GetAttribute("tabindex") == null);
    }

    // ---------- moving a run with the keyboard (plan 7ba95ed6, Fáze 6) ----------

    /// <summary>The documented key set of a run row — the value <c>aria-keyshortcuts</c> must carry.</summary>
    private const string MoveKeys = "Alt+ArrowLeft Alt+ArrowRight Alt+ArrowUp Alt+ArrowDown";

    /// <summary>
    /// Drives the one entry point the package module calls (<c>OnRunMoveStep</c>): the module owns the chord
    /// (Alt + arrow, and the browser's Back/Forward suppression) and hands .NET the run id plus the accepted key, so the
    /// component's own contract starts here.
    /// </summary>
    private static Task MoveKey(IRenderedComponent<TmScheduledRuns> cut, string runId, string key)
        => cut.InvokeAsync(() => cut.Instance.OnRunMoveStepAsync(runId, key));

    [Fact]
    public void TheModuleInvokable_IsReachableUnderTheNameTheModuleCalls()
    {
        var method = typeof(TmScheduledRuns).GetMethod(nameof(TmScheduledRuns.OnRunMoveStepAsync));

        method.Should().NotBeNull();
        var attribute = method!.GetCustomAttributes(typeof(Microsoft.JSInterop.JSInvokableAttribute), inherit: false)
            .Cast<Microsoft.JSInterop.JSInvokableAttribute>()
            .Should().ContainSingle().Subject;
        attribute.Identifier.Should().Be("OnRunMoveStep",
            "tm-scheduled-runs.js invokes exactly this name, and Blazor never strips an Async suffix");
    }

    [Fact]
    public void ARunRowAdvertisesTheMoveKeys_AndKeepsTheMenu()
    {
        var cut = RenderRuns();

        var row = cut.Find("[data-testid='runs-run'][data-run-id='r1']");
        row.GetAttribute("aria-keyshortcuts").Should().Be(MoveKeys,
            "the shortcut is announced on the row it moves, in the ARIA key-shortcut grammar");
        row.GetAttribute("data-tm-run-move-keys").Should().NotBeNull(
            "the module's Alt+arrow guard needs to find the row that owns the keys");
        row.QuerySelector(".tm-context-menu__trigger").Should().NotBeNull(
            "the menu stays the alternative that lists every window");
    }

    [Fact]
    public async Task AltArrowRightMovesTheRunToTheNextWindow()
    {
        ScheduledRunMoveRequest? raised = null;
        var cut = RenderRuns(p => p.Add(c => c.OnMove, Capture(request => raised = request)));

        await MoveKey(cut, "r1", "ArrowRight");
        cut.WaitForAssertion(() => raised.Should().NotBeNull());

        raised!.Run.Id.Should().Be("r1");
        raised.From!.Id.Should().Be("w-mon-am");
        raised.To.Id.Should().Be("w-mon-pm");
        cut.Find("[data-window-id='w-mon-pm'] [data-testid='runs-run'][data-run-id='r1']").Should().NotBeNull(
            "the run visibly sits in the next window");
        Announcement(cut).Should().Contain("moved to", "every keyboard move is announced");
    }

    [Fact]
    public async Task AltArrowLeftMovesTheRunToThePreviousWindow()
    {
        ScheduledRunMoveRequest? raised = null;
        var cut = RenderRuns(p => p.Add(c => c.OnMove, Capture(request => raised = request)));

        // r1 starts in the first window, so move it forward first and then step back with the keyboard.
        await cut.InvokeAsync(() => cut.Instance.OnTransferAsync(
            new SortableTransferArgs("w-mon-am", 0, "w-mon-pm", 0, SortableReorderSource.Pointer)));
        raised = null;

        await MoveKey(cut, "r1", "ArrowLeft");
        cut.WaitForAssertion(() => raised.Should().NotBeNull());

        raised!.From!.Id.Should().Be("w-mon-pm");
        raised.To.Id.Should().Be("w-mon-am");
    }

    [Fact]
    public async Task AltArrowLeftOnTheFirstWindowAnnouncesTheBoundary_AndMovesNothing()
    {
        ScheduledRunMoveRequest? raised = null;
        var cut = RenderRuns(p => p.Add(c => c.OnMove, Capture(request => raised = request)));

        await MoveKey(cut, "r1", "ArrowLeft");

        raised.Should().BeNull("there is no window before the first one");
        Announcement(cut).Should().Be("Nightly export is already in the first window.");
        cut.Find("[data-window-id='w-mon-am'] [data-testid='runs-run'][data-run-id='r1']").Should().NotBeNull();
    }

    [Fact]
    public async Task AltArrowRightOnTheLastWindowAnnouncesTheBoundary_AndMovesNothing()
    {
        ScheduledRunMoveRequest? raised = null;
        var cut = RenderRuns(
            p => p.Add(c => c.OnMove, Capture(request => raised = request)),
            windows: [Window("w-mon-am", 0, 9, capacity: 4)],
            runs: [Run("r1", "Nightly export", 2)]);

        await MoveKey(cut, "r1", "ArrowRight");

        raised.Should().BeNull();
        Announcement(cut).Should().Be("Nightly export is already in the last window.");
    }

    [Fact]
    public async Task AltArrowDownMovesTheRunToTheNextDay_AndUpBackAgain()
    {
        ScheduledRunMoveRequest? raised = null;
        var cut = RenderRuns(p => p.Add(c => c.OnMove, Capture(request => raised = request)));

        await MoveKey(cut, "r1", "ArrowDown");
        cut.WaitForAssertion(() => raised.Should().NotBeNull());
        raised!.To.Id.Should().Be("w-tue-am", "the next day is entered at its FIRST window");
        cut.Find("[data-window-id='w-tue-am'] [data-testid='runs-run'][data-run-id='r1']").Should().NotBeNull();

        raised = null;
        await MoveKey(cut, "r1", "ArrowUp");
        cut.WaitForAssertion(() => raised.Should().NotBeNull());
        raised!.To.Day.Should().Be(new DateOnly(2026, 11, 9), "the previous day is a Monday");
    }

    [Fact]
    public async Task AltArrowUpOnTheFirstDayAnnouncesTheBoundary_AndMovesNothing()
    {
        ScheduledRunMoveRequest? raised = null;
        var cut = RenderRuns(p => p.Add(c => c.OnMove, Capture(request => raised = request)));

        await MoveKey(cut, "r1", "ArrowUp");

        raised.Should().BeNull();
        Announcement(cut).Should().Be("Nightly export is already on the first day.");
    }

    [Fact]
    public async Task AltArrowDownOnTheLastDayAnnouncesTheBoundary_AndMovesNothing()
    {
        ScheduledRunMoveRequest? raised = null;
        var cut = RenderRuns(
            p => p.Add(c => c.OnMove, Capture(request => raised = request)),
            windows: [Window("w-mon-am", 0, 9, capacity: 4)],
            runs: [Run("r1", "Nightly export", 2)]);

        await MoveKey(cut, "r1", "ArrowDown");

        raised.Should().BeNull();
        Announcement(cut).Should().Be("Nightly export is already on the last day.");
    }

    [Fact]
    public async Task AltArrowRightIntoAFullWindowIsRefusedWithANoticeAndAnnouncement()
    {
        ScheduledRunMoveRequest? raised = null;
        var cut = RenderRuns(
            p => p.Add(c => c.OnMove, Capture(request => raised = request)),
            windows:
            [
                Window("w-mon-am", 0, 9, capacity: 4),
                Window("w-full", 0, 13, capacity: 2, used: 2),
            ],
            runs: [Run("r1", "Nightly export", 2)]);

        await MoveKey(cut, "r1", "ArrowRight");

        raised.Should().BeNull("the surface never reports a placement the plan did not make");
        cut.Find("[data-window-id='w-mon-am'] [data-testid='runs-run'][data-run-id='r1']").Should().NotBeNull(
            "the run stays where it was");
        var notice = cut.Find("[data-testid='runs-notice']").TextContent.Trim();
        notice.Should().Contain("Not moved");
        Announcement(cut).Should().Be(notice, "the refusal is announced through the same one region");
    }

    [Fact]
    public async Task AKeyOrRunTheSurfaceDoesNotOwnChangesNothing()
    {
        ScheduledRunMoveRequest? raised = null;
        var cut = RenderRuns(p => p.Add(c => c.OnMove, Capture(request => raised = request)));

        // The module already filters the modifiers and the four arrows; the component maps exactly those four keys and
        // ignores a key or a run id it does not know (a stale row, a renamed run).
        await MoveKey(cut, "r1", "PageDown");
        await MoveKey(cut, "r1", "Enter");
        await MoveKey(cut, "ghost", "ArrowRight");
        await MoveKey(cut, "r1", "");

        raised.Should().BeNull("only the documented Alt+arrow chord moves a run");
        Announcement(cut).Should().BeEmpty("a key the surface does not own says nothing");
    }

    [Fact]
    public async Task TheTableRowOffersTheSameKeyboardMove()
    {
        ScheduledRunMoveRequest? raised = null;
        var cut = RenderRuns(p => p.Add(c => c.OnMove, Capture(request => raised = request)));

        await cut.InvokeAsync(() => cut.Instance.OnViewChangedAsync(RunViewMode.Table));

        var row = cut.Find($"[data-testid='runs-table-row'][data-run-id='r1']");
        row.GetAttribute("aria-keyshortcuts").Should().Be(MoveKeys);
        row.GetAttribute("data-tm-run-move-keys").Should().NotBeNull(
            "the module finds the row by this attribute in the table presentation too");

        await MoveKey(cut, "r1", "ArrowRight");
        cut.WaitForAssertion(() => raised.Should().NotBeNull());
        raised!.To.Id.Should().Be("w-mon-pm", "the table is as operable as the board");
    }

    // ---------- accessibility and localization ----------

    [Fact]
    public void TheSurfaceOwnsExactlyOnePoliteLiveRegion()
    {
        var cut = RenderRuns();

        var regions = cut.FindAll("[aria-live]");
        regions.Should().HaveCount(1, "the window lists are transfer-only, so they render no region of their own");
        regions[0].GetAttribute("role").Should().Be("status");
        regions[0].GetAttribute("aria-live").Should().Be("polite");
    }

    [Fact]
    public void TheInstructionsAreVisibleAndDescribedByTheBoard()
    {
        var cut = RenderRuns();

        var instructions = cut.Find(".tm-ai-runs__instructions");
        instructions.TextContent.Trim().Should().NotBeNullOrEmpty();
        cut.Find(".tm-ai-runs__board").GetAttribute("aria-describedby").Should().Be(instructions.Id);
    }

    [Fact]
    public void NoUserVisibleStringIsHardCoded()
    {
        Services.AddSingleton<Tempo.Blazor.Localization.ITmLocalizer>(BuildCzechLocalizer());
        var cut = RenderRuns();

        cut.Find(".tm-ai-runs__title").TextContent.Trim().Should().Be("Naplánované běhy");
        cut.Find(".tm-ai-runs__instructions").TextContent.Should().NotContain("Move a run");
        cut.Find("[data-testid='runs-run'][data-run-id='r1'] .tm-ai-runs__status-text").TextContent.Trim()
            .Should().Be("Naplánováno");
    }
}
