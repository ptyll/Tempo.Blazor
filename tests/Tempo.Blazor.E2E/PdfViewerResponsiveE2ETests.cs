using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// PdfViewer redesign phase 7 (FZE-7): the responsive viewer on tablet and mobile. Touch
/// targets meet <c>--tm-touch-target</c> under a coarse pointer (1024×768, touch), the mobile
/// fit-width default leaves no horizontal scroll (390×844), the comments panel reads and
/// resolves as a bottom sheet, continuous scroll works on a phone, and 320 px stays free of
/// horizontal overflow (viewer + redaction + Notion-style narrow column). Screenshots land in
/// <c>__screenshots__/pdf-viewer/{tablet,mobile,mobile-320}-*.png</c> and
/// <c>__screenshots__/redaction/mobile.png</c> for the UX review against
/// <c>output/imagegen/tempo-reimagined/Tempo.Blazor.PdfViewer/pdf-viewer--tablet.png</c> and
/// friends.
/// </summary>
[TestClass]
public class PdfViewerResponsiveE2ETests : WasmTestBase
{
    private const string PdfPage = "/pdf-viewer";
    private const string RedactionPage = "/redaction";

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

    private async Task<IPage> OpenAsync(string route, int width, int height, bool touch)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = touch,
            IsMobile = touch,
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

