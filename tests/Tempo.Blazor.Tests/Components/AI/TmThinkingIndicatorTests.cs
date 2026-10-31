using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// bUnit contract of <see cref="TmThinkingIndicator"/> (plan 7f21f26b, Fáze 4): the phase text, the
/// localized default when the host names no phase, the completion that replaces the spinner with a
/// check, and the <c>role="status"</c> region that is the component's single announcement channel.
/// </summary>
public sealed class TmThinkingIndicatorTests : LocalizationTestBase
{
    [Fact]
    public void ThePhaseText_IsRendered_InsideAPoliteStatusRegion()
    {
        var cut = Render<TmThinkingIndicator>(parameters => parameters
            .Add(indicator => indicator.Phase, "Searching the web"));

        var root = cut.Find(".tm-ai-thinking");
        root.GetAttribute("role").Should().Be("status",
            "a phase change happens without a focus move, so it must be announced");
        root.GetAttribute("data-testid").Should().Be("ai-thinking");
        root.Attributes.Any(attribute => attribute.Name == "aria-live").Should().BeFalse(
            "role=status already implies a polite live region — a second declaration is noise");

        cut.Find(".tm-ai-thinking__phase").TextContent.Trim().Should().Be("Searching the web");
    }

    [Fact]
    public void WithoutAPhase_TheLocalizedDefaultIsShown()
    {
        var cut = Render<TmThinkingIndicator>(parameters => parameters
            .Add(indicator => indicator.Phase, null));

        cut.Find(".tm-ai-thinking__phase").TextContent.Trim().Should().Be("Working…");
    }

    [Fact]
    public void WhileRunning_ThereIsAShimmeringLabelAndASpinner()
    {
        var cut = Render<TmThinkingIndicator>(parameters => parameters
            .Add(indicator => indicator.Phase, "Searching the web"));

        cut.FindAll(".tm-ai-thinking__spinner").Should().HaveCount(1);
        cut.Find(".tm-ai-thinking__phase").ClassList.Should().Contain("tm-ai-shimmer");
        cut.FindAll(".tm-ai-thinking__check").Should().BeEmpty();
        cut.Find(".tm-ai-thinking").GetAttribute("data-completed").Should().BeNull();
    }

    [Fact]
    public void Completed_ReplacesTheSpinnerWithACheck_AndStopsTheShimmer()
    {
        var cut = Render<TmThinkingIndicator>(parameters => parameters
            .Add(indicator => indicator.Phase, "Searching the web")
            .Add(indicator => indicator.Completed, true));

        cut.Find(".tm-ai-thinking").GetAttribute("data-completed").Should().Be("true");
        cut.FindAll(".tm-ai-thinking__spinner").Should().BeEmpty("the work is over; a spinner would lie");
        cut.FindAll(".tm-ai-thinking__check").Should().HaveCount(1);
        cut.Find(".tm-ai-thinking__check svg").Should().NotBeNull();
        cut.Find(".tm-ai-thinking__phase").ClassList.Should().NotContain("tm-ai-shimmer");
        cut.Find(".tm-ai-thinking__phase").TextContent.Trim().Should().Be("Searching the web",
            "the last phase stays readable after the work finished");
    }

    [Fact]
    public void Completed_WithoutAPhase_ShowsTheLocalizedCompletion()
    {
        var cut = Render<TmThinkingIndicator>(parameters => parameters
            .Add(indicator => indicator.Completed, true));

        cut.Find(".tm-ai-thinking__phase").TextContent.Trim().Should().Be("Done");
    }

    [Fact]
    public void ThereIsExactlyOneLiveRegion()
    {
        var cut = Render<TmThinkingIndicator>(parameters => parameters
            .Add(indicator => indicator.Phase, "Searching the web")
            .Add(indicator => indicator.Completed, true));

        cut.FindAll("[role='status']").Count.Should().Be(1);
    }

    [Fact]
    public void TheDefaultLabels_AreLocalized()
    {
        UseCzechLocalization();

        var thinking = Render<TmThinkingIndicator>(parameters => parameters
            .Add(indicator => indicator.Phase, string.Empty));
        thinking.Find(".tm-ai-thinking__phase").TextContent.Trim().Should().Be("Pracuji…");

        var completed = Render<TmThinkingIndicator>(parameters => parameters
            .Add(indicator => indicator.Completed, true));
        completed.Find(".tm-ai-thinking__phase").TextContent.Trim().Should().Be("Hotovo");
    }
}
