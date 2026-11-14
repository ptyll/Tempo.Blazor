using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan 3e96f6d8 phase 4 — <c>TmModelPicker</c> in a real browser. The assertions read behaviour rather than
/// markup: the trigger names the choice, the popup compares the catalogue with bars AND the values in words,
/// the search filters and marks its hits, the arrow keys walk the list and Enter picks (the page's own
/// read-out follows and the focus returns to the trigger), the reasoning options follow the picked model, the
/// open list streams every change into the page's log, the edge states say what they are, the phone presents
/// the popup as a bottom sheet with the page never scrolling sideways, and a scoped axe scan of three sections
/// finds no critical or serious violation. Reference screenshots (light, dark, phone) land in
/// <c>TestResults/model-picker</c>.
/// </summary>
public abstract class ModelPickerE2EBase : PlaywrightTestBase
{
    private const string BasicSection = "[data-testid='model-picker-basic-section']";
    private const string VariantsSection = "[data-testid='model-picker-variants-section']";
    private const string EventsSection = "[data-testid='model-picker-events-section']";
    private const string EdgeSection = "[data-testid='model-picker-edge-section']";

    private string ShotDir
        => Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "model-picker");

    [TestInitialize]
    public void EnsureShotDir() => Directory.CreateDirectory(ShotDir);

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    private static ILocator Picker(IPage page, string testId) => page.Locator($"[data-testid='{testId}']");

    private static ILocator Trigger(IPage page, string testId)
        => Picker(page, testId).Locator("[data-testid='model-picker-trigger']");

    private static ILocator Options(IPage page, string testId)
        => Picker(page, testId).Locator("[data-testid='model-picker-option']");

    private static async Task<string?> ReadoutAsync(IPage page)
        => (await page.Locator("[data-testid='model-picker-selection-readout']").TextContentAsync())?.Trim();

    private async Task<IPage> OpenAsync(int width = 1440, int height = 1100, bool coarsePointer = false)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
            HasTouch = coarsePointer,
        });
        await context.AddInitScriptAsync(
            "localStorage.setItem('tm-demo-culture', 'en'); document.cookie = 'tm-demo-culture=en;path=/;max-age=31536000;samesite=lax';");
        RegisterContext(context);

        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}/model-picker");
        await page.WaitForSelectorAsync(
            $"{BasicSection} [data-testid='model-picker-trigger']",
            new PageWaitForSelectorOptions { Timeout = 30000 });
        await page.WaitForTimeoutAsync(400);
        return page;
    }

    [TestMethod]
    public async Task TheCatalogueComparesTheModels_AndTheTriggerNamesTheChoice()
    {
        var page = await OpenAsync();

        StringAssert.Contains(await Trigger(page, "model-picker-basic").TextContentAsync(), "GPT-4o");
        StringAssert.Contains(await Trigger(page, "model-picker-basic").TextContentAsync(), "OpenAI");
        Assert.IsNull(await Trigger(page, "model-picker-basic").GetAttributeAsync("aria-label"),
            "the trigger is named by its visible text, so the state tags stay in the accessible name");
        var triggerName = string.Join(
            " ",
            ((await Trigger(page, "model-picker-basic").TextContentAsync()) ?? string.Empty)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        StringAssert.StartsWith(triggerName, "Model: GPT-4o",
            "a visually hidden 'Model:' prefix keeps the field's context in that name");

        await Trigger(page, "model-picker-basic").ClickAsync();
        await page.WaitForSelectorAsync($"{BasicSection} [data-testid='model-picker-option']");

        Assert.AreEqual(5, await Options(page, "model-picker-basic").CountAsync());
        var headers = await Picker(page, "model-picker-basic")
            .Locator("[data-testid='model-picker-group-header']").AllTextContentsAsync();
        CollectionAssert.AreEqual(new[] { "OpenAI", "Anthropic", "Other" },
            headers.Select(header => header.Trim()).ToArray());

        var premium = Picker(page, "model-picker-basic").Locator("[data-model-id='gpt-4o']");
        Assert.AreEqual("Speed 4/5",
            (await premium.Locator("[data-testid='model-picker-option-speed']").TextContentAsync())?.Trim());
        Assert.AreEqual("Quality 5/5",
            (await premium.Locator("[data-testid='model-picker-option-quality']").TextContentAsync())?.Trim());
        Assert.AreEqual("$3.00 / 1M input",
            (await premium.Locator("[data-testid='model-picker-option-price']").TextContentAsync())?.Trim());
        Assert.AreEqual("128k context",
            (await premium.Locator("[data-testid='model-picker-option-context']").TextContentAsync())?.Trim());
        StringAssert.Contains(
            await premium.Locator("[data-testid='model-picker-option-capabilities']").TextContentAsync(), "vision");

        // The price bar is scaled against the most expensive model in the catalogue, and it is decorative.
        var cheapFill = Picker(page, "model-picker-basic")
            .Locator("[data-model-id='gpt-4o-mini'] [data-testid='model-picker-option-price'] .tm-model-picker__bar-fill");
        Assert.AreEqual("5%", (await cheapFill.GetAttributeAsync("style"))?.Replace("--tm-model-picker-bar:", "").Trim());
        Assert.AreEqual("true",
            await cheapFill.Locator("xpath=..").GetAttributeAsync("aria-hidden"));

        // A model that reports nothing says nothing: no bar, no chip, but the deprecated word is there.
        var legacy = Picker(page, "model-picker-basic").Locator("[data-model-id='legacy-text']");
        Assert.AreEqual(0, await legacy.Locator("[data-testid='model-picker-option-speed']").CountAsync());
        Assert.AreEqual(0, await legacy.Locator("[data-testid='model-picker-option-context']").CountAsync());
        Assert.AreEqual("Deprecated",
            (await legacy.Locator("[data-testid='model-picker-option-deprecated']").TextContentAsync())?.Trim());

        // The prompt estimate prices the selected model.
        Assert.AreEqual("Estimated prompt cost ≈ $0.30",
            (await Picker(page, "model-picker-basic").Locator("[data-testid='model-picker-estimate']").TextContentAsync())?.Trim());
    }

    [TestMethod]
    public async Task TheSearchFiltersTheList_AndMarksTheMatchedRun()
    {
        var page = await OpenAsync();
        await Trigger(page, "model-picker-basic").ClickAsync();
        await page.WaitForSelectorAsync($"{BasicSection} [data-testid='model-picker-search']");

        await page.Keyboard.TypeAsync("mini");
        await page.WaitForTimeoutAsync(250);

        Assert.AreEqual(1, await Options(page, "model-picker-basic").CountAsync());
        var marks = await Picker(page, "model-picker-basic").Locator("mark.tm-highlighted-text__match").AllTextContentsAsync();
        CollectionAssert.Contains(marks.ToList(), "mini");

        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.TypeAsync("zzz");
        await page.WaitForTimeoutAsync(250);

        Assert.AreEqual(0, await Options(page, "model-picker-basic").CountAsync());
        Assert.AreEqual("No model matches \"zzz\".",
            (await Picker(page, "model-picker-basic").Locator("[data-testid='model-picker-empty']").TextContentAsync())?.Trim());
        Assert.AreEqual("Matches: 0",
            (await Picker(page, "model-picker-basic").Locator("[data-testid='model-picker-announcement']").TextContentAsync())?.Trim());
    }

    [TestMethod]
    public async Task TheKeyboardOpensTheList_AndEnterPicks()
    {
        var page = await OpenAsync();

        await Trigger(page, "model-picker-basic").FocusAsync();
        await page.Keyboard.PressAsync("ArrowDown");
        await page.WaitForTimeoutAsync(350);

        // The focus moved into the popup's search combobox.
        Assert.AreEqual("model-picker-search",
            await page.EvaluateAsync<string?>("() => document.activeElement?.dataset?.testid ?? null"));

        await page.Keyboard.PressAsync("ArrowDown");
        await page.Keyboard.PressAsync("ArrowDown");
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(350);

        Assert.AreEqual("Model: Claude 3.5 Sonnet · extended thinking: off · effort: not set", await ReadoutAsync(page));
        Assert.AreEqual(0, await Picker(page, "model-picker-basic").Locator("[data-testid='model-picker-list']").CountAsync(),
            "a pick closes the list");

        // The focus went back to the trigger that owned it (WCAG 2.4.3).
        Assert.AreEqual("model-picker-trigger",
            await page.EvaluateAsync<string?>("() => document.activeElement?.dataset?.testid ?? null"));
    }

    [TestMethod]
    public async Task TheReasoningOptions_BelongToThePickedModel()
    {
        var page = await OpenAsync();
        await Trigger(page, "model-picker-basic").ClickAsync();
        await Picker(page, "model-picker-basic").Locator("[data-model-id='claude-3-5-sonnet']").ClickAsync();
        await page.WaitForTimeoutAsync(300);

        await Trigger(page, "model-picker-basic").ClickAsync();
        await page.WaitForSelectorAsync($"{BasicSection} [data-testid='model-picker-thinking']");
        Assert.AreEqual(3,
            await Picker(page, "model-picker-basic").Locator("[data-testid='model-picker-effort'] [role='radio']").CountAsync(),
            "the model's two levels plus the model default");

        // The switch's native checkbox is visually hidden (the track is the visible control), so the tap
        // lands on the label the way a reader's does.
        await Picker(page, "model-picker-basic").Locator("[data-testid='model-picker-thinking'] .tm-toggle-label").ClickAsync();
        await page.WaitForTimeoutAsync(250);
        Assert.AreEqual("Model: Claude 3.5 Sonnet · extended thinking: on · effort: not set", await ReadoutAsync(page));

        await Picker(page, "model-picker-basic")
            .Locator("[data-testid='model-picker-effort'] [data-value='high']").ClickAsync();
        await page.WaitForTimeoutAsync(250);
        Assert.AreEqual("Model: Claude 3.5 Sonnet · extended thinking: on · effort: High", await ReadoutAsync(page));
        Assert.AreEqual("Reasoning effort: High.",
            (await Picker(page, "model-picker-basic").Locator("[data-testid='model-picker-announcement']").TextContentAsync())?.Trim());

        // A model with neither option offers no options block at all.
        await Picker(page, "model-picker-basic").Locator("[data-model-id='gpt-4o-mini']").ClickAsync();
        await page.WaitForTimeoutAsync(300);
        await Trigger(page, "model-picker-basic").ClickAsync();
        await page.WaitForTimeoutAsync(250);
        Assert.AreEqual(0, await Picker(page, "model-picker-basic").Locator("[data-testid='model-picker-options']").CountAsync());
        Assert.AreEqual(0, await Picker(page, "model-picker-basic").Locator("[data-testid='model-picker-thinking']").CountAsync());
    }

    [TestMethod]
    public async Task TheOpenListReportsEveryChange_AndTheVariantsPickIndependently()
    {
        var page = await OpenAsync();
        await page.Locator(EventsSection).ScrollIntoViewIfNeededAsync();

        await Trigger(page, "model-picker-events").ClickAsync();
        await Picker(page, "model-picker-events").Locator("[data-model-id='gpt-4o-mini']").ClickAsync();
        await page.WaitForTimeoutAsync(300);
        Assert.AreEqual(1, await Picker(page, "model-picker-events").Locator("[data-testid='model-picker-list']").CountAsync(),
            "CloseOnSelect=false keeps the list open");

        await Picker(page, "model-picker-events").Locator("[data-model-id='claude-3-5-sonnet']").ClickAsync();
        await page.WaitForTimeoutAsync(300);
        var entries = await page.Locator("[data-testid='model-picker-events-entry']").AllTextContentsAsync();
        Assert.AreEqual(2, entries.Count);
        StringAssert.Contains(entries[0], "Claude 3.5 Sonnet");
        StringAssert.Contains(entries[1], "GPT-4o mini");

        // A second picker on the page keeps its own choice.
        await page.Locator(VariantsSection).ScrollIntoViewIfNeededAsync();
        StringAssert.Contains(await Trigger(page, "model-picker-compact").TextContentAsync(), "Claude 3.5 Sonnet");
        Assert.AreEqual(0,
            await Picker(page, "model-picker-compact").Locator("[data-testid='model-picker-search']").CountAsync());
    }

    [TestMethod]
    public async Task TheEdgeStates_SayWhatTheyAre()
    {
        var page = await OpenAsync();
        await page.Locator(EdgeSection).ScrollIntoViewIfNeededAsync();

        Assert.IsNotNull(await Trigger(page, "model-picker-disabled").GetAttributeAsync("disabled"),
            "the disabled picker's trigger carries the native disabled attribute");
        await Trigger(page, "model-picker-disabled").ClickAsync(new LocatorClickOptions { Force = true });
        await page.WaitForTimeoutAsync(250);
        Assert.AreEqual(0,
            await Picker(page, "model-picker-disabled").Locator("[data-testid='model-picker-list']").CountAsync());

        StringAssert.Contains(await Trigger(page, "model-picker-deprecated").TextContentAsync(), "Deprecated");
        StringAssert.Contains(await Trigger(page, "model-picker-missing").TextContentAsync(), "retired-model");

        await Trigger(page, "model-picker-empty").ClickAsync();
        await page.WaitForTimeoutAsync(250);
        Assert.AreEqual("No models are available.",
            (await Picker(page, "model-picker-empty").Locator("[data-testid='model-picker-empty']").TextContentAsync())?.Trim());

        await Trigger(page, "model-picker-credits").ClickAsync();
        await page.WaitForTimeoutAsync(250);
        Assert.AreEqual("300 credits / 1M input",
            (await Picker(page, "model-picker-credits")
                .Locator("[data-model-id='gpt-4o'] [data-testid='model-picker-option-price']")
                .TextContentAsync())?.Trim());
    }

    [TestMethod]
    public async Task ThePhonePresentsThePopupAsABottomSheet_AndThePageNeverScrollsSideways()
    {
        var page = await OpenAsync(375, 812, coarsePointer: true);

        var scrollWidth = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth");
        Assert.IsTrue(scrollWidth <= 376, $"the page does not scroll sideways (scrollWidth {scrollWidth})");

        var trigger = await Trigger(page, "model-picker-basic").BoundingBoxAsync();
        Assert.IsNotNull(trigger);
        Assert.IsTrue(trigger!.Height >= 43.5, $"the trigger reaches the 44 px touch target (height {trigger.Height})");

        await Trigger(page, "model-picker-basic").ClickAsync();
        var sheet = page.Locator(".tm-overlay-panel-sheet").First;
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        // The sheet is the popup: the same listbox and the same rows live inside it.
        Assert.IsTrue(await sheet.Locator("[data-testid='model-picker-list']").CountAsync() > 0);
        Assert.IsTrue(await sheet.Locator("[data-testid='model-picker-option']").CountAsync() >= 5);

        var option = await sheet.Locator("[data-testid='model-picker-option']").First.BoundingBoxAsync();
        Assert.IsNotNull(option);
        Assert.IsTrue(option!.Height >= 43.5, $"an option row reaches the 44 px touch target (height {option.Height})");

        // Picking from the sheet still reports the whole answer to the page.
        await sheet.Locator("[data-model-id='claude-3-5-sonnet']").ClickAsync();
        await page.WaitForTimeoutAsync(400);
        Assert.AreEqual("Model: Claude 3.5 Sonnet · extended thinking: off · effort: not set", await ReadoutAsync(page));
    }

    [TestMethod]
    public async Task ScopedAxeScan_HasNoCriticalOrSeriousViolations()
    {
        var page = await OpenAsync();
        await page.AddScriptTagAsync(new PageAddScriptTagOptions
        {
            Url = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js"
        });

        // Scan the page with the popup open as well: the listbox and its combobox are the new surface.
        await Trigger(page, "model-picker-basic").ClickAsync();
        await page.WaitForSelectorAsync($"{BasicSection} [data-testid='model-picker-option']");

        foreach (var testId in new[] { "model-picker-basic-section", "model-picker-variants-section", "model-picker-edge-section" })
        {
            var violations = await page.EvaluateAsync<string[]>(
                """
                async (selector) => {
                    const host = document.querySelector(selector);
                    // axe samples the RESOLVED colours: a finite CSS animation/transition still running (the demo's fade-in, a colour
                    // transition, the segmented indicator) paints a blended, low-contrast frame. Wait for them (looping ones,
                    // like the skeleton shimmer, never finish and are not waited for).
                    await Promise.all(document.getAnimations()
                        .filter(a => a.effect && a.effect.getComputedTiming().iterations !== Infinity)
                        .map(a => a.finished.catch(() => { })));
                    await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
                    const result = await axe.run(host, {
                        runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa'] },
                        resultTypes: ['violations']
                    });
                    return result.violations
                        .filter(v => v.impact === 'critical' || v.impact === 'serious')
                        .map(v => `${v.impact}: ${v.id} - ${v.help} (${v.nodes.map(n => n.target.join(' ')).slice(0, 4).join('; ')})`);
                }
                """,
                $"[data-testid='{testId}']");
            Assert.AreEqual(0, violations.Length, $"axe [{testId}]: " + string.Join(" | ", violations));
        }
    }

    [TestMethod]
    public async Task VisualSweep_LightAndDark_DesktopAndPhone()
    {
        var page = await OpenAsync();
        await page.Locator(BasicSection).ScrollIntoViewIfNeededAsync();
        await ShootAsync(page, "model-picker-basic-light-1440");

        // The popup itself, open, in the light theme.
        await Trigger(page, "model-picker-basic").ClickAsync();
        await page.WaitForTimeoutAsync(400);
        await ShootAsync(page, "model-picker-popup-light-1440");
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(250);

        await ToggleThemeAsync(page);
        await page.Locator(BasicSection).ScrollIntoViewIfNeededAsync();
        await ShootAsync(page, "model-picker-basic-dark-1440");
        await Trigger(page, "model-picker-basic").ClickAsync();
        await page.WaitForTimeoutAsync(400);
        await ShootAsync(page, "model-picker-popup-dark-1440");
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(250);

        await ToggleThemeAsync(page);
        await page.Locator(EdgeSection).ScrollIntoViewIfNeededAsync();
        await ShootAsync(page, "model-picker-edge-light-1440");
        await ToggleThemeAsync(page);
        await ShootAsync(page, "model-picker-edge-dark-1440");

        // The phone, with the sheet open — the surface a real reader meets.
        var phone = await OpenAsync(375, 812, coarsePointer: true);
        await phone.Locator(BasicSection).ScrollIntoViewIfNeededAsync();
        await ShootAsync(phone, "model-picker-basic-light-375");
        await Trigger(phone, "model-picker-basic").ClickAsync();
        await phone.WaitForTimeoutAsync(500);
        await ShootAsync(phone, "model-picker-sheet-light-375");
        await phone.Keyboard.PressAsync("Escape");
        await phone.WaitForTimeoutAsync(250);
        await ToggleThemeAsync(phone);
        await Trigger(phone, "model-picker-basic").ClickAsync();
        await phone.WaitForTimeoutAsync(500);
        await ShootAsync(phone, "model-picker-sheet-dark-375");
    }

    /// <summary>Switches the demo's own theme (the header toggle), so the whole frame repaints.</summary>
    private static async Task ToggleThemeAsync(IPage page)
    {
        await page.Locator("[data-testid='theme-toggle']:visible").First.ClickAsync();
        await page.WaitForTimeoutAsync(600);
    }

    /// <summary>
    /// Frames the section that is being reviewed, in the viewport: a full-page shot of a long demo page simply
    /// frames the top of the page and proves nothing about this component.
    /// </summary>
    private async Task ShootAsync(IPage page, string name)
    {
        await page.WaitForTimeoutAsync(300);
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync(
            "() => document.activeElement instanceof HTMLElement && document.activeElement.blur()");
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(ShotDir, $"{name}.png"),
            FullPage = false,
        });
    }
}

/// <summary>The WASM lane (the demo default).</summary>
[TestClass]
public class ModelPickerE2ETests : ModelPickerE2EBase
{
    protected override string BaseUrl =>
        Environment.GetEnvironmentVariable("TM_E2E_WASM_URL") ?? "https://localhost:7106";
}
