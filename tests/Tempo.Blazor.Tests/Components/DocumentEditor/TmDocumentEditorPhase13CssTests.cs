using System.Text.RegularExpressions;
using FluentAssertions;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

/// <summary>
/// Redesign 146c7e1b, phase 13: the dialogs and panels of the editor answer to their own container and to the layout the editor
/// resolved, never to the viewport; the bounded phone frame gives up the room an on-screen keyboard covers. Semantic checks (selectors,
/// declarations, tokens), never pixels.
/// </summary>
public sealed class TmDocumentEditorPhase13CssTests
{
    private static string ReadCss(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        return File.ReadAllText(Path.Combine(directory!.FullName, "src", "Tempo.Blazor.DocumentEditor", "wwwroot", "css", "components", fileName));
    }

    private static string StripComments(string css) => Regex.Replace(css, @"/\*.*?\*/", " ", RegexOptions.Singleline);

    // The declarations of every rule whose selector list contains exactly `selector` (rules nested in an at-rule included).
    private static string Body(string css, string selector)
    {
        var bodies = new List<string>();
        foreach (Match match in Regex.Matches(StripComments(css), @"([^{}]+)\{([^{}]*)\}"))
        {
            var prelude = match.Groups[1].Value.Trim();
            if (prelude.StartsWith('@'))
            {
                continue;
            }

            if (prelude.Split(',').Select(candidate => candidate.Trim()).Contains(selector))
            {
                bodies.Add(match.Groups[2].Value);
            }
        }

        return string.Concat(bodies);
    }

    [Fact]
    public void NoStylesheetOfThePackage_HasAViewportMaxWidthMedia()
    {
        foreach (var name in new[]
        {
            "_document-editor.css", "_document-editor-toolbar.css", "_document-editor-comments.css", "_document-editor-find.css",
            "_document-editor-panels.css", "_document-editor-canvas-overlays.css", "_document-editor-sheets.css", "_document-editor-floating.css",
        })
        {
            Regex.IsMatch(StripComments(ReadCss(name)), @"@media[^{]*\b(max-width|min-width|width\s*[<>])").Should().BeFalse($"{name}: a viewport @media silently breaks a forced LayoutMode");
        }
    }

    [Fact]
    public void CompareDialogSources_ReactToTheirOwnWidth_AndToTheDialogLayout()
    {
        var css = ReadCss("_document-editor-floating.css");

        // No core-modal width override and no viewport / threshold hack: the grid fills as many 18rem columns as the dialog's OWN width holds.
        css.Should().NotContain(".tm-modal--lg", "the compare dialog keeps the core large width");
        Body(css, ".tm-document-compare-dialog__sources").Should().Contain("repeat(auto-fit, minmax(min(100%, 18rem), 1fr))");

        // A forced phone layout on a wide viewport: the modal overlay carries the resolved layout.
        Body(css, ".tm-modal-overlay[data-layout=\"mobile\"] .tm-document-compare-dialog__sources").Should().Contain("grid-template-columns: minmax(0, 1fr)");
        // The forced-mobile panel is fullscreen on a wide screen: its content is a readable column and the footer buttons are finger-sized and share the row.
        Body(css, ".tm-modal-overlay[data-layout=\"mobile\"] .tm-document-compare-dialog__content").Should().Contain("max-inline-size: 40rem").And.Contain("margin-inline: auto");
        var footer = Body(css, ".tm-modal-overlay.tm-document-compare-dialog[data-layout=\"mobile\"] .tm-modal-footer .tm-btn");
        footer.Should().Contain("flex: 1 1 0").And.Contain("min-block-size: var(--tm-touch-target)");
    }

    [Fact]
    public void PanelGrids_AreNarrowByDefault_AndWidenOnlyInAWideContainerOutsideAPhoneLayout()
    {
        var css = ReadCss("_document-editor-floating.css");
        const string wide = ".tm-document-editor:not([data-layout=\"mobile\"])";

        // The narrow columns are the BASE (no copy of them under a [data-layout=mobile] rule) ...
        Body(css, ".tm-document-style-gallery__form").Should().Contain("grid-template-columns: minmax(0, 1fr)");
        Body(css, ".tm-document-image-panel__library").Should().Contain("repeat(2, minmax(0, 1fr))");
        Body(css, ".tm-document-page-layout").Should().Contain("grid-template-columns: minmax(0, 1fr)");
        css.Should().NotContain(".tm-document-editor[data-layout=\"mobile\"] .tm-document-style-gallery__form", "no duplicated base values for the pinned phone layout");

        // ... the wide ones live in ONE container rule on the panel body, gated off for a pinned phone layout.
        css.Should().Contain("@container tm-doc-floating (width >= 640px)");
        Body(css, $"{wide} .tm-document-style-gallery__form").Should().Contain("repeat(2, minmax(0, 1fr))");
        Body(css, $"{wide} .tm-document-image-panel__library").Should().Contain("repeat(3, minmax(0, 1fr))");
        Body(css, $"{wide} .tm-document-page-layout").Should().Contain("minmax(7rem, 9rem) minmax(0, 1fr)");
        Body(css, $"{wide} .tm-document-page-layout__grid--four").Should().Contain("repeat(4, minmax(0, 1fr))");
        Body(css, $"{wide} .tm-document-page-layout__grid--columns").Should().Contain("minmax(0, 1fr) minmax(0, 1fr) auto");
    }

    [Fact]
    public void BoundedFrame_HasNoFocusRingOnTheDocumentTallPageSurface()
    {
        // The page surface is as tall as the whole document: in a bounded frame its focus ring was clipped by the scroller and read as one stray
        // blue line at the right edge of the visible column. The canvas keeps its own indicators (caret, selection, the active page outline).
        var css = ReadCss("_document-editor.css");

        Body(css, ".tm-document-editor[data-frame=\"bounded\"] .tm-document-editor__page-surface:focus-within").Should().Contain("outline: none");
        css.Should().NotContain("[data-scroll-viewport=\"true\"]:focus-within", "a ring on the scroller is painted under the canvas pages and shows up the same way");
    }

    [Fact]
    public void SidePanel_OnlyTheEdgeToggleIsFingerSized_TheCloseButtonKeepsTheTabRowRoom()
    {
        // Phase 12 gave the edge toggle the touch size and, by sharing the rule, the close button too: its 12 extra px pushed a tab out of the
        // 282 px docked tab row (Desktop_InspectorTabs_AreInsideTheTablist at 1600). Only the toggle (a lone square on the document's edge) is enlarged.
        var css = ReadCss("_document-editor.css");

        Body(css, ".tm-document-side-panel__edge-toggle").Should().Contain("width: var(--tm-touch-target)").And.Contain("height: var(--tm-touch-target)");
        var close = Body(css, ".tm-document-side-panel__close");
        close.Should().Contain("width: 2rem").And.Contain("height: 2rem");
        close.Should().NotContain("--tm-touch-target");
    }

    [Fact]
    public void BoundedPhoneFrame_GivesUpTheRoomTheKeyboardCovers()
    {
        var body = Body(ReadCss("_document-editor.css"), ".tm-document-editor[data-frame=\"bounded\"][data-layout=\"mobile\"]");

        body.Should().Contain("padding-block-end: var(--tm-visual-viewport-inset-block-end, 0px)");
        body.Should().Contain("box-sizing: border-box", "the frame keeps its height; the padding takes the room out of the content");
    }
}
