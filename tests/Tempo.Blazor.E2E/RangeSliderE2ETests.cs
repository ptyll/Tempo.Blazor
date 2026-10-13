using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 20 lane: the <c>TmRangeSlider</c> dual-native-thumb model on the real /new-components demo.
/// The two stacked <c>input[type=range]</c> drag THEMSELVES (CSS <c>pointer-events</c> split, no JS
/// gesture): a mouse drags the end thumb, a finger drags the start thumb (real CDP touch at 768 and
/// 390 — the old eval gesture died on touch because the container had no touch-action), a vertical
/// swipe on the track still scrolls the page, and the start thumb cannot pass the end. Keyboard:
/// arrows raise by Step on the natively operable inputs. The demo slider is Min 0 / Max 100 /
/// Step 1 with start 20, end 80 and <c>ShowValue</c>. Diagnostic shots go to gitignored
/// TestResults, never a committed baseline.
/// </summary>
[TestClass]
public class RangeSliderE2ETests : WasmTestBase
{
    private const string Route = "/new-components";
    private const string Slider = ".tm-range-slider";
    private const string StartInput = ".tm-range-slider__input--start";
    private const string EndInput = ".tm-range-slider__input--end";

    private string ShotDir
        => Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "range-slider");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TempoBlazor.slnx")))
        {
            dir = dir.Parent!;
        }

        return dir.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [TestInitialize]
    public void EnsureShotDir() => Directory.CreateDirectory(ShotDir);

    private async Task<IPage> OpenAsync(int width, int height, bool touch)
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
        await page.GotoAsync($"{BaseUrl}{Route}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        var slider = page.Locator(Slider).First;
        await slider.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await slider.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(300);
        return page;
    }

    private static ILocator FirstSlider(IPage page) => page.Locator(Slider).First;

    private static ILocator StartSpan(IPage page) => FirstSlider(page).Locator(".tm-range-slider__values span").First;

    private static ILocator EndSpan(IPage page) => FirstSlider(page).Locator(".tm-range-slider__values span").Last;

    /// <summary>
    /// The centre of an input's thumb, derived from the input's box and its OWN current value —
    /// the position the browser actually renders, so a clamped-out thumb is caught where it sits.
    /// </summary>
    private static async Task<(float X, float Y)> ThumbCenterAsync(IPage page, string inputClass)
    {
        var point = await page.EvaluateAsync<double[]>(@$"
            () => {{
                const input = document.querySelector('{Slider} {inputClass}');
                const box = input.getBoundingClientRect();
                const min = parseFloat(input.min), max = parseFloat(input.max);
                const ratio = (input.valueAsNumber - min) / (max - min);
                return [box.left + ratio * box.width, box.top + box.height / 2];
            }}");
        return ((float)point[0], (float)point[1]);
    }

    private async Task ShotAsync(IPage page, string name)
    {
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync("() => document.activeElement instanceof HTMLElement && document.activeElement.blur()");
        await page.WaitForTimeoutAsync(250);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(ShotDir, $"{name}.png"), FullPage = false });
    }

    // ── mouse at 1440 ───────────────────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task RangeSlider_1440_MouseDragEndThumb_ChangesTheEndValue()
    {
        var page = await OpenAsync(1440, 900, touch: false);
        Assert.AreEqual("80", await EndSpan(page).InnerTextAsync(), "precondition: the demo starts at 20–80");

        var (x, y) = await ThumbCenterAsync(page, EndInput);
        await page.Mouse.MoveAsync(x, y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(x - 200, y, new MouseMoveOptions { Steps = 10 });
        await page.Mouse.UpAsync();
        await page.WaitForTimeoutAsync(400);   // the change events round-trip through Blazor

        var end = int.Parse(await EndSpan(page).InnerTextAsync());
        Assert.IsTrue(end is > 20 and < 80, $"dragging the end thumb left lowered the end value (80 -> {end})");
        Assert.AreEqual("20", await StartSpan(page).InnerTextAsync(), "the start value is untouched");
        await ShotAsync(page, "1440-mouse-end-dragged");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task RangeSlider_1440_StartThumb_CannotPassTheEndThumb()
    {
        var page = await OpenAsync(1440, 900, touch: false);
        var (x, y) = await ThumbCenterAsync(page, StartInput);

        await page.Mouse.MoveAsync(x, y);
        await page.Mouse.DownAsync();
        // Straight past the end thumb at 80.
        await page.Mouse.MoveAsync(x + 700, y, new MouseMoveOptions { Steps = 12 });
        await page.Mouse.UpAsync();
        await page.WaitForTimeoutAsync(400);

        var start = int.Parse(await StartSpan(page).InnerTextAsync());
        var end = int.Parse(await EndSpan(page).InnerTextAsync());
        Assert.IsTrue(start <= end, $"the start never passes the end ({start} vs {end})");
        Assert.AreEqual(80, start, "the drag clamps the start at the end value");
        Assert.IsTrue(await FirstSlider(page).EvaluateAsync<bool>("e => e.classList.contains('tm-range-slider--start-on-top')"),
            "with both thumbs at the same value the start thumb lifts above the end one");
        // The clamp must reach the input itself: a stuck raw gesture value (90) would park the
        // rendered thumb past the end thumb even though the state says 80.
        Assert.AreEqual("80", await FirstSlider(page).Locator(StartInput).InputValueAsync(),
            "the input carries the clamped value, not the raw gesture value");
        var (sx, _) = await ThumbCenterAsync(page, StartInput);
        var (ex, _) = await ThumbCenterAsync(page, EndInput);
        Assert.IsTrue(sx <= ex + 0.5f, $"the start thumb sits at or left of the end thumb ({sx} vs {ex})");
    }

    // ── touch at 768 and 390 ────────────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [DataRow(768)]
    [DataRow(390)]
    public async Task RangeSlider_TouchDragStartThumb_ChangesTheStartValue_AndTheTrackStillScrolls(int width)
    {
        var page = await OpenAsync(width, 1024, touch: true);
        Assert.AreEqual("20", await StartSpan(page).InnerTextAsync(), "precondition: the demo starts at 20–80");

        var (x, y) = await ThumbCenterAsync(page, StartInput);
        var session = await page.Context.NewCDPSessionAsync(page);
        await TouchAsync(session, "touchStart", x, y);
        for (var i = 1; i <= 6; i++)
        {
            await TouchAsync(session, "touchMove", x + 12 * i, y);
            await page.WaitForTimeoutAsync(16);
        }

        await TouchAsync(session, "touchEnd", x + 72, y);
        await page.WaitForTimeoutAsync(400);

        var start = int.Parse(await StartSpan(page).InnerTextAsync());
        Assert.IsTrue(start is > 20 and <= 80, $"the finger dragged the start thumb natively (20 -> {start})");
        await ShotAsync(page, $"{width}-touch-start-dragged");

        // A vertical swipe that starts on the track (pointer-events: none there) is a scroll,
        // not a stuck drag: the page must move.
        var scrollBefore = await page.EvaluateAsync<double>("() => window.scrollY");
        var track = (await FirstSlider(page).Locator(".tm-range-slider__track").BoundingBoxAsync())!;
        var tx = track.X + track.Width / 2;
        var ty = track.Y + track.Height / 2;
        await TouchAsync(session, "touchStart", tx, ty);
        for (var i = 1; i <= 6; i++)
        {
            await TouchAsync(session, "touchMove", tx, ty + 30 * i);
            await page.WaitForTimeoutAsync(16);
        }

        await TouchAsync(session, "touchEnd", tx, ty + 180);
        await page.WaitForTimeoutAsync(400);

        var scrollAfter = await page.EvaluateAsync<double>("() => window.scrollY");
        // The finger moves DOWN the screen, so the page content is pulled down with it: scrollY
        // DECREASES (1507 -> 1342 in practice). Any real scroll proves the track never trapped
        // the gesture into a stuck drag.
        Assert.IsTrue(scrollAfter < scrollBefore - 50,
            $"a vertical swipe on the track scrolls the page ({scrollBefore} -> {scrollAfter})");
    }

    // ── keyboard: the inputs are native range inputs ────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task RangeSlider_Keyboard_ArrowRightRaisesTheStartValueByStep()
    {
        var page = await OpenAsync(1440, 900, touch: false);
        var start = FirstSlider(page).Locator(StartInput);
        await start.FocusAsync();
        await Assertions.Expect(start).ToBeFocusedAsync();

        await page.Keyboard.PressAsync("ArrowRight");
        await page.Keyboard.PressAsync("ArrowRight");
        await page.WaitForTimeoutAsync(300);

        Assert.AreEqual("22", await StartSpan(page).InnerTextAsync(), "two arrows at Step 1 raise 20 to 22");
        Assert.AreEqual("80", await EndSpan(page).InnerTextAsync(), "the end value is untouched");
        await ShotAsync(page, "1440-keyboard-start-raised");
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
