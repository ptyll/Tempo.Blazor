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

    // Phase-9 review: the 112 px hysteresis of TmDocumentOpenDialog (FullscreenChromePx) is the chrome the dialog pays on BOTH
    // sides that the fullscreen panel does not: overlay padding + modal margin + body padding. It is a copy of three values in
    // _modal.css, so it is derived here from the stylesheet and the C# constant must agree - a spacing change in the modal that
    // forgets the dialog goes red instead of letting the presentation flap again.
    [Fact]
    public void FullscreenChromeHysteresis_EqualsTheModalChromeBelow768()
    {
        static double Rem(string value, Dictionary<string, string> tokens)
        {
            var match = Regex.Match(ThemeCss.ResolveColour(value, tokens), @"^(?<n>[0-9.]+)rem$", RegexOptions.None, RegexTimeout);
            match.Success.Should().BeTrue("'{0}' must resolve to a rem length", value);
            return double.Parse(match.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture) * 16;
        }

        var tokens = ThemeCss.TokenGraph(dark: false);

        // Between 640 and 767 px: .tm-modal-overlay padding (space-4), .tm-modal margin (space-4), .tm-modal-body padding (space-6).
        // (Below 640 px the overlay / body steps shrink, but then the dialog is already past the mobile threshold.)
        var overlay = ThemeCss.Property("_modal.css", ".tm-modal-overlay", "padding");
        var margin = ThemeCss.Property("_modal.css", ".tm-modal", "margin");
        var body = ThemeCss.Property("_modal.css", ".tm-modal-body", "padding");

        overlay.Should().Be("var(--tm-space-4)");
        margin.Should().Be("var(--tm-space-4)");
        body.Should().Be("var(--tm-space-6)");

        var oneSide = Rem(overlay, tokens) + Rem(margin, tokens) + Rem(body, tokens);
        var chrome = (int)(2 * oneSide);
        chrome.Should().Be(112);

        var source = File.ReadAllText(Path.Combine(
            ThemeCss.RepositoryRoot().FullName, "src", "Tempo.Blazor", "Components", "Files", "TmDocumentOpenDialog.razor.cs"));
        Regex.IsMatch(source, $@"private const int FullscreenChromePx = {chrome};", RegexOptions.None, RegexTimeout)
            .Should().BeTrue("the dialog's FullscreenChromePx must equal the chrome _modal.css paints ({0} px)", chrome);
    }

    // Phase-9 review: the fullscreen panel was reached through a ::deep selector under .tm-dod-host, so the modal rules it overrides
    // must still exist under the exact names the stylesheet relies on.
    [Fact]
    public void MobilePanelOverrides_TargetModalParts_ThatStillExist()
    {
        var css = Css();
        var modal = ThemeCss.ComponentCss("_modal.css");

        foreach (var modalPart in new[] { ".tm-modal-body", ".tm-modal-footer", ".tm-modal--fullscreen-presentation" })
        {
            modal.Should().Contain(modalPart, "_modal.css owns {0}, which the dialog's mobile panel overrides", modalPart);
        }

        css.Should().Contain(".tm-dod-host ::deep .tm-dod-modal--mobile .tm-modal-body");
        css.Should().Contain(".tm-dod-host ::deep .tm-dod-modal--mobile .tm-modal-footer");
    }

    // Phase-3: ink on the primary-subtle wash reads the text token made for it (the fill token measured 4.19:1 in indigo dark).
    [Fact]
    public void InkOnThePrimarySubtleWash_ReadsThePrimaryTextToken()
    {
        var css = Css();
        var rules = Regex.Matches(css, @"(?<sel>[^{}]+)\{(?<body>[^{}]*)\}", RegexOptions.None, RegexTimeout);
        var offenders = new List<string>();
        foreach (Match rule in rules)
        {
            var body = rule.Groups["body"].Value;
            if (body.Contains("background: var(--tm-color-primary-subtle)", StringComparison.Ordinal)
                && Regex.IsMatch(body, @"(?<![-\w])color:\s*var\(--tm-color-primary\)\s*;", RegexOptions.None, RegexTimeout))
            {
                offenders.Add(rule.Groups["sel"].Value.Trim());
            }
        }

        offenders.Should().BeEmpty("a rule that paints primary-subtle must put its text ink on --tm-color-primary-text: {0}", string.Join(" | ", offenders));
    }
}
