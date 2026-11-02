using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 4: mention picker and the internal switch on the /activity composer.
/// The demo wires a local <c>ITmPeopleProvider</c> and <c>ShowInternalToggle</c>.
/// The picker opens from a real typed "@" (<c>PressSequentiallyAsync</c>); Arrow/Enter are real keys.
/// Committed captures land in <c>__screenshots__/activity-comments/fze-4/</c>.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public class ActivityCommentsComposerE2ETests : WasmTestBase
{
    private const string SectionSelector = "[data-testid='activity-comments-section']";
    private const string ThemeToggleSelector = "[data-testid='theme-toggle']";

    private static string OutputDirectory =>
        Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "__screenshots__", "activity-comments", "fze-4");

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

    private async Task CaptureAsync(IPage page, string name, string? selector = null)
    {
        await page.Mouse.MoveAsync(0, 0);
        await page.Locator($"{SectionSelector} .tm-rte-editor-content").EvaluateAsync(
            "el => el.scrollIntoView({ block: 'start' })");
        var target = selector ?? $"{SectionSelector} .tm-comments";
        await WaitForLayoutToSettleAsync(page, target);
        Directory.CreateDirectory(OutputDirectory);
        var path = Path.Combine(OutputDirectory, $"{name}.png");
        // A null selector captures the page: the mention list hangs below the editor and a
        // locator shot of the composer clips it.
        if (selector is null)
        {
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = false });
        }
        else
        {
            await page.Locator(target).ScreenshotAsync(new LocatorScreenshotOptions
            {
                Path = path,
                Timeout = 60_000
            });
        }
        Assert.IsTrue(File.Exists(path), $"the screenshot was not written: {path}");
        TestContext.AddResultFile(path);
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

    private static async Task OpenComposerAsync(IPage page)
    {
        await page.Locator($"{SectionSelector} .tm-comments-add-btn").ClickAsync();
        await Assertions.Expect(Composer(page)).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task MentionPicker_Keyboard_MovesHighlight_AndEnterSelects()
    {
        var page = await OpenActivityAsync();
        await OpenComposerAsync(page);
        await TypeMentionQueryAsync(page, "@al");

        var list = page.Locator($"{SectionSelector} .tm-rte-mention-dropdown");
        await Assertions.Expect(list).ToBeVisibleAsync();
        await Assertions.Expect(list).ToHaveAttributeAsync("role", "listbox");
        await Assertions.Expect(list.Locator(".tm-rte-mention-user").First)
            .ToHaveClassAsync(new System.Text.RegularExpressions.Regex("tm-rte-mention-highlighted"));

        await page.Locator($"{SectionSelector} .tm-rte-editor-content").PressAsync("ArrowDown");
        await Assertions.Expect(list.Locator(".tm-rte-mention-user").Nth(1))
            .ToHaveClassAsync(new System.Text.RegularExpressions.Regex("tm-rte-mention-highlighted"));
        await CaptureAsync(page, "mentions-highlight-light");

        await page.Locator($"{SectionSelector} .tm-rte-editor-content").PressAsync("Enter");
        await Assertions.Expect(list).ToHaveCountAsync(0);
    }

    [TestMethod]
    public async Task MentionPicker_NoResults_AndLongName_LightDark()
    {
        var page = await OpenActivityAsync();
        await OpenComposerAsync(page);

        await TypeMentionQueryAsync(page, "@");
        var list = page.Locator($"{SectionSelector} .tm-rte-mention-dropdown");
        await Assertions.Expect(list).ToBeVisibleAsync();
        await Assertions.Expect(list.GetByText("Alexandrina Montgomery-Featherstone")).ToBeVisibleAsync();
        await CaptureAsync(page, "mentions-long-name-light");

        await page.Locator($"{SectionSelector} .tm-rte-editor-content").PressAsync("Escape");
        await Assertions.Expect(list).ToHaveCountAsync(0);

        // No match: the list stays open and says so. Colour is not the cue.
        await TypeMentionQueryAsync(page, "@zzz");
        await Assertions.Expect(list).ToBeVisibleAsync();
        await Assertions.Expect(list.Locator(".tm-rte-mention-empty")).ToBeVisibleAsync();
        await CaptureAsync(page, "mentions-empty-light");
        await page.Locator($"{SectionSelector} .tm-rte-editor-content").PressAsync("Escape");
        await TypeMentionQueryAsync(page, "@");
        await Assertions.Expect(list).ToBeVisibleAsync();

        await SetDarkAsync(page);
        await CaptureAsync(page, "mentions-long-name-dark");
    }

    [TestMethod]
    public async Task MentionPicker_Mobile390_LightDark_AndHighlightDark()
    {
        var page = await OpenActivityAsync(width: 390, height: 844);
        await OpenComposerAsync(page);
        await TypeMentionQueryAsync(page, "@");

        var list = page.Locator($"{SectionSelector} .tm-rte-mention-dropdown");
        await Assertions.Expect(list).ToBeVisibleAsync();
        await CaptureAsync(page, "mentions-open-light-390");

        await SetDarkAsync(page);
        await CaptureAsync(page, "mentions-open-dark-390");

        // The theme toggle takes focus, so the highlight key goes after the switch.
        await page.Locator($"{SectionSelector} .tm-rte-editor-content").PressAsync("ArrowDown");
        await Assertions.Expect(list.Locator(".tm-rte-mention-user").Nth(1))
            .ToHaveClassAsync(new System.Text.RegularExpressions.Regex("tm-rte-mention-highlighted"));
        await CaptureAsync(page, "mentions-highlight-dark");
    }

    [TestMethod]
    public async Task InternalToggle_Submit_ShowsInternalComment_AndResets()
    {
        var page = await OpenActivityAsync();
        var before = await page.Locator($"{SectionSelector} [data-comment-id]").CountAsync();

        await OpenComposerAsync(page);
        var toggle = page.Locator($"{SectionSelector} .tm-comment-internal-toggle input");
        var toggleLabel = page.Locator($"{SectionSelector} .tm-comment-internal-toggle .tm-toggle-label");
        await Assertions.Expect(toggle).ToHaveAttributeAsync("role", "switch");
        await Assertions.Expect(Composer(page)).Not.ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("tm-comment-editor--internal"));

        await toggleLabel.ClickAsync();
        await Assertions.Expect(Composer(page)).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("tm-comment-editor--internal"));
        await Assertions.Expect(page.GetByTestId("activity-comments-draft-internal")).ToHaveTextAsync("yes");
        await CaptureAsync(page, "composer-internal-light", $"{SectionSelector} .tm-comments");

        // Empty body with the switch on posts nothing and leaves the switch on.
        await page.Locator($"{SectionSelector} .tm-comment-submit-btn").ClickAsync();
        await Assertions.Expect(page.Locator($"{SectionSelector} [data-comment-id]")).ToHaveCountAsync(before);
        await Assertions.Expect(toggle).ToBeCheckedAsync();

        await page.Locator($"{SectionSelector} .tm-rte-editor-content").FillAsync("Internal note from the composer");
        await page.Locator($"{SectionSelector} .tm-comment-submit-btn").ClickAsync();

        var posted = page.Locator($"{SectionSelector} [data-comment-id]").Nth(before);
        await Assertions.Expect(posted).ToHaveAttributeAsync("data-internal", "true");
        await Assertions.Expect(posted).ToContainTextAsync("Internal note from the composer");
        await Assertions.Expect(posted.Locator(".tm-comment-internal-label")).ToBeVisibleAsync();

        // The switch resets; OnCommentAdded still appended the comment.
        await OpenComposerAsync(page);
        await Assertions.Expect(page.Locator($"{SectionSelector} .tm-comment-internal-toggle input")).Not.ToBeCheckedAsync();
        await Assertions.Expect(page.Locator($"{SectionSelector} [data-comment-id]")).ToHaveCountAsync(before + 1);
        await CaptureAsync(page, "posted-internal-light", $"{SectionSelector} .tm-comments");
    }

    [TestMethod]
    public async Task InternalComposer_Dark_AndMobile390()
    {
        var page = await OpenActivityAsync(width: 390, height: 844);
        await OpenComposerAsync(page);
        await page.Locator($"{SectionSelector} .tm-comment-internal-toggle .tm-toggle-label").ClickAsync();

        var editor = Composer(page);
        var box = await editor.BoundingBoxAsync();
        Assert.IsNotNull(box);
        Assert.IsTrue(box!.Width <= 390, $"composer wider than the viewport: {box.Width}");

        await SetDarkAsync(page);
        await CaptureAsync(page, "composer-internal-dark-390", $"{SectionSelector} .tm-comments");

        // Double-click must not post twice. Fill, then click submit twice in a burst.
        await page.Locator($"{SectionSelector} .tm-rte-editor-content").FillAsync("One internal post");
        var submit = page.Locator($"{SectionSelector} .tm-comment-submit-btn");
        await submit.ClickAsync();
        if (await submit.CountAsync() > 0)
        {
            await submit.ClickAsync();
        }

        await Assertions.Expect(page.Locator($"{SectionSelector} .tm-comment-item").Filter(
            new LocatorFilterOptions { HasText = "One internal post" })).ToHaveCountAsync(1);
    }

    [TestMethod]
    public async Task InlineEdit_HasNoInternalToggle_ButKeepsMentions()
    {
        var page = await OpenActivityAsync();
        await page.Locator($"{SectionSelector} [data-comment-id='demo-c1'] .tm-comment-edit-btn").ClickAsync();

        var editing = page.Locator($"{SectionSelector} [data-comment-id='demo-c1'] .tm-comment-editor");
        await Assertions.Expect(editing).ToBeVisibleAsync();
        await Assertions.Expect(editing.Locator(".tm-comment-internal-toggle")).ToHaveCountAsync(0);

        await page.Locator($"{SectionSelector} .tm-rte-editor-content").PressSequentiallyAsync("@al");
        await Assertions.Expect(editing.Locator(".tm-rte-mention-dropdown")).ToBeVisibleAsync();
        await page.Locator($"{SectionSelector} .tm-rte-editor-content").PressAsync("Escape");
        await Assertions.Expect(editing.Locator(".tm-rte-mention-dropdown")).ToHaveCountAsync(0);
    }

    private static async Task TypeMentionQueryAsync(IPage page, string text)
    {
        var editor = page.Locator($"{SectionSelector} .tm-rte-editor-content");
        await editor.ClickAsync();
        await editor.PressSequentiallyAsync(text, new LocatorPressSequentiallyOptions { Delay = 30 });
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
