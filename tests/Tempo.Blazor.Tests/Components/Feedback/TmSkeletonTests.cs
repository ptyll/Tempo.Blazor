using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Common;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Feedback;

/// <summary>
/// TDD tests for TmSkeleton component.
/// RED phase: written before implementation.
/// </summary>
public class TmSkeletonTests : LocalizationTestBase
{
    // ─── Rendering ────────────────────────────────────────────────────────────

    [Fact]
    public void TmSkeleton_Renders_Div_Element()
    {
        var cut = Render<TmSkeleton>();

        cut.Find("div").Should().NotBeNull();
    }

    [Fact]
    public void TmSkeleton_Element_Span_Renders_APhrasingRoot()
    {
        var cut = Render<TmSkeleton>(p => p.Add(c => c.Element, TmRootElement.Span));

        cut.FindAll("div").Count.Should().Be(0);
        cut.Find("span.tm-skeleton").Should().NotBeNull();
    }

    [Fact]
    public void TmSkeleton_Element_AnUndefinedValue_FallsBack_ToDiv()
    {
        var cut = Render<TmSkeleton>(p => p.Add(c => c.Element, (TmRootElement)99));

        cut.Find("div.tm-skeleton").Should().NotBeNull();
    }

    [Fact]
    public void TmSkeleton_Has_Base_CssClass()
    {
        var cut = Render<TmSkeleton>();

        cut.Find("div").ClassList.Should().Contain("tm-skeleton");
    }

    // ─── Variant CSS ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(SkeletonVariant.Text,   "tm-skeleton-text")]
    [InlineData(SkeletonVariant.Circle, "tm-skeleton-circle")]
    [InlineData(SkeletonVariant.Rect,   "tm-skeleton-rect")]
    public void TmSkeleton_Applies_Variant_CssClass(SkeletonVariant variant, string expectedClass)
    {
        var cut = Render<TmSkeleton>(p => p
            .Add(c => c.Variant, variant));

        cut.Find("div").ClassList.Should().Contain(expectedClass);
    }

    [Fact]
    public void TmSkeleton_Default_Variant_Is_Text()
    {
        var cut = Render<TmSkeleton>();

        cut.Find("div").ClassList.Should().Contain("tm-skeleton-text");
    }

    // ─── Width / Height ───────────────────────────────────────────────────────

    [Fact]
    public void TmSkeleton_Width_Applied_As_Style()
    {
        var cut = Render<TmSkeleton>(p => p
            .Add(c => c.Width, "200px"));

        cut.Find("div").GetAttribute("style").Should().Contain("width: 200px");
    }

    [Fact]
    public void TmSkeleton_Height_Applied_As_Style()
    {
        var cut = Render<TmSkeleton>(p => p
            .Add(c => c.Height, "40px"));

        cut.Find("div").GetAttribute("style").Should().Contain("height: 40px");
    }

    [Fact]
    public void TmSkeleton_No_Style_When_No_Width_Or_Height()
    {
        var cut = Render<TmSkeleton>();

        var styleAttr = cut.Find("div").GetAttribute("style");
        // style attribute should be empty or not set
        (styleAttr is null || styleAttr == string.Empty).Should().BeTrue();
    }

    [Fact]
    public void TmSkeleton_Both_Width_And_Height_Applied()
    {
        var cut = Render<TmSkeleton>(p => p
            .Add(c => c.Width, "100%")
            .Add(c => c.Height, "16px"));

        var style = cut.Find("div").GetAttribute("style")!;
        style.Should().Contain("width: 100%");
        style.Should().Contain("height: 16px");
    }
}
