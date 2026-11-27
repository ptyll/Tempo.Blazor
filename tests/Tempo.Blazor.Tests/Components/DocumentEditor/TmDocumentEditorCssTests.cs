using FluentAssertions;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

public class TmDocumentEditorCssTests
{
    [Fact]
    public void CssFiles_AreSplitImportedAndBundledByProject()
    {
        var root = FindRepositoryRoot();
        var cssRoot = Path.Combine(root, "src", "Tempo.Blazor.DocumentEditor", "wwwroot", "css");
        var mainCss = Path.Combine(cssRoot, "components", "_document-editor.css");
        var toolbarCss = Path.Combine(cssRoot, "components", "_document-editor-toolbar.css");
        var commentsCss = Path.Combine(cssRoot, "components", "_document-editor-comments.css");
        var entryCss = Path.Combine(cssRoot, "tempo-blazor-document-editor.css");
        var coreEntryCss = Path.Combine(root, "src", "Tempo.Blazor", "wwwroot", "css", "tempo-blazor.css");
        var projectFile = Path.Combine(root, "src", "Tempo.Blazor.DocumentEditor", "Tempo.Blazor.DocumentEditor.csproj");

        File.Exists(mainCss).Should().BeTrue();
        File.Exists(toolbarCss).Should().BeTrue();
        File.Exists(commentsCss).Should().BeTrue();

        var imports = File.ReadAllText(entryCss);
        imports.Should().Contain("@import \"components/_document-editor.css\";");
        imports.Should().Contain("@import \"components/_document-editor-toolbar.css\";");
        imports.Should().Contain("@import \"components/_document-editor-comments.css\";");
        imports.Should().Contain("@import \"components/_document-editor-find.css\";");

        var coreImports = File.ReadAllText(coreEntryCss);
        coreImports.Should().NotContain("@import \"components/_document-editor.css\";");
        coreImports.Should().NotContain("@import \"components/_document-editor-toolbar.css\";");
        coreImports.Should().NotContain("@import \"components/_document-editor-comments.css\";");

        var project = File.ReadAllText(projectFile);
        project.Should().Contain("<PackageId>Tempo.Blazor.DocumentEditor</PackageId>");
        project.Should().Contain("AngleSharp");
    }

