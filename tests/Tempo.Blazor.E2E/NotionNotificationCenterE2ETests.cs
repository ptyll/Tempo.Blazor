using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E coverage for N325 (Tempo 2.9.1): <c>.tm-notion-notifications__panel</c> must actually
/// apply its declared width. The rule used to live in
/// TmNotionNotificationCenter.razor.css — scoped — but the panel element is rendered by
/// TmOverlayPanel and carries that component's scope attribute, so the rule never matched and
/// the width silently fell back to the global 360px bell dropdown. It now lives in the
/// NotionEditor global bundle.
/// </summary>
[TestClass]
public class NotionNotificationCenterE2ETests : WasmTestBase
{
    private string PageUrl => $"{BaseUrl}/notion-editor";

    [TestMethod]
    public async Task NotionNotificationCenter_Panel_UsesDeclaredWidth()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 900);
        await page.GotoAsync(PageUrl);
        await WaitForAppReadyAsync(page);

        var toggle = page.Locator("[data-testid='notion-notification-toggle']").First;
        await toggle.ClickAsync();

        var panel = page.Locator(".tm-notion-notifications__panel.tm-overlay-panel--open");
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // 1440px: min(24rem, 100vw − 1.5rem) = 384px. The dead scoped rule used to leave the
        // global bell-dropdown's 360px.
        var wideWidth = await panel.EvaluateAsync<float>("el => el.getBoundingClientRect().width");
        Assert.IsTrue(Math.Abs(wideWidth - 384f) < 1.5f,
            $"at 1440px the panel must be 24rem=384px wide (got {wideWidth}px)");

        // 390px: the min() branch flips to 100vw − 1.5rem = 366px.
        await page.SetViewportSizeAsync(390, 800);
        var narrowWidth = await panel.EvaluateAsync<float>("el => el.getBoundingClientRect().width");
        Assert.IsTrue(Math.Abs(narrowWidth - 366f) < 1.5f,
            $"at 390px the panel must clamp to viewport − 24px = 366px (got {narrowWidth}px)");
    }

    [TestMethod]
    public async Task NotionNotificationCenter_Panel_ExposesAccessibleName()
    {
        // N326: a Role="dialog" overlay must carry an accessible name — here aria-labelledby
        // pointing at the visible title rendered inside the open panel.
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 900);
        await page.GotoAsync(PageUrl);
        await WaitForAppReadyAsync(page);

        var toggle = page.Locator("[data-testid='notion-notification-toggle']").First;
        await toggle.ClickAsync();

        var panel = page.Locator(".tm-notion-notifications__panel.tm-overlay-panel--open");
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        Assert.AreEqual("dialog", await panel.GetAttributeAsync("role"));

        var labelledBy = await panel.GetAttributeAsync("aria-labelledby");
        Assert.IsFalse(string.IsNullOrWhiteSpace(labelledBy),
            "the dialog panel must be named by its visible title via aria-labelledby");

        var title = panel.Locator($"#{labelledBy}");
        Assert.AreEqual(1, await title.CountAsync(),
            $"aria-labelledby must resolve to an element inside the open panel (id '{labelledBy}')");
        Assert.IsFalse(string.IsNullOrWhiteSpace(await title.First.TextContentAsync()),
            "the naming element must carry non-empty text");
    }
}
