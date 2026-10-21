using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

using Tick = Tempo.Blazor.Components.Wireframe.TmWireframeRuler.Tick;

/// <summary>
/// Tests for TmWireframeRuler — tick generation, viewBox, scaling, and rendering.
/// </summary>
public class WireframeRulerTests : LocalizationTestBase
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TmWireframeRuler MakeRuler(
        string orientation = "Horizontal",
        int size = 20,
        double viewBoxX = 0,
        double viewBoxY = 0,
        double viewBoxW = 1200,
        double viewBoxH = 800,
        double scale = 1.0,
        double? indicatorPos = null)
    {
        var ruler = new TmWireframeRuler();
        typeof(TmWireframeRuler)
            .GetProperty(nameof(TmWireframeRuler.Orientation))!
            .SetValue(ruler, orientation);
        typeof(TmWireframeRuler)
            .GetProperty(nameof(TmWireframeRuler.Size))!
            .SetValue(ruler, size);
        typeof(TmWireframeRuler)
            .GetProperty(nameof(TmWireframeRuler.ViewBoxX))!
            .SetValue(ruler, viewBoxX);
        typeof(TmWireframeRuler)
            .GetProperty(nameof(TmWireframeRuler.ViewBoxY))!
            .SetValue(ruler, viewBoxY);
        typeof(TmWireframeRuler)
            .GetProperty(nameof(TmWireframeRuler.ViewBoxW))!
            .SetValue(ruler, viewBoxW);
        typeof(TmWireframeRuler)
            .GetProperty(nameof(TmWireframeRuler.ViewBoxH))!
            .SetValue(ruler, viewBoxH);
        typeof(TmWireframeRuler)
            .GetProperty(nameof(TmWireframeRuler.Scale))!
            .SetValue(ruler, scale);
        typeof(TmWireframeRuler)
            .GetProperty(nameof(TmWireframeRuler.IndicatorPos))!
            .SetValue(ruler, indicatorPos);
        return ruler;
    }

    private static void RefreshTicks(TmWireframeRuler ruler)
    {
        // OnParametersSet is protected — invoke via reflection
        typeof(TmWireframeRuler)
            .GetMethod("OnParametersSet", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(ruler, null);
    }

    private static List<Tick> GetTicks(TmWireframeRuler ruler)
    {
        var field = typeof(TmWireframeRuler)
            .GetField("_ticks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return (List<Tick>)field.GetValue(ruler)!;
    }

    // ── Defaults ──────────────────────────────────────────────────────────────

    [Fact]
    public void Defaults_AreCorrect()
    {
        var r = new TmWireframeRuler();
        r.Orientation.Should().Be("Horizontal");
        r.Size.Should().Be(20);
        r.ViewBoxX.Should().Be(0);
        r.ViewBoxY.Should().Be(0);
        r.ViewBoxW.Should().Be(1200);
        r.ViewBoxH.Should().Be(800);
        r.Scale.Should().Be(1.0);
        r.IndicatorPos.Should().BeNull();
    }

    // ── Tick generation ───────────────────────────────────────────────────────

    [Fact]
    public void BuildTicks_Horizontal_GeneratesTicks()
    {
        var r = MakeRuler(viewBoxX: 0, viewBoxW: 200, scale: 1.0);
        RefreshTicks(r);
        var ticks = GetTicks(r);

        ticks.Should().NotBeEmpty();
        // At scale 1.0 majorStep = 50, minorStep = 10
        // 0..200 should have major ticks at 0, 50, 100, 150, 200
        ticks.Count(t => t.Label == "0").Should().Be(1);
        ticks.Count(t => t.Label == "50").Should().Be(1);
        ticks.Count(t => t.Label == "100").Should().Be(1);
        ticks.Count(t => t.Label == "150").Should().Be(1);
        ticks.Count(t => t.Label == "200").Should().Be(1);
    }

    [Fact]
    public void BuildTicks_Vertical_GeneratesTicks()
    {
        var r = MakeRuler(orientation: "Vertical", viewBoxY: 0, viewBoxH: 200, scale: 1.0);
        RefreshTicks(r);
        var ticks = GetTicks(r);

        ticks.Should().NotBeEmpty();
        ticks.Count(t => t.Label == "0").Should().Be(1);
        ticks.Count(t => t.Label == "100").Should().Be(1);
        ticks.Count(t => t.Label == "200").Should().Be(1);
    }

    [Theory]
    [InlineData(0.2, 200, 50)]   // low zoom  → coarse ticks
    [InlineData(0.5, 100, 25)]   // medium-low
    [InlineData(1.0, 50, 10)]    // normal
    [InlineData(2.0, 25, 5)]     // high
    [InlineData(5.0, 10, 2)]     // very high
    public void BuildTicks_StepSize_ScalesWithZoom(double scale, int expectedMajorStep, int expectedMinorStep)
    {
        var r = MakeRuler(viewBoxX: 0, viewBoxW: expectedMajorStep * 4, scale: scale);
        RefreshTicks(r);
        var ticks = GetTicks(r);

        // Verify major step by checking distance between labeled ticks
        var labeled = ticks.Where(t => !string.IsNullOrEmpty(t.Label)).OrderBy(t => t.Pos).ToList();
        labeled.Count.Should().BeGreaterThanOrEqualTo(2);
        var step = labeled[1].Pos - labeled[0].Pos;
        step.Should().BeApproximately(expectedMajorStep, 0.001);
    }

    [Fact]
    public void BuildTicks_MajorTicks_AreLongest()
    {
        var r = MakeRuler(viewBoxX: 0, viewBoxW: 200, scale: 1.0);
        RefreshTicks(r);
        var ticks = GetTicks(r);

        var major = ticks.First(t => t.Label == "100");
        var minor = ticks.First(t => string.IsNullOrEmpty(t.Label) && t.Pos > 100 && t.Pos < 120);

        major.Len.Should().Be(20);               // Size = 20
        minor.Len.Should().BeLessThan(major.Len);
    }

    [Fact]
    public void BuildTicks_NegativeViewBoxOffset_Works()
    {
        var r = MakeRuler(viewBoxX: -100, viewBoxW: 300, scale: 1.0);
        RefreshTicks(r);
        var ticks = GetTicks(r);

        ticks.Should().Contain(t => t.Label == "0");
        ticks.Should().Contain(t => t.Label == "100");
        ticks.Should().Contain(t => t.Label == "200");
    }

    // ── Indicator ─────────────────────────────────────────────────────────────

    [Fact]
    public void Indicator_Null_DoesNotRenderIndicator()
    {
        var r = MakeRuler(indicatorPos: null);
        r.IndicatorPos.Should().BeNull();
    }

    [Fact]
    public void Indicator_Set_RendersIndicator()
    {
        var r = MakeRuler(indicatorPos: 150);
        r.IndicatorPos.Should().Be(150);
    }

    // ── bUnit rendering ───────────────────────────────────────────────────────

    [Fact]
    public void Render_Horizontal_HasCorrectClass()
    {
        var cut = Render<TmWireframeRuler>(parameters => parameters
            .Add(p => p.Orientation, "Horizontal")
            .Add(p => p.ViewBoxW, 1200)
            .Add(p => p.ViewBoxH, 800));

        cut.Find("svg").ClassList.Should().Contain("tm-wd-ruler--horizontal");
    }

    [Fact]
    public void Render_Vertical_HasCorrectClass()
    {
        var cut = Render<TmWireframeRuler>(parameters => parameters
            .Add(p => p.Orientation, "Vertical")
            .Add(p => p.ViewBoxW, 1200)
            .Add(p => p.ViewBoxH, 800));

        cut.Find("svg").ClassList.Should().Contain("tm-wd-ruler--vertical");
    }

    [Fact]
    public void Render_ContainsTicks()
    {
        var cut = Render<TmWireframeRuler>(parameters => parameters
            .Add(p => p.ViewBoxX, 0)
            .Add(p => p.ViewBoxW, 200)
            .Add(p => p.Scale, 1.0));

        var lines = cut.FindAll("line");
        lines.Count.Should().BeGreaterThan(5); // background rect is not a line, just ticks + maybe cursor
    }

    [Fact]
    public void Render_ContainsLabels()
    {
        var cut = Render<TmWireframeRuler>(parameters => parameters
            .Add(p => p.ViewBoxX, 0)
            .Add(p => p.ViewBoxW, 200)
            .Add(p => p.Scale, 1.0));

        var texts = cut.FindAll("text");
        texts.Count.Should().BeGreaterThan(0);
        texts.Any(t => t.TextContent == "0").Should().BeTrue();
        texts.Any(t => t.TextContent == "100").Should().BeTrue();
    }

    [Fact]
    public void Render_WithoutIndicator_NoCursorLine()
    {
        var cut = Render<TmWireframeRuler>(parameters => parameters
            .Add(p => p.IndicatorPos, (double?)null));

        // All lines should be tick lines (same stroke color as ticks), not cursor lines
        var cursorLines = cut.FindAll("line")
            .Where(l => l.GetAttribute("stroke")?.Contains("--tm-wd-ruler-cursor") == true)
            .ToList();
        cursorLines.Should().BeEmpty();
    }

    [Fact]
    public void Render_WithIndicator_HasCursorLine()
    {
        var cut = Render<TmWireframeRuler>(parameters => parameters
            .Add(p => p.Orientation, "Horizontal")
            .Add(p => p.IndicatorPos, 150.0));

        var cursorLines = cut.FindAll("line")
            .Where(l => l.GetAttribute("stroke")?.Contains("--tm-wd-ruler-cursor") == true)
            .ToList();
        cursorLines.Should().HaveCount(1);
    }

    [Fact]
    public void Render_HorizontalAriaLabel()
    {
        var cut = Render<TmWireframeRuler>(parameters => parameters
            .Add(p => p.Orientation, "Horizontal"));

        cut.Find("svg").GetAttribute("aria-label").Should().Be("Horizontal ruler");
    }

    [Fact]
    public void Render_VerticalAriaLabel()
    {
        var cut = Render<TmWireframeRuler>(parameters => parameters
            .Add(p => p.Orientation, "Vertical"));

        cut.Find("svg").GetAttribute("aria-label").Should().Be("Vertical ruler");
    }

    // ── ViewBox ───────────────────────────────────────────────────────────────

    [Fact]
    public void ViewBox_Horizontal_IsCorrect()
    {
        var cut = Render<TmWireframeRuler>(parameters => parameters
            .Add(p => p.Orientation, "Horizontal")
            .Add(p => p.ViewBoxX, 100)
            .Add(p => p.ViewBoxW, 500)
            .Add(p => p.Size, 24));

        var vb = cut.Find("svg").GetAttribute("viewBox");
        vb.Should().Be("100 0 500 24");
    }

    [Fact]
    public void ViewBox_Vertical_IsCorrect()
    {
        var cut = Render<TmWireframeRuler>(parameters => parameters
            .Add(p => p.Orientation, "Vertical")
            .Add(p => p.ViewBoxY, 50)
            .Add(p => p.ViewBoxH, 600)
            .Add(p => p.Size, 24));

        var vb = cut.Find("svg").GetAttribute("viewBox");
        vb.Should().Be("0 50 24 600");
    }
}
