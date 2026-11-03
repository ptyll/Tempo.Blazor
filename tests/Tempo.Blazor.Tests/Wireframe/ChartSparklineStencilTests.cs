using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Configuration;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// Plan ff043b2e, phase 3: the chart's index tooltip and the sparkline's new opt-in knobs are
/// wireframe citizens too. A schema knob the canvas ignores is a knob that does nothing, so the
/// chart's native sketch has to DRAW the crosshair card and the sparkline's declarative pack has to
/// branch on the three new flags.
/// </summary>
public sealed class ChartSparklineStencilTests
{
    [Fact]
    public void ChartSchema_ExposesTheTooltipModeAndItsShowTooltip()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmChart");

        schema.Roles.Should().BeEquivalentTo("chart");
        schema.Props.Select(prop => prop.Name).Should().Contain(["showTooltip", "tooltipMode"]);

        var mode = schema.Props.Single(prop => prop.Name == "tooltipMode");
        mode.Type.Should().Be(PropType.Enum);
        mode.Options.Should().BeEquivalentTo(["point", "index"]);
        mode.Default.Should().Be("point", "Point is the chart's own default, so the sketch matches");
    }

    [Fact]
    public void SparklineSchema_ExposesThePhase3Flags()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmSparkline");

        schema.Props.Select(prop => prop.Name).Should()
            .Equal("type", "color", "showTooltip", "showMinMax", "showLastPoint");
        schema.Props.Where(prop => prop.Name.StartsWith("show", StringComparison.Ordinal))
            .Should().OnlyContain(prop => prop.Type == PropType.Bool);
    }

    [Fact]
    public void SparklinePack_RendersTheNewPartsConditionally()
    {
        var entry = new BuiltInStencilPackProvider().GetDefinitions()
            .Single(definition => definition.Type == "TmSparkline");

        entry.Props.Select(prop => prop.Name).Should()
            .Contain(["showTooltip", "showMinMax", "showLastPoint"]);

        entry.RenderSvg.Should().NotBeNull("the declarative pack render is what the canvas paints");

        var raw = Entry("TmSparkline");
        raw.Should().Contain("{showMinMax}");
        raw.Should().Contain("{showLastPoint}");
        raw.Should().Contain("{showTooltip}");
    }

    [Fact]
    public async Task ChartSketch_DrawsTheCrosshairCard_InIndexMode()
    {
        var svg = await RenderChartAsync(("tooltipMode", JsonSerializer.SerializeToElement("index")),
            ("showTooltip", JsonSerializer.SerializeToElement(true)));

        svg.Should().Contain("stroke-dasharray='3,3'", "the sketch draws the category crosshair");
    }

    [Fact]
    public async Task ChartSketch_StaysPlain_InPointMode()
    {
        var svg = await RenderChartAsync(("tooltipMode", JsonSerializer.SerializeToElement("point")),
            ("showTooltip", JsonSerializer.SerializeToElement(true)));

        svg.Should().NotContain("stroke-dasharray='3,3'");
    }

    [Fact]
    public async Task ChartSketch_StaysPlain_WhenTheTooltipIsOff()
    {
        var svg = await RenderChartAsync(("tooltipMode", JsonSerializer.SerializeToElement("index")),
            ("showTooltip", JsonSerializer.SerializeToElement(false)));

        svg.Should().NotContain("stroke-dasharray='3,3'");
    }

    private static string Entry(string type) => BuiltInStencilPackProvider.ReadPackJson()
        .Split('\n')
        .Single(line => line.Contains($"\"type\": \"{type}\""));

    private static async Task<string> RenderChartAsync(params (string Key, JsonElement Value)[] props)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTempoBlazorWireframe();
        using var provider = services.BuildServiceProvider();

        const int width = 400;
        const int height = 240;
        var element = new WireframeElement { Type = "TmChart", X = 0, Y = 0, W = width, H = height };
        foreach (var (key, value) in props)
        {
            element.Props[key] = value;
        }

        var page = new WireframePage { Name = "P", Width = width, Height = height };
        page.Elements.Add(element);

        return await provider.GetRequiredService<IWireframeSvgRenderer>().RenderPageAsync(page);
    }
}
