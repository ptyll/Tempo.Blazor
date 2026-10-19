using System.Globalization;
using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Charts;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Charts;

/// <summary>TDD tests for the TmChart DateTime X axis (TMPO-004).</summary>
public class TmChartTimeAxisTests : LocalizationTestBase
{
    private static ChartData TimeData(DateTime[] points, double[] values, string label = "Series") => new()
    {
        Labels = [],
        TimePoints = points,
        Datasets = [new ChartDataset { Label = label, Values = values, Color = "#3b82f6" }]
    };

    private static IDisposable UseCulture(string name)
    {
        return CultureScope.Use(name);
    }

    // ── TIM-1: nerovnoměrná řada → proporcionální X pozice ─────────────────

    [Fact]
    public void TimeAxis_UnevenSeries_ScalesProportionally()
    {
        // 9-day span: Jan 1, Jan 2, Jan 10. Chart X range: PL=50 … PL+CW=580.
        var data = TimeData(
            [new DateTime(2025, 1, 1), new DateTime(2025, 1, 2), new DateTime(2025, 1, 10)],
            [10, 20, 30]);

        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Line)
            .Add(x => x.Data, data));

        var cxs = cut.FindAll("circle.tm-chart__point")
            .Select(c => double.Parse(c.GetAttribute("cx")!, CultureInfo.InvariantCulture))
            .OrderBy(v => v)
            .ToList();

        cxs.Should().HaveCount(3);
        cxs[0].Should().Be(50);                       // t = 0/9
        cxs[1].Should().BeApproximately(108.9, 0.5);  // t = 1/9 → 50 + 530/9
        cxs[2].Should().Be(580);                      // t = 9/9
    }

    // ── TIM-2: neseřazená data se interně setřídí (polyline X monotónní) ───

    [Fact]
    public void TimeAxis_UnsortedInput_IsSortedInternally()
    {
        var data = TimeData(
            [new DateTime(2025, 3, 1), new DateTime(2025, 1, 1), new DateTime(2025, 2, 1)],
            [30, 10, 20]);

        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Line)
            .Add(x => x.Data, data));

        var points = cut.Find("polyline.tm-chart__line").GetAttribute("points")!
            .Split(' ')
            .Select(pt => pt.Split(','))
            .Select(pt => (X: double.Parse(pt[0], CultureInfo.InvariantCulture),
                           Y: double.Parse(pt[1], CultureInfo.InvariantCulture)))
            .ToList();

        points.Select(pt => pt.X).Should().BeInAscendingOrder();
        // Jan(10) is the lowest value → highest Y; Mar(30) the highest value → lowest Y.
        points[0].Y.Should().BeGreaterThan(points[2].Y);
    }

    // ── TIM-3: duplicitní timestampy — poslední vyhrává ────────────────────

    [Fact]
    public void TimeAxis_DuplicateTimestamps_LastValueWins()
    {
        var duplicate = new DateTime(2025, 6, 1);
        var data = TimeData(
            [new DateTime(2025, 5, 1), duplicate, duplicate],
            [10, 20, 40]);

        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Line)
            .Add(x => x.Data, data));

        // Only two rendered points remain (May 1 + deduped Jun 1).
        var circles = cut.FindAll("circle.tm-chart__point");
        circles.Should().HaveCount(2);

        // The Jun 1 point carries the LAST value (40 = domain max → y at the top: PT = 20).
        var topPoint = circles.Select(c => double.Parse(c.GetAttribute("cy")!, CultureInfo.InvariantCulture)).Min();
        topPoint.Should().Be(20);
    }

    // ── TIM-4: rozsah měsíců → měsíční popisky dle kultury (en) ────────────

    [Fact]
    public void TimeAxis_MonthRange_ShowsEnglishMonthLabels()
    {
        using var _ = UseCulture("en-US");
        var points = Enumerable.Range(0, 12).Select(m => new DateTime(2025, 1, 15).AddMonths(m)).ToArray();
        var data = TimeData(points, Enumerable.Range(1, 12).Select(v => (double)v).ToArray());

        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Line)
            .Add(x => x.Data, data));

        cut.Markup.Should().Contain("Feb");
    }

    // ── TIM-5: stejná data v cs kultuře → české měsíční popisky ────────────

    [Fact]
    public void TimeAxis_MonthRange_ShowsCzechMonthLabels()
    {
        using var _ = UseCulture("cs-CZ");
        var points = Enumerable.Range(0, 12).Select(m => new DateTime(2025, 1, 15).AddMonths(m)).ToArray();
        var data = TimeData(points, Enumerable.Range(1, 12).Select(v => (double)v).ToArray());

        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Line)
            .Add(x => x.Data, data));

        var cs = new CultureInfo("cs-CZ");
        var expected = new DateTime(2025, 2, 1).ToString("MMM", cs); // "úno"
        cut.Markup.Should().Contain(expected);
    }

    // ── TIM-6: rozsah > 10 let → roční popisky ─────────────────────────────

    [Fact]
    public void TimeAxis_DecadeRange_ShowsYearLabels()
    {
        var points = Enumerable.Range(0, 15).Select(y => new DateTime(2010 + y, 6, 1)).ToArray();
        var data = TimeData(points, Enumerable.Range(1, 15).Select(v => (double)v).ToArray());

        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Line)
            .Add(x => x.Data, data));

        cut.Markup.Should().MatchRegex(@"20\d{2}</text>", "year-scale labels must render standalone years");
        cut.Markup.Should().NotContain("Jun", "year-scale labels must not include month names");
    }

    // ── TIM-7: rozsah < 1 den → časové popisky ─────────────────────────────

    [Fact]
    public void TimeAxis_SubDayRange_ShowsTimeLabels()
    {
        using var _ = UseCulture("en-US");
        var start = new DateTime(2025, 6, 1, 8, 0, 0);
        var points = Enumerable.Range(0, 6).Select(h => start.AddHours(h)).ToArray();
        var data = TimeData(points, [1, 2, 3, 4, 5, 6]);

        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Line)
            .Add(x => x.Data, data));

        cut.Markup.Should().MatchRegex(@"\d{1,2}:\d{2}");
    }

    // ── TIM-8: popisky se prořeďují — nikdy víc než 8 ──────────────────────

    [Fact]
    public void TimeAxis_ManyPoints_ThinsLabelsToAvoidOverlap()
    {
        var start = new DateTime(2024, 1, 1);
        var points = Enumerable.Range(0, 120).Select(d => start.AddDays(d)).ToArray();
        var data = TimeData(points, Enumerable.Range(1, 120).Select(v => (double)v).ToArray());

        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Line)
            .Add(x => x.Data, data));

        cut.FindAll("text.tm-chart__label").Count.Should().BeLessThanOrEqualTo(8);
    }

    // ── TIM-9: vlastní TimeLabelFormat má přednost ─────────────────────────

    [Fact]
    public void TimeAxis_CustomLabelFormat_IsApplied()
    {
        var points = Enumerable.Range(0, 6).Select(m => new DateTime(2025, 1, 1).AddMonths(m)).ToArray();
        var data = TimeData(points, [1, 2, 3, 4, 5, 6]);

        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Line)
            .Add(x => x.Data, data)
            .Add(x => x.TimeLabelFormat, "yyyy-MM"));

        cut.Markup.Should().Contain("2025-03");
    }

    // ── TIM-10: tooltip zobrazuje plné datum ───────────────────────────────

    [Fact]
    public void TimeAxis_Tooltip_ShowsFullDate()
    {
        using var _ = UseCulture("en-US");
        var points = new[] { new DateTime(2025, 1, 1), new DateTime(2025, 4, 15), new DateTime(2025, 7, 1) };
        var data = TimeData(points, [10, 20, 30]);

        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Line)
            .Add(x => x.Data, data));

        cut.FindAll("circle.tm-chart__point")[1].TriggerEvent("onmouseover",
            new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        var tooltip = cut.Find("[data-testid='chart-tooltip']").TextContent;
        tooltip.Should().Contain(new DateTime(2025, 4, 15).ToString("d", new CultureInfo("en-US")));
    }

    // ── TIM-11: jediné datum → bod uprostřed, bez pádu ─────────────────────

    [Fact]
    public void TimeAxis_SingleDate_RendersCenteredPoint()
    {
        var data = TimeData([new DateTime(2025, 5, 1)], [42]);

        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Line)
            .Add(x => x.Data, data));

        var circle = cut.Find("circle.tm-chart__point");
        double.Parse(circle.GetAttribute("cx")!, CultureInfo.InvariantCulture)
            .Should().Be(315); // PL + CW/2 = 50 + 265
    }

    // ── TIM-12: Area typ používá časovou osu stejně ────────────────────────

    [Fact]
    public void TimeAxis_AreaChart_UsesProportionalX()
    {
        var data = TimeData(
            [new DateTime(2025, 1, 1), new DateTime(2025, 1, 2), new DateTime(2025, 1, 10)],
            [10, 20, 30]);

        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Area)
            .Add(x => x.Data, data));

        cut.FindAll("path.tm-chart__area").Should().HaveCount(1);
        var cxs = cut.FindAll("circle.tm-chart__point")
            .Select(c => double.Parse(c.GetAttribute("cx")!, CultureInfo.InvariantCulture))
            .OrderBy(v => v)
            .ToList();
        cxs[1].Should().BeApproximately(108.9, 0.5);
    }

    // ── TIM-13: TimeAxisMin/Max vymezí rozsah osy ──────────────────────────

    [Fact]
    public void TimeAxis_ExplicitRange_ClampsAxis()
    {
        // Axis runs Jan 1 – Jan 21 (20 days); the single data point on Jan 11 sits mid-axis.
        var data = TimeData([new DateTime(2025, 1, 11)], [10]);

        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Line)
            .Add(x => x.Data, data)
            .Add(x => x.TimeAxisMin, new DateTime(2025, 1, 1))
            .Add(x => x.TimeAxisMax, new DateTime(2025, 1, 21)));

        var circle = cut.Find("circle.tm-chart__point");
        double.Parse(circle.GetAttribute("cx")!, CultureInfo.InvariantCulture)
            .Should().Be(315); // midpoint of the axis
    }

    // ── TIM-14: zpětná kompatibilita — bez TimePoints kategorická osa ──────

    [Fact]
    public void NoTimePoints_KeepsCategoricalAxis()
    {
        var data = new ChartData
        {
            Labels = ["Jan", "Feb", "Mar"],
            Datasets = [new ChartDataset { Label = "S", Values = [1, 2, 3], Color = "#3b82f6" }]
        };

        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Line)
            .Add(x => x.Data, data));

        var labels = cut.FindAll("text.tm-chart__label");
        labels.Should().HaveCount(3);
        labels[0].TextContent.Should().Be("Jan");
        // Categorical spacing: evenly distributed.
        var cxs = cut.FindAll("circle.tm-chart__point")
            .Select(c => double.Parse(c.GetAttribute("cx")!, CultureInfo.InvariantCulture))
            .ToList();
        cxs.Should().Equal([50, 315, 580]);
    }

    // ── TIM-15: prázdné TimePoints + prázdné Labels → empty state ──────────

    [Fact]
    public void EmptyTimePointsAndLabels_ShowsEmptyState()
    {
        var data = new ChartData
        {
            Labels = [],
            TimePoints = [],
            Datasets = [new ChartDataset { Label = "S", Values = [], Color = "#3b82f6" }]
        };

        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Line)
            .Add(x => x.Data, data));

        cut.Find(".tm-chart__empty").Should().NotBeNull();
    }
}
