using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E.Feedback;

/// <summary>
/// Review round 6, against real hosts: opening a Prompt dialog with Enter must not submit it.
/// The opener activates on its Enter keydown, the per-type initial focus lands on the input while
/// the key is still down, and the trailing keyup then landed on the input — the old keyup submit
/// fired the DefaultValue at once, so a keyboard user never saw the prompt (M-r6-1). After the
/// fix the submit happens on the input's Enter keydown (guarded against auto-repeat), and the
/// keyup of the opening press is spent.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class OverlayRound6E2ETests : WasmTestBase
{
    private static readonly string ShotDir =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TestResults", "overlay-round6");

    private async Task<IPage> OpenAsync(int width, int height = 1000)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            HasTouch = width < 1024,
            ViewportSize = new ViewportSize { Width = width, Height = height },
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        var page = await context.NewPageAsync();
        return page;
    }

    private async Task GotoAsync(IPage page, string path)
    {
        await page.GotoAsync($"{BaseUrl}{path}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 90000 });
        await WaitForAppReadyAsync(page);
    }

    private static async Task<string> ShootAsync(IPage page, string name)
    {
        Directory.CreateDirectory(ShotDir);
        var path = Path.Combine(ShotDir, $"{name}.png");
        // FullPage resets touch emulation; a viewport shot keeps (pointer: coarse) honest.
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = path,
            FullPage = false,
            Timeout = 60_000,
        });
        return path;
    }

    private static async Task<string> ActiveElementAsync(IPage page)
    {
        return await page.EvaluateAsync<string>(
            """
            () => {
                const a = document.activeElement;
                if (!a) return '(null)';
                return a.tagName.toLowerCase() + '.' + (a.className || '').toString().trim().split(/\s+/).join('.');
            }
            """);
    }

    [TestMethod]
    [DataRow(390)]
    [DataRow(1440)]
    public async Task PromptDialog_EnterOnTheOpener_StaysOpenWithTheDefaultValue_ThenTypedEnterSubmits(int width)
    {
        var page = await OpenAsync(width);
        await GotoAsync(page, "/modal-dialog");

        // Open the way a keyboard user does: focus the opener and press Enter. One press must
        // activate exactly once — opening the dialog, not opening AND submitting "John Doe".
        var opener = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Enter Name" });
        await opener.ScrollIntoViewIfNeededAsync();
        await opener.FocusAsync();
        await page.Keyboard.PressAsync("Enter");

        await page.Locator(".tm-dialog").WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await page.WaitForFunctionAsync(
            "() => document.activeElement && document.activeElement.tagName === 'INPUT' && !!document.activeElement.closest('.tm-dialog-content')",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });

        // The trailing keyup needs time to (wrongly) submit if the defect were still there.
        await page.WaitForTimeoutAsync(400);

        var open = await page.Locator(".tm-modal-overlay").IsVisibleAsync();
        var active = await ActiveElementAsync(page);
        var value = await page.Locator("input.tm-dialog-input").InputValueAsync();
        var banners = await page.GetByText("You entered:").CountAsync();
        TestContext.WriteLine($"prompt {width}: open={open} focus={active} value='{value}' banners={banners}");

        Assert.IsTrue(open, $"the dialog must stay open after the opener's Enter at {width}px");
        Assert.IsTrue(active.Contains("tm-dialog-input"), $"focus must rest on the prompt input, got '{active}'");
        Assert.AreEqual("John Doe", value, "the untouched DefaultValue must still sit in the input");
        Assert.AreEqual(0, banners, "the keyup of the opening press must not submit the DefaultValue");

        await ShootAsync(page, $"{width}-prompt-kbd-open-holds-value");

        // The user edits the value and confirms with Enter — exactly one submit of the typed text.
        await page.Keyboard.PressAsync("Control+a");
        await page.Keyboard.TypeAsync("Pavel Novák");
        await page.Keyboard.PressAsync("Enter");
        await page.Locator(".tm-modal-overlay").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 10000 });

        var banner = page.GetByText("You entered:").First;
        await banner.ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(banner).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        await Assertions.Expect(page.GetByText("Pavel Novák").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

        await ShootAsync(page, $"{width}-prompt-kbd-typed-enter-submits");
    }
}
