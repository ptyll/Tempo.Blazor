using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E coverage for CF18-7: the Notion AI menu, mention menu, and comment-mention dropdown are
/// TmOverlayPanel popovers. Opened inside a TmModal they must render in the browser top layer —
/// a hit-test at the menu centre must land inside the menu, not on the modal overlay.
/// </summary>
[TestClass]
public class NotionOverlayInModalE2ETests : WasmTestBase
{
    private string PageUrl => $"{BaseUrl}/notion-editor";

    private async Task<IPage> OpenPageInModalAsync(IPage page)
    {
        await page.GotoAsync(PageUrl);
        await WaitForAppReadyAsync(page);

        await page.GetByTestId("overlay-modal-open").ClickAsync();
        await page.Locator(".tm-modal").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
        });
        return page;
    }

    /// <summary>
    /// Hit-test at the element's visual centre must resolve inside the element — proves the
    /// surface paints above the modal overlay instead of under it.
    /// </summary>
    private static async Task AssertTopmostAtCentreAsync(ILocator panel)
    {
        Assert.IsTrue(await panel.EvaluateAsync<bool>("el => el.matches(':popover-open')"));

        var isTopmost = await panel.EvaluateAsync<bool>(
            @"el => {
                const r = el.getBoundingClientRect();
                const hit = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
                return hit !== null && (hit === el || el.contains(hit));
            }");
        Assert.IsTrue(isTopmost, "elementFromPoint at the panel centre should land inside the panel.");
    }

    [TestMethod]
    public async Task AiMenu_InsideTmModal_IsTopmost()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await OpenPageInModalAsync(page);

        await page.GetByTestId("overlay-modal-open-ai").ClickAsync();
        var panel = page.Locator(".tm-notion-ai");
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await AssertTopmostAtCentreAsync(panel);
    }

    [TestMethod]
    public async Task MentionMenu_InsideTmModal_IsTopmost()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await OpenPageInModalAsync(page);

        await page.GetByTestId("overlay-modal-open-mention").ClickAsync();
        var panel = page.Locator(".tm-nmm");
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await AssertTopmostAtCentreAsync(panel);
    }

    [TestMethod]
    public async Task CommentMentionDropdown_InsideTmModal_IsTopmost()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await OpenPageInModalAsync(page);

        await page.Locator(".tm-comment-mention-input textarea").FillAsync("@a");
        var panel = page.Locator(".tm-comment-mention-dropdown");
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await AssertTopmostAtCentreAsync(panel);
    }

    /// <summary>
    /// Firefox legs: the Popover API ships in Firefox ≥125, so the identical top-layer contract
    /// must hold there. PlaywrightTestBase runs Chromium only, so these tests own a dedicated
    /// Playwright + Firefox pair.
    /// </summary>
    [TestMethod]
    public async Task AiMenu_Firefox_InsideTmModal_IsTopmost()
        => await RunFirefoxLegAsync("overlay-modal-open-ai", ".tm-notion-ai");

    [TestMethod]
    public async Task MentionMenu_Firefox_InsideTmModal_IsTopmost()
        => await RunFirefoxLegAsync("overlay-modal-open-mention", ".tm-nmm");

    private async Task RunFirefoxLegAsync(string triggerTestId, string panelSelector)
    {
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Firefox.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = !TestContext.Properties.Contains("Headless") || TestContext.Properties["Headless"]?.ToString() != "false",
        });
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1280, Height = 720 },
            IgnoreHTTPSErrors = true,
        });
        try
        {
            var page = await context.NewPageAsync();
            await OpenPageInModalAsync(page);

            await page.GetByTestId(triggerTestId).ClickAsync();
            var panel = page.Locator(panelSelector);
            await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

            await AssertTopmostAtCentreAsync(panel);
        }
        finally
        {
            await context.CloseAsync();
        }
    }
}
