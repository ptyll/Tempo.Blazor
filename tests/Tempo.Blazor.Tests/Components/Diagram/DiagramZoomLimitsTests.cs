using System.Globalization;
using System.Text.RegularExpressions;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Stencils;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Diagram;

/// <summary>
/// R3 zoom contract: one shared 0.15–5.0 range for the buttons, wheel, pinch, zoomTo and
/// fit-to-view. The C# <see cref="DiagramZoomLimits"/> pair mirrors the JS MIN_SCALE / MAX_SCALE,
/// and the zoom buttons step between the two limits (clamp on the limit, never frozen).
/// </summary>
public class DiagramZoomLimitsTests : LocalizationTestBase
{
    public DiagramZoomLimitsTests()
    {
        Services.GetRequiredService<DiagramStencilRegistry>()
            .RegisterProvider(new BuiltInDiagramStencilProvider());
    }

    [Fact]
    public void JsLimits_MatchCSharpLimits()
    {
        var js = File.ReadAllText(Path.Combine(FindRepositoryRoot(),
            "src/Tempo.Blazor.DiagramEditor/wwwroot/js/diagram-editor.js"));

        var min = Regex.Match(js, @"MIN_SCALE:\s*([\d.]+)");
        var max = Regex.Match(js, @"MAX_SCALE:\s*([\d.]+)");

        min.Success.Should().BeTrue("the JS engine must declare MIN_SCALE");
        max.Success.Should().BeTrue("the JS engine must declare MAX_SCALE");
        double.Parse(min.Groups[1].Value, CultureInfo.InvariantCulture)
            .Should().Be(DiagramZoomLimits.MinScale, "the JS and C# minimum must stay equal");
        double.Parse(max.Groups[1].Value, CultureInfo.InvariantCulture)
            .Should().Be(DiagramZoomLimits.MaxScale, "the JS and C# maximum must stay equal");
    }

    [Fact]
    public void Clamp_StaysWithinTheRange()
    {
        DiagramZoomLimits.Clamp(0.01).Should().Be(DiagramZoomLimits.MinScale);
        DiagramZoomLimits.Clamp(42).Should().Be(DiagramZoomLimits.MaxScale);
        DiagramZoomLimits.Clamp(1.0).Should().Be(1.0);
    }

    [Fact]
    public void ZoomOut_Repeatedly_StopsAt15Percent()
    {
        using var cut = Render<TmDiagramEditor>(p => p.Add(e => e.ReadOnly, false));

        for (var i = 0; i < 13; i++)
            cut.Find("button[aria-label='Zoom out']").Click();

        cut.Find(".tm-diagram-editor__zoom-label").TextContent.Should().Be("15%");
    }

    [Fact]
    public void ZoomIn_Repeatedly_StopsAt500Percent()
    {
        using var cut = Render<TmDiagramEditor>(p => p.Add(e => e.ReadOnly, false));

        for (var i = 0; i < 13; i++)
            cut.Find("button[aria-label='Zoom in']").Click();

        cut.Find(".tm-diagram-editor__zoom-label").TextContent.Should().Be("500%");
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
                return current.FullName;
            current = current.Parent;
        }

        throw new InvalidOperationException("Repository root could not be located.");
    }
}
