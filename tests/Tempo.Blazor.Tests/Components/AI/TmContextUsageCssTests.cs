using System.Text.RegularExpressions;
using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// Hygiene gate for <c>TmContextUsage</c> (plan 3e96f6d8, phase 5): every class the markup emits has a rule in
/// <c>_ai-context-usage.css</c>, the surface declares no motion of its own, a coarse pointer raises the
/// disclosure / remove / compact controls to the 44 px touch target, forced colours keep the section tones and
/// hand the focus ring back to the system's <c>Highlight</c>, and every per-kind tone reads a DECLARED knob on
/// the component root (an undeclared <c>var(--tm-…)</c> is an undefined token).
/// </summary>
/// <remarks>
/// The package-wide token rules (no colour literal, no raw palette step, no <c>[data-theme]</c> block, every
/// <c>var(--tm-*)</c> resolvable) are enforced for every stylesheet of the package by
/// <c>AiMarkdownViewCssTests</c>.
/// </remarks>
public sealed class TmContextUsageCssTests
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
        new(@"(--tm-context-usage-[a-z0-9-]+)\s*:", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex UsedKnob =
        new(@"var\(\s*(--tm-context-usage-[a-z0-9-]+)", RegexOptions.Compiled, RegexTimeout);

    private const string Prefix = "tm-context-usage";

    private static readonly string[] Kinds =
        ["system", "tools", "memory", "files", "chat", "results", "other"];

    private static string Root => ThemeCss.RepositoryRoot().FullName;

    private static string Stylesheet()
        => Comment.Replace(
            File.ReadAllText(Path.Combine(
                Root, "src", "Tempo.Blazor.AI", "wwwroot", "css", "components", "_ai-context-usage.css")),
            " ");

    private static string Markup()
    {
        var files = Directory
            .EnumerateFiles(Path.Combine(Root, "src", "Tempo.Blazor.AI", "Components", "AI"), "TmContextUsage.razor*")
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
            $"TmContextUsage emits {string.Join(", ", missing)} without a rule in _ai-context-usage.css — an "
            + "unstyled class ships as an unstyled surface");
    }

    [Fact]
    public void TheSurfaceDeclaresNoMotion()
    {
        var motion = MotionDeclaration.Matches(Stylesheet()).Cast<Match>()
            .Select(match => match.Value.Trim()).ToList();

        motion.Should().BeEmpty(
            "a polling usage panel that animated would flicker on every refresh; the component owns no "
            + "transition and no animation, so it needs no reduced-motion block");
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
    public void EveryKindTone_HasItsOwnDeclaredKnob_AndItsOwnModifierRule()
    {
        var root = Declarations($".{Prefix}");

        foreach (var kind in Kinds)
        {
            root.Should().Contain($"--tm-context-usage-{kind}:",
                $"the {kind} tone is a declared knob a host can re-point");
            Declarations($".{Prefix}__segment--{kind}").Should()
                .Contain($"var(--tm-context-usage-{kind})", $"the bar segment of {kind} paints its own tone");
            Declarations($".{Prefix}__swatch--{kind}").Should()
                .Contain($"var(--tm-context-usage-{kind})", $"the legend swatch of {kind} paints its own tone");
        }
    }

    [Fact]
    public void TheKindTones_AreSemanticTokens_NotRawPaletteSteps()
    {
        var root = Declarations($".{Prefix}");

        foreach (var kind in Kinds)
        {
            var declaration = Regex.Match(
                root,
                $@"--tm-context-usage-{kind}:\s*([^;]+);",
                RegexOptions.Compiled,
                RegexTimeout);

            declaration.Success.Should().BeTrue();
            declaration.Groups[1].Value.Trim().Should().StartWith("var(--tm-color-",
                "a raw palette step would bypass the dark theme");
        }
    }

    [Fact]
    public void ACoarsePointer_RaisesTheInteractiveControlsToTheTouchTarget()
    {
        var coarse = Block("@media (pointer: coarse)");

        foreach (var selector in new[]
                 {
                     $".{Prefix}__toggle", $".{Prefix}__remove",
                     $".{Prefix}__item-remove", $".{Prefix}__compact",
                 })
        {
            coarse.Should().Contain(selector, $"{selector} is a finger target on a phone");
        }

        coarse.Should().Contain("var(--tm-touch-target)",
            "the shared 44 px target token is what a coarse pointer gets");
    }

    [Fact]
    public void ForcedColors_HandTheFocusRingBackToTheSystemHighlight()
    {
        var block = Block("@media (forced-colors: active)");

        foreach (var selector in new[]
                 {
                     $".{Prefix}__toggle:focus-visible", $".{Prefix}__remove:focus-visible",
                     $".{Prefix}__item-remove:focus-visible", $".{Prefix}__compact:focus-visible",
                     $".{Prefix}:focus-visible",
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
    public void ForcedColors_KeepTheSectionTonesAndTheOverBudgetStateVisible()
    {
        var block = Block("@media (forced-colors: active)");

        block.Should().Contain("forced-color-adjust: none",
            "a tone-coded segment, cell and swatch are background-only, so they collapse into one system "
            + "colour without an explicit opt-out");

        foreach (var selector in new[]
                 {
                     $".{Prefix}__segment", $".{Prefix}__cell--used", $".{Prefix}__swatch",
                 })
        {
            block.Should().Contain(selector, $"{selector} keeps its tone in a forced-colours palette");
        }

        block.Should().Contain($".{Prefix}--over-budget",
            "the over-budget mark must not rely on the danger tone alone");
        block.Should().Contain("CanvasText");
    }

    [Fact]
    public void TheBarClipsItsFloorWidths_InsteadOfOverflowingTheCard()
    {
        Declarations($".{Prefix}__bar").Should().Contain("overflow: hidden",
            "a sliver floor can push the total past 100 %, so the track clips instead of growing the card");
    }

    [Fact]
    public void TheTableRows_WrapTheirLabels_InsteadOfTruncating()
    {
        var label = Declarations($".{Prefix}__label");

        label.Should().Contain("overflow-wrap: anywhere",
            "a host-supplied section label is the whole point of the row, so it wraps at 320 px");
        label.Should().NotContain("text-overflow: ellipsis");
    }
}
