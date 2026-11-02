using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

[TestClass]
public class SigningDocumentCommentsE2ETests : WasmTestBase
{
    [TestMethod]
    [Description("Document comments can create a page-anchored thread with a mention from the viewer")]
    public async Task DocumentComments_CreateThreadWithMention()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{BaseUrl}/signing-components");
        await WaitForAppReadyAsync(page);

        var viewer = page.Locator("[data-testid='document-comments-viewer']").First;
        await viewer.ScrollIntoViewIfNeededAsync();
        await viewer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });

        await viewer.Locator(".tm-document-page-viewer__comment-toggle").ClickAsync();
        var pageElement = viewer.Locator(".tm-document-page-viewer__page").First;
        await pageElement.ScrollIntoViewIfNeededAsync();
        var box = await pageElement.BoundingBoxAsync();
        Assert.IsNotNull(box, "Document comment page should have a bounding box.");

        await page.Mouse.MoveAsync((float)(box!.X + box.Width * 0.18), (float)(box.Y + box.Height * 0.18));
        await page.Mouse.DownAsync();
        await page.Mouse.UpAsync();

        await Assertions.Expect(viewer.Locator("[data-testid='document-comment-draft']")).ToBeVisibleAsync();

        var composer = viewer.Locator("[data-testid='document-comment-draft'] .tm-comment-composer").First;
        await composer.Locator("textarea").FillAsync("Please check @Nor");
        await Assertions.Expect(composer.Locator(".tm-comment-composer__mention-option")).ToContainTextAsync("Nora Lee");
        await composer.Locator(".tm-comment-composer__mention-option").ClickAsync();
        await composer.Locator(".tm-comment-composer__button--primary").ClickAsync();

        await Assertions.Expect(page.Locator("[data-testid='document-comments-last-event']")).ToContainTextAsync("Created comment for Nora Lee");
        await Assertions.Expect(viewer.Locator(".tm-document-comments-layer__point")).ToHaveCountAsync(2);
        await Assertions.Expect(viewer.Locator("[data-testid='document-comments-panel']")).ToContainTextAsync("Please check");
    }

    [TestMethod]
    [Description("Document comment thread detail supports reply, resolve, reopen, and reactions")]
    public async Task DocumentComments_ThreadActionsWork()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{BaseUrl}/signing-components");
        await WaitForAppReadyAsync(page);

        var viewer = page.Locator("[data-testid='document-comments-viewer']").First;
        await viewer.ScrollIntoViewIfNeededAsync();
        await viewer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });

        var panel = viewer.Locator("[data-testid='document-comments-panel']").First;
        await Assertions.Expect(panel).ToContainTextAsync("Please confirm this amount");
        await Assertions.Expect(panel.Locator(".tm-document-comments-panel__detail")).ToBeVisibleAsync();

        var replyComposer = panel.Locator(".tm-document-comments-panel__detail .tm-comment-composer").Last;
        await replyComposer.Locator("textarea").FillAsync("Finance approved the amount.");
        await replyComposer.Locator(".tm-comment-composer__button--primary").ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid='document-comments-last-event']")).ToContainTextAsync("Reply added");
        await Assertions.Expect(panel).ToContainTextAsync("Finance approved the amount.");

        await panel.Locator(".tm-document-comments-panel__resolve").ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid='document-comments-last-event']")).ToContainTextAsync("Resolved comment-amount");
        await Assertions.Expect(panel.Locator(".tm-document-comments-panel__resolve")).ToContainTextAsync("Reopen");

        await panel.Locator(".tm-document-comments-panel__resolve").ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid='document-comments-last-event']")).ToContainTextAsync("Reopened comment-amount");

        await panel.Locator(".tm-document-comments-panel__reaction-add").First.ClickAsync();
        await panel.Locator(".tm-document-comments-panel__reaction-choice").First.ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid='document-comments-last-event']")).ToContainTextAsync("Reaction");
    }

    [TestMethod]
    [Description("Document comments support area drafts and Escape cancellation")]
    public async Task DocumentComments_AreaDraftAndEscapeWork()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{BaseUrl}/signing-components");
        await WaitForAppReadyAsync(page);

        var viewer = page.Locator("[data-testid='document-comments-viewer']").First;
        await viewer.ScrollIntoViewIfNeededAsync();
        await viewer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await viewer.Locator(".tm-document-page-viewer__comment-toggle").ClickAsync();

        var pageElement = viewer.Locator(".tm-document-page-viewer__page").First;
        await pageElement.ScrollIntoViewIfNeededAsync();
        var box = await pageElement.BoundingBoxAsync();
        Assert.IsNotNull(box, "Document comment page should have a bounding box.");

        await DragAreaAsync(page, box!.X, box.Y, box.Width, box.Height, 0.2, 0.52, 0.42, 0.62);
        await Assertions.Expect(viewer.Locator("[data-testid='document-comment-draft']")).ToBeVisibleAsync();
        await Assertions.Expect(viewer.Locator(".tm-document-comments-layer__draft")).ToBeVisibleAsync();

        await pageElement.FocusAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(viewer.Locator("[data-testid='document-comment-draft']")).ToHaveCountAsync(0);

        await pageElement.ScrollIntoViewIfNeededAsync();
        box = await pageElement.BoundingBoxAsync();
        Assert.IsNotNull(box, "Document comment page should have a bounding box after draft cancellation.");
        await DragAreaAsync(page, box.X, box.Y, box.Width, box.Height, 0.22, 0.54, 0.44, 0.64);
        var composer = viewer.Locator("[data-testid='document-comment-draft'] .tm-comment-composer").First;
        await composer.Locator("textarea").FillAsync("Area review from E2E");
        await composer.Locator(".tm-comment-composer__button--primary").ClickAsync();

        await Assertions.Expect(page.Locator("[data-testid='document-comments-last-event']")).ToContainTextAsync("Created comment: Area review from E2E");
        await Assertions.Expect(viewer.Locator(".tm-document-comments-layer__area")).ToHaveCountAsync(2);
    }

    [TestMethod]
    [Description("Document comments keep markers on the correct page and allow marker keyboard selection")]
    public async Task DocumentComments_PaginationAndMarkerKeyboardWork()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{BaseUrl}/signing-components");
        await WaitForAppReadyAsync(page);

        var viewer = await OpenCommentsViewerAsync(page);
        await Assertions.Expect(viewer.Locator(".tm-document-page-viewer__page-input")).ToHaveValueAsync("1 / 2");
        await Assertions.Expect(viewer.Locator("[data-thread-id='comment-page-two']")).ToHaveCountAsync(0);

        await viewer.Locator(".tm-document-page-viewer__next-page").ClickAsync();
        await Assertions.Expect(viewer.Locator(".tm-document-page-viewer__page-input")).ToHaveValueAsync("2 / 2");

        var pageTwoMarker = viewer.Locator("[data-thread-id='comment-page-two']").First;
        await Assertions.Expect(pageTwoMarker).ToBeVisibleAsync();

        await pageTwoMarker.FocusAsync();
        await page.Keyboard.PressAsync("Enter");

        var panel = viewer.Locator("[data-testid='document-comments-panel']").First;
        await Assertions.Expect(panel.Locator(".tm-document-comments-panel__detail")).ToHaveAttributeAsync("data-thread-id", "comment-page-two");
        await Assertions.Expect(panel).ToContainTextAsync("Second page note");
    }

    [TestMethod]
    [Description("Document comment draft submit stays disabled for empty text")]
    public async Task DocumentComments_EmptyDraftCannotSubmit()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{BaseUrl}/signing-components");
        await WaitForAppReadyAsync(page);

        var viewer = await OpenCommentsViewerAsync(page);
        await viewer.Locator(".tm-document-page-viewer__comment-toggle").ClickAsync();

        var pageElement = viewer.Locator(".tm-document-page-viewer__page").First;
        await pageElement.ScrollIntoViewIfNeededAsync();
        var box = await pageElement.BoundingBoxAsync();
        Assert.IsNotNull(box, "Document comment page should have a bounding box.");

        await page.Mouse.ClickAsync((float)(box!.X + box.Width * 0.18), (float)(box.Y + box.Height * 0.18));

        var composer = viewer.Locator("[data-testid='document-comment-draft'] .tm-comment-composer").First;
        await Assertions.Expect(composer).ToBeVisibleAsync();
        await Assertions.Expect(composer.Locator(".tm-comment-composer__button--primary")).ToBeDisabledAsync();

        await composer.Locator("textarea").FillAsync("   ");
        await Assertions.Expect(composer.Locator(".tm-comment-composer__button--primary")).ToBeDisabledAsync();
    }

    [TestMethod]
    [Description("Document comment markers remain inside the page after zoom changes")]
    public async Task DocumentComments_MarkersStayInsidePageAfterZoom()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{BaseUrl}/signing-components");
        await WaitForAppReadyAsync(page);

        var viewer = await OpenCommentsViewerAsync(page);
        var pageElement = viewer.Locator(".tm-document-page-viewer__page").First;
        var marker = viewer.Locator("[data-thread-id='comment-amount']").First;

        await AssertMarkerInsidePageAsync(pageElement, marker, "before zoom");

        await viewer.Locator(".tm-document-page-viewer__zoom-in").ClickAsync();
        await Assertions.Expect(viewer.Locator(".tm-document-page-viewer__zoom-label")).ToContainTextAsync("125%");
        await AssertMarkerInsidePageAsync(pageElement, marker, "after zoom in");

        await viewer.Locator(".tm-document-page-viewer__zoom-out").ClickAsync();
        await Assertions.Expect(viewer.Locator(".tm-document-page-viewer__zoom-label")).ToContainTextAsync("100%");
        await AssertMarkerInsidePageAsync(pageElement, marker, "after zoom out");
    }

    [TestMethod]
    [Description("Document comment reactions can be toggled on and off by the current user")]
    public async Task DocumentComments_ReactionToggleCanRemoveCurrentUser()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{BaseUrl}/signing-components");
        await WaitForAppReadyAsync(page);

        var viewer = await OpenCommentsViewerAsync(page);
        var panel = viewer.Locator("[data-testid='document-comments-panel']").First;

        await panel.Locator(".tm-document-comments-panel__reaction-add").First.ClickAsync();
        await panel.Locator(".tm-document-comments-panel__reaction-choice").Nth(2).ClickAsync();
        await Assertions.Expect(panel.Locator(".tm-document-comments-panel__reaction--active").First).ToContainTextAsync("👀");

        await panel.Locator(".tm-document-comments-panel__reaction--active").First.ClickAsync();
        await Assertions.Expect(panel.Locator(".tm-document-comments-panel__reaction--active")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("[data-testid='document-comments-last-event']")).ToContainTextAsync("Reaction 👀 toggled");
    }

    [TestMethod]
    [Description("Document comments keep the thread panel usable on mobile viewport")]
    public async Task DocumentComments_MobilePanelUsesStickySheet()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(390, 844);

        await page.GotoAsync($"{BaseUrl}/signing-components");
        await WaitForAppReadyAsync(page);

        var viewer = await OpenCommentsViewerAsync(page);
        await page.Locator("[data-testid='document-comments-layout'] select").SelectOptionAsync("Mobile");
        var sheet = viewer.Locator(".tm-document-page-viewer__comments-sheet .tm-drawer__panel").First;
        await Assertions.Expect(sheet).ToBeVisibleAsync();
        await Assertions.Expect(viewer.Locator(".tm-document-page-viewer__comments-sheet")).ToHaveAttributeAsync("data-snap-index", "0");

        var panel = viewer.Locator("[data-testid='document-comments-panel']").First;
        await Assertions.Expect(panel).ToBeVisibleAsync();
        await Assertions.Expect(panel).ToHaveAttributeAsync("aria-label", "Document comments");
        await Assertions.Expect(panel).ToContainTextAsync("Comments");
    }

    [TestMethod]
    [Description("Document comments stay usable across desktop, tablet, dark theme, and browser zoom smoke scenarios")]
    public async Task DocumentComments_ResponsiveThemeAndHighZoomSmoke()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1366, 768);

        await page.GotoAsync($"{BaseUrl}/signing-components");
        await WaitForAppReadyAsync(page);

        var viewer = await OpenCommentsViewerAsync(page);
        await Assertions.Expect(viewer.Locator("[data-testid='document-comments-panel']").First).ToBeVisibleAsync();
        await AssertMarkerInsidePageAsync(
            viewer.Locator(".tm-document-page-viewer__page").First,
            viewer.Locator("[data-thread-id='comment-amount']").First,
            "at 1366x768");

        await page.SetViewportSizeAsync(1024, 768);
        await page.WaitForTimeoutAsync(250);
        await Assertions.Expect(viewer.Locator("[data-testid='document-comments-panel']").First).ToBeVisibleAsync();
        await AssertMarkerInsidePageAsync(
            viewer.Locator(".tm-document-page-viewer__page").First,
            viewer.Locator("[data-thread-id='comment-amount']").First,
            "at 1024x768");

        await page.EvaluateAsync("document.documentElement.setAttribute('data-theme', 'dark')");
        var panelColor = await viewer.Locator("[data-testid='document-comments-panel']").First.EvaluateAsync<string>(
            "element => getComputedStyle(element).color");
        Assert.IsFalse(string.IsNullOrWhiteSpace(panelColor), "Comment panel should keep a computed text color in dark theme.");

        await page.EvaluateAsync("document.body.style.zoom = '1.25'");
        await page.WaitForTimeoutAsync(250);
        await Assertions.Expect(viewer.Locator("[data-testid='document-comments-panel']").First).ToBeVisibleAsync();
        await Assertions.Expect(viewer.Locator("[data-thread-id='comment-amount']").First).ToBeVisibleAsync();
    }

    private static async Task<ILocator> OpenCommentsViewerAsync(IPage page)
    {
        var viewer = page.Locator("[data-testid='document-comments-viewer']").First;
        await viewer.ScrollIntoViewIfNeededAsync();
        await viewer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        return viewer;
    }

    private static async Task AssertMarkerInsidePageAsync(ILocator pageElement, ILocator marker, string stage)
    {
        var pageBox = await pageElement.BoundingBoxAsync();
        var markerBox = await marker.BoundingBoxAsync();

        Assert.IsNotNull(pageBox, $"Document page should have a bounding box {stage}.");
        Assert.IsNotNull(markerBox, $"Comment marker should have a bounding box {stage}.");
        Assert.IsTrue(markerBox!.X >= pageBox!.X - 1, $"Marker should remain inside page horizontally {stage}.");
        Assert.IsTrue(markerBox.Y >= pageBox.Y - 1, $"Marker should remain inside page vertically {stage}.");
        Assert.IsTrue(markerBox.X + markerBox.Width <= pageBox.X + pageBox.Width + 1, $"Marker should remain inside page horizontally {stage}.");
        Assert.IsTrue(markerBox.Y + markerBox.Height <= pageBox.Y + pageBox.Height + 1, $"Marker should remain inside page vertically {stage}.");
    }

    private static async Task DragAreaAsync(
        IPage page,
        double boxX,
        double boxY,
        double boxWidth,
        double boxHeight,
        double startX,
        double startY,
        double endX,
        double endY)
    {
        await page.Mouse.MoveAsync((float)(boxX + boxWidth * startX), (float)(boxY + boxHeight * startY));
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)(boxX + boxWidth * endX), (float)(boxY + boxHeight * endY), new MouseMoveOptions { Steps = 4 });
        await page.Mouse.UpAsync();
    }
}
