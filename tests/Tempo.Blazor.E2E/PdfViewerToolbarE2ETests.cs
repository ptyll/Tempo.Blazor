using System.Text.RegularExpressions;
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

    private async Task<IPage> OpenPdfPageAsync(int width = DesktopWidth)
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, 1000);
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

    // ── FZE-11: the header's default subtitle carries the file size ──────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfViewer_Header_ShowsFileSize()
    {
        var page = await OpenPdfPageAsync();
        await WaitForLoadedViewerAsync(page, "Document Header");
        var section = page.Locator("section:has(h2:has-text('Document Header'))");

        var header = section.Locator(".tm-pdf-viewer__header").First;
        var subtitle = header.Locator(".tm-pdf-viewer__header-subtitle").First;
        await subtitle.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });

        // The demo fixture is ~100 KB, so the JS getDownloadInfo report formats as KB
        // ("Pages: 120 · 100.0 KB" in the en-US lane culture).
        var text = (await subtitle.TextContentAsync())!.Trim();
        StringAssert.Matches(text,
            new Regex(@"^Pages: \d+ · \d+(\.\d+)? (B|KB|MB|GB)$"),
            $"the default subtitle must append the localized file size, got '{text}'.");

        await SaveToolbarScreenshotAsync(page, "header-file-size");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfViewer_Header_FileSizeUnknown_Edge()
    {
        var context = await CreateContextAsync();
        // Simulate the unknown-length edge end-to-end: block the filesize lookup module,
        // so pdf-viewer.js reports NOTHING (the same path a rejected getDownloadInfo
        // takes) and the subtitle keeps its pages-only form.
        await context.RouteAsync("**/pdf-viewer-filesize.mjs", route =>
            route.FulfillAsync(new RouteFulfillOptions { Status = 404, ContentType = "text/plain", Body = "gone" }));
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(DesktopWidth, 1000);
        await page.GotoAsync($"{BaseUrl}{PdfPage}",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);

        await WaitForLoadedViewerAsync(page, "Document Header");
        var section = page.Locator("section:has(h2:has-text('Document Header'))");
        var subtitle = section.Locator(".tm-pdf-viewer__header-subtitle").First;
        await subtitle.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });

        var text = (await subtitle.TextContentAsync())!.Trim();
        StringAssert.Matches(text, new Regex(@"^Pages: \d+$"),
            $"without a known length the subtitle must keep its pages-only form, got '{text}'.");
    }

    // ── FZE-11: the floating search card (desktop default) ───────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfViewer_SearchCard_FloatsOverPage_AndEscapeRestoresFocus()
    {
        var page = await OpenPdfPageAsync();
        var section = page.Locator("section:has(h2:has-text('Document Search'))");
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await section.ScrollIntoViewIfNeededAsync();

        var input = section.Locator("[data-testid='pdf-search-input']").First;
        await input.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await input.FillAsync("the");
        await input.PressAsync("Enter");

        var card = section.Locator("[data-testid='pdf-search-card']").First;
        await card.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        Assert.AreEqual("search", await card.GetAttributeAsync("role"),
            "the floating card is the search landmark.");

        var count = section.Locator("[data-testid='pdf-search-count']").First;
        await count.WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });

        // The card floats over the page body: it lives inside .tm-pdf-viewer__body and
        // never paints above the toolbar.
        var verdict = await page.EvaluateAsync<string>(@"
() => {
    const section = [...document.querySelectorAll('section')]
        .find(s => s.querySelector('h2')?.textContent.includes('Document Search'));
    const card = section?.querySelector('[data-testid=""pdf-search-card""]');
    const toolbar = section?.querySelector('.tm-pdf-viewer__toolbar');
    const body = section?.querySelector('.tm-pdf-viewer__body');
    if (!card || !toolbar || !body) return 'missing';
    if (!body.contains(card)) return 'outside-body';
    const cardBox = card.getBoundingClientRect();
    const toolbarBox = toolbar.getBoundingClientRect();
    if (cardBox.top < toolbarBox.bottom - 1) return 'covers-toolbar';
    return 'ok';
    }");
        Assert.AreEqual("ok", verdict, "the search card must float over the body, below the toolbar.");

        await SaveToolbarScreenshotAsync(page, "search-card-desktop");

        // Escape closes the card and restores the focus to the toolbar's search toggle.
        await input.FocusAsync();
        await input.PressAsync("Escape");
        // Scoped to the section: every ShowSearch viewer on the page renders its own card.
        await Assertions.Expect(section.Locator("[data-testid='pdf-search-card']"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });
        var focused = await page.EvaluateAsync<string>(
            "() => document.activeElement && document.activeElement.hasAttribute('data-tm-search-toggle') ? 'toggle' : 'other'");
        Assert.AreEqual("toggle", focused,
            "Escape must return the focus to the toolbar's search toggle.");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfViewer_SearchCard_NoMatches_Edge()
    {
        var page = await OpenPdfPageAsync();
        var section = page.Locator("section:has(h2:has-text('Document Search'))");
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await section.ScrollIntoViewIfNeededAsync();

        var input = section.Locator("[data-testid='pdf-search-input']").First;
        await input.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await input.FillAsync("zzzqqqxxx");
        await input.PressAsync("Enter");

        // No matches: the localized verdict renders in the count slot, the input flags
        // aria-invalid, both live in the always-present role=status region and the card
        // stays open for a new query.
        var count = section.Locator("[data-testid='pdf-search-count']").First;
        await Assertions.Expect(count).ToHaveTextAsync("No matches",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30000 });
        Assert.AreEqual("true", await input.GetAttributeAsync("aria-invalid"),
            "a zero-match search flags the input aria-invalid.");

        var status = section.Locator("[data-testid='pdf-search-card'] [role='status']").First;
        Assert.AreEqual("polite", await status.GetAttributeAsync("aria-live"),
            "the live region wrapper is always present.");

        var card = section.Locator("[data-testid='pdf-search-card']").First;
        Assert.IsTrue(await card.IsVisibleAsync(), "the card stays open when nothing matches.");

        await SaveToolbarScreenshotAsync(page, "search-card-no-matches");
    }

    // ── Review: Escape from a card whose toggle lives in "More" ───────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfViewer_SearchCard_ToggleInMore_EscapeFocusesMoreTrigger()
    {
        // Desktop 1280: the labelled toolbar overflows, the search toggle renders
        // collapsed (inert) and is offered in the "More" menu. Escape in the card must
        // focus the More TRIGGER (focusing an inert toggle would be a no-op).
        var page = await OpenPdfPageAsync(1280);
        var section = page.Locator("section:has(h2:has-text('Document Search'))");
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await section.ScrollIntoViewIfNeededAsync();

        var toggle = section.Locator("[data-testid='pdf-search-toggle']").First;
        // Attached, not visible: at this width the toggle renders collapsed (inert).
        await toggle.WaitForAsync(new LocatorWaitForOptions
        {
            State = Microsoft.Playwright.WaitForSelectorState.Attached,
            Timeout = 30000,
        });
        Assert.AreEqual("true", await toggle.GetAttributeAsync("aria-hidden"),
            "precondition: the search toggle overflows into More at this width.");

        // ShowSearch starts the card open — close it, then reopen through the More menu
        // (the only surface the collapsed toggle lives on at this width).
        await section.Locator("[data-testid='pdf-search-close']").First
            .ClickAsync(new LocatorClickOptions { Timeout = 15000 });
        await Assertions.Expect(section.Locator("[data-testid='pdf-search-card']"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });

        await section.Locator(".tm-toolbar-more").First.ClickAsync(new LocatorClickOptions { Timeout = 15000 });
        await section.Locator(".tm-toolbar-more__item").Filter(new() { HasTextString = "Search" }).First
            .ClickAsync(new LocatorClickOptions { Timeout = 15000 });

        var input = section.Locator("[data-testid='pdf-search-input']").First;
        await input.WaitForAsync(new LocatorWaitForOptions
        {
            State = Microsoft.Playwright.WaitForSelectorState.Visible,
            Timeout = 15000,
        });

        await input.PressAsync("Escape");
        await Assertions.Expect(section.Locator("[data-testid='pdf-search-card']"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });

        var focused = await page.EvaluateAsync<string>(
            "() => document.activeElement ? (document.activeElement.className || '') : ''");
        Assert.IsTrue(focused.Contains("tm-toolbar-more"),
            $"Escape must focus the More trigger when the toggle overflows, got '{focused}'.");
    }

    // ── Review: the active match never parks under the floating card ─────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfViewer_SearchCard_ActiveMatchStaysClearOfCard()
    {
        var page = await OpenPdfPageAsync();
        var section = page.Locator("section:has(h2:has-text('Document Search'))");
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await section.ScrollIntoViewIfNeededAsync();

        var input = section.Locator("[data-testid='pdf-search-input']").First;
        await input.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await input.FillAsync("the");
        await input.PressAsync("Enter");

        await section.Locator("[data-testid='pdf-search-count']").First
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });

        var next = section.Locator("[data-testid='pdf-search-next']").First;
        for (var step = 0; step < 8; step++)
        {
            if (step > 0)
            {
                await next.ClickAsync(new LocatorClickOptions { Timeout = 15000 });
            }

            await page.WaitForFunctionAsync(
                """
                () => {
                    const section = [...document.querySelectorAll('section')]
                        .find(s => s.querySelector('h2')?.textContent.includes('Document Search'));
                    const card = section?.querySelector('[data-testid="pdf-search-card"]');
                    const active = section?.querySelector('.tm-pdf-search-highlight--active');
                    if (!card || !active) return false;
                    const a = active.getBoundingClientRect();
                    const c = card.getBoundingClientRect();
                    const gap = 8;
                    return !(a.top < c.bottom + gap && a.bottom > c.top);
                }
                """, null, new PageWaitForFunctionOptions { Timeout = 15000 });
        }
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
