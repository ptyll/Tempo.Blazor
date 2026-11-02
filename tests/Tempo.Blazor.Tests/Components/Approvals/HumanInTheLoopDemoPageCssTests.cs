using System.Text.RegularExpressions;
using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Components.Approvals;

/// <summary>
/// Guards for the demo-only stylesheet inside <c>HumanInTheLoopPage.razor</c> (plan 10b3966f, phase 6). The
/// four decision surfaces are styled by their components; the page only frames them, and these rules decide
/// whether the frame follows the design tokens and stays usable in every mode the browser can force on it.
/// </summary>
/// <remarks>
/// The page is a Razor file, so the core CSS sweeps (which read <c>src/Tempo.Blazor/wwwroot/css</c>) never see
/// it. The rules are read from the file instead of asserted through the DOM, because bUnit applies no CSS at
/// all: a raw palette step or a focus ring that High Contrast strips is invisible to a markup assertion.
/// </remarks>
public sealed class HumanInTheLoopDemoPageCssTests
{
    /// <summary>
    /// The page must follow the semantic tokens, never a raw palette step: the demo shell offers a light and a
    /// dark theme, and a literal colour stays the same in both (and ignores a host that rebinds a token).
    /// </summary>
    [Fact]
    public void PageStyle_UsesSemanticTokensAndNoRawColour()
    {
        var style = PageStyle();

        var rawColours = Regex.Matches(style, @"#[0-9a-fA-F]{3,8}\b|\brgba?\s*\(|\bhsla?\s*\(")
            .Select(match => match.Value)
            .Distinct()
            .ToList();

        rawColours.Should().BeEmpty(
            "the demo frame must read --tm-* tokens (light and dark values come from the token tables), "
            + "but the page's <style> block carries: " + string.Join(", ", rawColours));

        ThemeCss.RuleDeclarations(style, ".hitl-demo-button").Should()
            .Contain("min-block-size: var(--tm-touch-target)",
                "a coarse pointer needs a 44px target on the demo's own buttons too");

        ThemeCss.RuleDeclarations(style, ".hitl-demo-result").Should()
            .Contain("color: var(--tm-text-primary)");
    }

    /// <summary>
    /// WCAG 2.4.7 in forced-colors mode: Windows High Contrast drops box shadows, so a focus indicator that
    /// exists only as <c>box-shadow: var(--tm-focus-ring)</c> disappears there. The outline is the fallback —
    /// transparent in a normal theme (the box shadow is the visible ring) and painted in High Contrast.
    /// </summary>
    [Fact]
    public void DemoButton_KeepsAVisibleFocusIndicator_InForcedColorsMode()
    {
        var style = PageStyle();
        var focus = ThemeCss.RuleDeclarations(style, ".hitl-demo-button:focus-visible");

        focus.Should().Contain("box-shadow: var(--tm-focus-ring)",
            "the normal theme shows the shared focus ring");
        focus.Should().Contain("outline: 2px solid transparent",
            "forced colors strips the box shadow, so the ring needs an outline that High Contrast can repaint");
        focus.Should().Contain("outline-offset: 2px",
            "a repainted outline needs the same 2px gap the box shadow clears");

        ThemeCss.RuleDeclarations(ForcedColorsBlock(style), ".hitl-demo-button:focus-visible").Should()
            .Contain("outline-color: Highlight",
                "in High Contrast the transparent outline has to become the system's focus colour");
    }

    /// <summary>
    /// The demo must not animate anything of its own: the components already honour
    /// <c>prefers-reduced-motion</c> in the shipped bundle, and a second animated subject in a page stylesheet
    /// would need its own reduce block that no core sweep can see.
    /// </summary>
    [Fact]
    public void PageStyle_DeclaresNoAnimationWithoutAReduceBlock()
    {
        var style = PageStyle();

        style.Should().NotContain("animation:", "the page frames the components, it does not animate them");
        style.Should().NotContain("transition:", "the page frames the components, it does not animate them");
    }

    private static string PageStyle()
    {
        var path = Path.Combine(
            ThemeCss.RepositoryRoot().FullName,
            "src",
            "Tempo.Blazor.Demo.SharedUI",
            "Pages",
            "HumanInTheLoopPage.razor");

        File.Exists(path).Should().BeTrue("the /human-in-the-loop demo page carries its own frame styles");

        var razor = File.ReadAllText(path);
        var start = razor.IndexOf("<style>", StringComparison.Ordinal);
        var end = razor.IndexOf("</style>", StringComparison.Ordinal);

        start.Should().BeGreaterThanOrEqualTo(0, "the page frames the demo with its own <style> block");
        end.Should().BeGreaterThan(start, "the page's <style> block must be closed");

        return razor[(start + "<style>".Length)..end];
    }

    /// <summary>The body of the page's forced-colors block (an outline fallback that only High Contrast sees).</summary>
    private static string ForcedColorsBlock(string style)
    {
        // Razor needs the double '@' for a CSS at-rule inside a <style> block; the browser receives one.
        var media = style.IndexOf("@@media (forced-colors: active)", StringComparison.Ordinal);
        media.Should().BeGreaterThanOrEqualTo(0,
            "the focus fallback has to live in a forced-colors block, otherwise the transparent outline is all "
            + "a normal theme gets");

        var open = style.IndexOf('{', media);
        open.Should().BeGreaterThan(media);

        var depth = 0;
        for (var index = open; index < style.Length; index++)
        {
            if (style[index] == '{')
            {
                depth++;
            }
            else if (style[index] == '}' && --depth == 0)
            {
                return style[(open + 1)..index];
            }
        }

        throw new InvalidOperationException("Unbalanced braces in the page's forced-colors block.");
    }
}
