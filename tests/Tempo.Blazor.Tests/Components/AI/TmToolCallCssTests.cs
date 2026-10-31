using System.Text.RegularExpressions;
using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// Hygiene gate for the two Fáze 4 stylesheets (plan 7f21f26b):
/// <c>_ai-tool-call.css</c> (the card, its actions and the group) and <c>_ai-thinking.css</c> (the
/// thinking indicator, the reasoning panel and the shared shimmer/spinner motion). Every class the
/// markup emits carries a rule, the coarse-pointer floor and the forced-colors block survive, the only
/// animated subjects (the shimmer and the spinner) are declared in <c>_ai-thinking.css</c> and each of
/// them has a reduced-motion rule.
/// </summary>
/// <remarks>
/// The token rules (no colour literal, no raw palette step, no <c>[data-theme]</c> block, every
/// <c>var(--tm-*)</c> resolvable) are already enforced for the whole package by
/// <c>AiMarkdownViewCssTests</c>, which reads every file under the package's <c>wwwroot/css</c>.
/// </remarks>
public sealed class TmToolCallCssTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static readonly Regex Comment = new(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline, RegexTimeout);

    private static readonly Regex RazorComment = new(@"@\*.*?\*@", RegexOptions.Compiled | RegexOptions.Singleline, RegexTimeout);

    private static readonly Regex CssClassSelector = new(@"\.(tm-[a-zA-Z][\w-]*)", RegexOptions.Compiled, RegexTimeout);

    private static string Root => ThemeCss.RepositoryRoot().FullName;

    private static string Stylesheet(string file)
        => Comment.Replace(
            File.ReadAllText(Path.Combine(Root, "src", "Tempo.Blazor.AI", "wwwroot", "css", "components", file)),
            " ");

    private static string Markup(string component)
        => RazorComment.Replace(
            File.ReadAllText(Path.Combine(Root, "src", "Tempo.Blazor.AI", "Components", "AI", component)),
            string.Empty);

    /// <summary>
    /// A class token emitted by the markup's <c>class</c> attributes, with the interpolated modifier suffix
    /// trimmed. Only the CLASS attribute is read: an element id such as <c>tm-tool-call-details-{guid}</c>
    /// names a disclosure target, not a styleable class.
    /// </summary>
    private static HashSet<string> Emitted(string component, params string[] prefixes)
        => Regex.Matches(Markup(component), @"class\s*=\s*[""']([^""']*)[""']")
            .SelectMany(match => match.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Select(token => token.Split('@', '{')[0].TrimEnd('-'))
            .Where(name => prefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The body of the first block whose selector starts at <paramref name="selector"/>, matched by brace
    /// depth on the comment-stripped stylesheet (so one nested rule cannot truncate the body).
    /// </summary>
    private static string Block(string css, string selector)
    {
        var start = css.IndexOf(selector, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"the stylesheet must keep its {selector} block");

        var opened = css.IndexOf('{', start);
        var depth = 0;
        for (var index = opened; index < css.Length; index++)
        {
            depth += css[index] switch { '{' => 1, '}' => -1, _ => 0 };
            if (depth == 0)
            {
                return css[(opened + 1)..index];
            }
        }

        return css[(opened + 1)..];
    }

    private static HashSet<string> Styled(string file)
        => CssClassSelector.Matches(Stylesheet(file))
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void EveryToolCallClassTheMarkupEmits_HasARule()
    {
        var emitted = Emitted("TmToolCallCard.razor", "tm-tool-call");
        var styled = Styled("_ai-tool-call.css");

        emitted.Should().NotBeEmpty("the card emits its own BEM classes");
        emitted.Should().BeSubsetOf(styled,
            "a class the card emits without a rule ships unstyled. Missing: {0}",
            string.Join(", ", emitted.Except(styled)));
    }

    [Fact]
    public void EveryToolCallGroupClassTheMarkupEmits_HasARule()
    {
        var emitted = Emitted("TmToolCallGroup.razor", "tm-tool-call-group");
        var styled = Styled("_ai-tool-call.css");

        emitted.Should().NotBeEmpty("the group emits its own BEM classes");
        emitted.Should().BeSubsetOf(styled,
            "a class the group emits without a rule ships unstyled. Missing: {0}",
            string.Join(", ", emitted.Except(styled)));
    }

    [Fact]
    public void EveryThinkingClassTheMarkupEmits_HasARule()
    {
        var emitted = Emitted("TmThinkingIndicator.razor", "tm-ai-thinking", "tm-ai-shimmer");
        var styled = Styled("_ai-thinking.css");

        emitted.Should().NotBeEmpty("the indicator emits its own BEM classes");
        emitted.Should().BeSubsetOf(styled,
            "a class the indicator emits without a rule ships unstyled. Missing: {0}",
            string.Join(", ", emitted.Except(styled)));
    }

    [Fact]
    public void EveryReasoningClassTheMarkupEmits_HasARule()
    {
        var emitted = Emitted("TmReasoningPanel.razor", "tm-ai-reasoning", "tm-ai-shimmer");
        var styled = Styled("_ai-thinking.css");

        emitted.Should().NotBeEmpty("the reasoning panel emits its own BEM classes");
        emitted.Should().BeSubsetOf(styled,
            "a class the panel emits without a rule ships unstyled. Missing: {0}",
            string.Join(", ", emitted.Except(styled)));
    }

    [Fact]
    public void BothStylesheetsKeepTheCoarsePointerTouchFloor()
    {
        var toolCall = Stylesheet("_ai-tool-call.css");
        toolCall.Should().Contain("@media (pointer: coarse)");
        toolCall.Should().Contain("--tm-touch-target");
        foreach (var selector in new[]
                 {
                     ".tm-tool-call__copy", ".tm-tool-call__retry", ".tm-tool-call__toggle",
                     ".tm-tool-call-group__toggle",
                 })
        {
            toolCall.Should().Contain(selector, $"{selector} is a control a thumb has to reach");
        }

        var thinking = Stylesheet("_ai-thinking.css");
        thinking.Should().Contain("@media (pointer: coarse)", "the reasoning disclosure is a thumb target too");
        thinking.Should().Contain(".tm-ai-reasoning__toggle");
    }

    [Fact]
    public void BothStylesheetsKeepTheForcedColorsBlock()
    {
        foreach (var file in new[] { "_ai-tool-call.css", "_ai-thinking.css" })
        {
            Stylesheet(file).Should().Contain("@media (forced-colors: active)",
                $"{file} must survive Windows high contrast");
        }

        Stylesheet("_ai-tool-call.css").Should().Contain("CanvasText");
        Stylesheet("_ai-thinking.css").Should().Contain("CanvasText");
    }

    /// <summary>
    /// <c>forced-color-adjust: none</c> opts a control out of the system palette, so the theme's focus token
    /// would keep painting a ring the system does not guarantee to be visible, and a <c>color: Highlight</c>
    /// label over <c>Canvas</c> has no contrast guarantee either. The blocks therefore move the focus ring to
    /// the system <c>Highlight</c> colour and mark an active state with the system BORDER, keeping the ink
    /// <c>CanvasText</c>.
    /// </summary>
    [Fact]
    public void BothForcedColorsBlocks_KeepTheFocusRingAndTheInkOnSystemColors()
    {
        foreach (var file in new[] { "_ai-tool-call.css", "_ai-thinking.css" })
        {
            var css = Stylesheet(file);
            css.Should().Contain("outline-color: Highlight",
                $"{file} pins focus rings to a theme token inside its forced-colors block otherwise");
            css.Should().NotMatch(@"(?<![-\w])color:\s*Highlight",
                $"{file} must not paint a Highlight-coloured label over Canvas — highlight CANVAS is the fill");
        }
    }

    /// <summary>
    /// The spinner is the one shape the package draws itself: its two dimensions and its stroke come from the
    /// spacing/border tokens, never from a hardcoded <c>1rem</c> / <c>2px</c>, so a host that rescales
    /// <c>--tm-space-*</c> or a dense theme gets a proportional spinner.
    /// </summary>
    [Fact]
    public void TheSpinnerGeometry_ComesFromTokens()
    {
        var spinner = Regex.Match(Stylesheet("_ai-thinking.css"), @"\.tm-ai-thinking__spinner\s*\{([^}]*)\}")
            .Groups[1].Value;

        spinner.Should().NotBeNullOrWhiteSpace("the stylesheet declares the spinner");
        spinner.Should().Contain("var(--tm-space-4)").And.Contain("var(--tm-border-width)");
        spinner.Should().NotMatch(@"(?<![-\w])\d+(?:\.\d+)?(?:rem|px|em)\b",
            "a hardcoded spinner size ignores the spacing scale");
    }

    /// <summary>
    /// The Retry control renders <c>disabled</c> while the host's first retry is still in flight. Inside the
    /// forced-colors block it paints system <c>CanvasText</c> like every other button, and the resting
    /// <c>opacity: 0.6</c> is a filter rather than an ink the high-contrast user chose — over <c>Canvas</c> a
    /// disabled Retry can read as present as an enabled one. The system <c>GrayText</c> keyword is the
    /// disabled ink Windows itself uses, so the state survives the mode.
    /// </summary>
    [Fact]
    public void ForcedColorsPaintTheDisabledRetryWithTheSystemGrayText()
    {
        var block = Block(Stylesheet("_ai-tool-call.css"), "@media (forced-colors: active)");

        var rule = Regex.Match(block, @"\.tm-tool-call__retry:disabled\s*\{([^}]*)\}");

        rule.Success.Should().BeTrue(
            "forced colors paint the disabled Retry with CanvasText unless it gets a disabled-specific rule");
        rule.Groups[1].Value.Should().Contain("color: GrayText");
        rule.Groups[1].Value.Should().Contain("border-color: GrayText");
    }

    /// <summary>
    /// The ends of the sweep are what the reader sees for most of the animation, so an endpoint token under
    /// AA contrast cannot be rescued by the bright peak in the middle. The reduced-motion fallback already
    /// settles on <c>--tm-color-text-secondary</c>; the gradient must use the SAME token, so the moving and
    /// the still label have one contrast guarantee (and a host redefining the muted alias cannot lower it).
    /// </summary>
    [Fact]
    public void TheShimmerEndpoints_UseTheSameTokenAsTheReducedMotionFallback()
    {
        var thinking = Stylesheet("_ai-thinking.css");

        var gradient = Regex.Match(thinking, @"\.tm-ai-shimmer\s*\{[^}]*?background-image\s*:([^;]*);")
            .Groups[1].Value;

        gradient.Should().NotBeNullOrWhiteSpace("the stylesheet declares the shimmer gradient");
        Regex.Matches(gradient, @"var\(--tm-color-text-secondary\)").Count.Should().Be(2,
            "both ends of the sweep must paint in the secondary text colour");
        gradient.Should().NotContain("--tm-color-text-muted",
            "the muted alias has no contrast floor, so it cannot be what the label mostly shows");

        Block(thinking, "@media (prefers-reduced-motion: reduce)")
            .Should().Contain("color: var(--tm-color-text-secondary)",
                "the fallback and the gradient must paint the label with the same ink");
    }

    [Fact]
    public void TheOnlyAnimatedSubjectsLiveInTheThinkingStylesheet_AndBothAreCoveredByReducedMotion()
    {
        Stylesheet("_ai-tool-call.css").Should().NotContain("animation:", "a tool call card does not move")
            .And.NotContain("@keyframes")
            .And.NotContain("transition:", "nothing in the card animates, so it needs no reduce block here");

        var thinking = Stylesheet("_ai-thinking.css");
        thinking.Should().Contain("@keyframes");
        thinking.Should().Contain("@media (prefers-reduced-motion: reduce)");
        thinking.Should().Contain(".tm-ai-shimmer");
        thinking.Should().Contain(".tm-ai-thinking__spinner");
    }
}
