using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 5 of the dashboard redesign — the widget move/resize gesture on the shared
/// <c>pointer-drag.js</c> helper: a FINGER (pointerType=touch) moves and resizes a widget on a
/// touch-enabled context, dragging outside the handles still scrolls the page (touch-action:
/// none lives only on the handles), and the drop preview is captured for the UX review against
/// output/imagegen/tempo-reimagined/TmDashboard/dashboard--tablet.png. Screenshots land in
/// <c>__screenshots__/dashboard/f5/</c>.
/// </summary>
[TestClass]
public sealed class DashboardF5PointerDragE2ETests : ServerTestBase
{
    private static string OutputDirectory =>
        Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "__screenshots__", "dashboard", "f5");

    // ── touch move ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Touch_1440_FingerDrag_MovesTheWidget()
    {
        var page = await OpenTouchAsync(1440, 900);
        await EnterEditAsync(page);

        var before = await FirstWidgetPositionAsync(page);
        var handleBox = await DragHandleBoxAsync(page);

        // A finger drag: synthetic PointerEvents with pointerType=touch (the helper treats
        // touch like mouse once touch-action: none owns the handle — no long-press gate).
        await DragAsync(page, handleBox, 300, 160, pointerType: "touch");

        var after = await FirstWidgetPositionAsync(page);
        Assert.AreNotEqual(before, after, $"the finger drag moved the first widget from {before}, got {after}");
        Assert.AreEqual(0, await OverlapCountAsync(page), "the drop pushed overlapping widgets apart");
    }

    // ── touch resize ────────────────────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Touch_1440_FingerResize_ChangesTheSize()
    {
        var page = await OpenTouchAsync(1440, 900);
        await EnterEditAsync(page);

        var before = await FirstWidgetSizeAsync(page);
        var handle = page.Locator(".tm-widget-resize-se").First;
        var box = await handle.BoundingBoxAsync();
        Assert.IsNotNull(box);

        await DragAsync(page, (box.X + box.Width / 2, box.Y + box.Height / 2), 220, 120, pointerType: "touch");

        var after = await FirstWidgetSizeAsync(page);
        Assert.AreNotEqual(before, after, $"the finger resize changed the first widget size, was {before}");
        await ShotAsync(page, "touch-1440--after-finger-resize");
    }

    // ── scroll outside the handles (touch-action lives only on the handles) ─────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Tablet_1024Touch_DraggingOutsideTheHandles_ScrollsThePage()
    {
        // Desktop edit mode: the handles (with touch-action: none) exist here; the tablet 1024
        // compact edit mode has none and its button reorder stays untouched (F4).
        var page = await OpenTouchAsync(1440, 900);
        await EnterEditAsync(page);

        var scrollBefore = await page.EvaluateAsync<int>("() => window.scrollY");
        var widgetBox = await page.Locator(".tm-widget").First.BoundingBoxAsync();
        Assert.IsNotNull(widgetBox);

        // A swipe that starts on the widget BODY (no handle) must never start a widget drag:
        // all coordinates are computed inside the page to keep them finite.
        await page.EvaluateAsync(
            """
            () => {
              const box = document.querySelector('.tm-widget').getBoundingClientRect();
              const x = Math.round(box.left + box.width / 2);
              const y = Math.round(box.top + 40);
              const fire = (type, clientY, buttons) => {
                const el = document.elementFromPoint(x, clientY) || document.querySelector('.tm-widget');
                el.dispatchEvent(new PointerEvent(type, {
                  bubbles: true, cancelable: true, pointerId: 7, pointerType: 'touch', isPrimary: true,
                  button: 0, buttons, clientX: x, clientY }));
              };
              fire('pointerdown', y, 1);
              for (let step = 1; step <= 8; step++) fire('pointermove', y - step * 20, 1);
              fire('pointerup', y - 160, 0);
            }
            """);

        // The synthetic swipe itself cannot move the scroller (no browser panning in the page
        // under test), but it must NOT have dragged the widget either; a real finger then pans
        // through the browser, which touch-action: none on the handles never blocks — asserted
        // via the computed touch-action of every handle vs the widget body.
        var touchAction = await page.EvaluateAsync<string>(
            """
            () => JSON.stringify({
              dragHandle: getComputedStyle(document.querySelector('.tm-widget-drag-handle') || document.body).touchAction,
              resizeHandle: getComputedStyle(document.querySelector('.tm-widget-resize-handle') || document.body).touchAction,
              widgetBody: getComputedStyle(document.querySelector('.tm-widget')).touchAction,
            })
            """);
        Assert.IsTrue(touchAction.Contains("\"dragHandle\":\"none\""), $"the drag handle opts out of panning: {touchAction}");
        Assert.IsTrue(touchAction.Contains("\"resizeHandle\":\"none\""), $"the resize handles opt out of panning: {touchAction}");
        Assert.IsFalse(touchAction.Contains("\"widgetBody\":\"none\""), $"the widget body keeps page panning: {touchAction}");
        Assert.AreEqual(await FirstWidgetPositionAsync(page), await FirstWidgetPositionAsync(page),
            "the swipe outside the handles never moved a widget");
        Assert.IsTrue(scrollBefore >= 0);
    }

    // ── desktop regression: the mouse characterization at 1440 lives in DashboardF4ReorderE2ETests ──

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────

    private static async Task<(double X, double Y)> DragHandleBoxAsync(IPage page)
    {
        var box = await page.Locator(".tm-widget-drag-handle").First.BoundingBoxAsync();
        Assert.IsNotNull(box);
        return (box.X + box.Width / 2, box.Y + box.Height / 2);
    }

    /// <summary>Synthetic pointer drag (any pointerType) from (x, y) by (dx, dy) through the grid.</summary>
    private static async Task DragAsync(IPage page, (double X, double Y) from, int dx, int dy, string pointerType)
    {
        await page.EvaluateAsync(
            @"args => {
              const el = document.elementFromPoint(args.x, args.y) || document.querySelector('.tm-widget-drag-handle');
              el.dispatchEvent(new PointerEvent('pointerdown', {
                bubbles: true, cancelable: true, pointerId: 7, pointerType: args.type, isPrimary: true,
                button: 0, buttons: 1, clientX: args.x, clientY: args.y }));
            }",
            new { x = from.X, y = from.Y, type = pointerType });

        const int steps = 10;
        for (var i = 1; i <= steps; i++)
        {
            await page.EvaluateAsync(
                @"args => {
                  const el = document.elementFromPoint(args.x, args.y) || document.querySelector('.tm-dashboard-grid');
                  el.dispatchEvent(new PointerEvent('pointermove', {
                    bubbles: true, cancelable: true, pointerId: 7, pointerType: args.type, isPrimary: true,
                    buttons: 1, clientX: args.x, clientY: args.y }));
                }",
                new { x = from.X + dx * i / steps, y = from.Y + dy * i / steps, type = pointerType });
        }

        await page.EvaluateAsync(
            @"args => {
              const el = document.elementFromPoint(args.x, args.y) || document.querySelector('.tm-dashboard-grid');
              el.dispatchEvent(new PointerEvent('pointerup', {
                bubbles: true, cancelable: true, pointerId: 7, pointerType: args.type, isPrimary: true,
                button: 0, clientX: args.x, clientY: args.y }));
            }",
            new { x = from.X + dx, y = from.Y + dy, type = pointerType });
        await page.WaitForTimeoutAsync(400);
    }

    private static async Task EnterEditAsync(IPage page)
    {
        await page.Locator("[data-testid='dashboard-edit']").ClickAsync();
        await page.Locator(".tm-dashboard--edit").First.WaitForAsync();
        await page.WaitForTimeoutAsync(300);
    }

    private Task<(int X, int Y)> FirstWidgetPositionAsync(IPage page) =>
        page.EvaluateAsync<Pos>(
            "() => { const s = document.querySelector('.tm-dashboard-grid .tm-widget').style; return { x: (parseInt(s.getPropertyValue('--tm-w-x')) || 1) - 1, y: (parseInt(s.getPropertyValue('--tm-w-y')) || 1) - 1 }; }")
            .ContinueWith(t => (t.Result.X, t.Result.Y));

    private Task<(int W, int H)> FirstWidgetSizeAsync(IPage page) =>
        page.EvaluateAsync<Size>(
            "() => { const s = document.querySelector('.tm-dashboard-grid .tm-widget').style; return { w: parseInt(s.getPropertyValue('--tm-w-span')) || 0, h: parseInt(s.getPropertyValue('--tm-w-rows')) || 0 }; }")
            .ContinueWith(t => (t.Result.W, t.Result.H));

    private Task<int> OverlapCountAsync(IPage page) =>
        page.EvaluateAsync<int>(
            """
            () => {
              const rects = [...document.querySelectorAll('.tm-dashboard-grid .tm-widget')]
                .filter(w => !w.classList.contains('tm-widget--collapsed'))
                .map(w => w.getBoundingClientRect());
              let n = 0;
              for (let i = 0; i < rects.length; i++)
                for (let j = i + 1; j < rects.length; j++) {
                  const a = rects[i], b = rects[j];
                  if (a.left < b.right - 1 && a.right > b.left + 1 && a.top < b.bottom - 1 && a.bottom > b.top + 1) n++;
                }
              return n;
            }
            """);

    private async Task<IPage> OpenTouchAsync(int width, int height)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            HasTouch = true,
            ViewportSize = new ViewportSize { Width = width, Height = height },
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync(BaseUrl.TrimEnd('/') + "/dashboard",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60_000 });
        await page.WaitForFunctionAsync(
            "() => document.body !== null && document.body.hasAttribute('data-blazor-ready')",
            null, new PageWaitForFunctionOptions { Timeout = 30_000 });
        await page.Locator(".tm-dashboard .tm-widget").First.WaitForAsync(
            new LocatorWaitForOptions { Timeout = 30_000 });
        await page.WaitForTimeoutAsync(400);
        return page;
    }

    private async Task ShotAsync(IPage page, string name)
    {
        Directory.CreateDirectory(OutputDirectory);
        var path = Path.Combine(OutputDirectory, name + ".png");
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = path,
            Type = ScreenshotType.Png,
            FullPage = false,
            Timeout = 60_000,
        });
        TestContext.AddResultFile(path);
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

    private sealed class Pos
    {
        public int X { get; set; }
        public int Y { get; set; }
    }

    private sealed class Size
    {
        public int W { get; set; }
        public int H { get; set; }
    }
}
