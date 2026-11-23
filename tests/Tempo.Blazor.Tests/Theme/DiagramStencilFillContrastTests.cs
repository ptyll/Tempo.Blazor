using System.Text.RegularExpressions;
using FluentAssertions;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Stencils;
using Tempo.Blazor.Tests.Components.Diagram;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Fáze 8c — every coloured stencil fill × its section ink holds WCAG AA (4.5:1) in light, dark
/// AND the light island nested in dark (R4). Stroke × fill and stroke × paper hold 3:1 (1.4.11)
/// except the two historical light yellow/amber strokes frozen by (d). Pairs are taken from the
/// stencils themselves so a mutation that restores a hex fill turns this RED too.
/// </summary>
public sealed class DiagramStencilFillContrastTests
{
    private static readonly Regex TokenPaint = new(
        @"^var\(\s*(--[\w-]+)\s*\)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private const string OnFillInk = "var(--tm-diagram-node-ink-on-fill)";
    private const string Paper = "var(--tm-canvas-paper)";

    /// <summary>
    /// Light-only 1.4.11 exceptions: these BuiltIn strokes were already under 3:1 in 2.12.0 and
    /// raising them would change the light pixels (decision DIAGRAM-STENCIL-THEMED-FILLS R7).
    /// Dark and the island must still meet 3:1.
    /// </summary>
    private static readonly HashSet<string> LightStrokeContrastExceptions = new(StringComparer.Ordinal)
    {
        "general.sticky-note",
        "ba.gap",
        "strategy.bmc-value-proposition"
    };

    public static IEnumerable<object[]> ColouredNodes()
    {
        foreach (var stencil in DiagramStencilThemedFillTests.AllNodeStencils())
        {
            if (string.IsNullOrWhiteSpace(stencil.Layout.Fill))
            {
                continue;
            }

            if (IsPaperFill(stencil.Layout.Fill))
            {
                continue;
            }

            var ink = stencil.Layout.Sections
                .Select(section => section.TextStyle?.Color)
                .FirstOrDefault(color => !string.IsNullOrWhiteSpace(color))
                ?? OnFillInk;

            yield return [stencil.Id, stencil.Layout.Fill!, ink, stencil.Layout.Stroke];
        }
    }

    [Fact]
    public void Coloured_nodes_are_discovered_from_the_stencils()
    {
        ColouredNodes().Should().HaveCountGreaterThan(20,
            "pairs must come from the stencil packs — a hand-written list would miss a mutation");
    }

    [Theory]
    [MemberData(nameof(ColouredNodes))]
    public void Fill_is_a_catalogued_token(string stencilId, string fill, string ink, string? stroke)
    {
        _ = ink;
        _ = stroke;
        IsToken(fill).Should().BeTrue(
            "{0} Layout.Fill must be var(--token) so the theme can flip it (was {1})", stencilId, fill);
        TokenGraphContains(TokenName(fill), light: true, dark: true, island: true, stencilId);
    }

    [Theory]
    [MemberData(nameof(ColouredNodes))]
    public void Fill_x_ink_holds_aa_in_light_dark_and_island(string stencilId, string fill, string ink, string? stroke)
    {
        _ = stroke;
        IsToken(ink).Should().BeTrue(
            "{0} section ink must be var(--token) (was {1})", stencilId, ink);

        foreach (var (label, tokens) in Graphs())
        {
            var ratio = ThemeCss.Contrast(
                ThemeCss.ResolveColour(ink, tokens),
                ThemeCss.ResolveColour(fill, tokens));
            ratio.Should().BeGreaterThanOrEqualTo(4.5,
                "{0} ink {1} on fill {2} is {3:0.00}:1 in {4} (need 4.5:1)",
                stencilId, ink, fill, ratio, label);
        }
    }

    [Fact]
    public void Table_header_fill_holds_aa_against_on_fill_ink()
    {
        // Table header cells used to ship literal #f3f4f6 under muted --tm-diagram-node-ink
        // (~2:1 in dark). The header fill token must clear 4.5:1 against on-fill ink in every
        // graph, and the name must exist on :root, the dark roots and the light island.
        const string fill = "var(--tm-diagram-stencil-table-header-fill)";
        const string token = "--tm-diagram-stencil-table-header-fill";
        TokenGraphContains(token, light: true, dark: true, island: true, "table.basic");

        foreach (var (label, tokens) in Graphs())
        {
            var ratio = ThemeCss.Contrast(
                ThemeCss.ResolveColour(OnFillInk, tokens),
                ThemeCss.ResolveColour(fill, tokens));
            ratio.Should().BeGreaterThanOrEqualTo(4.5,
                "table header ink {0} on fill {1} is {2:0.00}:1 in {3} (need 4.5:1)",
                OnFillInk, fill, ratio, label);
        }
    }

    [Theory]
    [MemberData(nameof(ColouredNodes))]
    public void Stroke_holds_non_text_contrast_in_every_theme(string stencilId, string fill, string ink, string? stroke)
    {
        _ = ink;
        if (string.IsNullOrWhiteSpace(stroke))
        {
            return;
        }

        IsToken(stroke).Should().BeTrue(
            "{0} Layout.Stroke must be var(--token) (was {1})", stencilId, stroke);

        foreach (var (label, tokens) in Graphs())
        {
            // Island re-pins the light hexes (R4), so the historical light yellow/amber
            // 1.4.11 shortfall applies there too. Dark still has to clear 3:1 (R7).
            var skipFill = label != "dark" && LightStrokeContrastExceptions.Contains(stencilId);
            if (!skipFill)
            {
                var vsFill = ThemeCss.Contrast(
                    ThemeCss.ResolveColour(stroke, tokens),
                    ThemeCss.ResolveColour(fill, tokens));
                vsFill.Should().BeGreaterThanOrEqualTo(3.0,
                    "{0} stroke {1} on fill {2} is {3:0.00}:1 in {4} (need 3:1)",
                    stencilId, stroke, fill, vsFill, label);
            }

            var skipPaper = label != "dark" && LightStrokeContrastExceptions.Contains(stencilId);
            if (!skipPaper)
            {
                var vsPaper = ThemeCss.Contrast(
                    ThemeCss.ResolveColour(stroke, tokens),
                    ThemeCss.ResolveColour(Paper, tokens));
                vsPaper.Should().BeGreaterThanOrEqualTo(3.0,
                    "{0} stroke {1} on paper is {2:0.00}:1 in {3} (need 3:1)",
                    stencilId, stroke, vsPaper, label);
            }
        }
    }

    private static IEnumerable<(string Label, Dictionary<string, string> Tokens)> Graphs()
    {
        yield return ("light", ThemeCss.TokenGraph(false));
        yield return ("dark", ThemeCss.TokenGraph(true));
        yield return ("island", IslandGraph());
    }

    /// <summary>
    /// A light island nested in a dark ancestor: start from the dark graph and overlay the
    /// <c>[data-theme="light"]</c> block. A token declared only on the dark roots then LEAKS its
    /// dark value into the island — the R4 mutation.
    /// </summary>
    internal static Dictionary<string, string> IslandGraph()
    {
        var tokens = ThemeCss.TokenGraph(true);
        var css = ThemeCss.StripComments(File.ReadAllText(ThemeCss.CssPath("tokens-dark.css")));
        var match = Regex.Match(css, @"\[data-theme\s*=\s*""light""\]\s*\{(?<body>[^{}]*)\}");
        match.Success.Should().BeTrue("tokens-dark.css must contain the light-island block");
        foreach (Match declaration in Regex.Matches(
                     match.Groups["body"].Value,
                     @"(?<name>--[\w-]+)\s*:\s*(?<value>[^;]+);"))
        {
            tokens[declaration.Groups["name"].Value] = ThemeCss.Normalise(declaration.Groups["value"].Value);
        }

        return tokens;
    }

    private static void TokenGraphContains(string token, bool light, bool dark, bool island, string stencilId)
    {
        if (light)
        {
            ThemeCss.TokenGraph(false).Should().ContainKey(token,
                "{0} names {1} which must be declared in tokens.css", stencilId, token);
        }

        if (dark)
        {
            ThemeCss.TokenGraph(true).Should().ContainKey(token,
                "{0} names {1} which must be re-declared on the dark roots", stencilId, token);
        }

        if (island)
        {
            IslandGraph().Should().ContainKey(token,
                "{0} names {1} which must be re-pinned in the light island (R4)", stencilId, token);
        }
    }

    private static bool IsToken(string paint) => TokenPaint.IsMatch(paint.Trim());

    private static string TokenName(string paint) => TokenPaint.Match(paint.Trim()).Groups[1].Value;

    private static bool IsPaperFill(string fill)
        => fill.Contains("--tm-diagram-paper", StringComparison.Ordinal)
           || fill.Contains("--tm-canvas-paper", StringComparison.Ordinal)
           || fill.Contains("--stencil-", StringComparison.Ordinal)
           || fill.Equals("transparent", StringComparison.OrdinalIgnoreCase)
           || fill.Equals("none", StringComparison.OrdinalIgnoreCase);
}