    [Fact]
    public void CssFiles_CoverEditorStatesResponsivenessAndDarkMode()
    {
        // Phase 1 / DE-1.0: the contract is semantic (selectors, states, responsive hooks, dark-mode rules,
        // token-driven focus), never a pixel value. A redesign that retunes a width, breakpoint or shadow must
        // not break this test; one that drops a state, a dark rule or the focus ring must.
        var root = FindRepositoryRoot();
        var cssRoot = Path.Combine(root, "src", "Tempo.Blazor.DocumentEditor", "wwwroot", "css", "components");
        var componentRoot = Path.Combine(root, "src", "Tempo.Blazor.DocumentEditor", "Components", "DocumentEditor");
        var css = string.Concat(
            File.ReadAllText(Path.Combine(cssRoot, "_document-editor.css")),
            File.ReadAllText(Path.Combine(cssRoot, "_document-editor-toolbar.css")),
            File.ReadAllText(Path.Combine(cssRoot, "_document-editor-comments.css")),
            File.ReadAllText(Path.Combine(componentRoot, "TmDocumentCanvasEngineHost.razor.css")));
        var rules = CssRules.Parse(css);

        foreach (var selector in new[]
        {
            ".tm-document-editor__loading",
            ".tm-document-editor__empty",
            ".tm-document-editor__error",
            ".tm-document-editor--readonly",
            ".tm-document-editor__dirty",
            ".tm-document-editor__save-message",
            ".tm-document-canvas-engine-host",
            ".tm-document-canvas-engine-host__page",
            ".tm-document-canvas-selection-rect",
            ".tm-document-canvas-object-selection",
            ".tm-document-canvas-object-resize-handle",
            ".tm-document-canvas-revision-overlay__marker",
            ".tm-document-inline--comment-anchor",
            ".tm-document-comment-thread--selected",
            ".tm-document-version-panel__preview",
            ".tm-document-revision-panel__item",
            ".tm-document-revision-panel__action--accept",
            ".tm-document-revision-panel__action--reject",
            ".tm-document-revision-panel__batch-action--accept",
            ".tm-document-revision-panel__batch-action--reject",
            ".tm-document-revision-panel__action:disabled",
            ".tm-document-revision-panel__batch-action:disabled",
            ".tm-document-side-panel[data-panel-layout=\"docked-tabs\"]",
            ".tm-document-editor__ribbon-tab--active::after",
            ".tm-document-editor__track-toggle--on",
            ".tm-document-editor__track-toggle--off",
            ".tm-document-editor__track-toggle::before",
            ".tm-document-editor__track-toggle::after",
            ".tm-document-editor__ribbon-button"
        })
        {
            rules.Any(rule => rule.Selectors.Any(candidate => candidate.Contains(selector, StringComparison.Ordinal))).Should().BeTrue($"selector {selector} must keep a rule");
        }

        // Responsiveness: the editor adapts through a width media query or a container query, whichever the
        // current layout mechanism is - the exact breakpoint is deliberately not part of the contract.
        var adaptive = css.Contains("@container", StringComparison.Ordinal)
            || System.Text.RegularExpressions.Regex.IsMatch(css, @"@media\s*\(\s*max-width\s*:");
        adaptive.Should().BeTrue("the editor chrome must keep a responsive hook");
        rules.Any(rule => rule.Selectors.Contains(".tm-document-editor__ribbon-button") && rule.Body.Contains("inline-size", StringComparison.Ordinal))
            .Should().BeTrue("the ribbon buttons keep a sizing rule");
        rules.Any(rule => rule.Selectors.Contains(".tm-document-editor__comment-rail") && rule.Body.Contains("max-height", StringComparison.Ordinal))
            .Should().BeTrue("the comment rail stays height-bounded");
        rules.Any(rule => rule.Selectors.Any(selector => selector.StartsWith(".tm-document-editor__ribbon-group::after", StringComparison.Ordinal)) && rule.Body.Contains("content:", StringComparison.Ordinal))
            .Should().BeTrue("the ribbon group caption is rendered from a pseudo element");

        // Elevation comes from a shadow declaration on the editor surface; the geometry is free to change.
        rules.Any(rule => rule.Selectors.Contains(".tm-document-editor") && rule.Body.Contains("box-shadow:", StringComparison.Ordinal))
            .Should().BeTrue("the editor surface keeps its elevation");

        // Dark mode: each key surface either keeps a dedicated dark rule or paints only through semantic tokens
        // (tokens-dark.css re-points those, so a token-only rule IS dark-safe and a redesign may drop the override).
        foreach (var surface in new[]
        {
            ".tm-document-editor__page-surface",
            ".tm-document-editor__ribbon",
            ".tm-document-comment-thread",
            ".tm-document-diff__pane pre"
        })
        {
            var hasDarkRule = rules.Any(rule => rule.Selectors.Any(selector => selector.StartsWith("[data-theme=\"dark\"] " + surface, StringComparison.Ordinal)));
            var colourDeclarations = rules
                .Where(rule => rule.Selectors.Any(selector => selector == surface))
                .SelectMany(rule => rule.Body.Split(';'))
                .Select(declaration => declaration.Trim())
                .Where(declaration => System.Text.RegularExpressions.Regex.IsMatch(declaration, @"^(color|background(-color)?|border(-color)?)\s*:"))
                .ToList();
            var tokenOnly = colourDeclarations.Count > 0
                && colourDeclarations.All(declaration =>
                    !System.Text.RegularExpressions.Regex.IsMatch(declaration, @"#[0-9a-fA-F]{3,8}\b|rgba?\(|hsla?\(")
                    && System.Text.RegularExpressions.Regex.IsMatch(declaration, @"var\(--tm-|transparent|inherit|currentColor|none"));
            (hasDarkRule || tokenOnly).Should().BeTrue($"{surface} needs a dark-mode rule or token-only colours");
        }

        // Focus: some :focus / :focus-within / :focus-visible rule draws the ring through a semantic token
        // (an outline or a box-shadow ring), never a literal colour.
        rules.Any(rule => rule.Selectors.Any(selector => selector.Contains(":focus", StringComparison.Ordinal))
                && System.Text.RegularExpressions.Regex.IsMatch(
                    rule.Body,
                    @"(outline[\w-]*|box-shadow)\s*:[^;]*var\(--tm-(color-primary|color-focus[\w-]*|focus-ring[\w-]*|border-color-focus)\)"))
            .Should().BeTrue("keyboard focus must be visible through a semantic focus token");
    }

