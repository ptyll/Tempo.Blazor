using System.Text.RegularExpressions;
using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// Hygiene gate for the conversation stylesheet (<c>_ai-conversation.css</c>, plan 7f21f26b phase 5): every
/// class the markup emits carries a rule, the scroll container and the two floating pills stay reachable on a
/// coarse pointer, the forced-colors block survives — including a counterpart for every <c>:hover</c> rule,
/// which would otherwise outrank it — and the file declares no motion of its own — the transcript does not
/// animate, so it needs no reduced-motion block (the shared streaming caret in
/// <c>_ai-streaming.css</c> does).
/// </summary>
/// <remarks>
/// The package-wide token rules (no colour literal, no raw palette step, no <c>[data-theme]</c> block, every
/// <c>var(--tm-*)</c> resolvable) are enforced for every stylesheet of the package by
/// <c>AiMarkdownViewCssTests</c>.
/// </remarks>
public sealed class TmAiConversationCssTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static readonly Regex Comment = new(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline, RegexTimeout);

    private static readonly Regex RazorComment = new(@"@\*.*?\*@", RegexOptions.Compiled | RegexOptions.Singleline, RegexTimeout);

    private static readonly Regex CssClassSelector = new(@"\.(tm-[a-zA-Z][\w-]*)", RegexOptions.Compiled, RegexTimeout);

    private static string Root => ThemeCss.RepositoryRoot().FullName;

    private static string Stylesheet()
        => Comment.Replace(
            File.ReadAllText(Path.Combine(Root, "src", "Tempo.Blazor.AI", "wwwroot", "css", "components", "_ai-conversation.css")),
            " ");

    private static string Markup()
        => RazorComment.Replace(
            File.ReadAllText(Path.Combine(Root, "src", "Tempo.Blazor.AI", "Components", "AI", "TmAiConversation.razor"))
            + File.ReadAllText(Path.Combine(Root, "src", "Tempo.Blazor.AI", "Components", "AI", "TmAiConversation.razor.cs")),
            string.Empty);

    /// <summary>
    /// A class token emitted by the markup's <c>class</c> attributes (or by a <c>"tm-…"</c> literal in the
    /// code-behind), with the interpolated modifier suffix trimmed.
    /// </summary>
    private static HashSet<string> Emitted()
    {
        var fromAttributes = Regex.Matches(Markup(), @"class\s*=\s*[""']([^""']*)[""']")
            .SelectMany(match => match.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Select(token => token.Split('@', '{')[0].TrimEnd('-'));

        var fromLiterals = Regex.Matches(Markup(), @"""([^""]*tm-[^""]*)""")
            .SelectMany(match => match.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Select(token => token.Split('@', '{')[0].TrimEnd('-'));

        return fromAttributes
            .Concat(fromLiterals)
            .Where(name => name.StartsWith("tm-ai-conversation", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
    }

    [Fact]
    public void EveryConversationClassTheMarkupEmits_HasARule()
    {
        var emitted = Emitted();
        var styled = CssClassSelector.Matches(Stylesheet())
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        emitted.Should().NotBeEmpty("the conversation emits its own BEM classes");
        emitted.Should().BeSubsetOf(styled,
            "a class the conversation emits without a rule ships unstyled. Missing: {0}",
            string.Join(", ", emitted.Except(styled)));
    }

    [Fact]
    public void TheStylesheetIsImportedByThePackageEntryPoint()
    {
        var entry = File.ReadAllText(Path.Combine(Root, "src", "Tempo.Blazor.AI", "wwwroot", "css", "tempo-blazor-ai.css"));

        entry.Should().Contain("@import \"components/_ai-conversation.css\"",
            "a host loads exactly one stylesheet, so an unimported partial never reaches the page");
    }

    [Fact]
    public void TheScrollContainer_KeepsItsOwnScrollAndFocusTreatment()
    {
        var css = Stylesheet();

        var log = Regex.Match(css, @"\.tm-ai-conversation__log\s*\{([^}]*)\}").Groups[1].Value;
        log.Should().NotBeNullOrWhiteSpace("the transcript is the scroll container");
        log.Should().Contain("overflow-y: auto");
        log.Should().Contain("min-block-size: 0", "a flex child without a zero floor cannot shrink to scroll");

        css.Should().Contain(".tm-ai-conversation__log:focus-visible",
            "the transcript is keyboard focusable (WCAG 2.1), so it needs a visible focus indicator");
    }

    [Fact]
    public void TheActionablePills_ReachTheTouchFloorOnACoarsePointer()
    {
        var css = Stylesheet();

        css.Should().Contain("@media (pointer: coarse)");
        foreach (var selector in new[]
                 {
                     ".tm-ai-conversation__load-older",
                     ".tm-ai-conversation__new-messages",
                     ".tm-ai-conversation__stop",
                     ".tm-ai-conversation__suggestion",
                 })
        {
            css.Should().Contain(selector, $"{selector} is a control a thumb has to reach");
        }

        var coarse = Regex.Match(css, @"@media \(pointer: coarse\)\s*\{(?<body>.*)", RegexOptions.Singleline).Groups["body"].Value;
        coarse.Should().Contain("--tm-touch-target");
        coarse.Should().Contain("min-block-size: var(--tm-touch-target)");
    }

    [Fact]
    public void TheStylesheetSurvivesForcedColors()
    {
        var css = Stylesheet();

        css.Should().Contain("@media (forced-colors: active)");
        css.Should().Contain("outline-color: Highlight",
            "forced-color-adjust: none opts the focus ring out of the system palette, so the ring must ride Highlight");
        css.Should().NotMatch(@"(?<![-\w])color:\s*Highlight\s*[;}]",
            "a Highlight-coloured label over Canvas has no contrast guarantee; a Highlight BACKGROUND takes HighlightText");
        css.Should().Contain("color: HighlightText",
            "the Stop pill keeps a Highlight background under forced colors, so its ink must be the HighlightText half of the pair");
    }

    /// <summary>
    /// A `:hover` rule is a class plus a pseudo-class, so it outranks the forced-colors block's plain class
    /// selectors and wins there too — under the pointer the theme tokens would come back over the system
    /// palette, and nothing would guarantee the resulting ink/background pair is readable. Every hover rule
    /// therefore needs a counterpart INSIDE the forced-colors block.
    /// </summary>
    [Fact]
    public void EveryHoverRule_HasAForcedColorsCounterpart()
    {
        var css = Stylesheet();
        var forced = ForcedColorsBlock(css);
        var hoverRules = HoverSelectors(OutsideForcedColors(css));

        hoverRules.Should().NotBeEmpty("the conversation gives its controls a pointer affordance");

        foreach (var selector in hoverRules)
        {
            forced.Should().Contain(selector,
                "{0} would repaint with the theme tokens in high-contrast mode, so it needs a forced-colors counterpart",
                selector);
        }
    }

    /// <summary>The body of the <c>@media (forced-colors: active)</c> block.</summary>
    private static string ForcedColorsBlock(string css)
    {
        var start = css.IndexOf("@media (forced-colors: active)", StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, "the stylesheet opts out of the automatic adjustment");

        var open = css.IndexOf('{', start);
        var depth = 0;
        for (var index = open; index < css.Length; index++)
        {
            switch (css[index])
            {
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        return css[(open + 1)..index];
                    }

                    break;
            }
        }

        throw new InvalidOperationException("The forced-colors block is not closed.");
    }

    /// <summary>Everything the stylesheet declares outside the forced-colors block.</summary>
    private static string OutsideForcedColors(string css)
    {
        var start = css.IndexOf("@media (forced-colors: active)", StringComparison.Ordinal);
        if (start < 0)
        {
            return css;
        }

        var block = ForcedColorsBlock(css);
        var blockStart = css.IndexOf(block, start, StringComparison.Ordinal);
        return css[..Math.Max(0, blockStart - 1)] + css[(blockStart + block.Length + 1)..];
    }

    private static List<string> HoverSelectors(string css)
        => Regex.Matches(css, @"(?<selectors>[^{}]*(?::hover)[^{}]*)\{", RegexOptions.None, RegexTimeout)
            .SelectMany(match => match.Groups["selectors"].Value.Split(','))
            .Select(selector => selector.Trim())
            .Where(selector => selector.Contains(":hover", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// The conversation itself does not move: the streaming caret and the thinking shimmer are owned by
    /// <c>_ai-streaming.css</c> / <c>_ai-thinking.css</c>, so this file declaring an animation would be a
    /// reduced-motion block waiting to be forgotten.
    /// </summary>
    [Fact]
    public void TheConversationStylesheetDeclaresNoMotion()
    {
        var css = Stylesheet();

        css.Should().NotContain("animation:");
        css.Should().NotContain("@keyframes");
        css.Should().NotContain("transition:");
    }
}
