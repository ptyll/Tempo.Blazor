using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan 1c95309c Fáze 6 — E2E of TmTimePicker Popup/Auto on /time-input. No screenshots
/// (never stage PNGs / __screenshots__/reporting).
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class TmTimePickerPresentationE2ETests : WasmTestBase
{
    private static Task PinCultureAsync(IBrowserContext context) =>
        context.AddInitScriptAsync(
            "localStorage.setItem('tm-demo-culture', 'en'); document.cookie = 'tm-demo-culture=en;path=/;max-age=31536000;samesite=lax';");

    private async Task<IPage> OpenTimeInputAsync(int width, int height, bool touch = false)
    {
        var page = await OpenAppPageAsync(
            $"{BaseUrl}/time-input",
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = width, Height = height },
                Locale = "en-US",
                IgnoreHTTPSErrors = true,
                HasTouch = touch,
                IsMobile = touch,
            },
            PinCultureAsync);

        await page.WaitForSelectorAsync("[data-testid='time-picker-popup']", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60_000,
        });
        return page;
    }

    [TestMethod]
    public async Task Popup_Desktop_OpensDialogSelector()
    {
        var page = await OpenTimeInputAsync(1280, 800);
        var card = page.Locator("[data-testid='time-picker-popup-card']");
        await Assertions.Expect(card.Locator(".tm-time-seg")).ToHaveCountAsync(2);

        var trigger = card.Locator(".tm-time-picker__trigger");
        await Assertions.Expect(trigger).ToHaveAttributeAsync("aria-haspopup", "dialog");
        await Assertions.Expect(trigger).ToHaveAttributeAsync("aria-expanded", "false");

        await trigger.ClickAsync();
        await Assertions.Expect(trigger).ToHaveAttributeAsync("aria-expanded", "true");
        await Assertions.Expect(page.Locator(".tm-time-picker__popup [data-testid='time-selector']")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".tm-time-picker__popup [data-tm-time-segment='hours']")).ToHaveTextAsync("14");
    }

    [TestMethod]
    public async Task Auto_Mobile_OpensSheetWithoutSegments()
    {
        var page = await OpenTimeInputAsync(390, 844, touch: true);
        var card = page.Locator("[data-testid='time-picker-auto-card']");
        await Assertions.Expect(card.Locator(".tm-time-seg")).ToHaveCountAsync(0);

        var trigger = card.Locator(".tm-time-picker__trigger");
        await Assertions.Expect(trigger).ToContainTextAsync("9:00");
        await trigger.ClickAsync();

        await Assertions.Expect(page.Locator(".tm-drawer")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".tm-overlay-panel-sheet__done")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".tm-drawer [data-testid='time-selector']")).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task DstSection_ShowsGapAndAmbiguousSelectors()
    {
        var page = await OpenTimeInputAsync(1280, 800);
        var card = page.Locator("[data-testid='time-picker-dst-card']");
        await Assertions.Expect(card.Locator("[data-testid='time-selector']")).ToHaveCountAsync(2);
        await Assertions.Expect(card.Locator("#time-selector-dst-gap-hours")).ToBeVisibleAsync();
        await Assertions.Expect(card.Locator("#time-selector-dst-ambiguous-hours")).ToBeVisibleAsync();
    }
}
