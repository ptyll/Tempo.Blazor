using Bunit;
using FluentAssertions;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.AI.Agents;

/// <summary>
/// Plan d67b33af, Fáze 1 — the bUnit contract of <see cref="TmAgentBadge"/>, the shared building block every
/// agent visualization names an agent with: initials (or the host's picture), the agent's palette colour, the
/// name and — when the host reports one — the activity state as an icon, a WORD and a tone.
/// <para>
/// Accessibility is the reason the state is not just a coloured dot: the word is always rendered next to the
/// icon, so the state never depends on colour alone (WCAG 1.4.1). The badge is deliberately NOT a live region
/// and carries no <c>aria-live</c> — the surface that owns the run (a lane, a graph, a monitor) has exactly one
/// announcement channel, and a badge inside it must never become a second one.
/// </para>
/// </summary>
public sealed class TmAgentBadgeTests : LocalizationTestBase
{
    private static AgentRef Agent(int colorIndex = 0, string name = "Data analyst")
        => new(name, "agent-1") { ColorIndex = colorIndex };

    [Fact]
    public void TheBadge_ShowsTheInitials_TheName_AndTheStateInWords()
    {
        var cut = Render<TmAgentBadge>(parameters => parameters
            .Add(badge => badge.Agent, Agent())
            .Add(badge => badge.Status, AgentActivityStatus.Running));

        cut.Find(".tm-agent-badge__initials").TextContent.Trim().Should().Be("DA");
        cut.Find(".tm-agent-badge__name").TextContent.Trim().Should().Be("Data analyst");
        cut.Find(".tm-agent-badge__status-text").TextContent.Trim().Should().Be("Running",
            "the state word is the part a reader with no colour perception needs");

        var root = cut.Find(".tm-agent-badge");
        root.GetAttribute("data-status").Should().Be("Running");
        root.GetAttribute("data-agent-id").Should().Be("agent-1");
    }

    [Fact]
    public void TheStateChip_CarriesAToneModifier_SoEachStatePaintsItsOwnTone()
    {
        var cut = Render<TmAgentBadge>(parameters => parameters
            .Add(badge => badge.Agent, Agent())
            .Add(badge => badge.Status, AgentActivityStatus.WaitingForApproval));

        cut.Find(".tm-agent-badge__status").ClassList.Should().Contain("tm-agent-badge__status--approval",
            "the CSS hook is a stable slug, not ToString().ToLower() of an enum member");
        cut.Find(".tm-agent-badge__status-text").TextContent.Trim().Should().Be("Waiting for approval");
    }

    [Fact]
    public void TheStateIcon_IsDecorative_BecauseTheWordIsRightNextToIt()
    {
        var cut = Render<TmAgentBadge>(parameters => parameters
            .Add(badge => badge.Agent, Agent())
            .Add(badge => badge.Status, AgentActivityStatus.Succeeded));

        var icon = cut.Find(".tm-agent-badge__status-icon");
        icon.GetAttribute("aria-hidden").Should().Be("true",
            "an icon announced next to its own label doubles every state change");
    }

    [Fact]
    public void WithoutAStatus_ThereIsNoChip()
    {
        var cut = Render<TmAgentBadge>(parameters => parameters.Add(badge => badge.Agent, Agent()));

        cut.FindAll(".tm-agent-badge__status").Should().BeEmpty();
        cut.Find(".tm-agent-badge").GetAttribute("data-status").Should().BeNull();
    }

    [Fact]
    public void ThePaletteSlot_ReachesTheMarkupAsTheDeclaredToken()
    {
        var cut = Render<TmAgentBadge>(parameters => parameters.Add(badge => badge.Agent, Agent(colorIndex: 5)));

        var root = cut.Find(".tm-agent-badge");
        root.GetAttribute("data-agent-color").Should().Be("5");
        root.GetAttribute("style").Should().Contain("--tm-agent-badge-color: var(--tm-agent-color-5)",
            "the badge paints with the palette token, so the theme decides the value");
    }

    [Fact]
    public void AnOutOfRangeColorIndex_IsNormalisedIntoThePalette()
    {
        var cut = Render<TmAgentBadge>(parameters => parameters.Add(badge => badge.Agent, Agent(colorIndex: 11)));

        cut.Find(".tm-agent-badge").GetAttribute("data-agent-color").Should().Be("3",
            "eleven mod eight is three — a host counting past the palette must still land on a declared token");
    }

