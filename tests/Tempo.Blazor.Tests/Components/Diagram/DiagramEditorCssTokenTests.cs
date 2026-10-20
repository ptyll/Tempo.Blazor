using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Components.Diagram;

/// <summary>
/// Guards U13: the interaction-critical hardcoded values in the DiagramEditor stylesheet are
/// token-driven — the resize-handle hit area follows <c>--tm-space-6</c> / <c>--tm-touch-target</c>
/// (so a host rebranding touch targets rebrands the handles) and every floating surface (context
/// menu, view-menu panel, table-inserter popover) rides <c>var(--tm-z-popover)</c> with its
/// backdrop at <c>calc(var(--tm-z-popover) - 1)</c> — no three-digit-or-higher <c>z-index</c>
/// literal may exist anywhere in the package sources outside an explicit, reasoned allowlist.
/// </summary>
public class DiagramEditorCssTokenTests
{
    private const string DiagramEditorCssPath =
        "src/Tempo.Blazor.DiagramEditor/wwwroot/css/components/_diagram-editor.css";

    private const string EditorRazorPath =
        "src/Tempo.Blazor.DiagramEditor/Components/Diagram/TmDiagramEditor.razor";

    private static string Css() => ThemeCss.StripComments(File.ReadAllText(
        Path.Combine(ThemeCss.RepositoryRoot().FullName, DiagramEditorCssPath)));

    [Fact]
    public void DiagramEditorCss_HandleHitUsesSpaceAndTouchTokens()
    {
        var css = Css();

        var handles = System.Text.RegularExpressions.Regex.Match(
            css, @"\.tm-diagram-node-handles\s*\{([^}]*)\}");
        handles.Success.Should().BeTrue();
        ThemeCss.Normalise(handles.Groups[1].Value)
            .Should().Contain("--tm-diagram-handle-hit: var(--tm-space-6)");

        // The coarse-pointer override must come from the core touch-target token, not a 44px literal.
        var coarseHit = System.Text.RegularExpressions.Regex.Match(
            css, @"@media \(pointer: coarse\)\s*\{[^{]*\{[^{}]*--tm-diagram-handle-hit:\s*(?<value>[^;]+);");
        coarseHit.Success.Should().BeTrue("the coarse-pointer handle-hit override must stay");
        ThemeCss.Normalise(coarseHit.Groups["value"].Value)
            .Should().Be("var(--tm-touch-target)");
    }

