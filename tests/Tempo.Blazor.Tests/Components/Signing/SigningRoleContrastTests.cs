using System.Globalization;
using System.Text.RegularExpressions;
using FluentAssertions;
using Tempo.Blazor.Components.Signing;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Components.Signing;

/// <summary>
/// Role pill contrast (Signing redesign phase 7 review): <see cref="SigningRolePalette.ContrastTextFor"/>
/// picks the ink with the HIGHER ACTUAL WCAG contrast ratio — default-palette colours (and the
/// legacy role-6 value) resolve to their per-theme <c>--tm-signing-role-N-on</c> tokens, a null or
/// unparsable colour rides <c>var(--tm-color-on-primary)</c>, and only a CUSTOM colour computes the
/// white/near-black decision inline (<see cref="SigningRolePalette.IsLightPaint"/> marks it for the
/// tone class). The token pairs themselves are re-verified against the stylesheet per theme.
/// </summary>
public class SigningRoleContrastTests
{
    [Theory]
    [InlineData("#2563eb", "var(--tm-signing-role-1-on)")]
    [InlineData("#16a34a", "var(--tm-signing-role-2-on)")]
    [InlineData("#dc2626", "var(--tm-signing-role-3-on)")]
    [InlineData("#9333ea", "var(--tm-signing-role-4-on)")]
    [InlineData("#0891b2", "var(--tm-signing-role-5-on)")]
    [InlineData("#a16207", "var(--tm-signing-role-6-on)")]
    public void ContrastTextFor_DefaultPaletteColour_MapsToItsOnToken(string color, string expected)
    {
        SigningRolePalette.ContrastTextFor(color).Should().Be(expected);
    }

    [Theory]
    [InlineData("#ca8a04")]
    [InlineData("#CA8A04")]
    public void ContrastTextFor_LegacyRole6Colour_MapsToTheRole6OnToken(string color)
    {
        // Stored templates still carry the pre-phase-1 role-6 colour; its ink follows the token.
        SigningRolePalette.ContrastTextFor(color).Should().Be("var(--tm-signing-role-6-on)");
    }

    [Fact]
    public void ContrastTextFor_NullColour_RidesTheOnPrimaryToken()
    {
        // The pill without a colour paints the primary fill; the theme resolves its ink.
        SigningRolePalette.ContrastTextFor(null).Should().Be("var(--tm-color-on-primary)");
        SigningRolePalette.ContrastTextFor(string.Empty).Should().Be("var(--tm-color-on-primary)");
    }

    [Theory]
    [InlineData("var(--tm-signing-role-6)")]
    [InlineData("not-a-colour")]
    public void ContrastTextFor_UnparsableColour_RidesTheOnPrimaryToken(string color)
    {
        // An unparsable value paints nothing custom: the primary fallback fill is the effective
        // paint, so its on-colour token is the ink (never a baked hex).
        SigningRolePalette.ContrastTextFor(color).Should().Be("var(--tm-color-on-primary)");
    }

    [Theory]
    [InlineData("#000000", "var(--tm-color-on-primary-fixed)", false)]
    [InlineData("#ffffff", "var(--tm-signing-role-ink-dark)", true)]
    [InlineData("#1f2937", "var(--tm-color-on-primary-fixed)", false)]
    [InlineData("#fef3c7", "var(--tm-signing-role-ink-dark)", true)]
    public void ContrastTextFor_CustomColour_ReturnsAToneToken_NeverAHex(
        string color,
        string expectedInk,
        bool expectedLightPaint)
    {
        // Custom colours still decide the tone, but the return is a token name — never a baked hex
        // the dark/indigo themes could redefine (or that C# would have to keep in sync with CSS).
        SigningRolePalette.ContrastTextFor(color).Should().Be(expectedInk);
        SigningRolePalette.ContrastTextFor(color).Should().NotContain("#");
        SigningRolePalette.IsLightPaint(color).Should().Be(expectedLightPaint);
    }

