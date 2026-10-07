using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E.Feedback;

/// <summary>
/// The review-round sheet, focus and mobile-presentation cases, against the real demo components.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class SheetReviewE2ETests : WasmTestBase
{
    // The self-hosted probe checks 7106. An external host answers on the URL the round-2 tests use.
    protected override string BaseUrl =>
        Environment.GetEnvironmentVariable("TM_E2E_WASM_URL") ?? base.BaseUrl;

    private async Task<IPage> OpenAsync(string route, int width, int height, bool touch)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            HasTouch = touch,
            ViewportSize = new ViewportSize { Width = width, Height = height },
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}{route}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        return page;
    }

    [TestMethod]
    public async Task ModalSheet_AnchorsAndKeepsFooterWhileScrolling()
    {
        var page = await OpenAsync("/modal-dialog", 390, 844, touch: true);
        await page.GetByTestId("open-sheet").ClickAsync();
        var panel = page.Locator(".tm-modal--sheet .tm-modal");
        await panel.WaitForAsync();
        await panel.EvaluateAsync("el => Promise.all(el.getAnimations().map(a => a.finished)).catch(() => {})");

        var box = await panel.BoundingBoxAsync();
        Assert.IsNotNull(box);
        Assert.AreEqual(844, box.Y + box.Height, 2, "the sheet bottom is the viewport bottom");
        await ShotAsync(page, panel, "modal-sheet-390");
    }

    [TestMethod]
    public async Task NonModalSheet_LeavesTheCanvasReachable()
    {
        var page = await OpenAsync("/modal-dialog", 390, 844, touch: true);
        await page.GetByTestId("open-inline-drawer").ClickAsync();
        await page.Locator(".tm-drawer--inline").WaitForAsync();
        var reached = await page.EvaluateAsync<bool>(
            "() => { const canvas = document.getElementById('sheet-canvas'); canvas.focus(); return document.activeElement === canvas; }");
        Assert.IsTrue(reached, "a non-modal sheet does not trap Tab or steal focus from the canvas");
    }

    [TestMethod]
    public async Task Dashboard_CreateAndDelete_At390()
    {
        var page = await OpenAsync("/dashboard", 390, 844, touch: true);
        await page.Locator(".tm-dashboard-views button").First.ClickAsync();
        await page.Locator(".tm-dashboard-menu-item--action").ClickAsync();
        var sheet = page.Locator(".tm-modal--sheet");
        await sheet.WaitForAsync();
        await ShotAsync(page, sheet, "dashboard-create-390");
        await page.Keyboard.PressAsync("Escape");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });

        await page.Locator(".tm-dashboard-views button").First.ClickAsync();
        await page.Locator(".tm-dashboard-menu-action--danger").First.ClickAsync();
        await page.Locator(".tm-dialog").WaitForAsync();
        await ShotAsync(page, page.Locator(".tm-modal-overlay"), "dashboard-delete-390");
    }

    [TestMethod]
    public async Task ViewManager_PresentsAt390And1440()
    {
        var phone = await OpenAsync("/data-table", 390, 844, touch: true);
        await phone.Locator(".tm-view-manager-toggle").First.ClickAsync();
        await phone.Locator(".tm-view-manager-panel .tm-btn-primary").ClickAsync();
        await phone.Locator(".tm-modal--sheet").WaitForAsync();
        await ShotAsync(phone, phone.Locator(".tm-modal--sheet"), "view-manager-390");

        var desk = await OpenAsync("/data-table", 1440, 900, touch: false);
        await desk.Locator(".tm-view-manager-toggle").First.ClickAsync();
        await desk.Locator(".tm-view-manager-panel .tm-btn-primary").ClickAsync();
        await desk.Locator(".tm-modal").WaitForAsync();
        var layout = await desk.Locator(".tm-modal-overlay").GetAttributeAsync("data-layout");
        Assert.AreEqual("desktop", layout);
        await ShotAsync(desk, desk.Locator(".tm-modal-overlay"), "view-manager-1440");
    }

    private static async Task ShotAsync(IPage page, ILocator target, string name)
    {
        var dir = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E", "TestResults", "sheet-review");
        Directory.CreateDirectory(dir);
        await target.ScreenshotAsync(new LocatorScreenshotOptions { Path = Path.Combine(dir, $"{name}.png") });
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TempoBlazor.slnx")))
            dir = dir.Parent;
        return dir ?? throw new InvalidOperationException("repo root not found");
    }
}
