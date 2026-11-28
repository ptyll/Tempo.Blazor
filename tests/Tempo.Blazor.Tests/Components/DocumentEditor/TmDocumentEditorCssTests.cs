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
            File.ReadAllText(Path.Combine(cssRoot, "_document-editor-panels.css")),
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
            ".tm-document-version-preview-strip",
            ".tm-document-review-card",
            ".tm-document-review-card--deletion",
            ".tm-document-review-card__action:disabled",
            ".tm-document-review-summary",
            ".tm-document-image-inspector__tile",
            ".tm-document-revision-panel__batch-action",
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
        rules.Any(rule => rule.Selectors.Any(selector => selector.EndsWith(".tm-toolbar-btn", StringComparison.Ordinal) && selector.Contains(".tm-document-editor__ribbon-toolbar", StringComparison.Ordinal))
                && rule.Body.Contains("min-height", StringComparison.Ordinal))
            .Should().BeTrue("the ribbon buttons keep a sizing rule");
        rules.Any(rule => rule.Selectors.Contains(".tm-document-editor__comment-rail") && rule.Body.Contains("max-height", StringComparison.Ordinal))
            .Should().BeTrue("the comment rail stays height-bounded");
        rules.Any(rule => rule.Selectors.Any(selector => selector.StartsWith(".tm-document-editor__ribbon-group::after", StringComparison.Ordinal)))
            .Should().BeFalse("DE-1.2: a ribbon group prints no caption - its name is for assistive technology and for its section in More");

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
            ".tm-document-diff__rows"
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
        CssRules.Body(rules, ".tm-document-find-panel__options").Should().Contain("flex-direction: column", "the options are a column of checkboxes");
        // The count sits INSIDE the field, so the ring is drawn by the field when its input is focused.
        var focus = CssRules.Body(rules, ".tm-document-find-panel__field:focus-within");
        focus.Should().Contain("var(--tm-focus-ring)").And.NotContain("--tm-shadow-focus", "the focus ring is the shared token, not the glow");
        CssRules.Body(rules, ".tm-document-find-panel").Should().NotContain("position: absolute", "phase 6: find is a panel of the inspector, not a drop-down pinned over the page");
    }

    // Phase 5 (G6): the version, compare and debug dialogs and the command palette are core TmModal instances. The phase-1 interim
    // CSS (a hand-rolled scrim, centering and fixed positioning) is deleted - the modal owns all of it.
    [Fact]
    public void FloatingModals_AreCoreTmModal_WithNoHandRolledScrimOrCentering()
    {
        var main = ReadComponentCss("_document-editor.css");
        var floating = ReadComponentCss("_document-editor-floating.css");

        foreach (var gone in new[]
        {
            ".tm-document-dialog-scrim", ".tm-document-json-debug-modal__backdrop", ".tm-document-json-debug-modal {",
            ".tm-document-version-dialog {", ".tm-document-compare-dialog {",
        })
        {
            main.Should().NotContain(gone, $"{gone} was the phase-1 interim CSS; TmModal owns the scrim and the centering");
        }

        var rules = CssRules.Parse(floating);
        rules.Any(rule => rule.Selectors.Any(selector => selector.Contains("scrim", StringComparison.Ordinal))).Should().BeFalse();
        CssRules.Body(rules, ".tm-document-debug-modal__json").Should().Contain("var(--tm-font-mono)");
        CssRules.Body(rules, ".tm-document-compare-dialog__sources").Should().Contain("grid-template-columns");
        CssRules.Body(rules, ".tm-document-compare-dialog__ready").Should().Contain("border");
    }

    [Fact]
    public void FloatingPanels_AreTopLayerFrames_PaintedOnlyThroughSemanticTokens_WithNoHexOrGradient()
    {
        var css = ReadComponentCss("_document-editor-floating.css");
        var rules = CssRules.Parse(css);

        css.Should().NotMatchRegex(@"#[0-9a-fA-F]{3,8}\b|rgba?\(|hsla?\(|linear-gradient|radial-gradient", "tokens only, no decorative gradient");
        CssRules.Body(rules, ".tm-document-floating-panel").Should().Contain("var(--tm-color-surface-elevated)").And.Contain("var(--tm-shadow-lg)");
        css.Should().NotContain("position: fixed", "the core overlay positions the panel; the frame never positions itself");
        css.Should().NotContain("position: absolute;\n    inline-size: min", "no hand-rolled popup anchoring");
        CssRules.Body(rules, ".tm-document-palette-grid__grid").Should().Contain("var(--tm-document-palette-columns");
    }

    [Fact]
    public void PageLayoutForm_IsTwoColumnsFromA40remContainer_AndStyledOnlyInTheSharedFloatingStylesheet()
    {
        var floating = ReadComponentCss("_document-editor-floating.css");
        floating.Should().Contain("container-type: inline-size").And.Contain("@container tm-doc-floating (width >= 640px)");
        var main = ReadComponentCss("_document-editor.css");
        main.Should().NotContain("tm-document-page-layout-inspector", "the old popup form CSS is gone");
        File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Tempo.Blazor.DocumentEditor", "Components", "DocumentEditor", "TmDocumentEditorToolbar.razor.css"))
            .Should().NotContain("tm-document-page-layout-inspector");
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
        css.Should().Contain("@container tm-doc-editor (width < 640px)");
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

    // ── Phase 4 / DE-1.2: the compact ribbon on the core toolbar ────────────────────────────────────────────

    [Fact]
    public void Ribbon_IsOneRow_NeverScrollsHorizontally_AndPrintsNoGroupCaptions()
    {
        var css = ReadComponentCss("_document-editor-toolbar.css");
        var rules = CssRules.Parse(css);

        // Desktop and tablet never scroll the row: the overflow is the core More menu by priority. The phone keeps ONE documented
        // scroll fallback until its own command row lands (a later phase).
        rules.Where(rule => rule.Selectors.Any(selector => selector.Contains(".tm-document-editor__ribbon-groups", StringComparison.Ordinal)))
            .Where(rule => rule.Body.Contains("overflow-x: auto", StringComparison.Ordinal))
            .Should().OnlyContain(rule => rule.Selectors.All(selector => selector.Contains("[data-layout=\"mobile\"]", StringComparison.Ordinal)),
                "only the phone layout keeps a horizontal scroll fallback");
        css.Should().NotContain("content: attr(aria-label)", "a ribbon group prints no caption");
        rules.Any(rule => rule.Selectors.Contains(".tm-document-editor__ribbon-toolbar.tm-toolbar") && rule.Body.Contains("flex-wrap: nowrap", StringComparison.Ordinal))
            .Should().BeTrue("the ribbon is ONE row");
        rules.Any(rule => rule.Selectors.Contains(".tm-document-editor__ribbon-toolbar.tm-toolbar") && rule.Body.Contains("container-type", StringComparison.Ordinal))
            .Should().BeFalse("phase 5: the ribbon's popups are core top-layer panels, so the bar is the same size container as every TmToolbar (the opt-out is gone)");
        css.Should().NotContain("toolbar-floating-backdrop", "the click-away backdrop was deleted: an outside press closes a panel without being swallowed");
    }

    [Fact]
    public void RibbonButtons_AreThirtySixPixels_OnDesktop_AndTouchTargetOnACoarsePointer()
    {
        var css = ReadComponentCss("_document-editor-toolbar.css");

        css.Should().Contain("min-height: 2.25rem", "32-36 px buttons on desktop");
        var coarse = css.IndexOf("@media (pointer: coarse)", StringComparison.Ordinal);
        coarse.Should().BeGreaterThan(0);
        var block = css[coarse..Math.Min(css.Length, coarse + 900)];
        block.Should().Contain(".tm-document-editor__ribbon-toolbar .tm-toolbar-btn");
        block.Should().Contain("var(--tm-touch-target)", "44 px on a coarse pointer");
    }

    [Fact]
    public void RibbonLayoutRules_LiveInOnePlace_NotInTheScopedStylesheet()
    {
        var scoped = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Tempo.Blazor.DocumentEditor", "Components", "DocumentEditor", "TmDocumentEditorToolbar.razor.css"));

        // The ribbon's controls are core components: isolated CSS cannot reach them, and a second set of mobile-ribbon rules next to the
        // package stylesheet is how the 40rem / 42rem conflict arose. The row is styled once.
        scoped.Should().NotContain(".tm-document-editor__ribbon-groups");
        scoped.Should().NotContain(".tm-document-editor__ribbon-commands-wrapper");
        scoped.Should().NotContain("overflow-x: auto");
        scoped.Should().NotContain("max-content", "a group that never shrinks below its content would defeat the measured overflow");
    }

    // ── Phase 2 / DE-2.1: tokens, visual layer and the shared panel building blocks ─────────────────────────

    private static readonly string[] ChromeStylesheets =
    [
        "_document-editor.css",
        "_document-editor-toolbar.css",
        "_document-editor-comments.css",
        "_document-editor-find.css",
        "_document-editor-panels.css",
        "_document-editor-canvas-overlays.css",
        "_document-editor-sheets.css"
    ];

    private static string ReadComponentCss(string fileName)
    {
        var root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(
            root, "src", "Tempo.Blazor.DocumentEditor", "wwwroot", "css", "components", fileName));
    }

    // Phase 7 review fixes: semantic contracts of the mode styles (selectors and tokens, never pixels).
    [Fact]
    public void ModeStyles_NarrowDiff_PrintPreviewAndTouchContracts()
    {
        var panels = ReadComponentCss("_document-editor-panels.css");
        var root = FindRepositoryRoot();
        var scoped = File.ReadAllText(Path.Combine(root, "src", "Tempo.Blazor.DocumentEditor", "Components", "DocumentEditor", "TmDocumentEditor.razor.css"));

        // A narrow viewer shows an unchanged block once and names the side of a changed one.
        panels.Should().Contain("@container tm-doc-diff (width < 640px)");
        panels.Should().Contain(".tm-document-diff__row--unchanged .tm-document-diff__cell--old");
        panels.Should().Contain(".tm-document-diff__side-label");

        // <ins> / <del>: the UA decoration is reset and "added" carries its own non-colour cue.
        var rules = CssRules.Parse(panels);
        CssRules.Body(rules, ".tm-document-diff__segment").Should().Contain("text-decoration: none");
        CssRules.Body(rules, ".tm-document-diff__segment--added").Should().Contain("underline");
        CssRules.Body(rules, ".tm-document-diff__segment--removed").Should().Contain("line-through");

        // The changed tint also has a bar (dark mode), the highlight mapping has NO fallback, touch targets grow on a coarse pointer.
        CssRules.Body(rules, ".tm-document-diff__row--changed .tm-document-diff__cell").Should().Contain("border-inline-start");
        panels.Should().NotContain("var(--tm-document-highlight-insert,").And.NotContain("var(--tm-document-highlight-delete,").And.NotContain("var(--tm-document-highlight-change,");
        panels.Should().Contain("@media (pointer: coarse)");
        panels.Should().Contain(".tm-document-diff__nav-button");

        // The print preview: the ruler is hidden (it kept the mount wider than the region), the thumbnails are paper, a phone has only Close filled
        // and no zoom group; the short Close word replaces the long one.
        scoped.Should().Contain(".tm-document-editor--canvas-print-preview ::deep .tm-document-canvas-ruler-shell");
        CssRules.Body(rules, ".tm-document-print-pages .tm-document-page-navigator__thumbnail").Should().Contain("var(--tm-paper)");
        panels.Should().Contain("[data-layout=\"mobile\"] .tm-document-print-toolbar__group--zoom");
        panels.Should().Contain("[data-layout=\"mobile\"] .tm-document-print-actions__label--short");
        panels.Should().Contain("[data-layout=\"mobile\"] .tm-document-print-actions__card--primary");
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

        // Label beside the control while the panel body is wide enough, above it when not: an intrinsic flex-wrap decided by
        // the body's own width - never the viewport, and no 24rem container query (that width is outside TmBreakpoints).
        var field = CssRules.Body(rules, ".tm-document-inspector-form__field");
        field.Should().Contain("display: flex").And.Contain("flex-wrap: wrap");
        CssRules.Body(rules, ".tm-document-inspector-form__label").Should().Contain("flex: 0 1 8rem");
        CssRules.Body(rules, ".tm-document-inspector-form__control").Should().Contain("flex: 1 1 12rem");
        CssRules.Body(rules, ".tm-document-inspector-form__field--stacked").Should().Contain("display: grid");
        css[..css.IndexOf("/* ── Image inspector", StringComparison.Ordinal)].Should().NotContain("@container", "the form pattern needs no container query: the wrap is intrinsic (only the panels below tune themselves)");

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
        panel.Should().Contain("max-height").And.Contain("--tm-document-frame-block-size").And.Contain("align-self: start");
        CssRules.Body(rules, ".tm-document-editor").Should().Contain("--tm-document-frame-block-size: 100dvh");
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

    // ── Phase 3 / DE-1.1: the editor lays itself out from its own container ───────────────────────────────

    private static string[] EditorStylesheets() =>
    [
        ReadComponentCss("_document-editor.css"),
        ReadComponentCss("_document-editor-toolbar.css"),
        ReadComponentCss("_document-editor-comments.css"),
        ReadComponentCss("_document-editor-panels.css"),
        ReadComponentCss("_document-editor-find.css"),
        ReadComponentCss("_document-editor-canvas-overlays.css"),
        ReadComponentCss("_document-editor-sheets.css"),
        ReadScopedCss("TmDocumentEditor.razor.css"),
        ReadScopedCss("TmDocumentEditorToolbar.razor.css"),
        ReadScopedCss("TmDocumentCanvasEngineHost.razor.css"),
    ];

    private static string ReadScopedCss(string fileName)
        => File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Tempo.Blazor.DocumentEditor", "Components", "DocumentEditor", fileName));

    [Fact]
    public void EditorSizeContainer_IsTheMainRegion_NeverTheRootOrTheRibbon()
    {
        // A size container is the containing block of every position:fixed descendant. The ribbon pickers/backdrops, the
        // dialogs and the floating root position against the viewport, so none of them may sit inside the container.
        var rules = CssRules.Parse(string.Concat(EditorStylesheets()));

        CssRules.Body(rules, ".tm-document-editor__main").Should().Contain("container: tm-doc-editor / inline-size");
        foreach (var selector in new[] { ".tm-document-editor", ".tm-layout.tm-document-editor-host", ".tm-document-editor__ribbon", ".tm-document-editor__floating-root" })
        {
            CssRules.Body(rules, selector).Should().NotContain("container", $"{selector} must not become the containing block of fixed descendants");
        }

        var razor = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Tempo.Blazor.DocumentEditor", "Components", "DocumentEditor", "TmDocumentEditor.razor"));
        var mainStart = razor.IndexOf("class=\"tm-document-editor__main\"", StringComparison.Ordinal);
        var floatingStart = razor.IndexOf("class=\"tm-document-editor__floating-root\"", StringComparison.Ordinal);
        var toolbarStart = razor.IndexOf("<TmDocumentEditorToolbar", StringComparison.Ordinal);
        mainStart.Should().BeGreaterThan(toolbarStart, "the ribbon renders before (outside) the size container");
        floatingStart.Should().BeGreaterThan(mainStart);
        razor[mainStart..floatingStart].Should().NotContain("position: fixed");
    }

    [Fact]
    public void WorkspaceRegions_AreNamedGridAreas_KeyedOnDataLayout_NotOnTheContainerWidth()
    {
        // data-layout is the ONE source of the arrangement (the markup, a forced LayoutMode and the CSS cannot disagree); the
        // size container only tunes density and the first-frame guard of an unmeasured Auto editor.
        var css = ReadComponentCss("_document-editor.css");
        var rules = CssRules.Parse(css);

        CssRules.Body(rules, ".tm-document-editor[data-layout=\"desktop\"] .tm-document-editor__workspace").Should().Contain("grid-template-areas: \"nav doc inspector\"");
        // Phase 8: a tablet is ONE column - the inspector is a side sheet in the SAME cell as the document, never a column of its own.
        CssRules.Body(rules, ".tm-document-editor[data-layout=\"tablet\"] .tm-document-editor__workspace").Should().Contain("grid-template-areas: \"doc\"");
        css.Should().NotContain("@container tm-doc-editor (width >= 640px)", "the arrangement does not answer to the container");
        css.Should().NotContain("@container tm-doc-editor (width >= 1024px)");
        css.Should().Contain(".tm-document-editor:not([data-layout-forced])[data-layout=\"desktop\"]", "the first-frame guard never overrides a forced mode");
        CssRules.Body(rules, ".tm-document-editor__workspace > [data-region=\"nav\"]").Should().NotContain("display: none",
            "the column is hidden only by the first-frame guard, never by default");
        css.Should().NotContain("grid-template-areas: \"doc inspector\"", "no layout gives the inspector a column beside the document any more");
        foreach (var region in new[] { "nav", "doc", "inspector" })
        {
            CssRules.Body(rules, $".tm-document-editor__workspace > [data-region=\"{region}\"]").Should().Contain($"grid-area: {region}");
        }

    }

    [Fact]
    public void EditorContainerQueries_UseRangeSyntax_AndOnlyTheBreakpointConstants()
    {
        var allowed = new[] { 640, 768, 1024, 1280 };
        foreach (var css in EditorStylesheets())
        {
            var withoutComments = System.Text.RegularExpressions.Regex.Replace(css, @"/\*.*?\*/", " ", System.Text.RegularExpressions.RegexOptions.Singleline);
            foreach (System.Text.RegularExpressions.Match container in System.Text.RegularExpressions.Regex.Matches(withoutComments, @"@container\b(?<condition>[^{]*)\{"))
            {
                var condition = container.Groups["condition"].Value;
                condition.Should().NotContain("max-width").And.NotContain("min-width");
                foreach (System.Text.RegularExpressions.Match width in System.Text.RegularExpressions.Regex.Matches(condition, @"\b(\d+)px\b"))
                {
                    allowed.Should().Contain(int.Parse(width.Groups[1].Value), $"'{condition.Trim()}' must use a TmBreakpoints width");
                }
            }
        }
    }

    [Fact]
    public void LayoutMediaQueries_AreGone_ExceptTheViewportBoundOverlays()
    {
        // Every rule that lays the editor out answers to the editor's container; the only viewport queries left are the
        // fixed surfaces of the floating root (dialogs) and the ribbon's popup menus, which are sized by the viewport.
        var offenders = new List<string>();
        foreach (var (name, css) in new[]
        {
            ("_document-editor.css", ReadComponentCss("_document-editor.css")),
            ("_document-editor-toolbar.css", ReadComponentCss("_document-editor-toolbar.css")),
            ("_document-editor-comments.css", ReadComponentCss("_document-editor-comments.css")),
            ("_document-editor-floating.css", ReadComponentCss("_document-editor-floating.css")),
            ("TmDocumentEditor.razor.css", ReadScopedCss("TmDocumentEditor.razor.css")),
            ("TmDocumentCanvasEngineHost.razor.css", ReadScopedCss("TmDocumentCanvasEngineHost.razor.css")),
        })
        {
            var withoutComments = System.Text.RegularExpressions.Regex.Replace(css, @"/\*.*?\*/", " ", System.Text.RegularExpressions.RegexOptions.Singleline);
            foreach (System.Text.RegularExpressions.Match media in System.Text.RegularExpressions.Regex.Matches(withoutComments, @"@media\s*\((?:max|min)-width[^{]*\{"))
            {
                var start = media.Index + media.Length;
                var body = withoutComments.Substring(start, Math.Min(400, withoutComments.Length - start));
                var viewportBound = body.Contains(".tm-document-version-dialog", StringComparison.Ordinal)
                    || body.Contains(".tm-document-compare-dialog__sources", StringComparison.Ordinal)
                    || body.Contains(".tm-document-symbol-menu", StringComparison.Ordinal);
                if (!viewportBound)
                {
                    offenders.Add($"{name}: {media.Value.Trim()}");
                }
            }
        }

        offenders.Should().BeEmpty("layout rules must be @container (or data-layout) rules, never viewport width queries");
    }

    [Fact]
    public void HeaderAndNavigation_PaintOnlyThroughSemanticTokens()
    {
        var css = ReadComponentCss("_document-editor.css");
        var rules = CssRules.Parse(css);
        foreach (var selector in new[]
        {
            ".tm-document-editor__header",
            ".tm-document-editor__header-status-dot",
            ".tm-document-editor__header-mode",
            ".tm-document-navigation",
            ".tm-document-navigation__search",
        })
        {
            var body = CssRules.Body(rules, selector);
            body.Should().NotBeEmpty($"{selector} must keep a rule");
            body.Should().NotMatchRegex(@"#[0-9a-fA-F]{3,8}\b|rgba?\(|hsla?\(|linear-gradient|radial-gradient");
        }

        rules.Any(rule => rule.Selectors.Contains(".tm-document-navigation__toggle:focus-visible")).Should().BeTrue("the collapse toggle keeps a focus ring");
        CssRules.Body(rules, ".tm-document-side-panel[data-tab-style=\"text\"] .tm-document-side-panel__tabs").Should().Contain("display: flex");
    }

    [Fact]
    public void Header_IsNotAContainer_AndFollowsDataLayout()
    {
        var css = ReadComponentCss("_document-editor.css");
        var rules = CssRules.Parse(css);

        CssRules.Body(rules, ".tm-document-editor__header").Should().NotContain("container", "a second container would be a second source of truth for the mode");
        css.Should().NotContain("tm-doc-editor-header");
        CssRules.Body(rules, ".tm-document-editor:not([data-layout=\"desktop\"]) .tm-document-editor__header-button--labelled").Should().Contain("aspect-ratio: 1");
        CssRules.Body(rules, ".tm-document-editor[data-layout=\"mobile\"] .tm-document-editor__header-row").Should().Contain("flex-wrap: nowrap", "a phone header is one row");
        css.Should().Contain("@media (pointer: coarse)");
        css.Should().Contain("min-inline-size: var(--tm-touch-target)");
    }

    [Fact]
    public void SheetsCss_TabletInspectorOverlaysTheDocumentCell_AndThePhoneKeepsOneColumn()
    {
        // Phase 8 (DE-2.7): the contract is semantic - the inspector region of a tablet shares the document's grid cell (so it never takes
        // width from the page), only the sheet takes pointer hits, the phone has no second row, and everything keys off data-layout.
        var rules = CssRules.Parse(ReadComponentCss("_document-editor-sheets.css"));

        var region = CssRules.Body(rules, ".tm-document-editor[data-layout=\"tablet\"] .tm-document-editor__workspace > [data-region=\"inspector\"]");
        region.Should().Contain("grid-area: doc").And.Contain("justify-self: end").And.Contain("pointer-events: none");
        CssRules.Body(rules, ".tm-document-editor[data-layout=\"tablet\"] [data-region=\"inspector\"] > .tm-document-side-panel")
            .Should().Contain("pointer-events: auto").And.Contain("--tm-document-ribbon-block-size");

        var sheets = ReadComponentCss("_document-editor-sheets.css");
        sheets.Should().Contain("data-layout=\"mobile\"");
        sheets.Should().Contain(".tm-document-editor__action-bar");
        sheets.Should().NotContain("@media (max-width", "the layout answers to data-layout, never to the viewport");
    }

    [Fact]
    public void ToolbarCss_TheCommandRowFocusRingSitsInsideTheButton()
    {
        var rules = CssRules.Parse(ReadComponentCss("_document-editor-toolbar.css"));

        CssRules.Body(rules, ".tm-document-editor__ribbon-toolbar .tm-toolbar-btn:focus-visible").Should().Contain("outline-offset: -2px");
    }

    [Fact]
    public void SheetsCss_AFittedPageHidesTheRuler_AndAPhoneKeepsOnlyTheSaveStateInTheStatusBar()
    {
        var rules = CssRules.Parse(ReadComponentCss("_document-editor-sheets.css"));

        // Phase 11 (fix round): the stopgap hides the ruler of a fitted page in EVERY layout (a desktop fitted next to the docked inspector too).
        CssRules.Body(rules, ".tm-document-editor[data-page-fit=\"true\"] .tm-document-canvas-ruler-shell")
            .Should().Contain("display: none");
        ReadComponentCss("_document-editor-sheets.css").Should().NotContain("[data-page-fit=\"true\"]:not([data-layout=\"desktop\"])");
        var sheets2 = ReadComponentCss("_document-editor-sheets.css");
        var fittedStage = System.Text.RegularExpressions.Regex.Match(sheets2, @"@container tm-doc-editor \(width >= 768px\)\s*\{(?<body>.*?)\n\}", System.Text.RegularExpressions.RegexOptions.Singleline).Groups["body"].Value;
        fittedStage.Should().Contain(".tm-document-editor[data-page-fit=\"true\"] .tm-document-canvas-engine-host__mount").And.Contain("var(--tm-space-3)",
            "a fitted page takes back the stage padding from 768 px up; a phone's stage already pads by --tm-space-2 and is never made wider");
        CssRules.Body(rules, ".tm-document-editor[data-layout=\"mobile\"] .tm-document-canvas-engine-host__mount")
            .Should().Contain("padding-block-start: var(--tm-space-2)", "a phone shrinks the gutter above the page");
        var sheets = ReadComponentCss("_document-editor-sheets.css");
        sheets.Should().Contain(".tm-document-editor[data-layout=\"mobile\"] .tm-document-editor__status-bar-group--metrics")
            .And.Contain(".tm-document-editor[data-layout=\"mobile\"] .tm-document-editor__status-bar-group--controls");
    }

    [Fact]
    public void SheetsCss_TheReadableFloorPresentsOnlyTheInspectorAsTheSheet_OnADesktopLayout()
    {
        // Decision DE-AUTO-FIT-READABLE-FLOOR: data-inspector-sheet keeps the desktop ribbon / navigation and drops only the inspector column.
        var rules = CssRules.Parse(ReadComponentCss("_document-editor-sheets.css"));
        var workspace = ".tm-document-editor[data-layout=\"desktop\"][data-inspector-sheet=\"true\"] .tm-document-editor__workspace";

        CssRules.Body(rules, workspace).Should().Contain("grid-template-areas: \"nav doc\"");
        CssRules.Body(rules, ".tm-document-editor[data-inspector-sheet=\"true\"] .tm-document-editor__workspace > [data-region=\"inspector\"]")
            .Should().Contain("grid-area: doc").And.Contain("pointer-events: none");
        CssRules.Body(rules, ".tm-document-editor[data-inspector-sheet=\"true\"] [data-region=\"inspector\"] > .tm-document-side-panel")
            .Should().Contain("position: sticky").And.Contain("box-shadow: var(--tm-shadow-popover)");
    }

    [Fact]
    public void SheetsCss_PhoneFindAndInspectorSheetsAreFullHeight_ObjectSheetKeepsContentHeight()
    {
        var sheets = ReadComponentCss("_document-editor-sheets.css");

        sheets.Should().Contain("data-floating-kind=\"find\"").And.Contain("data-floating-kind=\"inspector\"");
        sheets.Should().Contain("--tm-sheet-max: 1");
        sheets.Should().NotContain("data-floating-kind=\"image-context\"", "the object sheet is a short sheet and keeps the core content height");
    }

    [Fact]
    public void StickyNavigation_ParksBelowTheRibbon_AndTheF6RingIsStyled()
    {
        var rules = CssRules.Parse(ReadComponentCss("_document-editor.css"));

        CssRules.Body(rules, ".tm-document-navigation").Should().Contain("top: calc(var(--tm-document-ribbon-block-size) + var(--tm-space-2))")
            .And.Contain("--tm-document-ribbon-block-size");
        CssRules.Body(rules, ".tm-document-editor").Should().Contain("--tm-document-ribbon-block-size");
        var ring = CssRules.Body(rules, "[data-focus-region][data-region-focus]");
        ring.Should().Contain("outline: 2px solid var(--tm-border-color-focus)").And.Contain("outline-offset: -2px");
    }

    [Fact]
    public void PageSurfaces_DoNotClip_SoAWiderPageStaysReachable()
    {
        var rules = CssRules.Parse(ReadComponentCss("_document-editor.css"));

        CssRules.Body(rules, ".tm-document-editor__surface").Should().NotContain("overflow: hidden");
        CssRules.Body(rules, ".tm-document-editor__page-surface").Should().NotContain("overflow: hidden");
    }

    [Fact]
    public void SidePanelTabs_FocusRingIsInset()
    {
        var rules = CssRules.Parse(ReadComponentCss("_document-editor.css"));

        CssRules.Body(rules, ".tm-document-side-panel__tab:focus-visible").Should().Contain("outline-offset: -2px");
    }

    [Fact]
    public void SidePanelTextTabs_StayOnOneRow_AndScrollInsteadOfWrapping()
    {
        var rules = CssRules.Parse(ReadComponentCss("_document-editor.css"));

        var tabs = CssRules.Body(rules, ".tm-document-side-panel[data-tab-style=\"text\"] .tm-document-side-panel__tabs");
        tabs.Should().Contain("flex-wrap: nowrap").And.Contain("overflow-x: auto").And.Contain("scrollbar-width: none");
        tabs.Should().NotContain("flex-wrap: wrap;");
        CssRules.Body(rules, ".tm-document-side-panel[data-tab-style=\"text\"] .tm-document-side-panel__close").Should().Contain("flex: none");
    }

    [Fact]
    public void HeaderToggle_WhenPressed_ReadsAsOn()
    {
        var rules = CssRules.Parse(ReadComponentCss("_document-editor.css"));

        CssRules.Body(rules, ".tm-document-editor__header-button[aria-pressed=\"true\"]").Should().Contain("background: var(--tm-color-primary-subtle)").And.Contain("color: var(--tm-color-primary)");
        var subtitle = CssRules.Body(rules, ".tm-document-editor:not([data-layout=\"desktop\"]) .tm-document-editor__header-subtitle");
        subtitle.Should().Contain("display: none").And.NotContain("background");
    }

    [Fact]
    public void PrintPreview_DoesNotFrameThePageSurface_WithTheFocusOutline()
    {
        var rules = CssRules.Parse(ReadComponentCss("_document-editor.css"));

        CssRules.Body(rules, ".tm-document-editor__page-surface:focus-within").Should().Contain("outline: 2px solid var(--tm-color-primary)");
        CssRules.Body(rules, ".tm-document-editor__workspace--print-preview .tm-document-editor__page-surface:focus-within").Should().Contain("outline: none");
    }

    [Fact]
    public void CoarsePointer_RevisionBatchActions_AreTouchTargets()
    {
        var css = ReadComponentCss("_document-editor-sheets.css");
        var coarse = System.Text.RegularExpressions.Regex.Match(css, @"@media \(pointer: coarse\)\s*\{.*?\n\}", System.Text.RegularExpressions.RegexOptions.Singleline).Value;

        var rule = System.Text.RegularExpressions.Regex.Match(coarse, @"(?<sel>[^{}]*\.tm-document-revision-panel__batch-action[^{}]*)\{(?<body>[^}]*)\}");
        rule.Success.Should().BeTrue("Accept all / Reject all sit in the coarse-pointer block with the per-card actions");
        rule.Groups["body"].Value.Should().Contain("min-height: var(--tm-touch-target)");
        rule.Groups["sel"].Value.Should().Contain(".tm-document-review-card__action");
    }

    [Fact]
    public void SidePanelTextTabs_TakeADenserForm_InANarrowPanel_AndTheDockIsWideEnoughForFourOfThem()
    {
        var css = ReadComponentCss("_document-editor.css");
        var rules = CssRules.Parse(css);

        CssRules.Body(rules, ".tm-document-side-panel[data-tab-style=\"text\"]").Should().Contain("container: tm-doc-panel / inline-size");
        css.Should().Contain("@container tm-doc-panel (width < 23rem)");
        var dense = System.Text.RegularExpressions.Regex.Match(css, @"@container tm-doc-panel \(width < 23rem\)\s*\{(?<body>.*?)\n\}", System.Text.RegularExpressions.RegexOptions.Singleline).Groups["body"].Value;
        dense.Should().NotContain("font-size", "the tab keeps --tm-font-size-sm: a dock that still overflows scrolls instead of shrinking the type");
        dense.Should().NotContain("padding-inline: calc(var(--tm-space-1) / 2)", "the tab keeps its --tm-space-2 gutter");
        CssRules.Body(rules, ".tm-document-editor__workspace").Should().Contain("--tm-document-inspector-width: clamp(22rem, 26cqw, 24rem)", "Properties, Comments, Revisions and Versions fit the dock without a sideways scroll");
    }

    [Fact]
    public void SidePanelTextTabs_DropTheCountBadgeOnlyOnARealOverflow_OrBelowNineteenRem_SoTheFirstTabIsNeverHalfHidden()
    {
        // Phase 11 (fix round): the badge leaves the row (visually; the number stays in the accessible name of the tab) when side-panel-tabs.mjs publishes
        // data-tabs-overflow - the row REALLY overflows with the badges shown - or when the dock is below 19 rem. The tab keeps --tm-space-2 and
        // --tm-font-size-sm. Keyed on the panel's own size container, never on the viewport.
        var css = ReadComponentCss("_document-editor.css");
        var rules = CssRules.Parse(css);
        var badge = ".tm-document-side-panel[data-tab-style=\"text\"] .tm-document-side-panel__tabs[data-tabs-overflow=\"true\"] .tm-document-side-panel__badge";

        CssRules.Body(rules, badge).Should().Contain("clip-path: inset(50%)").And.NotContain("display: none");
        CssRules.Body(rules, ".tm-document-side-panel[data-tab-style=\"text\"] .tm-document-side-panel__tabs[data-tabs-overflow=\"true\"] .tm-document-side-panel__tab")
            .Should().Contain("padding-inline: var(--tm-space-1)").And.NotContain("font-size", "an overflowing row gives up the gutter, never the type size");
        css.Should().NotContain("@container tm-doc-panel (width < 22rem)", "the unconditional 22 rem rule is gone");
        var dense = System.Text.RegularExpressions.Regex.Match(css, @"@container tm-doc-panel \(width < 19rem\)\s*\{(?<body>.*?)\n\}", System.Text.RegularExpressions.RegexOptions.Singleline).Groups["body"].Value;
        dense.Should().Contain(".tm-document-side-panel__badge").And.Contain("clip-path: inset(50%)").And.NotContain("display: none");
        CssRules.Body(rules, ".tm-document-side-panel[data-tab-style=\"text\"] .tm-document-side-panel__tab").Should().Contain("padding-inline: var(--tm-space-2)").And.Contain("font-size: var(--tm-font-size-sm)");
    }

    [Fact]
    public void SidePanelTextTabs_FadeTheEdgeThatStillHidesTabs()
    {
        var rules = CssRules.Parse(ReadComponentCss("_document-editor.css"));
        const string tabs = ".tm-document-side-panel[data-tab-style=\"text\"] .tm-document-side-panel__tabs";

        CssRules.Body(rules, tabs + "[data-overflow-end=\"true\"]").Should().Contain("mask-image").And.Contain("to right");
        CssRules.Body(rules, tabs + "[data-overflow-start=\"true\"]").Should().Contain("mask-image");
        CssRules.Body(rules, tabs + "[data-overflow-start=\"true\"][data-overflow-end=\"true\"]").Should().Contain("mask-image").And.Contain("transparent");
    }

    [Fact]
    public void CommandPalette_GoesFullscreenOnThePhoneLayout_NotOnAViewportWidth()
    {
        var rules = CssRules.Parse(ReadComponentCss("_document-editor-floating.css"));

        var panel = CssRules.Body(rules, ".tm-document-editor[data-layout=\"mobile\"] .tm-document-command-palette .tm-command-palette");
        panel.Should().Contain("inline-size: 100vw").And.Contain("block-size: 100dvh");
        CssRules.Body(rules, ".tm-document-editor[data-layout=\"mobile\"] .tm-document-command-palette .tm-command-palette-list").Should().Contain("flex: 1 1 auto");
    }

    [Fact]
    public void PhoneLayout_TunesTheActionBarTilesAndTheKebabSheet_ForNarrowColumns()
    {
        var sheets = ReadComponentCss("_document-editor-sheets.css");
        var rules = CssRules.Parse(sheets);

        sheets.Should().Contain("@container tm-doc-editor (width < 22.5rem)", "the 320 px tiles take the bar's gutters back");
        CssRules.Body(rules, ".tm-document-editor__header-kebab-body .tm-btn").Should().Contain("min-block-size: var(--tm-touch-target)").And.Contain("inline-size: 100%");
        CssRules.Body(rules, ".tm-document-editor[data-layout=\"tablet\"] .tm-document-side-panel[data-tab-style=\"text\"] .tm-document-side-panel__tab")
            .Should().Contain("min-height: var(--tm-touch-target)");
        CssRules.Body(CssRules.Parse(ReadComponentCss("_document-editor-panels.css")), ".tm-document-diff__segments").Should().Contain("flex: 1 1 100%");
    }

    [Fact]
    public void ReviewSummary_IsOneRow_AndTheSentenceIsVisuallyHidden_BecauseTheButtonsSayTheSame()
    {
        var css = ReadComponentCss("_document-editor-panels.css");
        var rules = CssRules.Parse(css);

        CssRules.Body(rules, ".tm-document-review-summary").Should().Contain("flex-wrap: nowrap");
        CssRules.Body(rules, ".tm-document-review-summary__text").Should().Contain("flex-wrap: nowrap").And.Contain("min-width: 0");
        var sentence = CssRules.Body(rules, ".tm-document-review-summary__text span");
        sentence.Should().Contain("clip: rect(0 0 0 0)").And.Contain("position: absolute", "the sentence duplicates the two buttons' labels: accessible, not visible, at every width");
        sentence.Should().NotContain("text-overflow");
    }

    [Fact]
    public void ImageInspector_SizeRow_IsThreeEqualColumns_WithTheUnitInsideTheField()
    {
        var rules = CssRules.Parse(ReadComponentCss("_document-editor-panels.css"));

        CssRules.Body(rules, ".tm-document-image-inspector__triple").Should().Contain("repeat(3, minmax(0, 1fr))");
        CssRules.Body(rules, ".tm-document-inspector-form__adorned").Should().Contain("position: relative");
        var suffix = CssRules.Body(rules, ".tm-document-inspector-form__adorned > .tm-document-inspector-form__suffix");
        suffix.Should().Contain("position: absolute").And.Contain("inset-inline-end: var(--tm-space-2)");
        CssRules.Body(rules, ".tm-document-inspector-form__adorned > input").Should().Contain("padding-inline-end");
    }

    [Fact]
    public void ImageInspector_WrapTiles_AreFiveColumns_WithAContainerFallbackToFour()
    {
        var css = ReadComponentCss("_document-editor-panels.css");
        var rules = CssRules.Parse(css);

        var tiles = rules.Where(rule => rule.Selectors.Contains(".tm-document-image-inspector__tiles")).Select(rule => rule.Body).ToList();
        tiles.Should().Contain(body => body.Contains("repeat(5, minmax(0, 1fr))"));
        tiles.Should().Contain(body => body.Contains("repeat(4, minmax(0, 1fr))"), "a narrow inspector falls back to four columns");
        css.Should().Contain("@container tm-doc-image-inspector (width <");
        CssRules.Body(rules, ".tm-document-image-inspector").Should().Contain("container: tm-doc-image-inspector / inline-size");
        CssRules.Body(rules, ".tm-document-image-inspector__tiles .tm-document-image-inspector__tile").Should().Contain("hyphens: auto");
    }

    [Fact]
    public void InspectorForm_ReadonlyInputs_LookInert_WithExistingTokens()
    {
        var css = ReadComponentCss("_document-editor-panels.css");
        var rules = CssRules.Parse(css);

        var readonlyRule = CssRules.Body(rules, ".tm-document-inspector-form input[readonly]");
        readonlyRule.Should().Contain("background: var(--tm-color-surface-muted)").And.Contain("color: var(--tm-color-text-secondary)");
        var tokens = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Tempo.Blazor", "wwwroot", "css", "tokens.css"));
        tokens.Should().Contain("--tm-color-surface-muted:").And.Contain("--tm-color-text-secondary:");
    }

    [Fact]
    public void ReviewCard_AccentIsDefinedOnTheBaseRule_WithoutAFallbackAtTheUseSites()
    {
        var css = ReadComponentCss("_document-editor-panels.css");
        var rules = CssRules.Parse(css);

        CssRules.Body(rules, ".tm-document-review-card").Should().Contain("--tm-review-accent: var(--tm-color-info)");
        css.Should().NotContain("var(--tm-review-accent,");
    }

    // ── Phase 12: the viewport-bounded frame ────────────────────────────────────────────────────────────
    // The contract is semantic (selectors, declarations, the context a rule lives in), never a pixel: every bounded rule keys on data-frame, none of them
    // sits in a viewport @media (a host that pins a height inside a narrow column must get the same frame as one on a wide screen).

    [Fact]
    public void BoundedFrame_RootAndObserverHost_TakeTheHeight_AndMayShrink()
    {
        var rules = CssRules.Parse(ReadComponentCss("_document-editor.css"));

        var root = CssRules.Body(rules, ".tm-document-editor[data-frame=\"bounded\"]");
        root.Should().Contain("height: var(--tm-document-editor-height)").And.Contain("min-height: 0");
        var host = CssRules.Body(rules, ".tm-document-editor-host[data-frame=\"bounded\"]");
        host.Should().Contain("height: var(--tm-document-editor-height)").And.Contain("min-height: 0");
    }

    [Fact]
    public void BoundedFrame_Main_FillsTheFrameThroughTheFlexChain_AndDoesNotCollapseWithoutADefiniteHeight()
    {
        var rules = CssRules.Parse(ReadComponentCss("_document-editor.css"));

        var main = CssRules.Body(rules, ".tm-document-editor[data-frame=\"bounded\"] .tm-document-editor__main");
        main.Should().Contain("flex: 1 1 auto", "a basis of 0 would collapse the editor to its chrome inside a parent without a definite height").And.Contain("min-block-size: 0");
        main.Should().Contain("--tm-document-ribbon-block-size: 0px",
            "the ribbon is outside the scroller in a bounded frame, so the sticky columns park at the top of their cell");
        main.Should().Contain("--tm-document-frame-block-size: 100%", "the sticky panels are as tall as their cell, not the viewport");
        main.Should().NotContain("container:", "main stays the inline-size container it always was - no size containment, so no collapse");
        CssRules.Body(rules, ".tm-document-editor").Should().Contain("--tm-document-frame-block-size: 100dvh",
            "outside a bounded frame the sticky panels are bounded by the viewport, as before");
        CssRules.Body(rules, ".tm-document-editor").Should().NotContain("status-block-size", "no guessed height of the status bar");
    }

    [Fact]
    public void BoundedFrame_Workspace_IsTheBoundedArea_AndOnlyTheRegionsScroll()
    {
        var rules = CssRules.Parse(ReadComponentCss("_document-editor.css"));

        var workspace = CssRules.Body(rules, ".tm-document-editor[data-frame=\"bounded\"] .tm-document-editor__workspace");
        workspace.Should().Contain("overflow: hidden", "a second scroller around the regions is exactly the double scrollbar this frame removes")
            .And.Contain("min-block-size: 0").And.Contain("min-height: 0").And.Contain("flex: 1 1 auto")
            .And.Contain("grid-template-rows: minmax(0, 1fr)", "the single row is the frame, so no region can stretch the workspace past it")
            .And.Contain("padding-block-start: 0");

        var doc = CssRules.Body(rules, ".tm-document-editor[data-frame=\"bounded\"] [data-region=\"doc\"][data-scroll-viewport=\"true\"]");
        doc.Should().Contain("overflow: auto").And.Contain("min-block-size: 0").And.Contain("scrollbar-gutter: stable")
            .And.NotContain("both-edges")
            .And.Contain("overflow-anchor: none", "the virtualizer mounts pages under the reader; scroll anchoring would move the scroll position by pages");

        var column = CssRules.Body(rules, ".tm-document-editor[data-frame=\"bounded\"] .tm-document-editor__navigation");
        column.Should().Contain("min-block-size: 0").And.Contain("max-block-size: 100%").And.Contain("overflow-y: auto").And.Contain("overscroll-behavior: contain");
        CssRules.Body(rules, ".tm-document-editor[data-frame=\"bounded\"] [data-region=\"inspector\"][data-presentation=\"docked\"] > .tm-document-side-panel")
            .Should().Contain("block-size: 100%", "the docked inspector panel fills its column, its body scrolls");

        CssRules.Body(rules, ".tm-document-editor[data-frame=\"bounded\"] .tm-document-canvas-engine-host")
            .Should().Contain("overflow: visible", "the canvas host stops being a (content-tall) scroller of its own - its scrollbar would sit at the end of the document");
        CssRules.Body(rules, ".tm-document-editor[data-frame=\"bounded\"] .tm-document-editor__comment-rail").Should().Contain("max-height: none");
        CssRules.Body(rules, ".tm-document-editor[data-fullscreen=\"true\"]").Should().Contain("--tm-document-editor-height: 100dvh",
            "fullscreen sets the frame height in CSS, not in an inline style");
    }

    [Fact]
    public void InspectorEdgeToggle_IsTheSameSquareAsTheFoldedNavigationRail()
    {
        var rules = CssRules.Parse(ReadComponentCss("_document-editor.css"));

        CssRules.Body(rules, ".tm-document-side-panel__edge-toggle").Should().Contain("width: var(--tm-touch-target)").And.Contain("height: var(--tm-touch-target)");
    }

    [Fact]
    public void BoundedFrame_ChromeAroundTheScroller_DoesNotShrinkAndDoesNotStick()
    {
        var rules = CssRules.Parse(ReadComponentCss("_document-editor.css") + ReadComponentCss("_document-editor-toolbar.css") + ReadComponentCss("_document-editor-sheets.css"));

        CssRules.Body(rules, ".tm-document-editor[data-frame=\"bounded\"] .tm-document-editor__status-bar").Should().Contain("flex: 0 0 auto");
        CssRules.Body(rules, ".tm-document-editor[data-frame=\"bounded\"] .tm-document-editor__status-bar:last-child")
            .Should().Contain("env(safe-area-inset-bottom)", "when the status bar is the last edge of the frame it keeps clear of a home indicator");
        CssRules.Body(rules, ".tm-document-editor[data-frame=\"bounded\"] .tm-document-editor__ribbon")
            .Should().Contain("position: relative", "the ribbon is above the scroller, it has nothing to stick to");
        var actionBar = CssRules.Body(rules, ".tm-document-editor[data-frame=\"bounded\"] .tm-document-editor__action-bar");
        actionBar.Should().Contain("position: relative").And.Contain("flex: 0 0 auto");
    }

    [Fact]
    public void BoundedFrame_StickyPanels_AreBoundedByTheFrame_NotTheViewport()
    {
        var css = ReadComponentCss("_document-editor.css") + ReadComponentCss("_document-editor-sheets.css") + ReadComponentCss("_document-editor-panels.css");
        var rules = CssRules.Parse(css);

        foreach (var selector in new[]
        {
            ".tm-document-navigation",
            ".tm-document-print-pages",
            ".tm-document-editor[data-layout=\"tablet\"] [data-region=\"inspector\"] > .tm-document-side-panel",
            ".tm-document-editor[data-inspector-sheet=\"true\"] [data-region=\"inspector\"] > .tm-document-side-panel",
        })
        {
            var body = CssRules.Body(rules, selector);
            body.Should().Contain("var(--tm-document-frame-block-size)", $"{selector} must be capped by the frame, which is the viewport only when nothing bounds it");
            body.Should().NotContain("100dvh", $"{selector} would overflow a bounded frame shorter than the viewport");
        }

        CssRules.Body(rules, ".tm-document-side-panel").Should().Contain("var(--tm-document-frame-block-size)").And.Contain("align-self: start");
        CssRules.Body(rules, ".tm-document-editor[data-frame=\"bounded\"] .tm-document-side-panel").Should().Contain("max-height: calc(var(--tm-document-frame-block-size)");
    }

    [Fact]
    public void BoundedFrame_Fullscreen_IsTheSameFrameAtTheViewportHeight()
    {
        var css = ReadComponentCss("_document-editor.css") + ReadComponentCss("_document-editor-find.css");
        var rules = CssRules.Parse(css);

        var fullscreen = CssRules.Body(rules, ".tm-document-editor[data-fullscreen=\"true\"]");
        fullscreen.Should().Contain("position: fixed").And.Contain("inset: 0").And.Contain("z-index");
        fullscreen.Should().NotContain("overflow: hidden", "the frame decides what scrolls; clipping the root would cut the floating UI of the ribbon");
        css.Should().NotContain("body.tm-document-editor--fullscreen .tm-document-editor",
            "fullscreen keys on the editor's own data-fullscreen like every other state - the body class is only the scroll lock");
    }

    [Fact]
    public void BoundedFrame_Rules_AreNeverInsideAViewportMediaQuery()
    {
        var offenders = new List<string>();
        foreach (var file in new[]
        {
            "_document-editor.css", "_document-editor-toolbar.css", "_document-editor-sheets.css", "_document-editor-panels.css", "_document-editor-find.css", "_document-editor-floating.css",
        })
        {
            var css = System.Text.RegularExpressions.Regex.Replace(ReadComponentCss(file), @"/\*.*?\*/", string.Empty, System.Text.RegularExpressions.RegexOptions.Singleline);
            var index = 0;
            while ((index = css.IndexOf("[data-frame=\"bounded\"]", index, StringComparison.Ordinal)) >= 0)
            {
                foreach (var prelude in EnclosingAtRules(css, index))
                {
                    if (prelude.StartsWith("@media", StringComparison.Ordinal) && !prelude.Contains("prefers-reduced-motion", StringComparison.Ordinal))
                    {
                        offenders.Add($"{file}: {prelude}");
                    }
                }

                index += 10;
            }
        }

        offenders.Should().BeEmpty("a bounded rule keys on data-frame (and the container), never on the viewport - a pinned height in a narrow column is the same frame");
    }

    private static IEnumerable<string> EnclosingAtRules(string css, int position)
    {
        var stack = new Stack<string>();
        var preludeStart = 0;
        for (var i = 0; i < position; i++)
        {
            switch (css[i])
            {
                case '{':
                    stack.Push(css[preludeStart..i].Trim());
                    preludeStart = i + 1;
                    break;
                case '}':
                    if (stack.Count > 0)
                    {
                        stack.Pop();
                    }

                    preludeStart = i + 1;
                    break;
                case ';':
                    preludeStart = i + 1;
                    break;
            }
        }

        return stack.Where(prelude => prelude.StartsWith('@'));
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
