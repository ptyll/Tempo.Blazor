using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// TG-007 (plan 92e96b25, phase 9): interface density E2E. The <c>/density</c> demo page renders
/// a form, a data table, a card, tabs and a sidebar inside a <c>data-tm-density</c> scope with a
/// Comfortable/Compact switcher. These tests measure the real-browser computed metrics on both
/// sides of the switch — comfortable must equal the pre-TG-007 values, compact must shrink every
/// hooked metric — and capture locator screenshots (light/dark, reduced motion, phone) that are
/// UX-reviewed. Screenshot output lands in <c>__screenshots__/density/tg007/</c>.
/// </summary>
[TestClass]
public sealed class DensityTG007E2ETests : WasmTestBase
{
    private static string OutputDirectory =>
        Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "__screenshots__", "density", "tg007");

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Comfortable_MatchesPreTg007Metrics()
    {
        var page = await OpenPageAsync("/density");

        // The scope renders the explicit comfortable value; no attribute is required for it.
        Assert.AreEqual("comfortable", await page.Locator("[data-testid='density-scope']")
            .GetAttributeAsync("data-tm-density"));

        var metrics = await ReadMetricsAsync(page);

        // Control height: --tm-input-height-md = 2.375rem = 38px at the default 16px root font.
        Assert.AreEqual(38, metrics.ButtonHeight, 1, "comfortable TmButton keeps the 38 px box");
        Assert.AreEqual(38, metrics.InputHeight, 1, "comfortable TmTextInput keeps the 38 px box");
        Assert.AreEqual(38, metrics.SelectHeight, 1, "comfortable TmSelect keeps the 38 px box");
        Assert.AreEqual(38, metrics.SearchHeight, 1, "comfortable TmSearchInput keeps the 38 px box");

        // Table cells: --tm-data-table-cell-py/px = space-3/space-4 = 12/16 px.
        Assert.AreEqual(12, metrics.CellPaddingTop, 1, "comfortable cell vertical padding is 12 px");
        Assert.AreEqual(16, metrics.CellPaddingLeft, 1, "comfortable cell horizontal padding is 16 px");

        // Card: content padding space-5 = 20px.
        Assert.AreEqual(20, metrics.CardContentPadding, 1, "comfortable card content padding is 20 px");

        // Tabs: Line row = 2×10 px padding + 20 px line box + 2 px baseline = 42 px.
        Assert.AreEqual(42, metrics.TabHeight, 1.5, "comfortable Line tab row is 42 px");

        // Sidebar item: padding space-2 = 8 px top.
        Assert.AreEqual(8, metrics.SidebarLinkPaddingTop, 1, "comfortable sidebar item block padding is 8 px");

        await AssertNoBlazorErrorAsync(page);
        await page.CloseAsync();
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Compact_ShrinksEveryHookedMetric_AndSwitchesBack()
    {
        var page = await OpenPageAsync("/density");
        var comfortable = await ReadMetricsAsync(page);

        await SwitchToAsync(page, "Compact");
        Assert.AreEqual("compact", await page.Locator("[data-testid='density-scope']")
            .GetAttributeAsync("data-tm-density"));

        var compact = await ReadMetricsAsync(page);

        Assert.AreEqual(32, compact.ButtonHeight, 1, "compact TmButton reads the 2rem control height");
        Assert.AreEqual(32, compact.InputHeight, 1, "compact TmTextInput reads the 2rem control height");
        Assert.AreEqual(32, compact.SelectHeight, 1, "compact TmSelect reads the 2rem control height");
        Assert.AreEqual(32, compact.SearchHeight, 1, "compact TmSearchInput reads the 2rem control height");

        Assert.AreEqual(8, compact.CellPaddingTop, 1, "compact cell vertical padding is space-2");
        Assert.AreEqual(12, compact.CellPaddingLeft, 1, "compact cell horizontal padding is space-3");
        Assert.AreEqual(16, compact.CardContentPadding, 1, "compact card content padding is space-4");
        Assert.AreEqual(34, compact.TabHeight, 1.5, "compact Line tab row is 2×6 + 20 + 2 = 34 px");
        Assert.AreEqual(6, compact.SidebarLinkPaddingTop, 1, "compact sidebar item block padding is space-1-5");

        // Every hooked metric must actually shrink — a no-op compact would pass the absolute
        // assertions above only if the comfortable ones were already wrong.
        Assert.IsTrue(compact.ButtonHeight < comfortable.ButtonHeight, "button shrinks");
        Assert.IsTrue(compact.CellPaddingTop < comfortable.CellPaddingTop, "cells shrink");
        Assert.IsTrue(compact.CardContentPadding < comfortable.CardContentPadding, "card shrinks");
        Assert.IsTrue(compact.TabHeight < comfortable.TabHeight, "tabs shrink");
        Assert.IsTrue(compact.SidebarLinkPaddingTop < comfortable.SidebarLinkPaddingTop, "sidebar shrinks");

        // And switching back restores the exact comfortable metrics (no residue).
        await SwitchToAsync(page, "Comfortable");
        var restored = await ReadMetricsAsync(page);
        Assert.AreEqual(comfortable.ButtonHeight, restored.ButtonHeight, 0.5, "button restores");
        Assert.AreEqual(comfortable.TabHeight, restored.TabHeight, 0.5, "tabs restore");
        Assert.AreEqual(comfortable.CellPaddingTop, restored.CellPaddingTop, 0.5, "cells restore");

        await AssertNoBlazorErrorAsync(page);
        await page.CloseAsync();
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Density_LightAndDark_Screenshots()
    {
        foreach (var (density, dark) in new[] { ("comfortable", false), ("comfortable", true), ("compact", false), ("compact", true) })
        {
            var page = await OpenPageAsync("/density");
            if (dark)
            {
                await page.Locator("[data-testid='theme-toggle']:visible").First.ClickAsync();
                await page.WaitForFunctionAsync(
                    "() => document.querySelector('[data-theme=\"dark\"]') !== null",
                    null, new PageWaitForFunctionOptions { Timeout = 10000 });
            }

            if (density == "compact")
            {
                await SwitchToAsync(page, "Compact");
            }

            await AssertNoBlazorErrorAsync(page);
            await SettleAsync(page);
            await CaptureAsync(page, $"density-{density}-{(dark ? "dark" : "light")}");
            await page.CloseAsync();
        }
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Compact_ReducedMotion_KeepsTheContract()
    {
        var page = await OpenPageAsync("/density", reducedMotion: true);
        await SwitchToAsync(page, "Compact");

        var media = await page.EvaluateAsync<string>(
            "() => matchMedia('(prefers-reduced-motion: reduce)').matches ? 'reduce' : 'no-preference'");
        Assert.AreEqual("reduce", media, "the page really rendered under reduced motion");

        // Reduced motion must not change the density contract (density is metric-only, no animation).
        var metrics = await ReadMetricsAsync(page);
        Assert.AreEqual(32, metrics.ButtonHeight, 1, "compact control height holds under reduced motion");

        await AssertNoBlazorErrorAsync(page);
        await SettleAsync(page);
        await CaptureAsync(page, "density-compact-reduced-motion");
        await page.CloseAsync();
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Compact_PhoneStage_NoOverflow()
    {
        var page = await CreatePageAsync();
        await page.SetViewportSizeAsync(390, 844);
        await page.GotoAsync(BaseUrl.TrimEnd('/') + "/density");
        await WaitBlazorReadyAsync(page);
        await SwitchToAsync(page, "Compact");

        var metrics = await ReadMetricsAsync(page);
        Assert.AreEqual(32, metrics.ButtonHeight, 1, "compact holds on the phone stage");

        var overflow = await page.EvaluateAsync<bool>(
            "() => document.documentElement.scrollWidth > document.documentElement.clientWidth");
        Assert.IsFalse(overflow, "no horizontal overflow on the 390 px stage");

        await AssertNoBlazorErrorAsync(page);
        await SettleAsync(page);
        await CaptureAsync(page, "density-compact-phone");
        await page.CloseAsync();
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Compact_TouchPointer_KeepsTheCoarseTargets()
    {
        // The 390 px phone stage above emulates a desktop pointer, so its 32 px inputs never
        // exercised the touch contract. A HasTouch context reports (pointer: coarse): compact
        // must still shrink the density metrics, and the sidebar items must keep the 44 px
        // coarse-pointer floor — the compact 6 px block padding alone would give a ~30 px row.
        var page = await CreatePageAsync(hasTouch: true);
        await page.SetViewportSizeAsync(390, 844);
        await page.GotoAsync(BaseUrl.TrimEnd('/') + "/density");
        await WaitBlazorReadyAsync(page);

        Assert.IsTrue(
            await page.EvaluateAsync<bool>("() => matchMedia('(pointer: coarse)').matches"),
            "precondition: the HasTouch context must emulate a coarse pointer");

        await SwitchToAsync(page, "Compact");

        var metrics = await ReadMetricsAsync(page);
        Assert.AreEqual(32, metrics.ButtonHeight, 1, "compact control height holds under a coarse pointer");
        Assert.AreEqual(6, metrics.SidebarLinkPaddingTop, 1, "compact sidebar block padding holds under a coarse pointer");

        var minBlock = await page.Locator("[data-testid='density-sidebar'] .tm-sidebar-nav-link").First
            .EvaluateAsync<double>("(el) => parseFloat(getComputedStyle(el).minBlockSize)");
        Assert.IsTrue(minBlock >= 44,
            $"sidebar items keep a >= 44 px min-block-size on a coarse pointer (got {minBlock})");

        await AssertNoBlazorErrorAsync(page);
        await SettleAsync(page);
        await CaptureAsync(page, "density-compact-phone-touch");
        await page.CloseAsync();
    }

    // ── helpers ───────────────────────────────────────────────────

    private sealed record DensityMetrics(
        double ButtonHeight, double InputHeight, double SelectHeight, double SearchHeight,
        double CellPaddingTop, double CellPaddingLeft, double CardContentPadding,
        double TabHeight, double SidebarLinkPaddingTop);

    private static async Task<DensityMetrics> ReadMetricsAsync(IPage page)
    {
        var scope = page.Locator("[data-testid='density-scope']");
        await scope.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });

        double Height(ILocator locator) =>
            locator.EvaluateAsync<double>("(el) => el.getBoundingClientRect().height").Result;

        var button = Height(page.Locator("[data-testid='density-button']"));
        // TmTextInput and TmSearchInput spread AdditionalAttributes on the <input> itself;
        // TmSelect keeps them on the wrapper, so its <select> is a descendant.
        var input = Height(page.Locator("[data-testid='density-input-name']"));
        var select = Height(page.Locator("[data-testid='density-select-role'] select"));
        var search = Height(page.Locator("[data-testid='density-search']"));

        var cell = page.Locator("[data-testid='density-table'] .tm-data-table td").First;
        var cellPaddingTop = await cell.EvaluateAsync<double>(
            "(el) => parseFloat(getComputedStyle(el).paddingTop)");
        var cellPaddingLeft = await cell.EvaluateAsync<double>(
            "(el) => parseFloat(getComputedStyle(el).paddingLeft)");

        var cardPadding = await page.Locator("[data-testid='density-card'] .tm-card-content")
            .EvaluateAsync<double>("(el) => parseFloat(getComputedStyle(el).paddingTop)");

        var tab = Height(page.Locator("[data-testid='density-tabs'] .tm-tab").First);

        var sidebarPadding = await page.Locator("[data-testid='density-sidebar'] .tm-sidebar-nav-link").First
            .EvaluateAsync<double>("(el) => parseFloat(getComputedStyle(el).paddingTop)");

        return new DensityMetrics(button, input, select, search,
            cellPaddingTop, cellPaddingLeft, cardPadding, tab, sidebarPadding);
    }

    private static async Task SwitchToAsync(IPage page, string label)
    {
        var option = page.Locator("[data-testid='density-switcher'] [role='radio']", new PageLocatorOptions { HasTextString = label }).First;
        await option.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await option.ClickAsync();
        await page.WaitForFunctionAsync(
            $"() => document.querySelector(\"[data-testid='density-scope']\")?.getAttribute('data-tm-density') === '{label.ToLowerInvariant()}'",
            null, new PageWaitForFunctionOptions { Timeout = 15000 });
    }

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
        await page.Locator("[data-testid='density-scope']").WaitForAsync(
            new LocatorWaitForOptions { Timeout = 30000 });
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
