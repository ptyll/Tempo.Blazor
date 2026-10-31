using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Component-surface contrast facts of <see cref="TokenContrastTests"/> (state focus rings, the
/// phase-11 chrome, the TmGantt phase-2 repaint), split out to keep every source file under the
/// 1200-line ceiling (SourceFileLengthTests, N270/F13).
/// </summary>
public sealed partial class TokenContrastTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void StateFocusRings_HoldThreeToOne_AgainstTheSurface(bool indigo, bool dark)
    {
        var tokens = indigo ? IndigoTokens(dark) : ThemeCss.TokenGraph(dark);
        var surface = ThemeCss.ResolveColour("var(--tm-bg-surface)", tokens);

        foreach (var ring in new[] { "--tm-focus-ring-danger", "--tm-focus-ring-success" })
        {
            ThemeCss.Contrast(ThemeCss.LastShadowColour(tokens[ring], tokens), surface)
                .Should().BeGreaterThanOrEqualTo(3,
                    "{0} je non-text indikátor a musí držet 3:1 proti povrchu ({1} {2})",
                    ring, indigo ? "indigo" : "default", dark ? "dark" : "light");
        }
    }

    /// <summary>
    /// TmGantt phase 2: the status chips and the priority glyphs are painted on the Gantt's own
    /// surfaces - the card (<c>--tm-bg-surface</c>) and the workspace behind it
    /// (<c>--tm-bg-workspace</c>, which only the indigo theme moves) - so the F0 tokens are measured in
    /// that context in light, dark and both indigo variants. Chip text holds AA on its fill; a priority
    /// glyph holds the 3:1 a non-text mark needs against either surface.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void GanttStatusAndPriority_HoldContrast_OnTheGanttSurfaces(bool indigo, bool dark)
    {
        var tokens = indigo ? IndigoTokens(dark) : ThemeCss.TokenGraph(dark);
        var theme = $"{(indigo ? "indigo" : "default")} {(dark ? "dark" : "light")}";

        foreach (var status in new[] { "open", "inprogress", "done", "closed" })
        {
            var fill = PaintedColour($"var(--tm-status-{status}-bg)", tokens);
            ThemeCss.Contrast(ThemeCss.ResolveColour($"var(--tm-status-{status}-fg)", tokens), fill)
                .Should().BeGreaterThanOrEqualTo(4.5, "{0} chip text on its fill ({1})", status, theme);
        }

        foreach (var surface in new[] { "var(--tm-bg-surface)", "var(--tm-bg-workspace)" })
        {
            var ground = ThemeCss.ResolveColour(surface, tokens);
            foreach (var priority in new[] { "lowest", "low", "medium", "high", "highest" })
            {
                ThemeCss.Contrast(ThemeCss.ResolveColour($"var(--tm-priority-{priority})", tokens), ground)
                    .Should().BeGreaterThanOrEqualTo(3, "{0} priority glyph on {1} ({2})", priority, surface, theme);
            }
        }
    }

    /// <summary>
    /// TmGantt phase 2 pairs painted by the redesigned markup: the workload cells (accent text on the
    /// primary wash, danger text on the danger wash), the "Today" pill (inverse ink on the danger fill)
    /// and the default bar label (fixed white on the solid primary).
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void GanttWorkloadTodayAndBarLabel_HoldAa(bool indigo, bool dark)
    {
        var tokens = indigo ? IndigoTokens(dark) : ThemeCss.TokenGraph(dark);
        var theme = $"{(indigo ? "indigo" : "default")} {(dark ? "dark" : "light")}";

        var pairs = new[]
        {
            ("workload in capacity", "var(--tm-color-primary-text)", "var(--tm-color-primary-subtle)"),
            ("workload overloaded", "var(--tm-color-danger-text)", "var(--tm-color-danger-subtle)"),
            ("today pill", "var(--tm-text-inverse)", "var(--tm-color-danger)"),
            ("bar label", "var(--tm-color-on-primary-fixed)", "var(--tm-color-primary-solid)"),
        };

        foreach (var (name, foreground, background) in pairs)
        {
            ThemeCss.Contrast(ThemeCss.ResolveColour(foreground, tokens), PaintedOnSurface(background, tokens))
                .Should().BeGreaterThanOrEqualTo(4.5, "{0} ({1})", name, theme);
        }
    }

    /// <summary>
    /// The colour a fill paints over <c>--tm-bg-surface</c>: an <c>rgb(from …)</c> wash goes through
    /// <see cref="PaintedColour"/>, a literal <c>rgba()</c> wash (the dark danger / info subtle) is
    /// composited from its own channels, an opaque token resolves as is.
    /// </summary>
    private static string PaintedOnSurface(string tokenExpr, Dictionary<string, string> tokens)
    {
        var resolved = ThemeCss.ResolveColour(tokenExpr, tokens);
        return resolved.StartsWith("rgba(", StringComparison.Ordinal)
            ? OverScrim(tokenExpr, tokens, ThemeCss.ResolveColour("var(--tm-bg-surface)", tokens))
            : PaintedColour(tokenExpr, tokens);
    }
}
