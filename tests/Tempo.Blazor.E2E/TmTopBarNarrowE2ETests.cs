using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan 92e96b25, phase 3 (TG-003) — <c>TmTopBar</c> on the real <c>/layout</c> demo in a narrow
/// container: below 480px of the bar's OWN box (a <c>@container tm-topbar</c> query, never the
/// viewport) the Auto user name gives way (the avatar stays), the command palette trigger collapses
/// to its square icon button (the localized aria-label stays the accessible name) and the actions
/// never wrap or overflow. <c>UserNameDisplay</c> (Auto default / Always / Never) pins the name for
/// hosts that need it. Assertions read COMPUTED styles and real geometry; locator screenshots land
/// in gitignored <c>TestResults/topbar-narrow</c> for the UX review — never a committed baseline.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class TmTopBarNarrowE2ETests : WasmTestBase
{
    private const string Route = "/layout";
    private const string Demo = "[data-testid='topbar-demo']";

    private string ShotDir
        => Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "topbar-narrow");

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

    private async Task<IPage> OpenAsync(int width, int height = 844, bool touch = false, bool reducedMotion = false)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = touch,
            IsMobile = touch,
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
            ReducedMotion = reducedMotion ? ReducedMotion.Reduce : ReducedMotion.NoPreference,
        });
        RegisterContext(context);
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, message) => errors.Add(message);
        await page.GotoAsync($"{BaseUrl}{Route}");
        await WaitForAppReadyAsync(page);
        var demo = page.Locator(Demo).First;
        await demo.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await page.WaitForTimeoutAsync(400);
        Assert.AreEqual(0, errors.Count, "The page raised: " + string.Join(" | ", errors));
        return page;
    }

    /// <summary>Per-component locator screenshot after ScrollIntoViewIfNeeded, with the health checks.</summary>
    private async Task ShotAsync(IPage page, ILocator subject, string name)
    {
        await subject.ScrollIntoViewIfNeededAsync();
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync("() => document.activeElement instanceof HTMLElement && document.activeElement.blur()");
        await page.WaitForTimeoutAsync(250);
        var path = Path.Combine(ShotDir, $"{name}.png");
        await subject.ScreenshotAsync(new LocatorScreenshotOptions { Path = path });
        Assert.IsTrue(await page.EvaluateAsync<bool>(
                "() => !document.querySelector('#blazor-error-ui') || document.querySelector('#blazor-error-ui').offsetParent === null"),
            "the Blazor error overlay must stay hidden");
        Assert.IsTrue(new FileInfo(path).Length > 2 * 1024, $"screenshot {name} looks empty");
    }

    private static async Task AssertNoHorizontalOverflowAsync(IPage page)
    {
        var metrics = await page.EvaluateAsync<double[]>(
            "() => [document.documentElement.scrollWidth, window.innerWidth]");
        Assert.IsTrue(metrics[0] <= metrics[1] + 1,
            $"the document overflows horizontally: scrollWidth {metrics[0]} > viewport {metrics[1]}");
    }

    // ── The narrow bar itself (390 px phone) ───────────────────────────────────

    [TestMethod]
    public async Task At_390_The_Bar_Sheds_The_Name_And_Hint_Without_Overflow()
    {
        // The phase acceptance case: menu button + search + bell + a LONG user name on a 390px phone.
        var page = await OpenAsync(390);

        await AssertNoHorizontalOverflowAsync(page);

        var bar = page.Locator(Demo).First;
        var name = bar.Locator(".tm-topbar-user-name");
        Assert.AreEqual("none", await name.EvaluateAsync<string>("el => getComputedStyle(el).display"),
            "below 480px of the bar's own box the Auto name gives way");
        Assert.IsTrue(await bar.Locator(".tm-topbar-user-button").IsVisibleAsync(),
            "the avatar (and the menu behind it) stays");

        // The search trigger narrows to its icon; the accessible name is still the localized label.
        var trigger = bar.Locator(".tm-topbar-search-trigger");
        Assert.AreEqual("none", await trigger.Locator(".tm-topbar-search-hint").EvaluateAsync<string>(
                "el => getComputedStyle(el).display"),
            "the Ctrl+K hint is a wide-bar luxury");
        Assert.IsFalse(string.IsNullOrWhiteSpace(await trigger.GetAttributeAsync("aria-label")),
            "the icon-only trigger keeps its accessible name");
        var triggerWidth = (await trigger.BoundingBoxAsync())!.Width;
        Assert.IsTrue(triggerWidth is >= 35 and <= 38, $"the trigger is a square icon button (was {triggerWidth}px)");

        // One row: the actions never wrap (the menu button and the account button share the row's
        // centre line — their heights differ by design, so compare centres, not tops).
        var rows = await bar.EvaluateAsync<double[]>(
            """
            root => {
                const menu = root.querySelector('.tm-topbar-menu-btn');
                const user = root.querySelector('.tm-topbar-user-button');
                if (!menu || !user) { return [-1, -2]; }
                const m = menu.getBoundingClientRect();
                const u = user.getBoundingClientRect();
                return [m.top + m.height / 2, u.top + u.height / 2];
            }
            """);
        Assert.AreEqual(rows[0], rows[1], 0.5, "a wrapped bar doubles its height — the actions must stay on one row");

        await ShotAsync(page, bar, "topbar-narrow-390-light");
    }

    [TestMethod]
    public async Task At_390_Dark_Mode_Keeps_The_Narrow_Bar_Readable()
    {
        var page = await OpenAsync(390);
        var bar = page.Locator(Demo).First;

        await ToggleDarkModeAsync(page);
        await page.WaitForTimeoutAsync(400);

        await AssertNoHorizontalOverflowAsync(page);
        Assert.AreEqual("none", await bar.Locator(".tm-topbar-user-name").EvaluateAsync<string>(
                "el => getComputedStyle(el).display"),
            "the narrow behaviour is theme-independent");

        var background = await bar.Locator(".tm-topbar").EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");
        StringAssert.StartsWith(background, "rgb(", "the bar paints a resolved token background in dark mode");

        await ShotAsync(page, bar, "topbar-narrow-390-dark");
    }

    // ── Wide and tablet: today's behaviour is untouched ────────────────────────

    [TestMethod]
    public async Task At_768_The_Name_And_Hint_Stay()
    {
        var page = await OpenAsync(768, 1024);
        var bar = page.Locator(Demo).First;

        await AssertNoHorizontalOverflowAsync(page);
        var name = bar.Locator(".tm-topbar-user-name");
        Assert.AreNotEqual("none", await name.EvaluateAsync<string>("el => getComputedStyle(el).display"));
        Assert.IsTrue((await name.BoundingBoxAsync())!.Width > 0, "the name is visible at 768px");
        Assert.AreNotEqual("none", await bar.Locator(".tm-topbar-search-hint").EvaluateAsync<string>(
                "el => getComputedStyle(el).display"),
            "the Ctrl+K hint shows from 480px up");

        await ShotAsync(page, bar, "topbar-narrow-768-light");
    }

    [TestMethod]
    public async Task At_1440_The_Bar_Is_Unchanged_In_Both_Themes()
    {
        var page = await OpenAsync(1440, 900);
        var bar = page.Locator(Demo).First;

        await AssertNoHorizontalOverflowAsync(page);
        Assert.AreNotEqual("none", await bar.Locator(".tm-topbar-user-name").EvaluateAsync<string>(
                "el => getComputedStyle(el).display"));
        Assert.AreNotEqual("none", await bar.Locator(".tm-topbar-search-hint").EvaluateAsync<string>(
                "el => getComputedStyle(el).display"));

        await ShotAsync(page, bar, "topbar-narrow-1440-light");

        await ToggleDarkModeAsync(page);
        await page.WaitForTimeoutAsync(400);
        await AssertNoHorizontalOverflowAsync(page);
        await ShotAsync(page, bar, "topbar-narrow-1440-dark");
    }

    // ── UserNameDisplay on the narrow showcase (384px boxes inside a wide page) ──

    [TestMethod]
    public async Task Showcase_Auto_Hides_The_Name_On_A_Narrow_Box_Whatever_The_Viewport()
    {
        var page = await OpenAsync(1280, 900);
        var box = page.Locator("[data-testid='topbar-narrow-auto'] .tm-topbar");

        Assert.AreEqual("none", await box.Locator(".tm-topbar-user-name").EvaluateAsync<string>(
                "el => getComputedStyle(el).display"),
            "the container query reads the bar's own 384px box, not the 1280px viewport");

        await ShotAsync(page, box, "topbar-showcase-auto");
    }

    [TestMethod]
    public async Task Showcase_Always_Keeps_The_Name_And_Ellipsises_It()
    {
        var page = await OpenAsync(1280, 900);
        var box = page.Locator("[data-testid='topbar-narrow-always'] .tm-topbar");

        var name = box.Locator(".tm-topbar-user-name");
        Assert.AreNotEqual("none", await name.EvaluateAsync<string>("el => getComputedStyle(el).display"),
            "Always keeps the name even under 480px");
        var ellipsis = await name.EvaluateAsync<double[]>("el => [el.clientWidth, el.scrollWidth]");
        Assert.IsTrue(ellipsis[1] > ellipsis[0], "the long name ellipsises instead of pushing the actions out");
        Assert.IsTrue(ellipsis[0] <= 193, $"the name caps at its 12rem budget (was {ellipsis[0]}px)");

        await AssertNoHorizontalOverflowAsync(page);
        await ShotAsync(page, box, "topbar-showcase-always");
    }

    [TestMethod]
    public async Task Showcase_Never_Renders_The_Avatar_Alone()
    {
        var page = await OpenAsync(1280, 900);
        var box = page.Locator("[data-testid='topbar-narrow-never'] .tm-topbar");

        Assert.AreEqual(0, await box.Locator(".tm-topbar-user-name").CountAsync(),
            "Never renders no name span at all (not a hidden one)");
        Assert.IsTrue(await box.Locator(".tm-topbar-user-button").IsVisibleAsync(), "the avatar stays");
        Assert.IsFalse(string.IsNullOrWhiteSpace(await box.Locator(".tm-topbar-user-button").GetAttributeAsync("aria-label")),
            "the avatar-only button still carries the localized accessible name");

        await ShotAsync(page, box, "topbar-showcase-never");
    }

    // ── Edge cases ─────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task At_320_The_Bar_Still_Fits()
    {
        var page = await OpenAsync(320, 640);

        await AssertNoHorizontalOverflowAsync(page);
        Assert.IsTrue(await page.Locator(Demo).First.Locator(".tm-topbar-user-button").IsVisibleAsync(),
            "the avatar stays reachable on the smallest phones");

        await ShotAsync(page, page.Locator(Demo).First, "topbar-narrow-320-light");
    }

    [TestMethod]
    public async Task At_390_With_Touch_The_Targets_Stay_Finger_Sized()
    {
        var page = await OpenAsync(390, 844, touch: true);
        var bar = page.Locator(Demo).First;

        await AssertNoHorizontalOverflowAsync(page);

        var boxes = await bar.EvaluateAsync<double[]>(
            """
            root => {
                const out = [];
                for (const sel of ['.tm-topbar-menu-btn', '.tm-topbar-user-button', '.tm-topbar-search-trigger']) {
                    const el = root.querySelector(sel);
                    if (!el) { out.push(-1, -1); continue; }
                    const box = el.getBoundingClientRect();
                    out.push(box.width, box.height);
                }
                return out;
            }
            """);
        foreach (var size in boxes)
        {
            Assert.IsTrue(size >= 43.5, $"a finger needs 44px targets in BOTH axes (was {size}px)");
        }

        await ShotAsync(page, bar, "topbar-narrow-390-touch");
    }

    [TestMethod]
    public async Task At_390_With_Reduced_Motion_The_Bar_Fits()
    {
        var page = await OpenAsync(390, 844, reducedMotion: true);

        await AssertNoHorizontalOverflowAsync(page);
        Assert.AreEqual("none", await page.Locator(Demo).First.Locator(".tm-topbar-user-name")
            .EvaluateAsync<string>("el => getComputedStyle(el).display"));

        await ShotAsync(page, page.Locator(Demo).First, "topbar-narrow-390-reduced-motion");
    }
}

