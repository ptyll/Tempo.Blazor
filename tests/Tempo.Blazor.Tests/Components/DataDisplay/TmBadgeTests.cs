using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DataDisplay;

/// <summary>
/// TDD tests for TmBadge component.
/// RED phase: written before implementation.
/// </summary>
public class TmBadgeTests : LocalizationTestBase
{
    // ─── Rendering ────────────────────────────────────────────────────────────

    [Fact]
    public void TmBadge_Renders_Span_Element()
    {
        var cut = RenderComponent<TmBadge>(p => p
            .AddChildContent("New"));

        cut.Find("span.tm-badge").Should().NotBeNull();
    }

    [Fact]
    public void TmBadge_Renders_ChildContent()
    {
        var cut = RenderComponent<TmBadge>(p => p
            .AddChildContent("New"));

        cut.Find("span.tm-badge").TextContent.Should().Contain("New");
    }

    [Fact]
    public void TmBadge_Has_Base_CssClass()
    {
        var cut = RenderComponent<TmBadge>(p => p
            .AddChildContent("Test"));

        cut.Find("span").ClassList.Should().Contain("tm-badge");
    }

    // ─── Variant CSS ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(BadgeVariant.Default, "tm-badge-default")]
    [InlineData(BadgeVariant.Primary, "tm-badge-primary")]
    [InlineData(BadgeVariant.Success, "tm-badge-success")]
    [InlineData(BadgeVariant.Danger,  "tm-badge-danger")]
    [InlineData(BadgeVariant.Warning, "tm-badge-warning")]
    [InlineData(BadgeVariant.Info,    "tm-badge-info")]
    public void TmBadge_Applies_Variant_CssClass(BadgeVariant variant, string expectedClass)
    {
        var cut = RenderComponent<TmBadge>(p => p
            .Add(c => c.Variant, variant)
            .AddChildContent("Test"));

        cut.Find("span").ClassList.Should().Contain(expectedClass);
    }

    [Fact]
    public void TmBadge_Default_Variant_Is_Default()
    {
        var cut = RenderComponent<TmBadge>(p => p
            .AddChildContent("Test"));

        cut.Find("span").ClassList.Should().Contain("tm-badge-default");
    }

    // ─── Size CSS ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(BadgeSize.Sm, "tm-badge-sm")]
    [InlineData(BadgeSize.Md, "tm-badge-md")]
    public void TmBadge_Applies_Size_CssClass(BadgeSize size, string expectedClass)
    {
        var cut = RenderComponent<TmBadge>(p => p
            .Add(c => c.Size, size)
            .AddChildContent("Test"));

        cut.Find("span").ClassList.Should().Contain(expectedClass);
    }

    [Fact]
    public void TmBadge_Default_Size_Is_Md()
    {
        var cut = RenderComponent<TmBadge>(p => p
            .AddChildContent("Test"));

        cut.Find("span").ClassList.Should().Contain("tm-badge-md");
    }

    // ─── Style CSS ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(BadgeStyle.Filled,  "tm-badge-filled")]
    [InlineData(BadgeStyle.Outline, "tm-badge-outline")]
    [InlineData(BadgeStyle.Subtle,  "tm-badge-subtle")]
    public void TmBadge_Applies_Style_CssClass(BadgeStyle style, string expectedClass)
    {
        var cut = RenderComponent<TmBadge>(p => p
            .Add(c => c.BadgeStyle, style)
            .AddChildContent("Test"));

        cut.Find("span").ClassList.Should().Contain(expectedClass);
    }

    [Fact]
    public void TmBadge_Default_Style_Is_Filled()
    {
        var cut = RenderComponent<TmBadge>(p => p
            .AddChildContent("Test"));

        cut.Find("span").ClassList.Should().Contain("tm-badge-filled");
    }

    // ─── Pill ─────────────────────────────────────────────────────────────────

    [Fact]
    public void TmBadge_Pill_Adds_Pill_CssClass()
    {
        var cut = RenderComponent<TmBadge>(p => p
            .Add(c => c.Pill, true)
            .AddChildContent("Test"));

        cut.Find("span").ClassList.Should().Contain("tm-badge-pill");
    }

    [Fact]
    public void TmBadge_NonPill_Does_Not_Have_Pill_CssClass()
    {
        var cut = RenderComponent<TmBadge>(p => p
            .Add(c => c.Pill, false)
            .AddChildContent("Test"));

        cut.Find("span").ClassList.Should().NotContain("tm-badge-pill");
    }

    // ─── Dot ──────────────────────────────────────────────────────────────────

    [Fact]
    public void TmBadge_Dot_Renders_Dot_Element()
    {
        var cut = RenderComponent<TmBadge>(p => p
            .Add(c => c.Dot, true)
            .AddChildContent("Test"));

        cut.FindAll(".tm-badge-dot").Should().NotBeEmpty();
    }

    [Fact]
    public void TmBadge_No_Dot_By_Default()
    {
        var cut = RenderComponent<TmBadge>(p => p
            .AddChildContent("Test"));

        cut.FindAll(".tm-badge-dot").Should().BeEmpty();
    }

    // ─── Icon ─────────────────────────────────────────────────────────────────

    [Fact]
    public void TmBadge_Icon_Renders_TmIcon()
    {
        var cut = RenderComponent<TmBadge>(p => p
            .Add(c => c.Icon, "check")
            .AddChildContent("Done"));

        cut.FindAll(".tm-icon").Should().NotBeEmpty();
    }

    [Fact]
    public void TmBadge_No_Icon_When_Icon_Null()
    {
        var cut = RenderComponent<TmBadge>(p => p
            .AddChildContent("Done"));

        cut.FindAll(".tm-icon").Should().BeEmpty();
    }
}
