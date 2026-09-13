using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// A fail-closed list of text×surface token pairs that must keep WCAG AA (4.5:1) in the theme they
/// are measured in. Every pair is resolved through the real token graph — <see cref="ThemeCss.TokenGraph"/>
/// layers <c>tokens-dark.css</c> over <c>tokens.css</c> exactly as the theme does — so the number is
/// computed, not copied.
/// </summary>
/// <remarks>
/// 910c84fe is the pair this list exists for: <c>--tm-text-placeholder</c> was declared only on
/// <c>:root</c> as <c>var(--tm-text-tertiary)</c>, and a custom property substitutes its var() where
/// it is DECLARED — so every dark element inherited the LIGHT tertiary (#6b7280), which measures
/// 3,03:1 on <c>--tm-bg-surface</c>. The dark override re-declares the same expression, which is the
/// convention <c>tokens-dark.css</c> already documents for <c>--tm-sort-indicator-active</c> and
/// <c>--tm-color-on-primary</c>.
/// </remarks>
public sealed class TokenContrastTests
{
    /// <summary>
    /// Text×surface pairs held to AA. The denominator is this list itself: a token that vanishes from
    /// the graph fails the resolution, and a pair added here can never pass vacuously — the theme's
    /// own values are what get measured.
    /// </summary>
    private static readonly (string Foreground, string Surface, bool Dark, double Minimum)[] Pairs =
    [
        ("var(--tm-text-primary)", "var(--tm-bg-surface)", false, 4.5),
        ("var(--tm-text-secondary)", "var(--tm-bg-surface)", false, 4.5),
        ("var(--tm-text-placeholder)", "var(--tm-bg-surface)", false, 4.5),
        ("var(--tm-text-primary)", "var(--tm-bg-surface)", true, 4.5),
        ("var(--tm-text-secondary)", "var(--tm-bg-surface)", true, 4.5),
        ("var(--tm-text-tertiary)", "var(--tm-bg-surface)", true, 4.5),
        ("var(--tm-text-placeholder)", "var(--tm-bg-surface)", true, 4.5),
    ];

    [Fact]
    public void DarkTheme_DeclaresItsOwnPlaceholderToken()
    {
        var darkDeclarations = ThemeCss.Declarations(ThemeCss.CssPath("tokens-dark.css"));

        darkDeclarations.Should().ContainKey(
            "--tm-text-placeholder",
            "910c84fe: the token exists only as a :root alias — the dark file must name it itself, "
            + "or a future refactor of the light alias silently decides what dark placeholders paint");
    }

    [Fact]
    public void EveryTextToken_KeepsAaContrast_OnItsSurface()
    {
        Pairs.Should().NotBeEmpty("bez dvojic by prázdný seznam hlídal nic — jmenovatel je seznam sám");

        foreach (var (foreground, surface, dark, minimum) in Pairs)
        {
            var tokens = ThemeCss.TokenGraph(dark);
            var ratio = ThemeCss.Ratio(foreground, surface, dark);
            ratio.Should().BeGreaterThanOrEqualTo(
                minimum,
                "{0} na {1} ({2}) musí držet {3}:1 — změřeno {4:0.00}:1; "
                + "text, který se pod povrchem ztratí, je afordance bez mechanismu",
                foreground, surface, dark ? "dark" : "light", minimum, ratio);
        }
    }

    /// <summary>
    /// The mutation: a placeholder tone that only passes in light must go red in dark. #64748b —
    /// the step this defect actually painted before the fix — measures 3,07:1 on the dark surface,
    /// so a regression to it is caught by the same arithmetic the pair list uses.
    /// </summary>
    [Fact]
    public void TheCheck_DiscriminatesABelowAaToken()
    {
        var tokens = ThemeCss.TokenGraph(dark: true);
        tokens["--tm-text-placeholder"] = "#64748b";

        var ratio = ThemeCss.Contrast(
            ThemeCss.ResolveColour("var(--tm-text-placeholder)", tokens),
            ThemeCss.ResolveColour("var(--tm-bg-surface)", tokens));

        ratio.Should().BeLessThan(
            4.5,
            "mutace na hodnotu, která na tmavém povrchu AA nedrží, musí být červená — "
            + "sonda, která tohle propustí, nehlídá kontrast ale existenci tokenu");
    }
}
