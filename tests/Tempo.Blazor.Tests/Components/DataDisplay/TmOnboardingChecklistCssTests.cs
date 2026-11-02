using System.Text.RegularExpressions;
using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Components.DataDisplay;

/// <summary>
/// The CSS contract of <c>TmOnboardingChecklist</c> (plan 5d396592, phase 5): the sheet paints through
/// semantic tokens only (the dark theme redefines tokens, never this file), it is imported and shipped
/// in the committed bundle, the celebration's pop is neutralised under reduced motion, the focus ring
/// survives forced colours through the system <c>Highlight</c> colour, and coarse pointers get 44 px
/// targets instead of desktop-sized ones.
/// </summary>
public class TmOnboardingChecklistCssTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    /// <summary>A colour literal outside a custom-property definition — the stylesheet rule.</summary>
    private static readonly Regex ColorLiteral =
        new(@"#[0-9a-fA-F]{3,8}\b|rgba?\(|hsla?\(", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex ManualDarkBlock =
        new(@"\[data-theme\s*=\s*[""']dark[""']\]|\.tm-dark\b", RegexOptions.Compiled, RegexTimeout);

    /// <summary>Every class the component emits, as the stylesheet has to cover it.</summary>
    private static readonly string[] EmittedClasses =
    [
        "tm-onboarding-checklist", "tm-onboarding-checklist--list", "tm-onboarding-checklist--card",
        "tm-onboarding-checklist--grid", "tm-onboarding-checklist__header", "tm-onboarding-checklist__title",
        "tm-onboarding-checklist__dismiss", "tm-onboarding-checklist__progress",
        "tm-onboarding-checklist__progress-text", "tm-onboarding-checklist__celebration",
        "tm-onboarding-checklist__celebration-icon", "tm-onboarding-checklist__celebration-text",
        "tm-onboarding-checklist__celebration-title", "tm-onboarding-checklist__celebration-body",
        "tm-onboarding-checklist__empty", "tm-onboarding-checklist__group",
        "tm-onboarding-checklist__group-toggle", "tm-onboarding-checklist__group-title",
        "tm-onboarding-checklist__group-count", "tm-onboarding-checklist__chevron",
        "tm-onboarding-checklist__group-body", "tm-onboarding-checklist__list",
        "tm-onboarding-checklist__item", "tm-onboarding-checklist__item--done",
        "tm-onboarding-checklist__item--optional", "tm-onboarding-checklist__checkbox",
        "tm-onboarding-checklist__body", "tm-onboarding-checklist__label",
        "tm-onboarding-checklist__task-title", "tm-onboarding-checklist__badge",
        "tm-onboarding-checklist__description", "tm-onboarding-checklist__action",
    ];

    private static string Read(params string[] path)
        => File.ReadAllText(Path.Combine(new[] { ThemeCss.RepositoryRoot().FullName }.Concat(path).ToArray()));

    private static string Sheet() => Read("src", "Tempo.Blazor", "wwwroot", "css", "components", "_onboarding-checklist.css");

    [Fact]
    public void TheSheet_UsesSemanticTokensOnly()
    {
        var css = Sheet();

        ColorLiteral.Matches(css).Should().BeEmpty(
            "the checklist paints through --tm-* tokens; the dark theme redefines tokens, never this file");
        ManualDarkBlock.Matches(css).Should().BeEmpty("no component-local dark block: dark values live in tokens-dark.css");
    }

    [Fact]
    public void TheSheet_IsImported_AndShipsInTheCommittedBundle()
    {
        Read("src", "Tempo.Blazor", "wwwroot", "css", "tempo-blazor.css")
            .Should().Contain("@import \"components/_onboarding-checklist.css\";");

        var bundle = Read("src", "Tempo.Blazor", "wwwroot", "css", "tempo-blazor.bundled.css");
        bundle.Should().Contain(".tm-onboarding-checklist{", "the committed bundle is what a host loads");
        bundle.Should().Contain("tm-onboarding-checklist__celebration-icon");
    }

    [Fact]
    public void EveryEmittedClass_HasARule()
    {
        var css = Sheet();

        var missing = EmittedClasses
            .Where(name => !Regex.IsMatch(css, @"\." + Regex.Escape(name) + @"(?![\w-])", RegexOptions.None, RegexTimeout))
            .ToList();

        missing.Should().BeEmpty("a class the markup emits without a rule is an unstyled contract");
    }

    [Fact]
    public void TheCelebrationAnimation_IsNeutralisedByReducedMotion()
    {
        var animations = Read("src", "Tempo.Blazor", "wwwroot", "css", "animations.css");

        animations.Should().Contain("@keyframes tm-onboarding-celebration-pop", "the celebration pops through one shared keyframe");
        Sheet().Should().Contain("animation: tm-onboarding-celebration-pop");

        var reduceBlock = ReducedMotionBlock(animations);
        reduceBlock.Should().Contain(".tm-onboarding-checklist__celebration-icon",
            "every animated Tempo subject must appear in the reduced-motion enumeration (ReducedMotionSelectorTests recomputes it)");
        reduceBlock.Should().Contain("animation-duration: 0.01ms");
    }

    [Fact]
    public void TheFocusRing_SurvivesForcedColours()
    {
        var css = Sheet();
        var forced = Block(css, "@media (forced-colors: active)");

        forced.Should().Contain("outline: 2px solid Highlight",
            "High Contrast replaces every token, so the focus ring has to use the system Highlight colour");
        forced.Should().Contain("box-shadow: none");
        forced.Should().Contain(".tm-onboarding-checklist__dismiss:focus-visible");
        forced.Should().Contain(".tm-onboarding-checklist__group-toggle:focus-visible");
        forced.Should().Contain(".tm-onboarding-checklist__checkbox:focus-visible",
            "the checkbox is a native control but the token ring around it disappears in High Contrast");
        forced.Should().Contain(".tm-onboarding-checklist__dismiss:focus,",
            "a programmatic:focus must also keep a visible ring");
    }

    [Fact]
    public void TheDismissButton_IsAFull32PxTarget_OnAFinePointer()
    {
        var dismiss = Block(Sheet(), ".tm-onboarding-checklist__dismiss {");

        dismiss.Should().Contain("inline-size: var(--tm-space-8)")
            .And.Contain("block-size: var(--tm-space-8)",
                "WCAG 2.5.8 asks for 24 px; 32 px leaves a margin, 24 px only just passed (review finding, UX #2)");
    }

    [Fact]
    public void CoarsePointers_GetTouchSizedTargets()
    {
        var coarse = Block(Sheet(), "@media (pointer: coarse)");

        coarse.Should().Contain(".tm-onboarding-checklist__dismiss");
        coarse.Should().Contain(".tm-onboarding-checklist__group-toggle");
        coarse.Should().Contain(".tm-onboarding-checklist__label",
            "the label is what enlarges the checkbox's hit area, so the box keeps its visual size");
        coarse.Should().Contain(".tm-onboarding-checklist__action");
        coarse.Should().Contain("var(--tm-touch-target)", "44 px is the library-wide coarse target");
        coarse.Should().Contain("min-block-size: var(--tm-touch-target)");
    }

    [Fact]
    public void TheCollapsedGroupBody_IsRemovedFromLayout()
    {
        Sheet().Should().Contain(".tm-onboarding-checklist__group-body[hidden]",
            "the author display:flex outranks the user agent's [hidden] rule, so the component declares it");
    }

    private static string Block(string css, string atRule)
    {
        var start = css.IndexOf(atRule, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"{atRule} block must exist");
        var open = css.IndexOf('{', start);
        var depth = 0;
        for (var i = open; i < css.Length; i++)
        {
            if (css[i] == '{') depth++;
            else if (css[i] == '}' && --depth == 0) return css[start..(i + 1)];
        }

        return css[start..];
    }

    private static string ReducedMotionBlock(string css) => Block(css, "@media (prefers-reduced-motion: reduce)");
}
