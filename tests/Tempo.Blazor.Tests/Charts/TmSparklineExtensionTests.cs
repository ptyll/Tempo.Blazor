using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Charts;
using Tempo.Blazor.Components.Common;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Charts;

/// <summary>
/// Plan ff043b2e, phase 3: the additive <see cref="TmSparkline"/> extensions — <c>AriaLabel</c>,
/// <c>Color</c>, <c>ShowMinMax</c>, <c>ShowLastPoint</c> and <c>ShowTooltip</c>. Every new default
/// keeps the pre-2.9.1 markup, so an existing host renders exactly what it rendered before.
/// </summary>
public class TmSparklineExtensionTests : LocalizationTestBase
{
    private static double[] SampleData => [10, 25, 15, 30, 20, 35, 28];

    private ITmLocalizer Localizer => Services.GetRequiredService<ITmLocalizer>();

    // ── Backward compatibility ──

    [Fact]
    public void Defaults_KeepThePreExistingMarkup()
    {
        var cut = Render<TmSparkline>(p => p.Add(x => x.Data, SampleData));

        cut.Find("svg").GetAttribute("tabindex").Should().BeNull();
        cut.FindAll(".tm-sparkline__last-point").Count.Should().Be(0);
        cut.FindAll(".tm-sparkline__minmax").Count.Should().Be(0);
        cut.FindAll("[aria-live]").Count.Should().Be(0);
        cut.FindAll(".tm-sparkline__tooltip").Count.Should().Be(0);
        cut.Find("polyline.tm-sparkline__line").GetAttribute("stroke").Should().Be("#3b82f6");
    }

    // ── AriaLabel ──

    [Fact]
    public void AriaLabel_DefaultsToALocalizedMinMaxLastSummary()
    {
        var cut = Render<TmSparkline>(p => p.Add(x => x.Data, SampleData));

        var expected = string.Format(
            Localizer["TmSparkline_Summary"],
            Format(10), Format(35), Format(28));

        cut.Find("svg").GetAttribute("aria-label").Should().Be(expected);
    }

    [Fact]
    public void AriaLabel_SummarySpeaksTheCurrentCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("cs-CZ");
            var cut = Render<TmSparkline>(p => p.Add(x => x.Data, [1.5, 2.5, 2]));

            cut.Find("svg").GetAttribute("aria-label").Should().Contain("2,5");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void AriaLabel_CustomValueWins()
    {
        var cut = Render<TmSparkline>(p => p
            .Add(x => x.Data, SampleData)
            .Add(x => x.AriaLabel, "Open tickets per day"));

        cut.Find("svg").GetAttribute("aria-label").Should().Be("Open tickets per day");
    }

    [Fact]
    public void AriaLabel_StaysThePlainSparklineLabelForEmptyData()
    {
        var cut = Render<TmSparkline>(p => p.Add(x => x.Data, Array.Empty<double>()));

        cut.Find(".tm-sparkline__empty").Should().NotBeNull();
        cut.FindAll("svg").Count.Should().Be(0);
    }

    // ── Element ──

    [Fact]
    public void Element_DefaultsToDiv_SoExistingHostsAreUnchanged()
    {
        var cut = Render<TmSparkline>(p => p.Add(x => x.Data, SampleData));

        cut.Find(".tm-sparkline").TagName.Should().Be("DIV");
    }

    [Fact]
    public void Element_Span_RendersAPhrasingRoot()
    {
        var cut = Render<TmSparkline>(p => p
            .Add(x => x.Data, SampleData)
            .Add(x => x.Element, TmRootElement.Span));

        cut.Find(".tm-sparkline").TagName.Should().Be("SPAN");
        cut.Find(".tm-sparkline").GetAttribute("style").Should().Contain("width:");
        cut.Find("polyline.tm-sparkline__line").Should().NotBeNull("the body is unchanged");
    }

