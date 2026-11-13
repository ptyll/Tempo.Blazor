using System.Globalization;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Plan 92e96b25, phase 11 (KG-008) — the WCAG AA contrast audit over <c>TmMultiSelect</c>
/// (disabled state, chips, placeholder), <c>TmChangeDiff</c>, the <c>TmRichEditorFull</c> /
/// <c>TmRichEditorSimple</c> footer counters and the clear-format glyph, <c>TmAlert</c>
/// descriptions (every variant) and the <c>TmTopBar</c> search hint. Backlog items
/// 743dc7bb-45c4-48b4-ab86-109934b25d94 (disabled TmMultiSelect painted a LIGHT surface in dark
/// mode through the inverted <c>--tm-color-gray-800</c> scale) and ca474f34-ca42-4f70-a890-4d0273f71405
/// (several text paints under WCAG 2.1 AA).
///
/// Every guard reads the DECLARED value out of the component stylesheet first (a revert to the
/// old paint fails on the string, not only on the ratio) and then measures it through the real
/// token graph — the same <see cref="ThemeCss"/> helpers the rest of the suite composites with.
/// Decision O1 (user binding): a disabled <c>TmMultiSelect</c> is used by PromptHelper for LOCKED,
/// READABLE content, so its chips keep AA 4.5:1 — the disabled state carries cursor,
/// <c>aria-disabled</c> and the muted box, never a whole-control <c>opacity</c>.
/// </summary>
public sealed class Kg008ContrastAuditTests
{
    // ── TmMultiSelect ────────────────────────────────────────────────────────