/// <summary>The narrow-bar contract on the Blazor Server demo (:7107) — geometry and computed
/// styles only (the host culture differs, so no language-specific strings).</summary>
[TestClass]
[TestCategory("Server")]
public sealed class TmTopBarNarrowServerE2ETests : ServerTestBase
{
    [TestMethod]
    public async Task TopBar_Narrow_Width_Behaves_On_The_Server_Host()
        => await TmTopBarNarrowSmoke.RunAsync(BaseUrl, () => CreateContextAsync(), (page, url) => GotoAppReadyAsync(page, url));
}

/// <summary>The narrow-bar contract on the InteractiveAuto demo (:7108).</summary>
[TestClass]
[TestCategory("InteractiveAuto")]
public sealed class TmTopBarNarrowInteractiveAutoE2ETests : InteractiveAutoTestBase
{
    [TestMethod]
    public async Task TopBar_Narrow_Width_Behaves_On_The_InteractiveAuto_Host()
        => await TmTopBarNarrowSmoke.RunAsync(BaseUrl, () => CreateContextAsync(), (page, url) => GotoAppReadyAsync(page, url));
}

/// <summary>The host-independent smoke assertions both non-WASM lanes run.</summary>
internal static class TmTopBarNarrowSmoke
{
    private const string Demo = "[data-testid='topbar-demo']";

