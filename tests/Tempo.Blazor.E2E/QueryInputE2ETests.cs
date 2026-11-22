using System.IO;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 4 — E2E coverage for the new TmQueryInput component on the /query-input demo: caret-aware
/// autocomplete, accepting a suggestion, inline error underlines and dark mode.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public class QueryInputE2ETests : WasmTestBase
{
    private static readonly string StableShotDir = Path.Combine(
        Environment.GetEnvironmentVariable("TM_E2E_SHOT_DIR") ?? Path.GetTempPath(), "kanban-e2e-shots");

    private async Task ShotAsync(IPage page, string name)
    {
        try
        {
            Directory.CreateDirectory(StableShotDir);
            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = Path.Combine(StableShotDir, name + ".png"),
                Type = ScreenshotType.Png,
                FullPage = true
            });
        }
        catch
        {
            // Screenshots are diagnostic — never fail a test on a screenshot write.
        }
    }

    private const int LongTimeout = 20000;

    private async Task<(IPage page, ILocator input)> OpenAsync()
    {
        var page = await CreatePageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}/query-input");
        var input = page.Locator("[data-testid='query-input-demo'] .tm-query-input__input");
        await input.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = LongTimeout });
        return (page, input);
    }

    // Types into the (already-present) query input, firing per-char Blazor input events so the
    // caret + debounced suggestions behave exactly as a user's typing would.
    private static async Task TypeAsync(ILocator input, string text)
    {
        await input.ClickAsync();
        await input.FillAsync("");
        await input.PressSequentiallyAsync(text, new LocatorPressSequentiallyOptions { Delay = 25 });
    }

    [TestMethod]
    [Description("E2E-P4.0: Typing a field prefix opens the autocomplete dropdown")]
    public async Task QueryInput_Typing_Opens_Autocomplete()
    {
        var (page, input) = await OpenAsync();

        await TypeAsync(input, "sta");
        var listbox = page.Locator("[data-testid='query-input-demo'] [role='listbox']");
        await listbox.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = LongTimeout });

        var option = page.Locator("[data-testid='query-input-demo'] .tm-query-input__option");
        Assert.IsTrue(await option.CountAsync() >= 1, "Expected at least the 'status' suggestion");
        StringAssert.Contains((await option.First.InnerTextAsync()), "status");

        await ShotAsync(page, "query_input_p4_autocomplete_light");
    }

    [TestMethod]
    [Description("E2E-P4.1: Clicking a suggestion inserts it at the caret")]
    public async Task QueryInput_AcceptSuggestion_UpdatesValue()
    {
        var (page, input) = await OpenAsync();

        await TypeAsync(input, "sta");
        var option = page.Locator("[data-testid='query-input-demo'] .tm-query-input__option").First;
        await option.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = LongTimeout });
        await option.ClickAsync();

        StringAssert.Contains(await input.InputValueAsync(), "status");
    }

    [TestMethod]
    [Description("E2E-P4.2 (edge): An invalid field value is underlined via the error overlay")]
    public async Task QueryInput_InvalidValue_ShowsErrorUnderline()
    {
        var (page, input) = await OpenAsync();

        await TypeAsync(input, "priority = Urgent");
        await page.Keyboard.PressAsync("Escape"); // close the suggestions dropdown so the underline is visible
        await page.WaitForTimeoutAsync(200);

        var error = page.Locator("[data-testid='query-input-demo'] .tm-query-input__error");
        await error.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = LongTimeout });
        await ShotAsync(page, "query_input_p4_error_underline");

        StringAssert.Contains(await error.First.InnerTextAsync(), "Urgent");
        StringAssert.Contains(await error.First.GetAttributeAsync("title") ?? "", "Unknown priority value");

        await ShotAsync(page, "query_input_p4_error_underline");
    }

    [TestMethod]
    [Description("E2E-P4.3: Query input renders correctly in dark mode with an open dropdown")]
    public async Task QueryInput_DarkMode()
    {
        var (page, input) = await OpenAsync();

        await page.EvaluateAsync("() => document.documentElement.setAttribute('data-theme','dark')");
        await page.WaitForTimeoutAsync(200);

        await TypeAsync(input, "status = ");
        var listbox = page.Locator("[data-testid='query-input-demo'] [role='listbox']");
        await listbox.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = LongTimeout });

        await ShotAsync(page, "query_input_p4_dark");
    }
}
