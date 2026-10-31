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

    [Fact]
    public void TheStylesheetDeclaresNoAnimationOfItsOwn()
    {
        var css = Stylesheet();

        css.Should().NotContain("animation:", "the streaming caret owns the package's only motion (and its reduce block)");
        css.Should().NotContain("@keyframes");
        css.Should().NotContain("transition:", "nothing in this component animates, so no reduce block is needed here");
    }
}
