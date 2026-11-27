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
            File.ReadAllText(Path.Combine(cssRoot, "_document-editor-canvas-overlays.css")),
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
            // Phase 2: the package carries no manual dark block any more (ChromeStylesheets_PaintOnlyThrough...), so the
            // contract is token-only colours - a surface paints through semantic tokens (or none / transparent / 0),
            // never a colour literal, and tokens-dark.css flips it.
            var colourDeclarations = rules
                .Where(rule => rule.Selectors.Any(selector => selector == surface))
                .SelectMany(rule => rule.Body.Split(';'))
                .Select(declaration => declaration.Trim())
                .Where(declaration => System.Text.RegularExpressions.Regex.IsMatch(declaration, @"^(color|background(-color)?|border(-color)?)\s*:"))
                .ToList();
            var tokenOnly = colourDeclarations.Count > 0
                && colourDeclarations.All(declaration =>
                    !System.Text.RegularExpressions.Regex.IsMatch(declaration, @"#[0-9a-fA-F]{3,8}\b|rgba?\(|hsla?\(")
                    && System.Text.RegularExpressions.Regex.IsMatch(declaration, @"var\(--tm-|transparent|inherit|currentColor|none|:\s*0\s*$"));
            tokenOnly.Should().BeTrue($"{surface} needs token-only colours");
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

    // ── Phase 2 / DE-2.1: tokens, visual layer and the shared panel building blocks ─────────────────────────

    private static readonly string[] ChromeStylesheets =
    [
        "_document-editor.css",
        "_document-editor-toolbar.css",
        "_document-editor-comments.css",
        "_document-editor-find.css",
        "_document-editor-panels.css",
        "_document-editor-canvas-overlays.css"
    ];

    private static string ReadComponentCss(string fileName)
    {
        var root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(
            root, "src", "Tempo.Blazor.DocumentEditor", "wwwroot", "css", "components", fileName));
    }

    [Fact]
    public void NoDecorativeGradients_AndRadiusTokens()
    {
        var main = CssRules.Parse(ReadComponentCss("_document-editor.css"));

        // The editor root and the workspace are flat: the workspace paints the semantic workspace token, the root a
        // surface token, and neither carries a (radial or linear) gradient with the primary colour.
        foreach (var selector in new[] { ".tm-document-editor", ".tm-document-editor__workspace" })
        {
            var body = CssRules.Body(main, selector);
            body.Should().NotContain("radial-gradient", $"{selector} must not paint a decorative gradient");
            body.Should().NotContain("linear-gradient", $"{selector} must not paint a decorative gradient");
        }

        CssRules.Body(main, ".tm-document-editor__workspace").Should().Contain("var(--tm-bg-workspace)");
        CssRules.Body(main, ".tm-document-editor").Should().Contain("var(--tm-radius-card)").And.Contain("var(--tm-shadow-card)");

        // Chrome radii are the two semantic steps: cards (root, panels, dialogs, menus) and controls (buttons, inputs).
        // Content geometry (the rendered page, image, token chip, WYSIWYG fallback surface) keeps its own radii.
        var contentSelector = new System.Text.RegularExpressions.Regex(
            @"wysiwyg|tm-document-image(?!-(editor__fields|dialog|inspector|insert-menu|wrap-panel))|inline--token|token-chip|cursor__label|table-editor__cell|diff__pane|paste-error|canvas-");
        var offenders = new List<string>();
        foreach (var fileName in ChromeStylesheets)
        {
            foreach (var rule in CssRules.Parse(ReadComponentCss(fileName)))
            {
                if (rule.Selectors.All(selector => contentSelector.IsMatch(selector)))
                {
                    continue;
                }

                foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(
                             rule.Body, @"border-radius\s*:\s*var\(--tm-radius-(sm|md|lg|xl)\)"))
                {
                    offenders.Add($"{fileName}: {string.Join(", ", rule.Selectors)} -> {match.Value}");
                }
            }
        }

        offenders.Should().BeEmpty("chrome radii are --tm-radius-card / --tm-radius-control");
    }

    [Fact]
    public void ChromeStylesheets_PaintOnlyThroughSemanticTokens_WithoutManualDarkBlocksOrLiteralFallbacks()
    {
        foreach (var fileName in ChromeStylesheets)
        {
            var css = System.Text.RegularExpressions.Regex.Replace(
                ReadComponentCss(fileName), @"/\*.*?\*/", string.Empty, System.Text.RegularExpressions.RegexOptions.Singleline);

            css.Should().NotContain("[data-theme=\"dark\"]", $"{fileName}: dark mode is carried by the token layer, not a manual block");
            css.Should().NotContain(".tm-dark", $"{fileName}: dark mode is carried by the token layer, not a manual block");
            System.Text.RegularExpressions.Regex.IsMatch(css, @"var\(--tm-[\w-]+\s*,\s*(#|rgb|hsl)")
                .Should().BeFalse($"{fileName}: no colour fallback literal inside var()");
        }
    }

    [Fact]
    public void HighlightTokens_MapToSemanticTokens_ForLightAndDark()
    {
        var rules = CssRules.Parse(ReadComponentCss("_document-editor.css"));
        var root = CssRules.Body(rules, ".tm-document-editor");

        foreach (var name in new[] { "match", "match-active", "insert", "delete", "change" })
        {
            var declaration = System.Text.RegularExpressions.Regex.Match(root, $@"--tm-document-highlight-{name}\s*:\s*([^;]+);");
            declaration.Success.Should().BeTrue($"--tm-document-highlight-{name} is declared on the editor root");
            declaration.Groups[1].Value.Should().Contain("var(--tm-", "a highlight maps onto a core semantic token, which flips with the theme");
            System.Text.RegularExpressions.Regex.IsMatch(declaration.Groups[1].Value, @"#[0-9a-fA-F]{3,8}\b|rgba?\(|hsla?\(")
                .Should().BeFalse($"--tm-document-highlight-{name} carries no colour literal");
        }
    }

    [Fact]
    public void PanelsCss_IsSplitImportedAndDeclaresTheInspectorFormPattern()
    {
        var root = FindRepositoryRoot();
        var entry = File.ReadAllText(Path.Combine(
            root, "src", "Tempo.Blazor.DocumentEditor", "wwwroot", "css", "tempo-blazor-document-editor.css"));
        entry.Should().Contain("@import \"components/_document-editor-panels.css\";");

        var css = ReadComponentCss("_document-editor-panels.css");
        var rules = CssRules.Parse(css);

        foreach (var selector in new[]
        {
            ".tm-document-inspector-header",
            ".tm-document-inspector-header__title",
            ".tm-document-inspector-header__close",
            ".tm-document-inspector-form",
            ".tm-document-inspector-form__section",
            ".tm-document-inspector-form__section-toggle",
            ".tm-document-inspector-form__chevron",
            ".tm-document-inspector-form__field",
            ".tm-document-inspector-form__suffix",
            ".tm-document-inspector-form__segmented",
            ".tm-document-inspector-form--embedded"
        })
        {
            rules.Any(rule => rule.Selectors.Any(candidate => candidate.StartsWith(selector, StringComparison.Ordinal)))
                .Should().BeTrue($"{selector} must keep a rule");
        }

        // Label above the field by default, label beside it once the panel body is wide enough: a container query
        // on the panel body, never the viewport.
        css.Should().Contain("@container");
        System.Text.RegularExpressions.Regex.IsMatch(css, @"container-type\s*:\s*inline-size").Should().BeTrue();

        // The embedded modifier drops the card chrome (frame + shadow) for a panel hosted inside the inspector.
        var embedded = CssRules.Body(rules, ".tm-document-inspector-form--embedded");
        embedded.Should().Contain("box-shadow: none").And.Contain("border: 0");

        // The section disclosure rotates a chevron icon instead of swapping a "+" / "-" glyph.
        CssRules.Body(rules, ".tm-document-inspector-form__chevron").Should().Contain("transition");
        CssRules.Body(rules, ".tm-document-inspector-form__section-toggle[aria-expanded=\"true\"] .tm-document-inspector-form__chevron")
            .Should().Contain("rotate");
    }

    [Fact]
    public void ActiveTabs_AreMarkedByAPrimaryUnderline()
    {
        var main = CssRules.Parse(ReadComponentCss("_document-editor.css"));
        var toolbar = CssRules.Parse(ReadComponentCss("_document-editor-toolbar.css"));

        var sideTab = string.Concat(main
            .Where(rule => rule.Selectors.Any(selector => selector.StartsWith(".tm-document-side-panel__tab--active", StringComparison.Ordinal)))
            .Select(rule => rule.Body));
        sideTab.Should().MatchRegex(@"(border-bottom|box-shadow)[^;]*var\(--tm-color-primary\)", "the active side-panel tab is underlined in the primary colour");

        var ribbonTab = string.Concat(toolbar
            .Where(rule => rule.Selectors.Any(selector => selector.StartsWith(".tm-document-editor__ribbon-tab--active::after", StringComparison.Ordinal)))
            .Select(rule => rule.Body));
        ribbonTab.Should().Contain("var(--tm-color-primary)");
    }

    [Fact]
    public void Shadows_NeverMixGrayTokens_BecauseGray900IsWhiteInDarkMode()
    {
        var offenders = new List<string>();
        foreach (var fileName in ChromeStylesheets)
        {
            foreach (var rule in CssRules.Parse(ReadComponentCss(fileName)))
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(rule.Body, @"box-shadow\s*:[^;]*--tm-color-gray-"))
                {
                    offenders.Add($"{fileName}: {string.Join(", ", rule.Selectors)}");
                }
            }
        }

        offenders.Should().BeEmpty("the gray scale flips in dark mode (gray-900 is white there); shadows use --tm-shadow-*");
    }

    [Fact]
    public void CanvasOverlayMarkers_AreGlobal_AndUseTheHighlightTokensWithoutFallbacks()
    {
        var css = ReadComponentCss("_document-editor-canvas-overlays.css");
        System.Text.RegularExpressions.Regex.Replace(css, @"/\*.*?\*/", string.Empty, System.Text.RegularExpressions.RegexOptions.Singleline)
            .Should().NotContain("::deep");
        var rules = CssRules.Parse(css);

        CssRules.Body(rules, ".tm-document-canvas-engine-host .tm-document-canvas-revision-overlay__marker--insertion")
            .Should().Contain("var(--tm-document-highlight-insert)");
        CssRules.Body(rules, ".tm-document-canvas-engine-host .tm-document-canvas-revision-overlay__marker--deletion")
            .Should().Contain("var(--tm-document-highlight-delete)");
        CssRules.Body(rules, ".tm-document-canvas-engine-host .tm-document-canvas-revision-overlay__marker--formatting")
            .Should().Contain("var(--tm-document-highlight-change)");

        foreach (var file in new[] { "_document-editor-canvas-overlays.css", "_document-editor-find.css" })
        {
            System.Text.RegularExpressions.Regex.IsMatch(ReadComponentCss(file), @"var\(--tm-document-highlight-[a-z-]+\s*,")
                .Should().BeFalse($"{file}: the highlight tokens are declared on .tm-document-editor and need no fallback");
        }

        // Generic -> specific: the base marker precedes the kinds, the state modifiers come last.
        var order = new[]
        {
            ".tm-document-canvas-revision-overlay__marker {",
            ".tm-document-canvas-revision-overlay__marker--insertion {",
            ".tm-document-canvas-revision-overlay__marker--deletion {",
            ".tm-document-canvas-revision-overlay__marker--formatting {",
            ".tm-document-canvas-revision-overlay__marker--simple {",
            ".tm-document-canvas-revision-overlay__marker--selected {"
        }.Select(marker => css.IndexOf(marker, StringComparison.Ordinal)).ToList();
        order.Should().OnlyContain(index => index >= 0).And.BeInAscendingOrder();

        var entry = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "Tempo.Blazor.DocumentEditor", "wwwroot", "css", "tempo-blazor-document-editor.css"));
        entry.Should().Contain("@import \"components/_document-editor-canvas-overlays.css\";");
    }

    // Frozen: the dead functional ::deep(...) rules of the canvas host. The list may only shrink; a new rule must use the
    // combinator form ("host ::deep .child") or live in a global stylesheet.
    private const int FrozenFunctionalDeepRulesInCanvasHost = 44;

    [Fact]
    public void ScopedCss_UsesDeepCombinator_NotFunctionalDeep()
    {
        var root = FindRepositoryRoot();
        var componentRoot = Path.Combine(root, "src", "Tempo.Blazor.DocumentEditor", "Components");
        foreach (var file in Directory.EnumerateFiles(componentRoot, "*.razor.css", SearchOption.AllDirectories))
        {
            var count = System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(file), @"::deep\(").Count;
            var allowed = Path.GetFileName(file) == "TmDocumentCanvasEngineHost.razor.css" ? FrozenFunctionalDeepRulesInCanvasHost : 0;
            count.Should().BeLessThanOrEqualTo(allowed,
                $"{Path.GetFileName(file)}: ::deep(.x) compiles to an invalid selector and never applies - write '.host ::deep .x'");
        }
    }

    [Fact]
    public void PanelContainers_HostingButtons_HaveNoBareTypeSelectorRules()
    {
        // A bare "span" / "div" under a panel out-specified the ink of every .tm-btn label inside it (the primary
        // "Create version" label rendered muted grey on the primary fill).
        var offenders = new List<string>();
        foreach (var fileName in ChromeStylesheets)
        {
            foreach (var rule in CssRules.Parse(ReadComponentCss(fileName)))
            {
                foreach (var selector in rule.Selectors)
                {
                    if (System.Text.RegularExpressions.Regex.IsMatch(selector, @"^\.tm-document-[\w-]*(panel|rail)[\w-]* (span|div|p|a)$"))
                    {
                        offenders.Add($"{fileName}: {selector}");
                    }
                }
            }
        }

        offenders.Should().BeEmpty();
        CssRules.Body(CssRules.Parse(ReadComponentCss("_document-editor.css")), ".tm-document-panel__meta")
            .Should().Contain("var(--tm-color-text-muted)");
    }

    [Fact]
    public void CanvasWorkspace_PaintsTheWorkspaceToken_NotAPrimaryTint()
    {
        var root = FindRepositoryRoot();
        var host = File.ReadAllText(Path.Combine(
            root, "src", "Tempo.Blazor.DocumentEditor", "Components", "DocumentEditor", "TmDocumentCanvasEngineHost.razor.css"));

        System.Text.RegularExpressions.Regex.Matches(host, @"--tm-document-canvas-workspace-background\s*:\s*([^;]+);")
            .Select(match => match.Groups[1].Value)
            .Where(value => value != "Canvas")
            .Should().OnlyContain(value => value.Trim() == "var(--tm-bg-workspace)", "the workspace is the workspace token in every scheme");
        host.Should().NotMatchRegex(@"\.tm-document-canvas-ruler\)\s*\{[^}]*--tm-color-primary", "the ruler is a plain surface");
    }

    [Fact]
    public void ActiveRibbonTab_IsAFlatPrimaryUnderline()
    {
        var rules = CssRules.Parse(ReadComponentCss("_document-editor-toolbar.css"));
        var active = CssRules.Body(rules, ".tm-document-editor__ribbon-tab--active");

        active.Should().Contain("background: transparent").And.Contain("box-shadow: none").And.Contain("border-color: transparent")
            .And.Contain("border-bottom-color: transparent").And.Contain("var(--tm-color-primary)");
        CssRules.Body(rules, ".tm-document-editor__ribbon-tab--active::after").Should().Contain("var(--tm-color-primary)").And.Contain("bottom: 0");
    }

    [Fact]
    public void SidePanel_IsBoundedByTheViewport_AndScrollsInside()
    {
        var rules = CssRules.Parse(ReadComponentCss("_document-editor.css"));
        var panel = CssRules.Body(rules, ".tm-document-side-panel");
        panel.Should().Contain("max-height").And.Contain("100dvh").And.Contain("align-self: start");
        CssRules.Body(rules, ".tm-document-side-panel__body").Should().Contain("overflow: auto").And.Contain("min-height: 0");
    }

    [Fact]
    public void PropertiesPanels_HostedInTheInspector_UseTheEmbeddedFormModifier()
    {
        var root = FindRepositoryRoot();
        var dir = Path.Combine(root, "src", "Tempo.Blazor.DocumentEditor", "Components", "DocumentEditor");
        File.ReadAllText(Path.Combine(dir, "TmDocumentTablePropertiesPanel.razor")).Should().Contain("tm-document-inspector-form--embedded");
        File.ReadAllText(Path.Combine(dir, "TmDocumentCellPropertiesPanel.razor")).Should().Contain("tm-document-inspector-form--embedded");
        var panels = CssRules.Parse(ReadComponentCss("_document-editor-panels.css"));
        CssRules.Body(panels, ".tm-document-inspector-form--embedded").Should().Contain("max-inline-size: 100%").And.Contain("min-inline-size: 0");
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
