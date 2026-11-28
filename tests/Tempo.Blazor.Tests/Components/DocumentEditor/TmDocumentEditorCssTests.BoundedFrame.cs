using FluentAssertions;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

// The phase-12 bounded-frame CSS contracts, split out of TmDocumentEditorCssTests (the 1200-line ceiling); the helpers are shared.
public partial class TmDocumentEditorCssTests
{
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
}
