using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.RegularExpressions;

namespace Tempo.Blazor.E2E;

[TestClass]
public class SigningFieldOverlayE2ETests : WasmTestBase
{
    [TestMethod]
    [Description("Signing field overlay is positioned using normalized document coordinates")]
    public async Task FieldOverlay_IsPositionedRelativeToDocumentPage()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await GotoAppReadyAsync(page, $"{BaseUrl}/signing-components");

        var pageElement = page.Locator("[data-testid='signing-document-viewer'] .tm-document-page-viewer__page").First;
        var overlay = page.Locator("[data-testid='signature-overlay']").First;
        await overlay.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });

        var pageBox = await pageElement.BoundingBoxAsync();
        var overlayBox = await overlay.BoundingBoxAsync();

        Assert.IsNotNull(pageBox, "Document page should have a bounding box.");
        Assert.IsNotNull(overlayBox, "Overlay should have a bounding box.");

        var expectedX = pageBox!.X + pageBox.Width * 0.52;
        var expectedY = pageBox.Y + pageBox.Height * 0.67;
        var toleranceX = pageBox.Width * 0.03;
        var toleranceY = pageBox.Height * 0.03;

        Assert.IsTrue(Math.Abs(overlayBox!.X - expectedX) <= toleranceX, "Overlay X should follow the normalized area.");
        Assert.IsTrue(Math.Abs(overlayBox.Y - expectedY) <= toleranceY, "Overlay Y should follow the normalized area.");
        Assert.IsTrue(overlayBox.X + overlayBox.Width <= pageBox.X + pageBox.Width, "Overlay should stay inside the page horizontally.");
        Assert.IsTrue(overlayBox.Y + overlayBox.Height <= pageBox.Y + pageBox.Height, "Overlay should stay inside the page vertically.");
    }

    [TestMethod]
    [Description("Clicking a signing field overlay selects the field")]
    public async Task FieldOverlay_Click_SelectsField()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await GotoAppReadyAsync(page, $"{BaseUrl}/signing-components");

        var overlay = page.Locator("[data-testid='signature-overlay']").First;
        await overlay.ClickAsync();

        await Expect(overlay).ToHaveClassAsync(new Regex("tm-signing-field--selected"));
        await Expect(page.Locator("[data-testid='signing-page-event']")).ToContainTextAsync("Clicked Signature");
    }

    [TestMethod]
    [Description("Context menu on a signing field invokes the component callback without opening the browser menu")]
    public async Task FieldOverlay_ContextMenu_InvokesComponentCallback()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await GotoAppReadyAsync(page, $"{BaseUrl}/signing-components");

        var overlay = page.Locator("[data-testid='signature-overlay']").First;
        await overlay.ClickAsync(new LocatorClickOptions { Button = MouseButton.Right });

        await Expect(page.Locator("[data-testid='signing-page-event']")).ToContainTextAsync("Context menu Signature");
    }

    [TestMethod]
    [Description("Signing field overlay remains legible on a mobile viewport")]
    public async Task FieldOverlay_MobileViewport_KeepsLabelReadable()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(390, 844);

        await GotoAppReadyAsync(page, $"{BaseUrl}/signing-components");

        var overlay = page.Locator("[data-testid='signature-overlay']").First;
        await overlay.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });

        var label = overlay.Locator(".tm-signing-field__label").First;
        var box = await overlay.BoundingBoxAsync();

        Assert.IsNotNull(box, "Overlay should have a bounding box on mobile.");
        Assert.IsTrue(box!.Width > 40, "Overlay should be wide enough to read on mobile.");
        Assert.IsTrue(box.Height > 20, "Overlay should be tall enough to read on mobile.");
        Assert.IsTrue(await label.IsVisibleAsync(), "Overlay label should remain visible.");
        Assert.AreEqual("Signature", (await label.TextContentAsync())?.Trim());
    }

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
