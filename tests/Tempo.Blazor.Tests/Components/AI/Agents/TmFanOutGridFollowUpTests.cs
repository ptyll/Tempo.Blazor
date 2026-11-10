using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Components.Overlay;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.AI.Agents;

/// <summary>
/// Plan 7ba95ed6, Fáze 3 — the <c>TmFanOutGrid</c> follow-ups: a measured column count
/// (<see cref="FanOutColumnsMode.Auto"/> over the component's own container), a popover detail
/// presentation (<see cref="FanOutDetailPresentation.Popover"/> anchored to the tile, Escape with
/// focus return) and an elapsed read-out for an active task that carries a <c>StartedAt</c> but no
/// <c>EstimatedEndAt</c> (a <c>FakeTimeProvider</c> closes the seconds without sleeping).
/// </summary>
public class TmFanOutGridFollowUpTests : LocalizationTestBase
{
    private const string FocusInvocation = "Blazor._internal.domWrapper.focus";

    private static readonly DateTimeOffset Ten = new(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock;

    public TmFanOutGridFollowUpTests()
    {
        _clock = new FakeTimeProvider(Ten);
        Services.AddSingleton<TimeProvider>(_clock);
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────

    private static FanOutTask Task(
        string id,
        AgentActivityStatus status = AgentActivityStatus.Running,
        DateTimeOffset? started = null,
        DateTimeOffset? estimate = null,
        string? error = null,
        AgentRef? worker = null,
        string? label = null)
        => new(id, label ?? "Region " + id)
        {
            Status = status,
            StartedAt = started,
            EstimatedEndAt = estimate,
            Error = error,
            Worker = worker,
        };

    private IRenderedComponent<TmFanOutGrid> RenderGrid(
        IReadOnlyList<FanOutTask>? tasks = null,
        int columns = 4,
        FanOutColumnsMode columnsMode = FanOutColumnsMode.Fixed,
        int minTileWidth = 144,
        FanOutDetailPresentation detailPresentation = FanOutDetailPresentation.Inline)
        => Render<TmFanOutGrid>(parameters =>
        {
            parameters.Add(component => component.Tasks, tasks ?? [Task("a"), Task("b")]);
            parameters.Add(component => component.Culture, CultureInfo.GetCultureInfo("en-US"));
            parameters.Add(component => component.TimeProvider, _clock);
            parameters.Add(component => component.Columns, columns);
            parameters.Add(component => component.ColumnsMode, columnsMode);
            parameters.Add(component => component.MinTileWidth, minTileWidth);
            parameters.Add(component => component.DetailPresentation, detailPresentation);
        });

    private static IReadOnlyList<AngleSharp.Dom.IElement> Tiles(IRenderedComponent<TmFanOutGrid> cut)
        => cut.FindAll("[data-testid='fan-out-tile']");

    private static AngleSharp.Dom.IElement Tile(IRenderedComponent<TmFanOutGrid> cut, string id)
        => cut.Find($"[data-testid='fan-out-tile'][data-task-id='{id}']");

    private static IReadOnlyDictionary<string, string?> References(IRenderedComponent<TmFanOutGrid> cut)
        => Tiles(cut).ToDictionary(
            tile => tile.GetAttribute("data-task-id")!,
            tile => tile.GetAttribute("blazor:elementreference"),
            StringComparer.Ordinal);

    private List<string?> FocusTargets()
        => JSInterop.Invocations
            .Where(invocation => invocation.Identifier == FocusInvocation)
            .Select(invocation => (invocation.Arguments[0] as ElementReference?)?.Id)
            .ToList();

    // ── ColumnsMode.Auto ─────────────────────────────────────────────────────

    [Fact]
    public void FixedMode_IsTheDefault_AndNeverImportsTheObserver()
    {
        var cut = RenderGrid();

        cut.FindComponents<TmLayoutObserver>().Should().BeEmpty(
            "the fixed layout is pure CSS plus the Columns parameter — no measurement, no JS");

        cut.Find("[data-testid='fan-out']").GetAttribute("data-columns").Should().Be("4");
    }

    [Fact]
    public void AutoMode_BeforeAMeasurement_FallsBackToTheColumnsParameter()
    {
        var cut = RenderGrid(columns: 3, columnsMode: FanOutColumnsMode.Auto);

        // No measurement has arrived yet, so the host's Columns value decides — the same value the
        // stylesheet reads from the inline knob.
        cut.Find("[data-testid='fan-out']").GetAttribute("data-columns").Should().Be("3");
        cut.Find("[data-testid='fan-out-grid-body']").GetAttribute("aria-colcount").Should().Be("3");
    }

    [Fact]
    public async Task AutoMode_AMeasurement_DrivesTheWindowAndTheStylesheetWithOneNumber()
    {
        var cut = RenderGrid(
            [Task("a"), Task("b"), Task("c"), Task("d"), Task("e")],
            columnsMode: FanOutColumnsMode.Auto,
            minTileWidth: 144);

        var observer = cut.FindComponent<TmLayoutObserver>();
        await cut.InvokeAsync(() => observer.Instance.OnContainerWidthChanged(375));

        // 375 px of grid: (375 + 8) / (144 + 8) = 2.52 → 2 columns.
        var root = cut.Find("[data-testid='fan-out']");
        root.GetAttribute("data-columns").Should().Be("2");
        root.GetAttribute("style").Should().Contain("--tm-fan-out-columns: 2",
            "the inline knob is the stylesheet's single source and it carries the MEASURED number");
        cut.Find("[data-testid='fan-out-grid-body']").GetAttribute("aria-colcount").Should().Be("2");
        cut.FindAll("[data-testid='fan-out-row']").Should().HaveCount(3,
            "five tiles over the MEASURED two columns are three rows — the C# window never diverges from the render");
    }

    [Fact]
    public async Task AutoMode_AWideMeasurement_ClampsToTheWidestGrid()
    {
        var cut = RenderGrid(columnsMode: FanOutColumnsMode.Auto, minTileWidth: 144);

        var observer = cut.FindComponent<TmLayoutObserver>();
        await cut.InvokeAsync(() => observer.Instance.OnContainerWidthChanged(4000));

        cut.Find("[data-testid='fan-out']").GetAttribute("data-columns").Should().Be("8",
            "more than eight columns stop reading as a grid");
    }

    [Fact]
    public async Task AutoMode_AResize_RecomputesTheWindow()
    {
        var cut = RenderGrid(
            [Task("a"), Task("b"), Task("c"), Task("d"), Task("e")],
            columnsMode: FanOutColumnsMode.Auto);

        var observer = cut.FindComponent<TmLayoutObserver>();
        await cut.InvokeAsync(() => observer.Instance.OnContainerWidthChanged(1440));
        cut.FindAll("[data-testid='fan-out-row']").Should().HaveCount(1,
            "five tiles over eight measured columns are one row");

        await cut.InvokeAsync(() => observer.Instance.OnContainerWidthChanged(375));
        cut.FindAll("[data-testid='fan-out-row']").Should().HaveCount(3,
            "and the same five tiles over two measured columns are three rows");
    }

    // ── DetailPresentation.Popover ───────────────────────────────────────────

    [Fact]
    public void PopoverMode_FocusingATile_OpensAnAnchoredPanelWithTheSameDetail()
    {
        var cut = RenderGrid(
            [Task("a", worker: new AgentRef("Fetcher", "w1") { ColorIndex = 1 })],
            detailPresentation: FanOutDetailPresentation.Popover);
        var tileReference = References(cut)["a"];

        cut.FindAll("[data-testid='fan-out-detail']").Should().BeEmpty(
            "the popover replaces the in-flow detail region, never duplicates it");

        Tile(cut, "a").Focus();

        var panel = cut.FindComponent<TmOverlayPanel>();
        panel.Instance.IsOpen.Should().BeTrue();
        panel.Instance.Anchor.Id.Should().Be(tileReference,
            "the panel is anchored to the tile the reader focused");

        var body = cut.Find("[data-testid='fan-out-detail'] .tm-ai-fan-out__detail");
        body.TextContent.Should().Contain("Fetcher");
        cut.Find("[data-testid='fan-out-detail']").GetAttribute("data-task-id").Should().Be("a");
    }

    [Fact]
    public async Task PopoverMode_EscapeDismissesThePanel_AndReturnsFocusToTheTile()
    {
        var cut = RenderGrid([Task("a")], detailPresentation: FanOutDetailPresentation.Popover);
        var tileReference = References(cut)["a"];
        Tile(cut, "a").Focus();

        var panel = cut.FindComponent<TmOverlayPanel>();
        await cut.InvokeAsync(() => panel.Instance.NotifyDismissedAsync("escape"));

        cut.FindComponent<TmOverlayPanel>().Instance.IsOpen.Should().BeFalse();
        FocusTargets().Should().Contain(tileReference,
            "a keyboard dismissal hands the focus back to the tile that owned the detail");
    }

    [Fact]
    public void PopoverMode_RefocusingTheTileAfterEscape_ReopensThePanel()
    {
        var cut = RenderGrid([Task("a"), Task("b")], detailPresentation: FanOutDetailPresentation.Popover);
        Tile(cut, "a").Focus();
        cut.FindComponent<TmOverlayPanel>().Instance.IsOpen.Should().BeTrue();

        cut.InvokeAsync(() => cut.FindComponent<TmOverlayPanel>().Instance.NotifyDismissedAsync("escape"));

        cut.FindComponent<TmOverlayPanel>().Instance.IsOpen.Should().BeFalse();

        Tile(cut, "a").Focus();
        cut.FindComponent<TmOverlayPanel>().Instance.IsOpen.Should().BeTrue(
            "the reader's return to the tile re-opens the dismissed popover");
    }

    [Fact]
    public async Task PopoverMode_OutsideDismissal_ClosesWithoutPullingFocusBack()
    {
        var cut = RenderGrid([Task("a")], detailPresentation: FanOutDetailPresentation.Popover);
        Tile(cut, "a").Focus();
        var focusCount = FocusTargets().Count;

        var panel = cut.FindComponent<TmOverlayPanel>();
        await cut.InvokeAsync(() => panel.Instance.NotifyDismissedAsync("outside"));

        cut.FindComponent<TmOverlayPanel>().Instance.IsOpen.Should().BeFalse();
        FocusTargets().Count.Should().Be(focusCount,
            "the reader clicked somewhere else: pulling focus back would undo that click");
    }

    [Fact]
    public void InlineMode_IsTheDefault_AndRendersNoPanel()
    {
        var cut = RenderGrid([Task("a")]);

        cut.FindComponents<TmOverlayPanel>().Should().BeEmpty();
        Tile(cut, "a").Focus();
        cut.Find("[data-testid='fan-out-detail']").GetAttribute("role").Should().Be("region");
    }

    // ── Elapsed without an estimate ──────────────────────────────────────────

    [Fact]
    public void AStartedTaskWithoutEstimate_ShowsTheElapsedReadOut_OnTheTileAndInTheDetail()
    {
        var cut = RenderGrid([Task("a", started: Ten.AddSeconds(-30))]);

        Tile(cut, "a").QuerySelector(".tm-ai-fan-out__tile-eta")!.TextContent.Trim()
            .Should().Be("running for 1 min", "a task 30 s into its first minute reads as one minute");

        Tile(cut, "a").Focus();
        cut.Find("[data-testid='fan-out-detail']").TextContent.Should().Contain("running for 1 min");
    }

    [Fact]
    public void TheElapsedReadOut_TicksOnTheInjectedClock()
    {
        var cut = RenderGrid([Task("a", started: Ten.AddMinutes(-1))]);

        Tile(cut, "a").QuerySelector(".tm-ai-fan-out__tile-eta")!.TextContent.Trim()
            .Should().Be("running for 1 min");

        _clock.Advance(TimeSpan.FromMinutes(1));

        cut.WaitForAssertion(() =>
            Tile(cut, "a").QuerySelector(".tm-ai-fan-out__tile-eta")!.TextContent.Trim()
                .Should().Be("running for 2 min"));
    }

    [Fact]
    public void AStartedTaskWithoutEstimate_AddsTheElapsedToTheAccessibleName()
    {
        var cut = RenderGrid([Task("a", started: Ten.AddMinutes(-2))]);

        Tile(cut, "a").GetAttribute("aria-label").Should().Contain("running for 2 min");
    }

    [Fact]
    public void ATaskWithAnEstimate_KeepsTheCountdown_ElapsedStaysOut()
    {
        var cut = RenderGrid([Task("a", started: Ten.AddMinutes(-2), estimate: Ten.AddSeconds(42))]);

        Tile(cut, "a").QuerySelector(".tm-ai-fan-out__tile-eta")!.TextContent.Trim()
            .Should().Be("0:42", "an estimate is a countdown, never an elapsed read-out");
        Tile(cut, "a").GetAttribute("aria-label").Should().Contain("ends in 0:42")
            .And.NotContain("running for");
    }

    [Fact]
    public void AQueuedTaskWithoutAStart_ShowsNoElapsed()
    {
        var cut = RenderGrid([Task("a", AgentActivityStatus.Queued, started: null)]);

        Tile(cut, "a").QuerySelectorAll(".tm-ai-fan-out__tile-eta").Should().BeEmpty(
            "a task that never started has nothing to count");
    }

    [Fact]
    public void TheElapsedReadOut_IsLocalizedWithAPluralFamily()
    {
        UseCzechLocalization();
        var clock = new FakeTimeProvider(Ten);
        var cut = Render<TmFanOutGrid>(parameters =>
        {
            parameters.Add(component => component.Tasks, [Task("a", started: Ten.AddMinutes(-2))]);
            parameters.Add(component => component.TimeProvider, clock);
        });

        Tile(cut, "a").QuerySelector(".tm-ai-fan-out__tile-eta")!.TextContent.Trim()
            .Should().Be("běží 2 min");
    }
}
