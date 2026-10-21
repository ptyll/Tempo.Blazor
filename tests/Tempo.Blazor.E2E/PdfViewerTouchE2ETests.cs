using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// PdfViewer redesign phase 9 (FZE-9): touch interaction. On a touch tablet (1024×768) a text
/// selection raises the sticky comment CTA whose tap opens the comments sheet, the annotator
/// draws with one finger and never with two (the second finger is the pinch hand-over, whose
/// commit re-renders at a new scale through the core pointer-gesture module), and a touch
/// selection highlights through the tablet-only sticky "Highlight" CTA; on a phone (390×844)
/// the two-finger pinch zooms the viewer and the sticky CTA is the comment path. Touch drags
/// run through CDP <c>Input.dispatchTouchEvent</c> (Playwright's <see cref="ILocator.TapAsync"/>
/// covers the taps); screenshots land in <c>__screenshots__/pdf-viewer/mobile-selection-cta.png</c>
/// and <c>__screenshots__/pdf-annotator/tablet-drawing.png</c>.
/// </summary>
[TestClass]
public class PdfViewerTouchE2ETests : WasmTestBase
{
    private const string PdfPage = "/pdf-viewer";
    private const string AnnotatorPage = "/pdf-annotator";
    private const int TabletWidth = 1024;
    private const int TabletHeight = 768;
    private const int MobileWidth = 390;
    private const int MobileHeight = 844;

    private static string ShotDir(string sub)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", sub);
            if (Directory.Exists(Path.Combine(current.FullName, "tests", "Tempo.Blazor.E2E")))
            {
                Directory.CreateDirectory(candidate);
                return candidate;
            }

