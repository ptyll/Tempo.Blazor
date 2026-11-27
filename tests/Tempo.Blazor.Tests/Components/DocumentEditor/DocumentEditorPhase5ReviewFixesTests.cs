using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.Components.Overlay;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

/// <summary>
/// Redesign 146c7e1b, phase 5 REVIEW fixes (code, architect and UX reviews): the table grid and the token list stay inside their panel,
/// panels never cover their anchor, the sheet's Done is a real confirm, the field popover reports why it closed and follows the
/// viewport, the palette grids know their column count in a sheet, and the dialogs are finished (compare footer, debug JSON regions).
/// </summary>
public sealed class DocumentEditorPhase5ReviewFixesTests : LocalizationTestBase
{
    private const string OverlayModulePath = "./_content/Tempo.Blazor/js/overlay.js";

    private static string FloatingCss()
        => File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Tempo.Blazor.DocumentEditor", "wwwroot", "css", "components", "_document-editor-floating.css"));

    private static string ToolbarCss()
        => File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Tempo.Blazor.DocumentEditor", "wwwroot", "css", "components", "_document-editor-toolbar.css"));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    // The declarations of the first rule whose selector is exactly `selector`.
    private static string RuleBody(string css, string selector)
    {
        var start = css.IndexOf(selector + " {", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"the rule {selector} exists");
        var open = css.IndexOf('{', start);
        var close = css.IndexOf('}', open);
        return css[(open + 1)..close];
    }

    // ─── 1. the table grid stays inside its panel ────────────────────────────

    [Fact]
    public void TableGridPicker_IsAPlainGrid_ItDoesNotPositionOrPaintItself()
    {
        var body = RuleBody(ToolbarCss(), ".tm-document-table-grid-picker");

        foreach (var property in new[] { "position", "top", "left", "translate", "z-index", "background", "border", "box-shadow", "border-radius", "padding" })
        {
            body.Should().NotContain(property + ":", $"the picker sits inside the floating panel - {property} would pull it out");
        }

        body.Should().Contain("display: grid");
    }

    [Fact]
    public void NothingUnderAFloatingPanel_DeclaresFixedOrAbsolutePositionForThePickerOrTheTokenList()
    {
        var css = ToolbarCss() + FloatingCss();
        css.Should().NotMatchRegex(@"\.tm-document-table-grid-picker\s*\{[^}]*position:\s*(fixed|absolute)");
        var tokenOverride = RuleBody(FloatingCss(), ".tm-document-floating-panel__frame[data-floating-kind=\"token\"] .tm-rte-token-dropdown");
        tokenOverride.Should().Contain("position: static").And.Contain("max-inline-size: none").And.Contain("box-shadow: none");
    }

    // ─── 3. panels never cover their anchor ──────────────────────────────────