    [Fact]
    public void DiagramEditorPackage_NoZIndexLiteralsOutsideTheTokenScale()
    {
        // Explicit allowlist of three-digit-or-higher z-index literals, per file, each with a
        // reason. Prefer tokenizing a new surface (var(--tm-z-popover) for menus/popovers,
        // calc(var(--tm-z-popover) - 1) for their backdrops) over adding an entry here.
        // Today the list is EMPTY: the press ring, the canvas lock badge and the arrow-select
        // menu all ride the token scale, so any match below is a regression.
        var allowlist = new Dictionary<string, (string Literal, string Reason)[]>(StringComparer.Ordinal)
        {
        };

        var root = Path.Combine(ThemeCss.RepositoryRoot().FullName, "src", "Tempo.Blazor.DiagramEditor");
        var literal = new System.Text.RegularExpressions.Regex(
            @"z-index\s*:\s*(?<value>\d{3,})",
            System.Text.RegularExpressions.RegexOptions.Compiled, TimeSpan.FromSeconds(5));

        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                     .Where(f => f.EndsWith(".css", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal))
                     .Where(f => !f.Replace('\\', '/').Contains("/obj/") && !f.Replace('\\', '/').Contains("/bin/")))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            var text = File.ReadAllText(file);
            foreach (System.Text.RegularExpressions.Match match in literal.Matches(text))
            {
                var value = match.Groups["value"].Value;
                allowlist.TryGetValue(relative, out var entries);
                entries.Should().Contain(e => e.Literal == value,
                    $"stacking order must come from the --tm-z-* tokens, not the literal 'z-index: {value}' " +
                    $"(in {relative}). Tokenize the surface or add an allowlisted entry WITH a reason.");
            }
        }
    }

    [Fact]
    public void DiagramEditorCss_PopoverSurfacesRideThePopoverToken()
    {
        var css = Css();

        Property(".tm-diagram-editor__view-menu-panel", "z-index").Should().Be("var(--tm-z-popover)");
        Property(".tm-diagram-editor__view-menu-backdrop", "z-index")
            .Should().Be("calc(var(--tm-z-popover) - 1)");
        Property(".tm-diagram-table-inserter-popover", "z-index").Should().Be("var(--tm-z-popover)");
        Property(".tm-diagram-press-ring", "z-index").Should().Be("var(--tm-z-popover)");

        var razor = File.ReadAllText(
            Path.Combine(ThemeCss.RepositoryRoot().FullName, EditorRazorPath));
        razor.Should().Contain("z-index:var(--tm-z-popover)",
            "the context menu is a fixed popover surface and rides the popover token");
        razor.Should().NotContain("z-index:var(--tm-z-dropdown)",
            "the context menu must not sit a band below other popovers");
    }

    [Fact]
    public void DiagramEditorCss_RestoredDeclarationsSurviveAsTokenValues()
    {
        // Regression guard (review fix): the U13 pass must convert in place, never delete —
        // every one of these declarations was previously dropped, silently regressing the UI.
        var css = Css();

        Property(".tm-diagram-table-inserter__cell", "width").Should().Be("var(--tm-space-4)");
        Property(".tm-diagram-table-inserter__cell", "height").Should().Be("var(--tm-space-4)");

        Property(".tm-diagram-template-card__category", "background")
            .Should().Be("var(--tm-color-primary-subtle)");

        Property(".tm-diagram-editor__statusbar", "border-top")
            .Should().Be("1px solid var(--tm-color-border)");

        Property(".tm-diagram-editor__canvas-wrap--rulers", "padding-left")
            .Should().Be("var(--tm-space-6)");

        // Review fix: the swimlane header band rides the package token so the painted header stays
        // in step with DiagramNodeSwimlaneData.HeaderSize = 30 (hit-testing and drop math measure 30px).
        Property(".tm-diagram-node__swimlane--horizontal .tm-diagram-node__swimlane-header", "width")
            .Should().Be("var(--tm-diagram-swimlane-header)");
        Property(".tm-diagram-node__swimlane--vertical .tm-diagram-node__swimlane-header", "height")
            .Should().Be("var(--tm-diagram-swimlane-header)");
        Property(".tm-diagram-canvas", "--tm-diagram-swimlane-header")
            .Should().Be("1.875rem");

        // The status-bar buttons keep their 30px footprint through the package token.
        Property(".tm-diagram-editor__statusbar .tm-diagram-editor__toolbar-group .tm-toolbar-btn", "height")
            .Should().Be("var(--tm-diagram-statusbar-btn)");
        Property(".tm-diagram-editor__statusbar", "--tm-diagram-statusbar-btn")
            .Should().Be("1.875rem");
    }

    /// <summary>The value of <paramref name="property"/> in the rule for <paramref name="selector"/>.</summary>
    private static string? Property(string selector, string property)
    {
        var css = Css();
        var rule = System.Text.RegularExpressions.Regex.Match(
            css, $@"{System.Text.RegularExpressions.Regex.Escape(selector)}\s*\{{(?<body>[^}}]*)\}}");
        rule.Success.Should().BeTrue($"a rule for '{selector}' must exist");
        foreach (var declaration in ThemeCss.Normalise(rule.Groups["body"].Value).Split(';'))
        {
            var separator = declaration.IndexOf(':');
            if (separator > 0 && declaration[..separator].Trim() == property)
                return declaration[(separator + 1)..].Trim();
        }

        return null;
    }
}
