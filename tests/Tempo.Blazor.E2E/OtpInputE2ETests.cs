using System.IO;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// „Nové komponenty 1/7" fáze 6 — E2E coverage of the /otp-input demo page: the three kinds
/// (numeric 6-digit with a simulated SMS verification, alphanumeric GroupSize=4, masked PIN),
/// typing, paste distribution via the clipboard API, autofill-like multi-character input,
/// the wrong-code shake/error/announce flow and the verified state. Edge-case screenshots land
/// in __screenshots__/otp-input/ at 1440 / 1024-touch / 390 / 320 in light and dark.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public class OtpInputE2ETests : WasmTestBase
{
    private const int LongTimeout = 20000;

    private static readonly string ShotDir = Path.Combine(
        TestContextHelper.RepositoryRoot, "tests", "Tempo.Blazor.E2E", "__screenshots__", "otp-input");

    private async Task ShotAsync(IPage page, string name)
    {
        Directory.CreateDirectory(ShotDir);
        var path = Path.Combine(ShotDir, name + ".png");
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = path,
            Type = ScreenshotType.Png,
            FullPage = true,
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

    /// <summary>Opens /otp-input at an arbitrary viewport; optionally switches to dark mode.</summary>
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
        await GotoAppReadyAsync(page, $"{BaseUrl}/otp-input");
        if (dark)
        {
            await ToggleDarkModeAsync(page);
        }

        await AssertNoBlazorErrorUiAsync(page);
        return page;
    }

    /// <summary>The demo's simulated SMS flow: send, then the numeric input is on screen.</summary>
    private static async Task<ILocator> SendCodeAndGetInputAsync(IPage page)
    {
        await page.Locator("[data-testid='otp-send-sms']").ClickAsync();
        var input = page.Locator("[data-testid='otp-numeric-input']");
        await input.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = LongTimeout });
        return input;
    }

    [TestMethod]
    [Description("E2E-OTP.0: All three OTP kinds render with their shapes (numeric 6, grouped alphanumeric, masked PIN)")]
    public async Task OtpInput_Renders_KindsLengthsGroupingMasked()
    {
        var page = await OpenPageAsync();

        // The numeric input appears after the simulated SMS send.
        var numeric = await SendCodeAndGetInputAsync(page);
        await Assertions.Expect(numeric.Locator(".tm-otp-input__cell")).ToHaveCountAsync(6);
        await Assertions.Expect(numeric.Locator(".tm-otp-input__cell").First).ToHaveAttributeAsync("inputmode", "numeric");
        await Assertions.Expect(numeric.Locator(".tm-otp-input__cell").First).ToHaveAttributeAsync("autocomplete", "one-time-code");

        var alpha = page.Locator("[data-testid='otp-alpha-input']");
        await Assertions.Expect(alpha.Locator(".tm-otp-input__cell")).ToHaveCountAsync(8);
        await Assertions.Expect(alpha.Locator(".tm-otp-input__separator")).ToHaveCountAsync(1);
        await Assertions.Expect(alpha).ToHaveAttributeAsync("data-tm-otp-kind", "alphanumeric");

        var pin = page.Locator("[data-testid='otp-pin-input']");
        await Assertions.Expect(pin.Locator("input.tm-otp-input__cell")).ToHaveCountAsync(4);
        await Assertions.Expect(pin.Locator("input.tm-otp-input__cell").First).ToHaveAttributeAsync("type", "password");

        await ShotAsync(page, "otp-overview-1440-light");
    }

    [TestMethod]
    [Description("E2E-OTP.1: Typing the demo code digit by digit verifies the numeric input")]
    public async Task OtpInput_TypingDemoCode_Verifies()
    {
        var page = await OpenPageAsync();
        var input = await SendCodeAndGetInputAsync(page);

        await input.Locator(".tm-otp-input__cell").First.ClickAsync();
        await page.Keyboard.TypeAsync("123456");

        await Assertions.Expect(input).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("tm-otp-input--verified"));
        await Assertions.Expect(page.Locator(".tm-otp-input__status").First).ToBeVisibleAsync();
        // Verified cells are readonly — the code cannot be edited any more.
        await Assertions.Expect(input.Locator(".tm-otp-input__cell").First).ToHaveAttributeAsync("readonly", "");

        await ShotAsync(page, "otp-verified-1440-light");
        await AssertNoBlazorErrorUiAsync(page);
    }

    [TestMethod]
    [Description("E2E-OTP.2 (edge): A wrong code shakes, announces the error and clears the cells for a retry")]
    public async Task OtpInput_WrongCode_ShakesAnnouncesAndClears()
    {
        var page = await OpenPageAsync(dark: true);
        var input = await SendCodeAndGetInputAsync(page);

        await input.Locator(".tm-otp-input__cell").First.ClickAsync();
        await page.Keyboard.TypeAsync("999999");

        await Assertions.Expect(input).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("tm-otp-input--invalid"));
        await Assertions.Expect(input).ToHaveAttributeAsync("aria-invalid", "true");
        await Assertions.Expect(page.Locator(".tm-otp-input__error")).ToContainTextAsync("Incorrect code");

        // The polite live region carried the error text to screen readers.
        var liveText = await page.EvaluateAsync<string>(
            "() => [...document.querySelectorAll('[role=status].tm-sr-only')].map(e => e.textContent).join(' ')");
        StringAssert.Contains(liveText, "Incorrect code");

        // The demo replays the error (ShowErrorAsync) and clears the draft so the next full code fires again.
        await Assertions.Expect(input.Locator(".tm-otp-input__cell").First).ToHaveValueAsync("");

        await ShotAsync(page, "otp-invalid-1440-dark");
        await AssertNoBlazorErrorUiAsync(page);
    }

    [TestMethod]
    [Description("E2E-OTP.3: Pasting a dashed code through the clipboard API distributes over the cells and verifies")]
    public async Task OtpInput_Paste_DistributesAndVerifies()
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
            Permissions = ["clipboard-read", "clipboard-write"]
        });
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}/otp-input");
        var input = await SendCodeAndGetInputAsync(page);

        await page.EvaluateAsync("() => navigator.clipboard.writeText('123-456')");
        await input.Locator(".tm-otp-input__cell").First.ClickAsync();
        await page.Keyboard.PressAsync("Control+V");

        // Paste distributes: every cell filled, the demo verifier confirms.
        await Assertions.Expect(input).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("tm-otp-input--verified"));
        await AssertNoBlazorErrorUiAsync(page);
    }

    [TestMethod]
    [Description("E2E-OTP.4 (edge): An autofill-like single input event with the whole code is distributed")]
    public async Task OtpInput_AutofillLikeMultiCharInput_Distributes()
    {
        var page = await OpenPageAsync();
        var input = await SendCodeAndGetInputAsync(page);

        // Browser/SMS autofill writes the whole code in ONE input event (no per-key beforeinput).
        await input.Locator(".tm-otp-input__cell").First.FillAsync("123456");

        await Assertions.Expect(input).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("tm-otp-input--verified"));
        await AssertNoBlazorErrorUiAsync(page);
    }

    [TestMethod]
    [Description("E2E-OTP.5 (edge): The masked PIN keeps type=password and reports only the length")]
    public async Task OtpInput_MaskedPin_StaysHidden()
    {
        var page = await OpenPageAsync(dark: true);
        var pin = page.Locator("[data-testid='otp-pin-input']");

        await pin.Locator(".tm-otp-input__cell").First.ClickAsync();
        await page.Keyboard.TypeAsync("4321");

        await Assertions.Expect(pin.Locator("input.tm-otp-input__cell").First).ToHaveAttributeAsync("type", "password");
        await Assertions.Expect(page.Locator("[data-testid='otp-pin-echo']")).ToContainTextAsync("4/4");
        await Assertions.Expect(page.Locator("[data-testid='otp-pin-echo']")).Not.ToContainTextAsync("4321");

        await ShotAsync(page, "otp-pin-1440-dark");
        await AssertNoBlazorErrorUiAsync(page);
    }

    [TestMethod]
    [Description("E2E-OTP.6: Alphanumeric input uppercases automatically and honours the group separators")]
    public async Task OtpInput_Alphanumeric_Uppercases()
    {
        var page = await OpenPageAsync();
        var alpha = page.Locator("[data-testid='otp-alpha-input']");

        await alpha.Locator(".tm-otp-input__cell").First.ClickAsync();
        await page.Keyboard.TypeAsync("ab12cd34");

        await Assertions.Expect(page.Locator("[data-testid='otp-alpha-echo']")).ToContainTextAsync("AB12CD34");
        await AssertNoBlazorErrorUiAsync(page);
    }

    [TestMethod]
    [Description("E2E-OTP.7 (a11y): the numeric input in its invalid state has no critical/serious axe violations")]
    public async Task OtpInput_InvalidState_AxeClean()
    {
        var page = await OpenPageAsync();
        var input = await SendCodeAndGetInputAsync(page);

        await input.Locator(".tm-otp-input__cell").First.ClickAsync();
        await page.Keyboard.TypeAsync("999999");
        await Assertions.Expect(input).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("tm-otp-input--invalid"));

        var violations = await AxeScan.RunAsync(page, "[data-testid='otp-numeric-input']");
        Assert.AreEqual(0, violations.Length,
            $"invalid TmOtpInput must have no critical/serious a11y violations: {string.Join(" | ", violations)}");
    }

    [TestMethod]
    [Description("E2E-OTP.8 (edge): 390 px touch viewport — 44px cells, sections stack, no horizontal overflow")]
    public async Task OtpInput_Mobile390_Touch_Layout()
    {
        var page = await OpenPageAsync(width: 390, height: 844, hasTouch: true);
        var input = await SendCodeAndGetInputAsync(page);

        var overflow = await page.EvaluateAsync<bool>(
            "() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1");
        Assert.IsFalse(overflow, "the OTP page must not overflow horizontally at 390 px");

        var box = await input.Locator(".tm-otp-input__cell").First.BoundingBoxAsync();
        Assert.IsTrue(box is { Height: >= 40 }, $"touch cells must be ~44px tall, got {box?.Height}");

        var lastBox = await input.Locator(".tm-otp-input__cell").Last.BoundingBoxAsync();
        Assert.IsTrue(lastBox is not null && lastBox.X + lastBox.Width <= 390,
            $"the last OTP cell must stay within the 390 px viewport, got right edge {lastBox?.X + lastBox?.Width}");

        await ShotAsync(page, "otp-mobile-390-touch-light");
        await AssertNoBlazorErrorUiAsync(page);
    }

    [TestMethod]
    [Description("E2E-OTP.9 (edge): 320 px narrow viewport in dark mode — grouping and masked PIN stay usable")]
    public async Task OtpInput_Narrow320_Dark_Layout()
    {
        var page = await OpenPageAsync(width: 320, height: 700, dark: true);
        var input = await SendCodeAndGetInputAsync(page);

        var overflow = await page.EvaluateAsync<bool>(
            "() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1");
        Assert.IsFalse(overflow, "the OTP page must not overflow horizontally at 320 px");

        var lastBox = await input.Locator(".tm-otp-input__cell").Last.BoundingBoxAsync();
        Assert.IsTrue(lastBox is not null && lastBox.X + lastBox.Width <= 320,
            $"the last OTP cell must stay within the 320 px viewport, got right edge {lastBox?.X + lastBox?.Width}");

        await ShotAsync(page, "otp-narrow-320-dark");
        await AssertNoBlazorErrorUiAsync(page);
    }

    [TestMethod]
    [Description("E2E-OTP.10 (edge): 1024 px touch viewport captures the tablet band")]
    public async Task OtpInput_Tablet1024_Touch_Layout()
    {
        var page = await OpenPageAsync(width: 1024, height: 768, hasTouch: true);
        await SendCodeAndGetInputAsync(page);
        await page.Locator("[data-testid='otp-alpha-input']").ScrollIntoViewIfNeededAsync();

        await ShotAsync(page, "otp-tablet-1024-touch-light");
        await AssertNoBlazorErrorUiAsync(page);
    }
}
