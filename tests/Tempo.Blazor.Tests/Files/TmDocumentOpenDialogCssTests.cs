using System.Text.RegularExpressions;
using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Files;

/// <summary>
/// CSS contract of <c>TmDocumentOpenDialog.razor.css</c> (wireframe redesign plan ce19aa8c, phase 2):
/// every colour comes from a CANONICAL core token — none of the aliases the analyses invented
/// (<c>--tm-primary</c>, <c>--tm-hover-bg</c>, …), no hex fallback (the old indigo <c>#4f46e5</c> was one),
/// no literal at all. The accent therefore follows <c>--tm-color-primary</c> in the default blue theme,
/// the opt-in indigo theme and dark mode.
/// </summary>
public sealed class TmDocumentOpenDialogCssTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static readonly string[] ForbiddenAliases =
    [
        "--tm-primary", "--tm-primary-soft", "--tm-hover-bg", "--tm-text-color",
        "--tm-danger", "--tm-danger-soft", "--tm-surface-2"
    ];

    private static readonly Regex VarReference =
        new(@"var\(\s*(?<token>--tm-[\w-]+)(?<fallback>\s*,[^;{}]*)?\)", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex ColourLiteral =
        new(@"#[0-9a-fA-F]{3,8}\b|rgba?\(|hsla?\(|(?<![\w-])(?:white|black)(?![\w-])", RegexOptions.Compiled, RegexTimeout);

    private static string Css() =>
        ThemeCss.StripComments(File.ReadAllText(Path.Combine(
            ThemeCss.RepositoryRoot().FullName, "src", "Tempo.Blazor", "Components", "Files", "TmDocumentOpenDialog.razor.css")));

    [Fact]
    public void DocumentOpenDialogCss_UsesOnlyDefinedTokens()
    {
        var css = Css();
        var defined = ThemeCss.TokenGraph(dark: false).Keys
            .Concat(ThemeCss.TokenGraph(dark: true).Keys)
            .ToHashSet(StringComparer.Ordinal);

        var references = VarReference.Matches(css).ToList();
        references.Should().NotBeEmpty();

        foreach (Match reference in references)
        {
            var token = reference.Groups["token"].Value;

            ForbiddenAliases.Should().NotContain(token, "{0} is an invented alias — use the canonical token (docs/css-token-aliases.md)", token);
            defined.Should().Contain(token, "{0} must be a token tokens.css defines", token);
            reference.Groups["fallback"].Success.Should().BeFalse(
                "{0} carries a fallback, which hides an undefined alias and pins a hex into the theme", token);
        }
    }

    [Fact]
    public void DocumentOpenDialogCss_HasNoColourLiteral()
    {
        ColourLiteral.Matches(Css()).Select(match => match.Value).Should().BeEmpty(
            "the dialog paints through tokens only — the indigo #4f46e5 fallback (B6) and the #fff on the danger confirm are gone");
    }

    [Fact]
    public void DocumentOpenDialogCss_SelectedStates_AreDistinctFromHover()
    {
        // Review finding 13 of the phase-1 baseline: the selected row/card was a faint tint. Selection now
        // carries the primary token as a border/inset marker, so it never reads as plain hover.
        var css = Css();

        Regex.IsMatch(css, @"\.tm-dod-card--selected\s*\{[^}]*border-color:\s*var\(--tm-color-primary\)", RegexOptions.None, RegexTimeout)
            .Should().BeTrue("a selected card is outlined in the primary colour");
        Regex.IsMatch(css, @"\.tm-dod-row--selected[^{]*\{[^}]*var\(--tm-color-primary\)", RegexOptions.None, RegexTimeout)
            .Should().BeTrue("a selected row carries a primary marker on top of the tint");
    }
}
