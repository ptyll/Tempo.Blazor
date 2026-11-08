using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan 8fa7319c, phase 3 — the <c>TmQRCode</c> cards on the real <c>/new-components</c> demo:
/// a named <c>role="img"</c> graphic, component-token colours whose quiet zone stays light in dark
/// mode (a scanner needs it), no inline style on the default path, and the JavaScript-free SVG
/// download. Diagnostic screenshots go to gitignored TestResults, never a committed baseline.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class TmQRCodeE2ETests : WasmTestBase
{
    private const string Route = "/new-components";
    private const string Section = "[data-testid='qr-code-section']";
    private const string DefaultCard = "[data-testid='qr-code-default']";
    private const string ThemeCard = "[data-testid='qr-code-theme']";
    private const string DownloadCard = "[data-testid='qr-code-download']";
    private const string LabelCard = "[data-testid='qr-code-label']";

    private string ShotDir
        => Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "qr-code");

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

    private async Task<IPage> OpenAsync(int width = 1440, int height = 1000)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}{Route}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        var section = page.Locator(Section).First;
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await section.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(400);
        return page;
    }

    private async Task ShotAsync(IPage page, string name)
    {
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync("() => document.activeElement instanceof HTMLElement && document.activeElement.blur()");
        await page.WaitForTimeoutAsync(250);
        await page.Locator(Section).First.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(ShotDir, $"{name}.png"),
        });
    }

    private static async Task<string?> ComputedFillAsync(IPage page, string selector)
        => await page.EvaluateAsync<string?>(
            "selector => { const el = document.querySelector(selector); return el ? getComputedStyle(el).fill : null; }",
            selector);

    [TestMethod]
    public async Task QrCode_Is_A_Named_Graphic_Without_Inline_Style()
    {
        var page = await OpenAsync();

        var svg = page.Locator($"{DefaultCard} .tm-qr-code svg");
        Assert.AreEqual("img", await svg.GetAttributeAsync("role"), "the graphic must expose role=\"img\"");
        Assert.AreEqual("QR code", await svg.GetAttributeAsync("aria-label"),
            "without AriaLabel the localized default names the graphic");
        // TextContent, not InnerText: <title> is an SVG element and Playwright's inner-text API only
        // accepts HTML elements.
        Assert.AreEqual("QR code", (await page.Locator($"{DefaultCard} .tm-qr-code svg title").TextContentAsync())?.Trim(),
            "the title carries the same name (native tooltip)");

        // The old markup painted the background with an inline style; a strict style-src blocks that.
        var inlineStyles = await page.Locator($"{DefaultCard} .tm-qr-code [style]").CountAsync();
        Assert.AreEqual(0, inlineStyles, "neither the SVG nor its children may carry an inline style");

        var labelled = page.Locator($"{LabelCard} .tm-qr-code svg");
        Assert.AreEqual("Scan to open the Tempo Blazor documentation", await labelled.GetAttributeAsync("aria-label"),
            "AriaLabel wins over the localized fallback");

        await ShotAsync(page, "qr-named");
    }

    [TestMethod]
    public async Task QrCode_Theme_Colors_Keep_The_Quiet_Zone_Light_In_Both_Themes()
    {
        var page = await OpenAsync();

        var surfaceSelector = $"{ThemeCard} .tm-qr-code .tm-qr-code__surface";
        var modulesSelector = $"{ThemeCard} .tm-qr-code .tm-qr-code__modules";

        Assert.AreEqual("rgb(255, 255, 255)", await ComputedFillAsync(page, surfaceSelector),
            "the themed quiet zone paints the paper token");
        Assert.AreEqual("rgb(31, 41, 55)", await ComputedFillAsync(page, modulesSelector),
            "the themed ink paints the paper-ink token");
        await ShotAsync(page, "qr-light");

        await ToggleDarkModeAsync(page);
        await page.WaitForTimeoutAsync(400);

        Assert.AreEqual("rgb(255, 255, 255)", await ComputedFillAsync(page, surfaceSelector),
            "dark mode must NOT flip the quiet zone — a dark surface makes the code unscannable");
        Assert.AreEqual("rgb(31, 41, 55)", await ComputedFillAsync(page, modulesSelector),
            "the ink is the theme-invariant paper ink");

        await ShotAsync(page, "qr-dark");
    }

    [TestMethod]
    public async Task QrCode_Download_Link_Carries_A_Standalone_Svg()
    {
        var page = await OpenAsync();

        var link = page.Locator($"{DownloadCard} .tm-qr-code__download");
        Assert.AreEqual(1, await link.CountAsync(), "the download action is opt-in and rendered when asked for");
        Assert.AreEqual("tempo-hub.svg", await link.GetAttributeAsync("download"));
        Assert.AreEqual("image/svg+xml", await link.GetAttributeAsync("type"));
        StringAssert.Contains((await link.InnerTextAsync()).Trim(), "Download",
            "the action carries a localized label");

        // The stylesheet really reaches the link: a scoped (non-::deep) rule would silently miss the
        // MarkupString SVG, so the check runs on the computed value, not on the class attribute.
        Assert.AreEqual("block", await link.EvaluateAsync<string>("el => getComputedStyle(el).display"),
            "the component stylesheet applies to the download action");

        var href = await link.GetAttributeAsync("href");
        Assert.IsNotNull(href);
        StringAssert.StartsWith(href, "data:image/svg+xml;base64,",
            "a data: URL needs no JavaScript and no Blob");

        // The exported file must stand on its own: absolute colours, never a page token or currentColor.
        var standalone = await link.EvaluateAsync<string>(
            "el => atob(el.getAttribute('href').split(',')[1])");
        StringAssert.Contains(standalone, "role=\"img\"");
        StringAssert.Contains(standalone, "xmlns=\"http://www.w3.org/2000/svg\"");
        StringAssert.Contains(standalone, "fill=\"#000000\"");
        StringAssert.Contains(standalone, "fill=\"#ffffff\"");
        Assert.IsFalse(standalone.Contains("currentColor", StringComparison.Ordinal),
            "currentColor would paint a dark quiet zone once the file is opened on its own");
        Assert.IsFalse(standalone.Contains("var(", StringComparison.Ordinal),
            "a page token cannot resolve inside a downloaded file");

        await ShotAsync(page, "qr-download");
    }
}

