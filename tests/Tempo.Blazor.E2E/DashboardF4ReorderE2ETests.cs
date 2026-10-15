using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 4 of the dashboard redesign — reordering WITHOUT drag and the keyboard alternative
/// (WCAG 2.1.1 / 2.5.7): a mouse-drag/resize characterization on desktop 1440 (the safety net
/// for the existing JS drag), the keyboard grab mode on the new drag-handle button, the compact
/// edit mode (move up / move down / remove) on mobile 390/320 and tablet 1024 touch, and the
/// DA2 check that a mobile swap equals the desktop swap after the viewport grows. Screenshots
/// land in <c>__screenshots__/dashboard/f4/</c> for the UX review against
/// output/imagegen/tempo-reimagined/TmDashboard/dashboard--mobile.png and dashboard--tablet.png.
/// </summary>
[TestClass]
public sealed class DashboardF4ReorderE2ETests : ServerTestBase
{
    private static string OutputDirectory =>
        Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "__screenshots__", "dashboard", "f4");

    // ── Characterization: the mouse drag/resize must keep working (F5 safety net) ──

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Desktop_1440_MouseDrag_MovesTheWidget_AndPushesOverlap()
    {
        var page = await OpenDashboardAsync(1440, 900);
        await EnterEditAsync(page);

        var before = await FirstWidgetPositionAsync(page);
        Assert.AreEqual((0, 0), before, "sanity: the demo's first widget starts at (0,0)");

        // Fáze 5: the move gesture is pointer-drag.js now — a REAL mouse drag on the handle
        // (Playwright's mouse dispatches pointerdown/move/up), not a synthetic HTML5 drag.
        // The drop preview follows the pointer and the drop commits through Blazor.
        var handle = page.Locator(".tm-widget-drag-handle").First;
        var handleBox = await handle.BoundingBoxAsync();
        Assert.IsNotNull(handleBox);
        var gridBox = await page.Locator(".tm-dashboard-grid").BoundingBoxAsync();
        Assert.IsNotNull(gridBox);
        await page.Mouse.MoveAsync(handleBox.X + handleBox.Width / 2, handleBox.Y + handleBox.Height / 2);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)(gridBox.X + gridBox.Width * 0.45), (float)(gridBox.Y + gridBox.Height * 0.4));
        await page.Mouse.MoveAsync((float)(gridBox.X + gridBox.Width * 0.9), (float)(gridBox.Y + gridBox.Height * 0.8));
        await page.WaitForTimeoutAsync(150);
        await ShotAsync(page, "desktop-1440--drop-preview");
        await page.Mouse.UpAsync();
        await page.WaitForTimeoutAsync(400);

        var after = await FirstWidgetPositionAsync(page);
        Assert.AreNotEqual(before, after, $"the mouse drag moved the first widget from {before}, got {after}");
        Assert.AreEqual(0, await OverlapCountAsync(page), "the drop pushed overlapping widgets apart");
        await ShotAsync(page, "desktop-1440--after-mouse-drag");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Desktop_1440_MouseResize_ChangesTheSize()
    {
        var page = await OpenDashboardAsync(1440, 900);
        await EnterEditAsync(page);

        var before = await FirstWidgetSizeAsync(page);

        // dashboard.js resize (Fáze 5: pointer-drag.js — Playwright's mouse dispatches pointer
        // events): press on the SE handle, move, release commits via Blazor.
        // The sibling S handle paints over the SE corner, so the hover is forced (actionability
        // would see it intercepted) — the coordinates are what matters for the drag.
        await page.Locator(".tm-widget-resize-se").First.HoverAsync(new LocatorHoverOptions
        {
            Force = true,
        });
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(1200, 600);
        await page.Mouse.UpAsync();
        await page.WaitForTimeoutAsync(400);

        var after = await FirstWidgetSizeAsync(page);
        Assert.AreNotEqual(before, after, $"the mouse resize changed the first widget size, was {before}");
    }

    // ── Keyboard grab mode (desktop, every width) ──

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Desktop_1440_Keyboard_EnterGrabs_ArrowMoves_EnterDrops()
    {
        var page = await OpenDashboardAsync(1440, 900);
        await EnterEditAsync(page);

        var handle = page.Locator(".tm-widget-drag-handle").First;
        var before = await FirstWidgetPositionAsync(page);

        await handle.FocusAsync();
        Assert.AreEqual("false", await handle.GetAttributeAsync("aria-pressed"));

        await page.Keyboard.PressAsync("Enter");
        Assert.AreEqual("true", await handle.GetAttributeAsync("aria-pressed"), "Enter grabs the widget");
        Assert.IsFalse(await page.Locator(".tm-dashboard-live-region").First.TextContentAsync() == "",
            "the grab is announced through the live region");

        await page.Keyboard.PressAsync("ArrowRight");
        var moved = await FirstWidgetPositionAsync(page);
        Assert.AreEqual(before.X + 1, moved.X, "ArrowRight moves the grabbed widget one column");
        Assert.AreEqual(before.Y, moved.Y);

        await page.Keyboard.PressAsync("Enter");
        Assert.AreEqual("false", await handle.GetAttributeAsync("aria-pressed"), "Enter drops the widget");
        Assert.AreEqual(moved, await FirstWidgetPositionAsync(page), "the drop keeps the keyboard position");
        await ShotAsync(page, "desktop-1440--after-keyboard-move");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Desktop_1440_Keyboard_ShiftArrow_Resizes_EscapeRestores()
    {
        var page = await OpenDashboardAsync(1440, 900);
        await EnterEditAsync(page);

        var handle = page.Locator(".tm-widget-drag-handle").First;
        await handle.FocusAsync();
        await page.Keyboard.PressAsync("Enter");

        await page.Keyboard.PressAsync("Shift+ArrowDown");
        var grown = await FirstWidgetSizeAsync(page);

        await page.Keyboard.PressAsync("Shift+ArrowRight");
        var wider = await FirstWidgetSizeAsync(page);
        Assert.AreEqual(grown.Item2, wider.Item2);
        Assert.AreEqual(grown.Item1 + 1, wider.Item1, "Shift+ArrowRight widens the grabbed widget one column");

        await page.Keyboard.PressAsync("Escape");
        Assert.AreEqual("false", await handle.GetAttributeAsync("aria-pressed"), "Escape cancels the grab");
        Assert.IsFalse((await FirstWidgetSizeAsync(page)).Equals(wider), "Escape restores the original size");
        await ShotAsync(page, "desktop-1440--after-keyboard-resize-escape");
    }

    // ── Compact edit mode: mobile reorder without drag ──

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Mobile_390_MoveButtonsReorder_AndMatchTheDesktopSwapAt1440()
    {
        var page = await OpenDashboardAsync(390, 844);
        Assert.AreEqual("mobile", await LayoutAsync(page));
        await EnterEditAsync(page);

        Assert.IsTrue(await page.Locator(".tm-widget-move-btn--up").First.IsVisibleAsync(),
            "the compact edit mode renders the move buttons");
        Assert.AreEqual(0, await page.Locator(".tm-widget-drag-handle").CountAsync(),
            "the compact edit mode has no drag handles");
        Assert.AreEqual(0, await page.Locator(".tm-widget-resize-handle").CountAsync(),
            "the compact edit mode has no resize handles");

        var order = await WidgetIdsAsync(page);
        var movedId = order[0];
        var neighbourId = order[1];
        var movedBefore = await PositionOfAsync(page, movedId);
        var neighbourBefore = await PositionOfAsync(page, neighbourId);

        // Move the FIRST widget down: it must take the neighbour's (X, Y) (DA2 swap).
        await page.Locator("[data-instance-id='" + movedId + "'] .tm-widget-move-btn--down").ClickAsync();
        await page.WaitForTimeoutAsync(300);

        var movedAfter = await PositionOfAsync(page, movedId);
        var neighbourAfter = await PositionOfAsync(page, neighbourId);
        Assert.AreEqual(neighbourBefore, movedAfter, "the moved widget swapped (X, Y) with its (Y, X) neighbour");
        Assert.AreEqual(movedBefore, neighbourAfter, "the neighbour took the moved widget's old position");

        var orderAfter = await WidgetIdsAsync(page);
        CollectionAssert.AreNotEqual(order, orderAfter, "the DOM re-sorted after the swap");
        await ShotAsync(page, "mobile-390--compact-edit-after-swap");

        // Grow to desktop: the swap result reads the same on the desktop grid (DA2).
        await page.SetViewportSizeAsync(1440, 900);
        await page.WaitForTimeoutAsync(400);
        Assert.AreEqual("desktop", await LayoutAsync(page));
        Assert.AreEqual(movedAfter, await PositionOfAsync(page, movedId), "the mobile swap equals the desktop swap (DA2)");
        Assert.AreEqual(neighbourAfter, await PositionOfAsync(page, neighbourId), "the neighbour kept the swapped position at 1440");
        await ShotAsync(page, "desktop-1440--after-mobile-swap");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Mobile_390_RemoveButton_RemovesTheWidget()
    {
        var page = await OpenDashboardAsync(390, 844);
        await EnterEditAsync(page);

        var countBefore = (await WidgetIdsAsync(page)).Length;
        await page.Locator(".tm-widget-btn--danger").First.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        Assert.AreEqual(countBefore - 1, (await WidgetIdsAsync(page)).Length, "Remove takes the widget off the dashboard");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Mobile_390_AddedWidget_IsScrolledIntoView_AndHighlighted()
    {
        var page = await OpenDashboardAsync(390, 844);
        await EnterEditAsync(page);

        // The mobile action bar owns the edit actions — open the widget selector from there.
        var addTile = page.Locator("[data-testid='dashboard-action-bar'] button")
            .Filter(new LocatorFilterOptions { HasText = "Add widget" });
        await addTile.ClickAsync();
        await page.Locator(".tm-widget-card").First.ClickAsync(new LocatorClickOptions { Timeout = 15_000 });

        // The fresh card carries the brief highlight; the scroll-into-view helper in dashboard.js
        // (smooth, animated) must bring it inside the 390×844 viewport — poll while it animates.
        // The added widget renders last in the (Y, X) order; the highlight class itself expires
        // after ~1.6 s and must not gate the assertion.
        var fresh = page.Locator(".tm-widget--highlight").First;
        await fresh.WaitForAsync(new LocatorWaitForOptions { Timeout = 5_000 });
        try
        {
            await page.WaitForFunctionAsync(
                """
                () => {
                  const el = document.querySelector('.tm-dashboard-grid .tm-widget:last-child');
                  if (!el) return false;
                  const r = el.getBoundingClientRect();
                  return r.top >= 0 && r.bottom <= window.innerHeight + 1;
                }
                """, null, new PageWaitForFunctionOptions { Timeout = 5_000 });
        }
        catch (TimeoutException)
        {
            var diag = await page.EvaluateAsync<string>(
                """
                () => {
                  const el = document.querySelector('.tm-dashboard-grid .tm-widget:last-child');
                  const r = el ? el.getBoundingClientRect() : null;
                  return JSON.stringify({
                    hasHelper: typeof window.tempoDashboard !== 'undefined'
                      && typeof window.tempoDashboard.scrollWidgetIntoView === 'function',
                    scrollY: window.scrollY,
                    rect: r ? { top: r.top, bottom: r.bottom } : null,
                    innerH: window.innerHeight,
                  });
                }
                """);
            Assert.Fail($"the added widget never entered the viewport; diagnostics: {diag}");
        }
        var box = await fresh.BoundingBoxAsync();
        Assert.IsNotNull(box);
        Assert.IsTrue(box.Y >= 0 && box.Y + box.Height <= 845,
            $"the freshly added widget must be visible in the 390×844 viewport, got Y={box.Y:0} H={box.Height:0}");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Mobile_320_MoveButtonsAreReachable_AndLiveRegionAnnounces()
    {
        var page = await OpenDashboardAsync(320, 844, culture: "fr");
        await EnterEditAsync(page);

        var box = await page.Locator(".tm-widget-move-btn--down").First.BoundingBoxAsync();
        Assert.IsNotNull(box);
        Assert.IsTrue(box.Height >= 44, $"the move buttons are 44px targets at 320, got {box.Height:0}");

        await page.Locator(".tm-widget-move-btn--down").First.ClickAsync();
        Assert.IsFalse(await page.Locator(".tm-dashboard-live-region").First.TextContentAsync() == "",
            "move actions feed the polite live region (fr culture renders the localized copy)");
        await ShotAsync(page, "mobile-320-fr--compact-edit");
    }

    // ── Tablet touch ──

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Tablet_1024Touch_CompactEdit_ShowsMoveButtons_AtLeast44px()
    {
        var page = await OpenDashboardAsync(1024, 768, touch: true);
        await EnterEditAsync(page);

        Assert.AreEqual(0, await page.Locator(".tm-widget-resize-handle").CountAsync(),
            "resize stays desktop-only on the tablet");
        Assert.IsTrue(await page.Locator(".tm-widget-move-btn--up").First.IsVisibleAsync(),
            "the compact edit mode gives the tablet the button-based reorder");

        var up = await page.Locator(".tm-widget-move-btn--up").First.BoundingBoxAsync();
        Assert.IsNotNull(up);
        Assert.IsTrue(up.Height >= 44, $"the move buttons are 44px touch targets, got {up.Height:0}");
        await ShotAsync(page, "tablet-1024-touch--compact-edit");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static async Task EnterEditAsync(IPage page)
    {
        await page.Locator("[data-testid='dashboard-edit']").ClickAsync();
        await page.Locator(".tm-dashboard--edit").First.WaitForAsync();
        await page.WaitForTimeoutAsync(250);
    }

    private Task<string[]> WidgetIdsAsync(IPage page) =>
        page.EvaluateAsync<string[]>(
            "() => [...document.querySelectorAll('.tm-dashboard-grid .tm-widget')].map(w => w.dataset.instanceId)");

    private Task<(int X, int Y)> FirstWidgetPositionAsync(IPage page) =>
        page.EvaluateAsync<Pos>(
            "() => { const s = document.querySelector('.tm-dashboard-grid .tm-widget').style; return { x: (parseInt(s.getPropertyValue('--tm-w-x')) || 1) - 1, y: (parseInt(s.getPropertyValue('--tm-w-y')) || 1) - 1 }; }")
            .ContinueWith(t => (t.Result.X, t.Result.Y));

    private Task<(int W, int H)> FirstWidgetSizeAsync(IPage page) =>
        page.EvaluateAsync<Size>(
            "() => { const s = document.querySelector('.tm-dashboard-grid .tm-widget').style; return { w: parseInt(s.getPropertyValue('--tm-w-span')) || 0, h: parseInt(s.getPropertyValue('--tm-w-rows')) || 0 }; }")
            .ContinueWith(t => (t.Result.W, t.Result.H));

    private async Task<(int X, int Y)> PositionOfAsync(IPage page, string instanceId) =>
        await page.EvaluateAsync<Pos>(
            "id => { const s = document.querySelector(`[data-instance-id='${id}']`).style; return { x: (parseInt(s.getPropertyValue('--tm-w-x')) || 1) - 1, y: (parseInt(s.getPropertyValue('--tm-w-y')) || 1) - 1 }; }",
            instanceId).ContinueWith(t => (t.Result.X, t.Result.Y));

    /// <summary>Count of widget pairs whose grid rectangles overlap (AllowOverlap=false sanity).</summary>
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

    private async Task<IPage> OpenDashboardAsync(int width, int height, bool touch = false, string? culture = null)
    {
        IPage page;
        if (touch)
        {
            var context = await Browser.NewContextAsync(new BrowserNewContextOptions
            {
                HasTouch = true,
                ViewportSize = new ViewportSize { Width = width, Height = height },
                Locale = "en-US",
                IgnoreHTTPSErrors = true,
            });
            RegisterContext(context);
            page = await context.NewPageAsync();
        }
        else
        {
            page = await CreatePageAsync();
            await page.SetViewportSizeAsync(width, height);
        }

        if (culture is not null)
        {
            await page.AddInitScriptAsync($"window.localStorage.setItem('tm-demo-culture','{culture}');");
        }

        await page.GotoAsync(BaseUrl.TrimEnd('/') + "/dashboard",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60_000 });
        await page.WaitForFunctionAsync(
            "() => document.body !== null && document.body.hasAttribute('data-blazor-ready')",
            null, new PageWaitForFunctionOptions { Timeout = 30_000 });
        await page.Locator(".tm-dashboard .tm-widget").First.WaitForAsync(
            new LocatorWaitForOptions { Timeout = 30_000 });
        await page.WaitForTimeoutAsync(400);
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync("() => document.activeElement && document.activeElement.blur()");
        return page;
    }

    private Task<string> LayoutAsync(IPage page) =>
        page.Locator(".tm-dashboard").First.GetAttributeAsync("data-layout")!;

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
