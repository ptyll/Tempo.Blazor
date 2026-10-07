using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E.Feedback;

/// <summary>
/// Review round 3, against the real demo drawers. A side panel must sit above its backdrop, a full
/// snap must survive a gesture that settles back on it, and a flick must dismiss.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class SheetRound3E2ETests : WasmTestBase
{
    private async Task<IPage> OpenAsync(int width)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            HasTouch = true,
            ViewportSize = new ViewportSize { Width = width, Height = 844 },
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/feedback", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        return page;
    }

    private static async Task OpenDrawerAsync(IPage page, string triggerId, string drawer)
    {
        var trigger = page.GetByTestId(triggerId);
        await trigger.ScrollIntoViewIfNeededAsync();
        await trigger.ClickAsync();
        await page.Locator(drawer).WaitForAsync();
        await page.Locator($"{drawer} .tm-drawer__panel").EvaluateAsync(
            "el => Promise.all(el.getAnimations().map(a => a.finished)).catch(() => {})");
    }

    /// <summary>A point of the panel, and whether the topmost element there is inside the panel.</summary>
    private static async Task<string> HitAsync(IPage page, string selector)
    {
        return await page.EvaluateAsync<string>(
            """
            (selector) => {
                const target = document.querySelector(selector);
                if (!target) return 'missing';
                const box = target.getBoundingClientRect();
                const hit = document.elementFromPoint(box.x + box.width / 2, box.y + box.height / 2);
                const panel = document.querySelector(selector)?.closest('.tm-drawer__panel')
                    ?? document.querySelector(selector);
                const inside = !!hit && panel.contains(hit);
                return (inside ? 'inside' : 'outside') + ':' + (hit?.className || hit?.tagName || 'null');
            }
            """,
            selector);
    }

    [TestMethod]
    [DataRow(390, "open-right-drawer", ".tm-drawer--right", "right-drawer-field")]
    [DataRow(1440, "open-right-drawer", ".tm-drawer--right", "right-drawer-field")]
    [DataRow(390, "open-left-drawer", ".tm-drawer--left", "left-drawer-field")]
    [DataRow(1440, "open-left-drawer", ".tm-drawer--left", "left-drawer-field")]
    public async Task SideDrawer_PanelSitsAboveTheBackdrop(int width, string triggerId, string drawer, string fieldId)
    {
        var page = await OpenAsync(width);
        await OpenDrawerAsync(page, triggerId, drawer);

        var panelHit = await HitAsync(page, $"{drawer} .tm-drawer__panel");
        Assert.IsTrue(panelHit.StartsWith("inside", StringComparison.Ordinal),
            $"elementFromPoint(panel centre) must be inside the panel, got {panelHit}");

        var closeHit = await HitAsync(page, $"{drawer} .tm-drawer__close");
        Assert.IsTrue(closeHit.StartsWith("inside", StringComparison.Ordinal),
            $"the header close must be inside the panel, got {closeHit}");

        // Typing must not close the drawer. A backdrop that paints over the field swallows the click
        // and the keystrokes never land.
        var field = page.GetByTestId(fieldId);
        await field.ClickAsync();
        await field.FillAsync("kept");
        Assert.AreEqual("kept", await field.InputValueAsync(), "a keystroke in a drawer field must land");
        Assert.IsTrue(await page.Locator(drawer).IsVisibleAsync(), "typing in a drawer field keeps it open");

        if (drawer.Contains("left", StringComparison.Ordinal))
        {
            await page.GetByTestId("left-drawer-close").ClickAsync();
        }
        else
        {
            await page.Locator($"{drawer} .tm-drawer__close").ClickAsync();
        }

        await page.Locator(drawer).WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Detached,
            Timeout = 5000,
        });
    }

    [TestMethod]
    [DataRow(1440, "open-right-drawer", ".tm-drawer--right")]
    [DataRow(1440, "open-left-drawer", ".tm-drawer--left")]
    public async Task SideDrawer_BackdropClick_IsOutsideThePanel(int width, string triggerId, string drawer)
    {
        var page = await OpenAsync(width);
        await OpenDrawerAsync(page, triggerId, drawer);
        await page.Locator(drawer).EvaluateAsync("el => Promise.all(el.getAnimations({ subtree: true }).map(a => a.finished)).catch(() => {})");

        // A click at (width/2, 8) lands inside a right drawer, so it never tests the backdrop.
        // Below 640px the right panel is full-bleed and there is no outside point; the close button
        // is the dismiss control, and a click on the panel must not hit the backdrop.
        await page.WaitForTimeoutAsync(400);
        var point = await page.EvaluateAsync<float[]>(
            """
            (drawer) => {
                const panel = document.querySelector(drawer + ' .tm-drawer__panel').getBoundingClientRect();
                const y = panel.top + panel.height / 2;
                const x = panel.left > 8 ? 4 : (panel.right + 8 < window.innerWidth ? panel.right + 8 : -1);
                if (x < 0 || (x >= panel.left && x <= panel.right)) return [-1, -1];
                return [x, y];
            }
            """,
            drawer);
        if (point[0] < 0)
        {
            var centre = await page.Locator($"{drawer} .tm-drawer__panel").BoundingBoxAsync();
            Assert.IsNotNull(centre);
            await page.Mouse.ClickAsync(centre.X + centre.Width / 2, centre.Y + centre.Height / 2);
            Assert.IsTrue(await page.Locator(drawer).IsVisibleAsync(),
                "a full-bleed panel covers the backdrop, so a click on the panel must not close it");
            await page.Locator($"{drawer} .tm-drawer__close").ClickAsync();
        }
        else
        {
            await page.Mouse.ClickAsync(point[0], point[1]);
        }

        await page.Locator(drawer).WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Detached,
            Timeout = 5000,
        });
    }

    [TestMethod]
    public async Task FullSnap_SurvivesASmallDragAndAPointerCancel()
    {
        var page = await OpenAsync(390);
        await OpenDrawerAsync(page, "open-bottom-sheet", ".tm-drawer--bottom");

        var handle = page.Locator(".tm-drawer--bottom .tm-sheet__handle");
        await handle.FocusAsync();
        await page.Keyboard.PressAsync("ArrowUp");
        await page.WaitForFunctionAsync(
            "() => document.querySelector('.tm-drawer--bottom')?.getAttribute('data-snap-index') === '1'",
            null, new PageWaitForFunctionOptions { Timeout = 5000 });

        var before = await page.Locator(".tm-drawer--bottom .tm-drawer__panel")
            .EvaluateAsync<double>("el => el.getBoundingClientRect().height");

        async Task NudgeAsync(bool cancel)
        {
            var box = await handle.BoundingBoxAsync();
            Assert.IsNotNull(box);
            var x = box.X + box.Width / 2;
            var y = box.Y + box.Height / 2;
            await page.Mouse.MoveAsync(x, y);
            await page.Mouse.DownAsync();
            for (var i = 1; i <= 8; i++)
            {
                await page.Mouse.MoveAsync(x, y + 4 * i);
            }

            if (cancel)
            {
                await page.EvaluateAsync(
                    """
                    () => document.querySelector('.tm-sheet__handle').dispatchEvent(
                        new PointerEvent('pointercancel', { bubbles: true, pointerId: 1, cancelable: true }))
                    """);
            }

            await page.Mouse.UpAsync();
            await page.WaitForTimeoutAsync(400);
        }

        await NudgeAsync(cancel: false);
        await AssertFullSnapAsync(page, before, "a small drag that settles on full");

        await NudgeAsync(cancel: true);
        await AssertFullSnapAsync(page, before, "a pointercancel");
    }

    private static async Task AssertFullSnapAsync(IPage page, double before, string afterWhat)
    {
        var panel = page.Locator(".tm-drawer--bottom .tm-drawer__panel");
        var read = await panel.EvaluateAsync<string>(
            """
            (el) => JSON.stringify({
                height: el.getBoundingClientRect().height,
                style: el.getAttribute('style') || '',
                index: el.closest('.tm-drawer')?.getAttribute('data-snap-index') || ''
            })
            """);
        var height = await panel.EvaluateAsync<double>("el => el.getBoundingClientRect().height");
        var style = await panel.EvaluateAsync<string>("el => el.getAttribute('style') || ''");
        var index = await page.Locator(".tm-drawer--bottom").GetAttributeAsync("data-snap-index");

        Assert.AreEqual("1", index, $"{afterWhat} must leave the snap index at full: {read}");
        var full = await page.Locator(".tm-drawer--bottom").EvaluateAsync<string>(
            "el => (el.getAttribute('data-snap-points') || '').split(',').at(-1)");
        Assert.IsTrue(style.Contains($"--tm-sheet-height: {full}", StringComparison.Ordinal),
            $"{afterWhat} must keep the Blazor height variable (full is capped at MaxHeight): {style}");
        // Full is min(1, max cap 0.85) of the viewport. A collapsed sheet falls back to the 0.5 default.
        Assert.AreEqual(before, height, before * 0.05,
            $"{afterWhat} must keep the full-snap height, not collapse to the 0.5 default: {read}");
    }

    [TestMethod]
    public async Task FullSnap_TapOnTheGrabber_StaysOpenAtFull()
    {
        var page = await OpenAsync(390);
        await OpenDrawerAsync(page, "open-bottom-sheet", ".tm-drawer--bottom");
        await SnapToFullAsync(page);

        var before = await page.Locator(".tm-drawer--bottom .tm-drawer__panel")
            .EvaluateAsync<double>("el => el.getBoundingClientRect().height");
        await page.Locator(".tm-drawer--bottom .tm-sheet__handle").ClickAsync();
        await page.WaitForTimeoutAsync(400);

        Assert.IsTrue(await page.Locator(".tm-drawer--bottom").IsVisibleAsync(),
            "a tap on the grabber must not close the drawer");
        await AssertFullSnapAsync(page, before, "a tap on the grabber");
    }

    [TestMethod]
    public async Task FullSnap_ShortDrag_KeepsTheSnap()
    {
        var page = await OpenAsync(390);
        await OpenDrawerAsync(page, "open-bottom-sheet", ".tm-drawer--bottom");
        await SnapToFullAsync(page);

        var before = await page.Locator(".tm-drawer--bottom .tm-drawer__panel")
            .EvaluateAsync<double>("el => el.getBoundingClientRect().height");
        var box = await page.Locator(".tm-drawer--bottom .tm-sheet__handle").BoundingBoxAsync();
        Assert.IsNotNull(box);
        var x = box.X + box.Width / 2;
        var y = box.Y + box.Height / 2;
        await page.Mouse.MoveAsync(x, y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(x, y + 30, new MouseMoveOptions { Steps = 4 });
        await page.Mouse.UpAsync();
        await page.WaitForTimeoutAsync(500);

        await AssertFullSnapAsync(page, before, "a 30px drag at full");
    }

    [TestMethod]
    [DataRow(390)]
    [DataRow(1440)]
    public async Task LongDialog_KeepsTitleAndReachesTheLastWord(int width)
    {
        var page = await OpenAsync(width);
        await page.GotoAsync($"{BaseUrl}/modal-dialog", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        if (width >= 1024)
        {
            await page.GetByTestId("toggle-sheet-presentation").CheckAsync();
        }

        await page.GetByTestId("open-long-dialog").ClickAsync();
        var dialog = page.Locator(".tm-dialog");
        await dialog.WaitForAsync();
        await dialog.EvaluateAsync("el => Promise.all(el.getAnimations().map(a => a.finished)).catch(() => {})");

        var visible = await page.EvaluateAsync<bool>(
            """
            () => {
                const title = document.querySelector('.tm-dialog-title').getBoundingClientRect();
                const footer = document.querySelector('.tm-dialog-footer').getBoundingClientRect();
                const view = window.innerHeight;
                return title.top >= -1 && title.bottom <= view + 1 && footer.top >= -1 && footer.bottom <= view + 1;
            }
            """);
        Assert.IsTrue(visible, "the title and the footer must both be inside the viewport");

        var reached = await page.EvaluateAsync<bool>(
            """
            () => {
                const content = document.querySelector('.tm-dialog-content');
                content.scrollTop = content.scrollHeight;
                const tail = content.innerText.trim().split(/\s+/).at(-1);
                return tail && content.innerText.endsWith(tail) && content.scrollTop + content.clientHeight >= content.scrollHeight - 2;
            }
            """);
        Assert.IsTrue(reached, "scrolling the dialog content must reach the last word");
    }

    [TestMethod]
    public async Task StandaloneScope_Escape_RestoresTheTrigger()
    {
        var page = await OpenAsync(390);
        await page.GotoAsync($"{BaseUrl}/modal-dialog", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        var trigger = page.GetByTestId("scope-trigger");
        await trigger.ScrollIntoViewIfNeededAsync();
        await trigger.ClickAsync();
        await page.GetByTestId("scope-input").WaitForAsync();

        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(300);

        var focused = await page.EvaluateAsync<string>("() => document.activeElement?.id ?? ''");
        Assert.AreEqual("scope-trigger", focused, "Escape must return focus to the trigger");
        var inert = await page.EvaluateAsync<int>("() => document.querySelectorAll('[inert]').length");
        Assert.AreEqual(0, inert, "closing the scope must release everything it marked inert");
    }

    [TestMethod]
    public async Task ViewManager_ThirdInstance_EscapeRestoresItsOwnToggle()
    {
        var page = await OpenAsync(1440);
        await page.GotoAsync($"{BaseUrl}/data-table", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);

        var toggles = page.Locator(".tm-view-manager-toggle");
        Assert.IsTrue(await toggles.CountAsync() >= 3, "the data-table page renders three view managers");
        var third = toggles.Nth(2);
        await third.ScrollIntoViewIfNeededAsync();
        var before = await page.EvaluateAsync<double>("() => window.scrollY");
        await third.ClickAsync();
        await page.Locator(".tm-view-manager-panel .tm-btn-primary").Last.ClickAsync();
        await page.Locator(".tm-modal").WaitForAsync();

        await page.Keyboard.PressAsync("Escape");
        await page.Locator(".tm-modal").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 5000 });

        var focusedId = await page.EvaluateAsync<string>("() => document.activeElement?.id ?? ''");
        var thirdId = await third.GetAttributeAsync("id");
        Assert.AreEqual(thirdId, focusedId, "Escape must restore the toggle that opened the modal, not the first one");
        var after = await page.EvaluateAsync<double>("() => window.scrollY");
        Assert.AreEqual(before, after, 80, "restoring focus must not jump the page to another instance");
    }

    private static async Task SnapToFullAsync(IPage page)
    {
        await page.Locator(".tm-drawer--bottom .tm-sheet__handle").FocusAsync();
        await page.Keyboard.PressAsync("ArrowUp");
        await page.WaitForFunctionAsync(
            "() => document.querySelector('.tm-drawer--bottom')?.getAttribute('data-snap-index') === '1'",
            null, new PageWaitForFunctionOptions { Timeout = 5000 });
    }

    [TestMethod]
    public async Task FastFlick_AtHalf_Dismisses()
    {
        var page = await OpenAsync(390);
        await OpenDrawerAsync(page, "open-bottom-sheet", ".tm-drawer--bottom");

        var box = await page.Locator(".tm-drawer--bottom .tm-sheet__handle").BoundingBoxAsync();
        Assert.IsNotNull(box);
        var x = box.X + box.Width / 2;
        var y = box.Y + box.Height / 2;

        // Playwright's mouse moves are too far apart to be a flick. Dispatch the pointer events in
        // one turn: their timestamps collapse, and the gesture treats a zero gap as 1ms.
        await page.EvaluateAsync(
            """
            () => {
                const handle = document.querySelector('.tm-drawer--bottom .tm-sheet__handle');
                const start = handle.getBoundingClientRect().top + handle.getBoundingClientRect().height / 2;
                const fire = (type, dy) => handle.dispatchEvent(new PointerEvent(type, {
                    bubbles: true, cancelable: true, pointerId: 1, clientY: start + dy, button: 0
                }));
                fire('pointerdown', 0);
                fire('pointermove', 12);
                fire('pointermove', 70);
                fire('pointerup', 70);
            }
            """);

        await page.Locator(".tm-drawer--bottom").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Detached,
            Timeout = 5000,
        });
    }
}
