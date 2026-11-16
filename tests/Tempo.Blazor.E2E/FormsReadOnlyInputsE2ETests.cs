using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E of the Fáze 3 input surfaces on the <c>/forms</c> demo page (WASM @ 7106; plan 98c80bc3):
/// the read-only textarea carries the native <c>readonly</c> attribute on the readable
/// <c>tm-input-readonly</c> surface (not the disabled gray), the read-only select carries
/// <c>aria-readonly</c>, the character counter lives IN the label row through the
/// <c>LabelTrailing</c> slot, and the new cards are captured in light and dark for UX review.
/// </summary>
[TestClass]
public sealed class FormsReadOnlyInputsE2ETests : WasmTestBase
{
    private const string PagePath = "/forms";
    private const string ReadOnlyTextareaCard = "[data-testid='textarea-readonly-card']";
    private const string ReadOnlySelectCard = "[data-testid='select-readonly-card']";

    private readonly List<string> _clientErrors = [];

    private async Task<IPage> OpenPageAsync(int width = 1440, int height = 1000)
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        page.PageError += (_, error) => { lock (_clientErrors) _clientErrors.Add("PAGEERROR: " + error); };
        await page.SetViewportSizeAsync(width, height);
        await GotoAppReadyAsync(page, $"{BaseUrl}{PagePath}");
        await page.Locator(ReadOnlyTextareaCard).WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        return page;
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task ReadOnlyTextarea_IsReadableAndNotDisabled()
    {
        var page = await OpenPageAsync();

        var textarea = page.Locator($"{ReadOnlyTextareaCard} textarea");
        Assert.IsNotNull(await textarea.GetAttributeAsync("readonly"),
            "the read-only textarea carries the native readonly attribute");
        Assert.IsNull(await textarea.GetAttributeAsync("disabled"),
            "readonly is the readable state — it is never the disabled presentation");
        StringAssert.Contains(await textarea.GetAttributeAsync("class"), "tm-input-readonly");
        StringAssert.DoesNotMatch(await textarea.GetAttributeAsync("class"), new Regex("tm-input-disabled"));

        AssertNoClientErrors();
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task ReadOnlySelect_CarriesAriaReadonlyAndStaysEnabled()
    {
        var page = await OpenPageAsync();

        var select = page.Locator($"{ReadOnlySelectCard} select");
        Assert.AreEqual("true", await select.GetAttributeAsync("aria-readonly"),
            "a native <select> has no readonly attribute, so the ARIA state names the locked value");
        Assert.IsNull(await select.GetAttributeAsync("disabled"));
        StringAssert.Contains(await select.GetAttributeAsync("class"), "tm-select-readonly");

        AssertNoClientErrors();
    }

    /// <summary>
    /// Review round (plan 98c80bc3, Fáze 3, B2): a read-only select must not move — a programmatic
    /// SelectOptionAsync attempt AND a keyboard typeahead both leave the DOM value at the locked
    /// value (the unselected options render disabled, and a raw attempt is rolled back).
    /// </summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task ReadOnlySelect_RejectsAnotherOptionAttemptAndTypeahead()
    {
        var page = await OpenPageAsync();

        var select = page.Locator($"{ReadOnlySelectCard} select");
        var before = await select.InputValueAsync();
        Assert.AreEqual("locked-contract", before);

        // A programmatic attempt (the API a test harness or an extension would use). The change is
        // refused and the select remounts from the rendered value, so the attempt does not linger —
        // polled, because the round trip through the WASM circuit takes a beat.
        await select.SelectOptionAsync("archived");
        await Microsoft.Playwright.Assertions.Expect(select).ToHaveValueAsync(before);

        // A keyboard typeahead attempt: focus the select and type the first letter of "Archived".
        // Every option that is not the locked value renders disabled, so the browser cannot move.
        await select.FocusAsync();
        await page.Keyboard.PressAsync("a");
        await Microsoft.Playwright.Assertions.Expect(select).ToHaveValueAsync(before);

        AssertNoClientErrors();
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task LabelTrailing_CounterLivesInTheLabelRow()
    {
        var page = await OpenPageAsync();

        var card = page.Locator("[data-testid='textarea-section'] .showcase-card")
            .Filter(new() { HasText = "With Character Limit" });
        var row = card.Locator(".tm-input-label-row");
        await Microsoft.Playwright.Assertions.Expect(row).ToBeVisibleAsync();
        var counter = row.Locator("[data-testid='textarea-label-trailing-counter']");
        await Microsoft.Playwright.Assertions.Expect(counter).ToContainTextAsync("0 / 100");

        // Typing updates the counter in the row (the demo binds the live length).
        await card.Locator("textarea").FillAsync("hello");
        await Microsoft.Playwright.Assertions.Expect(counter).ToContainTextAsync("5 / 100");

        AssertNoClientErrors();
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Screenshots_ReadOnlyCardsLightAndDark()
    {
        var page = await OpenPageAsync();

        await page.Locator("[data-testid='textarea-section']").ScrollIntoViewIfNeededAsync();
        await WaitForLayoutToSettleAsync(page, "[data-testid='textarea-section']");

        var directory = Path.Combine(
            FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "forms");
        Directory.CreateDirectory(directory);

        await page.Locator("[data-testid='textarea-section']").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(directory, "forms-textarea-fase3-light-1440.png"),
        });

        await ToggleDarkModeAsync(page);
        await WaitForLayoutToSettleAsync(page, "[data-testid='textarea-section']");
        await page.Locator("[data-testid='textarea-section']").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(directory, "forms-textarea-fase3-dark-1440.png"),
        });

