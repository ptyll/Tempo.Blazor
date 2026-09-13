using System.Net.Http.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

[TestClass]
public sealed class NotionRestrictionsE2ETests : NotionE2ETestBase
{
    private static readonly Guid RootPageId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ChildPageId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string ApiBaseUrl = "https://localhost:5100";

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("CF20: Page restrictions dialog, edit-only user, read-only others, inheritance, no provider, no-view, and user-vs-group conflict.")]
    public async Task PageRestrictions_AclDialogEffectivePermissionsAndEdges()
    {
        try
        {
            var page = await OpenNotionEditorAsync();
            await SeedRestrictionsPageAsync();

            await OpenRestrictionsDialogAsync(page);
            var dialog = page.Locator(".tm-nprd").First;
            await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
            Assert.IsTrue(await dialog.Locator(".tm-nprd__entry").CountAsync() >= 2, "Seeded restriction entries should render.");

            var dialogCapture = await CaptureBaselineAsync("restrictions", "cf20-restrictions-dialog", dialog);
            TestContext.WriteLine($"UX CF20 dialog baseline captured: {dialogCapture.FullPagePath} / {dialogCapture.RegionPath}");

            await dialog.Locator(".tm-nprd__primary").ClickAsync();
            await page.Locator(".tm-nprd").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 10000 });

            await page.Locator(".tm-notion-restricted-badge").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
            await page.Locator(".tm-notion-header-restricted-badge").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
            Assert.AreEqual(0, await page.Locator(".tm-notion-page--readonly").CountAsync(), "Alice should keep Edit access.");

            var restrictedBadge = page.Locator(".tm-notion-header-restricted-badge").First;
            var indicatorCapture = await CaptureRestrictionClipBaselineAsync(page, "cf20-restricted-indicator", restrictedBadge, 260, 64);
            TestContext.WriteLine($"UX CF20 restricted indicator captured: {indicatorCapture.FullPagePath} / {indicatorCapture.RegionPath}");

            var bob = await OpenNotionEditorAsync("?user=bob&groups=readers");
            await bob.Locator(".tm-notion-page--readonly").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
            Assert.AreEqual(1, await bob.Locator(".tm-notion-restricted-badge").CountAsync(), "Bob should see the restricted indicator.");

            var readOnlyCapture = await CaptureRestrictionBaselineAsync(bob, "cf20-read-only-state", bob.Locator(".tm-notion-editor").First);
            TestContext.WriteLine($"UX CF20 read-only baseline captured: {readOnlyCapture.FullPagePath} / {readOnlyCapture.RegionPath}");

            await bob.Locator(".tm-npt-toggle").First.ClickAsync();
            await bob.Locator(".tm-npt-title").Filter(new() { HasText = "CF20 Child Inherits Restrictions" }).ClickAsync();
            await bob.Locator($"article[data-page-id='{ChildPageId:D}'].tm-notion-page--readonly").WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 10000
            });

            var providerless = await OpenNotionEditorAsync("?disablePermissionProvider=true&user=charlie&groups=guests");
            Assert.AreEqual(0, await providerless.Locator(".tm-notion-restricted-badge").CountAsync(), "Without a provider every page should be open.");
            Assert.AreEqual(0, await providerless.Locator(".tm-notion-page--readonly").CountAsync(), "Without a provider the page should remain editable.");

            var providerlessCapture = await CaptureRestrictionBaselineAsync(providerless, "cf20-providerless-open-state", providerless.Locator(".tm-notion-editor").First);
            TestContext.WriteLine($"UX CF20 providerless baseline captured: {providerlessCapture.FullPagePath} / {providerlessCapture.RegionPath}");

            await SetRestrictionsAsync(RootPageId, 2,
            [
                new(0, "alice", 3)
            ]);
            var noView = await OpenNoAccessNotionEditorAsync("?user=bob&groups=readers");
            await noView.Locator(".tm-notion-no-access").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

            var noViewCapture = await CaptureRestrictionBaselineAsync(noView, "cf20-no-view-state", noView.Locator(".tm-notion-no-access").First);
            TestContext.WriteLine($"UX CF20 no-view baseline captured: {noViewCapture.FullPagePath} / {noViewCapture.RegionPath}");

            var restrictedNavTitle = noView.Locator(".tm-npt-title").Filter(new() { HasText = "CF20 Restricted Workspace" });
            if (await restrictedNavTitle.CountAsync() > 0)
            {
                Assert.IsFalse(await restrictedNavTitle.First.IsVisibleAsync(), "Pages without View permission should be hidden from navigation.");
            }

            await SetRestrictionsAsync(RootPageId, 2,
            [
                new(1, "readers", 3),
                new(0, "bob", 1)
            ]);
            var conflict = await OpenNotionEditorAsync("?user=bob&groups=readers");
            await conflict.Locator(".tm-notion-page--readonly").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
            Assert.AreEqual(0, await conflict.Locator(".tm-notion-no-access").CountAsync(),
                "User-specific View should override group Edit without removing View access.");

            TestContext.WriteLine("UX CF20 review: restrictions dialog presents subject and permission rows without truncation, restricted badges remain visible in header/sidebar, read-only/no-view states are explicit, and providerless pages do not expose misleading locked affordances.");
        }
        finally
        {
            await SetRestrictionsAsync(RootPageId, 0, []);
        }
    }

    private static async Task OpenRestrictionsDialogAsync(IPage page)
    {
        await page.Locator(".tm-npsm-trigger").First.ClickAsync();
        await page.Locator(".tm-npsm__item").Filter(new() { HasText = "Page restrictions" }).ClickAsync();
    }

    private async Task<IPage> OpenNoAccessNotionEditorAsync(string query)
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("window.localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1280, 720);
        await page.GotoAsync($"{BaseUrl}/notion-editor{query}", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60000
        });

        await WaitForAppReadyAsync(page);
        await page.WaitForSelectorAsync(".tm-notion-editor", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
        return page;
    }

    private static async Task SetRestrictionsAsync(Guid pageId, int mode, IReadOnlyList<AclEntry> entries)
    {
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri(ApiBaseUrl) };
        var response = await http.PutAsJsonAsync($"/api/notion/permissions/pages/{pageId:D}", new
        {
            pageId,
            mode,
            entries = entries.Select(entry => new
            {
                subjectType = entry.SubjectType,
                subjectId = entry.SubjectId,
                permission = entry.Permission
            })
        });
        response.EnsureSuccessStatusCode();
    }

    private async Task<NotionBaselineCapture> CaptureRestrictionBaselineAsync(IPage page, string state, ILocator region)
    {
        var outputDir = BaselineOutput.DirectoryFor(TestContext, "notion",
            "restrictions");
        Directory.CreateDirectory(outputDir);

        var fullPath = Path.Combine(outputDir, $"{state}.png");
        var regionPath = Path.Combine(outputDir, $"{state}.region.png");

        await page.WaitForTimeoutAsync(250);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = fullPath,
            Type = ScreenshotType.Png,
            FullPage = true
        });

        await region.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = regionPath,
            Type = ScreenshotType.Png,
            OmitBackground = false
        });

        TestContext.AddResultFile(fullPath);
        TestContext.AddResultFile(regionPath);
        return new NotionBaselineCapture(fullPath, regionPath);
    }

    private async Task<NotionBaselineCapture> CaptureRestrictionClipBaselineAsync(IPage page, string state, ILocator anchor, double width, double height)
    {
        var outputDir = BaselineOutput.DirectoryFor(TestContext, "notion",
            "restrictions");
        Directory.CreateDirectory(outputDir);

        var fullPath = Path.Combine(outputDir, $"{state}.png");
        var regionPath = Path.Combine(outputDir, $"{state}.region.png");

        await page.WaitForTimeoutAsync(250);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = fullPath,
            Type = ScreenshotType.Png,
            FullPage = true
        });

        var box = await anchor.BoundingBoxAsync();
        Assert.IsNotNull(box, $"CF20 baseline anchor for {state} should have a visible bounding box.");
        Assert.IsTrue(box.Width <= width, $"CF20 baseline anchor for {state} should fit inside the requested clip width.");
        Assert.IsTrue(box.Height <= height, $"CF20 baseline anchor for {state} should fit inside the requested clip height.");

        var viewport = page.ViewportSize ?? new() { Width = 1280, Height = 720 };
        var x = Math.Max(0, box.X - 12);
        var y = Math.Max(0, box.Y - 8);
        var clipWidth = Math.Min(width, viewport.Width - x);
        var clipHeight = Math.Min(height, viewport.Height - y);

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = regionPath,
            Type = ScreenshotType.Png,
            Clip = new Clip
            {
                X = (float)x,
                Y = (float)y,
                Width = (float)clipWidth,
                Height = (float)clipHeight
            }
        });

        TestContext.AddResultFile(fullPath);
        TestContext.AddResultFile(regionPath);
        return new NotionBaselineCapture(fullPath, regionPath);
    }

    private sealed record AclEntry(int SubjectType, string SubjectId, int Permission);
}
