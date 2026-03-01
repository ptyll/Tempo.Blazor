using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DataDisplay;

/// <summary>
/// TDD tests for TmCard component.
/// RED phase: written before implementation.
/// </summary>
public class TmCardTests : LocalizationTestBase
{
    // ─── Rendering ────────────────────────────────────────────────────────────

    [Fact]
    public void TmCard_Renders_Div_Element()
    {
        var cut = RenderComponent<TmCard>(p => p
            .AddChildContent("Content"));

        cut.Find("div.tm-card").Should().NotBeNull();
    }

    [Fact]
    public void TmCard_Renders_ChildContent_In_CardContent()
    {
        var cut = RenderComponent<TmCard>(p => p
            .AddChildContent("Hello"));

        cut.Find(".tm-card-content").TextContent.Should().Contain("Hello");
    }

    [Fact]
    public void TmCard_Has_Base_CssClass()
    {
        var cut = RenderComponent<TmCard>(p => p
            .AddChildContent("Test"));

        cut.Find("div").ClassList.Should().Contain("tm-card");
    }

    // ─── Variant CSS ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(CardVariant.Default,  "tm-card-default")]
    [InlineData(CardVariant.Elevated, "tm-card-elevated")]
    [InlineData(CardVariant.Outlined, "tm-card-outlined")]
    public void TmCard_Applies_Variant_CssClass(CardVariant variant, string expectedClass)
    {
        var cut = RenderComponent<TmCard>(p => p
            .Add(c => c.Variant, variant)
            .AddChildContent("Test"));

        cut.Find("div.tm-card").ClassList.Should().Contain(expectedClass);
    }

    [Fact]
    public void TmCard_Default_Variant_Is_Default()
    {
        var cut = RenderComponent<TmCard>(p => p
            .AddChildContent("Test"));

        cut.Find("div.tm-card").ClassList.Should().Contain("tm-card-default");
    }

    // ─── Header ───────────────────────────────────────────────────────────────

    [Fact]
    public void TmCard_Header_Renders_CardHeader()
    {
        var cut = RenderComponent<TmCard>(p => p
            .Add(c => c.Header, "My Card")
            .AddChildContent("Content"));

        cut.Find(".tm-card-header").TextContent.Trim().Should().Contain("My Card");
    }

    [Fact]
    public void TmCard_No_Header_When_Header_Null()
    {
        var cut = RenderComponent<TmCard>(p => p
            .AddChildContent("Content"));

        cut.FindAll(".tm-card-header").Should().BeEmpty();
    }

    [Fact]
    public void TmCard_HeaderIcon_Renders_TmIcon_In_Header()
    {
        var cut = RenderComponent<TmCard>(p => p
            .Add(c => c.Header, "Stats")
            .Add(c => c.HeaderIcon, "bar-chart-2")
            .AddChildContent("Content"));

        cut.FindAll(".tm-card-header .tm-icon").Should().NotBeEmpty();
    }

    // ─── Footer ───────────────────────────────────────────────────────────────

    [Fact]
    public void TmCard_Footer_Renders_CardFooter()
    {
        var cut = RenderComponent<TmCard>(p => p
            .Add(c => c.Footer, "Footer text")
            .AddChildContent("Content"));

        cut.Find(".tm-card-footer").TextContent.Trim().Should().Contain("Footer text");
    }

    [Fact]
    public void TmCard_No_Footer_When_Footer_Null()
    {
        var cut = RenderComponent<TmCard>(p => p
            .AddChildContent("Content"));

        cut.FindAll(".tm-card-footer").Should().BeEmpty();
    }

    // ─── Extra Class ──────────────────────────────────────────────────────────

    [Fact]
    public void TmCard_ExtraClass_Added_To_Root()
    {
        var cut = RenderComponent<TmCard>(p => p
            .Add(c => c.Class, "my-custom-class")
            .AddChildContent("Content"));

        cut.Find("div.tm-card").ClassList.Should().Contain("my-custom-class");
    }

    // ─── AdditionalAttributes ─────────────────────────────────────────────────

    [Fact]
    public void TmCard_AdditionalAttributes_Applied_To_Root()
    {
        var cut = RenderComponent<TmCard>(p => p
            .AddUnmatched("data-testid", "my-card")
            .AddChildContent("Content"));

        cut.Find("div.tm-card").GetAttribute("data-testid").Should().Be("my-card");
    }
}
