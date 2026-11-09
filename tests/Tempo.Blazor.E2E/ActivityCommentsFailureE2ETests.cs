using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 2 (TC4): failed-save simulation on the /activity standalone thread.
/// The demo routes posting through <c>CommentSubmitHandler</c>/<c>CommentEditHandler</c>
/// returning <c>false</c> while the "Simulate failed save" toggle is on: the draft must
/// survive (text + internal-switch state), the reason shows in a <c>TmAlert</c>, the
/// submit button wears <c>aria-disabled="true"</c> while the save is in flight, and a
/// retry after turning the simulation off succeeds and closes the editor.
/// Captures land in <c>__screenshots__/activity-comments/fze-2/</c> (light/dark × 1440/390,
/// taken at the post-failure state); every capture asserts <c>#blazor-error-ui</c> stays
/// hidden and the PNG is a real capture (&gt;2 KB).
/// </summary>
[TestClass]
[TestCategory("WASM")]
public class ActivityCommentsFailureE2ETests : WasmTestBase
{
    private const string SectionSelector = "[data-testid='activity-comments-section']";
    private const string ThemeToggleSelector = "[data-testid='theme-toggle']";
    private const string SimulateToggleSelector = "[data-testid='activity-comments-simulate-failure']";

    private static string OutputDirectory =>
        Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "__screenshots__", "activity-comments", "fze-2");

    private async Task<IPage> OpenActivityAsync(int width = 1440, int height = 1100)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);

        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/activity#activity-comments",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await Assertions.Expect(page.Locator($"{SectionSelector} [data-comment-id='demo-c1']"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
        return page;
    }

    private static async Task AssertNoBlazorErrorAsync(IPage page)
    {
        await Assertions.Expect(page.Locator("#blazor-error-ui"))
            .ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 5000 });
    }

    private async Task CaptureAsync(IPage page, string name)
    {
        await page.Mouse.MoveAsync(0, 0);
        // The section (not just .tm-comments): the consumer's TmAlert, the simulation
        // toggle, the draft-state panel and the composer must all be in the frame.
        var target = SectionSelector;
        // The demo chrome — the sticky desktop top bar ([data-demo-shell=main] > div.sticky)
        // and the fixed mobile header (.demo-shell > div.fixed) — would overlap the section
        // in an element capture: Playwright pins the element to the viewport top, where the
        // chrome sits. Freeze both to static for the shot and restore them afterwards.
        const string freezeChrome =
            "document.querySelectorAll('[data-demo-shell=\"main\"] > div.sticky, .demo-shell > div.fixed')" +
            ".forEach(el => el.style.setProperty('position', 'static', 'important'));";
        const string restoreChrome =
            "document.querySelectorAll('[data-demo-shell=\"main\"] > div.sticky, .demo-shell > div.fixed')" +
            ".forEach(el => el.style.removeProperty('position'));";
        await page.EvaluateAsync(freezeChrome);
        try
        {
            await page.Locator(target).EvaluateAsync("el => el.scrollIntoView({ block: 'start' })");
            await WaitForLayoutToSettleAsync(page, target);
            Directory.CreateDirectory(OutputDirectory);
            var path = Path.Combine(OutputDirectory, $"{name}.png");
            await page.Locator(target).ScreenshotAsync(new LocatorScreenshotOptions
            {
                Path = path,
                Timeout = 60_000
            });
            Assert.IsTrue(File.Exists(path), $"the screenshot was not written: {path}");
            Assert.IsTrue(new FileInfo(path).Length > 2 * 1024, $"screenshot too small (not a real capture): {path}");
            await AssertNoBlazorErrorAsync(page);
            TestContext.AddResultFile(path);
        }
        finally
        {
            await page.EvaluateAsync(restoreChrome);
        }
    }

    private static async Task SetDarkAsync(IPage page)
    {
        var toggle = page.Locator($"{ThemeToggleSelector}:visible").First;
        if (await toggle.GetAttributeAsync("aria-pressed") != "true")
        {
            await toggle.ClickAsync();
        }

        await Assertions.Expect(page.Locator(".demo-shell"))
            .ToHaveAttributeAsync("data-theme", "dark", new LocatorAssertionsToHaveAttributeOptions { Timeout = 15000 });
    }

    private static ILocator Composer(IPage page) => page.Locator($"{SectionSelector} .tm-comments > .tm-comment-editor");

    private static async Task EnableSimulationAsync(IPage page)
    {
        var toggle = page.Locator($"{SimulateToggleSelector} input");
        await Assertions.Expect(toggle).ToHaveAttributeAsync("role", "switch");
        if (!await toggle.IsCheckedAsync())
        {
            await page.Locator($"{SimulateToggleSelector} .tm-toggle-label").ClickAsync();
        }

        await Assertions.Expect(toggle).ToBeCheckedAsync();
    }

    private static async Task DisableSimulationAsync(IPage page)
    {
        var toggle = page.Locator($"{SimulateToggleSelector} input");
        if (await toggle.IsCheckedAsync())
        {
            await page.Locator($"{SimulateToggleSelector} .tm-toggle-label").ClickAsync();
        }

        await Assertions.Expect(toggle).Not.ToBeCheckedAsync();
        // Turning the simulation off clears the demo's failure alerts.
        await Assertions.Expect(page.Locator("[data-testid='activity-comments-save-failed']")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("[data-testid='activity-comments-edit-failed']")).ToHaveCountAsync(0);
    }

    private static async Task OpenComposerAsync(IPage page)
    {
        await page.Locator($"{SectionSelector} .tm-comments-add-btn").ClickAsync();
        await Assertions.Expect(Composer(page)).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task Comments_SubmitFailed_KeepsTextAndToggle()
    {
        var page = await OpenActivityAsync();
        var countBefore = await page.Locator($"{SectionSelector} [data-comment-id]").CountAsync();

        await EnableSimulationAsync(page);
        await OpenComposerAsync(page);

        var internalToggle = page.Locator($"{SectionSelector} .tm-comment-internal-toggle input");
        await page.Locator($"{SectionSelector} .tm-comment-internal-toggle .tm-toggle-label").ClickAsync();
        await Assertions.Expect(internalToggle).ToBeCheckedAsync();

        await page.Locator($"{SectionSelector} .tm-rte-editor-content").FillAsync("Draft that the server will reject");
        await page.Locator($"{SectionSelector} .tm-comment-submit-btn").ClickAsync();

        // The consumer-presented reason shows up; the composer stays open with everything intact.
        var alert = page.Locator("[data-testid='activity-comments-save-failed']");
        await Assertions.Expect(alert).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        await Assertions.Expect(alert).ToContainTextAsync("rejected");

        await Assertions.Expect(Composer(page)).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator($"{SectionSelector} .tm-rte-editor-content"))
            .ToContainTextAsync("Draft that the server will reject");
        await Assertions.Expect(internalToggle).ToBeCheckedAsync();
        await Assertions.Expect(page.Locator($"{SectionSelector} [data-comment-id]")).ToHaveCountAsync(countBefore);
        // The host-visible draft state still reports the unsaved draft.
        await Assertions.Expect(page.GetByTestId("activity-comments-draft-unsaved")).ToHaveTextAsync("yes");
        await AssertNoBlazorErrorAsync(page);

        await CaptureAsync(page, "submit-failed-light-1440");
        await SetDarkAsync(page);
        await CaptureAsync(page, "submit-failed-dark-1440");
    }

    [TestMethod]
    public async Task Comments_SubmitFailed_ThenRetrySucceeds_Closes()
    {
        var page = await OpenActivityAsync();
        var countBefore = await page.Locator($"{SectionSelector} [data-comment-id]").CountAsync();

        await EnableSimulationAsync(page);
        await OpenComposerAsync(page);
        await page.Locator($"{SectionSelector} .tm-rte-editor-content").FillAsync("Retry me");
        await page.Locator($"{SectionSelector} .tm-comment-submit-btn").ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid='activity-comments-save-failed']"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        // The failed save left the text in the editor — the retry reuses the same draft.
        await Assertions.Expect(page.Locator($"{SectionSelector} .tm-rte-editor-content")).ToContainTextAsync("Retry me");

        await DisableSimulationAsync(page);
        await page.Locator($"{SectionSelector} .tm-comment-submit-btn").ClickAsync();

        // Success: the composer closes and the comment posts exactly once.
        await Assertions.Expect(Composer(page)).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator($"{SectionSelector} [data-comment-id]")).ToHaveCountAsync(countBefore + 1);
        await Assertions.Expect(page.Locator($"{SectionSelector} [data-comment-id]").Filter(
            new LocatorFilterOptions { HasText = "Retry me" })).ToHaveCountAsync(1);
        await Assertions.Expect(page.GetByTestId("activity-comments-draft-unsaved")).ToHaveTextAsync("no");
        await AssertNoBlazorErrorAsync(page);
    }

    [TestMethod]
    public async Task Comments_EditFailed_KeepsInlineEditor()
    {
        var page = await OpenActivityAsync();

        await EnableSimulationAsync(page);
        await page.Locator($"{SectionSelector} [data-comment-id='demo-c1'] .tm-comment-edit-btn").ClickAsync();

        var editor = page.Locator($"{SectionSelector} [data-comment-id='demo-c1'] .tm-comment-editor");
        await Assertions.Expect(editor).ToBeVisibleAsync();
        var content = editor.Locator(".tm-rte-editor-content");
        await content.FillAsync("Edited body that the server will reject");
        await editor.Locator(".tm-comment-submit-btn").ClickAsync();

        // The inline editor stays open with the edited text; the reason shows in an alert.
        var alert = page.Locator("[data-testid='activity-comments-edit-failed']");
        await Assertions.Expect(alert).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        await Assertions.Expect(editor).ToBeVisibleAsync();
        await Assertions.Expect(content).ToContainTextAsync("Edited body that the server will reject");
        // The host-visible draft state reports the inline edit; the stored body only changes
        // after a successful save (.tm-comment-content is not mounted while editing).
        await Assertions.Expect(page.GetByTestId("activity-comments-draft-editing")).ToHaveTextAsync("demo-c1");
        await Assertions.Expect(page.GetByTestId("activity-comments-draft-unsaved")).ToHaveTextAsync("yes");
        await AssertNoBlazorErrorAsync(page);

        // Turning the simulation off and saving again applies the edit and closes the editor.
        await DisableSimulationAsync(page);
        await editor.Locator(".tm-comment-submit-btn").ClickAsync();
        await Assertions.Expect(editor).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator($"{SectionSelector} [data-comment-id='demo-c1'] .tm-comment-content"))
            .ToContainTextAsync("Edited body that the server will reject");
        await AssertNoBlazorErrorAsync(page);
    }

    [TestMethod]
    public async Task Comments_SubmitInProgress_ButtonDisabled()
    {
        var page = await OpenActivityAsync();

        await EnableSimulationAsync(page); // the simulated save waits 500 ms before failing
        await OpenComposerAsync(page);
        await page.Locator($"{SectionSelector} .tm-rte-editor-content").FillAsync("In flight");

        var submit = page.Locator($"{SectionSelector} .tm-comment-submit-btn");
        await submit.ClickAsync();

        // While the save is in flight the button carries aria-disabled (focus is NOT dropped);
        // the click guard prevents a second post.
        await Assertions.Expect(submit).ToHaveAttributeAsync("aria-disabled", "true");
        await Assertions.Expect(page.Locator($"{SectionSelector} .tm-comment-internal-toggle input"))
            .ToBeDisabledAsync();
        await submit.ClickAsync();

        // The simulated failure lands; the button is interactive again and the draft survived.
        // The save ran exactly once — the second click while in flight never reached the
        // handler (the demo counts each handler invocation).
        await Assertions.Expect(page.Locator("[data-testid='activity-comments-save-failed']"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        await Assertions.Expect(page.GetByTestId("activity-comments-save-attempts"))
            .ToHaveTextAsync("save attempts: 1");
        await Assertions.Expect(submit).Not.ToHaveAttributeAsync("aria-disabled", "true");
        await Assertions.Expect(Composer(page)).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator($"{SectionSelector} .tm-rte-editor-content")).ToContainTextAsync("In flight");
        await AssertNoBlazorErrorAsync(page);
    }

    [TestMethod]
    public async Task Comments_TwoConsecutiveFailures_KeepDraftAndAlert()
    {
        var page = await OpenActivityAsync(width: 390, height: 844);
        var countBefore = await page.Locator($"{SectionSelector} [data-comment-id]").CountAsync();

        await EnableSimulationAsync(page);
        await OpenComposerAsync(page);

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var submit = page.Locator($"{SectionSelector} .tm-comment-submit-btn");
            if (attempt == 1)
            {
                await page.Locator($"{SectionSelector} .tm-rte-editor-content").FillAsync("First attempt draft");
            }
            else
            {
                await page.Locator($"{SectionSelector} .tm-rte-editor-content").FillAsync("Second attempt draft");
            }

            await submit.ClickAsync();
            await Assertions.Expect(page.Locator("[data-testid='activity-comments-save-failed']"))
                .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

            // The draft is still editable after each failure — nothing was discarded.
            await Assertions.Expect(Composer(page)).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByTestId("activity-comments-draft-unsaved")).ToHaveTextAsync("yes");
            await AssertNoBlazorErrorAsync(page);
        }

        await Assertions.Expect(page.Locator($"{SectionSelector} .tm-rte-editor-content"))
            .ToContainTextAsync("Second attempt draft");
        await Assertions.Expect(page.Locator($"{SectionSelector} [data-comment-id]")).ToHaveCountAsync(countBefore);

        await CaptureAsync(page, "submit-failed-light-390");
        await SetDarkAsync(page);
        await CaptureAsync(page, "submit-failed-dark-390");
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
