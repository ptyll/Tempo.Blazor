using System.Text.RegularExpressions;
using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Components.DataDisplay;

/// <summary>
/// CSS contract of <c>TmQRCode.razor.css</c> (plan 8fa7319c, phase 3): the QR code's ink and quiet
/// zone are component tokens with PAPER defaults, so
/// <list type="bullet">
///   <item>no rule carries a colour literal — a tokenised component that pins <c>#ffffff</c> in CSS
///   is exactly the hardcoded colour the phase removes;</item>
///   <item>the quiet zone reads <c>--tm-qr-code-background</c>, whose default is
///   <c>var(--tm-paper)</c> — the paper tokens are the ones that stay light in EVERY theme
///   (<c>tokens-dark.css</c> deliberately does not re-declare them), which is what keeps the code
///   scannable on a dark page;</item>
///   <item>nothing is animated, so no reduced-motion block is needed.</item>
/// </list>
/// </summary>
public sealed class TmQRCodeCssTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static readonly Regex ColourLiteral = new(
        @"#[0-9a-fA-F]{3,8}\b|rgba?\(|(?<![\w-])(?:white|black)(?![\w-])",
        RegexOptions.Compiled,
        RegexTimeout);

    private static string Css() =>
        ThemeCss.StripComments(File.ReadAllText(Path.Combine(
            ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor.Codes", "Components", "DataDisplay", "TmQRCode.razor.css")));

    private static string TokensDark() =>
        ThemeCss.StripComments(File.ReadAllText(Path.Combine(
            ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor", "wwwroot", "css", "tokens-dark.css")));

    [Fact]
    public void TmQRCodeCss_Declares_The_Ink_And_Quiet_Zone_Tokens_Without_A_Literal()
    {
        var css = Css();

        css.Should().Contain("--tm-qr-code-foreground:", "the ink is a component token");
        css.Should().Contain("--tm-qr-code-background:", "the quiet zone is a component token");
        css.Should().Contain("--tm-qr-code-foreground: var(--tm-paper-ink)",
            "the default ink is the paper ink — dark enough for every scanner in every theme");
        css.Should().Contain("--tm-qr-code-background: var(--tm-paper)",
            "the default quiet zone is the paper surface, which is white in every theme");
    }

    [Fact]
    public void TmQRCodeCss_Has_No_Colour_Literal()
    {
        ColourLiteral.Matches(Css()).Select(match => match.Value).Should().BeEmpty(
            "a hex/rgb()/named colour in the component stylesheet is the drift the token contract forbids");
    }

    [Fact]
    public void TmQRCodeCss_Paints_The_Surface_From_The_Token()
    {
        Css().Should().Contain(".tm-qr-code__surface",
            "the themed quiet-zone rect is styled through this class");
        Css().Should().MatchRegex(@"\.tm-qr-code__surface\s*\{[^}]*fill:\s*var\(--tm-qr-code-background\)",
            "the surface reads the token with no fallback (the fallback would pin a colour)");
        Css().Should().MatchRegex(@"\.tm-qr-code__svg--themed\s*\{[^}]*color:\s*var\(--tm-qr-code-foreground\)",
            "the themed SVG carries the ink as currentColor's color");
    }

    [Fact]
    public void TmQRCodeCss_Has_No_Dark_Theme_Block_And_No_Dark_Quiet_Zone()
    {
        var css = Css();
        css.Should().NotContain("[data-theme=\"dark\"]", "a component stylesheet never forks the theme");
        css.Should().NotContain(".tm-dark");

        var dark = TokensDark();
        dark.Should().NotContain("--tm-qr-code-background",
            "the quiet zone must stay light in dark mode — a dark override makes the code unscannable");
        dark.Should().NotContain("--tm-paper:",
            "the paper tokens are deliberately theme-invariant; re-declaring one here would flip the quiet zone");
    }

    [Fact]
    public void TmQRCodeCss_Styles_The_Download_Action_From_Tokens()
    {
        var css = Css();
        css.Should().Contain(".tm-qr-code__download", "the opt-in download link is a real surface, not a bare anchor");
        css.Should().MatchRegex(@"\.tm-qr-code__download\s*\{[^}]*color:\s*var\(--tm-color-primary\)[^}]*\}",
            "the action reads the primary text token (a themed link, not a hardcoded blue)");
        css.Should().MatchRegex(@"\.tm-qr-code__download\s*\{[^}]*line-height:",
            "the root's line-height:0 would collapse the link's own text");
        css.Should().NotContain("animation", "the QR code is static; there is no reduced-motion subject here");
        css.Should().NotContain("transition");
    }
}
