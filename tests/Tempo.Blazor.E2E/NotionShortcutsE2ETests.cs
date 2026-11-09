using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

[TestClass]
public sealed class NotionShortcutsE2ETests : NotionE2ETestBase
{
    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("CF18: Keyboard shortcuts panel opens with ?, closes with Escape, ignores ? typed inside editable blocks, and captures a UX baseline.")]
    public async Task ShortcutsPanel_OpenCloseAndTypingEdge()
    {
        var page = await OpenNotionEditorAsync();
        await SeedRichPageAsync();

        Assert.IsTrue(await page.EvaluateAsync<bool>("() => typeof window.tmNotionEditor?.registerShortcuts === 'function'"),
            "The Notion shortcuts JS registration function must be available.");
        // Phase 3: the topbar is a single TmToolbar with Overflow=Menu. The More button only
        // renders when the bar actually overflows — at 1600 the shortcuts trigger sits directly
        // on the bar. Exercise the More menu only when it exists.
        var more = page.Locator(".tm-notion-topbar .tm-toolbar-more");
        var hasMore = await more.CountAsync() > 0;
        if (hasMore)
            await more.First.ClickAsync(new LocatorClickOptions { Force = true });
        Assert.AreEqual(1, await page.Locator("[data-testid='notion-shortcuts-open'], [data-menu-testid='notion-shortcuts-open']").CountAsync(),
            "The shortcuts trigger should render on the bar or in its More menu.");
        if (hasMore)
            await page.Keyboard.PressAsync("Escape");

        await page.Locator(".tm-notion-topbar__title").First.ClickAsync();
        await DispatchQuestionShortcutAsync(page);

        var panel = page.Locator(".tm-nsp").First;
        await panel.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
        await panel.Locator("[data-shortcut-action='OpenShortcuts']").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
        await panel.Locator("[data-shortcut-action='SlashMenu']").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
        Assert.IsTrue(await panel.EvaluateAsync<bool>("el => el.scrollWidth <= el.clientWidth + 1"), "Shortcuts panel should not overflow horizontally.");

        var capture = await CaptureBaselineAsync("shortcuts", "cf18-shortcuts-panel-baseline", panel);
        TestContext.WriteLine($"UX CF18 baseline captured: {capture.FullPagePath} / {capture.RegionPath}");

        await page.SetViewportSizeAsync(390, 844);
        Assert.IsTrue(await panel.EvaluateAsync<bool>("el => el.scrollWidth <= el.clientWidth + 1"), "Mobile shortcuts panel should not overflow horizontally.");
        await CaptureBaselineAsync("shortcuts", "cf18-shortcuts-panel-mobile", panel);
        await page.SetViewportSizeAsync(1280, 720);

        await page.Keyboard.PressAsync("Escape");
        await page.Locator(".tm-nsp").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Detached,
            Timeout = 10000
        });

        await DispatchQuestionShortcutAsync(page);
        await page.Locator(".tm-nsp").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
        // Phase 6: the panel rides TmModal, so the overlay click is the close path the old scrim was.
        await page.Locator(".tm-modal-overlay").Last.ClickAsync(new LocatorClickOptions
        {
            Position = new Position { X = 8, Y = 8 }
        });
        await page.Locator(".tm-nsp").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Detached,
            Timeout = 10000
        });

        var editable = page.Locator(".tm-notion-editable").First;
        await editable.ClickAsync();
        await editable.EvaluateAsync("""
            el => el.dispatchEvent(new KeyboardEvent('keydown', {
                key: '?',
                bubbles: true,
                cancelable: true
            }))
            """);
        await page.WaitForTimeoutAsync(500);

        Assert.AreEqual(0, await page.Locator(".tm-nsp").CountAsync(),
            "Typing ? inside a Notion editable block must not open the shortcuts panel.");
    }

    private static async Task DispatchQuestionShortcutAsync(IPage page)
        => await page.EvaluateAsync("""
            () => document.dispatchEvent(new KeyboardEvent('keydown', {
                key: '?',
                bubbles: true,
                cancelable: true
            }))
            """);
}