    [Fact]
    public void ACustomAvatar_ReplacesTheInitials_AndStaysDecorative()
    {
        var agent = Agent();
        agent = agent with { AvatarUrl = "https://example.test/agent.png" };

        var cut = Render<TmAgentBadge>(parameters => parameters.Add(badge => badge.Agent, agent));

        var image = cut.Find(".tm-agent-badge__image");
        image.GetAttribute("src").Should().Be("https://example.test/agent.png");
        image.GetAttribute("alt").Should().Be(string.Empty,
            "the picture repeats the name that is already on the badge (or its aria-label)");
        cut.FindAll(".tm-agent-badge__initials").Should().BeEmpty();
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JavaScript:alert(1)")]
    [InlineData("java\tscript:alert(1)")]
    [InlineData("//example.test/agent.png")]
    [InlineData("data:image/svg+xml,%3Csvg%2F%3E")]
    [InlineData("avatars/agent.png")]
    public void AnAvatarUrlTheImageAllowlistRefuses_FallsBackToTheInitials(string avatarUrl)
    {
        var agent = Agent() with { AvatarUrl = avatarUrl };

        var cut = Render<TmAgentBadge>(parameters => parameters.Add(badge => badge.Agent, agent));

        cut.FindAll(".tm-agent-badge__image").Should().BeEmpty(
            "the picture is host-supplied data, so it goes through the same image allowlist the markdown "
            + "pipeline uses (absolute http(s) only) instead of straight into src — a refused target must "
            + "never become a live source or a broken image");
        cut.Find(".tm-agent-badge__initials").TextContent.Trim().Should().Be("DA",
            "the initials are the documented fallback whenever there is no loadable picture");
    }

    [Fact]
    public void APaddedButAllowedAvatarUrl_StillRendersThePicture()
    {
        var agent = Agent() with { AvatarUrl = "  https://example.test/agent.png  " };

        var cut = Render<TmAgentBadge>(parameters => parameters.Add(badge => badge.Agent, agent));

        cut.Find(".tm-agent-badge__image").GetAttribute("src").Should().Be("https://example.test/agent.png",
            "surrounding whitespace is trimmed out of the loaded source, not out of the allowlist check");
        cut.FindAll(".tm-agent-badge__initials").Should().BeEmpty();
    }

    [Fact]
    public void AnAvatarOnlyBadge_IsNamedByItsAgent_AndItsState()
    {
        var cut = Render<TmAgentBadge>(parameters => parameters
            .Add(badge => badge.Agent, Agent())
            .Add(badge => badge.Status, AgentActivityStatus.Failed)
            .Add(badge => badge.ShowName, false));

        var root = cut.Find(".tm-agent-badge");
        root.GetAttribute("role").Should().Be("img",
            "a composed avatar has to become one labelled graphic — an aria-label on a role-less span is ignored");
        root.GetAttribute("aria-label").Should().Be("Data analyst, Failed");
        root.GetAttribute("aria-live").Should().BeNull("the badge is not an announcement channel");
    }

    [Fact]
    public void AnAvatarOnlyBadge_WithoutAStatus_IsNamedByTheAgentAlone()
    {
        var cut = Render<TmAgentBadge>(parameters => parameters
            .Add(badge => badge.Agent, Agent())
            .Add(badge => badge.ShowName, false));

        cut.Find(".tm-agent-badge").GetAttribute("aria-label").Should().Be("Data analyst");
    }

    [Fact]
    public void ANamedBadge_CarriesNoLabel_SoNothingIsAnnouncedTwice()
    {
        var cut = Render<TmAgentBadge>(parameters => parameters
            .Add(badge => badge.Agent, Agent())
            .Add(badge => badge.Status, AgentActivityStatus.Queued));

        var root = cut.Find(".tm-agent-badge");
        root.HasAttribute("aria-label").Should().BeFalse(
            "the visible name and state word already are the accessible name (WCAG 2.5.3)");
        root.GetAttribute("role").Should().BeNull();
    }

    [Fact]
    public void AWhitespaceName_RendersTheLocalizedFallback_InsteadOfAnEmptyGap()
    {
        var cut = Render<TmAgentBadge>(parameters => parameters
            .Add(badge => badge.Agent, new AgentRef("   ", "empty")));

        cut.Find(".tm-agent-badge__name").TextContent.Trim().Should().Be("Unnamed agent");
        cut.Find(".tm-agent-badge__initials").TextContent.Trim().Should().Be("?");
    }

    [Fact]
    public void AnAvatarOnlyUnnamedBadge_IsNamedByTheFallback()
    {
        var cut = Render<TmAgentBadge>(parameters => parameters
            .Add(badge => badge.Agent, new AgentRef(string.Empty, "empty"))
            .Add(badge => badge.ShowName, false));

        cut.Find(".tm-agent-badge").GetAttribute("aria-label").Should().Be("Unnamed agent");
    }

    [Fact]
    public void WithoutAnAgent_NothingIsRendered()
    {
        var cut = Render<TmAgentBadge>(parameters => parameters.Add(badge => badge.Agent, null));

        cut.FindAll(".tm-agent-badge").Should().BeEmpty(
            "a snapshot that has not loaded an agent yet renders no anonymous avatar");
    }

    [Theory]
    [InlineData(AgentBadgeSize.Sm, "sm")]
    [InlineData(AgentBadgeSize.Md, "md")]
    [InlineData(AgentBadgeSize.Lg, "lg")]
    public void TheSize_IsAModifierClass(AgentBadgeSize size, string slug)
    {
        var cut = Render<TmAgentBadge>(parameters => parameters
            .Add(badge => badge.Agent, Agent())
            .Add(badge => badge.Size, size));

        cut.Find(".tm-agent-badge").ClassList.Should().Contain($"tm-agent-badge--{slug}");
    }

    [Fact]
    public void TheDefaultSize_IsMd()
    {
        var cut = Render<TmAgentBadge>(parameters => parameters.Add(badge => badge.Agent, Agent()));

        cut.Find(".tm-agent-badge").ClassList.Should().Contain("tm-agent-badge--md");
    }

    [Fact]
    public void TheHostClass_AndTheTestIdConventions_AreHonoured()
    {
        var cut = Render<TmAgentBadge>(parameters => parameters
            .Add(badge => badge.Agent, Agent())
            .Add(badge => badge.Class, "lane-header-agent")
            .Add(badge => badge.TestIdPrefix, "lane-1")
            .AddUnmatched("data-extra", "x"));

        var root = cut.Find(".tm-agent-badge");
        root.ClassList.Should().Contain("lane-header-agent");
        root.GetAttribute("data-testid").Should().Be("lane-1-agent-badge");
        root.GetAttribute("data-extra").Should().Be("x");
    }

    [Fact]
    public void DataTestId_OverridesTheRootTestId()
    {
        var cut = Render<TmAgentBadge>(parameters => parameters
            .Add(badge => badge.Agent, Agent())
            .Add(badge => badge.DataTestId, "my-badge"));

        cut.Find(".tm-agent-badge").GetAttribute("data-testid").Should().Be("my-badge");
    }

    [Fact]
    public void AnUnknownStateValue_RendersNoChip()
    {
        var cut = Render<TmAgentBadge>(parameters => parameters
            .Add(badge => badge.Agent, Agent())
            .Add(badge => badge.Status, (AgentActivityStatus)99));

        cut.FindAll(".tm-agent-badge__status").Should().BeEmpty(
            "an enum value from a newer backend must not paint a verdict the component cannot name");
        cut.Find(".tm-agent-badge").GetAttribute("data-status").Should().BeNull();
    }

    [Fact]
    public void EveryState_HasItsOwnLocalizedWord()
    {
        var localizer = BuildEnglishLocalizer();

        foreach (var status in Enum.GetValues<AgentActivityStatus>())
        {
            var cut = Render<TmAgentBadge>(parameters => parameters
                .Add(badge => badge.Agent, Agent())
                .Add(badge => badge.Status, status));

            cut.Find(".tm-agent-badge__status-text").TextContent.Trim().Should().Be(
                AgentStatusPresentation.Text(localizer, status),
                "{0} renders the shared localized word", status);
        }
    }
}
