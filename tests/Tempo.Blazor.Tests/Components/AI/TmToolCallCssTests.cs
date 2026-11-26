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
            css.Should().NotMatchRegex(@"(?<![-\w])color:\s*Highlight",
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
        spinner.Should().NotMatchRegex(@"(?<![-\w])\d+(?:\.\d+)?(?:rem|px|em)\b",
            "a hardcoded spinner size ignores the spacing scale");
    }

    /// <summary>
    /// Plan b75365fb fáze 4b (EmusCz 2.13.0, remaining 463b8c0b): the retry in flight kept a literal
    /// <c>opacity: 0.6</c> and <c>cursor: default</c>, so it read differently from every other disabled
    /// control of the package. The disabled retry speaks the shared idiom the follow-up chip already
    /// carries: the <c>--tm-opacity-disabled</c> token (never a literal alpha) and the
    /// <c>not-allowed</c> cursor. Fáze 4d moved the state from the native <c>:disabled</c> to
    /// <c>[aria-disabled="true"]</c> — the button stays focusable while the host's task runs (WCAG
    /// 2.4.3) — so the same idiom must hang on the attribute selector. The review of fáze 4d then split
    /// the rule in two (WCAG 1.4.11): the BUTTON box carries only the cursor, because an opacity on the
    /// box would dim the focus outline with it — and the in-flight retry is exactly the control that
    /// keeps the keyboard focus; the DIM moved to the content (<c>&gt; *</c>), where it paints the
    /// identical look over the transparent box. The <c>&gt; *</c> selector still carries the
    /// <c>[aria-disabled="true"]</c> marker, so the package ratchet
    /// (<c>PackageCss_DisabledRules_SpeakTheSharedDisabledIdiom</c>) sweeps it for literal alphas.
    /// Mutant guards: restoring the literal or the default cursor turns this red, and so does an
    /// opacity back on the button block (the focus ring would dim again).
    /// </summary>
    [Fact]
    public void DisabledRetry_DimsThroughTheSharedToken_AndRefusesThePointer()
    {
        var button = ThemeCss.Normalise(Block(Stylesheet("_ai-tool-call.css"), ".tm-tool-call__retry[aria-disabled=\"true\"]"));

        button.Should().Contain("cursor: not-allowed",
            "a refused click must say so under the pointer, not keep the default arrow");
        button.Should().NotContain("cursor: default",
            "cursor: default is the pre-4b idiom — the refused click shows not-allowed");
        button.Should().NotMatchRegex(@"(?<![-\w])opacity\s*:",
            "an opacity on the button box dims the focus outline with it — the in-flight retry keeps " +
            "the keyboard focus, so its focus indicator must keep full contrast (WCAG 1.4.11); the dim " +
            "belongs on the content");

        var content = ThemeCss.Normalise(Block(Stylesheet("_ai-tool-call.css"), ".tm-tool-call__retry[aria-disabled=\"true\"] > *"));

        content.Should().Contain("opacity: var(--tm-opacity-disabled)",
            "the disabled retry dims its content through the shared token, never a literal alpha");
        content.Should().NotMatchRegex(@"(?<![-\w])opacity:\s*[\d.]",
            "a literal opacity pins one value the token cannot rescale");
    }

    /// <summary>
    /// The hover fill is an affordance of the ENABLED retry only: a retry in flight under the pointer
    /// keeps its resting surface, so every <c>:hover</c> rule that reaches the retry excludes the
    /// aria-disabled state (<c>:hover:not([aria-disabled="true"])</c> — the scheduled-runs day step
    /// carries the same attribute-form guard).
    /// </summary>
    [Fact]
    public void Retry_HoverFill_AppliesOnlyWhileEnabled()
    {
        var hoverRules = Regex.Matches(
                Stylesheet("_ai-tool-call.css"),
                @"([^{}]*\.tm-tool-call__retry:hover[^{}]*)\{([^}]*)\}")
            .SelectMany(match => ThemeCss.SelectorParts(match.Groups[1].Value))
            .Where(selector => selector.Contains(".tm-tool-call__retry:hover", StringComparison.Ordinal))
            .ToList();

        hoverRules.Should().NotBeEmpty("the enabled retry keeps its hover fill");
        hoverRules.Should().OnlyContain(selector => selector.Contains(":not([aria-disabled=\"true\"])", StringComparison.Ordinal),
            "an in-flight retry under the pointer must keep its resting surface — the hover fill is the " +
            "enabled control's affordance. Offending: {0}", string.Join(", ", hoverRules));
    }

    /// <summary>
    /// The Retry control renders <c>aria-disabled="true"</c> while the host's first retry is still in
    /// flight. Inside the forced-colors block it paints system <c>CanvasText</c> like every other button,
    /// and the resting content dim is a filter rather than an ink the high-contrast user chose — over
    /// <c>Canvas</c> a disabled Retry can read as present as an enabled one. The system <c>GrayText</c>
    /// keyword is the disabled ink Windows itself uses, so the state survives the mode. And because the
    /// surface keeps author colours, the resting dim must be dropped: opacity over a system colour
    /// lowers the contrast the system itself guarantees, so the content rule pins <c>opacity: 1</c> and
    /// lets GrayText alone carry the state (the same rule <c>_ai-message.css</c> carries for its
    /// disabled follow-up chip). Since the fáze-4d review the dim lives on the content
    /// (<c>&gt; *</c>), so the forced-colors override targets the same children.
    /// </summary>
    [Fact]
    public void ForcedColorsPaintTheDisabledRetryWithTheSystemGrayText()
    {
        var block = Block(Stylesheet("_ai-tool-call.css"), "@media (forced-colors: active)");

        var rule = Regex.Match(block, @"\.tm-tool-call__retry\[aria-disabled=""true""\]\s*\{([^}]*)\}");

        rule.Success.Should().BeTrue(
            "forced colors paint the disabled Retry with CanvasText unless it gets a disabled-specific rule");
        rule.Groups[1].Value.Should().Contain("color: GrayText");
        rule.Groups[1].Value.Should().Contain("border-color: GrayText");

        var content = Regex.Match(block, @"\.tm-tool-call__retry\[aria-disabled=""true""\] > \*\s*\{([^}]*)\}");

        content.Success.Should().BeTrue(
            "the resting dim lives on the content children, so the forced-colors override must reach " +
            "the same children — a dim left in place would lower the contrast GrayText guarantees");
        content.Groups[1].Value.Should().Contain("opacity: 1",
            "opacity over a system colour lowers its guaranteed contrast — the disabled state is GrayText " +
            "alone, so the resting dim must be dropped in forced colors");
    }

    /// <summary>
    /// The card root carries <c>tabindex="-1"</c> as the programmatic focus fallback of the
    /// retry-completion hand-off (WCAG 2.4.3): focusable from script, never a tab stop. The fallback
    /// adds no tab stop and no component-specific focus style — the card CSS has no root focus rule (the
    /// global <c>:focus-visible</c> ring from base.css may still show after a keyboard hand-off, which is
    /// fine); only the action buttons and the payload boxes own a component <c>:focus-visible</c> rule.
    /// </summary>
    [Fact]
    public void TheCardRoot_IsAProgrammaticFocusFallback_WithoutAFocusRingOfItsOwn()
    {
        var css = Stylesheet("_ai-tool-call.css");

        css.Should().NotMatchRegex(@"\.tm-tool-call:focus",
            "the card root never paints its own focus ring — the retry-completion fallback focus must " +
            "stay invisible; only the buttons and the payload boxes own :focus-visible rules");
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
