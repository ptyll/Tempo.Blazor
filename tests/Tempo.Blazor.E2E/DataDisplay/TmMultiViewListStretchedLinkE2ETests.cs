using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 5.5: one key press on a stretched-link card raises OnItemClick exactly once.
/// The card carries no key handler, so the native button click is the only activation.
/// </summary>
[TestClass]
public class TmMultiViewListStretchedLinkE2ETests : WasmTestBase
{
    [TestMethod]
    public async Task StretchedLink_EnterAndSpaceRaiseOnItemClickOnce()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/data-display");
        await WaitForAppReadyAsync(page);

        var section = page.GetByTestId("mvl-stretched-link");
        var button = section.Locator("button.tm-stretched-link").First;
        await button.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await button.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.GetByTestId("mvl-stretched-clicks")).ToHaveTextAsync("1");

        await button.FocusAsync();
        await page.Keyboard.PressAsync(" ");
        await Assertions.Expect(page.GetByTestId("mvl-stretched-clicks")).ToHaveTextAsync("2");

        // Enter on the anchor navigates and raises OnItemClick once — the card adds nothing.
        var anchor = section.Locator("a.tm-stretched-link").First;
        await anchor.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.GetByTestId("mvl-stretched-href-clicks")).ToHaveTextAsync("1");
    }
}
