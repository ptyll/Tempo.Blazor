using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// 2026-06-11 reproduction gates (Phase 0 of the canvas UX bugfix batch) for twelve issues reported from two
/// screen recordings + a printscreen on <c>/document-editor</c> (the "Service agreement" contract demo):
/// <list type="bullet">
///   <item>B1 — Home jumps to the END of the previous line instead of the start of the current visual line.</item>
///   <item>B2 — the floating mini toolbar only flickers after a mouse selection (the root click closes it).</item>
///   <item>B5 — after scrolling to the last page and back, page 0 is rendered below page 1 (wrong DOM order).</item>
///   <item>B7 — "Insert page break" does nothing in the canvas engine (legacy-only handler).</item>
///   <item>B9 — the side-panel page navigator only lists one page even when the document has several.</item>
///   <item>B11 — context-menu Copy is disabled and Ctrl+C does not write the clipboard on the full editor.</item>
///   <item>B12 — context-menu Paste is always disabled.</item>
/// </list>
/// These tests establish the reproduction: B1/B5/B7/B9/B11/B12 are expected RED before their fix phases and
/// GREEN afterwards; B2 is RED/flaky-RED (the trailing root click is timing dependent). Each test also writes
/// a screenshot + manifest under TestResults so the before/after can be reviewed as a human would see it.
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasUxFixE2ETests : WasmTestBase
{
    private const string ContractDocumentId = "contract-demo";
    private const string LargeDocumentId = "large-perf-1000";

    // A body paragraph that wraps onto several visual lines in the contract demo (used for the Home/End test).
    private const string OverviewBlockId = "contract-normal-overview";
    private const string AgreementBlockId = "contract-scope";
    private const string LeftWrapImageId = "contract-left-wrap-image";

    [TestInitialize]
    public Task ResetDocumentEditorDemoAsync()
        => DocumentEditorE2EReset.ResetAsync();

    /// <summary>
    /// B1 — click in the middle of a WRAPPED continuation line, press Home. The caret must move to the start
    /// of that same visual line (x decreases, y unchanged). RED before the fix: the shared caret stop lookup
    /// resolves the wrap-boundary offset to the END of the PREVIOUS line, so the caret jumps up a line.
    /// </summary>
    [TestMethod]
    public async Task UxB1_HomeKey_MovesToLineStart()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenDocumentAsync(page, ContractDocumentId, OverviewBlockId);

        var output = CreateOutputDirectory("b1-home");
        await ScreenshotAsync(page, Path.Combine(output, "00-before.png"));

        var line = await FindWrappedContinuationLineAsync(page);
        Assert.IsTrue(line.Found, $"Could not find a wrapped continuation line to test Home against. Debug: {line.Debug}");

        await page.Mouse.ClickAsync((float)line.ClickX, (float)line.ClickY);
        await WaitForCollapsedCaretAsync(page);
        var caretBefore = await ReadCaretRectAsync(page);
        Assert.IsTrue(caretBefore.Found, "Caret element must be present after clicking into the wrapped line.");

        await page.Keyboard.PressAsync("Home");
        await page.WaitForTimeoutAsync(200);
        var caretAfter = await ReadCaretRectAsync(page);
        await ScreenshotAsync(page, Path.Combine(output, "01-after-home.png"));

        var dy = Math.Abs(caretAfter.Y - caretBefore.Y);
        var dx = caretAfter.X - caretBefore.X;

        await File.WriteAllTextAsync(
            Path.Combine(output, "manifest.json"),
            JsonSerializer.Serialize(new
            {
                problem = "B1 Home jumps to previous line end",
                line,
                caretBefore,
                caretAfter,
                dy,
                dx
            }, JsonWebIndented));

        Assert.IsTrue(caretAfter.Found, "Caret element must still be present after pressing Home.");
        Assert.IsTrue(
            dy < 6,
            $"B1 regression: Home moved the caret to a DIFFERENT visual line (Δy={dy:N1}px). Caret before y={caretBefore.Y:N1}, after y={caretAfter.Y:N1} — Home jumped to the previous line instead of staying on the wrapped line.");
        Assert.IsTrue(
            dx < -8,
            $"B1 regression: Home did not move the caret toward the line start (Δx={dx:N1}px; expected a clear move left).");
    }

    /// <summary>
    /// B1 regression guard — End on a wrapped continuation line keeps the caret on that visual line and moves
    /// it to the right (the line end). Protects against the lineId-carrying fix breaking the (previously
    /// working) End direction. The line-bounded Home/End mechanics are also covered rigorously at the unit
    /// level in selection/__tests__/caret-wrap-boundary.test.mjs.
    /// </summary>
    [TestMethod]
    public async Task UxB1_End_StaysOnWrappedLine()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenDocumentAsync(page, ContractDocumentId, OverviewBlockId);

        var output = CreateOutputDirectory("b1-end");
        var line = await FindWrappedContinuationLineAsync(page);
        Assert.IsTrue(line.Found, $"Could not find a wrapped continuation line. Debug: {line.Debug}");

        await page.Mouse.ClickAsync((float)line.ClickX, (float)line.ClickY);
        await WaitForCollapsedCaretAsync(page);
        var caretBefore = await ReadCaretRectAsync(page);

        await page.Keyboard.PressAsync("End");
        await page.WaitForTimeoutAsync(150);
        var caretAfterEnd = await ReadCaretRectAsync(page);
        await ScreenshotAsync(page, Path.Combine(output, "00-after-end.png"));

        var endDy = Math.Abs(caretAfterEnd.Y - caretBefore.Y);
        await File.WriteAllTextAsync(
            Path.Combine(output, "manifest.json"),
            JsonSerializer.Serialize(new { caretBefore, caretAfterEnd, endDy, lineTop = line.LineTop }, JsonWebIndented));

        Assert.IsTrue(endDy < 6, $"End must stay on the same visual line (Δy={endDy:N1}px).");
        Assert.IsTrue(caretAfterEnd.X > line.ClickX, $"End must move the caret to the line end (x {caretAfterEnd.X:N1} should exceed click x {line.ClickX:N1}).");
    }

    /// <summary>
    /// B5 — scroll a multi-page document to the bottom, then incrementally back up to the top. The mounted
    /// canvas pages must keep ascending DOM order. RED before the fix: <c>ensurePage</c> always appends a newly
    /// mounted page before the bottom spacer, so scrolling up mounts page 0 AFTER page 1 ⇒ DOM order [1, 0, …].
    /// </summary>
    [TestMethod]
    public async Task UxB5_ScrollRoundtrip_KeepsPageOrder()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1280, 720);
        await OpenDocumentAsync(page, LargeDocumentId, blockId: null);

        var pageCount = await ReadIntAttrAsync(page, "data-canvas-page-count");
        Assert.IsTrue(pageCount >= 3, $"B5 needs a multi-page document; {LargeDocumentId} reported {pageCount} page(s).");

        var output = CreateOutputDirectory("b5-scroll-order");

        // Position the wheel over the canvas, scroll DOWN in steps until the last pages mount.
        var (centerX, centerY) = await ReadViewportCenterAsync(page);
        await page.Mouse.MoveAsync((float)centerX, (float)centerY);
        for (var i = 0; i < 16; i++)
        {
            await page.Mouse.WheelAsync(0, 1400);
            await page.WaitForTimeoutAsync(120);
        }
        await page.WaitForTimeoutAsync(300);
        var orderAtBottom = await ReadPageDomOrderAsync(page);

        // Scroll UP incrementally back to the top — this is where the append-only mount corrupts the order.
        for (var i = 0; i < 24; i++)
        {
            await page.Mouse.WheelAsync(0, -1400);
            await page.WaitForTimeoutAsync(120);
        }
        await page.WaitForTimeoutAsync(400);
        // Viewport-only capture (the full demo element spans the whole 1000-paragraph document) so the UX
        // review shows what the user actually sees at the top after the roundtrip.
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(output, "00-after-roundtrip.png"), Type = ScreenshotType.Png });

        var orderAtTop = await ReadPageDomOrderAsync(page);
        var ascending = IsStrictlyAscending(orderAtTop);

        await File.WriteAllTextAsync(
            Path.Combine(output, "manifest.json"),
            JsonSerializer.Serialize(new
            {
                problem = "B5 page order corrupts after scroll roundtrip",
                pageCount,
                orderAtBottom,
                orderAtTop,
                ascending
            }, JsonWebIndented));

        Assert.IsTrue(
            ascending,
            $"B5 regression: after a scroll roundtrip the mounted pages are out of order. DOM page-index order at top = [{string.Join(", ", orderAtTop)}] (expected strictly ascending).");
    }

    /// <summary>
    /// B7 — placing the caret in the body and clicking "Insert page break" must insert a page-break block (and
    /// Undo must remove it). RED before the fix: <c>InsertPageBreakAsync</c> is legacy-only (guards on the WYSIWYG
    /// host, which is null in canvas mode), so the command is a no-op. The page-break BLOCK count is the robust
    /// signal (the rendered page count is noisy — a forced break can also let the layout trim a trailing page).
    /// </summary>
    [TestMethod]
    public async Task UxB7_InsertPageBreak_AddsPage()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenDocumentAsync(page, ContractDocumentId, OverviewBlockId);

        var output = CreateOutputDirectory("b7-page-break");
        var pageBreaksBefore = await ReadPageBreakCountAsync(page);

        // Caret into the body so the command is enabled, then trigger it from the Insert ribbon tab.
        await ClickCanvasOffsetAsync(page, OverviewBlockId, 5);
        await WaitForCollapsedCaretAsync(page);
        await page.GetByTestId("document-ribbon-tab-insert").ClickAsync();
        await page.GetByTestId("document-insert-page-break").ClickAsync();
        await page.WaitForTimeoutAsync(700);
        await ScreenshotAsync(page, Path.Combine(output, "00-after-insert.png"));
        var pageBreaksAfter = await ReadPageBreakCountAsync(page);

        // Undo must remove the inserted page break.
        await page.GetByTestId("document-undo").ClickAsync();
        await page.WaitForTimeoutAsync(500);
        var pageBreaksAfterUndo = await ReadPageBreakCountAsync(page);

        await File.WriteAllTextAsync(
            Path.Combine(output, "manifest.json"),
            JsonSerializer.Serialize(new { problem = "B7 insert page break", pageBreaksBefore, pageBreaksAfter, pageBreaksAfterUndo }, JsonWebIndented));

        Assert.AreEqual(
            pageBreaksBefore + 1,
            pageBreaksAfter,
            $"B7 regression: inserting a page break did not add a page-break block (before={pageBreaksBefore}, after={pageBreaksAfter}).");
        Assert.AreEqual(
            pageBreaksBefore,
            pageBreaksAfterUndo,
            $"B7: undo must remove the inserted page break (before={pageBreaksBefore}, after undo={pageBreaksAfterUndo}).");
    }

    // Counts page-break blocks in the engine model (body blocks of type 'pageBreak').
    private static Task<int> ReadPageBreakCountAsync(IPage page)
        => EvaluateWithContextRetryAsync<int>(page, 
            """
            () => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                return import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs').then(module => {
                    const model = JSON.parse(module.getModelJson(handle) || '{}');
                    return (model?.body?.blocks || []).filter(b => String(b?.type || '').toLowerCase() === 'pagebreak').length;
                });
            }
            """);

    /// <summary>
    /// B9 — the side-panel page navigator must list every page in the document. RED before the fix: the canvas
    /// host never pushes page metrics, so the navigator stays on its single default page while the engine
    /// reports more.
    /// </summary>
    [TestMethod]
    public async Task UxB9_PageNavigator_ShowsAllPages()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenDocumentAsync(page, ContractDocumentId, OverviewBlockId);

        var output = CreateOutputDirectory("b9-page-navigator");

        var enginePageCount = await ReadIntAttrAsync(page, "data-canvas-page-count");
        Assert.IsTrue(enginePageCount >= 2, $"B9 needs a multi-page document; engine reported {enginePageCount} page(s).");

        // The side panel is docked open on the contract demo; switch it to the Pages tab.
        await page.GetByTestId("document-side-panel").WaitForAsync(new LocatorWaitForOptions { Timeout = 20_000 });
        await page.GetByTestId("document-side-panel-tab-pages").ClickAsync();
        await page.WaitForTimeoutAsync(400);
        await ScreenshotAsync(page, Path.Combine(output, "00-pages-tab.png"));

        var navigatorItemCount = await page.GetByTestId("document-page-navigator-item").CountAsync();
        var statusBarText = await page.GetByTestId("document-status-page-count").InnerTextAsync();

        // Clicking a later page in the navigator must scroll it into the visible set (B9 navigation).
        var lastPageIndex = enginePageCount - 1;
        await page.Locator($"[data-testid='document-page-navigator-item'][data-page-index='{lastPageIndex}']").ClickAsync();
        await page.WaitForTimeoutAsync(600);
        var visibleAfterNav = await EvaluateWithContextRetryAsync<string>(page, 
            "() => document.querySelector('[data-testid=\"document-canvas-engine-root\"]')?.getAttribute('data-canvas-visible-page-indexes') || ''");
        var navigatedToLast = visibleAfterNav.Split(',').Contains(lastPageIndex.ToString());

        await File.WriteAllTextAsync(
            Path.Combine(output, "manifest.json"),
            JsonSerializer.Serialize(new { problem = "B9 navigator", enginePageCount, navigatorItemCount, statusBarText, lastPageIndex, visibleAfterNav, navigatedToLast }, JsonWebIndented));

        Assert.AreEqual(
            enginePageCount,
            navigatorItemCount,
            $"B9 regression: the page navigator lists {navigatorItemCount} page(s) but the engine has {enginePageCount}. Canvas page metrics never reach the navigator.");
        Assert.IsTrue(
            statusBarText.Contains(enginePageCount.ToString(), StringComparison.Ordinal),
            $"B9: the status bar page count must match the engine ({enginePageCount}); status bar shows '{statusBarText}'.");
        Assert.IsTrue(
            navigatedToLast,
            $"B9: clicking the last page in the navigator must scroll it into view (visible indexes after nav = [{visibleAfterNav}]).");
    }

    /// <summary>
    /// B11 — selecting text and choosing Copy from the context menu must write it to the system clipboard.
    /// The selection is placed via the engine seam (reliable), then the menu is opened with a real right-click;
    /// the menu Copy routes to the engine clipboard controller (navigator.clipboard.write).
    /// </summary>
    [TestMethod]
    public async Task UxB11_ContextCopy_WritesClipboard()
    {
        var context = await CreateContextAsync();
        await context.GrantPermissionsAsync(
            ["clipboard-read", "clipboard-write"],
            new BrowserContextGrantPermissionsOptions { Origin = BaseUrl });
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenDocumentAsync(page, ContractDocumentId, OverviewBlockId);

        var output = CreateOutputDirectory("b11-copy");
        await EvaluateWithContextRetryAsync<string>(page, "value => { navigator.clipboard.writeText(value); return 'ok'; }", "SENTINEL-UX-B11");

        await SelectCanvasTextRangeViaInteropAsync(page, OverviewBlockId, 0, 9); // "The agree"
        await OpenTextContextMenuAtAsync(page, OverviewBlockId, 4);
        await ScreenshotAsync(page, Path.Combine(output, "00-context-menu.png"));

        var copyDisabled = await page.GetByTestId("document-context-copy").IsDisabledAsync();
        await ClickTestIdViaJsAsync(page, "document-context-copy");
        await page.WaitForTimeoutAsync(400);
        var clipboard = await EvaluateWithContextRetryAsync<string>(page, "() => navigator.clipboard.readText()");

        await File.WriteAllTextAsync(
            Path.Combine(output, "manifest.json"),
            JsonSerializer.Serialize(new { problem = "B11 context copy", copyDisabled, clipboard }, JsonWebIndented));

        Assert.IsFalse(copyDisabled, "context-menu Copy must be enabled for a non-empty canvas selection.");
        Assert.IsTrue(
            clipboard.Contains("The agree", StringComparison.Ordinal),
            $"B11 regression: context-menu Copy did not write the selected text to the clipboard (clipboard='{clipboard}').");
    }

    /// <summary>
    /// B11 — Cut from the context menu writes the selection to the clipboard AND removes it from the document.
    /// </summary>
    [TestMethod]
    public async Task UxB11_ContextCut_WritesClipboardAndRemovesText()
    {
        var context = await CreateContextAsync();
        await context.GrantPermissionsAsync(
            ["clipboard-read", "clipboard-write"],
            new BrowserContextGrantPermissionsOptions { Origin = BaseUrl });
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenDocumentAsync(page, ContractDocumentId, OverviewBlockId);

        var output = CreateOutputDirectory("b11-cut");
        var before = await ReadBlockTextAsync(page, OverviewBlockId);

        await SelectCanvasTextRangeViaInteropAsync(page, OverviewBlockId, 0, 4); // "The "
        await OpenTextContextMenuAtAsync(page, OverviewBlockId, 2);
        var cutDisabled = await page.GetByTestId("document-context-cut").IsDisabledAsync();
        await ClickTestIdViaJsAsync(page, "document-context-cut");
        await page.WaitForTimeoutAsync(500);
        var clipboard = await EvaluateWithContextRetryAsync<string>(page, "() => navigator.clipboard.readText()");
        var after = await ReadBlockTextAsync(page, OverviewBlockId);

        await File.WriteAllTextAsync(
            Path.Combine(output, "manifest.json"),
            JsonSerializer.Serialize(new { problem = "B11 context cut", cutDisabled, clipboard, before, after }, JsonWebIndented));

        Assert.IsFalse(cutDisabled, "context-menu Cut must be enabled for a non-empty canvas selection.");
        Assert.IsTrue(clipboard.StartsWith("The", StringComparison.Ordinal), $"Cut must write the selection to the clipboard (clipboard='{clipboard}').");
        Assert.IsTrue(after.Length < before.Length, $"Cut must remove the selection from the document (before {before.Length} → after {after.Length} chars).");
    }

    /// <summary>
    /// B12 — Paste from the context menu inserts the system clipboard content at the caret. The menu item must
    /// be enabled (it was hard-coded disabled), and clicking it pulls via the async Clipboard API.
    /// </summary>
    [TestMethod]
    public async Task UxB12_ContextPaste_InsertsText()
    {
        var context = await CreateContextAsync();
        await context.GrantPermissionsAsync(
            ["clipboard-read", "clipboard-write"],
            new BrowserContextGrantPermissionsOptions { Origin = BaseUrl });
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenDocumentAsync(page, ContractDocumentId, OverviewBlockId);

        var output = CreateOutputDirectory("b12-paste");
        await EvaluateWithContextRetryAsync<string>(page, "value => { navigator.clipboard.writeText(value); return 'ok'; }", "Zq7paste");

        // Caret at the start of the overview paragraph, then paste via the context menu.
        await SelectCanvasTextRangeViaInteropAsync(page, OverviewBlockId, 0, 0);
        await OpenTextContextMenuAtAsync(page, OverviewBlockId, 0);
        await ScreenshotAsync(page, Path.Combine(output, "00-context-menu.png"));

        var pasteDisabled = await page.GetByTestId("document-context-paste").IsDisabledAsync();
        await ClickTestIdViaJsAsync(page, "document-context-paste");
        await page.WaitForTimeoutAsync(600);
        var after = await ReadBlockTextAsync(page, OverviewBlockId);

        await File.WriteAllTextAsync(
            Path.Combine(output, "manifest.json"),
            JsonSerializer.Serialize(new { problem = "B12 context paste", pasteDisabled, after }, JsonWebIndented));

        Assert.IsFalse(pasteDisabled, "B12 regression: context-menu Paste must be enabled in an editable canvas context.");
        Assert.IsTrue(
            after.Contains("Zq7paste", StringComparison.Ordinal),
            $"B12 regression: context-menu Paste did not insert the clipboard text (block now '{after[..Math.Min(40, after.Length)]}...').");
    }

    /// <summary>
    /// B4 — a long image caption must wrap to the image width (it used to overflow to the right as a single
    /// line and collide with the wrapping body text). Verified from the engine layout: the caption wraps to
    /// multiple lines and its rect stays within the image width.
    /// </summary>
    [TestMethod]
    public async Task UxB4_ImageCaption_WrapsWithinImageWidth()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenDocumentAsync(page, ContractDocumentId, OverviewBlockId);
        await WaitForObjectPresentAsync(page, LeftWrapImageId);

        var output = CreateOutputDirectory("b4-caption-wrap");
        var caption = await EvaluateWithContextRetryAsync<string>(page, 
            """
            objectId => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                return import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs').then(module => {
                    const debug = JSON.parse(module.getRuntimeDebugSnapshotJson(handle) || '{}');
                    const blocks = [...(debug?.layout?.blocks || []), ...(debug?.render?.selectionLayout?.blocks || [])];
                    const imgs = blocks.filter(b => b?.type === 'image' || b?.captionRect || b?.captionLines);
                    const block = imgs.find(b => String(b?.objectId || b?.blockId || '').includes('left-wrap-image'))
                        || imgs.find(b => Array.isArray(b?.captionLines) && b.captionLines.length > 0)
                        || imgs[0];
                    return JSON.stringify(block ? {
                        captionLineCount: Array.isArray(block.captionLines) ? block.captionLines.length : 0,
                        captionRectWidth: block.captionRect?.width || 0,
                        captionRectHeight: block.captionRect?.height || 0,
                        imageWidth: block.rect?.width || 0,
                    } : null);
                });
            }
            """,
            LeftWrapImageId);
        await ScreenshotAsync(page, Path.Combine(output, "00-caption.png"));

        await File.WriteAllTextAsync(Path.Combine(output, "manifest.json"), caption ?? "null");

        Assert.IsNotNull(caption, "the left-wrap image layout (with its caption) must be present in the debug snapshot.");
        using var doc = JsonDocument.Parse(caption!);
        var captionLineCount = doc.RootElement.GetProperty("captionLineCount").GetInt32();
        var captionRectWidth = doc.RootElement.GetProperty("captionRectWidth").GetDouble();
        var imageWidth = doc.RootElement.GetProperty("imageWidth").GetDouble();

        Assert.IsTrue(captionLineCount >= 2, $"B4: the long caption must wrap to multiple lines (got {captionLineCount}).");
        Assert.IsTrue(
            captionRectWidth <= imageWidth + 1,
            $"B4: the caption must not overflow the image width (caption {captionRectWidth:N0}px vs image {imageWidth:N0}px).");
    }

    /// <summary>
    /// B6 — editing a header/footer must light up the Header &amp; Footer contextual ribbon tab (so the
    /// page-number etc. fields become available) and the caret region must report Header/Footer; closing
    /// returns to the body and hides the tab. Driven through the engine's programmatic edit seam (a synthetic
    /// double-click into the footer band is unreliable on /document-editor).
    /// </summary>
    [TestMethod]
    public async Task UxB6_HeaderFooterEdit_ShowsContextualTab()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenDocumentAsync(page, ContractDocumentId, OverviewBlockId);

        var output = CreateOutputDirectory("b6-header-footer");

        // Enter the footer through the engine seam, then the debounced region sync lights up the contextual tab.
        await EditHeaderFooterViaInteropAsync(page, "Footer");
        await page.GetByTestId("document-ribbon-tab-header-footer").WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        var regionInFooter = await ReadSelectionRegionAsync(page);

        // The Header & Footer tab is active → its field menu (page number, …) is available.
        await page.GetByTestId("document-ribbon-tab-header-footer").ClickAsync();
        var fieldMenuVisible = await page.GetByTestId("document-header-footer-insert-field-menu").IsVisibleAsync();

        // Visual editing affordance (B6 scope 3): dim the body, dashed-frame the active slot, label the band.
        var overlay = await EvaluateWithContextRetryAsync<string>(page, 
            """
            () => JSON.stringify({
                label: !!document.querySelector('[data-testid="document-canvas-hf-label"]'),
                labelText: document.querySelector('[data-testid="document-canvas-hf-label"]')?.textContent || null,
                dim: document.querySelectorAll('[data-canvas-hf-dim]').length,
                frame: document.querySelectorAll('[data-canvas-hf-frame]').length
            })
            """);
        await ScreenshotAsync(page, Path.Combine(output, "00-header-footer-edit.png"));

        // Close header/footer → back to body, tab hidden.
        await CloseHeaderFooterViaInteropAsync(page);
        await page.WaitForFunctionAsync(
            "() => !document.querySelector('[data-testid=\"document-ribbon-tab-header-footer\"]')",
            new PageWaitForFunctionOptions { Timeout = 10_000 });
        var regionAfterClose = await ReadSelectionRegionAsync(page);

        await File.WriteAllTextAsync(
            Path.Combine(output, "manifest.json"),
            JsonSerializer.Serialize(new { problem = "B6 header/footer contextual tab", regionInFooter, fieldMenuVisible, overlay, regionAfterClose }, JsonWebIndented));

        Assert.AreEqual("Footer", regionInFooter, "editing the footer must report the Footer region.");
        Assert.IsTrue(fieldMenuVisible, "the Header & Footer tab must expose the field insertion menu (page number, …).");

        using var overlayDoc = JsonDocument.Parse(overlay);
        Assert.IsTrue(overlayDoc.RootElement.GetProperty("label").GetBoolean(), "the footer edit mode must show a slot label badge.");
        Assert.AreEqual("Footer", overlayDoc.RootElement.GetProperty("labelText").GetString(), "the slot label must read 'Footer'.");
        Assert.IsTrue(overlayDoc.RootElement.GetProperty("dim").GetInt32() >= 1, "the footer edit mode must dim the body content.");
        Assert.IsTrue(overlayDoc.RootElement.GetProperty("frame").GetInt32() >= 1, "the footer edit mode must dashed-frame the active slot.");

        Assert.AreEqual("Body", regionAfterClose, "closing header/footer must return the caret to the body.");
    }

    /// <summary>
    /// B10 — the demo footer must use a real page-number field, not the literal "Page 1" on every page.
    /// </summary>
    [TestMethod]
    public async Task UxB10_DemoFooter_UsesPageNumberField()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenDocumentAsync(page, ContractDocumentId, OverviewBlockId);

        var output = CreateOutputDirectory("b10-footer-field");
        var footer = await EvaluateWithContextRetryAsync<string>(page, 
            """
            () => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                return import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs').then(module => {
                    const model = JSON.parse(module.getModelJson(handle) || '{}');
                    const footers = (model?.headersFooters || []).filter(hf => String(hf?.type || '').toLowerCase() === 'footer');
                    const runs = footers.flatMap(hf => (hf?.blocks || []).flatMap(b => b?.content?.runs || []));
                    return JSON.stringify({
                        hasPageNumberField: runs.some(r => r?.field && String(r.field.fieldType || '').toLowerCase() === 'pagenumber'),
                        literalPageOne: runs.some(r => String(r?.text || '').includes('Page 1')),
                        runTypes: runs.map(r => r?.type || (r?.field ? 'field' : '?')),
                    });
                });
            }
            """);

        await File.WriteAllTextAsync(Path.Combine(output, "manifest.json"), footer);

        using var doc = JsonDocument.Parse(footer);
        Assert.IsTrue(doc.RootElement.GetProperty("hasPageNumberField").GetBoolean(), "the demo footer must contain a PageNumber field run.");
        Assert.IsFalse(doc.RootElement.GetProperty("literalPageOne").GetBoolean(), "the demo footer must not hard-code the literal \"Page 1\".");
    }

    /// <summary>
    /// B2 — after a mouse-drag selection the floating mini toolbar must stay visible (Word/GDocs behaviour).
    /// RED/flaky-RED before the fix: the trailing click on the editor root runs <c>CloseFloatingUi</c>, so the
    /// toolbar appears and is immediately dismissed.
    /// </summary>
    [TestMethod]
    public async Task UxB2_MiniToolbar_StaysAfterMouseSelection()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenDocumentAsync(page, ContractDocumentId, AgreementBlockId);

        var output = CreateOutputDirectory("b2-mini-toolbar");

        await SelectCanvasTextRangeAsync(page, AgreementBlockId, 0, 12);

        // Sample the toolbar's presence over ~2s rather than a single instantaneous check: a flicker (the bug)
        // shows up as intermittent samples, "gone" as zero, and the fixed "stays" as nearly all present. A
        // single IsVisible can race a transient Blazor re-render that briefly swaps the DOM node.
        var presence = await EvaluateWithContextRetryAsync<int[]>(page, 
            """
            async () => {
                const sleep = ms => new Promise(r => setTimeout(r, ms));
                let present = 0; const total = 20;
                for (let i = 0; i < total; i += 1) {
                    const el = document.querySelector('[data-testid="document-mini-toolbar"]');
                    if (el && el.getClientRects().length > 0) present += 1;
                    await sleep(100);
                }
                return [present, total];
            }
            """);
        await ScreenshotAsync(page, Path.Combine(output, "00-after-selection.png"));

        var presentSamples = presence[0];
        var totalSamples = presence[1];
        await File.WriteAllTextAsync(
            Path.Combine(output, "manifest.json"),
            JsonSerializer.Serialize(new { problem = "B2 mini toolbar flickers after mouse selection", presentSamples, totalSamples }, JsonWebIndented));

        Assert.IsTrue(
            presentSamples >= totalSamples - 1,
            $"B2 regression: the floating mini toolbar did not stay visible after a mouse selection (present in {presentSamples}/{totalSamples} samples over 2s — a flicker or the trailing root click closing it).");
    }

    /// <summary>
    /// B3 — a selected image must show the floating mini toolbar (Word/GDocs parity). The object is selected
    /// through the engine's programmatic selection seam (interop.selectObject) because a synthetic pointer
    /// click is unreliable on the full /document-editor (canvas reflow). This still drives the real pipeline:
    /// engine object selection → mini toolbar + UI-state push → C# renders the floating image toolbar.
    /// </summary>
    [TestMethod]
    public async Task UxB3_ImageSelection_ShowsToolbar()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenDocumentAsync(page, ContractDocumentId, OverviewBlockId);
        await WaitForObjectPresentAsync(page, LeftWrapImageId);
        await page.WaitForTimeoutAsync(500); // let the initial layout/render settle before selecting

        var output = CreateOutputDirectory("b3-image-toolbar");
        // Select via the engine seam, retrying until the object-selected attribute lands (the first render
        // after load can race the selection push).
        var selectResult = string.Empty;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            selectResult = await SelectCanvasObjectViaInteropAsync(page, LeftWrapImageId);
            try
            {
                await page.WaitForFunctionAsync(
                    """
                    objectId => {
                        const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                        return root?.getAttribute('data-canvas-object-selected') === 'true'
                            && root?.getAttribute('data-canvas-object-id') === objectId;
                    }
                    """,
                    LeftWrapImageId,
                    new PageWaitForFunctionOptions { Timeout = 2_500 });
                break;
            }
            catch (TimeoutException) when (attempt < 4)
            {
                await page.WaitForTimeoutAsync(300);
            }
        }

        var presence = await SampleMiniToolbarPresenceAsync(page);
        var toolbarMode = await EvaluateWithContextRetryAsync<string>(page, 
            "() => document.querySelector('[data-testid=\"document-mini-toolbar\"]')?.getAttribute('data-mini-toolbar-mode') || ''");
        var hasImagePanel = await page.GetByTestId("document-image-wrap-panel").CountAsync();
        await ScreenshotAsync(page, Path.Combine(output, "00-image-selected.png"));

        await File.WriteAllTextAsync(
            Path.Combine(output, "manifest.json"),
            JsonSerializer.Serialize(new { problem = "B3 image selection toolbar", selectResult, toolbarMode, hasImagePanel, presentSamples = presence[0], totalSamples = presence[1] }, JsonWebIndented));

        Assert.IsTrue(
            presence[0] >= presence[1] - 1,
            $"B3 regression: the mini toolbar did not stay visible for a selected image (present in {presence[0]}/{presence[1]} samples).");
        Assert.AreEqual("object", toolbarMode, "the mini toolbar must render in object mode for an image selection.");
        Assert.IsTrue(hasImagePanel >= 1, "the floating image wrap panel must render for a selected image.");
    }

    /// <summary>
    /// B8 — selecting text inside a table cell must show the floating mini toolbar, including the table
    /// quick-actions group. The range is selected through the engine's programmatic seam
    /// (interop.selectTextRange) because synthetic drags inside a scrolled-in cell are unreliable on
    /// /document-editor; this still drives the real pipeline (engine selection → bounding rect → C# toolbar).
    /// </summary>
    [TestMethod]
    public async Task UxB8_TableCellSelection_ShowsToolbar()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenDocumentAsync(page, ContractDocumentId, OverviewBlockId);

        var output = CreateOutputDirectory("b8-table-toolbar");

        // The pricing table is on a later page; scroll it into view first so its page is mounted (the mini
        // toolbar can only anchor to a mounted page's geometry), let the scroll repaint settle, then select
        // the cell text via the engine seam.
        const string cellBlockId = "contract-pricing-table-r1-item-block";
        await ScrollUntilTextRectAsync(page, cellBlockId);
        await page.WaitForTimeoutAsync(500);
        var selectResult = await SelectCanvasTextRangeViaInteropAsync(page, cellBlockId, 0, 6);
        // The in-table flag is pushed to the root through the debounced C# selection sync, which can lag under
        // sustained full-suite load — give it room and re-assert the engine selection once if it slips.
        await page.WaitForTimeoutAsync(300);
        try
        {
            await page.WaitForFunctionAsync(
                """
                () => document.querySelector('[data-testid="document-canvas-engine-root"]')
                    ?.getAttribute('data-canvas-selection-in-table') === 'true'
                """,
                new PageWaitForFunctionOptions { Timeout = 15_000 });
        }
        catch (PlaywrightException)
        {
            await SelectCanvasTextRangeViaInteropAsync(page, cellBlockId, 0, 6);
            await page.WaitForFunctionAsync(
                """
                () => document.querySelector('[data-testid="document-canvas-engine-root"]')
                    ?.getAttribute('data-canvas-selection-in-table') === 'true'
                """,
                new PageWaitForFunctionOptions { Timeout = 15_000 });
        }

        var presence = await SampleMiniToolbarPresenceAsync(page);
        var hasTableActions = await page.GetByTestId("document-mini-table-row-after").CountAsync();
        await ScreenshotAsync(page, Path.Combine(output, "00-cell-selected.png"));

        await File.WriteAllTextAsync(
            Path.Combine(output, "manifest.json"),
            JsonSerializer.Serialize(new { problem = "B8 table cell selection toolbar", selectResult, hasTableActions, presentSamples = presence[0], totalSamples = presence[1] }, JsonWebIndented));

        Assert.IsTrue(
            presence[0] >= presence[1] - 1,
            $"B8 regression: the mini toolbar did not stay visible for a table-cell selection (present in {presence[0]}/{presence[1]} samples).");
        Assert.IsTrue(hasTableActions >= 1, "the mini toolbar must show table quick-actions for a table-cell selection.");
    }

    /// <summary>
    /// B4 / B5 / B10 baseline screenshots — the printscreen scenario (caption overlap), the page-1 view after a
    /// scroll roundtrip, and the demo footer that prints the "Page 1" literal. Captured for the before/after UX
    /// review; this test does not assert behaviour.
    /// </summary>
    [TestMethod]
    public async Task UxBaseline_Screenshots()
    {
        var output = CreateOutputDirectory("baseline");

        // Caption overlap (B4) + footer "Page 1" literal (B10): full contract demo at two scroll positions.
        var contractContext = await CreateContextAsync();
        var contractPage = await contractContext.NewPageAsync();
        await contractPage.SetViewportSizeAsync(1440, 1000);
        await OpenDocumentAsync(contractPage, ContractDocumentId, OverviewBlockId);
        await contractPage.WaitForTimeoutAsync(500);
        await ScreenshotAsync(contractPage, Path.Combine(output, "contract-top.png"));

        var (cx, cy) = await ReadViewportCenterAsync(contractPage);
        await contractPage.Mouse.MoveAsync((float)cx, (float)cy);
        for (var i = 0; i < 10; i++)
        {
            await contractPage.Mouse.WheelAsync(0, 1200);
            await contractPage.WaitForTimeoutAsync(120);
        }
        await contractPage.WaitForTimeoutAsync(400);
        await ScreenshotAsync(contractPage, Path.Combine(output, "contract-footer-page2.png"));

        // Page-1 view of a long document after a scroll roundtrip (B5).
        var largeContext = await CreateContextAsync();
        var largePage = await largeContext.NewPageAsync();
        await largePage.SetViewportSizeAsync(1280, 720);
        await OpenDocumentAsync(largePage, LargeDocumentId, blockId: null);
        var (lx, ly) = await ReadViewportCenterAsync(largePage);
        await largePage.Mouse.MoveAsync((float)lx, (float)ly);
        for (var i = 0; i < 14; i++)
        {
            await largePage.Mouse.WheelAsync(0, 1400);
            await largePage.WaitForTimeoutAsync(120);
        }
        for (var i = 0; i < 20; i++)
        {
            await largePage.Mouse.WheelAsync(0, -1400);
            await largePage.WaitForTimeoutAsync(120);
        }
        await largePage.WaitForTimeoutAsync(400);
        await ScreenshotAsync(largePage, Path.Combine(output, "large-page1-after-roundtrip.png"));

        Assert.IsTrue(
            new FileInfo(Path.Combine(output, "contract-top.png")).Length > 5_000,
            "Baseline screenshots must be real PNGs.");
    }

    // ---- helpers ----

    private async Task OpenDocumentAsync(IPage page, string documentId, string? blockId)
    {
        var url = $"{BaseUrl}/document-editor?documentId={documentId}";
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                await page.GotoAsync(url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 120_000
                });
                await WaitForCanvasDocumentReadyAsync(page, blockId);
                return;
            }
            catch (TimeoutException) when (attempt == 0)
            {
                await TryResetCanvasDocumentNavigationAsync(page);
            }
        }
    }

    private static async Task WaitForCanvasDocumentReadyAsync(IPage page, string? blockId)
    {
        await page.WaitForSelectorAsync(
            "[data-testid='document-canvas-engine-host'][data-canvas-engine-ready='true']",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 120_000 });
        if (!string.IsNullOrEmpty(blockId))
        {
            await page.WaitForFunctionAsync(
                """
                blockId => document.querySelectorAll(`[data-canvas-text-rect][data-block-id="${blockId}"]`).length >= 1
                    && document.querySelector('[data-testid="document-bold"]')
                """,
                blockId,
                new PageWaitForFunctionOptions { Timeout = 60_000 });
        }
        else
        {
            await page.WaitForFunctionAsync(
                "() => document.querySelectorAll('[data-canvas-text-rect]').length >= 1",
                new PageWaitForFunctionOptions { Timeout = 60_000 });
        }
    }

    private static async Task TryResetCanvasDocumentNavigationAsync(IPage page)
    {
        try
        {
            await page.GotoAsync("about:blank", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 10_000
            });
        }
        catch (TimeoutException)
        {
            // The retry below is the authoritative readiness check for these canvas UX tests.
        }
    }

    private static Task<WrappedLineProbe> FindWrappedContinuationLineAsync(IPage page)
        => EvaluateWithContextRetryAsync<WrappedLineProbe>(page, 
            """
            () => {
                const rects = Array.from(document.querySelectorAll('[data-canvas-text-rect]'))
                    .map(node => {
                        const r = node.getBoundingClientRect();
                        return {
                            blockId: node.getAttribute('data-block-id') || '',
                            left: r.left, right: r.right, top: r.top, bottom: r.bottom,
                            cy: r.top + r.height / 2
                        };
                    })
                    .filter(item => item.right - item.left > 1);
                // Group by block, then by visual row (distinct top within 4px).
                const byBlock = new Map();
                for (const item of rects) {
                    if (!byBlock.has(item.blockId)) byBlock.set(item.blockId, []);
                    byBlock.get(item.blockId).push(item);
                }
                for (const [blockId, items] of byBlock) {
                    const rows = [];
                    for (const item of items.sort((a, b) => a.top - b.top || a.left - b.left)) {
                        let row = rows.find(rr => Math.abs(rr.top - item.top) < 4);
                        if (!row) { row = { top: item.top, bottom: item.bottom, minLeft: item.left, maxRight: item.right, cy: item.cy }; rows.push(row); }
                        else { row.minLeft = Math.min(row.minLeft, item.left); row.maxRight = Math.max(row.maxRight, item.right); }
                    }
                    if (rows.length >= 2) {
                        const second = rows[1];
                        return {
                            found: true,
                            blockId,
                            clickX: (second.minLeft + second.maxRight) / 2,
                            clickY: second.cy,
                            lineTop: second.top,
                            lineStartX: second.minLeft,
                            debug: `block=${blockId} rows=${rows.length}`
                        };
                    }
                }
                return { found: false, blockId: '', clickX: 0, clickY: 0, lineTop: 0, lineStartX: 0, debug: `blocks=${byBlock.size}` };
            }
            """);

    private static Task<CaretRect> ReadCaretRectAsync(IPage page)
        => EvaluateWithContextRetryAsync<CaretRect>(page, 
            """
            () => {
                const caret = document.querySelector('[data-canvas-caret][data-testid="document-canvas-caret"]')
                    || document.querySelector('[data-canvas-caret]');
                if (!caret) return { found: false, x: 0, y: 0, height: 0 };
                const r = caret.getBoundingClientRect();
                return { found: true, x: r.left, y: r.top + r.height / 2, height: r.height };
            }
            """);

    // Samples the mini toolbar's presence over ~1.5s (15 × 100ms): distinguishes "stays" (≈all) from a
    // flicker (intermittent) and "never shown" (zero), without racing a transient Blazor re-render.
    private static async Task<int[]> SampleMiniToolbarPresenceAsync(IPage page, int total = 15)
    {
        // Sample per-tick from C# (one short evaluate each) so a single canvas re-paint that tears down the JS
        // execution context costs at most one retried sample instead of failing the whole 1.5s in-browser loop.
        var present = 0;
        for (var i = 0; i < total; i++)
        {
            bool visible;
            try
            {
                visible = await EvaluateWithContextRetryAsync<bool>(page, 
                    """
                    () => {
                        const el = document.querySelector('[data-testid="document-mini-toolbar"]');
                        return !!el && el.getClientRects().length > 0;
                    }
                    """);
            }
            catch (PlaywrightException)
            {
                // Execution context destroyed by a repaint — retry this sample once after a short settle.
                await page.WaitForTimeoutAsync(100);
                try
                {
                    visible = await EvaluateWithContextRetryAsync<bool>(page, 
                        """
                        () => {
                            const el = document.querySelector('[data-testid="document-mini-toolbar"]');
                            return !!el && el.getClientRects().length > 0;
                        }
                        """);
                }
                catch (PlaywrightException)
                {
                    visible = false;
                }
            }

            if (visible)
            {
                present += 1;
            }

            await page.WaitForTimeoutAsync(100);
        }

        return [present, total];
    }

    // Opens the text context menu at a block offset (right-click over the text). The selection is expected to
    // already be set (e.g. via the interop seam); the right-click does not move it (pointer-down ignores button 2).
    private static async Task OpenTextContextMenuAtAsync(IPage page, string blockId, int offset)
    {
        var point = await ReadCanvasPointAsync(page, blockId, offset);
        await page.Mouse.ClickAsync((float)point.X, (float)point.Y, new MouseClickOptions { Button = MouseButton.Right });
        await page.GetByTestId("document-text-context-menu").WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
    }

    // Invokes a testid element's click handler directly (bypasses pointer actionability — the floating menu
    // re-renders from the debounced toolbar sync, which trips Playwright's stability check even though a real
    // user click lands fine, the backdrop sitting a z-index below the menu).
    private static Task ClickTestIdViaJsAsync(IPage page, string testId)
        => EvaluateWithContextRetryAsync(page, 
            "id => document.querySelector(`[data-testid=\"${id}\"]`)?.click()",
            testId);

    // Reads a block's plain text from the engine model (concatenated run text).
    private static Task<string> ReadBlockTextAsync(IPage page, string blockId)
        => EvaluateWithContextRetryAsync<string>(page, 
            """
            blockId => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                return import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs').then(module => {
                    const model = JSON.parse(module.getModelJson(handle) || '{}');
                    const block = (model?.body?.blocks || []).find(b => String(b?.id || '') === blockId);
                    return (block?.content?.runs || []).map(r => String(r?.text || '')).join('');
                });
            }
            """,
            blockId);

    // B6: enter/exit header-footer editing through the engine seam (a synthetic dblclick into the band is
    // unreliable on /document-editor); still drives the real region push → contextual tab.
    private static Task EditHeaderFooterViaInteropAsync(IPage page, string type)
        => EvaluateWithContextRetryAsync(page, 
            """
            type => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                return import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs')
                    .then(module => module.editHeaderFooter(handle, type));
            }
            """,
            type);

    private static Task CloseHeaderFooterViaInteropAsync(IPage page)
        => EvaluateWithContextRetryAsync(page, 
            """
            () => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                return import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs')
                    .then(module => module.closeHeaderFooter(handle));
            }
            """);

    private static Task<string> ReadSelectionRegionAsync(IPage page)
        => EvaluateWithContextRetryAsync<string>(page, 
            """
            () => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                return import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs')
                    .then(module => JSON.parse(module.getSelectionStateJson(handle) || '{}').region || 'Body');
            }
            """);

    private static Task WaitForObjectPresentAsync(IPage page, string objectId)
        => page.WaitForFunctionAsync(
            "objectId => !!document.querySelector(`[data-canvas-object][data-object-id=\"${objectId}\"]`)",
            objectId,
            new PageWaitForFunctionOptions { Timeout = 30_000 });

    // Wheel-scrolls the canvas until a block's text rect mounts (virtualization mounts only visible pages) so
    // the mini toolbar can anchor to the block's page geometry.
    private static async Task ScrollUntilTextRectAsync(IPage page, string blockId)
    {
        var (cx, cy) = await ReadViewportCenterAsync(page);
        await page.Mouse.MoveAsync((float)cx, (float)cy);
        for (var i = 0; i < 24; i++)
        {
            bool found;
            try
            {
                found = await EvaluateWithContextRetryAsync<bool>(page, 
                    "blockId => document.querySelectorAll(`[data-canvas-text-rect][data-block-id=\"${blockId}\"]`).length >= 1",
                    blockId);
            }
            catch (PlaywrightException) when (i < 23)
            {
                // A canvas re-paint can tear down the JS execution context mid-evaluate ("Execution context was
                // destroyed, most likely because of a navigation"). Let the page settle and retry the probe.
                await page.WaitForTimeoutAsync(150);
                continue;
            }

            if (found)
            {
                return;
            }

            await page.Mouse.WheelAsync(0, 700);
            await page.WaitForTimeoutAsync(150);
        }

        throw new InvalidOperationException($"Could not scroll block {blockId} into view.");
    }

    // The first evaluate after the canvas flips ready can race a transient execution-context swap (the
    // engine mounts a fresh render pass on an idle continuation; window state survives — verified by
    // probe). Retry the call a few times instead of failing the test on "Execution context was destroyed".
    private static Task EvaluateWithContextRetryAsync(IPage page, string expression, object? arg = null)
        => EvaluateWithContextRetryAsync<object?>(page, expression, arg);

    private static async Task<T> EvaluateWithContextRetryAsync<T>(IPage page, string expression, object? arg = null)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await page.EvaluateAsync<T>(expression, arg);
            }
            catch (PlaywrightException ex) when (attempt < 4
                && (ex.Message.Contains("Execution context was destroyed", StringComparison.Ordinal)
                    || ex.Message.Contains("because of a navigation", StringComparison.Ordinal)))
            {
                await page.WaitForTimeoutAsync(250);
            }
        }
    }

    // Selects an image/drawing object through the engine's programmatic seam (interop.selectObject), avoiding
    // the unreliable synthetic pointer click on the full editor while still driving the real selection push.
    private static Task<string> SelectCanvasObjectViaInteropAsync(IPage page, string objectId)
        => EvaluateWithContextRetryAsync<string>(page,
            """
            objectId => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                return import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs')
                    .then(module => module.selectObject(handle, objectId) || '');
            }
            """,
            objectId);

    // Selects a text range (blockId[start..end]) through the engine's programmatic seam (interop.selectTextRange).
    private static Task<string> SelectCanvasTextRangeViaInteropAsync(IPage page, string blockId, int startOffset, int endOffset)
        => EvaluateWithContextRetryAsync<string>(page,
            """
            ([blockId, startOffset, endOffset]) => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                return import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs')
                    .then(module => module.selectTextRange(handle, blockId, startOffset, endOffset) || '');
            }
            """,
            new object[] { blockId, startOffset, endOffset });

    private static Task<int[]> ReadPageDomOrderAsync(IPage page)
        => EvaluateWithContextRetryAsync<int[]>(page, 
            """
            () => Array.from(document.querySelectorAll('[data-testid="document-canvas-page"]'))
                .map(node => Number(node.getAttribute('data-page-index') || '-1'))
            """);

    private static bool IsStrictlyAscending(int[] values)
    {
        for (var i = 1; i < values.Length; i++)
        {
            if (values[i] <= values[i - 1])
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<(double X, double Y)> ReadViewportCenterAsync(IPage page)
    {
        var box = await EvaluateWithContextRetryAsync<CenterPoint>(page, 
            """
            () => {
                const page = document.querySelector('[data-testid="document-canvas-page"]')
                    || document.querySelector('[data-testid="document-canvas-engine-root"]');
                const r = page.getBoundingClientRect();
                return { x: r.left + r.width / 2, y: Math.min(window.innerHeight - 40, r.top + 200) };
            }
            """);
        return (box.X, box.Y);
    }

    private static async Task SelectCanvasTextRangeAsync(IPage page, string blockId, int startOffset, int endOffset)
    {
        // Read both drag endpoints in one evaluate, after scrolling the selection into view once, so the two
        // points are expressed in the same (post-scroll) viewport frame. Two separate ReadCanvasPointAsync
        // calls would each scroll and leave the first endpoint stale.
        var range = await EvaluateWithContextRetryAsync<CanvasRangePoints>(page, 
            """
            ([blockId, startOffset, endOffset]) => {
                const items = Array.from(document.querySelectorAll(`[data-canvas-text-rect][data-block-id="${blockId}"]`))
                    .map(node => ({
                        node,
                        start: Number(node.getAttribute('data-canvas-start-offset') || '0'),
                        end: Number(node.getAttribute('data-canvas-end-offset') || '0')
                    }))
                    .filter(item => item.end > item.start);
                if (!items.length) throw new Error(`No canvas text rects found for ${blockId}.`);
                const startItem = items.find(item => startOffset >= item.start && startOffset <= item.end) || items[0];
                const endItem = items.find(item => endOffset >= item.start && endOffset <= item.end) || items[items.length - 1];
                // Center the midpoint rect so both endpoints land inside the viewport even on wrapped selections.
                const mid = items[Math.floor(items.indexOf(endItem) >= items.indexOf(startItem)
                    ? (items.indexOf(startItem) + items.indexOf(endItem)) / 2
                    : items.indexOf(startItem))] || startItem;
                mid.node.scrollIntoView({ block: 'center', inline: 'center', behavior: 'instant' });
                const point = (item, offset) => {
                    const rect = item.node.getBoundingClientRect();
                    const ratio = Math.max(0, Math.min(1, (offset - item.start) / Math.max(1, item.end - item.start)));
                    return { x: rect.left + Math.max(2, rect.width * ratio), y: rect.top + rect.height / 2 };
                };
                const s = point(startItem, startOffset);
                const e = point(endItem, endOffset);
                return { startX: s.x, startY: s.y, endX: e.x, endY: e.y };
            }
            """,
            new object[] { blockId, startOffset, endOffset });
        var start = new CanvasPoint { X = range.StartX, Y = range.StartY };
        var end = new CanvasPoint { X = range.EndX, Y = range.EndY };
        await page.Mouse.MoveAsync((float)start.X, (float)start.Y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)end.X, (float)end.Y, new MouseMoveOptions { Steps = 10 });
        await page.Mouse.UpAsync();
        await page.WaitForFunctionAsync(
            """
            blockId => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                return root?.getAttribute('data-canvas-selection-collapsed') === 'false'
                    && document.querySelectorAll('[data-testid="document-canvas-selection-rect"]').length >= 1;
            }
            """,
            blockId,
            new PageWaitForFunctionOptions { Timeout = 10_000 });
    }

    private static async Task ClickCanvasOffsetAsync(IPage page, string blockId, int offset)
    {
        var point = await ReadCanvasPointAsync(page, blockId, offset);
        await page.Mouse.ClickAsync((float)point.X, (float)point.Y);
    }

    private static Task WaitForCollapsedCaretAsync(IPage page)
        => page.WaitForFunctionAsync(
            """
            () => document.querySelector('[data-testid="document-canvas-engine-root"]')
                ?.getAttribute('data-canvas-selection-collapsed') === 'true'
            """,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task<CanvasPoint> ReadCanvasPointAsync(IPage page, string blockId, int offset)
        => EvaluateWithContextRetryAsync<CanvasPoint>(page, 
            """
            ([blockId, offset]) => {
                const items = Array.from(document.querySelectorAll(`[data-canvas-text-rect][data-block-id="${blockId}"]`))
                    .map(node => ({
                        node,
                        start: Number(node.getAttribute('data-canvas-start-offset') || '0'),
                        end: Number(node.getAttribute('data-canvas-end-offset') || '0')
                    }))
                    .filter(item => item.end > item.start);
                if (!items.length) throw new Error(`No canvas text rects found for ${blockId}.`);
                const target = items.find(item => offset >= item.start && offset <= item.end) || items[0];
                // The canvas is virtualized: a mounted text rect can still sit below the fold, which would make
                // getBoundingClientRect() return a y outside the viewport and the mouse op land on nothing.
                target.node.scrollIntoView({ block: 'center', inline: 'center', behavior: 'instant' });
                const rect = target.node.getBoundingClientRect();
                const ratio = Math.max(0, Math.min(1, (offset - target.start) / Math.max(1, target.end - target.start)));
                return {
                    x: rect.left + Math.max(2, rect.width * ratio),
                    y: rect.top + rect.height / 2
                };
            }
            """,
            new object[] { blockId, offset });

    private static async Task<int> ReadIntAttrAsync(IPage page, string attr)
        => await EvaluateWithContextRetryAsync<int>(page, 
            $"() => Number(document.querySelector('[data-testid=\"document-canvas-engine-root\"]')?.getAttribute('{attr}') || '0')");

    private static Task ScreenshotAsync(IPage page, string path)
        => page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = path,
            Type = ScreenshotType.Png
        });

    private static string CreateOutputDirectory(string scenario)
    {
        var output = Path.Combine(
            FindRepositoryRoot().FullName,
            "tests", "Tempo.Blazor.E2E", "TestResults", "document-editor-canvas",
            "ux-fix", "2026-06-11", scenario);
        Directory.CreateDirectory(output);
        return output;
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
            {
                return current;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx from test output directory.");
    }

    private static readonly JsonSerializerOptions JsonWebIndented =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private sealed class WrappedLineProbe
    {
        [JsonPropertyName("found")] public bool Found { get; set; }
        [JsonPropertyName("blockId")] public string BlockId { get; set; } = string.Empty;
        [JsonPropertyName("clickX")] public double ClickX { get; set; }
        [JsonPropertyName("clickY")] public double ClickY { get; set; }
        [JsonPropertyName("lineTop")] public double LineTop { get; set; }
        [JsonPropertyName("lineStartX")] public double LineStartX { get; set; }
        [JsonPropertyName("debug")] public string Debug { get; set; } = string.Empty;
    }

    private sealed class CaretRect
    {
        [JsonPropertyName("found")] public bool Found { get; set; }
        [JsonPropertyName("x")] public double X { get; set; }
        [JsonPropertyName("y")] public double Y { get; set; }
        [JsonPropertyName("height")] public double Height { get; set; }
    }

    private sealed class CanvasPoint
    {
        [JsonPropertyName("x")] public double X { get; set; }
        [JsonPropertyName("y")] public double Y { get; set; }
    }

    private sealed class CanvasRangePoints
    {
        [JsonPropertyName("startX")] public double StartX { get; set; }
        [JsonPropertyName("startY")] public double StartY { get; set; }
        [JsonPropertyName("endX")] public double EndX { get; set; }
        [JsonPropertyName("endY")] public double EndY { get; set; }
    }

    private sealed class CenterPoint
    {
        [JsonPropertyName("x")] public double X { get; set; }
        [JsonPropertyName("y")] public double Y { get; set; }
    }
}
