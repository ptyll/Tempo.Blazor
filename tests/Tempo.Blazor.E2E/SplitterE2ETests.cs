using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 20 lane: the <c>TmSplitterPane</c> resizer on the real /new-components demo. The drag runs
/// through <c>tm-splitter.js</c> → the shared pointer-drag helper: the pane follows the pointer LIVE
/// (an inline flex, no .NET round trip per move), the size commits ONCE on release, Escape restores
/// the start size without committing. A finger works with no long-press (threshold 0,
/// <c>touch-action: none</c> on the strip) — driven with real CDP touch events at 768 and 390. The
/// separator is keyboard operable: arrows resize by 16px (Shift 64px), <c>Home</c>/<c>End</c> jump to
/// the parseable bounds — the WCAG 2.5.7 non-drag alternative. Diagnostic shots go to gitignored
/// TestResults, never a committed baseline.
/// </summary>
[TestClass]
public class SplitterE2ETests : WasmTestBase
{
    private const string Route = "/new-components";
    private const string Separator = ".tm-splitter .tm-splitter__resizer";
    private const string FirstPane = ".tm-splitter .tm-splitter__pane";

    private string ShotDir
        => Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "splitter");

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
        var splitter = page.Locator(".tm-splitter").First;
        await splitter.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await splitter.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(300);
        return page;
    }

    private static Task<double> PaneWidthAsync(IPage page)
        => page.EvaluateAsync<double>($"() => document.querySelector('{FirstPane}').getBoundingClientRect().width");

    private static Task<string?> PaneStyleAsync(IPage page)
        => page.Locator(FirstPane).First.GetAttributeAsync("style");

    private async Task ShotAsync(IPage page, string name, bool parkPointer = true)
    {
        // Screenshots never render the cursor, but parking the pointer at (0,0) mid-gesture would
        // BE a drag: it legitimately pulls the pane to its minimum and the release commits that.
        // Pass parkPointer: false while a pointer is held down.
        if (parkPointer) await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync("() => document.activeElement instanceof HTMLElement && document.activeElement.blur()");
        await page.WaitForTimeoutAsync(250);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(ShotDir, $"{name}.png"), FullPage = false });
    }

    // ── mouse at 1440 ───────────────────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Splitter_1440_MouseDrag_FollowsThePointerLive_AndCommitsOnceOnRelease()
    {
        var page = await OpenAsync(1440, 900, touch: false);
        var before = await PaneWidthAsync(page);
        var box = (await page.Locator(Separator).BoundingBoxAsync())!;
        var x = box.X + box.Width / 2;
        var y = box.Y + box.Height / 2;

        await page.Mouse.MoveAsync(x, y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(x + 80, y, new MouseMoveOptions { Steps = 8 });
        var during = await PaneWidthAsync(page);
        Assert.IsTrue(during > before + 40, $"the pane follows the pointer live ({before} -> {during})");
        Assert.AreEqual("true",
            await page.Locator(Separator).EvaluateAsync<string>("e => e.classList.contains('tm-splitter__resizer--dragging') ? 'true' : 'false'"),
            "the dragging class is on while the pointer is down");
        await ShotAsync(page, "1440-mouse-dragging", parkPointer: false);

        await page.Mouse.UpAsync();
        await page.WaitForTimeoutAsync(400);   // the one commit renders the px flex

        var committed = await PaneWidthAsync(page);
        Assert.IsTrue(committed > before + 40, $"the committed size survives the release ({before} -> {committed})");
        var style = await PaneStyleAsync(page);
        Assert.IsNotNull(style);
        StringAssert.Contains(style, $"flex: 0 0 {Math.Round(committed)}px", "the committed size is an exact-pixel flex");
        await Assertions.Expect(page.Locator(Separator)).ToHaveAttributeAsync("aria-valuenow", $"{Math.Round(committed)}");
        await ShotAsync(page, "1440-mouse-committed");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Splitter_1440_MouseDrag_Escape_RestoresTheStartSize_WithoutCommitting()
    {
        var page = await OpenAsync(1440, 900, touch: false);
        var before = await PaneWidthAsync(page);
        var box = (await page.Locator(Separator).BoundingBoxAsync())!;
        var x = box.X + box.Width / 2;
        var y = box.Y + box.Height / 2;

        await page.Mouse.MoveAsync(x, y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(x + 80, y, new MouseMoveOptions { Steps = 8 });
        Assert.IsTrue(await PaneWidthAsync(page) > before + 40, "precondition: the live drag moved the pane");
        await page.Keyboard.PressAsync("Escape");
        await page.Mouse.UpAsync();
        await page.WaitForTimeoutAsync(400);

        Assert.AreEqual(before, await PaneWidthAsync(page), 1.5, "Escape restores the start size");
        Assert.AreEqual(Math.Round(before).ToString(),
            await page.Locator(Separator).GetAttributeAsync("aria-valuenow"),
            "aria-valuenow reports the restored start size");
        var style = await PaneStyleAsync(page);
        Assert.IsNotNull(style);
        // IsFalse, not Assert.DoesNotContain(string, string, string): that MSTest overload
        // delegates to itself and overflows the test host the moment the line runs.
        Assert.IsFalse(style.Contains("flex: 0 0"), "no commit happened: the Blazor style has no pixel flex");
    }

    // ── touch at 768 and 390 (threshold 0: the resize starts with the first move) ───────────

    [TestMethod]
    [TestCategory("WASM")]
    [DataRow(768)]
    [DataRow(390)]
    public async Task Splitter_TouchDrag_ResizesImmediately_OnTabletAndPhone(int width)
    {
        var page = await OpenAsync(width, 1024, touch: true);
        Assert.IsTrue(await page.EvaluateAsync<bool>("() => matchMedia('(pointer: coarse)').matches"),
            "precondition: coarse pointer emulation");
        Assert.AreEqual("none",
            await page.Locator(Separator).EvaluateAsync<string>("e => getComputedStyle(e).touchAction"),
            "touch-action: none on the strip: the browser never steals the gesture for scrolling");

        var before = await PaneWidthAsync(page);
        var box = (await page.Locator(Separator).BoundingBoxAsync())!;
        var x = box.X + box.Width / 2;
        var y = box.Y + box.Height / 2;

        var session = await page.Context.NewCDPSessionAsync(page);
        await TouchAsync(session, "touchStart", x, y);
        for (var i = 1; i <= 6; i++)
        {
            await TouchAsync(session, "touchMove", x + 8 * i, y);
            await page.WaitForTimeoutAsync(16);
        }

        var during = await PaneWidthAsync(page);
        Assert.IsTrue(during > before + 20,
            $"the finger resizes with no long-press and no per-move .NET round trip ({before} -> {during})");
        await ShotAsync(page, $"{width}-touch-dragging");
        await TouchAsync(session, "touchEnd", x + 48, y);
        await page.WaitForTimeoutAsync(400);

        var committed = await PaneWidthAsync(page);
        Assert.IsTrue(committed > before + 20, $"the committed size survives the finger lift ({committed})");
        await Assertions.Expect(page.Locator(Separator)).ToHaveAttributeAsync("aria-valuenow", $"{Math.Round(committed)}");
    }

    // ── keyboard alternative (WCAG 2.5.7) ───────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Splitter_Keyboard_ArrowRightTwice_GrowsThePaneBy32_ShiftSteps64()
    {
        var page = await OpenAsync(1440, 900, touch: false);
        var separator = page.Locator(Separator);
        await separator.FocusAsync();
        await Assertions.Expect(separator).ToBeFocusedAsync();

        // The demo pane starts at 30% (no aria-valuenow yet): the first arrow commits a px size and
        // the separator starts reporting it.
        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(separator).ToHaveAttributeAsync("aria-valuenow", new Regex("^.+$"),
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 5000 });
        var before = int.Parse((await separator.GetAttributeAsync("aria-valuenow"))!);

        await page.Keyboard.PressAsync("ArrowRight");
        await page.WaitForTimeoutAsync(100);
        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(separator).ToHaveAttributeAsync("aria-valuenow", $"{before + 32}");
        Assert.AreEqual(before + 32.0, Math.Round(await PaneWidthAsync(page)), 1.5, "the pane really grew");

        await page.Keyboard.PressAsync("Shift+ArrowRight");
        await Assertions.Expect(separator).ToHaveAttributeAsync("aria-valuenow", $"{before + 96}");
        await ShotAsync(page, "1440-keyboard-resized");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Splitter_Keyboard_HomeJumpsToTheMinSizeBound()
    {
        var page = await OpenAsync(1440, 900, touch: false);
        var separator = page.Locator(Separator);
        await separator.FocusAsync();

        // MinSize="100px" on the demo pane: Home jumps straight to 100 (End is inert: no MaxSize).
        await page.Keyboard.PressAsync("Home");
        await Assertions.Expect(separator).ToHaveAttributeAsync("aria-valuenow", "100");
        Assert.AreEqual(100.0, Math.Round(await PaneWidthAsync(page)), 1.5, "the pane renders at MinSize");
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
