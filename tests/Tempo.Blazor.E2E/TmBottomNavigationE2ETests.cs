using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan 92e96b25, phase 1 (TG-001) — the <c>TmBottomNavigation</c> bar on the real
/// <c>/new-components</c> demo: a localized accessible name (<c>aria-label</c>) instead of a raw
/// resource key, overridable through the <c>AriaLabel</c> parameter, verified in light and dark
/// mode and on a phone viewport. Diagnostic screenshots go to gitignored TestResults, never a
/// committed baseline.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class TmBottomNavigationE2ETests : WasmTestBase
{
    private const string Route = "/new-components";
    private const string Section = "[data-testid='bottom-navigation-section']";

    private string ShotDir
        => Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "bottom-navigation");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TempoBlazor.slnx")))
        {
            dir = dir.Parent!;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [TestInitialize]
    public void EnsureShotDir() => Directory.CreateDirectory(ShotDir);

    private async Task<IPage> OpenAsync(int width = 1440, int height = 1000, bool reducedMotion = false)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            IgnoreHTTPSErrors = true,
            ReducedMotion = reducedMotion ? ReducedMotion.Reduce : ReducedMotion.NoPreference,
        });
        RegisterContext(context);
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, message) => errors.Add(message);
        await page.GotoAsync($"{BaseUrl}{Route}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        var section = page.Locator(Section).First;
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await section.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(400);
        Assert.AreEqual(0, errors.Count, "The page raised: " + string.Join(" | ", errors));
        return page;
    }

    private async Task ShotAsync(IPage page, string name)
    {
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync("() => document.activeElement instanceof HTMLElement && document.activeElement.blur()");
        await page.WaitForTimeoutAsync(250);
        var path = Path.Combine(ShotDir, $"{name}.png");
        await page.Locator(Section).First.ScreenshotAsync(new LocatorScreenshotOptions { Path = path });
        Assert.IsTrue(await page.EvaluateAsync<bool>("() => !document.querySelector('#blazor-error-ui') || document.querySelector('#blazor-error-ui').offsetParent === null"),
            "the Blazor error overlay must stay hidden");
        Assert.IsTrue(new FileInfo(path).Length > 2 * 1024, $"screenshot {name} looks empty");
    }

    [TestMethod]
    public async Task BottomNavigation_Has_Localized_Accessible_Name_Not_A_Raw_Key()
    {
        var page = await OpenAsync();

        var nav = page.Locator($"{Section} nav.tm-bottom-nav").First;
        var ariaLabel = await nav.GetAttributeAsync("aria-label");
        Assert.IsFalse(string.IsNullOrWhiteSpace(ariaLabel), "the bar must carry an accessible name");
        // The demo hosts run in the browser's negotiated culture (cs by default): the name must be one
        // of the four localized strings, never the raw key.
        var localizedNames = new[]
        {
            "Main navigation", "Hlavní navigace", "Hlavná navigácia", "Navigation principale",
        };
        CollectionAssert.Contains(localizedNames, ariaLabel,
            "the accessible name must be a localized name, not a raw resource key");

        // The raw key must never leak into the DOM — not in the attribute, not in any text.
        Assert.IsFalse((await nav.GetAttributeAsync("aria-label"))!.Contains("TmBottomNavigation_AriaLabel"),
            "aria-label must not contain the raw resource key");
        Assert.AreEqual(0, await page.Locator($"text=TmBottomNavigation_AriaLabel").CountAsync(),
            "the raw resource key must not render anywhere on the page");

        await ShotAsync(page, "bottom-nav-light");
    }

    [TestMethod]
    public async Task BottomNavigation_Dark_Mode_Keeps_The_Accessible_Name_And_Contrast()
    {
        var page = await OpenAsync();

        var nav = page.Locator($"{Section} nav.tm-bottom-nav").First;
        var lightLabel = await nav.GetAttributeAsync("aria-label");
        Assert.IsFalse(string.IsNullOrWhiteSpace(lightLabel));

        await ToggleDarkModeAsync(page);
        await page.WaitForTimeoutAsync(400);

        Assert.AreEqual(lightLabel, await nav.GetAttributeAsync("aria-label"),
            "theming must not change the accessible name");

        var background = await nav.EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");
        StringAssert.StartsWith(background, "rgb(", "the bar paints a resolved token background in dark mode");

        await ShotAsync(page, "bottom-nav-dark");
    }

    [TestMethod]
    public async Task BottomNavigation_On_A_Phone_Viewport_Fits_Without_Horizontal_Overflow()
    {
        var page = await OpenAsync(width: 375, height: 720);

        var nav = page.Locator($"{Section} nav.tm-bottom-nav").First;
        var box = await nav.BoundingBoxAsync();
        Assert.IsNotNull(box);
        Assert.IsTrue(box!.Width <= 375, $"the bar fits the 375 px viewport (was {box.Width})");

        // Edge case: four items share the narrow bar; every label stays readable (non-zero size).
        var texts = nav.Locator(".tm-bottom-nav__text");
        Assert.AreEqual(4, await texts.CountAsync(), "the demo renders four items");
        for (var i = 0; i < await texts.CountAsync(); i++)
        {
            var textBox = await texts.Nth(i).BoundingBoxAsync();
            Assert.IsNotNull(textBox);
            Assert.IsTrue(textBox!.Width > 0 && textBox.Height > 0, $"item {i} label must be visible");
        }

        await ShotAsync(page, "bottom-nav-phone");
    }

    [TestMethod]
    public async Task BottomNavigation_Item_Click_Updates_The_Selection()
    {
        var page = await OpenAsync();

        var items = page.Locator($"{Section} .tm-bottom-nav__item");
        await items.Nth(2).Locator("button, a").ClickAsync();
        await page.WaitForTimeoutAsync(300);

        StringAssert.Matches((await items.Nth(2).GetAttributeAsync("class"))?.Trim() ?? string.Empty,
            new System.Text.RegularExpressions.Regex("--active"),
            "clicking the third item marks it active");
        var activeReadout = await page.Locator($"{Section} p").First.InnerTextAsync();
        StringAssert.Contains(activeReadout, "Favorites", "the demo read-out follows the selection");

        await ShotAsync(page, "bottom-nav-interaction");
    }
}

