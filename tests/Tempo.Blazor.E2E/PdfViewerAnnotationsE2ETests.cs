using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E for TmPdfViewer annotations and improved search on the PDF Viewer demo page
/// (WASM demo at 7106, API at 5100). Verifies the seeded annotation thread renders in the
/// side panel, selecting it opens the comment detail, and full-text search shows a live
/// counter with highlight and next-match navigation. Screenshots land in
/// <c>__screenshots__/pdf-viewer/</c> for UX review.
/// </summary>
[TestClass]
public class PdfViewerAnnotationsE2ETests : WasmTestBase
{
    private const string PdfPage = "/pdf-viewer";

    private async Task<IPage> OpenPdfPageAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await page.GotoAsync($"{BaseUrl}{PdfPage}",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        return page;
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfViewer_Annotations_SeededThreadRendersAndOpensDetail()
    {
        var page = await OpenPdfPageAsync();

        var section = page.Locator("section:has(h2:has-text('Annotations'))");
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await section.ScrollIntoViewIfNeededAsync();

        var panel = section.Locator("[data-testid='pdf-annotation-panel']").First;
        await panel.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });

        var thread = section.Locator("[data-testid='pdf-annotation-thread']").First;
        await thread.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await SaveScreenshotAsync(page, "annotations-panel");

        await thread.ClickAsync();

        var detail = section.Locator("[data-testid='pdf-annotation-detail']").First;
        await detail.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        Assert.IsTrue(await detail.IsVisibleAsync(), "Selecting the seeded thread should open its comment detail.");

