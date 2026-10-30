using System.Text.RegularExpressions;
using FluentAssertions;
using Tempo.Blazor.Tests.Theme;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Chat;

/// <summary>
/// CSS contract of the TmChatBubble phase (FZE-3) across the three shipped stylesheets: the
/// scoped <c>TmChatBubble.razor.css</c>, the scoped <c>TmChat.razor.css</c> and the global
/// fallback <c>wwwroot/css/components/_chat.css</c>.
/// <para>
/// Three rules are frozen here after review:
/// (1) the <c>--tm-chat-bubble-*</c> knobs are NEVER DECLARED by the library — not on
/// <c>.tm-chat</c>, not on <c>:root</c> — because a declaration would shadow a host override on
/// a wrapper or on <c>:root</c>; every <c>var()</c> carries its default as the FALLBACK instead.
/// (2) A bubble aligns itself in its row: <c>align-self</c> per variant, <c>width: fit-content</c>,
/// <c>min-width: 0</c> and the 80 % width cap on the base rule, mirrored on the built-in
/// <c>.tm-chat__message</c> row. (3) There is no <c>[data-theme="dark"]</c> / <c>.tm-dark</c>
/// block — the strict CSS audit forbids new ones, and every fallback is a semantic token that
/// already flips in <c>tokens-dark.css</c>.
/// </para>
/// </summary>
public sealed class TmChatBubbleCssTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static readonly Regex RuleBlock =
        new(@"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}", RegexOptions.Compiled, Timeout);

    private static readonly Regex KnobDeclaration =
        new(@"--tm-chat-bubble-[a-z0-9-]+\s*:", RegexOptions.Compiled, Timeout);

    private static readonly Regex ManualDarkBlock =
        new(@"\[data-theme\s*=\s*[""']dark[""']\]|\.tm-dark\b", RegexOptions.Compiled, Timeout);

    /// <summary>Every (variant → modifier class) pair whose surface is contrast-checked.</summary>
    private static readonly (string Name, string Modifier)[] Variants =
    [
        ("Incoming", "incoming"),
        ("Outgoing", "outgoing"),
        ("System", "system"),
        ("Pill", "pill"),
        ("Pending", "pending"),
        ("Queued", "queued"),
        ("Discarded", "discarded"),
        ("Locked", "locked"),
    ];

    private static string RepoFile(params string[] parts) =>
        Path.Combine(new[] { ThemeCss.RepositoryRoot().FullName }.Concat(parts).ToArray());

    /// <summary>The scoped component stylesheet (comments stripped).</summary>
    private static string Scoped(string file) =>
        ThemeCss.StripComments(File.ReadAllText(
            RepoFile("src", "Tempo.Blazor", "Components", "Chat", file)));

    private static string ChatRazorCss() => Scoped("TmChat.razor.css");
    private static string BubbleRazorCss() => Scoped("TmChatBubble.razor.css");
    private static string FallbackCss() => ThemeCss.ComponentCss("_chat.css");
    private static string BundledCss() => ThemeCss.BundledCss();

    /// <summary>
    /// The value of <paramref name="property"/> in the first rule that lists
    /// <paramref name="selector"/> as one of its comma-separated parts (exact match), or null.
    /// </summary>
    private static string? Prop(string css, string selector, string property)
    {
        foreach (Match rule in RuleBlock.Matches(css))
        {
            var parts = ThemeCss.Normalise(rule.Groups["selector"].Value)
                .Split(',').Select(part => part.Trim());
            if (!parts.Contains(selector, StringComparer.Ordinal))
            {
                continue;
            }

            foreach (var declaration in rule.Groups["body"].Value.Split(';'))
            {
                var separator = declaration.IndexOf(':');
                if (separator < 0)
                {
                    continue;
                }

                if (string.Equals(declaration[..separator].Trim(), property, StringComparison.Ordinal))
                {
                    return ThemeCss.Normalise(declaration[(separator + 1)..]);
                }
            }
        }

        return null;
    }

    // ── Rule 1: the library declares no knob defaults ────────────────────────

    [Theory]
    [InlineData("TmChat.razor.css")]
    [InlineData("TmChatBubble.razor.css")]
    public void ScopedStylesheets_DeclareNoBubbleKnobDefaults(string file)
    {
        var declarations = KnobDeclaration.Matches(Scoped(file)).Select(match => match.Value).ToList();

        declarations.Should().BeEmpty(
            "{0} must only READ --tm-chat-bubble-* knobs (inside var()) — a declaration would " +
            "shadow a host override on a wrapper or :root; the defaults live in the var() fallbacks",
            file);
    }

    [Fact]
    public void GlobalFallback_DeclaresNoBubbleKnobDefaults()
    {
        var declarations = KnobDeclaration.Matches(FallbackCss()).Select(match => match.Value).ToList();

        declarations.Should().BeEmpty(
            "_chat.css mirrors TmChat.razor.css and must not declare --tm-chat-bubble-* defaults either");
    }

    [Fact]
    public void BundledCss_DeclaresNoBubbleKnobDefaults()
    {
        var declarations = KnobDeclaration.Matches(BundledCss()).Select(match => match.Value).ToList();

        declarations.Should().BeEmpty("the shipped bundle must not declare bubble knob defaults");
    }

    // ── Rule 2: alignment contract ───────────────────────────────────────────

    [Theory]
    [InlineData("TmChatBubble.razor.css")]
    [InlineData("_chat.css")]
    public void BubbleBase_AlignsToRowStartAndShrinksToContent(string file)
    {
        var css = file.EndsWith(".razor.css", StringComparison.Ordinal) ? BubbleRazorCss() : FallbackCss();

        Prop(css, ".tm-chat-bubble", "align-self").Should().Be("flex-start");
        Prop(css, ".tm-chat-bubble", "width").Should().Be("fit-content");
        Prop(css, ".tm-chat-bubble", "min-width").Should().Be("0");
        Prop(css, ".tm-chat-bubble", "max-width").Should().Be("var(--tm-chat-bubble-max-width, 80%)");
    }

    [Theory]
    [InlineData("TmChatBubble.razor.css")]
    [InlineData("_chat.css")]
    public void ReservedVariants_AlignToRowEnd(string file)
    {
        var css = file.EndsWith(".razor.css", StringComparison.Ordinal) ? BubbleRazorCss() : FallbackCss();

        foreach (var modifier in new[] { "outgoing", "pending", "queued", "discarded" })
        {
            Prop(css, $".tm-chat-bubble--{modifier}", "align-self").Should().Be(
                "flex-end", "the {0} bubble hugs the end of the row", modifier);
        }
    }

    [Theory]
    [InlineData("TmChatBubble.razor.css")]
    [InlineData("_chat.css")]
    public void SystemVariant_CentresAndCentresItsText(string file)
    {
        var css = file.EndsWith(".razor.css", StringComparison.Ordinal) ? BubbleRazorCss() : FallbackCss();

        Prop(css, ".tm-chat-bubble--system", "align-self").Should().Be("center");
        Prop(css, ".tm-chat-bubble--system", "text-align").Should().Be("center");
    }

    [Theory]
    [InlineData("TmChatBubble.razor.css")]
    [InlineData("_chat.css")]
    public void PillVariant_CentresWrapsAndCapsAtFullWidth(string file)
    {
        var css = file.EndsWith(".razor.css", StringComparison.Ordinal) ? BubbleRazorCss() : FallbackCss();

        Prop(css, ".tm-chat-bubble--pill", "align-self").Should().Be("center");
        Prop(css, ".tm-chat-bubble--pill", "max-width").Should().Be("100%");
        Prop(css, ".tm-chat-bubble--pill", "flex-direction").Should().Be("row");
        Prop(css, ".tm-chat-bubble--pill", "flex-wrap").Should().Be("wrap");
    }

    [Theory]
    [InlineData("TmChat.razor.css")]
    [InlineData("_chat.css")]
    public void BuiltInRow_OwnsTheWidthCapAndTheKnobCanOnlyTighten(string file)
    {
        var css = file.EndsWith(".razor.css", StringComparison.Ordinal) ? ChatRazorCss() : FallbackCss();

        Prop(css, ".tm-chat__message", "max-width").Should().Be(
            "var(--tm-chat-bubble-max-width, 80%)",
            "the row carries the cap; the unset knob falls back to the same 80% the row had");
        Prop(css, ".tm-chat__bubble", "max-width").Should().BeNull(
            "the bubble must not carry a second cap — the row already caps the conversation");
    }

    // ── Rule 3: the built-in bubble reads the knobs ──────────────────────────

    [Theory]
    [InlineData("TmChat.razor.css")]
    [InlineData("_chat.css")]
    public void BuiltInBubble_PaintsTheVariantBorderAsAnInsetRing(string file)
    {
        var css = file.EndsWith(".razor.css", StringComparison.Ordinal) ? ChatRazorCss() : FallbackCss();

        Prop(css, ".tm-chat__bubble", "box-shadow").Should().Be(
            "inset 0 0 0 1px var(--tm-chat-bubble-in-border, transparent)");
        Prop(css, ".tm-chat__message--outgoing .tm-chat__bubble", "box-shadow").Should().Be(
            "inset 0 0 0 1px var(--tm-chat-bubble-out-border, transparent)");
    }

    [Theory]
    [InlineData("TmChat.razor.css")]
    [InlineData("_chat.css")]
    public void BuiltInSystemRow_ReadsTheSystemKnobs(string file)
    {
        var css = file.EndsWith(".razor.css", StringComparison.Ordinal) ? ChatRazorCss() : FallbackCss();

        Prop(css, ".tm-chat__message--system", "background").Should().Be(
            "var(--tm-chat-bubble-system-bg, var(--tm-bg-muted))");
        Prop(css, ".tm-chat__message--system", "color").Should().Be(
            "var(--tm-chat-bubble-system-fg, var(--tm-color-text-secondary))");
    }

    // ── Review defaults: padding compensation, pending border, discarded bg ──

    [Theory]
    [InlineData("TmChatBubble.razor.css")]
    [InlineData("_chat.css")]
    public void BubblePadding_IsCompensatedForTheAlwaysPaintedBorder(string file)
    {
        var css = file.EndsWith(".razor.css", StringComparison.Ordinal) ? BubbleRazorCss() : FallbackCss();

        Prop(css, ".tm-chat-bubble", "padding").Should().Be(
            "var(--tm-chat-bubble-padding, calc(var(--tm-space-2-5) - var(--tm-border-width)) calc(var(--tm-space-3-5) - var(--tm-border-width)))",
            "the 1px border is always painted (transparent by default), so the default padding gives the space back");
    }

    [Theory]
    [InlineData("TmChatBubble.razor.css")]
    [InlineData("_chat.css")]
    public void PendingBorder_DefaultsToTheStrongBorder(string file)
    {
        var css = file.EndsWith(".razor.css", StringComparison.Ordinal) ? BubbleRazorCss() : FallbackCss();

        Prop(css, ".tm-chat-bubble--pending", "border-color").Should().Be(
            "var(--tm-chat-bubble-pending-border, var(--tm-border-color-strong))",
            "a dashed --tm-border-color border disappears on a tinted surface; the strong step stays visible");
    }

    [Theory]
    [InlineData("TmChatBubble.razor.css")]
    [InlineData("_chat.css")]
    public void DiscardedBackground_DefaultsToTheMutedSurface(string file)
    {
        var css = file.EndsWith(".razor.css", StringComparison.Ordinal) ? BubbleRazorCss() : FallbackCss();

        Prop(css, ".tm-chat-bubble--discarded", "background").Should().Be(
            "var(--tm-chat-bubble-discarded-bg, var(--tm-bg-muted))",
            "--tm-bg-muted flips to a visible dark surface; --tm-bg-surface-secondary is the app background in dark mode");
    }

    [Theory]
    [InlineData("TmChatBubble.razor.css")]
    [InlineData("TmChat.razor.css")]
    [InlineData("_chat.css")]
    public void ChatStylesheets_WrapWithOverflowAnywhere(string file)
    {
        var css = file.EndsWith("_chat.css", StringComparison.Ordinal) ? FallbackCss()
            : file.EndsWith("TmChat.razor.css", StringComparison.Ordinal) ? ChatRazorCss()
            : BubbleRazorCss();

        var baseSelector = file == "TmChatBubble.razor.css" || file == "_chat.css"
            ? ".tm-chat-bubble"
            : ".tm-chat__text";

        Prop(css, baseSelector, "overflow-wrap").Should().Be(
            "anywhere", "word-break: break-word is the deprecated form");
        Prop(css, baseSelector, "word-break").Should().BeNull();
    }

    // ── No manual dark block in the NEW stylesheet ───────────────────────────

    [Fact]
    public void TmChatBubbleCss_HasNoManualDarkBlock()
    {
        ManualDarkBlock.IsMatch(BubbleRazorCss()).Should().BeFalse(
            "TmChatBubble.razor.css resolves dark mode through the semantic-token fallbacks at the " +
            "element — a [data-theme=dark] block is forbidden by the strict CSS audit. (TmChat.razor.css " +
            "and _chat.css keep their grandfathered attachment/reaction overrides.)");
    }

    // ── Per-variant text contrast, both themes ───────────────────────────────

    private static readonly Regex RelativeColour =
        new(@"rgb\(from\s+(?<expr>var\(--tm-[\w-]+\))[^)]*/\s*(?<alpha>[0-9.]+)\s*\)",
            RegexOptions.Compiled, Timeout);

    private static readonly Regex RgbaLiteral =
        new(@"^rgba?\(\s*(?<r>\d+)\s*,\s*(?<g>\d+)\s*,\s*(?<b>\d+)\s*,\s*(?<alpha>[0-9.]+)\s*\)$",
            RegexOptions.Compiled, Timeout);

    /// <summary>
    /// The colour and alpha a declared surface value carries after resolving the var() chain. The
    /// dark theme paints the Queued/Locked washes translucent — either a relative colour
    /// (<c>rgb(from --tm-x r g b / 0.15)</c>) or an <c>rgba(…, 0.15)</c> literal — and
    /// <see cref="ThemeCss.ResolveColour"/> deliberately drops the alpha, so the chain is walked
    /// here one step at a time until the literal form is visible.
    /// </summary>
    private static (string Colour, double Alpha) ResolveWithAlpha(string declared, bool dark)
    {
        var tokens = ThemeCss.TokenGraph(dark);
        var value = declared.Trim();

        for (var depth = 0; depth < 16 && value.StartsWith("var(", StringComparison.Ordinal); depth++)
        {
            var (name, fallback) = ThemeCss.SplitOnTopLevelComma(value[4..^1]);
            name = name.Trim();
            if (!tokens.TryGetValue(name, out var referenced))
            {
                referenced = fallback ?? throw new InvalidOperationException(
                    $"token {name} must be declared (or carry a fallback)");
            }

            value = referenced.Trim();
        }

        var relative = RelativeColour.Match(value);
        if (relative.Success)
        {
            return (ThemeCss.ResolveColour(relative.Groups["expr"].Value, tokens),
                double.Parse(relative.Groups["alpha"].Value, System.Globalization.CultureInfo.InvariantCulture));
        }

        var literal = RgbaLiteral.Match(value);
        if (literal.Success)
        {
            return ($"#{int.Parse(literal.Groups["r"].Value):x2}{int.Parse(literal.Groups["g"].Value):x2}{int.Parse(literal.Groups["b"].Value):x2}",
                double.Parse(literal.Groups["alpha"].Value, System.Globalization.CultureInfo.InvariantCulture));
        }

        return (ThemeCss.ResolveColour(value, tokens), 1.0);
    }

    /// <summary>
    /// The opaque colour a viewer sees for a declared surface value: a translucent wash is
    /// composited over the chat surface (<c>--tm-bg-surface</c>) — the same one method the suite
    /// composites with.
    /// </summary>
    private static string Effective(string declared, bool dark)
    {
        var (colour, alpha) = ResolveWithAlpha(declared, dark);
        return alpha >= 1.0
            ? colour
            : ThemeCss.Composite(colour, alpha, ThemeCss.ResolveColour("var(--tm-bg-surface)", ThemeCss.TokenGraph(dark)));
    }

    /// <summary>
    /// The frozen contrast record of the phase: every variant's declared foreground over its
    /// declared background, resolved through the token graph of each theme. The knobs are unset
    /// in the library, so the var() FALLBACKS are what renders — exactly what this measures.
    /// All eight variants must clear WCAG AA (4.5:1) in BOTH themes without a single
    /// <c>[data-theme=dark]</c> override (forbidden by the strict audit).
    /// </summary>
    [Theory]
    [InlineData("Incoming")]
    [InlineData("Outgoing")]
    [InlineData("System")]
    [InlineData("Pill")]
    [InlineData("Pending")]
    [InlineData("Queued")]
    [InlineData("Discarded")]
    [InlineData("Locked")]
    public void EveryVariant_ClearsWcagAaInBothThemes(string name)
    {
        var css = BubbleRazorCss();
        var modifier = Variants.Single(variant => variant.Name == name).Modifier;
        var selector = $".tm-chat-bubble--{modifier}";
        var foreground = Prop(css, selector, "color")
            ?? throw new InvalidOperationException($"{selector} declares no colour.");
        var background = Prop(css, selector, "background")
            ?? throw new InvalidOperationException($"{selector} declares no background.");

        ThemeCss.Ratio(foreground, Effective(background, dark: false), dark: false)
            .Should().BeGreaterThanOrEqualTo(4.5,
                "{0} light: {1} on {2} must clear WCAG AA", name, foreground, background);
        ThemeCss.Ratio(foreground, Effective(background, dark: true), dark: true)
            .Should().BeGreaterThanOrEqualTo(4.5,
                "{0} dark: {1} on {2} must clear WCAG AA", name, foreground, background);
    }
}
