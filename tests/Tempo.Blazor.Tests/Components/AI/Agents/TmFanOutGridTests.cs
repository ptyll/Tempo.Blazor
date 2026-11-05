using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.AI.Agents;

/// <summary>
/// Plan d67b33af, Fáze 4 — <c>TmFanOutGrid</c> (Tempo.Blazor.AI): one orchestrator splitting work across N workers, as
/// a grid of tiles with a summary above it.
/// <para>
/// The tests read behaviour, not markup: the surface is a labelled <c>role="grid"</c> whose tiles are native buttons
/// with ONE roving tab stop and the grid keys (arrows, Home/End, Ctrl+Home/End, PageUp/PageDown), a state is painted as
/// an icon AND a word AND a tone, the retry badge counts the attempts, the estimate is a LIVE countdown on the injected
/// clock, the detail follows the focused (or hovered) tile and carries the retry/cancel actions, the state filter
/// narrows the grid (with an announcement and a moved focus), and a few hundred tasks render a bounded window of tiles
/// while the grid still declares its full row count.
/// </para>
/// </summary>
public class TmFanOutGridTests : LocalizationTestBase
{
    private const string FocusInvocation = "Blazor._internal.domWrapper.focus";

    private static readonly DateTimeOffset Ten = new(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock;
    private string? _retriedId;
    private string? _cancelledId;
    private FanOutFilter? _filter;
    private int _retryCount;
    private int _cancelCount;

    public TmFanOutGridTests()
    {
        _clock = new FakeTimeProvider(Ten);
        Services.AddSingleton<TimeProvider>(_clock);
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────

    private static FanOutTask Task(
        string id,
        AgentActivityStatus status = AgentActivityStatus.Running,
        int attempt = 1,
        int? maxAttempts = null,
        long? cost = null,
        DateTimeOffset? started = null,
        DateTimeOffset? estimate = null,
        string? error = null,
        AgentRef? worker = null,
        string? label = null)
        => new(id, label ?? "Region " + id)
        {
            Status = status,
            Attempt = attempt,
            MaxAttempts = maxAttempts,
            CostMicroUnits = cost,
            StartedAt = started ?? Ten.AddMinutes(-1),
            EstimatedEndAt = estimate,
            Error = error,
            Worker = worker,
        };

    /// <summary>The fan-out of these tests: four running, two done, one failed (retryable) and one queued.</summary>
    private static IReadOnlyList<FanOutTask> Tasks() =>
    [
        Task("a", AgentActivityStatus.Running, cost: 120_000, estimate: Ten.AddSeconds(42),
            worker: new AgentRef("Fetcher", "w1") { ColorIndex = 1 }),
        Task("b", AgentActivityStatus.Running, estimate: Ten.AddSeconds(90)),
        Task("c", AgentActivityStatus.Running, attempt: 2, maxAttempts: 3),
        Task("d", AgentActivityStatus.Running),
        Task("e", AgentActivityStatus.Succeeded, cost: 500_000),
        Task("f", AgentActivityStatus.Succeeded),
        Task("g", AgentActivityStatus.Failed, attempt: 1, maxAttempts: 3, error: "Provider rate limit"),
        Task("h", AgentActivityStatus.Queued),
    ];

    private IRenderedComponent<TmFanOutGrid> RenderGrid(
        IReadOnlyList<FanOutTask>? tasks = null,
        string? title = null,
        bool showTitle = true,
        int headingLevel = 3,
        bool showSummary = true,
        bool showFilter = true,
        int columns = 4,
        bool compact = false,
        int visibleTaskLimit = 60,
        FanOutFilter? filter = null)
    {
        _retriedId = null;
        _cancelledId = null;
        _filter = null;
        _retryCount = 0;
        _cancelCount = 0;

        return Render<TmFanOutGrid>(parameters =>
        {
            parameters.Add(component => component.Tasks, tasks ?? Tasks());
            parameters.Add(component => component.Culture, CultureInfo.GetCultureInfo("en-US"));
            parameters.Add(component => component.TimeProvider, _clock);
            parameters.Add(component => component.ShowTitle, showTitle);
            parameters.Add(component => component.HeadingLevel, headingLevel);
            parameters.Add(component => component.ShowSummary, showSummary);
            parameters.Add(component => component.ShowFilter, showFilter);
            parameters.Add(component => component.Columns, columns);
            parameters.Add(component => component.Compact, compact);
            parameters.Add(component => component.VisibleTaskLimit, visibleTaskLimit);
            parameters.Add(component => component.OnRetry, (FanOutTask task) =>
            {
                _retriedId = task.Id;
                _retryCount++;
            });
            parameters.Add(component => component.OnCancel, (FanOutTask task) =>
            {
                _cancelledId = task.Id;
                _cancelCount++;
            });
            parameters.Add(component => component.FilterChanged, (FanOutFilter next) => _filter = next);

            if (title is not null)
            {
                parameters.Add(component => component.Title, title);
            }

            if (filter is not null)
            {
                parameters.Add(component => component.Filter, filter.Value);
            }
        });
    }

    private static Task UpdateAsync(IRenderedComponent<TmFanOutGrid> cut, params (string Name, object? Value)[] values)
        => cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            values.ToDictionary(value => value.Name, value => value.Value))));

    private static IReadOnlyList<AngleSharp.Dom.IElement> Tiles(IRenderedComponent<TmFanOutGrid> cut)
        => cut.FindAll("[data-testid='fan-out-tile']");

    private static AngleSharp.Dom.IElement Tile(IRenderedComponent<TmFanOutGrid> cut, string id)
        => cut.Find($"[data-testid='fan-out-tile'][data-task-id='{id}']");

    private static string Announcement(IRenderedComponent<TmFanOutGrid> cut)
        => cut.Find("[data-testid='fan-out-announcement']").TextContent;

    private static string Summary(IRenderedComponent<TmFanOutGrid> cut, string id)
        => cut.Find($"[data-testid='fan-out-summary-{id}']").TextContent.Trim();

    private List<string?> FocusTargets()
        => JSInterop.Invocations
            .Where(invocation => invocation.Identifier == FocusInvocation)
            .Select(invocation => (invocation.Arguments[0] as ElementReference?)?.Id)
            .ToList();

    /// <summary>
    /// The serialized element reference of every mounted tile, captured BEFORE an interaction: bUnit consumes the
    /// <c>blazor:elementreference</c> attribute when the component starts an element reference of its own, so a
    /// reference read after a focus move is already gone.
    /// </summary>
    private static IReadOnlyDictionary<string, string?> References(IRenderedComponent<TmFanOutGrid> cut)
        => Tiles(cut).ToDictionary(
            tile => tile.GetAttribute("data-task-id")!,
            tile => tile.GetAttribute("blazor:elementreference"),
            StringComparer.Ordinal);

    // ── The header ───────────────────────────────────────────────────────────

    [Fact]
    public void TheDefaultTitle_IsLocalized_AndSitsAtTheRequestedLevel()
    {
        var cut = RenderGrid();

        var title = cut.Find("[data-testid='fan-out-title']");
        title.TextContent.Should().Be("Fan-out");
        title.TagName.Should().Be("H3");

        RenderGrid(headingLevel: 2).Find("[data-testid='fan-out-title']").TagName.Should().Be("H2");
        RenderGrid(headingLevel: 42).Find("[data-testid='fan-out-title']").TagName.Should().Be("H6",
            "the level is clamped into the range a sectioning element allows");
    }

    [Fact]
    public void TheHostsTitle_Wins_AndShowTitleHidesIt()
    {
        RenderGrid(title: "Reconcile 40 files").Find("[data-testid='fan-out-title']").TextContent
            .Should().Be("Reconcile 40 files");

        RenderGrid(showTitle: false).FindAll("[data-testid='fan-out-title']").Should().BeEmpty();
    }

    [Fact]
    public void TheSummaryCountsDoneRunningAndFailed_TogetherWithTheCostAndTheEstimate()
    {
        var cut = RenderGrid();

        cut.Find("[data-testid='fan-out-summary']").GetAttribute("aria-label").Should().Be("Run summary");
        Summary(cut, "succeeded").Should().Be("2");
        Summary(cut, "running").Should().Be("4");
        Summary(cut, "failed").Should().Be("1");
        Summary(cut, "tasks").Should().Be("3/8", "three of the eight units of work reached a terminal state");
        Summary(cut, "cost").Should().Be("$0.62", "the priced tasks are summed in the culture's own currency");
        Summary(cut, "eta").Should().Be("1:30",
            "the LATEST estimate among the ACTIVE tasks, counted down on the injected clock");
    }

    [Fact]
    public void TheSummaryHidesTheMoneyLine_WhenNothingIsPriced()
    {
        var cut = RenderGrid([Task("a"), Task("b", AgentActivityStatus.Succeeded)]);

        cut.FindAll("[data-testid='fan-out-summary-cost']").Should().BeEmpty(
            "an unpriced fan-out is not a free one");
        Summary(cut, "eta").Should().Be("No estimate");
    }

    [Fact]
    public void AFinishedFanOut_SaysSo_InsteadOfCountingDown()
    {
        var cut = RenderGrid(
        [
            Task("a", AgentActivityStatus.Succeeded, cost: 1_000_000, estimate: Ten.AddMinutes(5)),
            Task("b", AgentActivityStatus.Failed, error: "boom"),
        ]);

        Summary(cut, "eta").Should().Be("Finished");
        Summary(cut, "tasks").Should().Be("2/2");
    }

    [Fact]
    public void TheSummaryCanBeTurnedOff()
    {
        RenderGrid(showSummary: false).FindAll("[data-testid='fan-out-summary']").Should().BeEmpty();
    }

    // ── The grid ─────────────────────────────────────────────────────────────

    [Fact]
    public void TheSurface_IsALabelledGrid_WithRowsAndCellsThatDeclareTheirPosition()
    {
        var cut = RenderGrid(tasks: [Task("a"), Task("b"), Task("c"), Task("d"), Task("e")], columns: 2);

        var grid = cut.Find("[data-testid='fan-out-grid-body']");
        grid.GetAttribute("role").Should().Be("grid");
        grid.GetAttribute("aria-label").Should().Be("Fan-out tasks");
        grid.GetAttribute("aria-rowcount").Should().Be("3", "five tiles over two columns are three rows");
        grid.GetAttribute("aria-colcount").Should().Be("2");
        grid.GetAttribute("aria-describedby").Should().Be(
            cut.Find("[data-testid='fan-out-instructions']").Id,
            "the grid is described by its instructions exactly once");

        var rows = cut.FindAll("[data-testid='fan-out-row']");
        rows.Should().HaveCount(3);
        rows.Select(row => row.GetAttribute("aria-rowindex")).Should().Equal(new[] { "1", "2", "3" },
            "a windowed grid still declares where each mounted row really sits");

        cut.FindAll("[data-testid='fan-out-cell']").Select(cell => cell.GetAttribute("aria-colindex"))
            .Should().Equal(new[] { "1", "2", "1", "2", "1" });
    }

    [Fact]
    public void TheWholeGrid_IsOneTabStop()
    {
        var cut = RenderGrid();

        var tabbable = cut.FindAll("[data-testid='fan-out-grid-body'] [tabindex='0']");
        tabbable.Should().ContainSingle("the roving tabindex is the grid's only stop");
        tabbable[0].GetAttribute("data-task-id").Should().Be("a", "the first tile holds it until the reader moves");
    }

    [Fact]
    public void EveryTile_IsANativeButton_WhoseNameStartsWithItsOwnLabel()
    {
        var cut = RenderGrid();

        var tile = Tile(cut, "a");
        tile.TagName.Should().Be("BUTTON");
        tile.GetAttribute("type").Should().Be("button");

        var name = tile.GetAttribute("aria-label")!;
        name.Should().StartWith("Region a", "a reader who says what they see must match the accessible name");
        name.Should().Contain("Running");
        name.Should().Contain("ends in 0:42");
        name.Should().Contain("cost $0.12");
        name.Should().NotContain("attempt",
            "a first attempt of an unlimited task has nothing to add");
    }

    [Fact]
    public void AState_IsPaintedAsAToneAnIconAndAWord()
    {
        var cut = RenderGrid();

        var failed = Tile(cut, "g");
        failed.GetAttribute("data-status").Should().Be("failed", "the stable hook a host styles against");
        failed.ClassList.Should().Contain("tm-ai-fan-out__tile--failed");
        failed.QuerySelector(".tm-ai-fan-out__tile-state-icon svg").Should()
            .NotBeNull("the state is never carried by colour alone");
        failed.QuerySelector(".tm-ai-fan-out__tile-state-text")!.TextContent.Trim().Should().Be("Failed");

        Tile(cut, "e").GetAttribute("data-status").Should().Be("succeeded");
        Tile(cut, "e").ClassList.Should().Contain("tm-ai-fan-out__tile--succeeded");
    }

    [Fact]
    public void TheRetryBadge_CountsTheAttempts()
    {
        var cut = RenderGrid();

        var retried = Tile(cut, "c");
        retried.QuerySelector(".tm-ai-fan-out__badge")!.TextContent.Trim().Should().Be("Attempt 2/3");

        Tile(cut, "a").QuerySelectorAll(".tm-ai-fan-out__badge").Should().BeEmpty(
            "a first attempt of an unlimited task has nothing to badge");
    }

    [Fact]
    public void AnAttemptWithNoLimit_StillBadgesASecondAttempt()
    {
        var cut = RenderGrid([Task("a", attempt: 3)]);

        Tile(cut, "a").QuerySelector(".tm-ai-fan-out__badge")!.TextContent.Trim().Should().Be("Attempt 3");
    }

    [Fact]
    public void ATileWithoutALabel_FallsBackToTheLocalizedPlaceholder()
    {
        var cut = RenderGrid([Task("a", label: "  ")]);

        Tile(cut, "a").QuerySelector(".tm-ai-fan-out__tile-label")!.TextContent.Trim()
            .Should().Be("Untitled task");
        Tile(cut, "a").GetAttribute("aria-label").Should().StartWith("Untitled task");
    }

    [Fact]
    public void AnEmptyList_RendersTheLocalizedEmptyState_AndNoGrid()
    {
        var cut = RenderGrid([]);

        cut.Find("[data-testid='fan-out-empty']").TextContent.Should().Contain("No fan-out tasks");
        cut.FindAll("[data-testid='fan-out-grid-body']").Should().BeEmpty();
        cut.FindAll("[data-testid='fan-out-detail']").Should().BeEmpty();
    }

    // ── The detail ───────────────────────────────────────────────────────────

    [Fact]
    public void TheDetailAppearsOnFocus_AndFollowsTheHover()
    {
        var cut = RenderGrid();

        cut.FindAll("[data-testid='fan-out-detail']").Should().BeEmpty(
            "nothing has been read yet, so there is no detail to re-state");

        Tile(cut, "a").Focus();

        var detail = cut.Find("[data-testid='fan-out-detail']");
        detail.GetAttribute("role").Should().Be("region");
        detail.GetAttribute("aria-label").Should().Be("Task detail");
        detail.GetAttribute("data-task-id").Should().Be("a");
        detail.TextContent.Should().Contain("Region a");
        detail.TextContent.Should().Contain("Fetcher", "the worker is named when the host identifies it");

        Tile(cut, "g").MouseOver();

        cut.Find("[data-testid='fan-out-detail']").GetAttribute("data-task-id").Should().Be("g");
        cut.Find("[data-testid='fan-out-detail']").TextContent.Should().Contain("Provider rate limit",
            "the failure reason is part of the detail, not something the reader has to guess");
    }

    [Fact]
    public void TheDetailTitle_SitsOneLevelBelowTheHeading_AndNeverSkipsALevel()
    {
        static string DetailTitleLevel(IRenderedComponent<TmFanOutGrid> cut)
            => cut.Find("[data-testid='fan-out-detail-title']").TagName;

        var atThree = RenderGrid(headingLevel: 3);
        Tile(atThree, "a").Focus();
        DetailTitleLevel(atThree).Should().Be("H4", "the detail is a section of the surface heading");

        var atTwo = RenderGrid(headingLevel: 2);
        Tile(atTwo, "a").Focus();
        DetailTitleLevel(atTwo).Should().Be("H3", "a surface rendered at h2 must not jump straight to h4");

        var atSix = RenderGrid(headingLevel: 6);
        Tile(atSix, "a").Focus();
        DetailTitleLevel(atSix).Should().Be("H6", "there is no h7, so the level caps at 6");
    }

    [Fact]
    public void ARefresh_KeepsTheDetailOnTheSameTile()
    {
        var cut = RenderGrid();

        Tile(cut, "b").Focus();
        Tile(cut, "b").MouseOver();

        cut.Render();

        cut.Find("[data-testid='fan-out-detail']").GetAttribute("data-task-id").Should().Be("b");
    }

    [Fact]
    public void TheDetailOffersRetryOnlyWhileAttemptsRemain_AndCancelOnlyWhileWorkHoldsAWorker()
    {
        var cut = RenderGrid();

        Tile(cut, "g").Focus();
        cut.FindAll("[data-testid='fan-out-detail-retry']").Should().ContainSingle(
            "a failed task with attempts left can be retried");
        cut.FindAll("[data-testid='fan-out-detail-cancel']").Should().BeEmpty("nothing is running to cancel");
    }

    [Fact]
    public void AnExhaustedTask_OffersNoRetry()
    {
        var cut = RenderGrid([Task("a", AgentActivityStatus.Failed, attempt: 3, maxAttempts: 3, error: "boom")]);

        Tile(cut, "a").Focus();

        cut.FindAll("[data-testid='fan-out-detail-retry']").Should().BeEmpty(
            "the host's attempt limit is the limit");
        cut.Find("[data-testid='fan-out-detail']").TextContent.Should().Contain("boom");
    }

    [Fact]
    public void ARunningTask_OffersACancel_WhichRaisesTheCallbackAndAnnouncesIt()
    {
        var cut = RenderGrid();

        Tile(cut, "d").Focus();
        cut.Find("[data-testid='fan-out-detail-cancel']").Click();

        _cancelledId.Should().Be("d");
        _cancelCount.Should().Be(1);
        Announcement(cut).Should().Be("Cancel requested for Region d.");
    }

    [Fact]
    public void RetryRaisesTheCallbackWithTheWholeTask_AndAnnouncesIt()
    {
        var cut = RenderGrid();

        Tile(cut, "g").Focus();
        cut.Find("[data-testid='fan-out-detail-retry']").Click();

        _retriedId.Should().Be("g");
        _retryCount.Should().Be(1);
        Announcement(cut).Should().Be("Retry requested for Region g.");
    }

    [Fact]
    public void TheRetryButton_IsALabelledNativeControl()
    {
        var cut = RenderGrid();

        Tile(cut, "g").Focus();
        var retry = cut.Find("[data-testid='fan-out-detail-retry']");

        retry.TagName.Should().Be("BUTTON");
        retry.TextContent.Trim().Should().Be("Retry");
    }

    // ── The filter ───────────────────────────────────────────────────────────

    [Fact]
    public void TheFilterIsALabelledGroupOfPressedButtons_WithTheCounts()
    {
        var cut = RenderGrid();

        var filters = cut.Find("[data-testid='fan-out-filters']");
        filters.GetAttribute("role").Should().Be("group");
        filters.GetAttribute("aria-label").Should().Be("Filter by state");

        var buttons = cut.FindAll("[data-testid='fan-out-filter']");
        buttons.Select(button => button.GetAttribute("data-filter")).Should()
            .Equal(new[] { "all", "queued", "running", "approval", "succeeded", "failed", "cancelled" });
        buttons[0].GetAttribute("aria-pressed").Should().Be("true", "the active filter is a pressed toggle");
        buttons[0].TextContent.Should().Contain("All");
        cut.Find("[data-testid='fan-out-filter'][data-filter='failed']").TextContent.Should().Contain("1");
    }

    [Fact]
    public void PickingAFilter_NarrowsTheGrid_RaisesTheBindingAndAnnouncesTheCount()
    {
        var cut = RenderGrid();

        cut.Find("[data-testid='fan-out-filter'][data-filter='failed']").Click();

        _filter.Should().Be(FanOutFilter.Failed);
        Tiles(cut).Select(tile => tile.GetAttribute("data-task-id")).Should().Equal(new[] { "g" });
        cut.Find("[data-testid='fan-out-grid-body']").GetAttribute("aria-rowcount").Should().Be("1");
        Announcement(cut).Should().Be("Showing 1 of 8 tasks.");
    }

    [Fact]
    public void AFilterFromTheHost_IsApplied_WithoutRaisingTheCallback()
    {
        var cut = RenderGrid(filter: FanOutFilter.Succeeded);

        Tiles(cut).Select(tile => tile.GetAttribute("data-task-id")).Should().Equal(new[] { "e", "f" });
        cut.Find("[data-testid='fan-out-filter'][data-filter='succeeded']").GetAttribute("aria-pressed")
            .Should().Be("true");
        _filter.Should().BeNull("the host already knows what it bound");
    }

    [Fact]
    public void AFilterThatMatchesNothing_SaysSoInsteadOfRenderingAnEmptyGrid()
    {
        var cut = RenderGrid([Task("a")], filter: FanOutFilter.Cancelled);

        cut.Find("[data-testid='fan-out-empty']").TextContent.Should().Contain("No task matches this filter");
        cut.FindAll("[data-testid='fan-out-grid-body']").Should().BeEmpty();
    }

    [Fact]
    public void TheFilterCanBeTurnedOff()
    {
        RenderGrid(showFilter: false).FindAll("[data-testid='fan-out-filters']").Should().BeEmpty();
    }

    [Fact]
    public void WhenTheFocusedTileIsFilteredAway_TheFocusMovesToTheFirstTile_AndTheReaderIsTold()
    {
        var cut = RenderGrid();

        Tile(cut, "h").Focus();
        cut.Find("[data-testid='fan-out-filter'][data-filter='failed']").Click();

        cut.Find("[data-testid='fan-out-grid-body'] [tabindex='0']").GetAttribute("data-task-id").Should().Be("g",
            "the roving stop must never point at a tile that is no longer mounted");
        cut.FindAll("[data-testid='fan-out-detail']").Should().BeEmpty(
            "the detail cannot outlive the tile it described");
        FocusTargets().Should().BeEmpty(
            "the reader is on the filter button: pulling the focus into the grid would make the next filter harder");
        Announcement(cut).Should().Contain("Showing 1 of 8 tasks");
    }

    // ── Keyboard ─────────────────────────────────────────────────────────────

    [Fact]
    public void TheArrowKeys_WalkTheGrid_InReadingOrder()
    {
        var cut = RenderGrid(tasks: [Task("a"), Task("b"), Task("c"), Task("d"), Task("e"), Task("f")], columns: 3);
        var refs = References(cut);

        Tile(cut, "a").Focus();
        Tile(cut, "a").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        FocusTargets()[^1].Should().Be(refs["b"]);

        Tile(cut, "b").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        FocusTargets()[^1].Should().Be(refs["e"], "one row down is one full row of columns");

        Tile(cut, "e").KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });
        FocusTargets()[^1].Should().Be(refs["b"]);

        Tile(cut, "b").KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });
        FocusTargets()[^1].Should().Be(refs["a"]);
    }

    [Fact]
    public void TheArrowKeys_WrapAtTheEdges_SoTheGridIsNeverADeadEnd()
    {
        var cut = RenderGrid(tasks: [Task("a"), Task("b"), Task("c"), Task("d")], columns: 2);
        var refs = References(cut);

        Tile(cut, "a").KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });
        FocusTargets()[^1].Should().Be(refs["d"], "left of the first tile wraps to the last");

        Tile(cut, "d").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        FocusTargets()[^1].Should().Be(refs["a"]);

        Tile(cut, "a").KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });
        FocusTargets()[^1].Should().Be(refs["c"], "up from the first row wraps to the same column below");
    }

    [Fact]
    public void HomeAndEndStayInsideTheRow_WhileCtrlHomeAndCtrlEndCrossTheWholeGrid()
    {
        var cut = RenderGrid(tasks: [Task("a"), Task("b"), Task("c"), Task("d"), Task("e")], columns: 2);
        var refs = References(cut);

        Tile(cut, "d").Focus();
        Tile(cut, "d").KeyDown(new KeyboardEventArgs { Key = "Home" });
        FocusTargets()[^1].Should().Be(refs["c"], "Home is the first cell of the current row");

        Tile(cut, "c").KeyDown(new KeyboardEventArgs { Key = "End" });
        FocusTargets()[^1].Should().Be(refs["d"]);

        Tile(cut, "d").KeyDown(new KeyboardEventArgs { Key = "Home", CtrlKey = true });
        FocusTargets()[^1].Should().Be(refs["a"], "Ctrl+Home is the first cell of the grid");

        Tile(cut, "a").KeyDown(new KeyboardEventArgs { Key = "End", CtrlKey = true });
        FocusTargets()[^1].Should().Be(refs["e"], "Ctrl+End is the last cell of the grid");
    }

    [Fact]
    public void PageUpAndPageDown_JumpOneWindowOfRows()
    {
        var cut = RenderGrid(
            [.. Enumerable.Range(0, 30).Select(index => Task("t" + index))],
            columns: 2,
            visibleTaskLimit: 0);
        var refs = References(cut);

        Tile(cut, "t0").KeyDown(new KeyboardEventArgs { Key = "PageDown" });
        FocusTargets()[^1].Should().Be(refs["t20"],
            "a page is ten rows, so a two-column grid jumps twenty tiles");
    }

    [Fact]
    public void EnterAndSpace_AreNeverEmulated_BecauseTheTileIsANativeButton()
    {
        var cut = RenderGrid();

        var dispatch = () => Tile(cut, "a").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        dispatch.Should().NotThrow("a native button needs no key emulation");

        FocusTargets().Should().BeEmpty("Enter does not move the focus; the browser activates the button itself");
    }

    // ── Windowing ────────────────────────────────────────────────────────────

    [Fact]
    public void HundredsOfTiles_MountABoundedWindow_WhileTheGridDeclaresEveryRow()
    {
        var tasks = Enumerable.Range(0, 300).Select(index => Task("t" + index)).ToList();
        var cut = RenderGrid(tasks, columns: 4, visibleTaskLimit: 60);

        Tiles(cut).Should().HaveCount(60, "the window is bounded, so 300 tiles never hit the DOM at once");
        cut.Find("[data-testid='fan-out-grid-body']").GetAttribute("aria-rowcount").Should().Be("75",
            "the declared row count is the FILTERED total, not the mounted window");
        cut.FindAll("[data-testid='fan-out-row']").Should().HaveCount(15);
        cut.Find("[data-testid='fan-out-row']").GetAttribute("aria-rowindex").Should().Be("1");
    }

    [Fact]
    public void JumpingToTheEnd_MovesTheWindowWithTheFocus()
    {
        var tasks = Enumerable.Range(0, 300).Select(index => Task("t" + index)).ToList();
        var cut = RenderGrid(tasks, columns: 4, visibleTaskLimit: 60);
        var firstReference = References(cut)["t0"];

        Tile(cut, "t0").Focus();
        Tile(cut, "t0").KeyDown(new KeyboardEventArgs { Key = "End", CtrlKey = true });

        cut.FindAll("[data-testid='fan-out-tile'][data-task-id='t299']").Should().ContainSingle(
            "the last tile has to be mounted for the focus to land on it");
        Tile(cut, "t299").GetAttribute("tabindex").Should().Be("0", "the roving stop moved with the focus");
        FocusTargets()[^1].Should().NotBe(firstReference);
        FocusTargets()[^1].Should().NotBeNullOrEmpty("the DOM focus has to follow the roving stop");
        cut.Find("[data-testid='fan-out-row']").GetAttribute("aria-rowindex").Should().Be("61",
            "the mounted rows declare their real position in the full grid");
        cut.Find("[data-testid='fan-out-window']").TextContent.Should().Contain("241")
            .And.Contain("300", "the reader is told which slice they are in");
    }

    [Fact]
    public void AWindowLimitOfZero_RendersEveryTile()
    {
        var tasks = Enumerable.Range(0, 120).Select(index => Task("t" + index)).ToList();

        RenderGrid(tasks, columns: 4, visibleTaskLimit: 0).FindAll("[data-testid='fan-out-tile']")
            .Should().HaveCount(120, "windowing is opt-out: a host that wants the whole DOM gets it");
    }

    // ── Compact mode and the live clock ──────────────────────────────────────

    [Fact]
    public void CompactKeepsTheStateAndTheBadge_AndDropsTheSecondaryLines()
    {
        var cut = RenderGrid(compact: true);

        cut.Find("[data-testid='fan-out']").ClassList.Should().Contain("tm-ai-fan-out--compact");
        var tile = Tile(cut, "c");
        tile.QuerySelector(".tm-ai-fan-out__tile-state-text")!.TextContent.Trim().Should().Be("Running");
        tile.QuerySelector(".tm-ai-fan-out__badge")!.TextContent.Trim().Should().Be("Attempt 2/3");
        tile.QuerySelectorAll(".tm-ai-fan-out__tile-eta").Should().BeEmpty();
        tile.QuerySelectorAll(".tm-ai-fan-out__tile-worker").Should().BeEmpty();
    }

    [Fact]
    public void TheCountdownTicksOnTheInjectedClock()
    {
        var cut = RenderGrid();

        Summary(cut, "eta").Should().Be("1:30");
        Tile(cut, "a").QuerySelector(".tm-ai-fan-out__tile-eta")!.TextContent.Trim().Should().Be("0:42");

        _clock.Advance(TimeSpan.FromSeconds(2));

        cut.WaitForAssertion(() => Summary(cut, "eta").Should().Be("1:28"));
        cut.WaitForAssertion(() =>
            Tile(cut, "a").QuerySelector(".tm-ai-fan-out__tile-eta")!.TextContent.Trim().Should().Be("0:40"));
    }

    [Fact]
    public void AnOverdueEstimate_SaysSoInsteadOfCountingUp()
    {
        var cut = RenderGrid(
        [
            Task("a", estimate: Ten.AddSeconds(-5)),
        ]);

        Tile(cut, "a").QuerySelector(".tm-ai-fan-out__tile-eta")!.TextContent.Trim().Should().Be("Overdue");
        Tile(cut, "a").GetAttribute("aria-label").Should().Contain("Overdue");
    }

    [Fact]
    public void AFinishedFanOut_ArmsNoTimer()
    {
        var cut = RenderGrid(
        [
            Task("a", AgentActivityStatus.Succeeded, estimate: Ten.AddMinutes(5)),
            Task("b", AgentActivityStatus.Cancelled),
        ]);

        _clock.Advance(TimeSpan.FromSeconds(5));

        cut.FindAll("[data-testid='fan-out-summary-eta']").Should().ContainSingle();
        Summary(cut, "eta").Should().Be("Finished");
    }

    // ── Host surface ─────────────────────────────────────────────────────────

    [Fact]
    public void TheRootCarriesTheHostsClass_AndItsTestId_AndTheColumnKnob()
    {
        var cut = Render<TmFanOutGrid>(parameters => parameters
            .Add(component => component.Tasks, Tasks())
            .Add(component => component.Columns, 3)
            .Add(component => component.Class, "my-fan-out")
            .Add(component => component.DataTestId, "fan-out-here"));

        var root = cut.Find("[data-testid='fan-out-here']");

        root.ClassList.Should().Contain("tm-ai-fan-out");
        root.ClassList.Should().Contain("my-fan-out");
        root.GetAttribute("style").Should().Contain("--tm-fan-out-columns: 3",
            "the column count is a knob the stylesheet reads, so one value decides the layout");
    }

    [Fact]
    public async Task ATileThatDisappears_HandsTheFocusToTheFirstSurvivingTile()
    {
        var cut = RenderGrid();

        Tile(cut, "c").Focus();
        var focusBefore = FocusTargets().Count;

        await UpdateAsync(cut, (nameof(TmFanOutGrid.Tasks), new[] { Task("a"), Task("b") }));

        cut.Find("[data-testid='fan-out-grid-body'] [tabindex='0']").GetAttribute("data-task-id").Should().Be("a");
        FocusTargets().Count.Should().BeGreaterThan(focusBefore);
    }
}