    public static async Task RunAsync(
        string baseUrl,
        Func<Task<IBrowserContext>> createContext,
        Func<IPage, string, Task> gotoReady)
    {
        var context = await createContext();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(390, 844);
        var errors = new List<string>();
        page.PageError += (_, message) => errors.Add(message);

        await gotoReady(page, $"{baseUrl}/layout");
        // The state attribute lives on the component root (.tm-topbar) — 'topbar-demo' is the demo
        // page's wrapper div around it.
        var bar = page.Locator("[data-testid='topbar-demo'] .tm-topbar").First;
        await bar.WaitForAsync(new LocatorWaitForOptions { Timeout = 90000 });
        await page.WaitForTimeoutAsync(400);

        // The root carries the state attribute and the narrow query fired on this host too.
        Assert.AreEqual("auto", await bar.GetAttributeAsync("data-user-name-display"),
            "every host renders the UserNameDisplay state attribute");
        Assert.AreEqual("none", await bar.Locator(".tm-topbar-user-name").EvaluateAsync<string>(
                "el => getComputedStyle(el).display"),
            "the Auto name hides below 480px on this host too");

        var metrics = await page.EvaluateAsync<double[]>(
            "() => [document.documentElement.scrollWidth, window.innerWidth]");
        Assert.IsTrue(metrics[0] <= metrics[1] + 1,
            $"the document overflows horizontally: {metrics[0]} > {metrics[1]}");

        Assert.AreEqual(0, errors.Count, "The page raised: " + string.Join(" | ", errors));
    }
}