        var comment = section.Locator("[data-testid='pdf-annotation-comment']").First;
        await comment.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await SaveScreenshotAsync(page, "annotations-detail");
    }

    // ── Continuous mode (wave 0) ─────────────────────────────────────────────

    /// <summary>
    /// Reproduces the wave-0 bug: starting directly in <c>ViewMode=Continuous</c> rendered an
    /// empty area because the JS document state was keyed by the single-page canvas, which is
    /// never rendered in continuous mode. After the fix the section starts with at least one
    /// rendered page canvas.
    /// </summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfViewer_ContinuousMode_StartsWithRenderedPages()
    {
        var page = await OpenPdfPageAsync();

        var section = page.Locator("section:has(h2:has-text('Continuous Scroll'))");
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await section.ScrollIntoViewIfNeededAsync();

        var wrap = section.Locator(".tm-pdf-viewer__continuous-wrap").First;
        await wrap.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });

        // At least one page canvas must exist once the document has loaded.
        var firstCanvas = wrap.Locator("canvas").First;
        await firstCanvas.WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });
        var canvasCount = await wrap.Locator("canvas").CountAsync();
        Assert.IsTrue(canvasCount > 0,
            $"Continuous mode should start with rendered pages, found {canvasCount} canvas(es).");
        Assert.IsTrue(await firstCanvas.IsVisibleAsync(), "The first continuous page canvas should be visible.");
        await SaveScreenshotAsync(page, "continuous-start");

        // Sharpness probe: at deviceScaleFactor=2 the backing store must be larger than the CSS size.
        var dprContext = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1440, Height = 1000 },
            DeviceScaleFactor = 2,
            Locale = "en-US",
            IgnoreHTTPSErrors = true
        });
        try
        {
            var dprPage = await dprContext.NewPageAsync();
            await dprPage.SetViewportSizeAsync(1440, 1000);
            await dprPage.GotoAsync($"{BaseUrl}{PdfPage}",
                new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
            await WaitForAppReadyAsync(dprPage);
            var dprSection = dprPage.Locator("section:has(h2:has-text('Continuous Scroll'))");
            await dprSection.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
            var dprWrap = dprSection.Locator(".tm-pdf-viewer__continuous-wrap").First;
            await dprWrap.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
            var dprCanvas = dprWrap.Locator("canvas").First;
            await dprCanvas.WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });
            var sharpness = await dprCanvas.EvaluateAsync<string>(
                "el => JSON.stringify({ backing: el.width, css: Math.round(el.getBoundingClientRect().width) })");
            var probe = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, int>>(sharpness);
            Assert.IsNotNull(probe);
            Assert.IsTrue(probe["backing"] >= probe["css"] * 2 - 2,
                $"At deviceScaleFactor=2 the backing store ({probe["backing"]}px) should be ~2x the CSS size ({probe["css"]}px).");
            await SaveScreenshotAsync(dprPage, "continuous-start-dpr2");
        }
        finally
        {
            await dprContext.DisposeAsync();
        }
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfViewer_Search_HighlightsMatchesAndNavigates()
    {
        var page = await OpenPdfPageAsync();

        var section = page.Locator("section:has(h2:has-text('Document Search'))");
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await section.ScrollIntoViewIfNeededAsync();

        var input = section.Locator("[data-testid='pdf-search-input']").First;
        await input.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await input.FillAsync("the");
        await input.PressAsync("Enter");

        // The counter only appears once matches are found, which requires the PDF text content.
        var count = section.Locator("[data-testid='pdf-search-count']").First;
        await count.WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });
        var firstCount = (await count.InnerTextAsync()).Trim();
        Assert.IsFalse(string.IsNullOrWhiteSpace(firstCount), "Search should report a match counter.");
        await SaveScreenshotAsync(page, "search-highlight");

        // Highlights are drawn into the search layer.
        var highlight = section.Locator(".tm-pdf-search-highlight").First;
        await highlight.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });

        await section.Locator("[data-testid='pdf-search-next']").First.ClickAsync();
        // Deterministic wait: the counter renders "current of total", so advancing
        // to the next match must change its text (searching "the" yields many matches).
        await Assertions.Expect(count).Not.ToHaveTextAsync(firstCount, new LocatorAssertionsToHaveTextOptions { Timeout = 15000 });
        await SaveScreenshotAsync(page, "search-next-match");
    }

    // ── Loading state (review fix 13) ────────────────────────────────────────

    /// <summary>
    /// Delays the first PDF request so the viewer's loading overlay stays visible,
    /// captures <c>__screenshots__/pdf-viewer/loading.png</c> while the
    /// <c>[role=status]</c> overlay is on screen, and then verifies the document
    /// still renders once the delay is released. Only the first request is held —
    /// PDF.js follows up with several range requests that must not each be delayed.
    /// </summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfViewer_LoadingState_CapturedWhileStatusVisible()
    {
        var context = await CreateContextAsync();
        var pdfRequestsHeld = 0;
        await context.RouteAsync("**/*.pdf", async route =>
        {
            if (Interlocked.Exchange(ref pdfRequestsHeld, 1) == 0)
            {
                await Task.Delay(3000);
            }
            await route.ContinueAsync();
        });

        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await page.GotoAsync($"{BaseUrl}{PdfPage}",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);

        var status = page.Locator(".tm-pdf-viewer__body [role='status']").First;
        await status.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        Assert.IsTrue(await status.IsVisibleAsync(),
            "The loading overlay must be visible while the PDF download is delayed.");

        var body = page.Locator(".tm-pdf-viewer__body").First;
        Assert.AreEqual("true", await body.GetAttributeAsync("aria-busy"),
            "The viewer body should expose aria-busy while loading.");

        await SaveScreenshotAsync(page, "loading");

        // The route delay only holds the request, so the viewer must recover.
        await status.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = 60000
        });
        Assert.AreEqual("false", await body.GetAttributeAsync("aria-busy"),
            "aria-busy must clear once the document has loaded.");
    }

    private static async Task SaveScreenshotAsync(IPage page, string fileName)
    {
        var dir = Path.Combine(FindRepoRoot().FullName,
            "tests", "Tempo.Blazor.E2E", "__screenshots__", "pdf-viewer");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{fileName}.png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = true });
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
            {
                return directory;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
