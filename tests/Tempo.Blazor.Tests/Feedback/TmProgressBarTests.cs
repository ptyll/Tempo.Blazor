using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Feedback;

/// <summary>TDD tests for TmProgressBar.</summary>
public class TmProgressBarTests : LocalizationTestBase
{
    [Fact]
    public void ProgressBar_Renders_WithRoleProgressbar()
    {
        var cut = RenderComponent<TmProgressBar>(p => p.Add(x => x.Value, 50));

        cut.Find("[role='progressbar']").Should().NotBeNull();
    }

    [Fact]
    public void ProgressBar_AriaValues_AreSet()
    {
        var cut = RenderComponent<TmProgressBar>(p => p.Add(x => x.Value, 60));

        var bar = cut.Find("[role='progressbar']");
        bar.GetAttribute("aria-valuenow").Should().Be("60");
        bar.GetAttribute("aria-valuemin").Should().Be("0");
        bar.GetAttribute("aria-valuemax").Should().Be("100");
    }

    [Fact]
    public void ProgressBar_Fill_HasCorrectWidth()
    {
        var cut = RenderComponent<TmProgressBar>(p => p.Add(x => x.Value, 75));

        var fill = cut.Find(".tm-progress-bar__fill");
        fill.GetAttribute("style").Should().Contain("75");
    }

    [Fact]
    public void ProgressBar_ZeroValue_HasZeroWidth()
    {
        var cut = RenderComponent<TmProgressBar>(p => p.Add(x => x.Value, 0));

        var fill = cut.Find(".tm-progress-bar__fill");
        fill.GetAttribute("style").Should().Contain("0");
    }

    [Fact]
    public void ProgressBar_HundredPercent_HasFullWidth()
    {
        var cut = RenderComponent<TmProgressBar>(p => p.Add(x => x.Value, 100));

        var fill = cut.Find(".tm-progress-bar__fill");
        fill.GetAttribute("style").Should().Contain("100");
    }

    // ── Sizes ──────────────────────────────────────────────

    [Theory]
    [InlineData(ProgressBarSize.Sm, "tm-progress-bar--sm")]
    [InlineData(ProgressBarSize.Md, "tm-progress-bar--md")]
    [InlineData(ProgressBarSize.Lg, "tm-progress-bar--lg")]
    public void ProgressBar_Size_AppliesCss(ProgressBarSize size, string expected)
    {
        var cut = RenderComponent<TmProgressBar>(p => p
            .Add(x => x.Value, 50)
            .Add(x => x.Size, size));

        cut.Find(".tm-progress-bar").ClassList.Should().Contain(expected);
    }

    // ── Variants ───────────────────────────────────────────

    [Theory]
    [InlineData(ProgressBarVariant.Success, "tm-progress-bar--success")]
    [InlineData(ProgressBarVariant.Warning, "tm-progress-bar--warning")]
    [InlineData(ProgressBarVariant.Error, "tm-progress-bar--error")]
    [InlineData(ProgressBarVariant.Gradient, "tm-progress-bar--gradient")]
    public void ProgressBar_Variant_AppliesCss(ProgressBarVariant variant, string expected)
    {
        var cut = RenderComponent<TmProgressBar>(p => p
            .Add(x => x.Value, 50)
            .Add(x => x.Variant, variant));

        cut.Find(".tm-progress-bar").ClassList.Should().Contain(expected);
    }

    // ── Label ──────────────────────────────────────────────

    [Fact]
    public void ProgressBar_ShowLabel_RendersPercentage()
    {
        var cut = RenderComponent<TmProgressBar>(p => p
            .Add(x => x.Value, 42)
            .Add(x => x.ShowLabel, true));

        cut.Find(".tm-progress-bar__label").TextContent.Should().Contain("42%");
    }

    // ── Striped ────────────────────────────────────────────

    [Fact]
    public void ProgressBar_Striped_HasClass()
    {
        var cut = RenderComponent<TmProgressBar>(p => p
            .Add(x => x.Value, 50)
            .Add(x => x.Striped, true));

        cut.Find(".tm-progress-bar").ClassList.Should().Contain("tm-progress-bar--striped");
    }

    [Fact]
    public void ProgressBar_Animated_HasClass()
    {
        var cut = RenderComponent<TmProgressBar>(p => p
            .Add(x => x.Value, 50)
            .Add(x => x.Animated, true));

        cut.Find(".tm-progress-bar").ClassList.Should().Contain("tm-progress-bar--animated");
    }

    // ── Indeterminate ──────────────────────────────────────

    [Fact]
    public void ProgressBar_Indeterminate_HasClass()
    {
        var cut = RenderComponent<TmProgressBar>(p => p.Add(x => x.Indeterminate, true));

        cut.Find(".tm-progress-bar").ClassList.Should().Contain("tm-progress-bar--indeterminate");
    }

    // ── Segments ───────────────────────────────────────────

    [Fact]
    public void ProgressBar_Segments_RenderMultipleFills()
    {
        var segments = new List<ProgressSegment>
        {
            new(30, "#22c55e", "Complete"),
            new(20, "#f59e0b", "In Progress"),
            new(10, "#ef4444", "Failed"),
        };

        var cut = RenderComponent<TmProgressBar>(p => p
            .Add(x => x.Value, 60)
            .Add(x => x.Segments, segments));

        var fills = cut.FindAll(".tm-progress-bar__segment");
        fills.Should().HaveCount(3);
    }

    // ── Custom class ───────────────────────────────────────

    [Fact]
    public void ProgressBar_CustomClass_IsApplied()
    {
        var cut = RenderComponent<TmProgressBar>(p => p
            .Add(x => x.Value, 50)
            .Add(x => x.Class, "my-progress"));

        cut.Find(".tm-progress-bar").ClassList.Should().Contain("my-progress");
    }
}
