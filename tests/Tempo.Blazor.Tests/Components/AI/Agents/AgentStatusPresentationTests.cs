using FluentAssertions;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.AI.Agents;

/// <summary>
/// Plan d67b33af, Fáze 1 — <c>AgentStatusPresentation</c>, the ONE place an <see cref="AgentActivityStatus"/>
/// becomes an icon, a CSS tone slug and a localized word.
/// <para>
/// The mapping is internal (it is presentation, not a contract) but shared: Fáze 2–8 render agent state in lanes,
/// graph nodes, fan-out tiles, handoffs and the presence roster, and eleven separate switches would drift the
/// moment one of them forgot a state. The three members are pinned here so a new state cannot ship half-painted —
/// an unmapped value would silently render the fallback icon with an empty word.
/// </para>
/// </summary>
public sealed class AgentStatusPresentationTests : LocalizationTestBase
{
    private static readonly AgentActivityStatus[] AllStatuses = Enum.GetValues<AgentActivityStatus>();

    [Fact]
    public void EveryStatus_HasAnIcon()
    {
        foreach (var status in AllStatuses)
        {
            AgentStatusPresentation.Icon(status).Should().NotBeNullOrWhiteSpace(
                "{0} must pair its tone with an icon, or the state is colour-only information", status);
        }

        AllStatuses.Select(AgentStatusPresentation.Icon).Should().OnlyHaveUniqueItems(
            "two states sharing a glyph makes the icon worthless exactly where it matters");
    }

    [Fact]
    public void EveryStatus_HasAStableToneSlug()
    {
        foreach (var status in AllStatuses)
        {
            AgentStatusPresentation.Tone(status).Should().MatchRegex("^[a-z][a-z-]*$",
                "{0} needs a lowercase CSS hook (the modifier class is built from it)", status);
        }

        AllStatuses.Select(AgentStatusPresentation.Tone).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void TheToneSlugs_StayShort_AndNamespacedToTheBadge()
        => AgentStatusPresentation.Tone(AgentActivityStatus.WaitingForApproval).Should().Be("approval",
            "the documented CSS hook is .tm-agent-badge__status--approval");

    [Fact]
    public void EveryStatus_HasALocalizedWord()
    {
        var localizer = BuildEnglishLocalizer();

        foreach (var status in AllStatuses)
        {
            var word = AgentStatusPresentation.Text(localizer, status);

            word.Should().NotBeNullOrWhiteSpace();
            word.Should().NotStartWith("TmAgentStatus_",
                "{0} must resolve to a translation, never to its own resource key", status);
        }

        AllStatuses.Select(status => AgentStatusPresentation.Text(localizer, status))
            .Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void AnUnknownStatus_FallsBackInsteadOfThrowing()
    {
        var localizer = BuildEnglishLocalizer();

        AgentStatusPresentation.Icon((AgentActivityStatus)99).Should().Be(IconNames.Circle);
        AgentStatusPresentation.Tone((AgentActivityStatus)99).Should().Be("idle");
        AgentStatusPresentation.Text(localizer, (AgentActivityStatus)99).Should().Be("Idle");
        AgentStatusPresentation.IsKnown((AgentActivityStatus)99).Should().BeFalse(
            "the badge renders no chip at all for a value it cannot name");
        AgentStatusPresentation.IsKnown(AgentActivityStatus.Failed).Should().BeTrue();
    }
}