    [Fact]
    public void MultiSelect_Disabled_Paints_Like_Other_Disabled_Inputs()
    {
        // Was: background var(--tm-color-gray-800) — the dark theme INVERTS the gray scale, so
        // gray-800 is #f8fafc there and the disabled control painted a light surface (backlog
        // 743dc7bb) — plus opacity: 0.6 over the whole control, sinking the chip to 1.52:1.
        var body = ThemeCss.RuleDeclarations(ThemeCss.ComponentCss("_multiselect.css"),
            ".tm-multiselect--disabled");

        ThemeCss.Property("_multiselect.css", ".tm-multiselect--disabled", "background-color")
            .Should().Be("var(--tm-color-gray-50)",
                "the disabled multiselect must paint the same surface as .tm-input[disabled]; "
                + "in dark --tm-color-gray-50 is #1e293b — the same surface the other disabled inputs get");
        ThemeCss.Property("_multiselect.css", ".tm-multiselect--disabled", "color")
            .Should().Be("var(--tm-text-disabled)",
                "the disabled multiselect must read the same disabled ink as .tm-input[disabled]");

        body.Should().NotContain("opacity",
            "the whole-control opacity: 0.6 was the second half of the 1.52:1 defect — locked "
            + "content must stay readable (decision O1); disabledness carries cursor + aria-disabled + "
            + "the muted box, not a paint-level fade");

        ThemeCss.TryProperty("_multiselect.css", "[data-theme=\"dark\"] .tm-multiselect--disabled",
                "background-color")
            .Should().BeNull(
                "the dark override re-pointing the disabled background at --tm-color-gray-800 must be "
                + "gone — the inverted gray scale turned that override into a light surface");
        ThemeCss.TryProperty("_multiselect.css", ".tm-dark .tm-multiselect--disabled",
                "background-color")
            .Should().BeNull("the .tm-dark twin of the override must be gone with it");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MultiSelectChip_HoldsAa_Enabled_And_OnTheDisabledBox(bool dark)
    {
        // Light: primary-700 on primary-100. Dark: the raw rgba(59,130,246,.2) wash +
        // --tm-color-primary-300 measured 1.52:1 on the disabled (light-painted) box; the chip
        // now paints the theme-aware subtle wash with the primary-text ink, on BOTH the enabled
        // surface and the disabled gray-50 box.
        var background = ThemeCss.Property("_multiselect.css",
            dark ? "[data-theme=\"dark\"] .tm-multiselect__chip" : ".tm-multiselect__chip",
            "background");
        var ink = ThemeCss.Property("_multiselect.css",
            dark ? "[data-theme=\"dark\"] .tm-multiselect__chip" : ".tm-multiselect__chip",
            "color");

        if (dark)
        {
            background.Should().Be("var(--tm-color-primary-subtle)",
                "the dark chip must composite over the surface the same way every other "
                + "primary-subtle wash does — the rgba(59,130,246,…) literal never rethemes");
            ink.Should().Be("var(--tm-color-primary-text)",
                "the dark chip ink must be the primary-text token made for text on that wash");
        }
        else
        {
            background.Should().Be("var(--tm-color-primary-100)");
            ink.Should().Be("var(--tm-color-primary-700)");
        }

        var tokens = ThemeCss.TokenGraph(dark);
        var inkColour = ThemeCss.ResolveColour(ink, tokens);

        foreach (var backdrop in new[] { "var(--tm-bg-surface)", "var(--tm-color-gray-50)" })
        {
            var wash = ThemeCss.PaintedColour(background, tokens, backdrop);
            ThemeCss.Contrast(inkColour, wash).Should().BeGreaterThanOrEqualTo(4.5,
                "chip ink on the painted chip wash over {0} ({1}) must hold AA — a locked chip is "
                + "content, not chrome (decision O1) — measured {2:0.00}:1",
                backdrop, dark ? "dark" : "light", ThemeCss.Contrast(inkColour, wash));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MultiSelectPlaceholder_HoldsAa_Without_Opacity(bool dark)
    {
        // Was: --tm-text-tertiary under the whole-control opacity: 0.6 → 2.45:1.
        var body = ThemeCss.RuleDeclarations(ThemeCss.ComponentCss("_multiselect.css"),
            ".tm-multiselect__placeholder");
        body.Should().NotContain("opacity",
            "the placeholder must never ride a paint-level fade");

        ThemeCss.Ratio("var(--tm-text-tertiary)", "var(--tm-bg-surface)", dark)
            .Should().BeGreaterThanOrEqualTo(4.5,
                "placeholder na povrchu ({0}) musí držet AA — změřeno {1:0.00}:1",
                dark ? "dark" : "light",
                ThemeCss.Ratio("var(--tm-text-tertiary)", "var(--tm-bg-surface)", dark));
    }

    // ── TmChangeDiff ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangeDiff_Values_HoldAa_OnTheRowSurface(bool dark)
    {
        // Was: --tm-color-success (a FILL tone) at 2.18:1 and --tm-color-danger under
        // opacity: 0.8 at 3.65:1 on --tm-bg-surface-secondary. The strikethrough keeps the old
        // value a text, so both halves are held to 4.5:1.
        var @new = ThemeCss.Property("_change-diff.css", ".tm-change-diff-new", "color");
        var old = ThemeCss.Property("_change-diff.css", ".tm-change-diff-old", "color");

        @new.Should().Be("var(--tm-color-success-text)",
            "the new value is TEXT and must paint the success text tone, not the fill tone");
        old.Should().Be("var(--tm-color-danger-text)",
            "the old value is TEXT (strikethrough does not demote it to decoration) and must paint "
            + "the danger text tone");

        ThemeCss.RuleDeclarations(ThemeCss.ComponentCss("_change-diff.css"), ".tm-change-diff-new")
            .Should().NotContain("opacity");
        ThemeCss.RuleDeclarations(ThemeCss.ComponentCss("_change-diff.css"), ".tm-change-diff-old")
            .Should().NotContain("opacity");

        foreach (var (selector, token) in new[]
                 {
                     (".tm-change-diff-new", @new),
                     (".tm-change-diff-old", old),
                 })
        {
            ThemeCss.Ratio(token, "var(--tm-bg-surface-secondary)", dark)
                .Should().BeGreaterThanOrEqualTo(4.5,
                    "{0} na --tm-bg-surface-secondary ({1}) musí držet AA — změřeno {2:0.00}:1",
                    selector, dark ? "dark" : "light",
                    ThemeCss.Ratio(token, "var(--tm-bg-surface-secondary)", dark));
        }
    }

    // ── Rich editor counters and clear-format glyph ──────────────────────────

    [Theory]
    [InlineData("_rich-editor-full.css", ".tm-rich-editor-full .tm-rte-word-count")]
    [InlineData("_rich-editor-full.css", ".tm-rich-editor-full .tm-rte-char-count")]
    [InlineData("_rich-editor-simple.css", ".tm-rich-editor-simple .tm-rte-char-count")]
    public void RichEditorCounters_Paint_SecondaryText_And_HoldAa(string stylesheet, string selector)
    {
        // Was: --tm-text-tertiary on the --tm-bg-muted footer → 4.04–4.34:1 (under AA).
        var declared = ThemeCss.Property(stylesheet, selector, "color");
        declared.Should().Be("var(--tm-text-secondary)",
            "{0} in {1} must read the secondary ink — tertiary on the muted footer measured under AA",
            selector, stylesheet);

        foreach (var dark in new[] { false, true })
        {
            ThemeCss.Ratio(declared, "var(--tm-bg-muted)", dark)
                .Should().BeGreaterThanOrEqualTo(4.5,
                    "{0} na --tm-bg-muted ({1}) musí držet AA — změřeno {2:0.00}:1",
                    selector, dark ? "dark" : "light",
                    ThemeCss.Ratio(declared, "var(--tm-bg-muted)", dark));
        }
    }

    /// <summary>
    /// E2E audit find (KG-008): the over-limit figure painted the raw <c>--tm-color-danger</c>
    /// FILL tone — 4.39:1 on the muted footer, just under AA. Error text takes the danger-text
    /// tone in both editor stylesheets.
    /// </summary>
    [Theory]
    [InlineData("_rich-editor-full.css", ".tm-rich-editor-full .tm-rte-char-count-over")]
    [InlineData("_rich-editor-simple.css", ".tm-rich-editor-simple .tm-rte-char-count-over")]
    public void RichEditorOverLimitCounter_Uses_TheDangerTextTone(string stylesheet, string selector)
    {
        var declared = ThemeCss.Property(stylesheet, selector, "color");
        declared.Should().Be("var(--tm-color-danger-text)",
            "the over-limit figure is error TEXT — the fill tone measured 4.39:1 on --tm-bg-muted");

        foreach (var dark in new[] { false, true })
        {
            ThemeCss.Ratio(declared, "var(--tm-bg-muted)", dark)
                .Should().BeGreaterThanOrEqualTo(4.5,
                    "{0} na --tm-bg-muted ({1}) musí držet AA — změřeno {2:0.00}:1",
                    selector, dark ? "dark" : "light",
                    ThemeCss.Ratio(declared, "var(--tm-bg-muted)", dark));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RichEditorClearFormat_Glyph_HoldsAa(bool dark)
    {
        // Was: opacity: 0.7 on the strikethrough "Tx" glyph → 3.59:1. The glyph now paints the
        // full button ink; the line-through alone carries the meaning, so the opacity is removed.
        ThemeCss.RuleDeclarations(ThemeCss.ComponentCss("_editor-toolbar.css"),
                ".tm-rte-toolbar .tm-rte-clear-format")
            .Should().NotContain("opacity");

        // The glyph inherits .tm-rte-btn's ink on the button surface — measure THAT pair.
        var ink = ThemeCss.Property("_editor-toolbar.css", ".tm-rte-toolbar .tm-rte-btn", "color");
        ink.Should().Be("var(--tm-text-secondary)");
        ThemeCss.Ratio(ink, "var(--tm-bg-surface)", dark)
            .Should().BeGreaterThanOrEqualTo(4.5,
                "clear-format glyph ink on the button surface ({0}) must hold AA — measured {1:0.00}:1",
                dark ? "dark" : "light",
                ThemeCss.Ratio(ink, "var(--tm-bg-surface)", dark));
    }

    // ── TmAlert descriptions (every variant) ─────────────────────────────────

    /// <summary>
    /// Variant × severity ink/background pairs exactly as the shipped <c>_alert.css</c> declares
    /// them (light literals, dark tokens). The description inherits the container ink — the old
    /// <c>opacity: 0.9</c> sank it to 4.09:1 at best, and the light filled success/warning fills
    /// never reached AA even at full opacity, so they move to the <c>-strong</c> steps (the
    /// badge register-C precedent).
    /// </summary>
    private static readonly (string Variant, string Severity, string Ink, string Background, bool Dark)[] AlertPairs =
    [
        ("soft", "info", "var(--tm-color-primary-800)", "var(--tm-color-primary-50)", false),
        ("soft", "success", "#166534", "#f0fdf4", false),
        ("soft", "warning", "#92400e", "#fffbeb", false),
        ("soft", "error", "#991b1b", "#fef2f2", false),
        ("filled", "info", "var(--tm-color-white)", "var(--tm-color-primary-600)", false),
        ("filled", "success", "var(--tm-color-white)", "var(--tm-color-success-strong)", false),
        ("filled", "warning", "var(--tm-color-white)", "var(--tm-color-warning-strong)", false),
        ("filled", "error", "var(--tm-color-white)", "var(--tm-color-danger)", false),
        // Outlined sits on the page surface.
        ("outlined", "info", "var(--tm-color-primary-700)", "var(--tm-bg-page)", false),
        ("outlined", "success", "#15803d", "var(--tm-bg-page)", false),
        ("outlined", "warning", "#a16207", "var(--tm-bg-page)", false),
        ("outlined", "error", "#b91c1c", "var(--tm-bg-page)", false),
        // Dark: filled takes dark ink on the brightened accents (the shipped dark block).
        ("filled", "info", "var(--tm-text-inverse)", "var(--tm-color-primary)", true),
        ("filled", "success", "var(--tm-text-inverse)", "var(--tm-color-success)", true),
        ("filled", "warning", "var(--tm-text-inverse)", "var(--tm-color-warning)", true),
        ("filled", "error", "var(--tm-text-inverse)", "var(--tm-color-danger)", true),
        // Dark: soft washes composite their 10% tint over the surface.
        ("soft", "info", "var(--tm-color-primary-300)",
            "rgba(var(--tm-color-primary-500-rgb), 0.1)", true),
        ("soft", "success", "#86efac", "rgba(34, 197, 94, 0.1)", true),
        ("soft", "warning", "#fcd34d", "rgba(245, 158, 11, 0.1)", true),
        ("soft", "error", "#fca5a5", "rgba(239, 68, 68, 0.1)", true),
        // Dark: outlined inks on the page surface.
        ("outlined", "info", "var(--tm-color-primary-300)", "var(--tm-bg-page)", true),
        ("outlined", "success", "#86efac", "var(--tm-bg-page)", true),
        ("outlined", "warning", "#fcd34d", "var(--tm-bg-page)", true),
        ("outlined", "error", "#fca5a5", "var(--tm-bg-page)", true),
    ];

    [Fact]
    public void AlertDescription_CarriesNoOpacity_And_EveryVariant_HoldsAa()
    {
        ThemeCss.RuleDeclarations(ThemeCss.ComponentCss("_alert.css"), ".tm-alert__description")
            .Should().NotContain("opacity",
                "the description is body text of the alert — the 0.9 fade was the defect");

        // Filled light success/warning must paint the -strong fills (white on the raw accents
        // measured 3.30 / 3.20 — the two pairs that never reached AA even without the fade).
        ThemeCss.Property("_alert.css", ".tm-alert--filled.tm-alert--success", "background-color")
            .Should().Be("var(--tm-color-success-strong)");
        ThemeCss.Property("_alert.css", ".tm-alert--filled.tm-alert--warning", "background-color")
            .Should().Be("var(--tm-color-warning-strong)");

        // KG-008 review nit: the filled inks were the literal #fff; they now paint the on-colour
        // token (white in light, and the dark block above already re-inks the fills with
        // --tm-text-inverse at a higher specificity).
        foreach (var severity in new[] { "info", "success", "warning", "error" })
        {
            ThemeCss.Property("_alert.css", $".tm-alert--filled.tm-alert--{severity}", "color")
                .Should().Be("var(--tm-color-on-primary)");
        }

        foreach (var (variant, severity, ink, background, dark) in AlertPairs)
        {
            var tokens = ThemeCss.TokenGraph(dark);
            var inkColour = ThemeCss.ResolveColour(ink, tokens);
            var surface = background.Contains("rgba(")
                ? CompositeRgba(background, tokens)
                : ThemeCss.ResolveColour(background, tokens);

            ThemeCss.Contrast(inkColour, surface).Should().BeGreaterThanOrEqualTo(4.5,
                "tm-alert--{0}.{1} description ink on its fill ({2}) must hold AA — measured {3:0.00}:1",
                variant, severity, dark ? "dark" : "light",
                ThemeCss.Contrast(inkColour, surface));
        }
    }

    /// <summary>The surface a tinted dark wash actually paints: its rgba tint over the surface.</summary>
    private static string CompositeRgba(string rgba, Dictionary<string, string> tokens)
    {
        var match = Regex.Match(rgba,
            @"rgba\(\s*(var\([^)]*\)|#\w+|[\d.,\s]+?)\s*,\s*([0-9.]+)\s*\)",
            RegexOptions.None, TimeSpan.FromSeconds(5));
        match.Success.Should().BeTrue($"'{rgba}' is an rgba(var(--x), a) wash");

        var source = match.Groups[1].Value.Trim();
        var resolved = source.StartsWith("var(", StringComparison.Ordinal) || source.StartsWith('#')
            ? ThemeCss.ResolveColour(source, tokens)
            : source;
        // Both a plain "34, 197, 94" triplet and the *-rgb tokens resolve to a channel
        // triplet, not #rrggbb — convert either to hex before compositing.
        var colour = resolved.StartsWith('#')
            ? resolved
            : "#" + string.Concat(resolved.Split(',', StringSplitOptions.TrimEntries)
                .Select(channel => int.Parse(channel, CultureInfo.InvariantCulture)
                    .ToString("x2", CultureInfo.InvariantCulture)));
        var alpha = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        return ThemeCss.Composite(colour, alpha, ThemeCss.ResolveColour("var(--tm-bg-surface)", tokens));
    }

    // ── TmTopBar search hint ─────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TopBarSearchHint_HoldsAa_OnTheTrigger(bool dark)
    {
        // Audit ca474f34 read 3.8:1 in dark. The hint rides the trigger's
        // --tm-bg-surface-secondary surface; the tertiary ink on that pair must hold AA in
        // both themes (the dark theme now declares its own tertiary).
        var declared = ThemeCss.Property("_topbar.css", ".tm-topbar-search-hint", "color");
        declared.Should().Be("var(--tm-text-tertiary)");

        ThemeCss.Ratio(declared, "var(--tm-bg-surface-secondary)", dark)
            .Should().BeGreaterThanOrEqualTo(4.5,
                "tm-topbar-search-hint na --tm-bg-surface-secondary ({0}) musí držet AA — změřeno {1:0.00}:1",
                dark ? "dark" : "light",
                ThemeCss.Ratio(declared, "var(--tm-bg-surface-secondary)", dark));
    }
}