            current = current.Parent!;
        }

        throw new InvalidOperationException("Could not locate the E2E screenshot directory.");
    }

    private async Task<IPage> OpenTouchAsync(string route, int width, int height)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = true,
            IsMobile = true,
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}{route}",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        return page;
    }

    private static ILocator Section(IPage page, string title)
        => page.Locator($"section:has(h2:has-text('{title}'))");

    /// <summary>Two drag waypoints inside the viewport-visible band of a box taller than the
    /// viewport (the annotator's canvas extends below the fold at this viewport).</summary>
    private static (double From, double To) DragBand(LocatorBoundingBoxResult box)
    {
        var viewportBottom = box.Y + box.Height <= 800 ? box.Y + box.Height : 800;
        var top = Math.Max(box.Y, 100);
        var usable = viewportBottom - top - 40;
        return (top + usable * 0.3, top + usable * 0.6);
    }

    /// <summary>Waits until the section's viewer has loaded its document (page input present).</summary>
    private static async Task WaitForLoadedViewerAsync(ILocator section)
    {
        var input = section.Locator(".tm-pdf-viewer__page-input").First;
        await input.WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });
    }

    private static Task<ICDPSession> CdpAsync(IPage page) => page.Context.NewCDPSessionAsync(page);

    private static Task TouchAsync(ICDPSession session, string type, (double X, double Y)[] points)
        => session.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
        {
            ["type"] = type,
            ["touchPoints"] = type == "touchEnd"
                ? Array.Empty<object>()
                : points.Select((p, i) => (object)new Dictionary<string, object>
                {
                    ["x"] = p.X,
                    ["y"] = p.Y,
                    ["id"] = i + 1,
                }).ToArray(),
        });

    /// <summary>One-finger touch drag (the annotator's ink stroke path).</summary>
    private static async Task TouchDragAsync(ICDPSession session, double fromX, double fromY, double toX, double toY, int steps = 8)
    {
        await TouchAsync(session, "touchStart", new[] { (fromX, fromY) });
        for (var i = 1; i <= steps; i++)
        {
            var t = (double)i / steps;
            await TouchAsync(session, "touchMove", new[] { (fromX + (toX - fromX) * t, fromY + (toY - fromY) * t) });
            await Task.Delay(15);
        }

        await TouchAsync(session, "touchEnd", Array.Empty<(double, double)>());
    }

    /// <summary>Two-finger pinch (out) around a centre — the core pointer-gesture path.</summary>
    private static async Task PinchAsync(ICDPSession session, double centerX, double centerY, double fromRadius, double toRadius, int steps = 10)
    {
        await TouchAsync(session, "touchStart", new[] { (centerX - fromRadius, centerY), (centerX + fromRadius, centerY) });
        for (var i = 1; i <= steps; i++)
        {
            var radius = fromRadius + (toRadius - fromRadius) * i / (double)steps;
            await TouchAsync(session, "touchMove", new[] { (centerX - radius, centerY), (centerX + radius, centerY) });
            await Task.Delay(20);
        }

        await TouchAsync(session, "touchEnd", Array.Empty<(double, double)>());
    }

    /// <summary>Selects text across the section's text-layer divs (the touch selection's own
    /// report path is pinned by the node tests; this drives the same <c>selectionchange</c>
    /// → .NET wiring the finger would).</summary>
    private static async Task<bool> SelectTextAsync(ILocator section)
        => await section.EvaluateAsync<bool>(@"(section) => {
            const divs = [...section.querySelectorAll('.tm-pdf-viewer__text-layer div')];
            if (divs.length < 2) return false;
            const sel = window.getSelection();
            sel.removeAllRanges();
            const range = document.createRange();
            range.setStartBefore(divs[0]);
            range.setEndAfter(divs[Math.min(divs.length - 1, 4)]);
            sel.addRange(range);
            return (sel.toString() || '').length > 0;
        }");

    /// <summary>Selects a run of BODY-TEXT divs — real sentences, not the short heading-like
    /// items at the top of the layer — so the sticky CTA's reference shot shows the case it
    /// exists for (UX1, FZE-9 review).</summary>
    private static async Task<bool> SelectBodyTextAsync(ILocator section)
        => await section.EvaluateAsync<bool>(@"(section) => {
            const divs = [...section.querySelectorAll('.tm-pdf-viewer__text-layer div')]
                .filter(d => (d.textContent || '').trim().length >= 12);
            if (divs.length < 2) return false;
            const sel = window.getSelection();
            sel.removeAllRanges();
            const range = document.createRange();
            range.setStartBefore(divs[0]);
            range.setEndAfter(divs[Math.min(divs.length - 1, 6)]);
            sel.addRange(range);
            return (sel.toString() || '').length > 0;
        }");

    private async Task SaveScreenshotAsync(IPage page, string directory, string name)
    {
        var path = Path.Combine(directory, name + ".png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = false, Timeout = 60_000 });
        TestContext.AddResultFile(path);
    }

    // ── Tablet: touch selection → sticky comment CTA → the comments sheet ─────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfViewer_Touch_SelectTextAndComment()
    {
        var page = await OpenTouchAsync(PdfPage, TabletWidth, TabletHeight);

        var section = Section(page, "Annotations & Comments");
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await section.ScrollIntoViewIfNeededAsync();
        await WaitForLoadedViewerAsync(section);

        await SelectTextAsync(section);

        var cta = section.Locator("[data-testid='pdf-viewer-selection-cta']");
        await cta.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        Assert.IsTrue(await cta.IsVisibleAsync(), "a touch selection shows the sticky comment CTA on a tablet layout");

        // The CTA opens the comments sheet (the touch layouts resolve mobile at this demo
        // width — accept either drawer presentation).
        await section.Locator("[data-testid='pdf-viewer-selection-cta-comment']").TapAsync();
        var sheet = page.Locator(".tm-drawer--bottom, .tm-drawer--right").First;
        await sheet.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        var composer = sheet.Locator("[data-testid='pdf-annotation-new']");
        await composer.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });

        var threadsBefore = await sheet.Locator("[data-testid='pdf-annotation-thread']").CountAsync();
        await sheet.Locator("[data-testid='pdf-annotation-new-input']").FillAsync("commented from the touch CTA");
        await sheet.Locator("[data-testid='pdf-annotation-new-submit']").TapAsync();

        await sheet.Locator($"[data-testid='pdf-annotation-thread']").Nth(threadsBefore)
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 20000 });
        Assert.IsTrue(await sheet.Locator("[data-testid='pdf-annotation-thread']").CountAsync() > threadsBefore,
            "the comment created through the sticky CTA appears as a thread");
    }

    // ── Tablet: one finger draws, two fingers pinch (no stroke) ───────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfAnnotator_Tablet_DrawWithOneFinger_PanWithTwo()
    {
        var page = await OpenTouchAsync(AnnotatorPage, TabletWidth, TabletHeight);

        var section = Section(page, "Tablet Layout");
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await section.ScrollIntoViewIfNeededAsync();
        await WaitForLoadedViewerAsync(section);

        // Draw mode through a touch tap on the tool pill.
        await section.Locator("[data-testid='pdf-annotator-mode-draw']").TapAsync();

        var surface = section.Locator("[data-testid='pdf-annotator-surface']");
        await surface.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await surface.ScrollIntoViewIfNeededAsync();
        var box = await surface.BoundingBoxAsync() ?? throw new AssertFailedException("The annotator surface has no box.");
        var (from, to) = DragBand(box);

        // Two fingers FIRST (a clean state): the pinch hand-over — no stroke draft may
        // appear, and the pinch commits a new scale through the core pointer-gesture module.
        // The X range stays well inside the canvas — the shell's panel resizer overlays the
        // surface's right edge.
        var composer = section.Locator("[data-testid='pdf-annotator-new']");
        var session = await CdpAsync(page);
        var zoomBefore = await ZoomTriggerTextAsync(section);
        await PinchAsync(session, box.X + box.Width * 0.35, (from + to) / 2, fromRadius: 25, toRadius: 80);
        await Task.Delay(1200);

        Assert.AreEqual(0, await composer.CountAsync(),
            "a two-finger gesture is the pan/pinch hand-over: no stroke draft may appear");
        var zoomAfter = await ZoomTriggerTextAsync(section);
        Assert.AreNotEqual(zoomBefore, zoomAfter,
            $"the pinch commits a new scale (zoom trigger '{zoomBefore}' → '{zoomAfter}')");

        // One finger draws: the ink draft appears with its composer.
        await TouchDragAsync(session, box.X + box.Width * 0.2, from, box.X + box.Width * 0.45, to);
        await composer.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });

        // UX4: the composer opening in the docked panel re-flows the canvas — bring the
        // drawn stroke back into the viewport before the reference shot (at narrow
        // container widths the labelled tablet tools collapse to icons and free the room).
        await surface.ScrollIntoViewIfNeededAsync();
        await SaveScreenshotAsync(page, ShotDir("pdf-annotator"), "tablet-drawing");
    }

    // ── Tablet: touch selection highlights through the sticky CTA ─────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfAnnotator_Tablet_HighlightByTouchSelection()
    {
        var page = await OpenTouchAsync(AnnotatorPage, TabletWidth, TabletHeight);

        var section = Section(page, "Tablet Layout");
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await section.ScrollIntoViewIfNeededAsync();
        await WaitForLoadedViewerAsync(section);

        // Highlight mode, then close the docked panel so the CTA is the only path.
        await section.Locator("[data-testid='pdf-annotator-mode-highlight']").TapAsync();
        var close = section.Locator("[data-testid='pdf-annotator-close']");
        if (await close.CountAsync() > 0)
        {
            await close.TapAsync();
        }

        Assert.IsTrue(await SelectTextAsync(section), "the annotator's text layer is selectable");

        var cta = section.Locator("[data-testid='pdf-annotator-highlight-cta']");
        await cta.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });

        // UX2: the full label must stay visible on the tablet — the span wraps instead of
        // being truncated (scrollWidth never exceeds clientWidth).
        var labelOverflow = await cta.Locator("span")
            .EvaluateAsync<bool>("el => el.scrollWidth <= el.clientWidth + 1");
        Assert.IsTrue(labelOverflow,
            "the highlight CTA label wraps (scrollWidth <= clientWidth) — it is never truncated by an ellipsis");

        await cta.TapAsync();

        var composer = section.Locator("[data-testid='pdf-annotator-new']");
        await composer.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        var threadsBefore = await section.Locator("[data-testid='pdf-annotator-thread']").CountAsync();

        await composer.Locator("[data-testid='pdf-annotator-new-input']").FillAsync("highlighted by touch");
        await composer.Locator("[data-testid='pdf-annotator-new-submit']").TapAsync();

        await section.Locator("[data-testid='pdf-annotator-thread']").Nth(threadsBefore)
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 20000 });
        Assert.IsTrue(await section.Locator("[data-testid='pdf-annotator-thread']").CountAsync() > threadsBefore,
            "the highlight created through the sticky CTA appears as a thread");
    }

    // ── Mobile: the sticky CTA + the core pinch zoom ──────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfViewer_Mobile_PinchZoom()
    {
        var page = await OpenTouchAsync(PdfPage, MobileWidth, MobileHeight);

        var section = Section(page, "Annotations & Comments");
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await section.ScrollIntoViewIfNeededAsync();
        await WaitForLoadedViewerAsync(section);

        // The phone's comment path: a selection pins the sticky CTA (reference shot over a
        // body-text selection, UX1).
        Assert.IsTrue(await SelectBodyTextAsync(section), "the mobile text layer is selectable");
        var cta = section.Locator("[data-testid='pdf-viewer-selection-cta']");
        await cta.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });

        // UX2: the full label must be visible at 390px — the span wraps instead of being
        // truncated (scrollWidth never exceeds clientWidth).
        var labelOverflow = await section.Locator("[data-testid='pdf-viewer-selection-cta-comment'] span")
            .EvaluateAsync<bool>("el => el.scrollWidth <= el.clientWidth + 1");
        Assert.IsTrue(labelOverflow,
            "the CTA label wraps (scrollWidth <= clientWidth) — it is never truncated by an ellipsis");

        await SaveScreenshotAsync(page, ShotDir("pdf-viewer"), "mobile-selection-cta");

        await section.Locator("[data-testid='pdf-viewer-selection-cta-dismiss']").TapAsync();
        await cta.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 15000 });

        // The core pinch: two fingers diverge on the canvas wrap and the committed scale
        // re-renders the page (the zoom trigger follows).
        var wrap = section.Locator(".tm-pdf-viewer__canvas-wrap").First;
        var box = await wrap.BoundingBoxAsync() ?? throw new AssertFailedException("The canvas wrap has no box.");
        var zoomBefore = await ZoomTriggerTextAsync(section);
        var canvasBefore = await section.Locator(".tm-pdf-viewer__canvas").First.EvaluateAsync<int>("el => el.clientWidth");

        var session = await CdpAsync(page);
        await PinchAsync(session, box.X + box.Width / 2, box.Y + box.Height / 2, fromRadius: 30, toRadius: 110);
        await Task.Delay(1500);

        var zoomAfter = await ZoomTriggerTextAsync(section);
        Assert.AreNotEqual(zoomBefore, zoomAfter,
            $"the pinch commits a new scale (zoom trigger '{zoomBefore}' → '{zoomAfter}')");
        var canvasAfter = await section.Locator(".tm-pdf-viewer__canvas").First.EvaluateAsync<int>("el => el.clientWidth");
        Assert.IsTrue(canvasAfter > canvasBefore,
            $"the page re-renders at the committed scale (canvas {canvasBefore}px → {canvasAfter}px)");
    }

    /// <summary>The zoom dropdown trigger's text (the percentage / fit label).</summary>
    private static async Task<string> ZoomTriggerTextAsync(ILocator section)
    {
        var trigger = section.Locator(".tm-pdf-viewer__zoom .tm-dropdown-trigger").First;
        await trigger.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        return (await trigger.TextContentAsync())?.Trim() ?? string.Empty;
    }
}