    [Fact]
    public void Element_AnUndefinedValue_FallsBackToDiv()
    {
        var cut = Render<TmSparkline>(p => p
            .Add(x => x.Data, SampleData)
            .Add(x => x.Element, (TmRootElement)99));

        cut.Find(".tm-sparkline").TagName.Should().Be("DIV");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Element_Span_WithNoData_RendersPhrasingContentOnly(bool nullData)
    {
        var cut = Render<TmSparkline>(p => p
            .Add(x => x.Data, nullData ? null : Array.Empty<double>())
            .Add(x => x.Element, TmRootElement.Span));

        cut.FindAll("div").Count.Should().Be(
            0, "a <span> root may hold phrasing content only; the empty state is no exception");
        cut.Find(".tm-sparkline__empty").TagName.Should().Be("SPAN");
    }

    // ── Color ──

    [Theory]
    [InlineData(SparklineType.Line)]
    [InlineData(SparklineType.Area)]
    [InlineData(SparklineType.Bar)]
    public void Color_PaintsTheSeries(SparklineType type)
    {
        var cut = Render<TmSparkline>(p => p
            .Add(x => x.Data, SampleData)
            .Add(x => x.Type, type)
            .Add(x => x.Color, "var(--tm-color-success)"));

        cut.FindAll("[stroke='var(--tm-color-success)'], [fill='var(--tm-color-success)']")
            .Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Color_IsIgnoredByPie_WhichKeepsItsCategoricalPalette()
    {
        var cut = Render<TmSparkline>(p => p
            .Add(x => x.Data, SampleData)
            .Add(x => x.Type, SparklineType.Pie)
            .Add(x => x.Color, "var(--tm-color-success)"));

        cut.FindAll("[fill='var(--tm-color-success)']").Count.Should().Be(0);
        cut.FindAll("path.tm-sparkline__slice").Count.Should().Be(7);
    }

    // ── ShowMinMax ──

    [Fact]
    public void ShowMinMax_RendersLocalizedMinAndMaxLabels()
    {
        var cut = Render<TmSparkline>(p => p
            .Add(x => x.Data, SampleData)
            .Add(x => x.ShowMinMax, true));

        var minmax = cut.Find(".tm-sparkline__minmax");
        minmax.TextContent.Should().Be(
            string.Format(Localizer["TmSparkline_MinMax"], Format(10), Format(35)));
        minmax.GetAttribute("aria-hidden").Should().Be(
            "true", "the numbers already live in the svg's aria-label summary");
    }

    [Fact]
    public void ShowMinMax_IsOmittedForEmptyData()
    {
        var cut = Render<TmSparkline>(p => p
            .Add(x => x.Data, Array.Empty<double>())
            .Add(x => x.ShowMinMax, true));

        cut.FindAll(".tm-sparkline__minmax").Count.Should().Be(0);
    }

    // ── ShowLastPoint ──

    [Theory]
    [InlineData(SparklineType.Line)]
    [InlineData(SparklineType.Area)]
    public void ShowLastPoint_MarksTheFinalValue(SparklineType type)
    {
        var cut = Render<TmSparkline>(p => p
            .Add(x => x.Data, SampleData)
            .Add(x => x.Type, type)
            .Add(x => x.ShowLastPoint, true));

        var markers = cut.FindAll("circle.tm-sparkline__last-point");
        markers.Count.Should().Be(1);
        markers[0].GetAttribute("data-value").Should().Be(Format(28));
        markers[0].GetAttribute("aria-hidden").Should().Be("true");
    }

    [Fact]
    public void ShowLastPoint_IsOmittedForBarAndPie()
    {
        var bar = Render<TmSparkline>(p => p
            .Add(x => x.Data, SampleData)
            .Add(x => x.Type, SparklineType.Bar)
            .Add(x => x.ShowLastPoint, true));
        bar.FindAll("circle.tm-sparkline__last-point").Count.Should().Be(0);

        var pie = Render<TmSparkline>(p => p
            .Add(x => x.Data, SampleData)
            .Add(x => x.Type, SparklineType.Pie)
            .Add(x => x.ShowLastPoint, true));
        pie.FindAll("circle.tm-sparkline__last-point").Count.Should().Be(0);
    }

    // ── ShowTooltip ──

    [Fact]
    public void ShowTooltip_MakesTheChartFocusableWithAKeyHint()
    {
        var cut = Render<TmSparkline>(p => p
            .Add(x => x.Data, SampleData)
            .Add(x => x.ShowTooltip, true));

        var svg = cut.Find("svg");
        svg.GetAttribute("tabindex").Should().Be("0");
        svg.GetAttribute("aria-keyshortcuts").Should().Contain("ArrowRight");

        var describedBy = svg.GetAttribute("aria-describedby");
        describedBy.Should().NotBeNullOrWhiteSpace();
        cut.Find($"#{describedBy}").TextContent.Should().Be(Localizer["TmSparkline_KeyboardHint"]);
    }

    [Fact]
    public void ShowTooltip_AddsExactlyOnePoliteLiveRegion()
    {
        var cut = Render<TmSparkline>(p => p
            .Add(x => x.Data, SampleData)
            .Add(x => x.ShowTooltip, true));

        var regions = cut.FindAll("[aria-live]");
        regions.Count.Should().Be(1);
        regions[0].GetAttribute("aria-live").Should().Be("polite");
        regions[0].GetAttribute("role").Should().Be("status");
        regions[0].TextContent.Should().BeEmpty();
    }

    [Fact]
    public void ShowTooltip_HoveringABarShowsItsValue()
    {
        var cut = Render<TmSparkline>(p => p
            .Add(x => x.Data, SampleData)
            .Add(x => x.Type, SparklineType.Bar)
            .Add(x => x.ShowTooltip, true));

        cut.FindAll("rect.tm-sparkline__bar")[3].MouseOver();

        var tip = cut.Find(".tm-sparkline__tooltip");
        tip.TextContent.Should().Contain(Format(30));
        cut.Find("[aria-live]").TextContent.Should().Contain(Format(30)).And.Contain("4");
    }

    [Fact]
    public void ShowTooltip_ArrowKeysWalkThePoints()
    {
        var cut = Render<TmSparkline>(p => p
            .Add(x => x.Data, SampleData)
            .Add(x => x.ShowTooltip, true));

        cut.Find("svg").KeyDown("ArrowRight");
        cut.Find(".tm-sparkline__tooltip").TextContent.Should().Contain(Format(10));

        cut.Find("svg").KeyDown("ArrowRight");
        cut.Find(".tm-sparkline__tooltip").TextContent.Should().Contain(Format(25));

        cut.Find("svg").KeyDown("ArrowLeft");
        cut.Find(".tm-sparkline__tooltip").TextContent.Should().Contain(Format(10));
    }

    [Fact]
    public void ShowTooltip_ArrowLeftAsTheFirstPressLandsOnTheLastPoint()
    {
        var cut = Render<TmSparkline>(p => p
            .Add(x => x.Data, SampleData)
            .Add(x => x.ShowTooltip, true));

        cut.Find("svg").KeyDown("ArrowLeft");

        cut.Find(".tm-sparkline__tooltip").TextContent.Should().Contain(Format(28));
    }

    [Fact]
    public void ShowTooltip_EscapeHidesItAndBlurClearsIt()
    {
        var cut = Render<TmSparkline>(p => p
            .Add(x => x.Data, SampleData)
            .Add(x => x.ShowTooltip, true));

        cut.Find("svg").KeyDown("ArrowRight");
        cut.FindAll(".tm-sparkline__tooltip").Count.Should().Be(1);

        cut.Find("svg").KeyDown("Escape");
        cut.FindAll(".tm-sparkline__tooltip").Count.Should().Be(0);

        cut.Find("svg").KeyDown("ArrowRight");
        cut.FindAll(".tm-sparkline__tooltip").Count.Should().Be(1);

        cut.Find("svg").Blur();
        cut.FindAll(".tm-sparkline__tooltip").Count.Should().Be(0);
    }

    [Fact]
    public void ShowTooltip_AnnouncementNamesThePointAndTheValue()
    {
        var cut = Render<TmSparkline>(p => p
            .Add(x => x.Data, SampleData)
            .Add(x => x.ShowTooltip, true));

        cut.Find("svg").KeyDown("ArrowRight");

        cut.Find("[aria-live]").TextContent.Should().Be(
            string.Format(Localizer["TmSparkline_PointAnnouncement"], 1, 7, Format(10)));
    }

    [Fact]
    public void ShowTooltip_MarksTheActivePointSoColorIsNeverTheOnlyCarrier()
    {
        var cut = Render<TmSparkline>(p => p
            .Add(x => x.Data, SampleData)
            .Add(x => x.ShowTooltip, true));

        cut.Find("svg").KeyDown("ArrowRight");
        cut.Find("svg").KeyDown("ArrowRight");

        var active = cut.FindAll(".tm-sparkline__point--active");
        active.Count.Should().Be(1);
        active[0].GetAttribute("data-index").Should().Be("1");
    }

    [Fact]
    public void ShowTooltip_HoveringAPointOfALineChartHighlightsIt()
    {
        var cut = Render<TmSparkline>(p => p
            .Add(x => x.Data, SampleData)
            .Add(x => x.ShowTooltip, true));

        cut.FindAll(".tm-sparkline__point")[2].MouseOver();

        cut.FindAll(".tm-sparkline__point--active").Count.Should().Be(1);
        cut.Find(".tm-sparkline__tooltip").TextContent.Should().Contain(Format(15));
    }

    [Fact]
    public void ShowTooltip_ALateMouseoutOfThePreviousPointDoesNotBlankTheNewCard()
    {
        var cut = Render<TmSparkline>(p => p
            .Add(x => x.Data, SampleData)
            .Add(x => x.ShowTooltip, true));

        cut.FindAll(".tm-sparkline__point")[0].MouseOver();
        cut.FindAll(".tm-sparkline__point")[3].MouseOver();
        cut.FindAll(".tm-sparkline__point")[0].MouseOut();

        cut.FindAll(".tm-sparkline__point--active").Count.Should().Be(1);
        cut.Find(".tm-sparkline__tooltip").TextContent.Should().Contain(Format(30));
    }

    [Fact]
    public void ShowTooltip_LeavingThePointHidesTheCard()
    {
        var cut = Render<TmSparkline>(p => p
            .Add(x => x.Data, SampleData)
            .Add(x => x.ShowTooltip, true));

        cut.FindAll(".tm-sparkline__point")[2].MouseOver();
        cut.FindAll(".tm-sparkline__tooltip").Count.Should().Be(1);

        cut.FindAll(".tm-sparkline__point")[2].MouseOut();

        cut.FindAll(".tm-sparkline__tooltip").Count.Should().Be(0);
    }

    [Fact]
    public void ShowTooltip_IsOmittedForPie()
    {
        var cut = Render<TmSparkline>(p => p
            .Add(x => x.Data, SampleData)
            .Add(x => x.Type, SparklineType.Pie)
            .Add(x => x.ShowTooltip, true));

        cut.Find("svg").GetAttribute("tabindex").Should().BeNull();
        cut.FindAll("[aria-live]").Count.Should().Be(0);
    }

    private static string Format(double value) => value.ToString("0.##", CultureInfo.CurrentCulture);
}
