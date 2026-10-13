using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// FZE-21 lane: the TmColorGradient gesture on the shared pointer-drag helper
/// (tm-color-gradient.js → pointer-drag.js). A real mouse drag that LEAVES the area mid-drag keeps
/// tracking (pointer capture — the old Blazor handlers silently stopped), a real CDP touch on a
/// 390px viewport changes the hue, and the hue slider is keyboard-operable (WCAG 2.1.1/2.5.7).
/// </summary>
[TestClass]
public class ColorGradientE2ETests : WasmTestBase
{
    private const string Section = "[data-testid='color-gradient-section']";
    private const string Area = $"{Section} .tm-color-gradient-area";
    private const string Hue = $"{Section} .tm-color-gradient-hue-track";
    private const string ValueText = $"{Section} .tm-color-gradient-value-text";

    private async Task<IPage> OpenAsync(bool touch, int width, int height)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = touch,
            IsMobile = touch && width < 640,
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/color-picker", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);

        await page.Locator(Area).WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await page.EvaluateAsync("() => document.querySelector(\"[data-testid='color-gradient-section']\").scrollIntoView({ block: 'center', behavior: 'instant' })");
        await page.WaitForTimeoutAsync(300);
        return page;
    }

    private static async Task<string> ValueAsync(IPage page)
        => (await page.Locator(ValueText).InnerTextAsync()).Trim();

    // ── mouse ───────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task ColorGradient_Mouse_DragLeavingTheArea_KeepsTracking_AndCommitsOnce()
    {
        var page = await OpenAsync(touch: false, 1440, 900);
        var before = await ValueAsync(page);

        var box = (await page.Locator(Area).BoundingBoxAsync())!;
        var startX = box.X + box.Width / 2;
        var startY = box.Y + box.Height / 2;
        await page.Mouse.MoveAsync(startX, startY);
        await page.Mouse.DownAsync();
        // The pointer leaves the element mid-drag: the capture keeps the gesture alive (the old
        // Blazor @onpointermove handlers silently stopped tracking outside the element).
        await page.Mouse.MoveAsync(box.X + box.Width + 40, Math.Max(box.Y - 20, 5), new MouseMoveOptions { Steps = 10 });
        Assert.AreNotEqual(before, await ValueAsync(page), "the preview streams while dragging");
        await page.Mouse.UpAsync();

        Assert.AreNotEqual(before, await ValueAsync(page), "the drag committed the colour");
        Assert.AreEqual("100", await page.Locator(Area).GetAttributeAsync("aria-valuenow"),
            "the pointer past the right edge clamps to saturation 1");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task ColorGradient_Mouse_EscapeMidDrag_RestoresTheStartColour()
    {
        var page = await OpenAsync(touch: false, 1440, 900);
        var before = await ValueAsync(page);

        var box = (await page.Locator(Area).BoundingBoxAsync())!;
        var startX = box.X + box.Width / 2;
        var startY = box.Y + box.Height / 2;
        await page.Mouse.MoveAsync(startX, startY);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(startX + 60, startY + 40, new MouseMoveOptions { Steps = 6 });
        Assert.AreNotEqual(before, await ValueAsync(page), "precondition: the drag changed the preview");
        await page.Keyboard.PressAsync("Escape");
        await page.Mouse.UpAsync();

        Assert.AreEqual(before, await ValueAsync(page), "Escape restored the colour the press started from");
    }

    // ── touch (coarse pointer, 390) ─────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task ColorGradient_Touch_At390_ChangesTheHue()
    {
        var page = await OpenAsync(touch: true, 390, 800);
        Assert.IsTrue(await page.EvaluateAsync<bool>("() => matchMedia('(pointer: coarse)').matches"), "precondition: coarse pointer emulation");
        Assert.AreEqual("none", await page.Locator(Hue).EvaluateAsync<string>("e => getComputedStyle(e).touchAction"),
            "the slider starts the drag immediately; touch-action belongs to it alone");
        var before = await ValueAsync(page);

        var box = (await page.Locator(Hue).BoundingBoxAsync())!;
        var session = await page.Context.NewCDPSessionAsync(page);
        var x = box.X + box.Width / 2;
        var y = box.Y + box.Height / 2;
        await TouchAsync(session, "touchStart", x, y);
        for (var i = 1; i <= 6; i++)
        {
            await TouchAsync(session, "touchMove", x + 70 * i / 6.0, y);
        }
        await TouchAsync(session, "touchEnd", x + 70, y);

        Assert.AreNotEqual(before, await ValueAsync(page), "the finger changed the hue at 390px");
    }

    // ── keyboard ────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task ColorGradient_Keyboard_HueSlider_ArrowRightX10_ChangesTheColour()
    {
        var page = await OpenAsync(touch: true, 390, 800);
        var hue = page.Locator(Hue);
        var before = int.Parse((await hue.GetAttributeAsync("aria-valuenow"))!);
        var valueBefore = await ValueAsync(page);

        await hue.FocusAsync();
        for (var i = 0; i < 10; i++)
        {
            await page.Keyboard.PressAsync("ArrowRight");
            await page.WaitForTimeoutAsync(80);
        }

        Assert.AreEqual(before + 10, int.Parse((await hue.GetAttributeAsync("aria-valuenow"))!),
            "ten arrow steps of 1° moved the hue");
        Assert.AreNotEqual(valueBefore, await ValueAsync(page), "the committed colour changed");
    }

    private static Task TouchAsync(ICDPSession session, string type, double x, double y)
        => session.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
        {
            ["type"] = type,
            ["touchPoints"] = type == "touchEnd"
                ? Array.Empty<object>()
                : new object[] { new Dictionary<string, object> { ["x"] = x, ["y"] = y, ["id"] = 1 } },
        });
}
