using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.Components.Observability;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Observability;

/// <summary>
/// Plan ff043b2e phase 6: <c>TmTraceWaterfall</c> is the generic span waterfall. These facts pin the
/// surface a host and a reader get — the treegrid semantics (role, column headers, aria-level,
/// aria-expanded, aria-selected, the roving tabindex), the keyboard contract (↑/↓, ←/→, Home/End,
/// Enter and Space with exactly one activation), the kind colour + icon + hidden kind name, the error
/// icon and text, the slowest badge, the selection binding with its detail panel, the axis units, the
/// zoom controls and their shortcuts, the metric columns with subtree sums, virtualization above the
/// threshold and the live mode driven by a <see cref="FakeTimeProvider"/>.
/// </summary>
public class TmTraceWaterfallTests : LocalizationTestBase
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    // ---------- fixtures ----------

    private static TraceSpan Span(
        string id,
        string? parentId = null,
        int startMs = 0,
        int? endMs = 1_000,
        string? name = null,
        TraceSpanKind kind = TraceSpanKind.Custom,
        TraceSpanStatus status = TraceSpanStatus.Ok,
        IReadOnlyDictionary<string, double>? metrics = null)
        => new()
        {
            Id = id,
            ParentId = parentId,
            Name = name ?? id,
            Kind = kind,
            StartedAt = T0.AddMilliseconds(startMs),
            EndedAt = endMs is { } end ? T0.AddMilliseconds(end) : null,
            Status = status,
            Metrics = metrics ?? new Dictionary<string, double>()
        };

    private static IReadOnlyDictionary<string, double> Metrics(params (string Key, double Value)[] values)
        => values.ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal);

    /// <summary>A root with two children, one of which has a grandchild.</summary>
    private static IReadOnlyList<TraceSpan> Tree() =>
    [
        Span("root", endMs: 1_000, name: "agent run", kind: TraceSpanKind.Agent),
        Span("llm", "root", endMs: 400, name: "gpt-4o", kind: TraceSpanKind.Llm),
        Span("tool", "root", startMs: 400, endMs: 900, name: "search_web", kind: TraceSpanKind.Tool),
        Span("http", "tool", startMs: 400, endMs: 600, name: "GET /search", kind: TraceSpanKind.Http)
    ];

    private IRenderedComponent<TmTraceWaterfall> RenderWaterfall(
        IReadOnlyList<TraceSpan> spans,
        Action<ComponentParameterCollectionBuilder<TmTraceWaterfall>>? configure = null)
        => Render<TmTraceWaterfall>(parameters =>
        {
            parameters.Add(component => component.Spans, spans);
            configure?.Invoke(parameters);
        });

    // ---------- treegrid semantics ----------

    [Fact]
    public void RendersATreegrid_WithColumnHeadersAndLevels()
    {
        var cut = RenderWaterfall(Tree());

        var grid = cut.Find("[role='treegrid']");
        grid.GetAttribute("aria-label").Should().Be("Trace waterfall");
        grid.GetAttribute("aria-rowcount").Should().Be("5");
        grid.GetAttribute("aria-colcount").Should().Be("3");

        var headers = cut.FindAll("[role='columnheader']").Select(header => header.TextContent.Trim()).ToList();
        headers.Should().Contain("Span").And.Contain("Duration");

        var rows = cut.FindAll("[role='row'][data-span-id]");
        rows.Should().HaveCount(4);
        rows.Select(row => row.GetAttribute("aria-level")).Should().Equal("1", "2", "2", "3");
        rows.Select(row => row.GetAttribute("tabindex")).Should().Equal("0", "-1", "-1", "-1");
        rows[0].GetAttribute("aria-expanded").Should().Be("true");
        rows[2].GetAttribute("aria-expanded").Should().Be("true");
        rows[1].GetAttribute("aria-expanded").Should().BeNull("a leaf has no disclosure");
        rows.Select(row => row.GetAttribute("aria-selected")).Should().OnlyContain(value => value == null);
        cut.FindAll(".tm-trace-waterfall__orphan").Should().BeEmpty(
            "every span of this trace names a supplied parent, so none of them is an orphan");
    }

    [Fact]
    public void TheRows_AreOwnedByTheTreegrid_ThroughARowgroup()
    {
        var cut = RenderWaterfall(Tree());

        var grid = cut.Find("[role='treegrid']");
        var rowgroup = grid.QuerySelector("[role='rowgroup']");

        rowgroup.Should().NotBeNull("a bare div between the grid and its rows breaks ARIA ownership");
        rowgroup!.QuerySelectorAll("[role='row'][data-span-id]").Length.Should().Be(4);
        rowgroup.QuerySelectorAll("[role='row']").All(row => row.HasAttribute("aria-level")).Should().BeTrue();
    }

    [Fact]
    public void IndentsEveryRowByItsDepth()
    {
        var cut = RenderWaterfall(Tree());

        var names = cut.FindAll(".tm-trace-waterfall__name");
        names[0].GetAttribute("style").Should().Contain("--tm-trace-waterfall-depth:0");
        names[1].GetAttribute("style").Should().Contain("--tm-trace-waterfall-depth:1");
        names[3].GetAttribute("style").Should().Contain("--tm-trace-waterfall-depth:2");
    }

    [Fact]
    public void Render_WithNoSpans_ShowsTheLocalizedEmptyState()
    {
        var cut = RenderWaterfall([]);

        cut.Find(".tm-trace-waterfall__empty").TextContent.Trim().Should().Be("No spans to show");
        cut.FindAll("[role='row'][data-span-id]").Should().BeEmpty();
    }

    [Fact]
    public void UsesTheTitleAsTheAccessibleName_WhenNoAriaLabelIsSupplied()
    {
        var cut = RenderWaterfall(Tree(), parameters => parameters.Add(component => component.Title, "Agent run"));

        cut.Find(".tm-trace-waterfall__title").TextContent.Trim().Should().Be("Agent run");
        cut.Find("[role='treegrid']").GetAttribute("aria-label").Should().Be("Agent run");
    }

    // ---------- collapse / expand ----------

    [Fact]
    public void LeftAndRightArrowKeys_CollapseAndExpandTheSubtree()
    {
        var cut = RenderWaterfall(Tree());
        var root = () => cut.FindAll("[role='row'][data-span-id='root']")[0];

        root().KeyDown("ArrowLeft");

        cut.FindAll("[role='row'][data-span-id]").Should().HaveCount(1, "collapsing the root hides its whole subtree");
        root().GetAttribute("aria-expanded").Should().Be("false");

        root().KeyDown("ArrowRight");

        cut.FindAll("[role='row'][data-span-id]").Should().HaveCount(4);
        root().GetAttribute("aria-expanded").Should().Be("true");
    }

    [Fact]
    public void TheDisclosureButton_TogglesWithoutSelectingTheRow()
    {
        string? selected = null;
        var cut = RenderWaterfall(Tree(), parameters => parameters.Add(
            component => component.SelectedSpanIdChanged,
            (string? value) => selected = value));

        cut.FindAll("[data-span-id='root'] .tm-trace-waterfall__toggle")[0].Click();

        cut.FindAll("[role='row'][data-span-id]").Should().HaveCount(1);
        selected.Should().BeNull("the disclosure is not a selection");
    }

    [Fact]
    public void LeftArrowOnACollapsedChild_WalksToItsParent()
    {
        var cut = RenderWaterfall(Tree());

        cut.FindAll("[role='row'][data-span-id='http']")[0].KeyDown("ArrowLeft");

        cut.FindAll("[role='row'][data-span-id='tool']")[0].GetAttribute("tabindex")
            .Should().Be("0", "the parent takes the roving tabindex");
        cut.FindAll("[role='row'][data-span-id='http']")[0].GetAttribute("tabindex").Should().Be("-1");
    }

    [Fact]
    public void DownUpHomeAndEndMoveTheRovingTabindex()
    {
        var cut = RenderWaterfall(Tree());

        cut.FindAll("[role='row'][data-span-id='root']")[0].KeyDown("ArrowDown");
        cut.FindAll("[role='row'][data-span-id='llm']")[0].GetAttribute("tabindex").Should().Be("0");

        cut.FindAll("[role='row'][data-span-id='llm']")[0].KeyDown("End");
        cut.FindAll("[role='row'][data-span-id='http']")[0].GetAttribute("tabindex").Should().Be("0");

        cut.FindAll("[role='row'][data-span-id='http']")[0].KeyDown("Home");
        cut.FindAll("[role='row'][data-span-id='root']")[0].GetAttribute("tabindex").Should().Be("0");

        cut.FindAll("[role='row'][data-span-id='root']")[0].KeyDown("ArrowUp");
        cut.FindAll("[role='row'][data-span-id='root']")[0].GetAttribute("tabindex")
            .Should().Be("0", "the first row does not wrap");
    }

    // ---------- activation ----------

    [Fact]
    public void EnterSelectsTheRow_ExactlyOnce()
    {
        var raised = 0;
        string? selected = null;
        var cut = RenderWaterfall(Tree(), parameters => parameters.Add(
            component => component.SelectedSpanIdChanged,
            (string? value) =>
            {
                raised++;
                selected = value;
            }));

        var row = cut.FindAll("[role='row'][data-span-id='llm']")[0];
        row.KeyDown("Enter");

        raised.Should().Be(1);
        selected.Should().Be("llm");
        cut.FindAll("[role='row'][data-span-id='llm']")[0].GetAttribute("aria-selected").Should().Be("true");
    }

    [Fact]
    public void SpaceSelectsOnKeyUp_NotOnKeyDown()
    {
        string? selected = null;
        var cut = RenderWaterfall(Tree(), parameters => parameters.Add(
            component => component.SelectedSpanIdChanged,
            (string? value) => selected = value));

        var row = cut.FindAll("[role='row'][data-span-id='llm']")[0];
        row.KeyDown(" ");
        selected.Should().BeNull("Space activates on keyup, exactly where a native button fires its click");

        cut.FindAll("[role='row'][data-span-id='llm']")[0].KeyUp(" ");
        selected.Should().Be("llm");
    }

    [Fact]
    public void AClickSelectsTheRow()
    {
        string? selected = null;
        var cut = RenderWaterfall(Tree(), parameters => parameters.Add(
            component => component.SelectedSpanIdChanged,
            (string? value) => selected = value));

        cut.FindAll("[role='row'][data-span-id='tool']")[0].Click();

        selected.Should().Be("tool");
    }

    [Fact]
    public void TheSelectedSpan_RendersTheDetailTemplate()
    {
        var cut = RenderWaterfall(Tree(), parameters =>
        {
            parameters.Add(component => component.SelectedSpanId, "http");
            parameters.Add(component => component.DetailTemplate,
                (TraceSpan span) => builder =>
                {
                    builder.OpenElement(0, "span");
                    builder.AddAttribute(1, "class", "tm-trace-waterfall__detail-name");
                    builder.AddContent(2, span.Name);
                    builder.CloseElement();
                });
        });

        var detail = cut.Find(".tm-trace-waterfall__detail");
        detail.GetAttribute("role").Should().Be("region");
        detail.GetAttribute("aria-label").Should().Be("Span detail");
        cut.Find(".tm-trace-waterfall__detail-name").TextContent.Should().Be("GET /search");
        cut.FindAll("[role='row'][data-span-id='http']")[0].GetAttribute("aria-selected").Should().Be("true");
    }

    [Fact]
    public void RendersNoDetailPanel_WhenNothingIsSelected()
    {
        var cut = RenderWaterfall(Tree(), parameters => parameters.Add(
            component => component.DetailTemplate,
            (TraceSpan span) => builder => builder.AddContent(0, span.Name)));

        cut.FindAll(".tm-trace-waterfall__detail").Should().BeEmpty();
    }

    // ---------- kind, status, slowest ----------

    [Fact]
    public void EveryKind_RendersItsColourClass_ItsIcon_AndItsHiddenName()
    {
        var spans = new List<TraceSpan>
        {
            Span("root", endMs: 1_000, name: "root", kind: TraceSpanKind.Agent)
        };
        var kinds = new[]
        {
            TraceSpanKind.Llm, TraceSpanKind.Tool, TraceSpanKind.Agent,
            TraceSpanKind.Http, TraceSpanKind.Db, TraceSpanKind.Custom
        };
        foreach (var kind in kinds)
        {
            spans.Add(Span("child-" + kind, "root", endMs: 100, kind: kind));
        }

        var cut = RenderWaterfall(spans);

        var expectedLabels = new Dictionary<TraceSpanKind, string>
        {
            [TraceSpanKind.Llm] = "LLM call",
            [TraceSpanKind.Tool] = "Tool call",
            [TraceSpanKind.Agent] = "Agent",
            [TraceSpanKind.Http] = "HTTP request",
            [TraceSpanKind.Db] = "Database query",
            [TraceSpanKind.Custom] = "Custom"
        };

        foreach (var kind in kinds)
        {
            var id = "child-" + kind;
            var bar = cut.FindAll($"[data-span-id='{id}'] .tm-trace-waterfall__bar")[0];
            bar.ClassList.Should().Contain("tm-trace-waterfall__bar--" + kind.ToString().ToLowerInvariant());

            var glyph = cut.FindAll($"[data-span-id='{id}'] .tm-trace-waterfall__kind")[0];
            glyph.ClassList.Should().Contain("tm-trace-waterfall__kind--" + kind.ToString().ToLowerInvariant());

            var row = cut.FindAll($"[role='row'][data-span-id='{id}']")[0];
            row.QuerySelectorAll(".tm-sr-only").Select(element => element.TextContent.Trim())
                .Should().Contain(expectedLabels[kind], "the colour is never the only carrier of the kind");
        }
    }

    [Fact]
    public void AnErrorSpan_ShowsAnIconAndLocalizedText()
    {
        var cut = RenderWaterfall(
        [
            Span("root", endMs: 1_000),
            Span("failed", "root", endMs: 400, status: TraceSpanStatus.Error)
        ]);

        var status = cut.FindAll("[data-span-id='failed'] .tm-trace-waterfall__status")[0];
        status.ClassList.Should().Contain("tm-trace-waterfall__status--error");
        status.TextContent.Trim().Should().Be("Error");
        status.QuerySelector("svg").Should().NotBeNull("an icon AND the text, never the text alone");

        cut.FindAll("[role='row'][data-span-id='failed']")[0].ClassList
            .Should().Contain("tm-trace-waterfall__row--error");
    }

    [Fact]
    public void TheSlowestLeafSpan_CarriesTheLocalizedBadge()
    {
        var cut = RenderWaterfall(
        [
            Span("root", endMs: 1_000),
            Span("short", "root", endMs: 100),
            Span("slow", "root", endMs: 700)
        ]);

        cut.FindAll("[data-span-id='slow'] .tm-trace-waterfall__slowest")[0]
            .TextContent.Trim().Should().Be("Slowest");
        cut.FindAll("[role='row'][data-span-id='slow']")[0].ClassList
            .Should().Contain("tm-trace-waterfall__row--slowest");
        cut.FindAll("[data-span-id='root'] .tm-trace-waterfall__slowest").Should().BeEmpty(
            "the root's duration is the whole request, never the highlight");
    }

    [Fact]
    public void AnOrphanSpan_IsRenderedUnderTheRoot_WithItsLabel()
    {
        var cut = RenderWaterfall(
        [
            Span("root", endMs: 1_000),
            Span("orphan", "missing", startMs: 100, endMs: 200)
        ]);

        var row = cut.FindAll("[role='row'][data-span-id='orphan']")[0];
        row.GetAttribute("aria-level").Should().Be("1");
        row.QuerySelector(".tm-trace-waterfall__orphan")!.TextContent.Trim().Should().Be("Orphan");
    }

    // ---------- axis ----------

    [Fact]
    public void TheAxis_TicksInTheRangesOwnUnit()
    {
        var cut = RenderWaterfall([Span("root", endMs: 1_000)]);

        var labels = cut.FindAll(".tm-trace-waterfall__tick-label")
            .Select(label => label.TextContent.Trim()).ToList();
        labels.Should().HaveCountGreaterThan(2);
        labels.Should().Contain("1 s", "a one second range reads in seconds");
        labels.Should().NotContain(label => label.Contains("ms", StringComparison.Ordinal));
    }

    [Fact]
    public void AShortRange_TicksInMilliseconds()
    {
        var cut = RenderWaterfall([Span("root", endMs: 500)]);

        var labels = cut.FindAll(".tm-trace-waterfall__tick-label")
            .Select(label => label.TextContent.Trim())
            .ToList();

        labels.Should().OnlyContain(label => label.EndsWith(" ms", StringComparison.Ordinal),
            "a half-second range reads in milliseconds, never in seconds");
        labels.Should().Contain("400 ms", "the axis lands on the last nice step inside the range");
    }

    // ---------- zoom ----------

    [Fact]
    public void TheZoomButtons_NarrowAndResetTheWindow()
    {
        var cut = RenderWaterfall([Span("root", endMs: 1_000)]);

        cut.Find("[data-testid='trace-zoom-reset']").HasAttribute("disabled").Should().BeTrue(
            "the window already shows the whole trace");

        cut.Find("[data-testid='trace-zoom-in']").Click();

        cut.Find(".tm-trace-waterfall__window").TextContent.Trim().Should().Be("Window: 500 ms");
        cut.Find("[data-testid='trace-zoom-reset']").HasAttribute("disabled").Should().BeFalse();

        cut.Find("[data-testid='trace-zoom-out']").Click();
        cut.Find(".tm-trace-waterfall__window").TextContent.Trim().Should().Be("Window: 1 s");

        cut.Find("[data-testid='trace-zoom-in']").Click();
        cut.Find("[data-testid='trace-zoom-reset']").Click();
        cut.Find(".tm-trace-waterfall__window").TextContent.Trim().Should().Be("Window: 1 s");
    }

    [Fact]
    public void TheZoomKeyboardShortcuts_WorkFromARow()
    {
        var cut = RenderWaterfall([Span("root", endMs: 1_000)]);

        cut.FindAll("[role='row'][data-span-id='root']")[0].KeyDown("+");
        cut.Find(".tm-trace-waterfall__window").TextContent.Trim().Should().Be("Window: 500 ms");

        cut.FindAll("[role='row'][data-span-id='root']")[0].KeyDown("-");
        cut.Find(".tm-trace-waterfall__window").TextContent.Trim().Should().Be("Window: 1 s");

        cut.FindAll("[role='row'][data-span-id='root']")[0].KeyDown("+");
        cut.FindAll("[role='row'][data-span-id='root']")[0].KeyDown("0");
        cut.Find(".tm-trace-waterfall__window").TextContent.Trim().Should().Be("Window: 1 s");
    }

    [Fact]
    public void TheSharedTemplateWidth_CountsEveryTrackAndGap()
    {
        var cut = RenderWaterfall(Tree(), parameters => parameters.Add(component => component.MetricColumns,
        [
            new TraceMetricColumn("tokens", "Tokens", "N0"),
            new TraceMetricColumn("cost", "Cost", "C2")
        ]));

        // The header and the rowgroup are separate grids from the rows they hold, so they only line up
        // while all of them share ONE deterministic inline size. Content-sized boxes let a long label
        // widen a single row and drift the columns apart (proven in the browser lane), so the component
        // emits that size from the same track knobs the header and the rows use.
        var style = cut.Find(".tm-trace-waterfall").GetAttribute("style")!;
        var width = style[style.IndexOf("--tm-trace-waterfall-template-width:", StringComparison.Ordinal)..];

        width.Should().StartWith("--tm-trace-waterfall-template-width:calc(");
        width.Split("var(--tm-trace-waterfall-metric-width)", StringSplitOptions.None)
            .Should().HaveCount(3, "one track term per metric column");
        width.Should().Contain("+ 4 * var(--tm-space-2)",
            "five columns means four gaps between their tracks");
    }

    [Fact]
    public void TheZoomControls_AnnounceInTheSingleLiveRegion()
    {
        var cut = RenderWaterfall([Span("root", endMs: 1_000)]);

        cut.Find("[data-testid='trace-zoom-in']").Click();

        cut.FindAll("[aria-live]").Should().HaveCount(1);
        cut.Find("[aria-live]").TextContent.Trim().Should().Contain("500 ms");
    }

    // ---------- metric columns ----------

    [Fact]
    public void MetricColumns_RenderTheirHeaders_TheirValues_AndTheSubtreeSum()
    {
        var cut = RenderWaterfall(
        [
            Span("root", endMs: 1_000, metrics: Metrics(("tokens", 10))),
            Span("child", "root", endMs: 400, metrics: Metrics(("tokens", 5))),
            Span("leaf", "child", endMs: 200)
        ],
            parameters => parameters.Add(component => component.MetricColumns,
            [
                new TraceMetricColumn("tokens", "Tokens", "N0")
            ]));

        cut.FindAll("[role='columnheader']").Select(header => header.TextContent.Trim())
            .Should().Contain("Tokens");

        var rootCell = cut.FindAll("[role='row'][data-span-id='root'] [data-metric='tokens']")[0];
        rootCell.TextContent.Should().Contain("10");
        rootCell.TextContent.Should().Contain("15", "the subtree total includes both children");
        rootCell.QuerySelector(".tm-trace-waterfall__metric-sum")!.GetAttribute("aria-label")
            .Should().Be("Subtree total: 15");

        cut.FindAll("[role='row'][data-span-id='leaf'] [data-metric='tokens']")[0]
            .QuerySelector(".tm-trace-waterfall__metric-none").Should().NotBeNull(
                "a span that does not report the key renders a dash, not a zero");
    }

    // ---------- virtualization ----------

    [Fact]
    public void OverTwoHundredRows_UsesTheVirtualizer()
    {
        var spans = new List<TraceSpan> { Span("root", endMs: 1_000) };
        for (var index = 0; index < 201; index++)
        {
            spans.Add(Span($"child-{index}", "root", endMs: 100));
        }

        var cut = RenderWaterfall(spans);

        cut.FindComponents<Virtualize<TraceWaterfallRow>>().Should().ContainSingle();
    }

    [Fact]
    public void AtTwoHundredRows_RendersPlainRows()
    {
        var spans = new List<TraceSpan> { Span("root", endMs: 1_000) };
        for (var index = 0; index < 199; index++)
        {
            spans.Add(Span($"child-{index}", "root", endMs: 100));
        }

        var cut = RenderWaterfall(spans);

        cut.FindComponents<Virtualize<TraceWaterfallRow>>().Should().BeEmpty();
        cut.FindAll("[role='row'][data-span-id]").Should().HaveCount(200);
    }

    // ---------- live mode ----------

    [Fact]
    public void LiveMode_GrowsTheRunningBarOnEveryTick()
    {
        var clock = new FakeTimeProvider(T0);
        var cut = RenderWaterfall(
        [
            Span("root", endMs: 1_000),
            Span("running", "root", startMs: 100, endMs: null, status: TraceSpanStatus.Running)
        ],
            parameters =>
            {
                parameters.Add(component => component.LiveMode, true);
                parameters.Add(component => component.LiveTickInterval, TimeSpan.FromSeconds(1));
                parameters.Add(component => component.TimeProvider, clock);
            });

        var widthBefore = cut.FindAll("[data-span-id='running'] .tm-trace-waterfall__bar")[0]
            .GetAttribute("style");

        clock.Advance(TimeSpan.FromSeconds(1));

        cut.WaitForAssertion(() =>
        {
            var widthAfter = cut.FindAll("[data-span-id='running'] .tm-trace-waterfall__bar")[0]
                .GetAttribute("style");
            widthAfter.Should().NotBe(widthBefore, "the running span's bar grows with the clock");
            widthAfter.Should().Contain("--tm-trace-waterfall-width:");
        });
    }

    [Fact]
    public void LiveMode_DoesNotRedrawWhileNothingRuns()
    {
        var clock = new FakeTimeProvider(T0);
        var cut = RenderWaterfall(
        [
            Span("root", endMs: 1_000),
            Span("child", "root", endMs: 500)
        ],
            parameters =>
            {
                parameters.Add(component => component.LiveMode, true);
                parameters.Add(component => component.TimeProvider, clock);
            });

        var before = cut.FindAll("[data-span-id='child'] .tm-trace-waterfall__bar")[0].GetAttribute("style");

        clock.Advance(TimeSpan.FromSeconds(10));

        cut.FindAll("[data-span-id='child'] .tm-trace-waterfall__bar")[0]
            .GetAttribute("style").Should().Be(before);
        cut.Find("[aria-live]").TextContent.Trim().Should().BeEmpty(
            "a tick never writes to the live region");
    }

    [Fact]
    public void LiveMode_FollowsASwappedTimeProvider()
    {
        var first = new FakeTimeProvider(T0);
        var second = new FakeTimeProvider(T0);
        var cut = RenderWaterfall(
        [
            Span("root", endMs: 1_000),
            Span("running", "root", startMs: 100, endMs: null, status: TraceSpanStatus.Running)
        ],
            parameters =>
            {
                parameters.Add(component => component.LiveMode, true);
                parameters.Add(component => component.TimeProvider, first);
            });

        var bar = () => cut.FindAll("[data-span-id='running'] .tm-trace-waterfall__bar")[0].GetAttribute("style");
        var before = bar();

        cut.Render(parameters => parameters.Add(component => component.TimeProvider, second));

        first.Advance(TimeSpan.FromSeconds(2));

        bar().Should().Be(before, "the timer of the replaced clock must not keep driving the ticker");

        second.Advance(TimeSpan.FromSeconds(1));

        cut.WaitForAssertion(() => bar().Should().NotBe(before, "the NEW clock drives the redraw"));
    }

    [Fact]
    public void TheTimeProviderParameter_DefaultsToTheSystemClock()
    {
        var cut = RenderWaterfall([Span("root", endMs: 1_000)]);

        cut.Instance.TimeProvider.Should().BeSameAs(TimeProvider.System);
    }

    [Fact]
    public void Dispose_ReleasesTheLiveTimer()
    {
        var clock = new FakeTimeProvider(T0);
        var cut = RenderWaterfall(
        [
            Span("root", endMs: 1_000),
            Span("running", "root", startMs: 100, endMs: null, status: TraceSpanStatus.Running)
        ],
            parameters =>
            {
                parameters.Add(component => component.LiveMode, true);
                parameters.Add(component => component.TimeProvider, clock);
            });

        cut.Instance.Should().BeAssignableTo<IDisposable>();

        var act = () =>
        {
            cut.Instance.Dispose();
            clock.Advance(TimeSpan.FromSeconds(5));
        };

        act.Should().NotThrow("a tick that looses the race with Dispose must be a no-op");
    }

    // ---------- scroll-key guard (JS) ----------

    [Fact]
    public void TheScrollKeyGuard_IsAttachedToTheGrid_AndDetachedOnDispose()
    {
        var module = JSInterop.SetupModule("./_content/Tempo.Blazor/js/tm-trace-waterfall.js");
        module.Mode = JSRuntimeMode.Strict;
        module.SetupVoid("attachScrollKeyGuard", _ => true).SetVoidResult();
        module.SetupVoid("detachScrollKeyGuard", _ => true).SetVoidResult();

        var cut = RenderWaterfall(Tree());

        module.Invocations.Should().Contain(
            invocation => invocation.Identifier == "attachScrollKeyGuard",
            "the per-key scroll cancellation cannot be expressed as a render-time preventDefault");

        cut.Instance.Dispose();

        module.Invocations.Should().Contain(
            invocation => invocation.Identifier == "detachScrollKeyGuard",
            "a disposed component must not leave its listener on the grid");
    }

    // ---------- localization ----------

    [Fact]
    public void EverySurface_UsesALocalizedKey()
    {
        var cut = RenderWaterfall(
            Tree(),
            parameters => parameters.Add(component => component.SelectedSpanId, "http"));

        cut.Markup.Should().NotContain("[TmTraceWaterfall",
            "a missing key renders its bracket marker, so this catches an unlocalized surface");
        cut.Markup.Should().NotContain("Undefined");
    }
}
