using System.Globalization;
using FluentAssertions;
using Tempo.Blazor.Components.Observability;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Tests.Observability;

/// <summary>
/// Plan ff043b2e phase 6: the pure layout pass behind <c>TmTraceWaterfall</c>. Every fact here is a
/// contract the component renders but cannot itself prove — the tree built from a flat span list
/// (orphans under the root, cycles rejected without throwing), the axis range, each bar's offset and
/// width in percent, running spans extended to the caller's "now", the slowest span and the per-key
/// sum of every subtree.
/// <para>
/// The layout is deterministic and clock-free: "now" is always a parameter, which is exactly what
/// lets the component's live mode be driven by a <c>FakeTimeProvider</c>.
/// </para>
/// </summary>
public class TraceWaterfallLayoutTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo Czech = CultureInfo.GetCultureInfo("cs-CZ");

    // ---------- fixtures ----------

    private static TraceSpan Span(
        string id,
        string? parentId = null,
        int startMs = 0,
        int? endMs = null,
        TraceSpanKind kind = TraceSpanKind.Custom,
        TraceSpanStatus status = TraceSpanStatus.Ok,
        IReadOnlyDictionary<string, double>? metrics = null)
        => new()
        {
            Id = id,
            ParentId = parentId,
            Name = id,
            Kind = kind,
            StartedAt = T0.AddMilliseconds(startMs),
            EndedAt = endMs is { } end ? T0.AddMilliseconds(end) : null,
            Status = status,
            Metrics = metrics ?? new Dictionary<string, double>()
        };

    private static IReadOnlyDictionary<string, double> Metrics(params (string Key, double Value)[] values)
        => values.ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal);

    private static TraceWaterfallLayoutResult Build(params TraceSpan[] spans)
        => TraceWaterfallLayout.Build(spans, T0.AddMilliseconds(1_000));

    // ---------- empty ----------

    [Fact]
    public void Build_WithNoSpans_ReturnsTheEmptyLayout()
    {
        var result = TraceWaterfallLayout.Build(null, T0);

        result.Rows.Should().BeEmpty();
        result.SlowestSpanId.Should().BeNull();
        result.RejectedSpanIds.Should().BeEmpty();
        result.Axis.Duration.Should().Be(TimeSpan.FromMilliseconds(1),
            "a zero-length axis would divide by zero when the bars are measured");
    }

    // ---------- tree ----------

    [Fact]
    public void Build_FlattensTheTreeInPreOrder_WithDepthAndChildFlag()
    {
        var result = Build(
            Span("root", startMs: 0, endMs: 1_000),
            Span("child-a", "root", startMs: 0, endMs: 400),
            Span("grandchild", "child-a", startMs: 0, endMs: 200),
            Span("child-b", "root", startMs: 400, endMs: 1_000));

        result.Rows.Select(row => row.Span.Id).Should().Equal("root", "child-a", "grandchild", "child-b");
        result.Rows.Select(row => row.Depth).Should().Equal(0, 1, 2, 1);
        result.Rows.Select(row => row.HasChildren).Should().Equal(true, true, false, false);
        result.Rows.Select(row => row.Index).Should().Equal(0, 1, 2, 3);
        result.Rows.Should().OnlyContain(row => !row.IsOrphan,
            "a span whose parent was resolved is never an orphan, however deep it sits");
    }

    [Fact]
    public void Build_PutsASpanWithAMissingParentUnderTheRoot_AsAnOrphan()
    {
        var result = Build(
            Span("root", startMs: 0, endMs: 1_000),
            Span("orphan", "gone", startMs: 100, endMs: 200),
            Span("orphan-child", "orphan", startMs: 100, endMs: 150));

        result.Rows.Select(row => row.Span.Id).Should().Equal("root", "orphan", "orphan-child");
        result.Rows[1].IsOrphan.Should().BeTrue("its parent id names no supplied span");
        result.Rows[1].Depth.Should().Be(0);
        result.Rows[2].IsOrphan.Should().BeFalse(
            "its parent resolved (to the orphan), so only the orphan itself carries the label");
        result.Rows[2].Depth.Should().Be(1, "the orphan itself becomes a root of its own subtree");
        result.RejectedSpanIds.Should().BeEmpty();
    }

    [Fact]
    public void Build_RejectsACycle_WithoutThrowing_AndReattachesItsChildrenAsOrphans()
    {
        var result = Build(
            Span("a", "b", startMs: 0, endMs: 100),
            Span("b", "a", startMs: 0, endMs: 100),
            Span("child-of-a", "a", startMs: 0, endMs: 50),
            Span("fine-root", startMs: 0, endMs: 500));

        result.RejectedSpanIds.Should().BeEquivalentTo("a", "b");
        result.Rows.Select(row => row.Span.Id).Should().Equal("child-of-a", "fine-root");
        result.Rows[0].IsOrphan.Should().BeTrue("its parent was rejected");
        result.Rows[0].Depth.Should().Be(0);
    }

    [Fact]
    public void Build_RejectsASelfParentedSpan()
    {
        var result = Build(
            Span("loop", "loop", startMs: 0, endMs: 100),
            Span("ok", startMs: 0, endMs: 100));

        result.RejectedSpanIds.Should().Equal("loop");
        result.Rows.Select(row => row.Span.Id).Should().Equal("ok");
    }

    [Fact]
    public void Build_KeepsTheFirstOccurrenceOfADuplicateId_AndSkipsBlankIds()
    {
        var result = Build(
            Span("dup", startMs: 0, endMs: 100),
            Span("dup", startMs: 0, endMs: 900),
            Span("", startMs: 0, endMs: 100),
            Span("  ", startMs: 0, endMs: 100));

        result.Rows.Select(row => row.Span.Id).Should().Equal("dup");
        result.Rows[0].Duration.Should().Be(TimeSpan.FromMilliseconds(100), "the first occurrence wins");
    }

    // ---------- axis and bars ----------

    [Fact]
    public void Build_ComputesTheAxisFromTheEarliestStartAndTheLatestEnd()
    {
        var result = Build(
            Span("root", startMs: 100, endMs: 900),
            Span("late", "root", startMs: 300, endMs: 1_200));

        result.Axis.Start.Should().Be(T0.AddMilliseconds(100));
        result.Axis.Duration.Should().Be(TimeSpan.FromMilliseconds(1_100));
    }

    [Fact]
    public void Build_MeasuresEachBarAsAPercentageOfTheAxis()
    {
        var result = Build(
            Span("root", startMs: 0, endMs: 1_000),
            Span("child", "root", startMs: 250, endMs: 750));

        result.Rows[0].OffsetPercent.Should().BeApproximately(0, 0.001);
        result.Rows[0].WidthPercent.Should().BeApproximately(100, 0.001);
        result.Rows[1].OffsetPercent.Should().BeApproximately(25, 0.001);
        result.Rows[1].WidthPercent.Should().BeApproximately(50, 0.001);
        result.Rows[0].IsVisible.Should().BeTrue();
    }

    [Fact]
    public void Build_ExtendsARunningSpanToNow()
    {
        var now = T0.AddMilliseconds(400);
        var result = TraceWaterfallLayout.Build(
            [Span("root", startMs: 0, endMs: 1_000), Span("running", "root", startMs: 100, status: TraceSpanStatus.Running)],
            now);

        var running = result.Rows[1];
        running.Duration.Should().Be(TimeSpan.FromMilliseconds(300));
        running.OffsetPercent.Should().BeApproximately(10, 0.001);
        running.WidthPercent.Should().BeApproximately(30, 0.001);
        result.RunningCount.Should().Be(1);
        result.Axis.Duration.Should().Be(TimeSpan.FromMilliseconds(1_000),
            "the axis still ends at the latest known end, which is the root's");
    }

    [Fact]
    public void Build_ClampsAnEndBeforeItsStartToAZeroLengthSpan()
    {
        var result = Build(
            Span("root", startMs: 0, endMs: 1_000),
            Span("broken", "root", startMs: 500, endMs: 100));

        result.Rows[1].Duration.Should().Be(TimeSpan.Zero);
        result.Rows[1].WidthPercent.Should().Be(0);
        result.Rows[1].OffsetPercent.Should().BeApproximately(50, 0.001);
    }

    [Fact]
    public void Build_FindsTheSlowestSpan_AndKeepsTheFirstOnATie()
    {
        var result = Build(
            Span("root", startMs: 0, endMs: 1_000),
            Span("short", "root", startMs: 0, endMs: 100),
            Span("slow", "root", startMs: 0, endMs: 600),
            Span("slow-twin", "root", startMs: 0, endMs: 600));

        result.SlowestSpanId.Should().Be("slow");
        result.Rows.Single(row => row.IsSlowest).Span.Id.Should().Be("slow");
        result.Rows[0].IsSlowest.Should().BeFalse(
            "the root measures the whole request, so it can never be the highlight");
    }

    // ---------- subtree metrics ----------

    [Fact]
    public void Build_SumsEveryMetricOverEachSubtree()
    {
        var result = Build(
            Span("root", startMs: 0, endMs: 1_000, metrics: Metrics(("tokens", 10), ("cost", 1))),
            Span("child-a", "root", startMs: 0, endMs: 400, metrics: Metrics(("tokens", 5), ("cost", 2))),
            Span("grandchild", "child-a", startMs: 0, endMs: 200, metrics: Metrics(("tokens", 3))),
            Span("child-b", "root", startMs: 400, endMs: 1_000, metrics: Metrics(("tokens", 7))));

        result.Rows[0].SubtreeMetrics["tokens"].Should().Be(25, "10 + 5 + 3 + 7, each span counted once");
        result.Rows[0].SubtreeMetrics["cost"].Should().Be(3);
        result.Rows[1].SubtreeMetrics["tokens"].Should().Be(8, "5 + 3, the child's own value plus its child");
        result.Rows[1].SubtreeMetrics["cost"].Should().Be(2);
        result.Rows[2].SubtreeMetrics["tokens"].Should().Be(3);
        result.Rows[2].SubtreeMetrics.Should().NotContainKey("cost",
            "neither the grandchild nor any descendant reports a cost, so the key stays absent");
        result.Rows[3].SubtreeMetrics.Should().NotContainKey("cost",
            "child-b reports no cost, so the key is absent rather than zero");
    }

    // ---------- zoom window ----------

    [Fact]
    public void Build_MeasuresTheBarsAgainstTheZoomedWindow()
    {
        var spans = new[]
        {
            Span("root", startMs: 0, endMs: 1_000),
            Span("late", "root", startMs: 500, endMs: 1_000)
        };

        var result = TraceWaterfallLayout.Build(
            spans,
            T0.AddMilliseconds(1_000),
            new TraceWaterfallWindow(0.5, 0.5));

        result.Axis.Start.Should().Be(T0.AddMilliseconds(500));
        result.Axis.Duration.Should().Be(TimeSpan.FromMilliseconds(500));
        result.TotalDuration.Should().Be(TimeSpan.FromMilliseconds(1_000),
            "the summary reports the whole trace, so zooming does not change what the trace is");
        result.Rows[1].OffsetPercent.Should().BeApproximately(0, 0.001);
        result.Rows[1].WidthPercent.Should().BeApproximately(100, 0.001);
        result.Rows[0].IsVisible.Should().BeTrue("the root still overlaps the window");
        result.Rows[0].WidthPercent.Should().BeApproximately(100, 0.001, "the root is clamped into the window");
    }

    [Fact]
    public void Build_ClampsAnInvalidWindowIntoAUsableOne()
    {
        var result = TraceWaterfallLayout.Build(
            [Span("root", startMs: 0, endMs: 1_000)],
            T0.AddMilliseconds(1_000),
            new TraceWaterfallWindow(-2, 0));

        result.Axis.Duration.Should().Be(TimeSpan.FromMilliseconds(1_000),
            "a window with no width is normalized back to the full trace");
    }

    [Fact]
    public void Build_KeepsASpanOutsideTheWindowAsANonVisibleRow()
    {
        var result = TraceWaterfallLayout.Build(
            [
                Span("early", startMs: 0, endMs: 200),
                Span("late", startMs: 800, endMs: 1_000)
            ],
            T0.AddMilliseconds(1_000),
            new TraceWaterfallWindow(0.5, 0.5));

        result.Rows.Select(row => row.Span.Id).Should().Equal("early", "late");
        result.Rows[0].IsVisible.Should().BeFalse("the row still renders, so its data is never lost");
        result.Rows[0].WidthPercent.Should().Be(0);
    }

    // ---------- ticks ----------

    [Fact]
    public void Build_ProducesNiceTicks_InTheRangesOwnUnit()
    {
        var result = Build(Span("root", startMs: 0, endMs: 1_000));

        result.Axis.Unit.Should().Be(TraceAxisUnit.Seconds);
        result.Axis.Ticks.Select(tick => tick.Offset).Should().Equal(
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromSeconds(1));
        result.Axis.Ticks[^1].Percent.Should().BeApproximately(100, 0.001);
        result.Axis.FormatTick(result.Axis.Ticks[^1], English).Should().Be("1");
        result.Axis.FormatTick(result.Axis.Ticks[1], Czech).Should().Be("0,5");
    }

    [Theory]
    [InlineData(500, "Ms")]
    [InlineData(1_000, "Seconds")]
    [InlineData(59_000, "Seconds")]
    [InlineData(60_000, "Minutes")]
    public void AxisUnit_FollowsTheVisibleRange(int durationMs, string expected)
    {
        TraceWaterfallFormat.AxisUnit(TimeSpan.FromMilliseconds(durationMs))
            .ToString().Should().Be(expected);
    }

    // ---------- duration formatting ----------

    [Theory]
    [InlineData(820, "820", "Ms")]
    [InlineData(1_240, "1.24", "Seconds")]
    [InlineData(150_000, "2.5", "Minutes")]
    public void FormatDuration_ReportsTheNumberInTheUnitItReadsBestIn(
        int durationMs,
        string expected,
        string unit)
    {
        var (text, actualUnit) = TraceWaterfallFormat.FormatDuration(
            TimeSpan.FromMilliseconds(durationMs),
            English);

        text.Should().Be(expected);
        actualUnit.ToString().Should().Be(unit);
    }

    [Fact]
    public void FormatMetric_UsesTheColumnsFormatAndTheCulture()
    {
        TraceWaterfallFormat.FormatMetric(1_234.6, null, English).Should().Be("1,235");
        TraceWaterfallFormat.FormatMetric(1_234.5, "N1", Czech).Should().Be("1\u00A0234,5");
    }

    [Fact]
    public void Format_ExposesTheLocalizedUnitKeyForEveryUnit()
    {
        TraceWaterfallFormat.UnitKey(TraceAxisUnit.Ms).Should().Be("TmTraceWaterfall_UnitMs");
        TraceWaterfallFormat.UnitKey(TraceAxisUnit.Seconds).Should().Be("TmTraceWaterfall_UnitSeconds");
        TraceWaterfallFormat.UnitKey(TraceAxisUnit.Minutes).Should().Be("TmTraceWaterfall_UnitMinutes");
    }
}
