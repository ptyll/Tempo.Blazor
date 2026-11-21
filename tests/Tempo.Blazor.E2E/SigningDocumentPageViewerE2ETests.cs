using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

[TestClass]
public class SigningDocumentPageViewerE2ETests : WasmTestBase
{
    [TestMethod]
    [Description("Signing demo page renders document page image and overlay fields")]
    public async Task DocumentPageViewer_RendersPageImageAndOverlay()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await GotoAppReadyAsync(page, $"{BaseUrl}/signing-components");

        var viewer = page.Locator("[data-testid='signing-document-viewer']").First;
        await viewer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });

        var image = viewer.Locator("img.tm-document-page-viewer__image").First;
        Assert.IsTrue(await image.IsVisibleAsync(), "Document page image should be visible.");
        Assert.AreEqual("NDA page 1", await image.GetAttributeAsync("alt"));

        var pageElement = viewer.Locator(".tm-document-page-viewer__page").First;
        Assert.AreEqual("0", await pageElement.GetAttributeAsync("data-page-index"));

        var overlay = viewer.Locator("[data-testid='signature-overlay']").First;
        Assert.IsTrue(await overlay.IsVisibleAsync(), "Signature overlay should be visible.");

        var pageBox = await pageElement.BoundingBoxAsync();
        var overlayBox = await overlay.BoundingBoxAsync();

        Assert.IsNotNull(pageBox, "Document page should have a bounding box.");
        Assert.IsNotNull(overlayBox, "Signature overlay should have a bounding box.");
        Assert.IsTrue(overlayBox!.X >= pageBox!.X, "Overlay should start inside the page horizontally.");
        Assert.IsTrue(overlayBox.Y >= pageBox.Y, "Overlay should start inside the page vertically.");
        Assert.IsTrue(overlayBox.X + overlayBox.Width <= pageBox.X + pageBox.Width, "Overlay should end inside the page horizontally.");
        Assert.IsTrue(overlayBox.Y + overlayBox.Height <= pageBox.Y + pageBox.Height, "Overlay should end inside the page vertically.");

        await TakeScreenshotAsync(page, "signing_document_page_viewer_desktop");

        await page.SetViewportSizeAsync(390, 844);
        await page.WaitForTimeoutAsync(500);

        Assert.IsTrue(await viewer.IsVisibleAsync(), "Viewer should remain visible on mobile viewport.");
        Assert.IsTrue(await overlay.IsVisibleAsync(), "Signature overlay should remain visible on mobile viewport.");
        await TakeScreenshotAsync(page, "signing_document_page_viewer_mobile");
    }

    [TestMethod]
    [Description("Signing document page viewer zoom controls resize the page while keeping overlays inside it")]
    public async Task DocumentPageViewer_ZoomControlsResizePage()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await GotoAppReadyAsync(page, $"{BaseUrl}/signing-components");

        var viewer = page.Locator("[data-testid='signing-document-viewer']").First;
        await viewer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });

        var pageElement = viewer.Locator(".tm-document-page-viewer__page").First;
        var before = await pageElement.BoundingBoxAsync();
        Assert.IsNotNull(before);

        await viewer.Locator(".tm-document-page-viewer__zoom-in").ClickAsync();
        await Assertions.Expect(viewer.Locator(".tm-document-page-viewer__zoom-label")).ToContainTextAsync("125%");
        var zoomed = await pageElement.BoundingBoxAsync();
        Assert.IsNotNull(zoomed);
        Assert.IsTrue(zoomed!.Width > before!.Width, "Zoom in should increase the visual page width.");

        await viewer.Locator(".tm-document-page-viewer__fit-page").ClickAsync();
        await Assertions.Expect(viewer.Locator(".tm-document-page-viewer__zoom-label")).ToContainTextAsync("85%");
        var fit = await pageElement.BoundingBoxAsync();
        Assert.IsNotNull(fit);
        Assert.IsTrue(fit!.Width < zoomed.Width, "Fit page should reduce the page after zoom in.");

        var overlay = viewer.Locator("[data-testid='signature-overlay']").First;
        var overlayBox = await overlay.BoundingBoxAsync();
        Assert.IsNotNull(overlayBox);
        Assert.IsTrue(overlayBox!.X >= fit.X, "Overlay should remain inside the fit page horizontally.");
        Assert.IsTrue(overlayBox.X + overlayBox.Width <= fit.X + fit.Width + 1, "Overlay should remain inside the fit page horizontally.");
    }
}
