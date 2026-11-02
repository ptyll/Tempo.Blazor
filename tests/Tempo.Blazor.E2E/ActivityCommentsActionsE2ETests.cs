using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 3: the host-owned <c>CommentActions</c> slot on the /activity standalone thread.
/// The demo puts a lock button in the slot and confirms the internal/public flip in a
/// <c>TmDialog</c>. The component never hides an internal comment — the dialog warning says so.
/// Committed captures land in <c>__screenshots__/activity-comments/fze-3/</c>.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public class ActivityCommentsActionsE2ETests : WasmTestBase
{
    private const string SectionSelector = "[data-testid='activity-comments-section']";
    private const string ThemeToggleSelector = "[data-testid='theme-toggle']";
    private const string DialogSelector = "[data-testid='activity-comments-visibility-dialog']";

    private static string OutputDirectory =>
        Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "__screenshots__", "activity-comments", "fze-3");

    private async Task<IPage> OpenActivityAsync(int width = 1440, int height = 900)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);

        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/activity",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await Assertions.Expect(page.Locator($"{SectionSelector} [data-comment-id='demo-c1']"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
        return page;
    }

    private Task CaptureCommentsAsync(IPage page, string name)
        => CaptureAsync(page, name, ".tm-comments");

    private async Task CaptureAsync(IPage page, string name, string? selector = null)
    {
        // Park the pointer so a sticky header or a leftover tooltip is not in the shot.
        await page.Mouse.MoveAsync(0, 0);
        var target = selector ?? SectionSelector;
        await WaitForLayoutToSettleAsync(page, target);
        Directory.CreateDirectory(OutputDirectory);
        var path = Path.Combine(OutputDirectory, $"{name}.png");
        await page.Locator(target).ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = path,
            Timeout = 60_000
        });
        Assert.IsTrue(File.Exists(path), $"the screenshot was not written: {path}");
        TestContext.AddResultFile(path);
    }

    private static ILocator Toggle(IPage page, string id)
        => page.Locator($"[data-testid='activity-comments-toggle-{id}']");

    [TestMethod]
    [TestCategory("Smoke")]
    public async Task ToggleInternal_ConfirmFlipsDataInternal_CancelLeavesIt()
    {
        var page = await OpenActivityAsync();

        // Edge: a comment with no edit/delete rights still gets the host action.
        var readOnly = page.Locator($"{SectionSelector} [data-comment-id='demo-c3']");
        await Assertions.Expect(readOnly.Locator(".tm-comment-edit-btn")).ToHaveCountAsync(0);
        await Assertions.Expect(readOnly.Locator(".tm-comment-delete-btn")).ToHaveCountAsync(0);
        await Assertions.Expect(readOnly.Locator(".tm-comment-actions__custom")).ToBeVisibleAsync();
        await Assertions.Expect(Toggle(page, "demo-c3")).ToBeVisibleAsync();

        // The slot follows the built-in buttons and does not steal their labels.
        var internalItem = page.Locator($"{SectionSelector} [data-comment-id='demo-c1']");
        await Assertions.Expect(internalItem).ToHaveAttributeAsync("data-internal", "true");
        await Assertions.Expect(internalItem.Locator(".tm-comment-edit-btn")).ToBeVisibleAsync();
        await Assertions.Expect(Toggle(page, "demo-c1")).ToHaveAttributeAsync("aria-label", "Make public");
        await CaptureCommentsAsync(page, "actions-in-row-internal-and-readonly-light");

        // Cancel must not flip visibility, and focus returns to the trigger.
        await Toggle(page, "demo-c1").ClickAsync();
        var dialog = page.Locator(DialogSelector);
        await Assertions.Expect(dialog).ToBeVisibleAsync();
        await Assertions.Expect(dialog).ToContainTextAsync("Make this comment public?");
        await Assertions.Expect(dialog).ToContainTextAsync("Notifications already sent cannot be recalled.");
        await CaptureAsync(page, "visibility-dialog-light", DialogSelector);

        await dialog.Locator(".tm-dialog-btn-cancel").ClickAsync();
        await Assertions.Expect(dialog).ToBeHiddenAsync();
        await Assertions.Expect(internalItem).ToHaveAttributeAsync("data-internal", "true");
        await Assertions.Expect(Toggle(page, "demo-c1")).ToBeFocusedAsync();

        // Confirm flips the painted state. The component still shows the comment.
        await Toggle(page, "demo-c1").ClickAsync();
        await dialog.Locator(".tm-dialog-btn-ok").ClickAsync();
        await Assertions.Expect(dialog).ToBeHiddenAsync();
        await Assertions.Expect(internalItem).ToHaveAttributeAsync("data-internal", "false");
        await Assertions.Expect(internalItem.Locator(".tm-comment-internal-label")).ToHaveCountAsync(0);
        await Assertions.Expect(internalItem).ToBeVisibleAsync();
        await Assertions.Expect(Toggle(page, "demo-c1")).ToHaveAttributeAsync("aria-label", "Make internal");
        await CaptureCommentsAsync(page, "after-toggle-public-light");

        await page.Locator($"{ThemeToggleSelector}:visible").First.ClickAsync();
        await Assertions.Expect(page.Locator(".demo-shell"))
            .ToHaveAttributeAsync("data-theme", "dark", new LocatorAssertionsToHaveAttributeOptions { Timeout = 15000 });
        // Park the pointer so a sticky header or tooltip is not in the capture.
        await CaptureCommentsAsync(page, "after-toggle-public-dark");
    }

    [TestMethod]
    public async Task Keyboard_EnterActivatesOnce_AndNarrowRowWraps()
    {
        var page = await OpenActivityAsync(width: 390, height: 844);
        var publicItem = page.Locator($"{SectionSelector} [data-comment-id='demo-c2']");
        await Assertions.Expect(publicItem).ToHaveAttributeAsync("data-internal", "false");
        await CaptureCommentsAsync(page, "actions-in-row-light-390");

        // Native button: Enter must open the dialog exactly once (no second dialog, no double flip).
        await Toggle(page, "demo-c2").FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        var dialog = page.Locator(DialogSelector);
        await Assertions.Expect(dialog).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".tm-dialog")).ToHaveCountAsync(1);
        await Assertions.Expect(dialog).ToContainTextAsync("Make this comment internal?");
        await CaptureAsync(page, "visibility-dialog-light-390", DialogSelector);

        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(dialog).ToBeHiddenAsync();
        await Assertions.Expect(publicItem).ToHaveAttributeAsync("data-internal", "false");
        await Assertions.Expect(Toggle(page, "demo-c2")).ToBeFocusedAsync();

        // Space on a native button activates on keyup — one dialog, then confirm.
        await page.Keyboard.PressAsync(" ");
        await Assertions.Expect(dialog).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".tm-dialog")).ToHaveCountAsync(1);
        await dialog.Locator(".tm-dialog-btn-ok").ClickAsync();
        await Assertions.Expect(publicItem).ToHaveAttributeAsync("data-internal", "true");
        await Assertions.Expect(publicItem.Locator(".tm-comment-internal-label")).ToBeVisibleAsync();
        await CaptureCommentsAsync(page, "after-toggle-internal-light-390");

        await page.Locator($"{ThemeToggleSelector}:visible").First.ClickAsync();
        await Assertions.Expect(page.Locator(".demo-shell"))
            .ToHaveAttributeAsync("data-theme", "dark", new LocatorAssertionsToHaveAttributeOptions { Timeout = 15000 });
        await CaptureCommentsAsync(page, "actions-in-row-dark-390");
    }

    [TestMethod]
    public async Task Editing_HidesActionsOnThatCommentOnly()
    {
        var page = await OpenActivityAsync();
        var editing = page.Locator($"{SectionSelector} [data-comment-id='demo-c1']");
        await editing.Locator(".tm-comment-edit-btn").ClickAsync();
        await Assertions.Expect(editing.Locator(".tm-comment-editor")).ToBeVisibleAsync();
        await Assertions.Expect(editing.Locator(".tm-comment-actions")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator($"{SectionSelector} [data-comment-id='demo-c2'] .tm-comment-actions__custom"))
            .ToBeVisibleAsync();
        await CaptureCommentsAsync(page, "actions-hidden-while-editing-light");
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