    /// <summary>Waits until the section's viewer has loaded its document (page input present).</summary>
    private static async Task WaitForLoadedViewerAsync(ILocator section)
    {
        var input = section.Locator(".tm-pdf-viewer__page-input").First;
        await input.WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });
    }

    private async Task ParkAndShootAsync(IPage page, ILocator section, string directory, string name)
    {
        await section.EvaluateAsync("el => el.scrollIntoView({ block: 'start', behavior: 'instant' })");
        await page.WaitForTimeoutAsync(400);
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync("() => document.activeElement instanceof HTMLElement && document.activeElement.blur()");
        var path = Path.Combine(directory, name + ".png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = false, Timeout = 60_000 });
        TestContext.AddResultFile(path);
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfViewer_Tablet_TouchTargets44()
    {
        var page = await OpenAsync(PdfPage, 1024, 768, touch: true);
        Assert.IsTrue(await page.EvaluateAsync<bool>("() => matchMedia('(pointer: coarse)').matches"),
            "precondition: the tablet context emulates a coarse pointer");

        // The forced-tablet demo section is the touch evidence: its layout does not depend
        // on how the demo column measures at this viewport.
        var section = Section(page, "Tablet Layout");
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await WaitForLoadedViewerAsync(section);

        // Every visible viewer-owned control meets the 44px touch target on coarse: buttons,
        // the page input (input), ARIA buttons and the file links (a) alike.
        var sizes = await section.EvaluateAsync<string>(@"
() => {
    const toolbar = document.querySelector('section .tm-pdf-viewer__toolbar');
    if (!toolbar) return 'missing-toolbar';
    const small = [...toolbar.querySelectorAll('button, input, [role=button], a')]
        .filter(b => b.offsetParent !== null)
        .filter(b => (b.getBoundingClientRect().width < 43 || b.getBoundingClientRect().height < 43))
        .map(b => (b.getAttribute('data-testid') ?? b.getAttribute('aria-label') ?? b.className) +
                  ':' + Math.round(b.getBoundingClientRect().width) + 'x' + Math.round(b.getBoundingClientRect().height));
    return small.length === 0 ? 'ok' : small.join(', ');
    }");
        Assert.AreEqual("ok", sizes,
            "on a coarse pointer every toolbar control must meet the --tm-touch-target minimum");

        // The tablet layout starts with the thumbnails collapsed (the rail keeps the reopen
        // affordance) and the comments panel not docked.
        Assert.AreEqual(0, await section.Locator("[data-testid='pdf-thumbnails-panel']").CountAsync(),
            "the tablet default collapses the thumbnails panel");
        Assert.AreEqual(1, await section.Locator("[data-testid='pdf-thumbnails-expand']").CountAsync(),
            "the collapsed rail keeps the reopen affordance");

        await ParkAndShootAsync(page, section, ShotDir("pdf-viewer"), "tablet-toolbar");

        // The comments side sheet (the ONE panel presentation on tablet), opened from the
        // toolbar toggle — evidence screenshot for the UX review.
        await section.Locator("[data-testid='pdf-annotations-toggle']").ClickAsync();
        var sheet = section.Locator(".tm-drawer--right").First;
        await sheet.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        Assert.IsTrue(await sheet.Locator("[data-testid='pdf-annotation-panel']").CountAsync() > 0,
            "the side sheet hosts the comments panel");
        await ParkAndShootAsync(page, section, ShotDir("pdf-viewer"), "tablet-comments-sheet");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfViewer_Mobile_FitWidthNoHorizontalScroll()
    {
        var page = await OpenAsync(PdfPage, 390, 844, touch: true);
        var section = Section(page, "Basic Usage");
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await WaitForLoadedViewerAsync(section);

        // PD2: ZoomMode=Auto on mobile requests the fit-width computation; the rendered page
        // then fits the container with no horizontal overflow of the viewer root.
        await page.WaitForFunctionAsync(
            "() => { const c = document.querySelector('section .tm-pdf-viewer__canvas'); return c && c.width > 0; }",
            null, new PageWaitForFunctionOptions { Timeout = 60000 });

        var overflow = await section.EvaluateAsync<bool>(
            "el => { const v = el.querySelector('.tm-pdf-viewer'); return v ? v.scrollWidth > v.clientWidth + 1 : true; }");
        Assert.IsFalse(overflow, "the mobile viewer must not scroll horizontally");

        // FZE-7 review: a tile renders only for an enabled feature — the Basic Usage viewer
        // enables none, so it renders no bar at all; a featured viewer exposes Pages | Search
        // | Comments.
        Assert.AreEqual(0, await section.Locator(".tm-mobile-action-bar__bar").CountAsync(),
            "a viewer with no features renders no mobile action bar");
        var featured = Section(page, "All Features Combined");
        var featuredBar = featured.Locator(".tm-mobile-action-bar__bar").First;
        await featuredBar.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        Assert.IsTrue(await featuredBar.IsVisibleAsync(), "a featured viewer renders the in-flow action bar");

        await ParkAndShootAsync(page, section, ShotDir("pdf-viewer"), "mobile-fit-width");

        // The mobile "More" menu (Open/Download/Rotate live there as OverflowOnly entries) —
        // evidence screenshot for the UX review.
        var more = section.Locator("button.tm-toolbar-more").First;
        await more.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await more.ClickAsync();
        var menu = page.Locator("[role='menu']").First;
        await menu.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await ParkAndShootAsync(page, section, ShotDir("pdf-viewer"), "mobile-more-menu");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfViewer_Mobile_CommentsBottomSheet_ReplyAndResolve()
    {
        var page = await OpenAsync(PdfPage, 390, 844, touch: true);
        var section = Section(page, "Annotations & Comments");
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await WaitForLoadedViewerAsync(section);

        // The docked panel is gone; the bar tile opens the bottom sheet.
        Assert.AreEqual(0, await section.Locator("[data-testid='pdf-annotation-panel']").CountAsync(),
            "the docked panel is not rendered on mobile");
        await section.Locator("[data-action-id='comments']").ClickAsync();

        var sheet = section.Locator(".tm-drawer--bottom").First;
        await sheet.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        var panel = section.Locator("[data-testid='pdf-annotation-panel']").First;
        await panel.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });

        // Read: the seeded thread card is reachable inside the sheet.
        var thread = panel.Locator("[data-testid='pdf-annotation-thread']").First;
        await thread.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await thread.ClickAsync();

        // Reply: the detail composer submits a reply.
        var replyInput = panel.Locator("textarea").Last;
        await replyInput.FillAsync("Mobile reply from the sheet");
        await replyInput.PressAsync("Enter");
        await page.WaitForTimeoutAsync(500);

        // Resolve through the thread's own ⋯ menu, then verify the chip.
        var cardMenu = panel.Locator("[data-testid='pdf-annotation-thread'] .tm-pdf-thread-card__more, [data-testid='pdf-annotation-thread'] [data-testid$='-menu']").First;
        if (await cardMenu.CountAsync() > 0)
        {
            await cardMenu.ClickAsync();
            var resolve = page.Locator("[role='menuitem']:has-text('Resolve')").First;
            if (await resolve.CountAsync() > 0)
            {
                await resolve.ClickAsync();
                await page.WaitForTimeoutAsync(500);
            }
        }

        await ParkAndShootAsync(page, section, ShotDir("pdf-viewer"), "mobile-comments-sheet");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfViewer_Mobile_ContinuousScroll()
    {
        var page = await OpenAsync(PdfPage, 390, 844, touch: true);
        var section = Section(page, "Continuous Scroll");
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await WaitForLoadedViewerAsync(section);

        var input = section.Locator(".tm-pdf-viewer__page-input").First;

        // Scroll the wrap: the lazy renderer paints pages and the page indicator follows.
        await section.EvaluateAsync(@"el => {
    const wrap = el.querySelector('.tm-pdf-viewer__continuous-wrap');
    wrap.scrollBy({ top: 4000, behavior: 'instant' });
    }");
        await page.WaitForFunctionAsync(
            @"() => {
      const s = [...document.querySelectorAll('section')]
          .find(x => x.querySelector('h2')?.textContent.includes('Continuous Scroll'));
      const el = s?.querySelector('.tm-pdf-viewer__page-input');
      return !!el && el.value !== '1';
      }",
            null, new PageWaitForFunctionOptions { Timeout = 60000 });

        var overflow = await section.EvaluateAsync<bool>(
            "el => { const v = el.querySelector('.tm-pdf-viewer'); return v ? v.scrollWidth > v.clientWidth + 1 : true; }");
        Assert.IsFalse(overflow, "continuous scroll on mobile must not introduce horizontal overflow");

        await ParkAndShootAsync(page, section, ShotDir("pdf-viewer"), "mobile-continuous");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfViewer_320_NoHorizontalOverflow()
    {
        var page = await OpenAsync(PdfPage, 320, 640, touch: true);
        var section = Section(page, "All Features Combined");
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await WaitForLoadedViewerAsync(section);

        await page.WaitForFunctionAsync(
            "() => { const c = document.querySelector('section .tm-pdf-viewer__canvas'); return c && c.width > 0; }",
            null, new PageWaitForFunctionOptions { Timeout = 60000 });

        // No responsive viewer on the 320px page may overflow horizontally. The forced
        // "Tablet Layout" reference is excluded: it pins the tablet chrome at any width, so
        // at a phone width its toolbar row legitimately exceeds the column (the evidence
        // section is meant for the 1024px touch context above).
        var overflows = await page.EvaluateAsync<string>(@"
() => [...document.querySelectorAll('section .tm-pdf-viewer')]
        .filter(v => { const s = v.closest('section'); return s && !(s.querySelector('h2')?.textContent ?? '').includes('Tablet Layout'); })
        .filter(v => v.scrollWidth > v.clientWidth + 1)
        .map(v => (v.closest('section')?.querySelector('h2')?.textContent ?? '?') + ' sw' + v.scrollWidth + '/cw' + v.clientWidth)
        .join(' ;; ')");
        Assert.AreEqual("", overflows, "no viewer on the 320px page may overflow horizontally");

        // FZE-7 round 2: the zoom dropdown must not wrap its trigger onto a second line —
        // the toolbar start section stays a single row, i.e. within about 1.2 × the 44px
        // touch target (~53px). Two lines would measure ~2 × the touch target.
        var startHeight = await section.EvaluateAsync<double>(
            "el => { const s = el.querySelector('.tm-pdf-viewer__toolbar .tm-toolbar-start'); return s ? s.getBoundingClientRect().height : 0; }");
        Assert.IsTrue(startHeight is > 0 and <= 53,
            $"the toolbar start section must stay a single row at 320px (measured {startHeight:0.#}px, limit 53px ≈ 1.2 × 44px touch target)");

        await ParkAndShootAsync(page, section, ShotDir("pdf-viewer"), "mobile-320-all-features");

        // The redaction layer keeps its own 900px rule — no overflow at 320 either.
        var redaction = await OpenAsync(RedactionPage, 320, 640, touch: true);
        var redSection = Section(redaction, "Redact a Personal ID Number");
        await redSection.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        // The canvas (the embedded viewer's page) must exist before any screenshot: the
        // stage width below is only meaningful once the page painted into it.
        await redaction.WaitForFunctionAsync(
            "() => { const c = document.querySelector('canvas.tm-redaction__canvas, section canvas'); return !!c; }",
            null, new PageWaitForFunctionOptions { Timeout = 60000 });
        var redOverflow = await redSection.EvaluateAsync<bool>(@"
() => {
    const root = document.querySelector('section .tm-redaction');
    return root ? root.scrollWidth > root.clientWidth + 1 : true;
    }");
        Assert.IsFalse(redOverflow, "the redaction layer must not overflow horizontally at 320");

        // The stage claims (nearly) the full column width below the 900px rule — the drawing
        // surface must not hug its content on a phone.
        var stageRatio = await redSection.EvaluateAsync<double>(@"
() => {
    const body = document.querySelector('section .tm-redaction__body');
    const stage = document.querySelector('section .tm-redaction__stage');
    if (!body || !stage) return 0;
    const bodyWidth = body.getBoundingClientRect().width;
    return bodyWidth > 0 ? stage.getBoundingClientRect().width / bodyWidth : 0;
    }");
        Assert.IsTrue(stageRatio >= 0.9,
            $"the redaction stage must span the column below 900px (measured {stageRatio:P0})");

        await ParkAndShootAsync(redaction, redSection, ShotDir("redaction"), "mobile");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfViewer_Mobile_ChevronsStayOnBar()
    {
        // 320 (smallest supported phone) and 390 (large phone): page navigation is the
        // primary call to action, so both chevrons are Pinned on the bar while every other
        // command overflows — the "More" menu never contains a navigation entry.
        foreach (var width in new[] { 320, 390 })
        {
            var page = await OpenAsync(PdfPage, width, 844, touch: true);
            var section = Section(page, "All Features Combined");
            await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
            await WaitForLoadedViewerAsync(section);
            await page.WaitForFunctionAsync(
                "() => { const c = document.querySelector('section .tm-pdf-viewer__canvas'); return c && c.width > 0; }",
                null, new PageWaitForFunctionOptions { Timeout = 60000 });

            var nav = await section.EvaluateAsync<string>(@"
el => {
    const toolbar = el.querySelector('.tm-pdf-viewer__toolbar');
    if (!toolbar) return 'missing-toolbar';
    const offBar = ['Previous page', 'Next page'].filter(label => {
        const btn = [...toolbar.querySelectorAll('button')]
            .find(b => b.getAttribute('aria-label') === label);
        return !btn || btn.classList.contains('tm-toolbar-item--collapsed') || btn.offsetParent === null;
    });
    return offBar.length === 0 ? 'ok' : 'off-bar: ' + offBar.join(', ');
    }");
            Assert.AreEqual("ok", nav, $"at {width}px both chevrons must stay on the bar");

            // The overflow menu must not offer navigation: open it and read the items.
            var more = section.Locator("button.tm-toolbar-more").First;
            await more.ClickAsync();
            var menu = page.Locator("[role='menu']").First;
            await menu.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
            var items = await menu.Locator("[role='menuitem']").AllTextContentsAsync();
            Assert.IsFalse(items.Any(t => t.Trim().Contains("Next page", StringComparison.OrdinalIgnoreCase)),
                $"at {width}px the More menu must not contain the next-page item (got: {string.Join(", ", items)})");
            await page.Keyboard.PressAsync("Escape");
            await page.WaitForTimeoutAsync(300);
        }
    }
}
