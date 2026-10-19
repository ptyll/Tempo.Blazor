using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Guards the theme-invariant "paper" tokens (<c>--tm-paper*</c>, spec-D U14, decision X10):
/// content that represents a printed/exported page (diagram pages today) stays paper-white with dark
/// ink in EVERY theme. Review fix: the five tokens are LITERAL light values declared only in
/// <c>tokens.css</c> — no <c>var()</c> alias (a substituted alias would capture whichever theme was
/// active where the host happened to declare an override) and NO re-declaration in
/// <c>tokens-dark.css</c> (nothing there is what keeps the paper white under a descendant-scoped
/// dark theme) — so the guard asserts presence + literal values in the light file and absence from
/// the dark file, plus a resolvable, high-contrast ink in both themes.
/// </summary>
public class PaperTokenTests
{
    [Theory]
    [InlineData("--tm-paper")]
    [InlineData("--tm-paper-ink")]
    [InlineData("--tm-paper-ink-muted")]
    [InlineData("--tm-paper-border")]
    [InlineData("--tm-paper-backdrop")]
    public void PaperToken_IsLiteralLightValue_DeclaredOnlyInTokensCss(string token)
    {
        var light = ThemeCss.Declarations(ThemeCss.CssPath("tokens.css"));
        var dark = ThemeCss.Declarations(ThemeCss.CssPath("tokens-dark.css"));

        light.Should().ContainKey(token, $"the paper token {token} must be declared in tokens.css");
        dark.Should().NotContainKey(token,
            $"{token} is theme-invariant: it must NOT be re-declared in tokens-dark.css — " +
            "absence is what keeps the paper white under a descendant-scoped dark theme");
        light[token].Should().NotContain("var(",
            $"{token} must be a literal light value, not a var() alias that would capture a theme at declaration time");
    }

    [Fact]
    public void PaperTokens_ResolveToPaperWhiteAndDarkInk_InBothThemes()
    {
        foreach (var dark in new[] { false, true })
        {
            var tokens = ThemeCss.TokenGraph(dark);
            ThemeCss.ResolveColour("var(--tm-paper)", tokens)
                .Should().Be("#ffffff", $"paper stays white in the {(dark ? "dark" : "light")} theme");
            ThemeCss.Ratio("var(--tm-paper-ink-muted)", "var(--tm-paper)", dark)
                .Should().BeGreaterThanOrEqualTo(4.5,
                    $"muted ink on paper must be readable in the {(dark ? "dark" : "light")} theme");
        }
    }
}
