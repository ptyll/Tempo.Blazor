using System.Globalization;
using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Plan 92e96b25, phase 12 (TG-010) — WCAG AA contrast guard for the new
/// <c>TmCommandPalette</c> elements: the item title/description/code paints, the trailing badge
/// slot, the transient-error row with its retry button and the keyboard-hint footer with its
/// <c>&lt;kbd&gt;</c> chips. Every guard reads the DECLARED value out of
/// <c>_command-palette.css</c> first (a revert to a weaker paint fails on the string, not only
/// on the ratio) and then measures through the real token graph in both themes.
/// </summary>
public sealed class CommandPaletteContrastTests
{
    private const double TextMinimum = 4.5;

    /// <summary>
    /// Composites a declared wash token over the page surface. Handles the two translucent shapes
    /// the token graph uses: the <c>rgb(from var(…) r g b / a)</c> relative form and a raw
    /// <c>rgba(r, g, b, a)</c> literal (what --tm-color-danger-subtle declares in dark).
    /// </summary>
    private static string PaintDeclaredWash(string declared, Dictionary<string, string> tokens)
    {
        var surface = ThemeCss.ResolveColour("var(--tm-bg-surface)", tokens);
        var raw = System.Text.RegularExpressions.Regex.Match(declared,
            @"rgba\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*([0-9.]+)\s*\)");
        if (raw.Success)
        {
            var rgb = Enumerable.Range(0, 3)
                .Select(i => int.Parse(raw.Groups[i + 1].Value, CultureInfo.InvariantCulture))
                .Aggregate("", (hex, channel) => hex + channel.ToString("x2", CultureInfo.InvariantCulture));
            return ThemeCss.Composite("#" + rgb,
                double.Parse(raw.Groups[4].Value, CultureInfo.InvariantCulture), surface);
        }

        return ThemeCss.PaintedColour(declared, tokens);
    }

    // ── Item text: title/description are the existing paints; the code chip is new ──

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CodeChip_HoldsAa_OnIts_Gray100_Fill(bool dark)
    {
        ThemeCss.Property("_command-palette.css", ".tm-command-palette-code", "color")
            .Should().Be("var(--tm-text-secondary)");
        ThemeCss.Property("_command-palette.css", ".tm-command-palette-code", "background-color")
            .Should().Be("var(--tm-color-gray-100)",
                "the chip reuses the shortcut-kbd fill so it rethemes with everything else gray-100");

        ThemeCss.Ratio("var(--tm-text-secondary)", "var(--tm-color-gray-100)", dark)
            .Should().BeGreaterThanOrEqualTo(TextMinimum,
                "the code is body-sized text — {0} theme, text-secondary on the gray-100 chip", dark ? "dark" : "light");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Title_And_Description_HoldAa_On_The_Item(bool dark)
    {
        // Regression net: the new trailing slot must not have come at the cost of the existing
        // paints — title and description still declare the surface-bound inks.
        ThemeCss.Property("_command-palette.css", ".tm-command-palette-title", "color")
            .Should().Be("var(--tm-text-primary)");
        ThemeCss.Property("_command-palette.css", ".tm-command-palette-description", "color")
            .Should().Be("var(--tm-text-secondary)");

        ThemeCss.Ratio("var(--tm-text-primary)", "var(--tm-bg-surface)", dark)
            .Should().BeGreaterThanOrEqualTo(TextMinimum, "item title, {0}", dark ? "dark" : "light");
        ThemeCss.Ratio("var(--tm-text-secondary)", "var(--tm-bg-surface)", dark)
            .Should().BeGreaterThanOrEqualTo(TextMinimum, "item description, {0}", dark ? "dark" : "light");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Title_Description_And_Code_HoldAa_On_The_Focused_Fill(bool dark)
    {
        // The focused/hovered row repaints itself with --tm-bg-muted; the ellipsis-clipped texts
        // can land on that fill, so the pair must hold there too (the kbd precedent swapped its
        // chip to the surface inside a focused row; the texts rely on the muted fill being close
        // to the surface family).
        ThemeCss.Ratio("var(--tm-text-primary)", "var(--tm-bg-muted)", dark)
            .Should().BeGreaterThanOrEqualTo(TextMinimum, "item title on the focused fill, {0}", dark ? "dark" : "light");
        ThemeCss.Ratio("var(--tm-text-secondary)", "var(--tm-bg-muted)", dark)
            .Should().BeGreaterThanOrEqualTo(TextMinimum, "item description/code on the focused fill, {0}", dark ? "dark" : "light");
    }

    // ── Transient error row ──────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ErrorText_And_Retry_HoldAa(bool dark)
    {
        ThemeCss.Property("_command-palette.css", ".tm-command-palette-error-text", "color")
            .Should().Be("var(--tm-text-primary)");
        ThemeCss.Ratio("var(--tm-text-primary)", "var(--tm-bg-surface)", dark)
            .Should().BeGreaterThanOrEqualTo(TextMinimum, "error text, {0}", dark ? "dark" : "light");

        ThemeCss.Property("_command-palette.css", ".tm-command-palette-retry", "color")
            .Should().Be("var(--tm-color-danger-text)");
        ThemeCss.Property("_command-palette.css", ".tm-command-palette-retry", "background-color")
            .Should().Be("var(--tm-bg-surface)");
        ThemeCss.Ratio("var(--tm-color-danger-text)", "var(--tm-bg-surface)", dark)
            .Should().BeGreaterThanOrEqualTo(TextMinimum, "retry label, {0}", dark ? "dark" : "light");

        // Hover paints the danger-subtle wash (a RAW rgba in dark, unlike the primary-subtle
        // relative-colour form — the shared helper only composites rgb(from …) chains, so the
        // declared token is parsed here and composited over the surface explicitly).
        ThemeCss.Property("_command-palette.css", ".tm-command-palette-retry:hover", "background-color")
            .Should().Be("var(--tm-color-danger-subtle)");
        var tokens = ThemeCss.TokenGraph(dark);
        var retryInk = ThemeCss.ResolveColour("var(--tm-color-danger-text)", tokens);
        var wash = PaintDeclaredWash(tokens["--tm-color-danger-subtle"], tokens);
        ThemeCss.Contrast(retryInk, wash).Should().BeGreaterThanOrEqualTo(TextMinimum,
            "retry label on the hovered wash, {0} — measured {1:0.00}:1",
            dark ? "dark" : "light", ThemeCss.Contrast(retryInk, wash));
    }

    // ── Footer with keyboard hints ───────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Footer_Hint_And_Kbd_HoldAa(bool dark)
    {
        ThemeCss.Property("_command-palette.css", ".tm-command-palette-footer", "color")
            .Should().Be("var(--tm-text-secondary)");
        ThemeCss.Ratio("var(--tm-text-secondary)", "var(--tm-bg-surface)", dark)
            .Should().BeGreaterThanOrEqualTo(TextMinimum, "footer hint text, {0}", dark ? "dark" : "light");

        // The kbd chips inherit the footer ink on the gray-100 chip (same pair as the code chip).
        ThemeCss.Property("_command-palette.css", ".tm-command-palette-footer kbd", "background-color")
            .Should().Be("var(--tm-color-gray-100)");
        ThemeCss.Ratio("var(--tm-text-secondary)", "var(--tm-color-gray-100)", dark)
            .Should().BeGreaterThanOrEqualTo(TextMinimum, "footer kbd text, {0}", dark ? "dark" : "light");
    }
}
