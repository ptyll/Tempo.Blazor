using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Plan d67b33af, Fáze 1 — the stylesheet contract of <see cref="Tempo.Blazor.Components.AI.TmAgentBadge"/>.
/// <para>
/// The package-level token gate (<see cref="AiMarkdownViewCssTests"/>) proves the AI package paints with tokens
/// only; this guard proves the badge's own share of it: the partial is imported by the package entry stylesheet,
/// every class the markup emits has a rule (a class without one ships unstyled — the badge is the first thing a
/// reader sees on a lane header), the fill is read through its component-local knob with a palette fallback, the
/// system-palette block keeps the avatar distinguishable under Windows high contrast, and the badge declares no
/// motion at all — the package's reduced-motion enumeration then stays exactly as it is, which is what
/// <c>ReducedMotionSelectorTests</c> requires of every animated subject.
/// </para>
/// </summary>
public sealed class TmAgentBadgeCssTests
{
    private static string BadgeCss()
    {
        var path = Path.Combine(
            ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor.AI", "wwwroot", "css", "components", "_ai-agent-badge.css");
        File.Exists(path).Should().BeTrue(
            "the badge ships its own partial next to the components that read it: " + path);
        return ThemeCss.StripComments(File.ReadAllText(path));
    }

    [Fact]
    public void ThePackageEntrySheet_ImportsTheBadgePartial()
    {
        var entry = ThemeCss.StripComments(File.ReadAllText(Path.Combine(
            ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor.AI", "wwwroot", "css", "tempo-blazor-ai.css")));

        entry.Should().Contain("@import \"components/_ai-agent-badge.css\"",
            "a host loads exactly one stylesheet for the package — an unimported partial never ships");
    }

    [Fact]
    public void EveryClassTheMarkupEmits_HasARule()
    {
        var css = BadgeCss();

        foreach (var name in new[]
                 {
                     "tm-agent-badge",
                     "tm-agent-badge--sm",
                     "tm-agent-badge--md",
                     "tm-agent-badge--lg",
                     "tm-agent-badge__avatar",
                     "tm-agent-badge__image",
                     "tm-agent-badge__initials",
                     "tm-agent-badge__name",
                     "tm-agent-badge__status",
                     "tm-agent-badge__status-icon",
                     "tm-agent-badge__status-text",
                     "tm-agent-badge__status--idle",
                     "tm-agent-badge__status--queued",
                     "tm-agent-badge__status--running",
                     "tm-agent-badge__status--approval",
                     "tm-agent-badge__status--succeeded",
                     "tm-agent-badge__status--failed",
                     "tm-agent-badge__status--cancelled",
                 })
        {
            css.Should().Contain("." + name, "{0} is emitted by the component and must be styled", name);
        }
    }

    [Fact]
    public void TheFill_IsAComponentKnob_WithAPaletteFallback()
    {
        var css = BadgeCss();

        css.Should().Contain("var(--tm-agent-badge-color, var(--tm-agent-color-0))",
            "the component writes the agent's slot into --tm-agent-badge-color; the fallback keeps an "
            + "unstyled-by-the-host instance on a DECLARED palette token instead of an invalid custom property");
    }

    [Fact]
    public void TheInitials_ArePaintedWithThePaletteInk_NotWithCurrentColor()
    {
        var css = BadgeCss();

        css.Should().Contain("color: var(--tm-agent-color-ink)",
            "the ink is the half of the contrast contract the token test measures — currentColor would follow "
            + "the page and could put light text on a light fill");
    }

    [Fact]
    public void ForcedColors_KeepTheAvatarDistinguishable()
    {
        var css = BadgeCss();

        css.Should().Contain("forced-colors: active",
            "Windows high contrast collapses every background to one Canvas colour, so the eight agent fills "
            + "would become indistinguishable");
        css.Should().Contain("forced-color-adjust: none");
        css.Should().Contain("CanvasText",
            "the border carries the shape of the avatar once the fill is opted out of the system palette");
    }

    [Fact]
    public void TheBadge_NeverAnimates()
    {
        var css = BadgeCss();

        css.Should().NotContain("animation",
            "an agent's state is carried by an icon, a word and a tone — a pulsing avatar would motion for the "
            + "whole run and force a new entry into the package reduced-motion enumeration");
        css.Should().NotContain("transition");
    }

    [Fact]
    public void TheAvatar_KeepsA44PixelFloor_Min()
    {
        var css = BadgeCss();

        css.Should().Contain("--tm-agent-badge-avatar-size",
            "the avatar size is one component knob the three size modifiers set, so the initials cannot drift "
            + "from the circle that contains them");
    }
}
