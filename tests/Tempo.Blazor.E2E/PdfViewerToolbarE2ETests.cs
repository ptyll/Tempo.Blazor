using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// PdfViewer redesign phase 3 (desktop 1440): the grouped toolbar with the editable page input,
/// the zoom presets with the fit modes, the optional document header and the collapsible
/// thumbnails. Screenshots land in <c>__screenshots__/pdf-viewer/</c> (toolbar-desktop,
/// header-desktop, thumbnails-collapsed) for UX review against
/// <c>output/imagegen/tempo-reimagined/Tempo.Blazor.PdfViewer/pdf-viewer--desktop.png</c>.
/// </summary>
[TestClass]
public class PdfViewerToolbarE2ETests : WasmTestBase
{
    private const string PdfPage = "/pdf-viewer";
    private const int DesktopWidth = 1440;

    private async Task<IPage> OpenPdfPageAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(DesktopWidth, 1000);
        await page.GotoAsync($"{BaseUrl}{PdfPage}",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        return page;
    }

    /// <summary>Waits until the section's viewer has loaded its document (page input present).</summary>
    private static async Task<ILocator> WaitForLoadedViewerAsync(IPage page, string sectionTitle)
    {
        var section = page.Locator($"section:has(h2:has-text('{sectionTitle}'))");
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await section.ScrollIntoViewIfNeededAsync();

        var input = section.Locator(".tm-pdf-viewer__page-input").First;
        // The toolbar renders the page input only after the PDF document has loaded.
        await input.WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });
        return input;
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfViewer_Toolbar_PageInputNavigates()
    {
        var page = await OpenPdfPageAsync();
        var input = await WaitForLoadedViewerAsync(page, "Basic Usage");

        // Type an in-range page and commit with Enter: the input keeps the value and the
        // viewer navigates (the next/prev buttons and the count stay consistent).
        await input.FillAsync("3");
        await input.PressAsync("Enter");
        await page.WaitForFunctionAsync(
            "() => { const el = document.querySelector('section .tm-pdf-viewer__page-input'); return el && el.value === '3'; }",
            null, new PageWaitForFunctionOptions { Timeout = 15000 });

        // Out-of-range entry reverts to the current page instead of navigating.
        await input.FillAsync("999");
        await input.PressAsync("Enter");
        await page.WaitForFunctionAsync(
            "() => { const el = document.querySelector('section .tm-pdf-viewer__page-input'); return el && el.value === '3'; }",
            null, new PageWaitForFunctionOptions { Timeout = 15000 });

        // The toolbar exposes the grouped a11y structure.
        var toolbar = page.Locator("section:has(h2:has-text('Basic Usage')) .tm-pdf-viewer__toolbar").First;
        Assert.AreEqual("toolbar", await toolbar.GetAttributeAsync("role"));
        Assert.IsTrue(await toolbar.Locator("div[role='group'][aria-label='Navigation']").First.IsVisibleAsync(),
            "the navigation group must be labelled for assistive tech");

        await SaveToolbarScreenshotAsync(page, "toolbar-desktop");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfViewer_Toolbar_FitWidthFillsContainer()
    {
        var page = await OpenPdfPageAsync();
        var input = await WaitForLoadedViewerAsync(page, "Basic Usage");
        var section = page.Locator("section:has(h2:has-text('Basic Usage'))");

        var zoomTrigger = section.Locator(".tm-pdf-viewer__zoom .tm-dropdown-trigger").First;
        await zoomTrigger.ClickAsync();

        var fitWidthItem = section.Locator(".tm-pdf-viewer__zoom .tm-dropdown-item", new LocatorLocatorOptions { HasTextString = "Fit width" }).First;
        await fitWidthItem.ClickAsync(new LocatorClickOptions { Timeout = 15000 });

        // The trigger switches to the fit label and the canvas fills the wrap width.
        await page.WaitForFunctionAsync(
            "() => document.querySelector('section .tm-pdf-viewer__zoom .tm-dropdown-trigger')?.textContent?.includes('Fit width') === true",
            null, new PageWaitForFunctionOptions { Timeout = 15000 });

        await AssertCanvasFillsWidthAsync(page);

        // DoD: fit-width keeps working after a resize (ResizeObserver recompute).
        await page.SetViewportSizeAsync(DesktopWidth - 400, 1000);
        await page.WaitForTimeoutAsync(500);
        await AssertCanvasFillsWidthAsync(page);
    }

    private static async Task AssertCanvasFillsWidthAsync(IPage page)
    {
        // Polled: the ResizeObserver-driven recompute lands a frame after the resize.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var fills = await page.EvaluateAsync<bool>(@"
() => {
    const wrap = document.querySelector('section .tm-pdf-viewer__canvas-wrap');
    const canvas = wrap?.querySelector('canvas');
    if (!wrap || !canvas) return false;
    const style = getComputedStyle(wrap);
    const available = wrap.clientWidth
        - (parseFloat(style.paddingLeft) || 0) - (parseFloat(style.paddingRight) || 0);
    return canvas.getBoundingClientRect().width >= available * 0.95;
}");
            if (fills)
            {
                return;
            }

            await page.WaitForTimeoutAsync(200);
        }

        Assert.Fail("In fit-width mode the page canvas must fill the container width, also after a resize.");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfViewer_Header_ShowsTitleAndPrimaryDownload()
    {
        var page = await OpenPdfPageAsync();
        await WaitForLoadedViewerAsync(page, "Document Header");
        var section = page.Locator("section:has(h2:has-text('Document Header'))");

        var header = section.Locator(".tm-pdf-viewer__header").First;
        await header.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        var headerText = await header.TextContentAsync();
        Assert.IsTrue(headerText!.Contains("Quarterly Report 2026"), "the header renders the Title.");
        StringAssert.Contains(headerText, "Pages:", "the subtitle defaults to the neutral page count.");

        // Download moved into the header and paints the primary style; the toolbar has no links.
        var download = header.Locator("a[download]").First;
        await download.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        var downloadClass = await download.GetAttributeAsync("class");
        StringAssert.Contains(downloadClass, "tm-pdf-viewer__link--primary",
            "Download is the primary action of the viewer chrome.");
        Assert.AreEqual(0, await section.Locator(".tm-pdf-viewer__toolbar a[download]").CountAsync(),
            "with a header, the toolbar renders no file links.");

        await SaveToolbarScreenshotAsync(page, "header-desktop");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfViewer_Thumbnails_CollapseAndExpand()
    {
        var page = await OpenPdfPageAsync();
        await WaitForLoadedViewerAsync(page, "Thumbnails Sidebar");
        var section = page.Locator("section:has(h2:has-text('Thumbnails Sidebar'))");

        var collapse = section.Locator("[data-testid='pdf-thumbnails-collapse']").First;
        await collapse.ClickAsync(new LocatorClickOptions { Timeout = 15000 });

        Assert.AreEqual(0, await section.Locator(".tm-pdf-viewer__thumbnails").CountAsync(),
            "the collapsed sidebar is hidden.");
        var expand = section.Locator("[data-testid='pdf-thumbnails-expand']").First;
        await expand.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await SaveToolbarScreenshotAsync(page, "thumbnails-collapsed");

        await expand.ClickAsync(new LocatorClickOptions { Timeout = 15000 });
        await section.Locator(".tm-pdf-viewer__thumbnails")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });

        // Thumbnail labels keep the page number and the active one is highlighted.
        var labels = section.Locator(".tm-pdf-thumbnail__label");
        Assert.IsTrue(await labels.First.IsVisibleAsync(), "each thumbnail carries its page number.");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfViewer_Toolbar_CommentsToggleDoesNotOverlapSiblings()
    {
        // CSS regression guard: `.tm-pdf-viewer__btn { width: 28px; padding: 0 }` used to
        // win over the comments-toggle override, squeezing the icon + open-thread count
        // into a 28px box whose content spilled into the neighboring toolbar controls.
        var page = await OpenPdfPageAsync();
        await WaitForLoadedViewerAsync(page, "Annotations & Comments");
        var section = page.Locator("section:has(h2:has-text('Annotations & Comments'))");

        var toggle = section.Locator("[data-testid='pdf-annotations-toggle']").First;
        await toggle.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });

        var result = await page.EvaluateAsync<string>(@"
() => {
    const section = [...document.querySelectorAll('section')]
        .find(s => s.querySelector('h2')?.textContent.includes('Annotations & Comments'));
    const toolbar = section?.querySelector('.tm-pdf-viewer__toolbar');
    const toggle = toolbar?.querySelector('[data-testid=""pdf-annotations-toggle""]');
    if (!toolbar || !toggle) return 'missing';
    const others = [...toolbar.querySelectorAll('*')]
        .filter(el => el !== toggle && !toggle.contains(el) && !el.contains(toggle)
            && el.offsetParent !== null)
        .map(el => el.getBoundingClientRect());
    const overlaps = (el) => {
        const box = el.getBoundingClientRect();
        return others.some(r => box.left < r.right - 0.5 && r.left < box.right - 0.5
            && box.top < r.bottom - 0.5 && r.top < box.bottom - 0.5);
    };
    if (overlaps(toggle)) return 'button-overlaps';
    // With the old fixed 28px box the count spilled outside the button.
    const count = toggle.querySelector('.tm-pdf-viewer__comments-count');
    if (count && overlaps(count)) return 'count-overlaps';
    return 'ok';
    }");

        Assert.AreEqual("ok", result,
            "the comments toggle (icon + count) must not overlap its toolbar siblings.");
    }

    private async Task SaveScreenshotAsync(IPage page, string name)
    {
        var directory = Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "__screenshots__", "pdf-viewer");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name + ".png");
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = path,
            Type = ScreenshotType.Png,
            FullPage = false,
            Timeout = 60_000,
        });
        TestContext.AddResultFile(path);
    }

    /// <summary>Parks pointer/focus and settles fonts before the UX-review capture.</summary>
    private async Task SaveToolbarScreenshotAsync(IPage page, string name)
    {
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync("() => document.activeElement && document.activeElement.blur()");
        await page.WaitForTimeoutAsync(400);
        await SaveScreenshotAsync(page, name);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent!;
        }

        throw new InvalidOperationException("Could not locate TempoBlazor.slnx.");
    }
}
