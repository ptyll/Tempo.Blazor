using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Charts;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Charts;

public class TmStockChartTests : LocalizationTestBase
{
    private static List<StockChartDataPoint> SampleData => new()
    {
        new(new DateTime(2026, 1, 1), 100, 110, 95, 105, 1000),
        new(new DateTime(2026, 1, 2), 105, 108, 102, 106, 1200),
        new(new DateTime(2026, 1, 3), 106, 115, 104, 114, 1500),
        new(new DateTime(2026, 1, 4), 114, 114, 108, 110, 900),
        new(new DateTime(2026, 1, 5), 110, 112, 109, 111, 1100),
    };

    [Fact]
    public void Render_SvgElement_Exists()
    {
        var cut = Render<TmStockChart>(p => p
            .Add(x => x.Data, SampleData)
            .Add(x => x.Type, StockChartType.Candlestick));

        cut.Find("svg").Should().NotBeNull();
        cut.Find(".tm-stock-chart").Should().NotBeNull();
    }

    [Fact]
    public void Candlestick_RendersRects()
    {
        var cut = Render<TmStockChart>(p => p
            .Add(x => x.Data, SampleData)
            .Add(x => x.Type, StockChartType.Candlestick));

        // Each data point renders a candlestick body rect
        cut.FindAll("rect.tm-stock-chart__body").Count.Should().Be(5);
    }

    [Fact]
    public void Candlestick_BullColor_WhenCloseGreaterThanOpen()
    {
        // Jan 3: Open 106, Close 114 → bull
        var data = new List<StockChartDataPoint>
        {
            new(new DateTime(2026, 1, 3), 106, 115, 104, 114, 1500)
        };

        var cut = Render<TmStockChart>(p => p
            .Add(x => x.Data, data)
            .Add(x => x.Type, StockChartType.Candlestick));

        var body = cut.Find("rect.tm-stock-chart__body");
        body.GetAttribute("fill").Should().Be("var(--tm-chart-positive)",
            "a bull candle takes the semantic positive token since TG-002");
    }

    [Fact]
    public void Candlestick_BearColor_WhenCloseLessThanOpen()
    {
        // Jan 4: Open 114, Close 110 → bear
        var data = new List<StockChartDataPoint>
        {
            new(new DateTime(2026, 1, 4), 114, 114, 108, 110, 900)
        };

        var cut = Render<TmStockChart>(p => p
            .Add(x => x.Data, data)
            .Add(x => x.Type, StockChartType.Candlestick));

        var body = cut.Find("rect.tm-stock-chart__body");
        body.GetAttribute("fill").Should().Be("var(--tm-chart-negative)",
            "a bear candle takes the semantic negative token since TG-002");
    }

    [Fact]
    public void Candlestick_Wicks_RenderedAsLines()
    {
        var cut = Render<TmStockChart>(p => p
            .Add(x => x.Data, SampleData)
            .Add(x => x.Type, StockChartType.Candlestick));

        // Each data point has one wick line
        cut.FindAll("line.tm-stock-chart__wick").Count.Should().Be(5);
    }

    [Fact]
    public void OHLC_RendersLines()
    {
        var cut = Render<TmStockChart>(p => p
            .Add(x => x.Data, SampleData)
            .Add(x => x.Type, StockChartType.OHLC));

        // OHLC uses vertical range line + horizontal open/close ticks
        cut.FindAll("line.tm-stock-chart__ohlc").Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Line_RendersPolyline()
    {
        var cut = Render<TmStockChart>(p => p
            .Add(x => x.Data, SampleData)
            .Add(x => x.Type, StockChartType.Line));

        cut.FindAll("polyline.tm-stock-chart__line").Count.Should().Be(1);
    }

    [Fact]
    public void Volume_RendersBars_WhenShowVolumeTrue()
    {
        var cut = Render<TmStockChart>(p => p
            .Add(x => x.Data, SampleData)
            .Add(x => x.Type, StockChartType.Candlestick)
            .Add(x => x.ShowVolume, true));

        cut.FindAll("rect.tm-stock-chart__volume").Count.Should().Be(5);
    }

    [Fact]
    public void Volume_Hidden_WhenShowVolumeFalse()
    {
        var cut = Render<TmStockChart>(p => p
            .Add(x => x.Data, SampleData)
            .Add(x => x.Type, StockChartType.Candlestick)
            .Add(x => x.ShowVolume, false));

        cut.FindAll("rect.tm-stock-chart__volume").Count.Should().Be(0);
    }

    [Fact]
    public void CustomClass_Applied()
    {
        var cut = Render<TmStockChart>(p => p
            .Add(x => x.Data, SampleData)
            .Add(x => x.Type, StockChartType.Candlestick)
            .Add(x => x.Class, "my-chart"));

        cut.Find(".tm-stock-chart").ClassList.Should().Contain("my-chart");
    }

    [Fact]
    public void EmptyData_ShowsPlaceholder()
    {
        var cut = Render<TmStockChart>(p => p
            .Add(x => x.Data, new List<StockChartDataPoint>())
            .Add(x => x.Type, StockChartType.Candlestick));

        cut.Find(".tm-stock-chart__empty").Should().NotBeNull();
    }

    [Fact]
    public void Animated_HasAnimatedClass()
    {
        var cut = Render<TmStockChart>(p => p
            .Add(x => x.Data, SampleData)
            .Add(x => x.Type, StockChartType.Candlestick)
            .Add(x => x.Animated, true));

        cut.Find(".tm-stock-chart").ClassList.Should().Contain("tm-stock-chart--animated");
    }
}
