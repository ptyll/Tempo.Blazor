using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E.Feedback;

/// <summary>
/// E2E debt carried from the F2 reviews (remaining task 1525e5c8):
///  1. nested-dialog focus-on-opener — closing a dialog that was opened from inside a sheet must
///     return focus to the opener button, not drop it on the body;
///  2. dialog prompt keyboard lift — a Prompt dialog presented as a sheet must lift above a
///     (fake) on-screen keyboard so its input stays visible;
///  3. FR labels at 320 in TmDialog — with the dialog forced to its dialog presentation (never a
///     sheet), 36-char French labels wrap before they truncate.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class DialogF2DebtE2ETests : WasmTestBase
{
    private static readonly string ShotDir =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TestResults", "dialog-f2-debt");

    private async Task<IPage> OpenAsync(string route, int width, int height = 844, string culture = "en")
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            HasTouch = true,
            ViewportSize = new ViewportSize { Width = width, Height = height },
            IgnoreHTTPSErrors = true,
            Locale = culture + "-US",
        });
        // The demo's default culture follows the browser — pin it (fr is seeded through
        // localStorage/cookie; the visible switcher only offers en/cs).
        await context.AddInitScriptAsync(
            $"""
            localStorage.setItem('tm-demo-culture', '{culture}');
            document.cookie = 'tm-demo-culture={culture};path=/;max-age=31536000;samesite=lax';
            """);
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}{route}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 90000 });
        await WaitForAppReadyAsync(page);
        return page;
    }

    private static async Task<string> ShootAsync(IPage page, string name)
    {
        Directory.CreateDirectory(ShotDir);
        var path = Path.Combine(ShotDir, $"{name}.png");
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
                return a.tagName.toLowerCase() + '.' + (a.className || '').toString().trim().split(/\s+/).join('.')
                    + '#' + (a.getAttribute('data-testid') || a.id || '');
            }
            """);
    }

    [TestMethod]
    public async Task NestedDialog_ClosedByEscape_ReturnsFocusToOpener()
    {
        var page = await OpenAsync("/feedback", 390);

        await page.GetByTestId("open-bottom-sheet").ClickAsync();
        await page.Locator(".tm-drawer--bottom").WaitForAsync();
        // The sheet opens at half height, so the trigger starts below the panel. Scroll it up first.
        var opener = page.GetByTestId("sheet-open-dialog");
        await opener.EvaluateAsync("el => el.scrollIntoView({ block: 'center' })");
        await opener.ClickAsync();
        await page.Locator(".tm-dialog").WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });

        await page.Keyboard.PressAsync("Escape");
        await page.Locator(".tm-dialog").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 5000 });

        // Focus must be back on the opener INSIDE the still-open sheet — not on the body.
        Assert.AreEqual(1, await page.Locator(".tm-drawer--bottom").CountAsync(),
            "the sheet must stay open after its nested dialog closes");
        var active = await ActiveElementAsync(page);
        Assert.IsTrue(active.Contains("sheet-open-dialog"),
            $"focus must return to the opener button, got '{active}'");
    }

    [TestMethod]
    public async Task PromptDialogSheet_LiftsAboveTheKeyboard()
    {
        var page = await OpenAsync("/modal-dialog", 390);

        // Install the fake visualViewport BEFORE the sheet attaches, the way the sheet E2E does:
        // the module reads it at attach and on resize.
        await page.EvaluateAsync(
            """
            () => {
                const viewport = { height: 480, offsetTop: 0, width: 390, addEventListener() {}, removeEventListener() {} };
                Object.defineProperty(window, 'visualViewport', { configurable: true, get: () => viewport });
                window.dispatchEvent(new Event('resize'));
            }
            """);

        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Enter Name" }).ClickAsync();
        var dialog = page.Locator(".tm-dialog");
        await dialog.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        // The sheet reads the viewport when it attaches; re-dispatch so a fake installed after
        // navigation is what the module measures.
        await page.EvaluateAsync("() => window.dispatchEvent(new Event('resize'))");

        await page.WaitForFunctionAsync(
            "() => { const i = document.querySelector('.tm-dialog-input'); return !!i && i.getBoundingClientRect().bottom <= 480; }",
            null, new PageWaitForFunctionOptions { Timeout = 5000 });

        var inputBottom = await page.Locator(".tm-dialog-input").EvaluateAsync<double>(
            "el => el.getBoundingClientRect().bottom");
        Assert.IsTrue(inputBottom <= 480,
            $"the prompt input ({inputBottom:F0}) must stay inside the visible viewport above the keyboard");

        await ShootAsync(page, "390-prompt-dialog-keyboard-lift");
    }

    [TestMethod]
    [DataRow(320)]
    [DataRow(390)]
    public async Task ForcedDialog_AtNarrowWidth_FrLabelsWrapBeforeTheyTruncate(int width)
    {
        // The page boots in French (seeded through localStorage/cookie)…
        var page = await OpenAsync("/modal-dialog", width, culture: "fr");

        // …and the long dialog is forced to its dialog presentation (never a sheet).
        await page.GetByTestId("toggle-dialog-presentation").CheckAsync();

        await page.GetByTestId("open-long-dialog").ClickAsync();
        var dialog = page.Locator(".tm-modal-overlay.tm-modal--dialog");
        await dialog.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });

        // 36-char FR label — the footer contract is wrap-before-truncate: the label may grow
        // taller, never ellipsize (a truncated label hides text with no title to reveal it in the
        // dialog presentation).
        await page.EvaluateAsync(
            """
            () => {
                const label = document.querySelector('.tm-dialog-footer .tm-btn .tm-btn-label');
                label.textContent = 'Supprimer définitivement le document';
            }
            """);
        await page.WaitForTimeoutAsync(300);

        var report = await page.EvaluateAsync<string>(
            """
            () => {
                const labels = [...document.querySelectorAll('.tm-dialog-footer .tm-btn .tm-btn-label')];
                return JSON.stringify(labels.map(l => ({
                    scroll: l.scrollWidth, client: l.clientWidth,
                    scrollH: l.scrollHeight, clientH: l.clientHeight,
                })));
            }
            """);
        TestContext.WriteLine($"forced dialog footer {width}: {report}");
        await ShootAsync(page, $"{width}-forced-dialog-fr-labels");

        using var doc = System.Text.Json.JsonDocument.Parse(report);
        foreach (var label in doc.RootElement.EnumerateArray())
        {
            Assert.IsTrue(label.GetProperty("scroll").GetDouble() <= label.GetProperty("client").GetDouble() + 1,
                $"no footer label may truncate at {width}px: {report}");
        }
    }
}
