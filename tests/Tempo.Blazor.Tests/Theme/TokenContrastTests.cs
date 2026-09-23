using System.Globalization;
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
    /// Application gap register #11: <c>.tm-btn-default</c> painted its border from the decorative
    /// <c>--tm-border-color</c> — 1,24:1 light / 1,41:1 dark against <c>--tm-bg-surface</c>, under
    /// the 3:1 a control boundary needs. The declaration now reads
    /// <c>--tm-border-color-control</c> (4,83:1 / 5,71:1), the same fix
    /// <c>.tm-btn-outline-secondary</c> got in 2.8.17. The test reads the DECLARED value out of
    /// <c>_button.css</c> and then measures it — a revert to the decorative token fails on the
    /// value, a token re-tune fails on the ratio.
    /// </summary>
    [Fact]
    public void DefaultButtonBorder_IsAControlToken_AndKeepsThreeToOne()
    {
        var declared = ThemeCss.Property("_button.css", ".tm-btn.tm-btn-default", "border-color");

        declared.Should().Be("var(--tm-border-color-control)",
            "dekorační --tm-border-color měřil 1,24:1/1,41:1 — pod prahem 3:1 pro hranici " +
            "ovládacího prvku; kontrolní token je tentýž fix jako .tm-btn-outline-secondary v 2.8.17");

        foreach (var dark in new[] { false, true })
        {
            ThemeCss.Ratio(declared, "var(--tm-bg-surface)", dark).Should().BeGreaterThanOrEqualTo(
                3.0,
                "hranice .tm-btn-default na --tm-bg-surface ({0}) musí držet 3:1 — změřeno {1:0.00}:1",
                dark ? "dark" : "light",
                ThemeCss.Ratio(declared, "var(--tm-bg-surface)", dark));
        }
    }

    /// <summary>
    /// UX review Fáze 17 retarget (N150): <c>.tm-btn-secondary</c> painted its border from the same
    /// decorative <c>--tm-border-color</c> register #11 measured at 1,24:1 light / 1,41:1 dark —
    /// under the 3:1 a control boundary needs. Secondary is the heavy Cancel/Discard surface, so it
    /// moves to <c>--tm-border-color-control</c> (4,83:1 / 5,71:1) like .tm-btn-default and
    /// .tm-btn-outline-secondary before it. The test reads the DECLARED value out of
    /// <c>_button.css</c> and then measures it — a revert to the decorative token fails on the
    /// value, a token re-tune fails on the ratio.
    /// </summary>
    [Fact]
    public void SecondaryButtonBorder_IsAControlToken_AndKeepsThreeToOne()
    {
        var declared = ThemeCss.Property("_button.css", ".tm-btn.tm-btn-secondary", "border-color");

        declared.Should().Be("var(--tm-border-color-control)",
            "N150: dekorační --tm-border-color měřil 1,24:1/1,41:1 — pod prahem 3:1 pro hranici " +
            "ovládacího prvku; kontrolní token je tentýž fix jako .tm-btn-default (register #11) " +
            "a .tm-btn-outline-secondary (2.8.17)");

        foreach (var dark in new[] { false, true })
        {
            ThemeCss.Ratio(declared, "var(--tm-bg-surface)", dark).Should().BeGreaterThanOrEqualTo(
                3.0,
                "hranice .tm-btn-secondary na --tm-bg-surface ({0}) musí držet 3:1 — změřeno {1:0.00}:1",
                dark ? "dark" : "light",
                ThemeCss.Ratio(declared, "var(--tm-bg-surface)", dark));
        }
    }

    /// <summary>
    /// Application gap register B: <c>.tm-btn-danger</c> and <c>.tm-badge-danger.tm-badge-filled</c>
    /// paint WHITE ink on <c>--tm-color-danger</c>, and the old #ef4444 measured 3,76:1 — under the
    /// 4,5:1 AA asks of text. The token moved one step darker (#dc2626 → 4,83:1, hover #b91c1c →
    /// 6,47:1). Dark is deliberately NOT darkened: it keeps the light #f87171 under
    /// <c>--tm-text-inverse</c> ink. The test measures the pair the elements actually render, in the
    /// theme they render it in.
    /// </summary>
    [Fact]
    public void DangerTokens_KeepTheirInkAtAa_InTheThemeEachPaints()
    {
        var light = ThemeCss.TokenGraph(dark: false);
        light["--tm-color-danger"].Should().Be("#dc2626",
            "gap register B: red-500 #ef4444 put white ink at 3,76:1 — the fill must be the "
            + "darker step the application proved (4,83:1)");
        light["--tm-color-danger-hover"].Should().Be("#b91c1c",
            "the hover step must keep white ink ≥4,5:1 as well (measured 6,47:1)");

        ThemeCss.Ratio("var(--tm-color-danger)", "var(--tm-color-white)", dark: false)
            .Should().BeGreaterThanOrEqualTo(4.5,
                "white ink on the light danger fill must hold AA — measured {0:0.00}:1",
                ThemeCss.Ratio("var(--tm-color-danger)", "var(--tm-color-white)", dark: false));
        ThemeCss.Ratio("var(--tm-color-danger-hover)", "var(--tm-color-white)", dark: false)
            .Should().BeGreaterThanOrEqualTo(4.5,
                "white ink on the light danger hover must hold AA — measured {0:0.00}:1",
                ThemeCss.Ratio("var(--tm-color-danger-hover)", "var(--tm-color-white)", dark: false));

        // Dark keeps the BRIGHT accent under dark ink — darkening it would sink the pair.
        ThemeCss.Ratio("var(--tm-text-inverse)", "var(--tm-color-danger)", dark: true)
            .Should().BeGreaterThanOrEqualTo(4.5,
                "dark ink on the dark danger fill must hold AA — measured {0:0.00}:1",
                ThemeCss.Ratio("var(--tm-text-inverse)", "var(--tm-color-danger)", dark: true));
    }

    /// <summary>
    /// Application gap register C: filled success/warning/info badges painted white ink on the
    /// semantic accents — 2,28 / 2,15 / 2,43:1 in light, all under AA. They now fill from
    /// <c>--tm-color-*-strong</c> (the 700-steps: 5,02 / 5,02 / 5,36:1 in light); dark re-points
    /// those tokens to the brightened accents and swaps the ink to <c>--tm-text-inverse</c>. The
    /// test reads the background AND the ink from the real rules in <c>_badge.css</c> — the light
    /// ink from the base rule, the dark ink from the dark override — so a drift in either half of
    /// either theme's pair goes red.
    /// </summary>
    [Fact]
    public void FilledBadges_KeepAaContrast_InBothThemes()
    {
        foreach (var style in new[] { "success", "warning", "info" })
        {
            var background = ThemeCss.Property("_badge.css", $".tm-badge-{style}.tm-badge-filled", "background");
            var inkLight = ThemeCss.Property("_badge.css", $".tm-badge-{style}.tm-badge-filled", "color");
            var inkDark = ThemeCss.Property("_badge.css",
                $"[data-theme=\"dark\"] .tm-badge-{style}.tm-badge-filled", "color");

            ThemeCss.Ratio(inkLight, background, dark: false).Should().BeGreaterThanOrEqualTo(4.5,
                "{0}: ink {1} on fill {2} must hold AA in light — measured {3:0.00}:1",
                style, inkLight, background,
                ThemeCss.Ratio(inkLight, background, dark: false));
            ThemeCss.Ratio(inkDark, background, dark: true).Should().BeGreaterThanOrEqualTo(4.5,
                "{0}: ink {1} on fill {2} must hold AA in dark — measured {3:0.00}:1",
                style, inkDark, background,
                ThemeCss.Ratio(inkDark, background, dark: true));
        }
    }

    /// <summary>
    /// Application gap register D: the alpha-composited primary tokens were literals in Tempo blue,
    /// so repointing the primary scale left the focus ring, the subtle wash and the soft border
    /// behind. They now derive via relative colour syntax — light reads primary-500, dark reads
    /// primary-500 for the wash and primary-400 (the dark primary step) for border and ring. The
    /// assertions pin the DECLARED expressions: a revert to a literal fails on the string, a wrong
    /// source step fails on it too.
    /// </summary>
    [Fact]
    public void FocusTokens_DeriveFromThePrimaryScale_InBothThemes()
    {
        var light = ThemeCss.Declarations(ThemeCss.CssPath("tokens.css"));
        var dark = ThemeCss.Declarations(ThemeCss.CssPath("tokens-dark.css"));

        light["--tm-shadow-focus"].Should().Be(
            "0 0 0 3px rgb(from var(--tm-color-primary-500) r g b / 0.4)",
            "the light ring must derive from the 500 step — a literal stays Tempo blue on rebrand");
        light["--tm-shadow-focus-danger"].Should().Be(
            "0 0 0 3px rgb(from var(--tm-color-danger) r g b / 0.3)",
            "the danger ring must derive from the danger token — same defect class");

        dark["--tm-color-primary-subtle"].Should().Be(
            "rgb(from var(--tm-color-primary-500) r g b / 0.15)",
            "the dark subtle wash must derive from the 500 step, not a blue literal");
        dark["--tm-color-primary-soft-border"].Should().Be(
            "rgb(from var(--tm-color-primary-400) r g b / 0.3)",
            "the dark soft border must derive from the dark primary step (400), not a blue literal");
        dark["--tm-shadow-focus"].Should().Be(
            "0 0 0 3px rgb(from var(--tm-color-primary-400) r g b / 0.5)",
            "the dark ring must derive from the dark primary step (400), not a blue literal");
    }

    /// <summary>
    /// The same register item, proven at the graph level instead of the string level: replacing
    /// <c>--tm-color-primary-500</c> must alter the computed focus-ring colour (light), and
    /// replacing <c>--tm-color-primary-400</c> must alter the dark one.
    /// </summary>
    [Fact]
    public void RepointingTheScaleStep_ChangesTheComputedFocusColour()
    {
        var tokens = ThemeCss.TokenGraph(dark: false);
        var declared = ThemeCss.TokenGraph(dark: false)["--tm-shadow-focus"];
        var original = ThemeCss.ResolveColour(declared, tokens);

        tokens["--tm-color-primary-500"] = "#6366f1";
        ThemeCss.ResolveColour(declared, tokens).Should().Be("#6366f1",
            "a primary-500 rebrand must repaint the focus ring — the literal never did");
        original.Should().Be("#3b82f6", "sanity: the default ring hue is primary-500");

        var darkTokens = ThemeCss.TokenGraph(dark: true);
        var darkDeclared = darkTokens["--tm-shadow-focus"];
        ThemeCss.ResolveColour(darkDeclared, darkTokens).Should().Be("#60a5fa",
            "sanity: the default dark ring hue is the dark primary step (primary-400)");
        darkTokens["--tm-color-primary-400"] = "#818cf8";
        ThemeCss.ResolveColour(darkDeclared, darkTokens).Should().Be("#818cf8",
            "a primary-400 rebrand must repaint the dark focus ring — the literal never did");
    }

    /// <summary>
    /// <c>--tm-color-primary-500-rgb</c> cannot derive in CSS (no syntax extracts channels from a
    /// colour), so what keeps it honest is this invariant: in light it names the channels of
    /// <c>--tm-color-primary-500</c>, in dark the channels of the dark primary step
    /// (<c>--tm-color-primary</c> → primary-400). A rebrand that moves the step but forgets the
    /// triplet goes red here instead of desaturating somebody's alpha-composited fills.
    /// </summary>
    [Fact]
    public void PrimaryRgbTriplet_TracksTheScaleStep_ItShadows()
    {
        static string ChannelsOf(string hex) =>
            string.Join(", ", Enumerable.Range(0, 3)
                .Select(i => int.Parse(hex.TrimStart('#').Substring(i * 2, 2),
                    NumberStyles.HexNumber, CultureInfo.InvariantCulture)));

        var light = ThemeCss.TokenGraph(dark: false);
        light["--tm-color-primary-500-rgb"].Should().Be(
            ChannelsOf(ThemeCss.ResolveColour("var(--tm-color-primary-500)", light)),
            "the light triplet must be the channels of primary-500");

        var dark = ThemeCss.TokenGraph(dark: true);
        dark["--tm-color-primary-500-rgb"].Should().Be(
            ChannelsOf(ThemeCss.ResolveColour("var(--tm-color-primary)", dark)),
            "the dark triplet must be the channels of the dark primary step (primary-400)");
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
