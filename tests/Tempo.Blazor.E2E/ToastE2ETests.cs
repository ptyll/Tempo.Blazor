using System.IO;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// „Nové komponenty 1/7" fáze 6 — E2E coverage of the toast extensions on the /feedback demo page:
/// Undo (list item delete + restore), promise toasts (simulated 2 s upload, success and error),
/// Key dedupe, the stacked deck, all six positions, Alt+T, pause-on-hover, Escape ordering inside
/// a modal (toast dismisses, modal stays) and swipe-to-dismiss with touch emulation. Edge-case
/// screenshots land in __screenshots__/toasts/ at 1440 / 1024-touch / 390 / 320 in light and dark.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public class ToastE2ETests : WasmTestBase
{
    private const int LongTimeout = 20000;

    private static readonly string ShotDir = Path.Combine(
        TestContextHelper.RepositoryRoot, "tests", "Tempo.Blazor.E2E", "__screenshots__", "toasts");

    private async Task ShotAsync(IPage page, string name)
    {
        Directory.CreateDirectory(ShotDir);
        var path = Path.Combine(ShotDir, name + ".png");
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = path,
            Type = ScreenshotType.Png,
            // Toast cards are position:fixed — a viewport capture is the UX artifact (a full-page
            // capture repaints the fixed container mid-page, exactly like Notion F-23).
            FullPage = false,
            Timeout = 60_000
        });

        var info = new FileInfo(path);
        Assert.IsTrue(info.Length > 2 * 1024, $"screenshot {name}.png must be a real capture, got {info.Length} bytes");
        await AssertNoBlazorErrorUiAsync(page);
    }

    /// <summary>
    /// A visible #blazor-error-ui means an unhandled Blazor exception — the same guard
    /// SmokeLaneE2ETests asserts. Screenshot captures and page opens must not accept such a page.
    /// </summary>
    private static async Task AssertNoBlazorErrorUiAsync(IPage page)
    {
        var visible = await page.EvaluateAsync<bool>(
            """
            () => {
                const el = document.querySelector('#blazor-error-ui');
                return !!el && getComputedStyle(el).display !== 'none';
            }
            """);
        Assert.IsFalse(visible, "#blazor-error-ui is visible — an unhandled Blazor exception occurred.");
    }

    private async Task<IPage> OpenPageAsync(int width = 1440, int height = 900, bool hasTouch = false, bool dark = false)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
            HasTouch = hasTouch
        });
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}/feedback");
        if (dark)
        {
            await ToggleDarkModeAsync(page);
        }

        await AssertNoBlazorErrorUiAsync(page);
        return page;
    }

    private static ILocator ToastCards(IPage page) => page.Locator(".tm-toast-container .tm-toast");

    /// <summary>
    /// Lets every in-flight CSS animation on the element settle before a screenshot, so captures
    /// do not catch an enter/exit transition mid-frame (swipe offset, deck collapse, progress bar).
    /// </summary>
    private static async Task WaitAnimationsAsync(ILocator element)
    {
        await element.EvaluateAsync("e => Promise.all(e.getAnimations().map(a => a.finished))");
    }

    private static async Task WaitForCardsAsync(IPage page, int minimum)
    {
        await page.WaitForFunctionAsync(
            $"(n) => document.querySelectorAll('.tm-toast-container .tm-toast').length >= {minimum}",
            null,
            new PageWaitForFunctionOptions { Timeout = LongTimeout });
    }

    [TestMethod]
    [Description("E2E-TOAST.0: Delete shows an Undo action toast; Undo restores the list item")]
    public async Task Toast_Undo_RestoresDeletedItem()
    {
        var page = await OpenPageAsync();
        var list = page.Locator("[data-testid='toast-undo-list']");
        var item = list.Locator("[data-testid='toast-undo-item']").First;
        await Assertions.Expect(item).ToContainTextAsync("Quarterly report.docx");

        await item.Locator("[data-testid='toast-undo-delete']").ClickAsync();
        // 3 items - 1 deleted (the .First locator would keep resolving to the next sibling).
        await Assertions.Expect(list.Locator("[data-testid='toast-undo-item']")).ToHaveCountAsync(2);

        var action = page.Locator(".tm-toast-container .tm-toast-action");
        await action.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = LongTimeout });
        await Assertions.Expect(action).ToHaveTextAsync("Undo");
        // The action toast is floored to MinActionDuration (10 s), not the default 5 s.
        await WaitAnimationsAsync(page.Locator(".tm-toast-container"));
        await ShotAsync(page, "toasts-undo-1440-light");

        await action.ClickAsync();
        await Assertions.Expect(list.Locator("[data-testid='toast-undo-item']")).ToHaveCountAsync(3);
        await Assertions.Expect(list.Locator("[data-testid='toast-undo-item']", new() { HasText = "Quarterly report.docx" }))
            .ToHaveCountAsync(1);
        await Assertions.Expect(ToastCards(page)).ToHaveCountAsync(0);
        await AssertNoBlazorErrorUiAsync(page);
    }

    [TestMethod]
    [Description("E2E-TOAST.1: Promise toast — sticky loading state, then an in-place success update")]
    public async Task Toast_Promise_SuccessUpdatesInPlace()
    {
        var page = await OpenPageAsync(dark: true);
        await page.Locator("[data-testid='toast-promise-success']").ClickAsync();

        // Loading: spinner card, sticky (no progress bar), one toast only.
        var card = page.Locator(".tm-toast-container .tm-toast--loading");
        await card.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = LongTimeout });
        await Assertions.Expect(card.Locator(".tm-toast-spinner")).ToBeVisibleAsync();
        await Assertions.Expect(card.Locator(".tm-toast-progress")).ToHaveCountAsync(0);

        // Same card (data-toast-id) settles as success in place.
        var id = await card.GetAttributeAsync("data-toast-id");
        await Assertions.Expect(page.Locator($".tm-toast-container .tm-toast[data-toast-id='{id}']"))
            .ToHaveClassAsync(new System.Text.RegularExpressions.Regex("tm-toast--success"),
                new() { Timeout = LongTimeout });
        await Assertions.Expect(page.Locator($".tm-toast[data-toast-id='{id}']")).ToContainTextAsync("uploaded");
        await ShotAsync(page, "toasts-promise-success-1440-dark");
        await AssertNoBlazorErrorUiAsync(page);
    }

    [TestMethod]
    [Description("E2E-TOAST.2 (edge): Promise toast failure updates in place to an assertive error")]
    public async Task Toast_Promise_ErrorUpdatesInPlace()
    {
        var page = await OpenPageAsync();
        await page.Locator("[data-testid='toast-promise-error']").ClickAsync();

        var loading = page.Locator(".tm-toast-container .tm-toast--loading");
        await loading.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = LongTimeout });
        var id = await loading.GetAttributeAsync("data-toast-id");

        var settled = page.Locator($".tm-toast-container .tm-toast[data-toast-id='{id}']");
        await Assertions.Expect(settled).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("tm-toast--error"),
            new() { Timeout = LongTimeout });
        await Assertions.Expect(settled).ToContainTextAsync("Upload failed");
        await AssertNoBlazorErrorUiAsync(page);
    }

    [TestMethod]
    [Description("E2E-TOAST.3: Key dedupe replaces the toast in place instead of stacking")]
    public async Task Toast_Dedupe_ReplacesInPlace()
    {
        var page = await OpenPageAsync();
        var count = page.Locator("[data-testid='toast-dedupe-count']");
        await count.ClickAsync();
        await WaitForCardsAsync(page, 1);
        var id = await ToastCards(page).First.GetAttributeAsync("data-toast-id");

        await count.ClickAsync();
        // The in-place replacement restarts the timer — wait for the updated counter text
        // instead of a fixed sleep.
        await Assertions.Expect(ToastCards(page).First).ToContainTextAsync("Counted 2×", new() { Timeout = LongTimeout });

        await Assertions.Expect(ToastCards(page)).ToHaveCountAsync(1);
        await Assertions.Expect(ToastCards(page).First).ToHaveAttributeAsync("data-toast-id", id!);
        await Assertions.Expect(ToastCards(page).First).ToContainTextAsync("Counted 2×");
        await AssertNoBlazorErrorUiAsync(page);
    }

    [TestMethod]
    [Description("E2E-TOAST.4: Alt+T focuses the newest toast action button")]
    public async Task Toast_AltT_FocusesNewestAction()
    {
        var page = await OpenPageAsync();
        await page.Locator("[data-testid='toast-undo-list'] [data-testid='toast-undo-delete']").First.ClickAsync();
        await page.Locator(".tm-toast-container .tm-toast-action")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = LongTimeout });

        await page.Keyboard.PressAsync("Alt+T");

        var focused = await page.EvaluateAsync<string>("() => document.activeElement?.className || ''");
        StringAssert.Contains(focused, "tm-toast-action", "Alt+T must move focus to the newest action button");
        await AssertNoBlazorErrorUiAsync(page);
    }

    [TestMethod]
    [Description("E2E-TOAST.5 (edge): Escape on a toast action inside a modal dismisses the toast, NOT the modal")]
    public async Task Toast_EscapeInsideModal_ClosesToastNotModal()
    {
        var page = await OpenPageAsync();
        await page.Locator("[data-testid='toast-escape-open-modal']").ClickAsync();
        var modal = page.Locator("[data-testid='toast-escape-modal']");
        await modal.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = LongTimeout });

        await page.Locator("[data-testid='toast-escape-push']").ClickAsync();
        await page.Locator(".tm-toast-container .tm-toast-action")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = LongTimeout });
        await page.Keyboard.PressAsync("Alt+T");
        await page.Keyboard.PressAsync("Escape");

        await Assertions.Expect(modal).ToBeVisibleAsync();
        await Assertions.Expect(ToastCards(page)).ToHaveCountAsync(0);
        await AssertNoBlazorErrorUiAsync(page);
    }

    [TestMethod]
    [Description("E2E-TOAST.6: Hovering pauses the auto-dismiss timer (frozen progress); leaving resumes it")]
    public async Task Toast_PauseOnHover_FreezesTimer()
    {
        var page = await OpenPageAsync();
        await page.Locator("[data-testid='toast-presentation-demo']").ClickAsync();
        var card = page.Locator(".tm-toast-container .tm-toast").First;
        await card.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = LongTimeout });

        await card.HoverAsync();
        await Assertions.Expect(card).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("tm-toast--paused"));

        await page.Mouse.MoveAsync(5, 5);
        await Assertions.Expect(card).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("tm-toast--paused"));
        await AssertNoBlazorErrorUiAsync(page);
    }

    [TestMethod]
    [Description("E2E-TOAST.6b (timing): a hovered toast outlives its duration; leaving resumes the timer and removes it")]
    public async Task Toast_PauseOnHover_OutlivesDurationThenRemoves()
    {
        var page = await OpenPageAsync();
        await page.Locator("[data-testid='toast-short-duration']").ClickAsync();
        var card = page.Locator(".tm-toast-container .tm-toast").First;
        await card.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = LongTimeout });

        // The duration is 1.5 s — hover BEFORE it elapses and stay hovered well past it.
        await card.HoverAsync();
        await Assertions.Expect(card).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("tm-toast--paused"));
        await page.WaitForTimeoutAsync(2200);
        await Assertions.Expect(card).ToBeVisibleAsync();

        // Leaving resumes the remaining timer — the toast dismisses shortly after.
        await page.Mouse.MoveAsync(5, 5);
        await Assertions.Expect(ToastCards(page)).ToHaveCountAsync(0, new() { Timeout = LongTimeout });
        await AssertNoBlazorErrorUiAsync(page);
    }

    [TestMethod]
    [Description("E2E-TOAST.7: The stacked deck keeps 3 cards visible and collapses the older ones to strips")]
    public async Task Toast_StackedDeck_CollapsesOlderCards()
    {
        var page = await OpenPageAsync();
        await page.Locator("[data-testid='toast-deck-toggle']").ClickAsync(); // stacked on
        await page.Locator("[data-testid='toast-deck-fill']").ClickAsync();
        // The fill spaces its pushes (~120 ms apart) so the deck order is real — wait for the
        // newest card, not just any five (the shot must not catch the fill mid-flight).
        await page.Locator(".tm-toast-container .tm-toast", new() { HasText = "Deck toast 5 (newest)" })
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = LongTimeout });

        var container = page.Locator(".tm-toast-container");
        await Assertions.Expect(container).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("tm-toast-container--stacked"));
        var total = await ToastCards(page).CountAsync();
        var collapsed = await page.Locator(".tm-toast--deck-collapsed").CountAsync();
        Assert.AreEqual(total - 3, collapsed, $"the newest three cards stay visible, the {total - 3} older ones collapse to strips");

        await WaitAnimationsAsync(container);
        await ShotAsync(page, "toasts-deck-1440-light");
        await AssertNoBlazorErrorUiAsync(page);
    }

    [TestMethod]
    [Description("E2E-TOAST.8: Position buttons reconfigure the layout container (TopCenter / BottomCenter)")]
    public async Task Toast_Positions_ReconfigureContainer()
    {
        var page = await OpenPageAsync(dark: true);
        await page.Locator("[data-testid='toast-position-topcenter']").ClickAsync();
        await Assertions.Expect(page.Locator(".tm-toast-container"))
            .ToHaveClassAsync(new System.Text.RegularExpressions.Regex("tm-toast-container--top-center"));

        // The position click also pushes a demo toast — let its enter transition settle.
        await WaitAnimationsAsync(page.Locator(".tm-toast-container"));
        await ShotAsync(page, "toasts-top-center-1440-dark");

        await page.Locator("[data-testid='toast-position-bottomcenter']").ClickAsync();
        await Assertions.Expect(page.Locator(".tm-toast-container"))
            .ToHaveClassAsync(new System.Text.RegularExpressions.Regex("tm-toast-container--bottom-center"));
        await AssertNoBlazorErrorUiAsync(page);
    }

    [TestMethod]
    [Description("E2E-TOAST.9 (edge, touch): a horizontal swipe on a toast card dismisses it (touch emulation)")]
    public async Task Toast_SwipeHorizontally_Dismisses()
    {
        var page = await OpenPageAsync(width: 390, height: 844, hasTouch: true);
        await page.Locator("[data-testid='toast-presentation-demo']").ClickAsync();
        var card = page.Locator(".tm-toast-container .tm-toast").First;
        await card.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = LongTimeout });
        var id = await card.GetAttributeAsync("data-toast-id");
        var box = await card.BoundingBoxAsync();
        Assert.IsNotNull(box);

        // Capture the card before the swipe dismisses it.
        await WaitAnimationsAsync(card);
        await ShotAsync(page, "toasts-mobile-390-touch-light");

        // Real touch points through CDP (Playwright's Touchscreen has no drag): a horizontal
        // flick past 40% of the card width dismisses through the swipe-to-dismiss module.
        var client = await page.Context.NewCDPSessionAsync(page);
        var startX = box!.X + box.Width / 2;
        var y = box.Y + box.Height / 2;
        var endX = box.X - box.Width; // well past 40 % to the left
        await client.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
        {
            ["type"] = "touchStart",
            ["touchPoints"] = new[] { new Dictionary<string, object> { ["x"] = startX, ["y"] = y } }
        });
        for (var step = 1; step <= 5; step++)
        {
            await client.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
            {
                ["type"] = "touchMove",
                ["touchPoints"] = new[] { new Dictionary<string, object> { ["x"] = startX + (endX - startX) * step / 5, ["y"] = y } }
            });
            await page.WaitForTimeoutAsync(15);
        }

        await client.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
        {
            ["type"] = "touchEnd",
            ["touchPoints"] = Array.Empty<object>()
        });

        await Assertions.Expect(page.Locator($".tm-toast-container .tm-toast[data-toast-id='{id}']"))
            .ToHaveCountAsync(0, new() { Timeout = LongTimeout });
        await AssertNoBlazorErrorUiAsync(page);
    }

    [TestMethod]
    [Description("E2E-TOAST.10 (edge): 320 px narrow dark viewport — toasts go full-width without the centering offset")]
    public async Task Toast_Narrow320_Dark_Layout()
    {
        var page = await OpenPageAsync(width: 320, height: 700, dark: true);
        await page.Locator("[data-testid='toast-position-topcenter']").ClickAsync();
        await page.Locator("[data-testid='toast-presentation-demo']").ClickAsync();
        var card = page.Locator(".tm-toast-container .tm-toast").First;
        await card.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = LongTimeout });

        var box = await card.BoundingBoxAsync();
        Assert.IsTrue(box is { X: >= 0 }, $"a 320 px viewport must not clip the card on the left: X={box?.X}");
        Assert.IsTrue(box is not null && box.X + box.Width <= 320,
            $"a 320 px viewport must not overflow: card spans X {box?.X}..{box?.X + box?.Width}");
        await WaitAnimationsAsync(card);
        await ShotAsync(page, "toasts-narrow-320-dark");
        await AssertNoBlazorErrorUiAsync(page);
    }

    [TestMethod]
    [Description("E2E-TOAST.10b (edge): 390 px mobile dark viewport — a toast shot for the mobile dark band")]
    public async Task Toast_Mobile390_Dark_Layout()
    {
        var page = await OpenPageAsync(width: 390, height: 844, dark: true);
        await page.Locator("[data-testid='toast-presentation-demo']").ClickAsync();
        var card = page.Locator(".tm-toast-container .tm-toast").First;
        await card.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = LongTimeout });

        await WaitAnimationsAsync(card);
        await ShotAsync(page, "toasts-mobile-390-dark");
        await AssertNoBlazorErrorUiAsync(page);
    }

    [TestMethod]
    [Description("E2E-TOAST.11 (edge): 1024 px touch viewport captures the tablet band with a toast")]
    public async Task Toast_Tablet1024_Touch_Layout()
    {
        var page = await OpenPageAsync(width: 1024, height: 768, hasTouch: true);
        await page.Locator("[data-testid='toast-promise-success']").ClickAsync();
        var loading = page.Locator(".tm-toast-container .tm-toast--loading");
        await loading.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = LongTimeout });

        await ShotAsync(page, "toasts-tablet-1024-touch-light");
        await AssertNoBlazorErrorUiAsync(page);
    }

    [TestMethod]
    [Description("E2E-TOAST.12 (a11y): a toast with an action has no critical/serious axe violations")]
    public async Task Toast_ActionToast_AxeClean()
    {
        var page = await OpenPageAsync();
        await page.Locator("[data-testid='toast-undo-list'] [data-testid='toast-undo-delete']").First.ClickAsync();
        var action = page.Locator(".tm-toast-container .tm-toast-action");
        await action.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = LongTimeout });

        var violations = await AxeScan.RunAsync(page, ".tm-toast-container");
        Assert.AreEqual(0, violations.Length,
            $"the toast container must have no critical/serious a11y violations: {string.Join(" | ", violations)}");
    }
}
