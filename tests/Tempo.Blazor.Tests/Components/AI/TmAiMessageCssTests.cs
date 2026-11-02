using System.Text.RegularExpressions;
using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// Hygiene gate for the <c>TmAiMessage</c> stylesheet (plan 7f21f26b, Fáze 3): every
/// <c>tm-ai-message*</c> class the markup emits carries a rule (a class without one ships unstyled), the
/// stylesheet keeps the accessibility blocks the component promises (44 px coarse-pointer floor, forced
/// colors) and it declares no animation of its own — the streaming caret is the package-level primitive
/// with its own reduced-motion block, and a second animated subject here would escape that sweep.
/// </summary>
/// <remarks>
/// The token rules themselves (no colour literal, no raw palette step, no <c>[data-theme]</c> block, every
/// <c>var(--tm-*)</c> resolvable) are already enforced for the whole package by
/// <c>AiMarkdownViewCssTests</c>, which reads every file under the package's <c>wwwroot/css</c>.
/// </remarks>
public sealed class TmAiMessageCssTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static readonly Regex Comment = new(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline, RegexTimeout);

    private static readonly Regex CssClassSelector = new(@"\.(tm-ai-message[\w-]*)", RegexOptions.Compiled, RegexTimeout);

    /// <summary>
    /// Every <c>tm-ai-message*</c> token in the markup, not only the simple <c>class</c> attributes: the
    /// streaming modifier and the avatar slot build their class from an expression, and those names must
    /// be styled just the same. A trailing <c>-</c> is the interpolated role/status modifier
    /// (<c>tm-ai-message--@RoleModifier</c>), whose concrete names live in C#.
    /// </summary>
    private static readonly Regex ClassToken = new(@"tm-ai-message[\w-]*", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex RazorComment = new(@"@\*.*?\*@", RegexOptions.Compiled | RegexOptions.Singleline, RegexTimeout);

    private static string Root => ThemeCss.RepositoryRoot().FullName;

    private static string RazorMarkup()
        => RazorComment.Replace(
            File.ReadAllText(Path.Combine(Root, "src", "Tempo.Blazor.AI", "Components", "AI", "TmAiMessage.razor")),
            string.Empty);

    private static string Stylesheet()
        => Comment.Replace(
            File.ReadAllText(Path.Combine(Root, "src", "Tempo.Blazor.AI", "wwwroot", "css", "components", "_ai-message.css")),
            " ");

    [Fact]
    public void EveryClassTheMarkupEmits_HasARule()
    {
        var emitted = ClassToken.Matches(RazorMarkup())
            .Select(match => match.Value.TrimEnd('-'))
            .Where(name => name == "tm-ai-message"
                || name.StartsWith("tm-ai-message__", StringComparison.Ordinal)
                || name.StartsWith("tm-ai-message--", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        var styled = CssClassSelector.Matches(Stylesheet())
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        emitted.Should().NotBeEmpty("the component emits its own BEM classes");
        emitted.Should().BeSubsetOf(styled,
            "a class the message markup emits without a rule ships unstyled. Missing: {0}",
            string.Join(", ", emitted.Except(styled)));
    }

    [Fact]
    public void TheStylesheetKeepsTheCoarsePointerTouchFloor()
    {
        var css = Stylesheet();

        css.Should().Contain("@media (pointer: coarse)", "a coarse pointer must get the 44px floor");
        css.Should().Contain("--tm-touch-target");
        foreach (var selector in new[]
                 {
                     ".tm-ai-message__action", ".tm-ai-message__version-button", ".tm-ai-message__preview-close",
                     ".tm-ai-message__follow-up", ".tm-ai-message__retry",
                 })
        {
            css.Should().Contain(selector, $"{selector} is a control a thumb has to reach");
        }
    }

    [Fact]
    public void TheStylesheetKeepsTheForcedColorsBlock()
    {
        Stylesheet().Should().Contain("@media (forced-colors: active)")
            .And.Contain("CanvasText", "the surfaces and the user bubble must survive Windows high contrast");
    }

    /// <summary>
    /// <c>forced-color-adjust: none</c> opts an element out of the system palette for EVERY colour — the focus
    /// ring included, and the property INHERITS. Four focusable controls of this component therefore sit
    /// inside an opted-out surface: the two version buttons (inside <c>__versions</c>), the follow-up chip
    /// (itself opted out), Retry (inside <c>__error</c>) and the source card's link (inside <c>__source</c>).
    /// Without a Highlight rule each of them keeps painting the theme token ring on a Canvas surface, which is
    /// the defect <c>_markdown-view.css</c>, <c>_ai-tool-call.css</c> and <c>_ai-thinking.css</c> already avoid
    /// for their own controls.
    /// </summary>
    [Fact]
    public void ForcedColors_RestoreTheSystemFocusRingInsideTheOptedOutSurfaces()
    {
        var ringed = SystemRingSelectors();

        foreach (var selector in new[]
                 {
                     ".tm-ai-message__version-button:focus-visible",
                     ".tm-ai-message__follow-up:focus-visible",
                     ".tm-ai-message__retry:focus-visible",
                     ".tm-ai-message__source-title a:focus-visible",
                 })
        {
            ringed.Should().Contain(selector,
                "the control inherits forced-color-adjust: none from its surface, so its ring needs the " +
                "system Highlight colour to stay visible in Windows high contrast");
        }
    }

    /// <summary>
    /// The opt-out is inherited, so opting the USER BUBBLE out would also strip its markdown (paragraphs,
    /// links, headings) of the system remapping: those keep the theme's own text colour on a Canvas surface,
    /// which in a dark high-contrast scheme is dark text on black. The bubble needs no opt-out at all — under
    /// forced colours its author background is remapped to Canvas anyway, which is exactly what the rule asks
    /// for — so the opt-out belongs on the surfaces that carry their own painted chrome, never on the body
    /// that carries the reader's text.
    /// </summary>
    [Fact]
    public void ForcedColors_TheUserBubbleDoesNotOptItsContentOutOfTheSystemPalette()
    {
        var optedOut = OptedOutSelectors();

        optedOut.Should().NotBeEmpty("the stylesheet keeps its forced-colors opt-outs");
        optedOut.Should().NotContain(selector => selector.Contains("tm-ai-message__body", StringComparison.Ordinal),
            "the user bubble carries the turn's markdown; opting it out takes the system palette away from " +
            "the reader's own text");
    }

    /// <summary>Every selector of the forced-colors block that restores the system <c>Highlight</c> ring.</summary>
    private static List<string> SystemRingSelectors()
        => Regex.Matches(ForcedColorsBlock(), @"([^{}]*:focus-visible[^{}]*)\{([^}]*)\}")
            .Where(match => ThemeCss.Normalise(match.Groups[2].Value)
                .Contains("outline-color: Highlight", StringComparison.Ordinal))
            .SelectMany(match => ThemeCss.SelectorParts(match.Groups[1].Value))
            .ToList();

    /// <summary>Every selector of the forced-colors block inside the rule that sets <c>forced-color-adjust: none</c>.</summary>
    private static List<string> OptedOutSelectors()
        => Regex.Matches(ForcedColorsBlock(), @"([^{}]+)\{([^}]*)\}")
            .Where(match => ThemeCss.Normalise(match.Groups[2].Value)
                .Contains("forced-color-adjust: none", StringComparison.Ordinal))
            .SelectMany(match => ThemeCss.SelectorParts(match.Groups[1].Value))
            .ToList();

    /// <summary>
    /// The body of the <c>@media (forced-colors: active)</c> block, matched by brace depth so a nested rule
    /// cannot truncate it (the stylesheet is already comment-stripped by <see cref="Stylesheet"/>).
    /// </summary>
    private static string ForcedColorsBlock()
    {
        const string marker = "@media (forced-colors: active)";
        var css = Stylesheet();
        var start = css.IndexOf(marker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"the stylesheet must keep its {marker} block");

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

    [Fact]
    public void TheStylesheetDeclaresNoAnimationOfItsOwn()
    {
        var css = Stylesheet();

        css.Should().NotContain("animation:", "the streaming caret owns the package's only motion (and its reduce block)");
        css.Should().NotContain("@keyframes");
        css.Should().NotContain("transition:", "nothing in this component animates, so no reduce block is needed here");
    }
}