/// <summary>The same bar on the Blazor Server demo (:7107).</summary>
[TestClass]
[TestCategory("Server")]
public sealed class TmBottomNavigationServerE2ETests : ServerTestBase
{
    [TestMethod]
    public async Task BottomNavigation_Renders_With_A_Localized_Name_On_The_Server_Host()
        => await TmBottomNavigationSmoke.RunAsync(BaseUrl, () => CreateContextAsync(), (page, url) => GotoAppReadyAsync(page, url));
}

/// <summary>The same bar on the InteractiveAuto demo (:7108).</summary>
[TestClass]
[TestCategory("InteractiveAuto")]
public sealed class TmBottomNavigationInteractiveAutoE2ETests : InteractiveAutoTestBase
{
    [TestMethod]
    public async Task BottomNavigation_Renders_With_A_Localized_Name_On_The_InteractiveAuto_Host()
        => await TmBottomNavigationSmoke.RunAsync(BaseUrl, () => CreateContextAsync(), (page, url) => GotoAppReadyAsync(page, url));
}

/// <summary>The host-independent smoke assertions both non-WASM lanes run.</summary>
internal static class TmBottomNavigationSmoke
{
    private const string Section = "[data-testid='bottom-navigation-section']";

    public static async Task RunAsync(
        string baseUrl,
        Func<Task<IBrowserContext>> createContext,
        Func<IPage, string, Task> gotoReady)
    {
        var context = await createContext();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        var errors = new List<string>();
        page.PageError += (_, message) => errors.Add(message);

        await gotoReady(page, $"{baseUrl}/new-components");
        var nav = page.Locator($"{Section} nav.tm-bottom-nav").First;
        await nav.WaitForAsync(new LocatorWaitForOptions { Timeout = 90000 });

        var ariaLabel = await nav.GetAttributeAsync("aria-label");
        Assert.IsFalse(string.IsNullOrWhiteSpace(ariaLabel), "every host renders a localized accessible name");
        Assert.IsFalse(ariaLabel!.Contains("TmBottomNavigation_AriaLabel", StringComparison.Ordinal),
            "the raw resource key must not leak on this host either");

        Assert.AreEqual(0, errors.Count, "The page raised: " + string.Join(" | ", errors));
    }
}
