using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

[TestClass]
public sealed class NotionPublicShareE2ETests : NotionE2ETestBase
{
    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("CF33: public share link opens in a separate anonymous context as read-only and revoke disables it.")]
    public async Task CF33_PublicShare_HappyPathAndBaseline()
    {
        var page = await OpenNotionEditorAsync();
        await SeedPublicSharePageAsync();
        await OpenShareDialogAsync(page);

        var dialog = page.GetByTestId("notion-share-dialog");
        await dialog.GetByTestId("notion-share-allow-comments").CheckAsync();
        await dialog.GetByTestId("notion-share-create").ClickAsync();
        var urlInput = dialog.GetByTestId("notion-share-url");
        await Assertions.Expect(urlInput).ToHaveValueAsync(new System.Text.RegularExpressions.Regex("/p/.+"));
        var publicUrl = await urlInput.InputValueAsync();

        var dialogCapture = await CaptureBaselineAsync("share", "cf33-share-dialog", page.Locator(".tm-npsd").First);
        TestContext.WriteLine($"UX CF33 share dialog baseline captured: {dialogCapture.FullPagePath} / {dialogCapture.RegionPath}");

        var publicContext = await CreateContextAsync();
        await publicContext.AddInitScriptAsync("window.localStorage.setItem('tm-demo-culture', 'en');");
        var publicPage = await publicContext.NewPageAsync();
        await publicPage.GotoAsync(publicUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60000
        });
        await WaitForAppReadyAsync(publicPage);
        await publicPage.GetByTestId("notion-public-page").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 60000 });
        await publicPage.WaitForSelectorAsync(".tm-notion-editor.tm-notion-editor--locked", new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 60000 });
        Assert.AreEqual(0, await publicPage.Locator(".tm-notion-sidebar").CountAsync(), "Public route must not expose the private workspace sidebar.");
        await Assertions.Expect(publicPage.Locator("[contenteditable='true']")).ToHaveCountAsync(0);
        await Assertions.Expect(publicPage.GetByTestId("notion-public-page")).ToContainTextAsync("CF33 Public Share Workspace");
        await Assertions.Expect(publicPage.GetByTestId("notion-public-page")).ToContainTextAsync("public read-only link");

        var publicCapture = await CaptureExternalPageBaselineAsync(publicPage, "share", "cf33-public-page", publicPage.GetByTestId("notion-public-page"));
        TestContext.WriteLine($"UX CF33 public page baseline captured: {publicCapture.FullPagePath} / {publicCapture.RegionPath}");

        await dialog.GetByTestId("notion-share-revoke").ClickAsync();
        await Assertions.Expect(dialog.GetByTestId("notion-share-disabled")).ToBeVisibleAsync();
        var revokedCapture = await CaptureBaselineAsync("share", "cf33-revoked-share-dialog", page.Locator(".tm-npsd").First);
        TestContext.WriteLine($"UX CF33 revoked dialog baseline captured: {revokedCapture.FullPagePath} / {revokedCapture.RegionPath}");

        await publicPage.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
        await WaitForAppReadyAsync(publicPage);
        await Assertions.Expect(publicPage.GetByTestId("notion-public-not-found")).ToBeVisibleAsync();

        TestContext.WriteLine("UX CF33 review: the dialog keeps link state, revoke, copy, comments, and expiry in one compact decision surface; the public page opens as a calm read-only document with the private sidebar removed.");
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("CF33: providerless entry point is hidden and expired tokens render an unavailable public page.")]
    public async Task CF33_PublicShare_EdgeCases_Baseline()
    {
        var providerless = await OpenNotionEditorAsync("?disablePublicShareProvider=true");
        await providerless.Locator(".tm-npsm-trigger").First.ClickAsync();
        var providerlessMenu = providerless.Locator(".tm-npsm").First;
        await providerlessMenu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        Assert.AreEqual(0, await providerless.GetByTestId("notion-share-open").CountAsync(), "Share entry point should be hidden when no public share provider is configured.");
        var providerlessCapture = await CaptureBaselineAsync("share", "cf33-providerless-settings-menu", providerlessMenu);
        TestContext.WriteLine($"UX CF33 providerless menu baseline captured: {providerlessCapture.FullPagePath} / {providerlessCapture.RegionPath}");

        var page = await OpenNotionEditorAsync();
        await SeedExpiredPublicSharePageAsync();

        var publicContext = await CreateContextAsync();
        await publicContext.AddInitScriptAsync("window.localStorage.setItem('tm-demo-culture', 'en');");
        var publicPage = await publicContext.NewPageAsync();
        await publicPage.GotoAsync($"{BaseUrl}/p/cf33-expired", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60000
        });
        await WaitForAppReadyAsync(publicPage);
        await Assertions.Expect(publicPage.GetByTestId("notion-public-not-found")).ToBeVisibleAsync();
        var expiredCapture = await CaptureExternalPageBaselineAsync(publicPage, "share", "cf33-expired-public-link", publicPage.GetByTestId("notion-public-not-found"));
        TestContext.WriteLine($"UX CF33 expired public link baseline captured: {expiredCapture.FullPagePath} / {expiredCapture.RegionPath}");

        await OpenShareDialogAsync(page);
        await Assertions.Expect(page.GetByTestId("notion-share-expired")).ToBeVisibleAsync();
        var expiredDialogCapture = await CaptureBaselineAsync("share", "cf33-expired-share-dialog", page.Locator(".tm-npsd").First);
        TestContext.WriteLine($"UX CF33 expired dialog baseline captured: {expiredDialogCapture.FullPagePath} / {expiredDialogCapture.RegionPath}");

        TestContext.WriteLine("UX CF33 edge review: providerless mode removes the share command from the private settings menu, and expired or revoked links resolve to the same unavailable public state without leaking private navigation.");
    }

    private static async Task OpenShareDialogAsync(IPage page)
    {
        await page.Locator(".tm-npsm-trigger").First.ClickAsync();
        await page.GetByTestId("notion-share-open").ClickAsync();
        await page.GetByTestId("notion-share-dialog").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
    }

    private async Task<NotionBaselineCapture> CaptureExternalPageBaselineAsync(IPage page, string area, string state, ILocator region)
    {
        var outputDir = BaselineOutput.DirectoryFor(TestContext, "notion", SanitizePathPart(area));
        Directory.CreateDirectory(outputDir);
        var safeState = SanitizePathPart(state);
        var fullPath = Path.Combine(outputDir, $"{safeState}.png");
        var regionPath = Path.Combine(outputDir, $"{safeState}.region.png");

        await page.WaitForTimeoutAsync(250);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = fullPath, Type = ScreenshotType.Png, FullPage = true });
        await region.ScreenshotAsync(new LocatorScreenshotOptions { Path = regionPath, Type = ScreenshotType.Png, OmitBackground = false });

        TestContext.AddResultFile(fullPath);
        TestContext.AddResultFile(regionPath);
        return new NotionBaselineCapture(fullPath, regionPath);
    }

    private static string SanitizePathPart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) || char.IsWhiteSpace(ch) ? '-' : char.ToLowerInvariant(ch)).ToArray();
        return new string(chars);
    }
}
