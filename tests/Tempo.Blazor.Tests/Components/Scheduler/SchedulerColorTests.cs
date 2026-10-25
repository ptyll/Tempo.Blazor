using FluentAssertions;
using Tempo.Blazor.Components.Scheduler;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>
/// White text on a light event colour fails WCAG AA, so the helper switches the text token. A dark
/// colour keeps the theme's white text, and an unreadable colour is left alone.
/// </summary>
public class SchedulerColorTests
{
    [Theory]
    [InlineData("#f59e0b")]
    [InlineData("#10b981")]
    [InlineData("#fff")]
    [InlineData("rgb(245, 158, 11)")]
    public void Light_Colours_Switch_To_The_Dark_Text_Token(string color)
    {
        SchedulerColor.ReadableText(color)
            .Should().Be("--event-text-color: var(--tm-scheduler-event-fg-on-light);");
    }

    [Theory]
    [InlineData("#1d4ed8")]
    [InlineData("#111827")]
    public void Dark_Colours_Keep_The_Theme_Text(string color)
    {
        SchedulerColor.ReadableText(color).Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("rebeccapurple")]
    public void Unreadable_Colours_Are_Left_To_The_Theme(string? color)
    {
        SchedulerColor.ReadableText(color).Should().BeEmpty();
    }

    [Fact]
    public void Amber_Fails_White_Text_And_Clears_The_Dark_Token()
    {
        var amber = (0xf5, 0x9e, 0x0b);
        SchedulerColor.Contrast(amber, (255, 255, 255)).Should().BeLessThan(SchedulerColor.MinimumContrast);
        SchedulerColor.Contrast(amber, (0, 0, 0)).Should().BeGreaterThan(SchedulerColor.MinimumContrast);
    }
}