    [Fact]
    public void FloatingPanel_AsksTheOverlayToConstrainItsHeight_AndTheFrameInheritsTheCap()
    {
        var module = JSInterop.SetupModule(OverlayModulePath);
        module.SetupVoid("open", _ => true).SetVoidResult();
        module.SetupVoid("update", _ => true).SetVoidResult();
        module.SetupVoid("close", _ => true).SetVoidResult();

        Render<TmDocumentFloatingPanel>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.Title, "Panel")
            .Add(x => x.LayoutMode, TmLayoutMode.Desktop)
            .AddChildContent("<p>Body</p>"));

        var options = module.Invocations["open"].Should().ContainSingle().Subject.Arguments[4]!;
        options.GetType().GetProperty("constrainHeight")!.GetValue(options).Should().Be(true);
        options.GetType().GetProperty("flipMinRoom")!.GetValue(options).Should().Be(240, "a ribbon panel stays below its trigger while 240 px are free there");

        var css = FloatingCss();
        RuleBody(css, ".tm-document-floating-panel__frame").Should().Contain("max-block-size: inherit");
        RuleBody(css, ".tm-document-floating-panel").Should().Contain("max-block-size:");
    }

    [Fact]
    public void FrameRules_AreKeyedOffTheFrame_BecauseThePanelRootClassIsNotAppliedInASheet()
    {
        // In sheet mode the core renders its own TmDrawer (.tm-overlay-panel-sheet): .tm-document-floating-panel is NOT on any element, the
        // frame (.tm-document-floating-panel__frame) is. A rule that hangs off the panel root silently skips every phone.
        var css = FloatingCss();
        css.Should().NotContain(".tm-document-floating-panel[data-floating-kind");
        css.Should().NotContain(".tm-document-floating-panel .tm-");

        var sheet = Render<TmDocumentFloatingPanel>(p => p.Add(x => x.IsOpen, true).Add(x => x.Title, "T").Add(x => x.Kind, "token")
            .Add(x => x.LayoutMode, TmLayoutMode.Mobile).AddChildContent("<p>B</p>"));
        sheet.FindAll(".tm-document-floating-panel").Should().BeEmpty("the panel root class is not applied inside a sheet");
        sheet.Find(".tm-overlay-panel-sheet .tm-document-floating-panel__frame").GetAttribute("data-floating-kind").Should().Be("token");

        var popover = Render<TmDocumentFloatingPanel>(p => p.Add(x => x.IsOpen, true).Add(x => x.Title, "T").Add(x => x.Kind, "token")
            .Add(x => x.LayoutMode, TmLayoutMode.Desktop).AddChildContent("<p>B</p>"));
        popover.Find(".tm-document-floating-panel__frame").GetAttribute("data-floating-kind").Should().Be("token");
    }

    // ─── 15, 16, 17: CSS contracts ───────────────────────────────────────────

    [Fact]
    public void SheetBody_DropsItsInlinePadding_BecauseTheSheetContentInsetsAlready()
        => RuleBody(FloatingCss(), ".tm-overlay-panel-sheet__content .tm-document-floating-panel__body").Should().Contain("padding-inline: 0");

    [Fact]
    public void CoarsePointer_GetsTheTouchTargetOnEveryPanelControl()
    {
        var css = FloatingCss();
        var block = css[css.IndexOf("@media (pointer: coarse)", StringComparison.Ordinal)..];
        block = block[..block.IndexOf("\n}\n", StringComparison.Ordinal)];
        block.Should().Contain(".tm-document-floating-list button")
            .And.Contain(".tm-document-image-panel__tab")
            .And.Contain(".tm-document-page-layout__check")
            .And.Contain(".tm-document-floating-panel__frame .tm-btn")
            .And.Contain("min-block-size: var(--tm-touch-target)");
    }

    [Fact]
    public void PaletteGrid_DoesNotUseAViewportMediaQuery_ForItsColumns()
        => FloatingCss().Should().NotContain("min(var(--tm-document-palette-columns, 6), 4)");

    [Fact]
    public void PointsField_UnitIsAnAddOnInsideTheFieldBorder()
    {
        var css = FloatingCss();
        RuleBody(css, ".tm-document-page-layout__points").Should().Contain("border: 1px solid");
        css.Should().Contain(".tm-document-page-layout__points:focus-within");
    }

    [Fact]
    public void FloatingCss_HasNoDeadZIndexFallbacks()
    {
        var root = FindRepositoryRoot();
        foreach (var file in new[] { "_document-editor.css", "_document-editor-toolbar.css", "_document-editor-floating.css" })
        {
            File.ReadAllText(Path.Combine(root, "src", "Tempo.Blazor.DocumentEditor", "wwwroot", "css", "components", file))
                .Should().NotContain("var(--tm-document-z-floating-ui, ", $"{file}: the token is defined on the root, a literal fallback is dead");
        }
    }

    // ─── 14. palette grids know their columns in a sheet ─────────────────────

    private sealed record Tile(string Id);

    [Fact]
    public void PaletteGrid_InASheet_UsesTheSheetColumns_ForTheCssVariableAndTheArrowKeys()
    {
        var items = Enumerable.Range(0, 12).Select(i => new Tile("t" + i)).ToList();
        var cut = Render<TmDocumentFloatingPanel>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.Title, "Grid")
            .Add(x => x.LayoutMode, TmLayoutMode.Mobile)
            .AddChildContent<DocumentPaletteGrid<Tile>>(g => g
                .Add(x => x.Items, items)
                .Add(x => x.Columns, 6)
                .Add(x => x.SheetColumns, 3)
                .Add(x => x.ItemContent, tile => (RenderFragment)(b => b.AddContent(0, tile.Id)))));

        var grid = cut.Find(".tm-document-palette-grid__grid");
        grid.GetAttribute("style").Should().Contain("--tm-document-palette-columns: 3");
        cut.Find(".tm-document-palette-grid").GetAttribute("data-columns").Should().Be("3");

        var buttons = cut.FindAll(".tm-document-palette-grid__item");
        buttons[0].KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        cut.FindAll(".tm-document-palette-grid__item")[3].GetAttribute("tabindex").Should().Be("0", "a row is three tiles in a sheet");
    }

    [Fact]
    public void PaletteGrid_OnDesktop_KeepsItsFullColumns()
    {
        var items = Enumerable.Range(0, 12).Select(i => new Tile("t" + i)).ToList();
        var cut = Render<TmDocumentFloatingPanel>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.Title, "Grid")
            .Add(x => x.LayoutMode, TmLayoutMode.Desktop)
            .AddChildContent<DocumentPaletteGrid<Tile>>(g => g
                .Add(x => x.Items, items)
                .Add(x => x.Columns, 6)
                .Add(x => x.SheetColumns, 3)
                .Add(x => x.ItemContent, tile => (RenderFragment)(b => b.AddContent(0, tile.Id)))));

        cut.Find(".tm-document-palette-grid").GetAttribute("data-columns").Should().Be("6");
    }

    // ─── 8. the sheet's Done is a real confirm ───────────────────────────────

    [Fact]
    public async Task SheetDone_RaisesTheDoneReason_Once()
    {
        var reasons = new List<DocumentFloatingCloseReason>();
        var cut = Render<TmDocumentFloatingPanel>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.Title, "Sheet")
            .Add(x => x.LayoutMode, TmLayoutMode.Mobile)
            .Add(x => x.OnClose, EventCallback.Factory.Create<DocumentFloatingCloseReason>(this, r => reasons.Add(r)))
            .AddChildContent("<p>Body</p>"));

        await cut.InvokeAsync(() => cut.Find(".tm-overlay-panel-sheet__done").Click());

        reasons.Should().Equal(DocumentFloatingCloseReason.Done);
    }

    [Fact]
    public void HideFooterInSheet_DropsAFooterThatOnlyDuplicatesDone_OnASheetOnly()
    {
        RenderFragment footer = b => b.AddMarkupContent(0, "<button data-testid=\"footer-close\">Close</button>");

        var sheet = Render<TmDocumentFloatingPanel>(p => p.Add(x => x.IsOpen, true).Add(x => x.Title, "T")
            .Add(x => x.LayoutMode, TmLayoutMode.Mobile).Add(x => x.HideFooterInSheet, true).Add(x => x.Footer, footer).AddChildContent("<p>B</p>"));
        sheet.FindAll("[data-testid='footer-close']").Should().BeEmpty();

        var popover = Render<TmDocumentFloatingPanel>(p => p.Add(x => x.IsOpen, true).Add(x => x.Title, "T")
            .Add(x => x.LayoutMode, TmLayoutMode.Desktop).Add(x => x.HideFooterInSheet, true).Add(x => x.Footer, footer).AddChildContent("<p>B</p>"));
        popover.FindAll("[data-testid='footer-close']").Should().ContainSingle();
    }

    [Fact]
    public void FieldPopover_InASheet_HasNoFooterClose_AndTheDoneReasonReachesTheHost()
    {
        var reasons = new List<DocumentFloatingCloseReason>();
        var cut = Render<TmDocumentFieldPopover>(p => p
            .AddCascadingValue(TmLayoutScopes.Viewport, new TmLayoutContext(TmLayoutMode.Auto, TmLayoutMode.Mobile))
            .Add(x => x.ContentControl, new DocumentFieldPopoverControl { ControlId = "cc", Kind = DocumentContentControlKind.Date, Title = "Date" })
            .Add(x => x.OnClose, EventCallback.Factory.Create<DocumentFloatingCloseReason>(this, r => reasons.Add(r))));

        cut.FindAll("[data-testid='document-field-popover-close']").Should().BeEmpty("the sheet's Done is the way out");

        cut.Find(".tm-overlay-panel-sheet__done").Click();

        reasons.Should().Equal(DocumentFloatingCloseReason.Done);
    }

    // ─── 6. field popover ────────────────────────────────────────────────────

    [Fact]
    public void FieldPopover_RepeatsChip_UsesTheClockwiseRefreshIcon_NotTheCounterClockwiseOne()
    {
        var cut = Render<TmDocumentFieldPopover>(p => p.Add(x => x.SigningField, new TmDocumentCanvasEngineHost.CanvasEngineSigningFieldSelection
        {
            Uuid = "sf", FieldType = "signature", Label = "L", Repeats = true,
        }));

        var chip = cut.Find("[data-chip='repeats']");
        chip.QuerySelector("svg")!.InnerHtml.Should().Contain("M3 12a9 9 0 0 1 9-9", "refresh-cw, not refresh-ccw");
    }

    [Fact]
    public void FieldPopover_WatchesTheViewport_AndClosesOnceTheCanvasMoves()
    {
        var module = JSInterop.SetupModule("./_content/Tempo.Blazor.DocumentEditor/js/document-editor/interop/field-popover-guard.mjs");
        module.SetupVoid("watchViewport", _ => true).SetVoidResult();
        module.SetupVoid("unwatch", _ => true).SetVoidResult();
        var reasons = new List<DocumentFloatingCloseReason>();

        var cut = Render<TmDocumentFieldPopover>(p => p
            .Add(x => x.ContentControl, new DocumentFieldPopoverControl { ControlId = "cc", Kind = DocumentContentControlKind.Date, Title = "Date" })
            .Add(x => x.OnClose, EventCallback.Factory.Create<DocumentFloatingCloseReason>(this, r => reasons.Add(r))));

        cut.WaitForAssertion(() => module.Invocations["watchViewport"].Should().ContainSingle());

        cut.InvokeAsync(() => cut.Instance.HandleViewportMovedAsync());

        // A scroll is not a click on the canvas: the host must not steal the focus back.
        reasons.Should().Equal(DocumentFloatingCloseReason.Outside);
    }

    [Fact]
    public void ViewportGuardModule_ExistsInThePackage()
        => File.Exists(Path.Combine(FindRepositoryRoot(), "src", "Tempo.Blazor.DocumentEditor", "wwwroot", "js", "document-editor", "interop", "field-popover-guard.mjs")).Should().BeTrue();

    // ─── toolbar: link and header/footer presets ─────────────────────────────

    private IRenderedComponent<TmDocumentEditorToolbar> RenderToolbar(Action<ComponentParameterCollectionBuilder<TmDocumentEditorToolbar>>? configure = null, TmLayoutMode? scope = null)
        => Render<TmDocumentEditorToolbar>(p =>
        {
            if (scope is { } mode)
            {
                p.AddCascadingValue(TmLayoutScopes.Viewport, new TmLayoutContext(TmLayoutMode.Auto, mode));
            }

            configure?.Invoke(p);
        });

    [Fact]
    public void Link_RemoveIsOfferedOnlyWhenTheSelectionAlreadyHadALink_NotWhenAUrlIsTyped()
    {
        var removed = new EventCallback(null, () => { });

        var fresh = RenderToolbar(p => p.Add(x => x.OnLinkRemoved, removed));
        fresh.Find("[data-testid='document-link']").Click();
        fresh.Find("[data-testid='document-link-url']").Input("https://example.test/new");
        fresh.FindAll("[data-testid='document-remove-link']").Should().BeEmpty("typing a URL into a fresh popover does not create a link to remove");

        var existing = RenderToolbar(p => p
            .Add(x => x.OnLinkRemoved, removed)
            .Add(x => x.LinkInfoProvider, () => Task.FromResult<WysiwygLinkInfo?>(new WysiwygLinkInfo { Href = "https://example.test/a" })));
        existing.Find("[data-testid='document-link']").Click();
        existing.Find("[data-testid='document-link-url']").Input("https://example.test/b");
        existing.FindAll("[data-testid='document-remove-link']").Should().ContainSingle("the selection carried a link when the popover opened");
    }

    [Fact]
    public void Link_SheetDone_AppliesAValidUrl_AndDiscardsAnEmptyOne()
    {
        var applied = new List<string>();
        var cut = RenderToolbar(
            p => p.Add(x => x.OnLinkApplied, EventCallback.Factory.Create<WysiwygLinkPayload>(this, payload => applied.Add(payload.Href))),
            TmLayoutMode.Mobile);

        cut.Find("[data-testid='document-link']").Click();
        cut.Find("[data-testid='document-link-url']").Input("https://example.test/ok");
        cut.Find(".tm-overlay-panel-sheet__done").Click();

        applied.Should().Equal("https://example.test/ok");
        cut.FindAll("[data-testid='document-link-dialog']").Should().BeEmpty();

        cut.Find("[data-testid='document-link']").Click();
        cut.Find(".tm-overlay-panel-sheet__done").Click();

        applied.Should().HaveCount(1, "an empty draft is a cancel, not an invalid apply");
        cut.FindAll("[data-testid='document-link-dialog']").Should().BeEmpty();
    }

    [Fact]
    public void HeaderFooterPresets_PageXOfYOnTheRight_HasItsOwnPreview()
    {
        var cut = RenderToolbar(p => p.Add(x => x.ActiveRegion, "Header"));
        cut.Find("[data-testid='document-ribbon-tab-header-footer']").Click();
        cut.Find("[data-testid='document-header-footer-preset-menu']").Click();

        var plain = cut.Find("[data-testid='document-header-footer-preset-page-number-right-footer'] .tm-document-header-footer-menu__preview");
        var wide = cut.Find("[data-testid='document-header-footer-preset-page-x-of-y-right-footer'] .tm-document-header-footer-menu__preview");
        plain.ClassList.Should().Contain("tm-document-header-footer-menu__preview--right");
        wide.ClassList.Should().Contain("tm-document-header-footer-menu__preview--right-wide").And.NotContain("tm-document-header-footer-menu__preview--right");
    }

    // ─── dialogs ─────────────────────────────────────────────────────────────

    [Fact]
    public void CompareDialog_FooterEndsWithThePrimaryAction_AndTheSourcesAreTmInputs()
    {
        var cut = Render<TmDocumentCompareDialog>(p => p.Add(x => x.CurrentDocument, DocumentEditorDocument.Empty()));

        var order = cut.FindAll(".tm-modal-footer button").Select(b => b.GetAttribute("data-testid")).ToList();
        order.Should().Equal("document-compare-cancel", "document-compare-run");
        cut.Find("[data-testid='document-compare-run']").ClassList.Should().Contain("tm-btn-primary");
        cut.FindAll(".tm-document-compare-dialog__cancel").Should().BeEmpty();
        cut.FindAll(".tm-document-compare-dialog__source select").Should().OnlyContain(e => e.ClassList.Contains("tm-input"));
        FloatingCss().Should().NotContain(".tm-document-compare-dialog__cancel");
    }

    [Fact]
    public void DialogsForwardTheirRestoreFocusTargetId_ToTheCoreModal()
    {
        const string target = "canvas-input";

        Render<TmDocumentVersionDialog>(p => p.Add(x => x.RestoreFocusTargetId, target))
            .FindComponent<Tempo.Blazor.Components.Feedback.TmModal>().Instance.RestoreFocusTargetId.Should().Be(target);
        Render<TmDocumentCompareDialog>(p => p.Add(x => x.CurrentDocument, DocumentEditorDocument.Empty()).Add(x => x.RestoreFocusTargetId, target))
            .FindComponent<Tempo.Blazor.Components.Feedback.TmModal>().Instance.RestoreFocusTargetId.Should().Be(target);
        Render<TmDocumentJsonDebugModal>(p => p.Add(x => x.IsOpen, true).Add(x => x.Json, "{}").Add(x => x.RestoreFocusTargetId, target))
            .FindComponent<Tempo.Blazor.Components.Feedback.TmModal>().Instance.RestoreFocusTargetId.Should().Be(target);
        Render<TmDocumentClipboardHtmlDebugModal>(p => p.Add(x => x.IsOpen, true).Add(x => x.Html, "<p/>").Add(x => x.RestoreFocusTargetId, target))
            .FindComponent<Tempo.Blazor.Components.Feedback.TmModal>().Instance.RestoreFocusTargetId.Should().Be(target);
    }

    [Fact]
    public void DebugJson_IsALabelledFocusableRegion_AndIsShownIndented()
    {
        var cut = Render<TmDocumentDebugSection>(p => p
            .Add(x => x.Title, "Runtime state")
            .Add(x => x.SectionTestId, "section")
            .Add(x => x.ContentTestId, "content")
            .Add(x => x.Content, "{\"runtime\":\"canvas\",\"pages\":[1,2]}"));

        var pre = cut.Find("[data-testid='content']");
        pre.GetAttribute("tabindex").Should().Be("0");
        pre.GetAttribute("role").Should().Be("region");
        cut.Find($"[id='{pre.GetAttribute("aria-labelledby")}']").TextContent.Should().Be("Runtime state");
        pre.TextContent.Should().Contain("\n  \"runtime\": \"canvas\"", "a one-line JSON is indented for reading");
        FloatingCss().Should().Contain(".tm-document-debug-modal__json:focus-visible");
    }

    [Fact]
    public void DebugSection_ShowsNonJsonAsReceived_AndIsNotFocusableWhenEmpty()
    {
        var text = Render<TmDocumentDebugSection>(p => p.Add(x => x.Title, "T").Add(x => x.SectionTestId, "s").Add(x => x.ContentTestId, "c").Add(x => x.Content, "<p>raw</p>"));
        text.Find("[data-testid='c']").TextContent.Should().Be("<p>raw</p>");

        var empty = Render<TmDocumentDebugSection>(p => p.Add(x => x.Title, "T").Add(x => x.SectionTestId, "s").Add(x => x.ContentTestId, "c").Add(x => x.Content, ""));
        empty.Find("[data-testid='c']").HasAttribute("tabindex").Should().BeFalse();
    }
}