    [Fact]
    public void FindPanelCss_UsesOnlyDefinedSemanticTokensAndDarkSafeHighlights()
    {
        var root = FindRepositoryRoot();
        var findCss = File.ReadAllText(Path.Combine(
            root, "src", "Tempo.Blazor.DocumentEditor", "wwwroot", "css", "components", "_document-editor-find.css"));
        var rules = CssRules.Parse(findCss);

        // The phase-1 token migration: these aliases were never defined, so every fallback literal won.
        foreach (var undefined in new[] { "var(--tm-border,", "var(--tm-primary,", "var(--tm-hover,", "var(--tm-text,", "var(--tm-surface," })
        {
            findCss.Should().NotContain(undefined);
        }

        // The find / replace panel and the search highlights carry no colour literal and no pixel geometry.
        var scoped = rules
            .Where(rule => rule.Selectors.Any(selector =>
                selector.StartsWith(".tm-document-find-panel", StringComparison.Ordinal)
                || selector.StartsWith(".tm-wysiwyg-search-match", StringComparison.Ordinal)))
            .ToList();
        scoped.Should().NotBeEmpty();
        foreach (var rule in scoped)
        {
            var declarations = string.Join("; ", rule.Body.Split(';').Select(part => part.Trim()));
            System.Text.RegularExpressions.Regex.IsMatch(rule.Body, @"#[0-9a-fA-F]{3,8}\b").Should().BeFalse($"{string.Join(",", rule.Selectors)} must not hold a hex colour ({declarations})");
            rule.Body.Should().NotContain("rgba(", $"{string.Join(",", rule.Selectors)} must not hold an rgba literal");
            System.Text.RegularExpressions.Regex.IsMatch(rule.Body, @"(?<![\w.-])(?!1px\b)\d+(\.\d+)?px\b").Should().BeFalse($"{string.Join(",", rule.Selectors)} must use --tm-space-* / --tm-font-size-* instead of pixels (1px hairlines excepted)");
        }

        // The match highlights ride on the warning tokens; the active one uses the filled-badge pair whose dark
        // values live in tokens-dark.css, so the component stylesheet needs (and has) no dark-mode override.
        findCss.Should().NotContain("[data-theme=\"dark\"]", "component stylesheets carry no dark-mode override (strict token audit)");

        // The search marker look is declared in exactly ONE stylesheet and carries no primary glow.
        var cssRoot = Path.Combine(root, "src", "Tempo.Blazor.DocumentEditor", "wwwroot", "css", "components");
        var declaringFiles = Directory.GetFiles(cssRoot, "*.css")
            .Concat(Directory.GetFiles(Path.Combine(root, "src", "Tempo.Blazor.DocumentEditor", "Components", "DocumentEditor"), "*.css"))
            .Where(file => CssRules.Parse(File.ReadAllText(file)).Any(rule => rule.Selectors.Any(selector =>
                selector is ".tm-wysiwyg-search-match" or ".tm-wysiwyg-marker--search" or ".tm-wysiwyg-search-match--active" or ".tm-wysiwyg-marker--search-active")))
            .Select(Path.GetFileName)
            .ToList();
        declaringFiles.Should().ContainSingle().Which.Should().Be("_document-editor-find.css");
        var markerRule = rules.Single(rule => rule.Selectors.Contains(".tm-wysiwyg-marker--search"));
        markerRule.Selectors.Should().Contain(".tm-wysiwyg-search-match");
        markerRule.Body.Should().Contain("--tm-color-warning").And.NotContain("--tm-color-primary").And.NotContain("box-shadow");
        var activeMarker = rules.Single(rule => rule.Selectors.Contains(".tm-wysiwyg-marker--search-active"));
        activeMarker.Selectors.Should().Contain(".tm-wysiwyg-search-match--active");
        activeMarker.Body.Should().Contain("var(--tm-color-warning-strong)").And.Contain("var(--tm-text-inverse)").And.NotContain("box-shadow");

        // Narrow viewports: the panel clamps to the viewport and wraps its rows.
        CssRules.Body(rules, ".tm-document-find-panel").Should().Contain("max-width").And.Contain("100vw");
        CssRules.Body(rules, ".tm-document-find-panel__row").Should().Contain("flex-wrap");
        CssRules.Body(rules, ".tm-document-find-panel__options").Should().Contain("flex-wrap");
        var focus = CssRules.Body(rules, ".tm-document-find-panel__input:focus");
        focus.Should().Contain("var(--tm-focus-ring)").And.NotContain("--tm-shadow-focus", "the focus ring is the shared token, not the glow");
    }

