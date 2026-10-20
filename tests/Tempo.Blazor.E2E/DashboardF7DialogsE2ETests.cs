using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 7 of the dashboard redesign: the "Create new dashboard" / "Delete dashboard" dialogs and
/// the dashboard switcher ride the F2/F3 foundation — bottom sheet on a phone, sticky footer,
/// focus back on the switcher trigger after closing. Shots land in
/// <c>__screenshots__/dashboard/</c> and are reviewed against
/// output/imagegen/tempo-reimagined/TmDashboard/dashboard--create-new-dashboard--*.png and
/// dashboard--delete-dashboard--*.png.
/// </summary>
[TestClass]
public sealed class DashboardF7DialogsE2ETests : WasmTestBase
{
    public static readonly (int Width, int Height, bool Touch)[] Viewports =
    {
        (1440, 900, false),
        (1024, 768, true),
        (390, 844, true),
        (320, 700, true),
    };

    [TestMethod]
    [TestCategory("WASM")]
    public async Task CreateDialog_AtEveryViewport_SheetOnMobile_InlineFooterFocusReturns()
    {
        foreach (var (width, height, touch) in Viewports)
        {
            var page = await OpenDashboardAsync(width, height, touch);
            await OpenCreateDialogAsync(page);

            var dialog = page.Locator(".tm-modal:visible").First;
            Assert.IsTrue(await dialog.IsVisibleAsync(), $"{width}px: create dialog visible");

            // Cancel is the outlined secondary action, Create the primary one.
            Assert.IsTrue(await page.Locator(".tm-modal-footer .tm-btn-secondary:visible").First.IsVisibleAsync(),
                $"{width}px: Cancel is secondary");
            Assert.IsTrue(await page.Locator(".tm-modal-footer .tm-btn-primary:visible").First.IsVisibleAsync(),
                $"{width}px: Create is primary");

            var isMobileWidth = width <= 390;
            var isSheet = await page.Locator(".tm-modal--sheet:visible").CountAsync() > 0;
            Assert.AreEqual(isMobileWidth, isSheet,
                $"{width}px: sheet presentation exactly at mobile widths");

            if (isSheet)
            {
                // The sheet is anchored to the bottom of the viewport and its footer is sticky.
                var box = await dialog.BoundingBoxAsync();
                Assert.IsNotNull(box);
                await WaitForSheetSettledAsync(dialog, height);
                box = await dialog.BoundingBoxAsync();
                Assert.AreEqual(height, box!.Y + box.Height, 2,
                    $"{width}px: the sheet sits flush with the bottom of the viewport");

                var footer = page.Locator(".tm-modal--sheet .tm-modal-footer").First;
                var footerPosition = await footer.EvaluateAsync<string>("el => getComputedStyle(el).position");
                Assert.AreEqual("sticky", footerPosition, $"{width}px: sheet footer is sticky");

                // 50/50 split, both ≥ 48px tall.
                var cancel = page.Locator(".tm-modal-footer .tm-btn-secondary").First;
                var create = page.Locator(".tm-modal-footer .tm-btn-primary").First;
                var cb = await cancel.BoundingBoxAsync();
                var pb = await create.BoundingBoxAsync();
                Assert.IsNotNull(cb);
                Assert.IsNotNull(pb);
                Assert.IsTrue(cb.Height >= 48 && pb.Height >= 48, $"{width}px: footer buttons ≥ 48px tall");
                Assert.AreEqual(cb.Width, pb.Width, 4, $"{width}px: footer buttons split 50/50");

                // The placeholder is a concrete example, not a repeat of the label.
                var placeholder = await page.Locator(".tm-modal input").First.GetAttributeAsync("placeholder");
                Assert.IsFalse(string.IsNullOrWhiteSpace(placeholder));
                Assert.IsTrue(placeholder!.StartsWith("e.g.") || placeholder.Contains("např", StringComparison.Ordinal),
                    $"{width}px: placeholder is an example (got '{placeholder}')");
            }

            await ShotAsync(page, $"f7-create-dialog--{width}");

            // Focus returns to the switcher trigger after closing (Escape).
            await page.Keyboard.PressAsync("Escape");
            await page.WaitForFunctionAsync(
                "() => document.activeElement && document.activeElement.classList.contains('tm-dashboard-switcher')",
                null, new PageWaitForFunctionOptions { Timeout = 10_000 });
            await page.CloseAsync();
        }
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task DeleteDialog_AtEveryViewport_TrashIcon_DeleteAboveCancelOnMobileSheet()
    {
        foreach (var (width, height, touch) in Viewports)
        {
            var page = await OpenDashboardAsync(width, height, touch);
            await page.Locator(".tm-dashboard-switcher").ClickAsync();
            await page.Locator(".tm-dashboard-menu, .tm-overlay-panel-sheet__content").First.WaitForAsync(
                new LocatorWaitForOptions { Timeout = 15_000 });

            var danger = page.Locator(".tm-dashboard-menu-action--danger").First;
            await danger.ClickAsync();
            var dialog = page.Locator(".tm-dialog:visible").First;
            await dialog.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });

            // The destructive confirm carries the trash-2 glyph.
            var iconPath = await page.Locator(".tm-dialog-icon svg").First.InnerHTMLAsync();
            Assert.IsTrue(iconPath.Contains("M19 6l-1 14"), $"{width}px: delete dialog shows the trash-2 icon");

            var isMobileWidth = width <= 390;
            var isSheet = await page.Locator(".tm-dialog.tm-sheet__panel:visible, .tm-modal--sheet .tm-dialog:visible").CountAsync() > 0;
            Assert.AreEqual(isMobileWidth, isSheet, $"{width}px: delete dialog sheet presentation at mobile widths");

            if (isSheet)
            {
                var box = await dialog.BoundingBoxAsync();
                Assert.IsNotNull(box);
                await WaitForSheetSettledAsync(dialog, height);
                box = await dialog.BoundingBoxAsync();
                Assert.AreEqual(height, box!.Y + box.Height, 2,
                    $"{width}px: the delete sheet sits flush with the bottom of the viewport");

                // Stacked footer, column-reverse: Delete (ok) paints ABOVE Cancel.
                var ok = page.Locator(".tm-dialog-btn-ok").First;
                var cancel = page.Locator(".tm-dialog-btn-cancel").First;
                var ob = await ok.BoundingBoxAsync();
                var cb = await cancel.BoundingBoxAsync();
                Assert.IsNotNull(ob);
                Assert.IsNotNull(cb);
                Assert.IsTrue(ob.Y < cb.Y, $"{width}px: Delete sits above Cancel on the mobile sheet");
                Assert.IsTrue(ob.Height >= 44 && cb.Height >= 44, $"{width}px: delete dialog buttons ≥ 44px tall");
            }

            await ShotAsync(page, $"f7-delete-dialog--{width}");

            await page.Keyboard.PressAsync("Escape");
            await page.WaitForFunctionAsync(
                "() => document.activeElement && document.activeElement.classList.contains('tm-dashboard-switcher')",
                null, new PageWaitForFunctionOptions { Timeout = 10_000 });
            await page.CloseAsync();
        }
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Switcher_OnMobile_IsBottomSheetWithTitle_DefaultRowExplainsAndFocusReturns()
    {
        var page = await OpenDashboardAsync(390, 844, touch: true);
        await page.Locator(".tm-dashboard-switcher").ClickAsync();

        var sheet = page.Locator(".tm-overlay-panel-sheet").First;
        await sheet.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });

        var title = await page.Locator(".tm-overlay-panel-sheet__title").First.TextContentAsync();
        Assert.IsFalse(string.IsNullOrWhiteSpace(title), "the sheet header is titled");

        var box = await sheet.BoundingBoxAsync();
        Assert.IsNotNull(box);
        await WaitForSheetSettledAsync(sheet, 844);
        box = await sheet.BoundingBoxAsync();
        Assert.AreEqual(844, box!.Y + box.Height, 2, "the switcher sheet is anchored to the bottom");

        // The default row explains why delete is unavailable, instead of a bare star.
        var defaultRow = page.Locator(".tm-dashboard-menu-default").First;
        Assert.IsTrue(await defaultRow.IsVisibleAsync());
        var hintText = await page.Locator(".tm-dashboard-menu-default-hint").First.TextContentAsync();
        Assert.IsFalse(string.IsNullOrWhiteSpace(hintText), "the default row explains why it cannot be deleted");

        await ShotAsync(page, "f7-switcher-sheet--390");

        // Done closes the sheet and focus returns to the trigger.
        await page.Locator(".tm-overlay-panel-sheet__done").First.ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.activeElement && document.activeElement.classList.contains('tm-dashboard-switcher')",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        await page.CloseAsync();
    }

    // ── helpers ───────────────────────────────────────────────────

    /// <summary>The sheets slide up from below the viewport — wait until the box rests at the bottom.</summary>
    private static async Task WaitForSheetSettledAsync(ILocator sheet, int viewportHeight)
    {
        for (var i = 0; i < 20; i++)
        {
            var box = await sheet.BoundingBoxAsync();
            if (box is not null && Math.Abs(box.Y + box.Height - viewportHeight) <= 2)
            {
                return;
            }

            await sheet.Page.WaitForTimeoutAsync(150);
        }
    }

    private async Task<IPage> OpenDashboardAsync(int width, int height, bool touch)
    {
        IPage page;
        if (touch)
        {
            var context = await Browser.NewContextAsync(new BrowserNewContextOptions
            {
                HasTouch = true,
                IsMobile = true,
                ViewportSize = new ViewportSize { Width = width, Height = height },
                Locale = "en-US",
                IgnoreHTTPSErrors = true,
            });
            RegisterContext(context);
            page = await context.NewPageAsync();
        }
        else
        {
            page = await CreatePageAsync();
            await page.SetViewportSizeAsync(width, height);
        }

        await page.GotoAsync(BaseUrl.TrimEnd('/') + "/dashboard",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60_000 });
        await page.WaitForFunctionAsync(
            "() => document.body !== null && document.body.hasAttribute('data-blazor-ready')",
            null, new PageWaitForFunctionOptions { Timeout = 30_000 });
        await page.Locator(".tm-dashboard .tm-widget").First.WaitForAsync(
            new LocatorWaitForOptions { Timeout = 30_000 });
        await page.WaitForTimeoutAsync(400);
        await page.Mouse.MoveAsync(0, 0);
        return page;
    }

    private static async Task OpenCreateDialogAsync(IPage page)
    {
        await page.Locator(".tm-dashboard-switcher").ClickAsync();
        await page.Locator(".tm-dashboard-menu, .tm-overlay-panel-sheet__content").First.WaitForAsync(
            new LocatorWaitForOptions { Timeout = 15_000 });
        await page.Locator(".tm-dashboard-menu-item--action").First.ClickAsync();
        await page.Locator(".tm-modal input").First.WaitForAsync(
            new LocatorWaitForOptions { Timeout = 15_000 });
    }

    private async Task ShotAsync(IPage page, string name)
    {
        var directory = Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "__screenshots__", "dashboard");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name + ".png");
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = path,
            Type = ScreenshotType.Png,
            FullPage = false,
            Timeout = 60_000,
        });
        TestContext.AddResultFile(path);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx.");
    }
}