        AssertNoClientErrors();
    }

    /// <summary>
    /// Review round (plan 98c80bc3, Fáze 3, B3): the state-comparison cards are captured in light and
    /// dark, so the readable locked surface and the gray disabled surface are visibly different
    /// artifacts for UX review.
    /// </summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task Screenshots_StateComparisonsLightAndDark()
    {
        var page = await OpenPageAsync();

        var directory = Path.Combine(
            FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "forms");
        Directory.CreateDirectory(directory);

        // The select comparison: editable, read-only, disabled side by side.
        await page.Locator("[data-testid='select-states-card']").ScrollIntoViewIfNeededAsync();
        await WaitForLayoutToSettleAsync(page, "[data-testid='select-states-card']");
        await page.Locator("[data-testid='select-states-card']").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(directory, "forms-select-fase3-light-1440.png"),
        });

        // The filterable dropdown comparison, including the read-only combobox demo card.
        await page.Locator("[data-testid='dropdown-states-card']").ScrollIntoViewIfNeededAsync();
        await WaitForLayoutToSettleAsync(page, "[data-testid='dropdown-states-card']");
        await page.Locator("[data-testid='dropdown-states-card']").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(directory, "forms-dropdown-fase3-light-1440.png"),
        });

        await ToggleDarkModeAsync(page);

        await page.Locator("[data-testid='select-states-card']").ScrollIntoViewIfNeededAsync();
        await WaitForLayoutToSettleAsync(page, "[data-testid='select-states-card']");
        await page.Locator("[data-testid='select-states-card']").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(directory, "forms-select-fase3-dark-1440.png"),
        });

        await page.Locator("[data-testid='dropdown-states-card']").ScrollIntoViewIfNeededAsync();
        await WaitForLayoutToSettleAsync(page, "[data-testid='dropdown-states-card']");
        await page.Locator("[data-testid='dropdown-states-card']").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(directory, "forms-dropdown-fase3-dark-1440.png"),
        });

        AssertNoClientErrors();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────

    private void AssertNoClientErrors()
    {
        lock (_clientErrors)
        {
            Assert.IsTrue(_clientErrors.Count == 0,
                "Unhandled client-side errors occurred:\n" + string.Join("\n", _clientErrors));
        }
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
            {
                return directory;
            }

            directory = directory.Parent!;
        }

        throw new InvalidOperationException("repo root not found");
    }
}