    // Phase-1 interim contract: the hand-rolled scrim / centering CSS is deleted when phase 5 moves these dialogs
    // onto TmModal; this test is replaced there together with the rules it pins.
    [Fact]
    public void FloatingModals_HaveScrimCenteredContentAndNoFlowImpact()
    {
        var root = FindRepositoryRoot();
        var css = File.ReadAllText(Path.Combine(
            root, "src", "Tempo.Blazor.DocumentEditor", "wwwroot", "css", "components", "_document-editor.css"));
        var rules = CssRules.Parse(css);

        CssRules.Body(rules, ".tm-document-json-debug-modal").Should().Contain("position: fixed");
        CssRules.Body(rules, ".tm-document-json-debug-modal__backdrop").Should().Contain("var(--tm-bg-overlay)");
        var content = CssRules.Body(rules, ".tm-document-json-debug-modal__content");
        content.Should().Contain("max-height").And.Contain("overflow");
        CssRules.Body(rules, ".tm-document-json-debug-modal__section").Should().NotBeEmpty();
        CssRules.Body(rules, ".tm-document-json-debug-modal pre").Should().Contain("var(--tm-font-mono)");

        // Compare and version dialogs sit on a shared scrim, centered, and never take part in the document flow.
        var scrim = CssRules.Body(rules, ".tm-document-dialog-scrim");
        scrim.Should().Contain("position: fixed").And.Contain("var(--tm-bg-overlay)").And.Contain("place-items: center");
        scrim.Should().Contain("z-index: var(--tm-z-modal);", "the token is defined, so no literal fallback");
        CssRules.Body(rules, ".tm-document-json-debug-modal").Should().Contain("z-index: var(--tm-z-modal);");
        // 390 px: the version / compare dialog is not stretched by the generic fixed rule.
        CssRules.Body(rules, ".tm-document-version-dialog").Should().Contain("align-content: start");
        CssRules.Body(rules, ".tm-document-dialog-scrim > .tm-document-version-dialog").Should().Contain("position: relative").And.Contain("max-height");
        CssRules.Body(rules, ".tm-document-version-dialog__actions").Should().Contain("align-items: center");
        CssRules.Body(rules, ".tm-document-compare-dialog").Should().Contain("max-height").And.Contain("overflow");
        CssRules.Body(rules, ".tm-document-compare-dialog__sources").Should().Contain("grid-template-columns");
    }

