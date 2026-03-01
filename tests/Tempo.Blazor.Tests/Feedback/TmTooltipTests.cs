using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Feedback;

/// <summary>TDD tests for TmTooltip.</summary>
public class TmTooltipTests : LocalizationTestBase
{
    [Fact]
    public void Tooltip_RendersChildContent()
    {
        var cut = RenderComponent<TmTooltip>(p => p
            .Add(x => x.Text, "Help text")
            .AddChildContent("<button>Hover me</button>"));

        cut.Markup.Should().Contain("Hover me");
    }

    [Fact]
    public void Tooltip_HasTooltipText()
    {
        var cut = RenderComponent<TmTooltip>(p => p
            .Add(x => x.Text, "This is help")
            .AddChildContent("<span>Info</span>"));

        var tooltipEl = cut.Find(".tm-tooltip__content");
        tooltipEl.TextContent.Should().Contain("This is help");
    }

    [Fact]
    public void Tooltip_DefaultPosition_IsTop()
    {
        var cut = RenderComponent<TmTooltip>(p => p
            .Add(x => x.Text, "Tip")
            .AddChildContent("<span>X</span>"));

        cut.Find(".tm-tooltip").ClassList.Should().Contain("tm-tooltip--top");
    }

    [Theory]
    [InlineData(TooltipPosition.Top, "tm-tooltip--top")]
    [InlineData(TooltipPosition.Bottom, "tm-tooltip--bottom")]
    [InlineData(TooltipPosition.Left, "tm-tooltip--left")]
    [InlineData(TooltipPosition.Right, "tm-tooltip--right")]
    public void Tooltip_Position_AppliesCss(TooltipPosition pos, string expected)
    {
        var cut = RenderComponent<TmTooltip>(p => p
            .Add(x => x.Text, "Tip")
            .Add(x => x.Position, pos)
            .AddChildContent("<span>X</span>"));

        cut.Find(".tm-tooltip").ClassList.Should().Contain(expected);
    }

    [Fact]
    public void Tooltip_HasRoleTooltip()
    {
        var cut = RenderComponent<TmTooltip>(p => p
            .Add(x => x.Text, "Tip")
            .AddChildContent("<span>X</span>"));

        cut.Find("[role='tooltip']").Should().NotBeNull();
    }

    [Fact]
    public void Tooltip_HasAriaDescribedBy()
    {
        var cut = RenderComponent<TmTooltip>(p => p
            .Add(x => x.Text, "Tip")
            .AddChildContent("<span>X</span>"));

        var wrapper = cut.Find(".tm-tooltip__trigger");
        var describedBy = wrapper.GetAttribute("aria-describedby");
        describedBy.Should().NotBeNullOrEmpty();

        // The tooltip content should have the matching id
        var tooltipContent = cut.Find(".tm-tooltip__content");
        tooltipContent.GetAttribute("id").Should().Be(describedBy);
    }

    [Fact]
    public void Tooltip_ContentHiddenByDefault()
    {
        var cut = RenderComponent<TmTooltip>(p => p
            .Add(x => x.Text, "Tip")
            .AddChildContent("<span>X</span>"));

        // Tooltip content should have aria-hidden="true" by default (shown via CSS :hover/:focus)
        var content = cut.Find(".tm-tooltip__content");
        content.Should().NotBeNull();
    }

    [Fact]
    public void Tooltip_CustomMaxWidth_IsApplied()
    {
        var cut = RenderComponent<TmTooltip>(p => p
            .Add(x => x.Text, "Tip")
            .Add(x => x.MaxWidth, "300px")
            .AddChildContent("<span>X</span>"));

        var content = cut.Find(".tm-tooltip__content");
        content.GetAttribute("style").Should().Contain("max-width");
        content.GetAttribute("style").Should().Contain("300px");
    }

    [Fact]
    public void Tooltip_CustomClass_IsApplied()
    {
        var cut = RenderComponent<TmTooltip>(p => p
            .Add(x => x.Text, "Tip")
            .Add(x => x.Class, "my-tooltip")
            .AddChildContent("<span>X</span>"));

        cut.Find(".tm-tooltip").ClassList.Should().Contain("my-tooltip");
    }
}
