using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Icons;

/// <summary>
/// TDD tests for TmIcon component.
/// RED phase: these tests are written before the component implementation.
/// </summary>
public class TmIconTests : LocalizationTestBase
{
    // ─── Rendering ────────────────────────────────────────────────────────────

    [Fact]
    public void TmIcon_Renders_SvgElement()
    {
        var cut = RenderComponent<TmIcon>(p => p
            .Add(c => c.Name, IconNames.Check));

        cut.Find("svg").Should().NotBeNull();
    }

    [Fact]
    public void TmIcon_Has_AriaHidden_True()
    {
        var cut = RenderComponent<TmIcon>(p => p
            .Add(c => c.Name, IconNames.Check));

        cut.Find("svg").GetAttribute("aria-hidden").Should().Be("true");
    }

    [Fact]
    public void TmIcon_Has_Focusable_False()
    {
        var cut = RenderComponent<TmIcon>(p => p
            .Add(c => c.Name, IconNames.Check));

        cut.Find("svg").GetAttribute("focusable").Should().Be("false");
    }

    // ─── Size ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(IconSize.Xs, "tm-icon", "tm-icon-xs")]
    [InlineData(IconSize.Sm, "tm-icon", "tm-icon-sm")]
    [InlineData(IconSize.Md, "tm-icon", "tm-icon-md")]
    [InlineData(IconSize.Lg, "tm-icon", "tm-icon-lg")]
    [InlineData(IconSize.Xl, "tm-icon", "tm-icon-xl")]
    public void TmIcon_Applies_Size_CssClass(IconSize size, string baseClass, string sizeClass)
    {
        var cut = RenderComponent<TmIcon>(p => p
            .Add(c => c.Name, IconNames.Check)
            .Add(c => c.Size, size));

        var svg = cut.Find("svg");
        svg.ClassList.Should().Contain(baseClass);
        svg.ClassList.Should().Contain(sizeClass);
    }

    [Fact]
    public void TmIcon_Default_Size_Is_Md()
    {
        var cut = RenderComponent<TmIcon>(p => p
            .Add(c => c.Name, IconNames.Check));

        cut.Find("svg").ClassList.Should().Contain("tm-icon-md");
    }

    // ─── Color ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(IconColor.Current, "tm-icon-current")]
    [InlineData(IconColor.Primary, "tm-icon-primary")]
    [InlineData(IconColor.Danger,  "tm-icon-danger")]
    [InlineData(IconColor.Success, "tm-icon-success")]
    [InlineData(IconColor.Warning, "tm-icon-warning")]
    [InlineData(IconColor.Muted,   "tm-icon-muted")]
    public void TmIcon_Applies_Color_CssClass(IconColor color, string expectedClass)
    {
        var cut = RenderComponent<TmIcon>(p => p
            .Add(c => c.Name, IconNames.Check)
            .Add(c => c.Color, color));

        cut.Find("svg").ClassList.Should().Contain(expectedClass);
    }

    // ─── Known icons render path/shape data ───────────────────────────────────

    [Fact]
    public void TmIcon_Check_Renders_Path_Content()
    {
        var cut = RenderComponent<TmIcon>(p => p
            .Add(c => c.Name, IconNames.Check));

        // Should render some path/polyline/circle SVG content
        var markup = cut.Markup;
        markup.Should().ContainAny("<path", "<polyline", "<circle", "<line", "<rect");
    }

    [Fact]
    public void TmIcon_UnknownName_Renders_Empty_Svg_Without_Throwing()
    {
        // Should not throw for unknown icon names — renders empty SVG gracefully
        var act = () => RenderComponent<TmIcon>(p => p
            .Add(c => c.Name, "non-existent-icon-xyz"));

        act.Should().NotThrow();
    }

    // ─── Additional Attributes ────────────────────────────────────────────────

    [Fact]
    public void TmIcon_Passes_AdditionalAttributes_To_Svg()
    {
        var cut = RenderComponent<TmIcon>(p => p
            .Add(c => c.Name, IconNames.Check)
            .AddUnmatched("data-testid", "my-icon"));

        cut.Find("svg").GetAttribute("data-testid").Should().Be("my-icon");
    }

    // ─── StrokeWidth ──────────────────────────────────────────────────────────

    [Fact]
    public void TmIcon_Default_StrokeWidth_Is_2()
    {
        var cut = RenderComponent<TmIcon>(p => p
            .Add(c => c.Name, IconNames.Check));

        cut.Find("svg").GetAttribute("stroke-width").Should().Be("2");
    }

    [Fact]
    public void TmIcon_Custom_StrokeWidth_Is_Applied()
    {
        var cut = RenderComponent<TmIcon>(p => p
            .Add(c => c.Name, IconNames.Check)
            .Add(c => c.StrokeWidth, 1.5));

        cut.Find("svg").GetAttribute("stroke-width").Should().Be("1.5");
    }
}
