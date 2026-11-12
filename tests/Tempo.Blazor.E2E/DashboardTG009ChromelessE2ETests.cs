using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// TG-009 (plan 92e96b25, phase 8): E2E captures of the chromeless widget presentation on the
/// <c>/dashboard-overview</c> demo page (WidgetChrome=EditOnly) and the <c>/dashboard</c> baseline
/// (WidgetChrome=Always). Locator screenshots per state and theme land in
/// <c>__screenshots__/dashboard/tg009/</c> and are UX-reviewed: the chromeless cards must read as
/// section cards (template title + link), the a11y tree keeps one region per widget, and edit mode
/// restores the full chrome without moving the grid.
/// </summary>
[TestClass]
public sealed class DashboardTG009ChromelessE2ETests : WasmTestBase
{
    private static string OutputDirectory =>
        Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "__screenshots__", "dashboard", "tg009");

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Overview_ChromelessView_LightAndDark()
    {
        foreach (var dark in new[] { false, true })
        {
            var page = await OpenPageAsync("/dashboard-overview");
            if (dark)
            {
                await page.Locator("[data-testid='theme-toggle']:visible").First.ClickAsync();
                await page.WaitForFunctionAsync(
                    "() => document.querySelector('[data-theme=\"dark\"]') !== null",
                    null, new PageWaitForFunctionOptions { Timeout = 10000 });
            }

            // Chromeless contract in the real browser: no widget header chrome, one region per widget,
            // every region named by its (sr-only) title, and the template's own title + link per card.
            var widgets = page.Locator(".tm-dashboard .tm-widget");
            await widgets.First.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
            var count = await widgets.CountAsync();
            Assert.AreEqual(5, count, "the seeded demo dashboard carries five widgets");
            Assert.AreEqual(0, await page.Locator(".tm-widget-header").CountAsync(),
                "outside edit mode no widget renders header chrome");
            Assert.AreEqual(5, await page.Locator(".tm-widget[data-widget-chrome='editonly']").CountAsync());
            Assert.AreEqual(5, await page.Locator(".tm-widget[role='region']").CountAsync(),
                "every chromeless widget stays a named region");
            for (var i = 0; i < count; i++)
            {
                var labelledBy = await widgets.Nth(i).GetAttributeAsync("aria-labelledby");
                Assert.IsFalse(string.IsNullOrEmpty(labelledBy), $"widget {i} is named via aria-labelledby");
                var title = page.Locator($"#{labelledBy}");
                Assert.AreEqual(1, await title.CountAsync(), $"widget {i}'s title id resolves");
                Assert.IsTrue((await title.EvaluateAsync<string>("(el) => el.className")).Contains("tm-sr-only"),
                    $"widget {i}'s region title is visually hidden");
            }
            Assert.AreEqual(5, await page.Locator(".tm-demo-ow__title").CountAsync(),
                "the template renders its own visible card titles");
            Assert.AreEqual(5, await page.Locator(".tm-demo-ow__link").CountAsync());

            await AssertNoBlazorErrorAsync(page);
            await SettleAsync(page);
            await CaptureAsync(page, $"overview-view-{(dark ? "dark" : "light")}");

            // Edit mode: the full chrome returns, the grid geometry does not move.
            var before = await widgets.Nth(0).GetAttributeAsync("style");
            await page.Locator("[data-testid='dashboard-edit']").ClickAsync();
            await page.Locator(".tm-dashboard--edit").First.WaitForAsync(
                new LocatorWaitForOptions { Timeout = 15000 });
            Assert.AreEqual(5, await page.Locator(".tm-widget-header").CountAsync(),
                "edit mode restores the full widget header");
            Assert.IsTrue(await page.Locator(".tm-widget-drag-handle").CountAsync() >= 1,
                "edit mode keeps the drag handle");
            var during = await page.Locator(".tm-dashboard .tm-widget").Nth(0).GetAttributeAsync("style");
            Assert.AreEqual(before, during, "entering edit mode must not move the grid");
            await AssertNoBlazorErrorAsync(page);
            await SettleAsync(page);
            await CaptureAsync(page, $"overview-edit-{(dark ? "dark" : "light")}");

            // Back to view mode through Cancel: chrome off again, geometry identical.
            await page.Locator(".tm-dashboard-toolbar-right .tm-btn-ghost").ClickAsync();
            await page.Locator(".tm-dashboard--edit").First.WaitForAsync(
                new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 15000 });
            Assert.AreEqual(0, await page.Locator(".tm-widget-header").CountAsync(),
                "leaving edit mode drops the chrome again");
            var after = await page.Locator(".tm-dashboard .tm-widget").Nth(0).GetAttributeAsync("style");
            Assert.AreEqual(before, after, "leaving edit mode restores the exact layout");
            await AssertNoBlazorErrorAsync(page);

            await page.CloseAsync();
        }
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Overview_ChromelessView_ReducedMotion()
    {
        var page = await OpenPageAsync("/dashboard-overview", reducedMotion: true);
        await page.Locator(".tm-dashboard .tm-widget").First.WaitForAsync(
            new LocatorWaitForOptions { Timeout = 30000 });

        // Reduced motion must not change the chromeless contract (no animation depends on it).
        Assert.AreEqual(0, await page.Locator(".tm-widget-header").CountAsync());
        var media = await page.EvaluateAsync<string>(
            "() => matchMedia('(prefers-reduced-motion: reduce)').matches ? 'reduce' : 'no-preference'");
        Assert.AreEqual("reduce", media, "the page really rendered under reduced motion");
        await SettleAsync(page);
        await CaptureAsync(page, "overview-view-reduced-motion");
        await page.CloseAsync();
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Overview_ChromelessView_PhoneStage()
    {
        var page = await CreatePageAsync();
        await page.SetViewportSizeAsync(390, 844);
        await page.GotoAsync(BaseUrl.TrimEnd('/') + "/dashboard-overview");
        await WaitBlazorReadyAsync(page);
        var widgets = page.Locator(".tm-dashboard .tm-widget");
        await widgets.First.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });

        Assert.AreEqual(0, await page.Locator(".tm-widget-header").CountAsync(),
            "the phone stacks the widgets chromeless too");
        Assert.AreEqual(5, await page.Locator(".tm-widget[role='region']").CountAsync());
        var overflow = await page.EvaluateAsync<bool>(
            "() => document.documentElement.scrollWidth > document.documentElement.clientWidth");
        Assert.IsFalse(overflow, "no horizontal overflow on the phone stage");
        await AssertNoBlazorErrorAsync(page);
        await SettleAsync(page);
        await CaptureAsync(page, "overview-view-phone");
        await page.CloseAsync();
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task BaselineDashboard_AlwaysChrome_Unchanged()
    {
        // Regression guard: the default WidgetChrome=Always page keeps the pre-TG-009 chrome
        // (header with icon tile, title and collapse chevron on every widget).
        var page = await OpenPageAsync("/dashboard");
        var widgets = page.Locator(".tm-dashboard .tm-widget");
        await widgets.First.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });

        Assert.AreEqual(5, await widgets.CountAsync());
        Assert.AreEqual(5, await page.Locator(".tm-widget-header").CountAsync(),
            "the baseline dashboard still renders the full widget header");
        Assert.AreEqual(0, await page.Locator(".tm-widget[data-widget-chrome]").CountAsync(),
            "the default Always path emits no chrome marker — the DOM stays strictly pre-TG-009");
        Assert.AreEqual(0, await page.Locator(".tm-widget[role='region']").CountAsync(),
            "chrome-ful widgets keep the pre-TG-009 a11y tree");
        Assert.AreEqual(5, await page.Locator(".tm-widget-icon").CountAsync());
        await SettleAsync(page);
        await CaptureAsync(page, "baseline-view-always");
        await page.CloseAsync();
    }

    // ── helpers ───────────────────────────────────────────────────

    private async Task<IPage> OpenPageAsync(string route, bool reducedMotion = false)
    {
        var page = await CreatePageAsync();
        if (reducedMotion)
        {
            await page.EmulateMediaAsync(new PageEmulateMediaOptions { ReducedMotion = ReducedMotion.Reduce });
        }

        await page.SetViewportSizeAsync(1440, 900);
        await page.GotoAsync(BaseUrl.TrimEnd('/') + route);
        await WaitBlazorReadyAsync(page);
        return page;
    }

    private static async Task WaitBlazorReadyAsync(IPage page)
    {
        await page.WaitForFunctionAsync(
            "() => document.body !== null && document.body.hasAttribute('data-blazor-ready')",
            null, new PageWaitForFunctionOptions { Timeout = 30000 });
    }

    private static async Task AssertNoBlazorErrorAsync(IPage page)
    {
        var visible = await page.Locator("#blazor-error-ui").IsVisibleAsync();
        Assert.IsFalse(visible, "the Blazor error UI must stay hidden");
    }

    /// <summary>Parks pointer and focus so the shot shows the resting page, then lets fonts settle.</summary>
    private static async Task SettleAsync(IPage page)
    {
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync("() => document.activeElement && document.activeElement.blur()");
        await page.WaitForTimeoutAsync(400);
    }

    private async Task CaptureAsync(IPage page, string name)
    {
        Directory.CreateDirectory(OutputDirectory);
        var path = Path.Combine(OutputDirectory, name + ".png");
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = path,
            Type = ScreenshotType.Png,
            FullPage = false,
            Timeout = 60_000,
        });
        TestContext.AddResultFile(path);
        var info = new FileInfo(path);
        Assert.IsTrue(info.Length > 2 * 1024, $"screenshot {name}.png looks empty ({info.Length} bytes)");
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

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx.");
    }
}
