using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

[TestClass]
public sealed class NotionWatchNotificationsE2ETests : NotionE2ETestBase
{
    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("CF21: watched page receives a page-edit notification from a second browser context.")]
    public async Task CF21_WatchPage_ReceivesNotificationFromSecondContext()
    {
        var watcher = await OpenNotionEditorAsync("?user=demo");
        await SeedPageInfoPageAsync();

        var watchToggle = watcher.GetByTestId("notion-watch-toggle");
        await watchToggle.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await watchToggle.ClickAsync();
        await Assertions.Expect(watchToggle).ToContainTextAsync("Unwatch");

        var watchCapture = await CaptureBaselineAsync("watch", "cf21-watch-button", watcher.GetByTestId("notion-watch-button"));
        TestContext.WriteLine($"UX CF21 watch button baseline captured: {watchCapture.FullPagePath} / {watchCapture.RegionPath}");

        var editor = await OpenSecondEditorAsync("?user=alice");
        var remoteBlock = editor.Locator("[data-block-id='cf160000-0000-0000-0000-000000000002'] .tm-notion-editable").First;
        await remoteBlock.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 60000 });
        await ReplaceEditableTextAsync(editor, remoteBlock, "CF21 remote page edit notification");

        var badge = watcher.GetByTestId("notion-notification-badge");
        await Assertions.Expect(badge).ToHaveTextAsync("1", new LocatorAssertionsToHaveTextOptions { Timeout = 15000 });

        await watcher.GetByTestId("notion-notification-toggle").ClickAsync();
        var panel = watcher.GetByTestId("notion-notification-panel");
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await Assertions.Expect(panel).ToContainTextAsync("edited");

        var panelCapture = await CaptureBaselineAsync("watch", "cf21-notification-center", panel);
        TestContext.WriteLine($"UX CF21 notification center baseline captured: {panelCapture.FullPagePath} / {panelCapture.RegionPath}");

        await CaptureBaselineAsync("watch", "cf21-notification-unread-state", panel);

        await watcher.GetByTestId("notion-notification-mark-all").ClickAsync();
        await Assertions.Expect(watcher.GetByTestId("notion-notification-badge")).ToHaveCountAsync(0);
        await Assertions.Expect(panel.Locator(".tm-notification-bell__item--unread")).ToHaveCountAsync(0);
        await CaptureBaselineAsync("watch", "cf21-notification-read-state", panel);

        TestContext.WriteLine("UX CF21 review: the watch control stays compact in the editor topbar, while the notification panel keeps unread/read state, message context, and direct navigation in one scan-friendly surface.");
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("CF21: providerless UI is hidden; include-children and mark-all-read edge flows work.")]
    public async Task CF21_WatchNotifications_EdgeCases()
    {
        var providerless = await OpenNotionEditorAsync("?disableWatchProvider=true&user=demo");
        Assert.AreEqual(0, await providerless.GetByTestId("notion-watch-button").CountAsync(), "Watch button should be hidden when no watch provider is configured.");
        await CaptureBaselineAsync("watch", "cf21-providerless-hidden-state", providerless.Locator(".tm-notion-topbar").First);

        var page = await OpenNotionEditorAsync("?user=demo");
        await SeedPageInfoPageAsync();

        await page.GetByTestId("notion-notification-toggle").ClickAsync();
        await Assertions.Expect(page.GetByTestId("notion-notification-empty")).ToBeVisibleAsync();
        await CaptureBaselineAsync("watch", "cf21-notification-empty-state", page.GetByTestId("notion-notification-panel"));
        await page.GetByTestId("notion-notification-toggle").ClickAsync();

        await page.GetByTestId("notion-watch-toggle").ClickAsync();
        var includeChildren = page.GetByTestId("notion-watch-include-children");
        await includeChildren.CheckAsync();
        await Assertions.Expect(includeChildren).ToBeCheckedAsync();

        var editor = await OpenSecondEditorAsync("?user=alice");
        var remoteBlock = editor.Locator("[data-block-id='cf160000-0000-0000-0000-000000000002'] .tm-notion-editable").First;
        await ReplaceEditableTextAsync(editor, remoteBlock, "CF21 mark all read edge edit");

        await Assertions.Expect(page.GetByTestId("notion-notification-badge")).ToHaveTextAsync("1", new LocatorAssertionsToHaveTextOptions { Timeout = 15000 });
        await page.GetByTestId("notion-notification-toggle").ClickAsync();
        await page.GetByTestId("notion-notification-mark-all").ClickAsync();
        await Assertions.Expect(page.GetByTestId("notion-notification-badge")).ToHaveCountAsync(0);

        await page.GetByTestId("notion-watch-toggle").ClickAsync();
        await Assertions.Expect(page.GetByTestId("notion-watch-toggle")).ToContainTextAsync("Watch");
    }

    private async Task<IPage> OpenSecondEditorAsync(string query)
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("window.localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1280, 720);
        await GotoAppReadyAsync(page, $"{BaseUrl}/notion-editor{query}", WaitUntilState.DOMContentLoaded);
        await page.WaitForSelectorAsync(".tm-notion-page", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
        return page;
    }

    private static async Task ReplaceEditableTextAsync(IPage page, ILocator editable, string text)
    {
        await editable.ClickAsync();
        await page.Keyboard.PressAsync(OperatingSystem.IsMacOS() ? "Meta+A" : "Control+A");
        await page.Keyboard.TypeAsync(text);
        await page.Locator(".tm-notion-topbar").ClickAsync(new LocatorClickOptions { Position = new Position { X = 12, Y = 12 } });
    }
}
