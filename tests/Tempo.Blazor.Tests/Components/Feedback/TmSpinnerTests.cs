using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Feedback;

/// <summary>
/// TDD tests for TmSpinner component.
/// RED phase: written before implementation changes.
/// </summary>
public class TmSpinnerTests : LocalizationTestBase
{
    // ─── Rendering ────────────────────────────────────────────────────────────

    [Fact]
    public void TmSpinner_Renders_Svg_Element()
    {
        var cut = RenderComponent<TmSpinner>();

        cut.Find("svg").Should().NotBeNull();
    }

    [Fact]
    public void TmSpinner_Has_Base_CssClass()
    {
        var cut = RenderComponent<TmSpinner>();

        cut.Find("svg").ClassList.Should().Contain("tm-spinner");
    }

    [Fact]
    public void TmSpinner_Is_AriaHidden()
    {
        var cut = RenderComponent<TmSpinner>();

        cut.Find("svg").GetAttribute("aria-hidden").Should().Be("true");
    }

    [Fact]
    public void TmSpinner_Has_Status_Role()
    {
        var cut = RenderComponent<TmSpinner>();

        cut.Find("svg").GetAttribute("role").Should().Be("status");
    }

    // ─── Size ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(SpinnerSize.Xs, "tm-spinner-xs")]
    [InlineData(SpinnerSize.Sm, "tm-spinner-sm")]
    [InlineData(SpinnerSize.Md, "tm-spinner-md")]
    [InlineData(SpinnerSize.Lg, "tm-spinner-lg")]
    public void TmSpinner_Applies_Size_CssClass(SpinnerSize size, string expectedClass)
    {
        var cut = RenderComponent<TmSpinner>(p => p
            .Add(c => c.Size, size));

        cut.Find("svg").ClassList.Should().Contain(expectedClass);
    }

    [Fact]
    public void TmSpinner_Default_Size_Is_Sm()
    {
        var cut = RenderComponent<TmSpinner>();

        cut.Find("svg").ClassList.Should().Contain("tm-spinner-sm");
    }

    // ─── Color ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(SpinnerColor.Current, "tm-spinner-current")]
    [InlineData(SpinnerColor.Primary, "tm-spinner-primary")]
    [InlineData(SpinnerColor.White,   "tm-spinner-white")]
    public void TmSpinner_Applies_Color_CssClass(SpinnerColor color, string expectedClass)
    {
        var cut = RenderComponent<TmSpinner>(p => p
            .Add(c => c.Color, color));

        cut.Find("svg").ClassList.Should().Contain(expectedClass);
    }

    [Fact]
    public void TmSpinner_Default_Color_Is_Current()
    {
        var cut = RenderComponent<TmSpinner>();

        cut.Find("svg").ClassList.Should().Contain("tm-spinner-current");
    }
}