    [Fact]
    public void ContrastTextFor_DefaultPaletteColours_ComputeNoInlineHex()
    {
        // A palette colour must never carry the computed ink: the token (which flips with the
        // theme) is the single source of the text colour.
        foreach (var color in SigningRolePalette.DefaultColors)
        {
            SigningRolePalette.ContrastTextFor(color).Should().StartWith("var(--tm-signing-role-");
            SigningRolePalette.IsLightPaint(color).Should().BeNull(
                "the pill style carries the token ink — no tone class is needed");
        }
    }

    /// <summary>WCAG 2.1 relative contrast ratio of two hex colours.</summary>
    private static double ContrastRatio(string hexA, string hexB)
    {
        var a = Luminance(hexA);
        var b = Luminance(hexB);
        var lighter = Math.Max(a, b);
        var darker = Math.Min(a, b);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance(string hex)
    {
        static double Linearize(int channel)
        {
            var value = channel / 255.0;
            return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Linearize(int.Parse(hex.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture))
            + 0.7152 * Linearize(int.Parse(hex.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture))
            + 0.0722 * Linearize(int.Parse(hex.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    /// <summary>A <c>--tm-signing-role-N: … / --tm-signing-role-N-on: …</c> pair parsed from a theme block.</summary>
    private sealed record RoleTokenPair(int Index, string Paint, string Ink);

    private static readonly Regex PairRegex = new(
        @"--tm-signing-role-(?<index>\d+)(?<on>-on)?\s*:\s*(?<value>#[0-9a-fA-F]{3,8})",
        RegexOptions.Compiled, TimeSpan.FromSeconds(5));

    /// <summary>The full text of the theme block that follows <paramref name="selector"/> in the
    /// Signing token file (e.g. the dark block starts at <c>[data-theme="dark"]</c>).</summary>
    private static string ThemeBlock(string css, string selector)
    {
        var start = css.IndexOf(selector, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"tokens-signing.css must declare a {selector} block");
        var open = css.IndexOf('{', start);
        var depth = 0;
        var close = open;
        for (var i = open; i < css.Length; i++)
        {
            if (css[i] == '{')
            {
                depth++;
            }
            else if (css[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    close = i;
                    break;
                }
            }
        }

        return css[(open + 1)..close];
    }

    private static List<RoleTokenPair> ParsePairs(string block)
    {
        return PairRegex.Matches(block)
            .Select(match => new RoleTokenPair(
                int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture),
                match.Groups["on"].Success ? string.Empty : match.Groups["value"].Value,
                match.Groups["on"].Success ? match.Groups["value"].Value : string.Empty))
            .ToList();
    }

    private static string TokensSigningCss()
    {
        var path = Path.Combine(
            ThemeCss.RepositoryRoot().FullName, "src", "Tempo.Blazor.Signing", "wwwroot", "css", "tokens-signing.css");
        File.Exists(path).Should().BeTrue("the Signing token file must exist");
        return ThemeCss.StripComments(File.ReadAllText(path));
    }

    [Fact]
    public void TokensSigning_LightRoleInkPairs_HoldAtLeast45To1()
    {
        var css = TokensSigningCss();
        var pairs = ParsePairs(ThemeBlock(css, ":root"));

        pairs.Should().HaveCount(SigningRolePalette.DefaultColors.Count * 2,
            "the light theme declares a paint and an -on ink for every role");
        VerifyPairs(pairs);
    }

    [Fact]
    public void TokensSigning_DarkRoleInkPairs_HoldAtLeast45To1()
    {
        var css = TokensSigningCss();
        var pairs = ParsePairs(ThemeBlock(css, "[data-theme=\"dark\"]"));

        pairs.Should().HaveCount(SigningRolePalette.DefaultColors.Count * 2,
            "the dark theme declares a paint and an -on ink for every role");
        VerifyPairs(pairs);
    }

    [Fact]
    public void TokensSigning_LightIslandRoleInkPairs_HoldAtLeast45To1()
    {
        var css = TokensSigningCss();
        var pairs = ParsePairs(ThemeBlock(css, "[data-theme=\"light\"]"));

        pairs.Should().HaveCount(SigningRolePalette.DefaultColors.Count * 2,
            "the forced-light island re-declares the light pairs for a dark page");
        VerifyPairs(pairs);
    }

    /// <summary>
    /// <c>--tm-signing-role-ink-dark</c> is the near-black ink of a LIGHT custom role paint. Dark and
    /// indigo themes redefine <c>--tm-color-gray-900</c> to white, so this token must be declared on
    /// <c>:root</c> only — a redefinition would paint white text on a light custom pill.
    /// </summary>
    [Fact]
    public void SigningRoleInkDark_IsDeclaredOnRoot_AndNeverRedefinedByATheme()
    {
        const string token = "--tm-signing-role-ink-dark";
        var signing = TokensSigningCss();
        ThemeBlock(signing, ":root").Should().Contain($"{token}:",
            "the light custom-paint ink is a Signing token, declared once on :root");

        foreach (var selector in new[] { "[data-theme=\"dark\"]", "[data-theme=\"light\"]" })
        {
            ThemeBlock(signing, selector).Should().NotContain(token,
                "{0} must not redefine the fixed near-black ink", selector);
        }

        var root = ThemeCss.RepositoryRoot().FullName;
        foreach (var relative in new[]
        {
            Path.Combine("src", "Tempo.Blazor", "wwwroot", "css", "tokens-dark.css"),
            Path.Combine("src", "Tempo.Blazor", "wwwroot", "css", "theme-indigo.css")
        })
        {
            var css = ThemeCss.StripComments(File.ReadAllText(Path.Combine(root, relative)));
            css.Should().NotContain(token,
                "{0} redefines --tm-color-gray-900; it must not touch the fixed signing ink", relative);
        }
    }

    [Fact]
    public void SigningRoleInkDark_OnALightCustomPaint_HoldsAtLeast45To1()
    {
        // #fef3c7 is a light custom paint the tone class maps onto the fixed near-black ink.
        // The token value is parsed from :root so the assertion follows the stylesheet, not a copy.
        var root = ThemeBlock(TokensSigningCss(), ":root");
        var ink = Regex.Match(root, @"--tm-signing-role-ink-dark\s*:\s*(?<value>#[0-9a-fA-F]{6})", RegexOptions.None, RegexTimeout)
            .Groups["value"].Value;
        ink.Should().NotBeNullOrEmpty("the :root declaration carries the concrete near-black ink");

        ContrastRatio("#fef3c7", ink).Should().BeGreaterThanOrEqualTo(4.5);
        ContrastRatio("#ffffff", ink).Should().BeGreaterThanOrEqualTo(4.5);
    }

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Every declared paint/ink pair of one theme block re-computes to ≥ 4.5:1, and the
    /// ink differs from a naive same-value pair (the tokens decide, not the paint).</summary>
    private static void VerifyPairs(IReadOnlyList<RoleTokenPair> pairs)
    {
        foreach (var group in pairs.GroupBy(pair => pair.Index).OrderBy(group => group.Key))
        {
            var paint = group.FirstOrDefault(pair => pair.Paint.Length > 0);
            var ink = group.FirstOrDefault(pair => pair.Ink.Length > 0);
            paint.Should().NotBeNull($"role {group.Key} declares its paint in this theme");
            ink.Should().NotBeNull($"role {group.Key} declares its -on ink in this theme");

            var ratio = ContrastRatio(paint!.Paint, ink!.Ink);
            ratio.Should().BeGreaterThanOrEqualTo(4.5,
                "role {0}: {1} on {2} must keep the WCAG AA text ratio (got {3:F2})",
                group.Key, ink.Ink, paint.Paint, ratio);
        }
    }
}
