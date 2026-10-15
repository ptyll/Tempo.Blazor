using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 6 of the dashboard redesign — the redesigned TmWidgetSelector on the three reference
/// widths: desktop 1440 (sidebar), tablet 1024 touch (sidebar + two columns) and mobile 390/320
/// (bottom sheet with the category chip radio group). Covers the DA1 CloseOnSelect=false flow
/// (the selector stays open, the added card flips to the "Přidáno" state) and keyboard-only
/// operation. Screenshots land in <c>__screenshots__/dashboard/f6/</c> for the UX review against
/// output/imagegen/tempo-reimagined/TmDashboard/widget-selector--{desktop,tablet,mobile}.png.
/// </summary>
[TestClass]
public sealed class DashboardF6SelectorE2ETests : WasmTestBase
{
    private static string OutputDirectory =>
        Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "__screenshots__", "dashboard", "f6");

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Selector_Desktop1440_SidebarFilter_AddedState_AndKeyboardOnly()
    {
        var page = await OpenDashboardAsync(1440, 900);

        // Open the selector and capture the redesigned desktop surface.
        await OpenSelectorAsync(page);
        await ShotAsync(page, "desktop-1440-selector-open");

        // Sidebar category filter: activating a category narrows the grid.
        await FilterBySidebarAsync(page);
        await ShotAsync(page, "desktop-1440-category-filter");
        await CloseSelectorAsync(page);

        // DA1: with CloseOnSelect=false an add keeps the selector open and the card flips to
        // the green "Přidáno" state with the live announcement. The checkbox input is visually
        // hidden behind the switch track — click the label (the visible switch).
        await page.Locator("[data-testid='dashboard-close-on-select'] .tm-toggle-label").ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid='dashboard-close-on-select'] input"))
            .ToHaveAttributeAsync("aria-checked", "false", new LocatorAssertionsToHaveAttributeOptions { Timeout = 10000 });
        await OpenSelectorAsync(page);
        await page.Locator(".tm-widget-card").First.ClickAsync();

        await Assertions.Expect(page.Locator(".tm-widget-card-badge--added").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        await Assertions.Expect(page.Locator(".tm-widget-selector-modal"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        await ShotAsync(page, "desktop-1440-added-state");

        // The live region announces the addition politely (the demo defaults to the Czech UI).
        var live = await page.Locator(".tm-widget-selector-live").TextContentAsync();
        StringAssert.Contains(live, "Přidáno");

        // Keyboard only: focus a card and activate it with Enter — a native button, so the
        // browser's own key handling produces exactly one activation (keyboard convention).
        await page.Locator(".tm-widget-card").First.FocusAsync();
        var focused = await page.EvaluateAsync<string>("() => document.activeElement.className");
        StringAssert.Contains(focused, "tm-widget-card", "the card is the focused element");
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator(".tm-widget-card-badge--added").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

        // Done closes the selector (the demo defaults to the Czech UI: "Hotovo").
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Hotovo" }).ClickAsync();
        await Assertions.Expect(page.Locator(".tm-widget-selector-modal"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });
        await page.CloseAsync();
    }

    /// <remarks>
    /// The reference tablet width is 1024, but the library's tablet band is 768–1023
    /// (<c>TmBreakpoints.Lg = 1024</c> puts 1024 itself on desktop), so the tablet run uses 900 —
    /// the same width the earlier dashboard phases captured.
    /// </remarks>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task Selector_Tablet900Touch_KeepsSidebar_TwoColumns()
    {
        var page = await OpenDashboardAsync(900, 768, touch: true);

        await OpenSelectorAsync(page);

        // The sidebar survives on tablet; the card grid sits in exactly two columns.
        // The layout probe measures asynchronously — wait for the resolved data-layout.
        await page.Locator(".tm-widget-selector-modal[data-layout='tablet']").First.WaitForAsync(
            new LocatorWaitForOptions { Timeout = 15000 });
        await Assertions.Expect(page.Locator(".tm-widget-selector-sidebar"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        var columns = await page.EvaluateAsync<int>(
            "() => getComputedStyle(document.querySelector('.tm-widget-selector-grid')).gridTemplateColumns.split(' ').filter(Boolean).length");
        Assert.AreEqual(2, columns, "the tablet selector shows two card columns");

        await ShotAsync(page, "tablet-1024-selector-open");
        await FilterBySidebarAsync(page);
        await ShotAsync(page, "tablet-1024-category-filter");
        await page.CloseAsync();
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Selector_Mobile390_Sheet_ChipFilter()
    {
        var page = await OpenDashboardAsync(390, 844, touch: true);

        await OpenSelectorAsync(page);

        // The F2 sheet presentation with the chip radio group replacing the sidebar.
        // The layout probe measures asynchronously — wait for the resolved data-layout first.
        var modalRoot = page.Locator(".tm-widget-selector-modal[data-layout='mobile']");
        await modalRoot.First.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        var panel = page.Locator(".tm-modal");
        StringAssert.Contains(await panel.GetAttributeAsync("class"), "tm-sheet__panel",
            "the mobile selector presents as a bottom sheet");
        Assert.AreEqual(0, await page.Locator(".tm-widget-selector-sidebar").CountAsync());

        var group = page.Locator(".tm-widget-selector-chips");
        Assert.AreEqual("radiogroup", await group.GetAttributeAsync("role"));
        Assert.IsTrue((await group.Locator(".tm-widget-selector-chip").CountAsync()) > 1,
            "the chips carry every category plus 'All widgets'");

        await ShotAsync(page, "mobile-390-selector-sheet");

        // Filter by a chip: the radio flips and the grid narrows to that category.
        await group.Locator(".tm-widget-selector-chip").Nth(1).ClickAsync();
        await Assertions.Expect(group.Locator(".tm-widget-selector-chip").Nth(1))
            .ToHaveAttributeAsync("aria-checked", "true", new LocatorAssertionsToHaveAttributeOptions { Timeout = 10000 });
        await ShotAsync(page, "mobile-390-chip-filter");

        // One column, plus on the right, and Done closes the sheet.
        var columns = await page.EvaluateAsync<int>(
            "() => getComputedStyle(document.querySelector('.tm-widget-selector-grid')).gridTemplateColumns.split(' ').filter(Boolean).length");
        Assert.AreEqual(1, columns, "the mobile selector stacks the cards in one column");
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Hotovo" }).ClickAsync();
        await Assertions.Expect(page.Locator(".tm-widget-selector-modal"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });
        await page.CloseAsync();
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Selector_Mobile320_SheetFits_AndChipsScroll()
    {
        var page = await OpenDashboardAsync(320, 700, touch: true);

        await OpenSelectorAsync(page);

        await ShotAsync(page, "mobile-320-selector-sheet");

        // The chips row is horizontally scrollable at 320 — the last chip is reachable.
        await page.Locator(".tm-widget-selector-chips").EvaluateAsync("el => el.scrollBy(500, 0)");
        var chips = page.Locator(".tm-widget-selector-chips .tm-widget-selector-chip");
        await chips.Last.ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(chips.Last).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

        await page.CloseAsync();
    }

    private async Task<IPage> OpenDashboardAsync(int width, int height, bool touch = false)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            HasTouch = touch,
            ViewportSize = new ViewportSize { Width = width, Height = height },
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync(BaseUrl.TrimEnd('/') + "/dashboard",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await page.Locator(".tm-dashboard .tm-widget").First.WaitForAsync(
            new LocatorWaitForOptions { Timeout = 30000 });
        return page;
    }

    private static async Task OpenSelectorAsync(IPage page)
    {
        // View mode first: the Add widget control lives in the view toolbar (or the edit bar).
        var edit = page.Locator("[data-testid='dashboard-edit']");
        if (await edit.CountAsync() > 0 && await edit.First.IsVisibleAsync())
        {
            await edit.First.ClickAsync();
        }

        // Desktop/tablet: the toolbar button. Mobile: the compact edit mode renders the action
        // in the mobile action bar instead.
        var add = page.Locator("[data-testid='dashboard-add-widget']");
        if (await add.First.IsVisibleAsync())
        {
            await add.First.ClickAsync();
        }
        else
        {
            var bar = page.Locator(".tm-dashboard .tm-mobile-action-bar__bar");
            await bar.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
            // The bar's first action is "Add widget" (Add / Save / Cancel) in every locale.
            await bar.Locator(".tm-mobile-action-bar__action").First.ClickAsync();
        }

        await page.Locator(".tm-widget-selector-modal").First.WaitForAsync(
            new LocatorWaitForOptions { Timeout = 15000 });
    }

    private static async Task FilterBySidebarAsync(IPage page)
    {
        var categories = page.Locator(".tm-widget-selector-sidebar .tm-widget-selector-category");
        await categories.Nth(1).ClickAsync();
        await Assertions.Expect(categories.Nth(1))
            .ToHaveAttributeAsync("aria-pressed", "true", new LocatorAssertionsToHaveAttributeOptions { Timeout = 10000 });
    }

    private static async Task CloseSelectorAsync(IPage page)
    {
        await page.Locator(".tm-modal-close").ClickAsync();
        await Assertions.Expect(page.Locator(".tm-widget-selector-modal"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });
    }

    private async Task ShotAsync(IPage page, string name)
    {
        Directory.CreateDirectory(OutputDirectory);
        var path = Path.Combine(OutputDirectory, name + ".png");
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
