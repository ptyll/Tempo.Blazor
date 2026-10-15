using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E.Localization;

/// <summary>
/// E2E for the K7 localization sweep (WASM @ 7106): loads the demo in Czech (the host reads
/// <c>tm-demo-culture</c> from localStorage at startup) and verifies swept component strings —
/// the widget selector title and the localized widget-category names — render in Czech.
/// Screenshot lands in <c>__screenshots__/localization/</c> for UX review.
/// </summary>
[TestClass]
public class CzechUiE2ETests : WasmTestBase
{
    [TestMethod]
    [TestCategory("WASM")]
    public async Task Dashboard_InCzech_ShowsLocalizedWidgetSelectorAndCategories()
    {
        var context = await CreateContextAsync();
        // The WASM host applies the culture stored here before the app renders.
        await context.AddInitScriptAsync("window.localStorage.setItem('tm-demo-culture','cs');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await page.GotoAsync($"{BaseUrl}/dashboard", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);

        // Enter edit mode → open the widget selector.
        await page.GetByTestId("dashboard-edit").ClickAsync();
        await page.GetByTestId("dashboard-add-widget").ClickAsync();

        var selector = page.Locator(".tm-widget-selector-modal");
        await selector.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });

        // Swept, now-localized strings render in Czech. The title/subtitle ride on the TmModal
        // header since B12, so the assertions scope to the whole modal surface.
        var modal = page.Locator(".tm-modal");
        await Assertions.Expect(modal).ToContainTextAsync("Přidat widgety",
            new LocatorAssertionsToContainTextOptions { Timeout = 10000 });   // TmWidgetSelector_Title
        await Assertions.Expect(modal).ToContainTextAsync("Všechny widgety");  // TmWidgetSelector_AllWidgets
        await Assertions.Expect(modal).ToContainTextAsync("Analýzy a KPI");    // TmDashboard_Category_Analytics

        await SaveScreenshotAsync(page, "czech-widget-selector");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Dashboard_InCzech_DeleteDialog_ShowsLocalizedCancel()
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("window.localStorage.setItem('tm-demo-culture','cs');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await page.GotoAsync($"{BaseUrl}/dashboard", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);

        // Open the switcher menu and request the delete of the non-default dashboard.
        await page.Locator(".tm-dashboard-views button").First.ClickAsync();
        var menu = page.Locator(".tm-dashboard-menu");
        await menu.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await menu.Locator(".tm-dashboard-menu-action--danger").ClickAsync();

        // B11: the cancel button of the delete dialog is localized (not a bare "Cancel").
        var cancel = page.Locator(".tm-dialog-btn-cancel");
        await cancel.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await Assertions.Expect(cancel).ToContainTextAsync("Zrušit", new LocatorAssertionsToContainTextOptions { Timeout = 10000 });
        // The delete title itself is Czech and the cs term is unified on „nástěnka" (DA4).
        await Assertions.Expect(page.Locator(".tm-dialog-title").First).ToContainTextAsync("Smazat nástěnku");

        await SaveScreenshotAsync(page, "czech-delete-dashboard-dialog");
        await cancel.ClickAsync();
        await Assertions.Expect(page.Locator(".tm-dialog")).ToHaveCountAsync(0,
            new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Dashboard_InCzech_WidgetSelector_EscapeCloses()
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("window.localStorage.setItem('tm-demo-culture','cs');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await page.GotoAsync($"{BaseUrl}/dashboard", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);

        await page.GetByTestId("dashboard-edit").ClickAsync();
        await page.GetByTestId("dashboard-add-widget").ClickAsync();

        var modal = page.Locator(".tm-widget-selector-modal");
        await modal.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });

        // B12: Escape (the modal's focus trap) closes the selector.
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator(".tm-modal-overlay")).ToHaveCountAsync(0,
            new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Dashboard_InCzech_CreateDashboard_EnterSubmits()
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("window.localStorage.setItem('tm-demo-culture','cs');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await page.GotoAsync($"{BaseUrl}/dashboard", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);

        await page.Locator(".tm-dashboard-views button").First.ClickAsync();
        await page.Locator(".tm-dashboard-menu-item--action").ClickAsync();

        var dialog = page.Locator(".tm-modal-overlay");
        await dialog.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        var input = dialog.Locator("input").First;
        await input.FillAsync("Nákupní tým");
        await input.PressAsync("Enter");

        // The form's implicit submission created the dashboard: the dialog closed, the new
        // dashboard is on screen in edit mode. The demo host sets Title, so the created
        // dashboard's own name renders in the secondary .tm-dashboard-name span next to the
        // heading (F2 §2.4) — the heading itself keeps the host title.
        await Assertions.Expect(dialog).ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });
        await Assertions.Expect(page.Locator(".tm-dashboard-name")).ToHaveTextAsync("Nákupní tým");
        await Assertions.Expect(page.Locator(".tm-dashboard--edit").First).ToBeVisibleAsync();

        await SaveScreenshotAsync(page, "czech-create-dashboard-enter");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Dashboard_SwitcherMenu_ArrowDownEnter_LoadsSecondDashboard()
    {
        // Fáze 1 review (keyboard switcher): the menu is a TmOverlayPanel with the shared
        // tm-menu-nav module attached, so a real keyboard round-trip — open, ArrowDown to the
        // second entry, Enter — must switch dashboards and hand focus back to the trigger.
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("window.localStorage.setItem('tm-demo-culture','cs');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await page.GotoAsync($"{BaseUrl}/dashboard", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);

        await page.Locator(".tm-dashboard-views button").First.ClickAsync();
        var menu = page.Locator(".tm-dashboard-menu");
        await menu.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });

        // Opening hands focus to the first entry ("Main Dashboard"); wait for it so ArrowDown
        // roves from item 0 to item 1 deterministically.
        await page.WaitForFunctionAsync(
            "() => document.activeElement !== null && document.activeElement.closest('.tm-dashboard-menu') !== null",
            null, new PageWaitForFunctionOptions { Timeout = 10000 });

        // ArrowDown moves to the second dashboard row ("Operations") and Enter activates it natively.
        await page.Keyboard.PressAsync("ArrowDown");
        await page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(menu).ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });
        // The selection close returns focus to the switcher trigger, not <body>.
        var trigger = page.Locator(".tm-dashboard-views button").First;
        await Assertions.Expect(trigger).ToBeFocusedAsync(
            new LocatorAssertionsToBeFocusedOptions { Timeout = 10000 });

        // The demo page runs with ShowTitle=false, so the loaded dashboard is proven through the
        // switcher itself: reopening marks "Operations" as the current row (aria-current).
        await trigger.ClickAsync();
        var activeRow = page.Locator(".tm-dashboard-menu-item--active");
        await activeRow.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        await Assertions.Expect(activeRow).ToContainTextAsync("Operations",
            new LocatorAssertionsToContainTextOptions { Timeout = 10000 });
        await Assertions.Expect(activeRow.Locator(".tm-dashboard-menu-item__select"))
            .ToHaveAttributeAsync("aria-current", "true",
                new LocatorAssertionsToHaveAttributeOptions { Timeout = 10000 });

        // Close again through the overlay's own Escape path.
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(menu).ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });
    }

    private static async Task SaveScreenshotAsync(IPage page, string fileName)
    {
        var dir = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "localization");
        Directory.CreateDirectory(dir);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(dir, $"{fileName}.png"), FullPage = true });
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx"))) return directory;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Repository root was not found.");
    }
}
