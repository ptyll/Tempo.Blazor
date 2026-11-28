using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Playwright;
using static Tempo.Blazor.E2E.DocumentEditorRedesignE2EHelpers;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Redesign plan 146c7e1b, phase 12: the viewport-bounded frame. A host that gives the editor a <c>Height</c> (the demo:
/// <c>/canvas-engine-host?height=viewport</c>) gets a frame whose scroller is the document region of the workspace - the status
/// bar is pinned under it, the ribbon stays on top, the page itself never scrolls and the horizontal scrollbar of a page wider than its
/// column sits at the bottom of what the reader sees. Everything runs on the 19-page performance document, so "page 10" and "page 11"
/// are real pages the virtualizer has to mount. Without <c>Height</c> nothing changes (the unbounded spec at the end).
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[TestCategory("DocumentEditor:BoundedFrame")]
[DoNotParallelize]
public sealed class DocumentEditorBoundedFrameE2ETests : WasmTestBase
{
    private const string LongDocumentId = "phase-22-canvas-performance";

    [TestInitialize]
    public Task ResetDocumentEditorDemoAsync()
        => DocumentEditorE2EReset.ResetAsync();

    [TestMethod]
    [Description("1440x900: the status bar is on screen at load and after scrolling to page 10, and the page itself never scrolls")]
    public async Task Bounded_StatusBarStaysPinned_AndThePageDoesNotScroll()
    {
        var (page, errors) = await OpenAsync(1440, 900, "height=viewport");

        var atLoad = await ReadFrameAsync(page);
        Assert.AreEqual("bounded", atLoad.Frame, "the root carries data-frame=bounded");
        Assert.AreEqual("true", atLoad.ScrollViewportMarker, "the document region is the marked scroll viewport");
        Assert.IsTrue(atLoad.PageCount >= 12, $"the spec needs a long document, got {atLoad.PageCount} pages");
        Assert.IsTrue(atLoad.StatusBarBottom <= atLoad.InnerHeight + 0.5, $"status bar bottom {atLoad.StatusBarBottom} within the viewport {atLoad.InnerHeight}");
        Assert.IsTrue(atLoad.StatusBarTop > atLoad.WorkspaceTop, "the status bar is under the workspace");
        Assert.IsTrue(atLoad.PageScrollHeight <= atLoad.InnerHeight + 1, $"the page does not scroll: scrollHeight {atLoad.PageScrollHeight} vs innerHeight {atLoad.InnerHeight}");
        Assert.AreEqual("visible", atLoad.CanvasHostOverflowX, "the canvas host is no scroller of its own in a bounded frame");

        await ScrollToPageAsync(page, 9);
        var onPage10 = await ReadFrameAsync(page);
        Assert.IsTrue(onPage10.ScrollerTop > 0, "the document region scrolled");
        Assert.AreEqual(0, onPage10.PageScrollTop, "the page itself did not scroll");
        Assert.IsTrue(onPage10.StatusBarBottom <= onPage10.InnerHeight + 0.5, "the status bar is still on screen on page 10");
        Assert.IsTrue(onPage10.RibbonTop >= -0.5 && onPage10.RibbonBottom <= onPage10.WorkspaceTop + 1, "the ribbon stays above the scroller");
        Assert.IsTrue(onPage10.PageScrollHeight <= onPage10.InnerHeight + 1, "the page still does not scroll");

        AssertNoClientErrors(errors, "bounded frame");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("Scrolling the workspace to page 8 mounts and paints pages 7-9 (the virtualizer follows the marked scroller, not the window)")]
    public async Task Bounded_ScrollingToPage8_MountsAndPaintsPages7To9()
    {
        var (page, errors) = await OpenAsync(1440, 900, "height=viewport");
        var before = await ReadMountedPagesAsync(page);
        Assert.IsFalse(before.Indexes.Contains(7), $"page 8 is not mounted at load ({string.Join(",", before.Indexes)})");

        await ScrollToPageAsync(page, 7);

        await page.WaitForFunctionAsync(
            """
            () => {
                const mounted = Array.from(document.querySelectorAll('[data-testid="document-canvas-page"][data-page-index]'))
                    .filter(p => Number(p.getAttribute('data-canvas-painted-command-count') || '0') > 0)
                    .map(p => Number(p.getAttribute('data-page-index')));
                return [6, 7, 8].every(i => mounted.includes(i));
            }
            """,
            null,
            new PageWaitForFunctionOptions { Timeout = 20_000 });
        var after = await ReadMountedPagesAsync(page);
        CollectionAssert.IsSubsetOf(new[] { 6, 7, 8 }, after.PaintedIndexes.ToList(), $"pages 7-9 are painted ({string.Join(",", after.PaintedIndexes)})");
        Assert.IsTrue(after.Indexes.Length < after.PageCount, "the virtualizer still mounts only a window of the document");
        AssertNoClientErrors(errors, "scroll to page 8");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("Find 'go to result' on a page far below moves the workspace scroller (not the window) to the match")]
    public async Task Bounded_FindGoToResult_OnALatePage_ScrollsTheWorkspace()
    {
        var (page, errors) = await OpenAsync(1440, 900, "height=viewport");
        var stride = (await ReadFrameAsync(page)).PageStride;

        await page.GetByTestId("document-canvas-engine-host").ClickAsync();
        await page.Keyboard.PressAsync("Control+F");
        await Assertions.Expect(page.GetByTestId("document-find-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await page.GetByTestId("document-find-input").FillAsync("Performance paragraph 140.");
        await Assertions.Expect(page.GetByTestId("document-find-result").First).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await page.GetByTestId("document-find-result").First.ClickAsync();

        // the match is on a page above number 11 (paragraph 140 of 180 lies around page 15): the workspace moved there and the match is on screen
        await page.WaitForFunctionAsync(
            """
            stride => {
                const region = document.querySelector('[data-region="doc"][data-scroll-viewport="true"]');
                const active = document.querySelector('.tm-document-canvas-search-highlight--active');
                if (!region || !active || region.scrollTop < 10 * stride) return false;
                const view = region.getBoundingClientRect();
                const rect = active.getBoundingClientRect();
                return rect.top >= view.top - 1 && rect.bottom <= view.bottom + 1;
            }
            """,
            stride,
            new PageWaitForFunctionOptions { Timeout = 25_000 });
        var frame = await ReadFrameAsync(page);
        Assert.AreEqual(0, frame.PageScrollTop, "the window did not scroll");
        Assert.IsTrue(frame.StatusBarBottom <= frame.InnerHeight + 0.5, "the status bar is still on screen");
        AssertNoClientErrors(errors, "find go to result");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("PageFitMode.Off at a 1048 px column: the horizontal scrollbar of the canvas is at the bottom of the visible workspace, not at the end of the document")]
    public async Task Bounded_HorizontalScrollbar_IsReachableWithoutScrollingToTheEnd()
    {
        var (page, errors) = await OpenAsync(1440, 900, "height=viewport&pageFit=off");

        var probe = await page.EvaluateAsync<JsonElement>(
            """
            () => {
                const region = document.querySelector('[data-region="doc"][data-scroll-viewport="true"]');
                const rect = region.getBoundingClientRect();
                return {
                    scrollWidth: region.scrollWidth, clientWidth: region.clientWidth,
                    scrollTop: region.scrollTop,
                    regionBottom: rect.bottom, innerHeight: window.innerHeight,
                    statusTop: document.querySelector('[data-testid="document-status-bar"]').getBoundingClientRect().top,
                    overflowX: getComputedStyle(region).overflowX,
                    column: document.querySelector('.tm-document-editor').getBoundingClientRect().width
                };
            }
            """);
        var column = probe.GetProperty("column").GetDouble();
        var scrollWidth = probe.GetProperty("scrollWidth").GetDouble();
        var clientWidth = probe.GetProperty("clientWidth").GetDouble();
        TestContext.WriteLine($"editor {column}px, region client {clientWidth} scroll {scrollWidth}");
        Assert.IsTrue(column is >= 1040 and <= 1120, $"the spec is about the demo's ~1048 px column, the editor is {column}px wide");
        Assert.IsTrue(scrollWidth > clientWidth + 1, $"with the fit off the page is wider than its column and the region scrolls horizontally ({scrollWidth} > {clientWidth})");
        Assert.AreEqual("auto", probe.GetProperty("overflowX").GetString());
        Assert.AreEqual(0, probe.GetProperty("scrollTop").GetDouble(), "still at the top of the document");
        Assert.IsTrue(probe.GetProperty("regionBottom").GetDouble() <= probe.GetProperty("innerHeight").GetDouble() + 0.5, "the bottom edge of the scroller (with its scrollbar) is on screen");
        Assert.IsTrue(probe.GetProperty("regionBottom").GetDouble() <= probe.GetProperty("statusTop").GetDouble() + 1, "the scroller ends where the pinned status bar begins - its scrollbar is not under the bar");

        // (Playwright's headless Chromium hides scrollbars, so the bar's thickness cannot be asserted: the scroller being the element that scrolls
        // BOTH ways, bounded by the frame and on screen, is what puts the bar at the bottom of what the reader sees.)
        await page.EvaluateAsync("() => { document.querySelector('[data-region=\"doc\"][data-scroll-viewport=\"true\"]').scrollLeft = 120; }");
        var scrolledLeft = await page.EvaluateAsync<double>("() => document.querySelector('[data-region=\"doc\"][data-scroll-viewport=\"true\"]').scrollLeft");
        Assert.IsTrue(scrolledLeft > 0, "the document scrolls sideways at the top of the document");
        AssertNoClientErrors(errors, "horizontal scrollbar");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("A phone (390): the action bar is in flow under the workspace, visible, and never covers the last visible line of the document")]
    public async Task Bounded_Phone_ActionBarIsVisible_AndDoesNotCoverTheDocument()
    {
        var (page, errors) = await OpenAsync(390, 844, "height=viewport", touch: true);

        var probe = await page.EvaluateAsync<JsonElement>(
            """
            () => {
                const bar = document.querySelector('[data-testid="document-mobile-action-bar"]');
                const workspace = document.querySelector('[data-testid="document-editor-workspace"]');
                const barRect = bar.getBoundingClientRect();
                const wsRect = workspace.getBoundingClientRect();
                const position = getComputedStyle(bar.closest('.tm-document-editor__action-bar') || bar).position;
                return {
                    barTop: barRect.top, barBottom: barRect.bottom, wsBottom: wsRect.bottom, innerHeight: window.innerHeight,
                    position, scrollHeight: document.scrollingElement.scrollHeight,
                    frame: document.querySelector('.tm-document-editor').getAttribute('data-frame')
                };
            }
            """);
        Assert.AreEqual("bounded", probe.GetProperty("frame").GetString());
        Assert.IsTrue(probe.GetProperty("barBottom").GetDouble() <= probe.GetProperty("innerHeight").GetDouble() + 0.5, "the action bar is on screen");
        Assert.IsTrue(probe.GetProperty("barTop").GetDouble() >= probe.GetProperty("wsBottom").GetDouble() - 1, "the bar starts where the workspace ends: it does not cover the last row");
        Assert.AreNotEqual("fixed", probe.GetProperty("position").GetString(), "the bar is part of the frame, not fixed over the content");
        Assert.IsTrue(probe.GetProperty("scrollHeight").GetDouble() <= probe.GetProperty("innerHeight").GetDouble() + 1, "the phone page does not scroll");
        AssertNoClientErrors(errors, "phone");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("A viewport resize re-bounds the frame: the status bar follows the new height, no stale pixel size")]
    public async Task Bounded_ViewportResize_KeepsTheStatusBarOnScreen()
    {
        var (page, errors) = await OpenAsync(1440, 900, "height=viewport");

        await page.SetViewportSizeAsync(1440, 640);
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-testid=\"document-status-bar\"]').getBoundingClientRect().bottom <= window.innerHeight + 0.5",
            null,
            new PageWaitForFunctionOptions { Timeout = 10_000 });
        var frame = await ReadFrameAsync(page);
        Assert.IsTrue(frame.PageScrollHeight <= frame.InnerHeight + 1, "the page does not scroll after the resize either");
        AssertNoClientErrors(errors, "resize");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("Fullscreen is the same bounded frame at the viewport height: root fixed, status bar pinned at the bottom of the screen")]
    public async Task Fullscreen_IsTheBoundedFrame_WithThePinnedStatusBar()
    {
        var (page, errors) = await OpenAsync(1440, 900, null);
        var unbounded = await ReadFrameAsync(page);
        Assert.IsNull(unbounded.Frame, "without Height the editor is not a bounded frame");

        await page.GetByTestId("document-ribbon-tab-view").ClickAsync();
        await page.GetByTestId("document-fullscreen").ClickAsync();
        await WaitForPredicateAsync(page, "() => document.querySelector('.tm-document-editor')?.getAttribute('data-fullscreen') === 'true'", 10_000);

        var frame = await ReadFrameAsync(page);
        Assert.AreEqual("bounded", frame.Frame, "fullscreen uses the bounded frame");
        Assert.AreEqual("true", frame.ScrollViewportMarker);
        Assert.IsTrue(Math.Abs(frame.StatusBarBottom - frame.InnerHeight) <= 1, $"the status bar is the bottom edge of the screen ({frame.StatusBarBottom} vs {frame.InnerHeight})");
        Assert.IsTrue(Math.Abs(frame.EditorHeight - frame.InnerHeight) <= 1, "the frame is the viewport tall");

        await page.GetByTestId("document-status-fullscreen").ClickAsync();
        await WaitForPredicateAsync(page, "() => !document.querySelector('.tm-document-editor')?.hasAttribute('data-fullscreen')", 10_000);
        var after = await ReadFrameAsync(page);
        Assert.IsNull(after.Frame, "leaving fullscreen leaves the frame (no Height was set)");
        AssertNoClientErrors(errors, "fullscreen");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("Without Height nothing changes: no frame markers, the page scrolls to reach the status bar, the canvas host keeps its own horizontal overflow")]
    public async Task Unbounded_KeepsTheFlowLayout()
    {
        var (page, errors) = await OpenAsync(1440, 900, null);

        var frame = await ReadFrameAsync(page);
        Assert.IsNull(frame.Frame);
        Assert.IsNull(frame.ScrollViewportMarker);
        Assert.AreEqual("auto", frame.CanvasHostOverflowX, "the legacy canvas host scroller is untouched");
        Assert.IsTrue(frame.PageScrollHeight > frame.InnerHeight + 1, "the editor is as tall as its document, so the page scrolls");
        AssertNoClientErrors(errors, "unbounded");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("On a phone-fitted page (zoom below 1) a scroll position maps to the page the reader is on: the virtualizer reads CSS pixels against CSS-pixel pages")]
    public async Task Bounded_FittedPage_VirtualizesInCssPixels_AtTheTabletAndThePhone()
    {
        foreach (var (width, height, touch) in new[] { (390, 844, true), (820, 1180, true) })
        {
            var (page, errors) = await OpenAsync(width, height, "height=viewport", touch);
            var scale = await page.EvaluateAsync<double>("() => Number(document.querySelector('[data-testid=\"document-canvas-engine-root\"]').getAttribute('data-canvas-zoom-percent')) / 100");
            Assert.IsTrue(scale < 0.95, $"{width}: the spec needs a fitted page, zoom is {scale}");

            await ScrollToPageAsync(page, 6);
            await page.WaitForFunctionAsync(
                """
                () => {
                    const region = document.querySelector('[data-region="doc"][data-scroll-viewport="true"]').getBoundingClientRect();
                    const el = document.querySelector('[data-testid="document-canvas-page"][data-page-index="6"]');
                    if (!el) return false;
                    const r = el.getBoundingClientRect();
                    return r.bottom > region.top && r.top < region.bottom;
                }
                """,
                null,
                new PageWaitForFunctionOptions { Timeout = 15_000 });
            AssertNoClientErrors(errors, $"fitted {width}");
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("'Page X of Y' names the page the reader is on, not the overscan page above it")]
    public async Task Bounded_StatusBar_NamesThePageTheReaderIsOn()
    {
        foreach (var (width, height, touch) in new[] { (1440, 900, false), (820, 1180, true) })
        {
            var (page, errors) = await OpenAsync(width, height, "height=viewport", touch);
            Assert.AreEqual("1", await page.GetByTestId("document-status-page-count").GetAttributeAsync("data-current-page"), $"{width}: page 1 at load");

            await ScrollToPageAsync(page, 9);
            await page.WaitForFunctionAsync(
                "() => document.querySelector('[data-testid=\"document-status-page-count\"]').getAttribute('data-current-page') === '10'",
                null,
                new PageWaitForFunctionOptions { Timeout = 15_000 });
            AssertNoClientErrors(errors, $"page number {width}");
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("Nothing but the regions scrolls: the workspace never overflows, with the navigation column long (desktop) and the inspector sheet open (tablet)")]
    public async Task Bounded_OnlyTheRegionsScroll_TheWorkspaceNeverOverflows()
    {
        var (desktop, errors) = await OpenAsync(1700, 800, "height=viewport&pageFit=off");
        await desktop.GetByTestId("document-navigation-expand").ClickAsync();
        await Assertions.Expect(desktop.GetByTestId("document-outline-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        var probe = await desktop.EvaluateAsync<JsonElement>(
            """
            () => {
                const ws = document.querySelector('[data-testid="document-editor-workspace"]');
                const nav = document.querySelector('[data-region="nav"]').getBoundingClientRect();
                const status = document.querySelector('[data-testid="document-status-bar"]').getBoundingClientRect();
                return { overflow: ws.scrollHeight - ws.clientHeight, navBottom: nav.bottom, statusTop: status.top, pageScroll: document.scrollingElement.scrollHeight - window.innerHeight };
            }
            """);
        Assert.IsTrue(probe.GetProperty("overflow").GetDouble() <= 1, $"the workspace does not scroll (overflow {probe.GetProperty("overflow").GetDouble()})");
        Assert.IsTrue(probe.GetProperty("navBottom").GetDouble() <= probe.GetProperty("statusTop").GetDouble() + 1, "the navigation column ends above the status bar");
        Assert.IsTrue(probe.GetProperty("pageScroll").GetDouble() <= 1, "the page does not scroll");
        AssertNoClientErrors(errors, "desktop regions");
        await desktop.Context.CloseAsync();

        var (tablet, tabletErrors) = await OpenAsync(820, 1180, "height=viewport", touch: true);
        await tablet.GetByTestId("document-side-panel-edge-toggle").ClickAsync();
        await Assertions.Expect(tablet.GetByTestId("document-side-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await tablet.GetByTestId("document-side-panel-tab-outline").ClickAsync();
        var tabletProbe = await tablet.EvaluateAsync<JsonElement>(
            """
            () => {
                const ws = document.querySelector('[data-testid="document-editor-workspace"]');
                const panel = document.querySelector('[data-testid="document-side-panel"]').getBoundingClientRect();
                const status = document.querySelector('[data-testid="document-status-bar"]').getBoundingClientRect();
                return { overflow: ws.scrollHeight - ws.clientHeight, panelBottom: panel.bottom, statusTop: status.top };
            }
            """);
        Assert.IsTrue(tabletProbe.GetProperty("overflow").GetDouble() <= 1, "the tablet workspace does not scroll with the sheet open");
        Assert.IsTrue(tabletProbe.GetProperty("panelBottom").GetDouble() <= tabletProbe.GetProperty("statusTop").GetDouble() + 1, "the sheet ends above the status bar");
        AssertNoClientErrors(tabletErrors, "tablet sheet");
        await tablet.Context.CloseAsync();
    }

    [TestMethod]
    [Description("Entering and leaving fullscreen keeps the reader on the same page")]
    public async Task Fullscreen_KeepsTheReadingPosition()
    {
        var (page, errors) = await OpenAsync(1440, 900, "height=viewport");
        await ScrollToPageAsync(page, 9);
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-testid=\"document-status-page-count\"]').getAttribute('data-current-page') === '10'",
            null,
            new PageWaitForFunctionOptions { Timeout = 15_000 });

        await page.GetByTestId("document-status-fullscreen").ClickAsync();
        await WaitForPredicateAsync(page, "() => document.querySelector('.tm-document-editor')?.getAttribute('data-fullscreen') === 'true'", 10_000);
        await AssertPageNearTheTopAsync(page, 9, "in fullscreen");

        await page.GetByTestId("document-status-fullscreen").ClickAsync();
        await WaitForPredicateAsync(page, "() => !document.querySelector('.tm-document-editor')?.hasAttribute('data-fullscreen')", 10_000);
        await AssertPageNearTheTopAsync(page, 9, "back in the frame");
        AssertNoClientErrors(errors, "fullscreen reading position");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("The demo page /document-editor fits the viewport by default: no page scroll, the status bar on screen, the editor under the demo top bar (1440 and a phone)")]
    public async Task DemoPage_DocumentEditor_FitsTheViewport_WithoutADoubleScroll()
    {
        foreach (var (width, height, touch) in new[] { (1440, 900, false), (390, 844, true) })
        {
            var context = await CreateContextAsync(ContextOptions(width, height, touch));
            var page = await context.NewPageAsync();
            await GotoAppReadyAsync(page, BoundedDocumentEditorUrl(BaseUrl, "documentId=contract-demo"), WaitUntilState.DOMContentLoaded);
            await WaitForPredicateAsync(page, """
                () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                    && document.querySelector('[data-testid="document-status-bar"]')
                """, 60_000);
            await page.WaitForTimeoutAsync(800);

            var probe = await page.EvaluateAsync<JsonElement>(
                """
                () => {
                    const editor = document.querySelector('.tm-document-editor').getBoundingClientRect();
                    const bar = document.querySelector('[data-demo-shell="main"] .sticky')?.getBoundingClientRect();
                    const status = document.querySelector('[data-testid="document-status-bar"]').getBoundingClientRect();
                    const action = document.querySelector('[data-testid="document-mobile-action-bar"]')?.getBoundingClientRect();
                    return {
                        frame: document.querySelector('.tm-document-editor').getAttribute('data-frame'),
                        pageScroll: document.scrollingElement.scrollHeight - window.innerHeight,
                        editorTop: editor.top, editorBottom: editor.bottom, barBottom: bar ? bar.bottom : 0,
                        lastEdge: Math.max(status.bottom, action ? action.bottom : 0), inner: window.innerHeight
                    };
                }
                """);
            Assert.AreEqual("bounded", probe.GetProperty("frame").GetString(), $"{width}: the demo bounds the editor by default");
            Assert.IsTrue(probe.GetProperty("pageScroll").GetDouble() <= 1, $"{width}: the demo page does not scroll ({probe.GetProperty("pageScroll").GetDouble()} px too tall)");
            Assert.IsTrue(probe.GetProperty("lastEdge").GetDouble() <= probe.GetProperty("inner").GetDouble() + 0.5, $"{width}: the status bar (action bar on a phone) is on screen");
            Assert.IsTrue(probe.GetProperty("editorTop").GetDouble() >= probe.GetProperty("barBottom").GetDouble() - 1, $"{width}: the demo top bar does not overlap the editor");
            await context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("Phase 13: a bounded PHONE frame gives up the room an on-screen keyboard covers - the action bar ends above the (emulated) keyboard and the knob is cleared again when it closes")]
    public async Task Bounded_Phone_ActionBarStaysAboveTheKeyboard()
    {
        var (page, errors) = await OpenAsync(390, 844, "height=viewport", touch: true);
        Assert.AreEqual("mobile", await page.Locator(".tm-document-editor").First.GetAttributeAsync("data-layout"));

        await page.EvaluateAsync("""
            height => {
                const viewport = window.visualViewport;
                Object.defineProperty(viewport, 'height', { configurable: true, get: () => height });
                viewport.dispatchEvent(new Event('resize'));
            }
            """, 544);
        await WaitForPredicateAsync(page, "() => !!document.querySelector('.tm-document-editor').style.getPropertyValue('--tm-visual-viewport-inset-block-end')", 10_000);
        // The inset is measured relative to the frame, so the bar ends exactly at the visual viewport's bottom edge.
        await page.WaitForFunctionAsync("() => Math.abs(document.querySelector('[data-testid=\"document-mobile-action-bar\"]').getBoundingClientRect().bottom - window.visualViewport.height) <= 1", null, new() { Timeout = 10_000 });
        var frame = await page.Locator(".tm-document-editor").First.BoundingBoxAsync();
        Assert.IsTrue(frame is not null && frame.Height > 300, "the frame keeps its height; only its content gives the room up");
        Assert.IsTrue(await page.GetByTestId("document-status-bar").CountAsync() == 0 || await page.GetByTestId("document-status-bar").EvaluateAsync<double>("e => e.getBoundingClientRect().bottom") <= 545, "a status bar, when present, is above the keyboard too");

        await page.EvaluateAsync("""
            height => {
                Object.defineProperty(window.visualViewport, 'height', { configurable: true, get: () => height });
                window.visualViewport.dispatchEvent(new Event('resize'));
            }
            """, 844);
        await WaitForPredicateAsync(page, "() => document.querySelector('.tm-document-editor').style.getPropertyValue('--tm-visual-viewport-inset-block-end') === ''", 10_000);
        var probe = await page.EvaluateAsync<double[]>("() => [document.querySelector('[data-testid=\"document-mobile-action-bar\"]').getBoundingClientRect().bottom, window.innerHeight]");
        Assert.IsTrue(probe[0] <= probe[1] + 1 && probe[0] > 700, $"the bar is back at the bottom ({probe[0]:0} of {probe[1]:0})");
        AssertNoClientErrors(errors, "keyboard");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("Phase 13: the floating mini toolbar of a range selection is clipped by the frame's scroller - hidden once the selection scrolls out of it, back when it returns")]
    public async Task Bounded_MiniToolbar_FollowsTheScrollerClip()
    {
        var (page, errors) = await OpenAsync(1440, 900, "height=viewport&pageFit=off");
        var point = await page.EvaluateAsync<int[]>("""
            () => {
                const region = document.querySelector('[data-region="doc"][data-scroll-viewport="true"]').getBoundingClientRect();
                const node = Array.from(document.querySelectorAll('[data-canvas-text-rect]'))
                    .find(n => { const r = n.getBoundingClientRect(); return r.width > 60 && r.top > region.top + 40 && r.bottom < region.bottom - 40; });
                const r = node.getBoundingClientRect();
                return [Math.round(r.left + 12), Math.round(r.top + r.height / 2)];
            }
            """);
        await page.Mouse.DblClickAsync(point[0], point[1]);
        var toolbar = page.GetByTestId("document-mini-toolbar");
        await Assertions.Expect(toolbar).ToBeVisibleAsync(new() { Timeout = 10_000 });
        var box = await toolbar.BoundingBoxAsync();
        var region = await page.Locator("[data-region='doc'][data-scroll-viewport='true']").BoundingBoxAsync();
        Assert.IsTrue(box is not null && region is not null && box.Y >= region.Y - 1 && box.Y + box.Height <= region.Y + region.Height + 1, "the toolbar sits inside the doc region's scroller");

        // The page surface (as tall as the document) shows no focus ring in a bounded frame - clipped, it was one stray blue line at the column's right edge.
        Assert.AreEqual("none", await page.EvaluateAsync<string>("() => getComputedStyle(document.querySelector('.tm-document-editor__page-surface')).outlineStyle"));

        await ScrollToPageAsync(page, 9);
        await Assertions.Expect(toolbar).ToHaveCountAsync(0, new() { Timeout = 10_000 });

        // Page 1 is unmounted while the reader is on page 10 (the virtualizer), so the way back is the scroller's own scrollTop.
        await page.EvaluateAsync("() => { document.querySelector('[data-region=\"doc\"][data-scroll-viewport=\"true\"]').scrollTop = 0; }");
        await Assertions.Expect(toolbar).ToBeVisibleAsync(new() { Timeout = 10_000 });
        AssertNoClientErrors(errors, "mini toolbar clip");
        await page.Context.CloseAsync();
    }

    private static void AssertNoClientErrors(List<string> errors, string context)
        => Assert.AreEqual(0, errors.Count, $"{context}: no console or page error expected, got: {string.Join(" | ", errors.Take(5))}");

    private static async Task AssertPageNearTheTopAsync(IPage page, int pageIndex, string when)
    {
        await page.WaitForFunctionAsync(
            """
            index => {
                const region = document.querySelector('[data-region="doc"][data-scroll-viewport="true"]').getBoundingClientRect();
                const el = document.querySelector(`[data-testid="document-canvas-page"][data-page-index="${index}"]`);
                if (!el) return false;
                const r = el.getBoundingClientRect();
                // the page is the one at the top of the view: it starts within the first quarter of the region (or the one before it ends there)
                return r.top < region.top + region.height * 0.5 && r.bottom > region.top + 40;
            }
            """,
            pageIndex,
            new PageWaitForFunctionOptions { Timeout = 15_000 });
        Assert.AreEqual((pageIndex + 1).ToString(), await page.GetByTestId("document-status-page-count").GetAttributeAsync("data-current-page"), $"the reader stays on page {pageIndex + 1} {when}");
    }

    private static Task ScrollToPageAsync(IPage page, int pageIndex)
        => page.EvaluateAsync(
            """
            index => {
                const region = document.querySelector('[data-region="doc"][data-scroll-viewport="true"]');
                const first = document.querySelector('[data-testid="document-canvas-page"][data-page-index="0"]');
                const stride = first.getBoundingClientRect().height + 24;
                region.scrollTop = index * stride;
            }
            """,
            pageIndex);

    private async Task<(IPage Page, List<string> Errors)> OpenAsync(int width, int height, string? query, bool touch = false)
    {
        var context = await CreateContextAsync(ContextOptions(width, height, touch));
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, message) => errors.Add($"pageerror: {message}");
        page.Console += (_, message) =>
        {
            if (message.Type == "error" && !message.Text.Contains("favicon", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"console: {message.Text}");
            }
        };

        var url = $"{BaseUrl}/canvas-engine-host?documentId={LongDocumentId}&showToolbar=true&preferLocalDraft=false{(query is null ? string.Empty : "&" + query)}";
        await GotoAppReadyAsync(page, url, WaitUntilState.DOMContentLoaded);
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelector('[data-testid="document-canvas-engine-root"]')?.getAttribute('data-canvas-layout-complete') === 'true'
                && document.querySelector('[data-testid="document-ribbon-tab-view"]')
            """, 60_000);
        await page.WaitForTimeoutAsync(500);
        return (page, errors);
    }

    private static async Task<FrameProbe> ReadFrameAsync(IPage page)
        => await page.EvaluateAsync<FrameProbe>(
            """
            () => {
                const editor = document.querySelector('.tm-document-editor');
                const region = document.querySelector('[data-region="doc"]');
                const status = document.querySelector('[data-testid="document-status-bar"]').getBoundingClientRect();
                const ribbon = document.querySelector('.tm-document-editor__ribbon')?.getBoundingClientRect();
                const workspace = document.querySelector('[data-testid="document-editor-workspace"]').getBoundingClientRect();
                const first = document.querySelector('[data-testid="document-canvas-page"][data-page-index="0"]');
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                return {
                    frame: editor.getAttribute('data-frame'),
                    scrollViewportMarker: region?.getAttribute('data-scroll-viewport'),
                    pageCount: Number(root?.getAttribute('data-canvas-page-count') || '0'),
                    innerHeight: window.innerHeight,
                    statusBarTop: status.top, statusBarBottom: status.bottom,
                    workspaceTop: workspace.top,
                    ribbonTop: ribbon ? ribbon.top : 0, ribbonBottom: ribbon ? ribbon.bottom : 0,
                    pageScrollHeight: document.scrollingElement.scrollHeight,
                    pageScrollTop: document.scrollingElement.scrollTop,
                    scrollerTop: region ? region.scrollTop : 0,
                    canvasHostOverflowX: getComputedStyle(document.querySelector('[data-testid="document-canvas-engine-host"]')).overflowX,
                    editorHeight: editor.getBoundingClientRect().height,
                    pageStride: first ? first.getBoundingClientRect().height + 24 : 0
                };
            }
            """);

    private static async Task<MountedPages> ReadMountedPagesAsync(IPage page)
        => await page.EvaluateAsync<MountedPages>(
            """
            () => {
                const pages = Array.from(document.querySelectorAll('[data-testid="document-canvas-page"][data-page-index]'));
                return {
                    pageCount: Number(document.querySelector('[data-testid="document-canvas-engine-root"]')?.getAttribute('data-canvas-page-count') || '0'),
                    indexes: pages.map(p => Number(p.getAttribute('data-page-index'))),
                    paintedIndexes: pages.filter(p => Number(p.getAttribute('data-canvas-painted-command-count') || '0') > 0).map(p => Number(p.getAttribute('data-page-index')))
                };
            }
            """);

    private sealed class FrameProbe
    {
        [JsonPropertyName("frame")] public string? Frame { get; set; }
        [JsonPropertyName("scrollViewportMarker")] public string? ScrollViewportMarker { get; set; }
        [JsonPropertyName("pageCount")] public int PageCount { get; set; }
        [JsonPropertyName("innerHeight")] public double InnerHeight { get; set; }
        [JsonPropertyName("statusBarTop")] public double StatusBarTop { get; set; }
        [JsonPropertyName("statusBarBottom")] public double StatusBarBottom { get; set; }
        [JsonPropertyName("workspaceTop")] public double WorkspaceTop { get; set; }
        [JsonPropertyName("ribbonTop")] public double RibbonTop { get; set; }
        [JsonPropertyName("ribbonBottom")] public double RibbonBottom { get; set; }
        [JsonPropertyName("pageScrollHeight")] public double PageScrollHeight { get; set; }
        [JsonPropertyName("pageScrollTop")] public double PageScrollTop { get; set; }
        [JsonPropertyName("scrollerTop")] public double ScrollerTop { get; set; }
        [JsonPropertyName("canvasHostOverflowX")] public string CanvasHostOverflowX { get; set; } = string.Empty;
        [JsonPropertyName("editorHeight")] public double EditorHeight { get; set; }
        [JsonPropertyName("pageStride")] public double PageStride { get; set; }
    }

    private sealed class MountedPages
    {
        [JsonPropertyName("pageCount")] public int PageCount { get; set; }
        [JsonPropertyName("indexes")] public int[] Indexes { get; set; } = [];
        [JsonPropertyName("paintedIndexes")] public int[] PaintedIndexes { get; set; } = [];
    }
}
