using System.Text.RegularExpressions;
using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// Hygiene gate for <c>TmPromptLibrary</c> (plan 3e96f6d8, phase 6): every class the markup emits has a rule in
/// <c>_ai-prompt-library.css</c>, the surface declares no motion of its own, a coarse pointer raises the row and
/// every action to the 44 px touch target, forced colours hand the focus ring back to the system's
/// <c>Highlight</c> and keep the variable marks visible, and every component-local knob the stylesheet reads is
/// DECLARED on the root (an undeclared <c>var(--tm-…)</c> is an undefined token).
/// </summary>
/// <remarks>
/// The package-wide token rules (no colour literal, no raw palette step, no <c>[data-theme]</c> block, every
/// <c>var(--tm-*)</c> resolvable) are enforced for every stylesheet of the package by
/// <c>AiMarkdownViewCssTests</c>.
/// </remarks>
public sealed class TmPromptLibraryCssTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static readonly Regex Comment =
        new(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline, RegexTimeout);

    private static readonly Regex RazorComment =
        new(@"@\*.*?\*@", RegexOptions.Compiled | RegexOptions.Singleline, RegexTimeout);

    private static readonly Regex CssClassSelector =
        new(@"\.(tm-[a-zA-Z][\w-]*)", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex MotionDeclaration =
        new(@"(^|;)\s*(transition(-[a-z-]+)?|animation(-[a-z-]+)?)\s*:", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex CssRule =
        new(@"([^{}]+)\{([^{}]*)\}", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex DeclaredKnob =
        new(@"(--tm-prompt-library-[a-z0-9-]+)\s*:", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex UsedKnob =
        new(@"var\(\s*(--tm-prompt-library-[a-z0-9-]+)", RegexOptions.Compiled, RegexTimeout);

    private const string Prefix = "tm-prompt-library";

    private static string Root => ThemeCss.RepositoryRoot().FullName;

    private static string Stylesheet()
        => Comment.Replace(
            File.ReadAllText(Path.Combine(
                Root, "src", "Tempo.Blazor.AI", "wwwroot", "css", "components", "_ai-prompt-library.css")),
            " ");

    private static string Markup()
    {
        var files = Directory
            .EnumerateFiles(Path.Combine(Root, "src", "Tempo.Blazor.AI", "Components", "AI"), "TmPromptLibrary.razor*")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText);

        return RazorComment.Replace(string.Concat(files), string.Empty);
    }

    private static HashSet<string> Emitted()
    {
        var source = Markup();
        var fromAttributes = Regex.Matches(source, @"class\s*=\s*""([^""]*)""")
            .SelectMany(match => match.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Select(token => token.Split('@', '{')[0].TrimEnd('-'));

        var fromLiterals = Regex.Matches(source, @"""([^""]*" + Prefix + @"[^""]*)""")
            .SelectMany(match => match.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Select(token => token.Split('@', '{')[0].TrimEnd('-'));

        return fromAttributes
            .Concat(fromLiterals)
            .Where(name => name.StartsWith(Prefix, StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> Styled()
        => CssClassSelector.Matches(Stylesheet())
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

    private static string Declarations(string selector)
    {
        var match = CssRule.Matches(Stylesheet())
            .Cast<Match>()
            .FirstOrDefault(candidate => candidate.Groups[1].Value
                .Split(',')
                .Any(token => string.Equals(token.Trim(), selector, StringComparison.Ordinal)));

        match.Should().NotBeNull($"the stylesheet declares a rule for {selector}");
        return ThemeCss.Normalise(match!.Groups[2].Value);
    }

    private static string Block(string selector)
    {
        var css = Stylesheet();
        var start = css.IndexOf(selector, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"the stylesheet declares a {selector} block");

        var open = css.IndexOf('{', start);
        var depth = 0;
        for (var index = open; index < css.Length; index++)
        {
            depth += css[index] switch { '{' => 1, '}' => -1, _ => 0 };
            if (depth == 0)
            {
                return css[(open + 1)..index];
            }
        }

        return css[(open + 1)..];
    }

    [Fact]
    public void EveryClassTheMarkupEmits_HasARule()
    {
        var missing = Emitted().Except(Styled()).OrderBy(name => name, StringComparer.Ordinal).ToList();

        missing.Should().BeEmpty(
            $"TmPromptLibrary emits {string.Join(", ", missing)} without a rule in _ai-prompt-library.css — an "
            + "unstyled class ships as an unstyled surface");
    }

    [Fact]
    public void TheSurfaceDeclaresNoMotion()
    {
        var motion = MotionDeclaration.Matches(Stylesheet()).Cast<Match>()
            .Select(match => match.Value.Trim()).ToList();

        motion.Should().BeEmpty(
            "the live preview re-renders on every keystroke, so the surface owns no transition and no animation "
            + "and needs no reduced-motion block");
    }

    [Fact]
    public void EveryKnobTheStylesheetReads_IsDeclaredOnTheRoot()
    {
        var declared = DeclaredKnob.Matches(Declarations($".{Prefix}"))
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
        var used = UsedKnob.Matches(Stylesheet())
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        var undeclared = used.Except(declared).OrderBy(name => name, StringComparer.Ordinal).ToList();

        undeclared.Should().BeEmpty(
            "a component-local custom property that no rule declares is an undefined token; declare every "
            + $"knob on the root rule ({string.Join(", ", undeclared)})");
    }

    [Fact]
    public void TheRoot_CanShrinkInsideAHostsGrid()
    {
        Declarations($".{Prefix}").Should().Contain("min-inline-size: 0",
            "a component root inside a grid or flex item otherwise grows to its content and is clipped");
    }

    [Fact]
    public void ACoarsePointer_RaisesTheRowAndEveryActionToTheTouchTarget()
    {
        var coarse = Block("@media (pointer: coarse)");

        foreach (var selector in new[]
                 {
                     $".{Prefix}__row", $".{Prefix}__back", $".{Prefix}__use", $".{Prefix}__copy",
                     $".{Prefix}__create", $".{Prefix}__edit", $".{Prefix}__delete",
                 })
        {
            coarse.Should().Contain(selector, $"{selector} is a finger target on a phone");
        }

        coarse.Should().Contain("var(--tm-touch-target)",
            "the shared 44 px target token is what a coarse pointer gets");
    }

    [Fact]
    public void ACoarsePointer_GivesTheRowTitleTheFullWidth_AndMovesTheActionsBelow()
    {
        var coarse = Block("@media (pointer: coarse)");

        coarse.Should().Contain("grid-template-areas",
            "an auto-placed body cell collapses to the narrow column and the title wraps to 85 px");
        coarse.Should().Contain("min-block-size: var(--tm-touch-target)");
    }

    [Fact]
    public void ForcedColors_HandTheFocusRingBackToTheSystemHighlight()
    {
        var block = Block("@media (forced-colors: active)");

        foreach (var selector in new[]
                 {
                     $".{Prefix}:focus-visible", $".{Prefix}__row:focus-visible",
                     $".{Prefix}__back:focus-visible", $".{Prefix}__use:focus-visible",
                     $".{Prefix}__copy:focus-visible", $".{Prefix}__create:focus-visible",
                     $".{Prefix}__edit:focus-visible", $".{Prefix}__delete:focus-visible",
                 })
        {
            var rule = Regex.Match(
                block,
                Regex.Escape(selector) + @"\s*\{([^}]*)\}",
                RegexOptions.Compiled,
                RegexTimeout);

            rule.Success.Should().BeTrue($"forced colours must repaint {selector}'s ring");
            rule.Groups[1].Value.Should().Contain("outline-color: Highlight");
        }
    }

    [Fact]
    public void ForcedColors_KeepTheVariableMarksVisible()
    {
        var block = Block("@media (forced-colors: active)");

        block.Should().Contain($"var(--tm-prompt-library-token-bg)");
        block.Should().Contain("forced-color-adjust: none",
            "a mark is background-only, so it collapses into one system colour without an explicit opt-out");
        block.Should().Contain($"var(--tm-prompt-library-token-empty-border)",
            "the unfilled placeholder must stay distinguishable from a filled token without colour alone");
    }

    [Fact]
    public void TheRowTitleAndThePreview_WrapInsteadOfTruncating()
    {
        Declarations($".{Prefix}__row-title").Should().Contain("overflow-wrap: anywhere",
            "a host-supplied title is the whole point of the row, so it wraps at 320 px");
        Declarations($".{Prefix}__row-title").Should().NotContain("text-overflow: ellipsis");

        Declarations($".{Prefix}__preview-body").Should().Contain("white-space: pre-wrap",
            "a prompt's own line breaks and spacing are part of it");
    }

    [Fact]
    public void ThePreview_ClipsLongTokensInsteadOfGrowingTheCard()
    {
        Declarations($".{Prefix}__preview-body").Should().Contain("overflow-wrap: anywhere");
        Declarations($".{Prefix}__preview").Should().Contain("min-inline-size: 0");
    }

    [Fact]
    public void ThePreviewLabel_IsSetInTheUprightCase()
    {
        var declarations = Declarations($".{Prefix}__preview-label");

        declarations.Should().NotContain("text-transform",
            "uppercase letterforms distort the Czech and Slovak diacritics these strings are translated into");
        declarations.Should().NotContain("letter-spacing",
            "there is no letter-spacing token in the design system, and loosened uppercase text is a symptom of the "
            + "uppercase transform this label must not use");
    }

    [Fact]
    public void TheTokenPadding_ComesFromASpacingToken()
    {
        Declarations($".{Prefix}__token").Should().Contain("padding-inline: var(--tm-space-0-5)",
            "a raw 2px literal bypasses the spacing scale the rest of the surface reads");
    }

    [Fact]
    public void TheUnavailableAction_IsStillATargetAndSaysItWillNotSubmit()
    {
        Declarations($".{Prefix}__use[aria-disabled=\"true\"]").Should().Contain("cursor: not-allowed",
            "the action stays focusable (aria-disabled, not disabled), so a pointer user needs the cue too");
    }
}