    [Fact]
    public void PageLayoutInspector_RootRuleHasOneSource()
    {
        var root = FindRepositoryRoot();
        var mainCss = File.ReadAllText(Path.Combine(
            root, "src", "Tempo.Blazor.DocumentEditor", "wwwroot", "css", "components", "_document-editor.css"));
        var scoped = File.ReadAllText(Path.Combine(
            root, "src", "Tempo.Blazor.DocumentEditor", "Components", "DocumentEditor", "TmDocumentEditorToolbar.razor.css"));

        CssRules.Parse(mainCss)
            .Count(rule => rule.Selectors.Contains(".tm-document-page-layout-inspector"))
            .Should().Be(1, "the root rule lives in one place (the shared stylesheet)");
        CssRules.Parse(scoped)
            .Any(rule => rule.Selectors.Contains(".tm-document-page-layout-inspector"))
            .Should().BeFalse("the scoped toolbar stylesheet must not redeclare the root rule");
    }

    [Fact]
    public void CssFiles_UseStrictLayeredImageLayoutWithoutWysiwygFloatFallback()
    {
        var root = FindRepositoryRoot();
        var css = string.Concat(
            File.ReadAllText(Path.Combine(root, "src", "Tempo.Blazor.DocumentEditor", "wwwroot", "css", "components", "_document-editor.css")),
            File.ReadAllText(Path.Combine(root, "src", "Tempo.Blazor.DocumentEditor", "Components", "DocumentEditor", "TmDocumentCanvasEngineHost.razor.css")));

        css.Should().Contain(".tm-document-canvas-engine-host__canvas");
        css.Should().Contain(".tm-document-canvas-selection-rect");
        css.Should().Contain(".tm-document-canvas-object-selection");
        css.Should().Contain(".tm-document-canvas-object-resize-handle");
        css.Should().Contain(".tm-document-canvas-table-resize-preview");
        css.Should().Contain("@media (max-width: 40rem)");
        css.Should().NotContain(".tm-wysiwyg-image--wrap-square");
        css.Should().NotContain(".tm-wysiwyg-image-sidecar-text");
        css.Should().NotContain("data-wrap-sidecar-for");
    }

    [Fact]
    public void CssFiles_ContainCanvasRestrictedEditingMarkers()
    {
        var root = FindRepositoryRoot();
        // Fáze 15: ribbon styly (vč. --active markeru) žijí v _document-editor-toolbar.css —
        // legacy ribbon blok v _document-editor.css byl odstraněn.
        var css = string.Concat(
            File.ReadAllText(Path.Combine(root, "src", "Tempo.Blazor.DocumentEditor", "wwwroot", "css", "components", "_document-editor.css")),
            File.ReadAllText(Path.Combine(root, "src", "Tempo.Blazor.DocumentEditor", "wwwroot", "css", "components", "_document-editor-toolbar.css")),
            File.ReadAllText(Path.Combine(root, "src", "Tempo.Blazor.DocumentEditor", "Components", "DocumentEditor", "TmDocumentCanvasEngineHost.razor.css")));

        css.Should().Contain(".tm-document-editor__ribbon-button--active");
        css.Should().Contain(".tm-document-canvas-engine-host--readonly");
        css.Should().Contain(".tm-document-canvas-engine-host__input");
        css.Should().Contain("cursor: not-allowed;");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }

    private sealed record CssRule(IReadOnlyList<string> Selectors, string Body);

    private static class CssRules
    {
        public static List<CssRule> Parse(string css)
        {
            var withoutComments = System.Text.RegularExpressions.Regex.Replace(css, @"/\*.*?\*/", string.Empty, System.Text.RegularExpressions.RegexOptions.Singleline);
            var result = new List<CssRule>();
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(withoutComments, @"([^{}]+)\{([^{}]*)\}"))
            {
                var prelude = match.Groups[1].Value.Trim();
                if (prelude.StartsWith('@'))
                {
                    continue;
                }

                var selectors = prelude.Split(',').Select(selector => selector.Trim()).Where(selector => selector.Length > 0).ToList();
                result.Add(new CssRule(selectors, match.Groups[2].Value));
            }

            return result;
        }

        public static string Body(IEnumerable<CssRule> rules, string selector)
            => string.Concat(rules.Where(rule => rule.Selectors.Contains(selector)).Select(rule => rule.Body));
    }
}
