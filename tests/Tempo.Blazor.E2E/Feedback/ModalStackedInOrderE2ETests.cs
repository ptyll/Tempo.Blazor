using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E.Feedback;

/// <summary>
/// Fáze 6 (plan 98c80bc3): <see cref="FooterLayout"/>.StackedInOrder on TmModal, exercised on the
/// /modal-dialog demo's five-action footer. Pins the DOM-order paint, the scroll cap
/// (--tm-modal-footer-max-height) and the wrapping labels, with light/dark screenshots at the
/// desktop and phone widths.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class ModalStackedInOrderE2ETests : WasmTestBase
{
    private static readonly string ShotDir =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "__screenshots__", "modal-fze6");

    private async Task<IPage> OpenAsync(int width, int height, bool dark)
    {
        var context = await CreateContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            IgnoreHTTPSErrors = true,
            Locale = "en-US",
        });
        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}/modal-dialog");

        if (dark)
        {
            await page.Locator("[data-testid='theme-toggle']:visible").First.ClickAsync();
            await page.WaitForFunctionAsync(
                "() => document.querySelector('[data-theme=\"dark\"]') !== null",
                null, new PageWaitForFunctionOptions { Timeout = 10000 });
        }

        return page;
    }

    private static async Task<string> ShootAsync(IPage page, string name)
    {
        Directory.CreateDirectory(ShotDir);
        var path = Path.Combine(ShotDir, $"{name}.png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = false, Timeout = 60_000 });
        return path;
    }

    [TestMethod]
    public async Task StackedInOrderFooter_PaintsTheDeclaredOrder_WrapsLabels_AndScrollsUnderTheCap()
    {
        var page = await OpenAsync(1440, 900, dark: false);

        await page.GetByTestId("open-stacked-in-order").ClickAsync();
        var footer = page.Locator(".tm-modal-footer");
        await footer.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });

        // The class contract the stylesheet keys off: the stacked chrome plus the DOM-order flip.
        var classes = await footer.GetAttributeAsync("class");
        StringAssert.Contains(classes, "tm-modal-footer--stacked");
        StringAssert.Contains(classes, "tm-modal-footer--stacked-in-order");

        // DOM order is the paint order: the FIRST declared action sits at the TOP. (The legacy
        // Stacked is column-reverse, where the first child would paint last/bottom.)
        var labels = await page.Locator(".tm-modal-footer .tm-btn .tm-btn-label")
            .EvaluateAllAsync<string[]>("els => els.map(e => e.textContent.trim())");
        CollectionAssert.AreEqual(
            new[] { "Option A — Draft", "Option B — Review", "Cancel", "OK" },
            labels.Where(l => !l.StartsWith("Option C")).ToArray(),
            "the footer children must stay in DOM order");

        // The long third label wraps onto its own row instead of truncating: white-space normal
        // and the button renders across more than one line (taller than its single-line peers —
        // the label span is display: contents, so its height shows on the button box).
        var wrap = await page.EvaluateAsync<string>(
            """
            () => {
                const btns = [...document.querySelectorAll('.tm-modal-footer .tm-btn')];
                const long = btns.find(b => b.textContent.includes('Option C'));
                const short = btns.find(b => b.textContent.includes('Option A'));
                if (!long || !short) return 'missing buttons';
                return JSON.stringify({
                    ws: getComputedStyle(long).whiteSpace,
                    longH: long.clientHeight,
                    shortH: short.clientHeight,
                    longW: long.clientWidth,
                    labelClass: long.querySelector('.tm-btn-label')?.className,
                    labelDisplay: long.querySelector('.tm-btn-label') ? getComputedStyle(long.querySelector('.tm-btn-label')).display : null,
                });
            }
            """);
        TestContext.WriteLine($"wrap probe: {wrap}");
        using var probe = System.Text.Json.JsonDocument.Parse(wrap);
        Assert.IsTrue(probe.RootElement.GetProperty("ws").GetString() == "normal"
            && probe.RootElement.GetProperty("longH").GetDouble() > probe.RootElement.GetProperty("shortH").GetDouble() + 4,
            $"the long action label must wrap onto a second line: {wrap}");

        // With the cap lowered the footer becomes its own scrollport.
        await page.EvaluateAsync(
            "() => document.querySelector('.tm-modal').style.setProperty('--tm-modal-footer-max-height', '120px')");
        var scroll = await page.EvaluateAsync<string>(
            """
            () => {
                const f = document.querySelector('.tm-modal-footer');
                const cs = getComputedStyle(f);
                return JSON.stringify({
                    sh: f.scrollHeight, ch: f.clientHeight,
                    oy: cs.overflowY,
                    maxH: cs.maxHeight,
                    shrink: cs.flexShrink,
                    dir: cs.flexDirection,
                    kids: [...f.children].map(k => 'top' + Math.round(k.offsetTop) + '/h' + Math.round(k.getBoundingClientRect().height)).join(','),
                    fh: Math.round(f.getBoundingClientRect().height),
                    lastBottom: Math.round([...f.children].at(-1).getBoundingClientRect().bottom - f.getBoundingClientRect().top),
                    inner: f.innerHTML.length,
                    modalStyle: document.querySelector('.tm-modal').getAttribute('style'),
                });
            }
            """);
        TestContext.WriteLine($"scroll probe: {scroll}");
        using var scrollDoc = System.Text.Json.JsonDocument.Parse(scroll);
        Assert.IsTrue(
            scrollDoc.RootElement.GetProperty("sh").GetDouble() > scrollDoc.RootElement.GetProperty("ch").GetDouble() + 1
            && scrollDoc.RootElement.GetProperty("oy").GetString() == "auto",
            $"the capped footer must be its own scrollport: {scroll}");

        await ShootAsync(page, "stacked-in-order-scrolling-light-1440");
    }

    [TestMethod]
    public async Task StackedInOrderFooter_ScreenshotsLightAndDark_AtDesktopAndPhone()
    {
        foreach (var (width, height, dark, name) in new[]
        {
            (1440, 900, false, "stacked-in-order-light-1440"),
            (1440, 900, true, "stacked-in-order-dark-1440"),
            (390, 844, false, "stacked-in-order-light-390"),
            (390, 844, true, "stacked-in-order-dark-390"),
        })
        {
            var page = await OpenAsync(width, height, dark);
            await page.GetByTestId("open-stacked-in-order").ClickAsync();
            await page.Locator(".tm-modal-footer").WaitForAsync(
                new LocatorWaitForOptions { Timeout = 15000 });
            await page.WaitForTimeoutAsync(400); // let the surface settle (animations/fonts)

            var path = await ShootAsync(page, name);
            Assert.IsTrue(new FileInfo(path).Length > 2_000, $"{name} screenshot must be a real frame");
        }
    }
}