/// <summary>The same QR cards on the Blazor Server demo (:7107).</summary>
[TestClass]
[TestCategory("Server")]
public sealed class TmQRCodeServerE2ETests : ServerTestBase
{
    [TestMethod]
    public async Task QrCode_Renders_On_The_Server_Host()
        => await TmQRCodeSmoke.RunAsync(BaseUrl, () => CreateContextAsync(), (page, url) => GotoAppReadyAsync(page, url));
}

/// <summary>The same QR cards on the InteractiveAuto demo (:7108).</summary>
[TestClass]
[TestCategory("InteractiveAuto")]
public sealed class TmQRCodeInteractiveAutoE2ETests : InteractiveAutoTestBase
{
    [TestMethod]
    public async Task QrCode_Renders_On_The_InteractiveAuto_Host()
        => await TmQRCodeSmoke.RunAsync(BaseUrl, () => CreateContextAsync(), (page, url) => GotoAppReadyAsync(page, url));
}

/// <summary>The host-independent smoke assertions both non-WASM lanes run.</summary>
internal static class TmQRCodeSmoke
{
    private const string Section = "[data-testid='qr-code-section']";

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
        var section = page.Locator(Section);
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 90000 });
        await section.ScrollIntoViewIfNeededAsync();

        var svg = section.Locator("[data-testid='qr-code-default'] .tm-qr-code svg");
        Assert.AreEqual("img", await svg.GetAttributeAsync("role"));
        Assert.IsFalse(string.IsNullOrWhiteSpace(await svg.GetAttributeAsync("aria-label")),
            "every QR code renders a name, also on this host");

        Assert.AreEqual(1, await section.Locator("[data-testid='qr-code-download'] .tm-qr-code__download").CountAsync());
        Assert.AreEqual(0, await section.Locator("[data-testid='qr-code-default'] .tm-qr-code [style]").CountAsync(),
            "no inline style on the default path");

        Assert.AreEqual(0, errors.Count, "The page raised: " + string.Join(" | ", errors));
    }
}
