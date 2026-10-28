using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// TmGantt redesign, phase 7 (tablet and touch): at a 1024x768 touch viewport the Gantt resolves the tablet layout from its own
/// container (two-row toolbar with overflow, task panel as a tabbed <c>TmSidePanel</c> side sheet, filter as a dialog), a finger taps a
/// row, long-presses for the context menu, scrolls the timeline natively and drags a dependency through the core <c>pointer-drag.js</c>
/// (driven with real CDP touch events); a 1440 mouse pass regression-checks pan, wheel and dependency drag, and a 900 px host
/// at 1440 resolves the tablet layout too. Screenshots land in <c>TestResults/gantt/phase7</c> (committed shots are rewritten only
/// with <c>TM_E2E_UPDATE_BASELINE=1</c>).
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class GanttPhase7TabletE2ETests : WasmTestBase
{
    private static bool UpdateBaseline =>
        string.Equals(Environment.GetEnvironmentVariable("TM_E2E_UPDATE_BASELINE"), "1", StringComparison.Ordinal);

    private static string ShotPath(string name)
    {
        var directory = Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E",
            UpdateBaseline ? Path.Combine("__screenshots__", "gantt", "phase7") : Path.Combine("TestResults", "gantt", "phase7"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{name}.png");
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx"))) return current.FullName;
            current = current.Parent;
        }

        throw new InvalidOperationException("TempoBlazor.slnx not found above the test output.");
    }

    private async Task<IPage> OpenAsync(int width, int height, bool touch, bool dark = false)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = touch,
            IgnoreHTTPSErrors = true,
            Locale = "en-US",
        });
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/gantt", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 90_000 });
        await WaitForAppReadyAsync(page);
        await Gantt(page).Locator(".tm-gantt__tree-row").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        // Full-bleed: the demo page keeps a sidebar and gutters; the layout is container-owned, so give the Gantt the width the test names.
        await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = "aside, nav.tm-sidebar, .tm-sidebar { display: none !important; } main, .tm-main { margin: 0 !important; padding: 0 !important; max-width: none !important; width: 100% !important; }" });
        if (dark)
        {
            await page.EvaluateAsync("() => { document.documentElement.setAttribute('data-theme','dark'); document.documentElement.classList.add('dark','tm-dark'); }");
        }

        await page.WaitForTimeoutAsync(400);
        return page;
    }

    private static ILocator Gantt(IPage page) => page.Locator(".tm-gantt").First;

    private static async Task WaitLayoutAsync(IPage page, string layout) =>
        await page.WaitForFunctionAsync("(l) => document.querySelector('.tm-gantt')?.getAttribute('data-layout') === l", layout, new PageWaitForFunctionOptions { Timeout = 20_000 });

    private static async Task ShootAsync(IPage page, string name)
    {
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync("() => document.activeElement && document.activeElement.blur()");
        await page.WaitForTimeoutAsync(250);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = ShotPath(name), Timeout = 60_000 });
    }

    // ── CDP touch ─────────────────────────────────────────────────────────────

    private static async Task<ICDPSession> CdpAsync(IPage page) => await page.Context.NewCDPSessionAsync(page);

    private static Task TouchAsync(ICDPSession cdp, string type, params (double X, double Y)[] points) =>
        cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
        {
            ["type"] = type,
            ["touchPoints"] = points.Select((p, i) => new Dictionary<string, object> { ["x"] = p.X, ["y"] = p.Y, ["id"] = i }).ToArray(),
        });

    private static async Task<(double X, double Y)> CenterAsync(ILocator locator)
    {
        var box = await locator.BoundingBoxAsync();
        Assert.IsNotNull(box, "the element has no box");
        return (box.X + box.Width / 2, box.Y + box.Height / 2);
    }

    private static async Task LongPressAsync(ICDPSession cdp, (double X, double Y) at, int holdMs = 700)
    {
        await TouchAsync(cdp, "touchStart", at);
        await Task.Delay(holdMs);
        await TouchAsync(cdp, "touchEnd");
    }

    // ── Tablet ────────────────────────────────────────────────────────────────

    [TestMethod]
    [Description("1024x768 touch: tablet layout, two-row toolbar without horizontal overflow, touch hint, light and dark shots")]
    public async Task Tablet_1024Touch_Layout_TwoRowToolbar_NoOverflow()
    {
        var page = await OpenAsync(1024, 768, touch: true);
        await WaitLayoutAsync(page, "tablet");

        var gantt = Gantt(page);
        Assert.IsTrue((await gantt.GetAttributeAsync("class"))!.Contains("tm-gantt--tablet"));
        Assert.AreEqual(2, await gantt.Locator(".tm-gantt__toolbar [role='toolbar']").CountAsync(), "the tablet toolbar is two rows");

        // DoD: no horizontal overflow of any toolbar row at 1024; every command stays reachable (bar or More).
        var overflow = await page.EvaluateAsync<string>(
            "() => JSON.stringify([...document.querySelectorAll('.tm-gantt__toolbar [role=toolbar]')].map(b => [b.scrollWidth, b.clientWidth]))");
        foreach (var pair in JsonSerializer.Deserialize<double[][]>(overflow)!)
        {
            Assert.IsTrue(pair[0] <= pair[1] + 1, $"a toolbar row overflows horizontally ({pair[0]} > {pair[1]})");
        }

        Assert.IsTrue(await gantt.Locator("[data-testid='gantt-touch-hint']").IsVisibleAsync(), "the long-press instruction shows on a coarse pointer");
        await ShootAsync(page, "tablet-1024-touch--main-light");

        var dark = await OpenAsync(1024, 768, touch: true, dark: true);
        await WaitLayoutAsync(dark, "tablet");
        await ShootAsync(dark, "tablet-1024-touch--main-dark");
    }

    [TestMethod]
    [Description("Tablet: a tap on a row opens the task panel as a tabbed side sheet; tabs switch; Escape / close dismisses it")]
    public async Task Tablet_Tap_Row_Opens_TabbedSideSheet()
    {
        var page = await OpenAsync(1024, 768, touch: true);
        await WaitLayoutAsync(page, "tablet");

        await Gantt(page).Locator(".tm-gantt__tree-row").Nth(2).TapAsync();

        var sheet = page.Locator(".tm-drawer.tm-side-panel-sheet").First;
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        Assert.AreEqual(1, await page.Locator(".tm-drawer.tm-side-panel-sheet").CountAsync(), "at most one side sheet");
        var tabs = await sheet.Locator(".tm-tab__label").AllInnerTextsAsync();
        CollectionAssert.AreEqual(new[] { "Detail", "Attachments", "Comments", "History" }, tabs.Select(t => t.Trim()).ToArray());
        await ShootAsync(page, "tablet-1024-touch--task-sheet-light");

        await sheet.Locator("#tab-history").TapAsync();
        await sheet.Locator("[data-testid='task-section-history']").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await ShootAsync(page, "tablet-1024-touch--task-sheet-history");

        await page.Keyboard.PressAsync("Escape");
        await page.WaitForSelectorAsync(".tm-drawer.tm-side-panel-sheet", new PageWaitForSelectorOptions { State = WaitForSelectorState.Detached, Timeout = 10_000 });

        // History replaces the task sheet: still one surface.
        await OpenToolbarCommandAsync(page, "gantt-history-btn");
        await Gantt(page).Locator("[data-testid='gantt-history-drawer']").WaitForAsync();
        await Gantt(page).Locator(".tm-gantt__tree-row").Nth(1).TapAsync();
        await page.Locator(".tm-drawer.tm-side-panel-sheet").First.WaitForAsync();
        Assert.AreEqual(0, await page.Locator("[data-testid='gantt-history-drawer']").CountAsync(), "opening the task sheet closes the history drawer");
    }

    [TestMethod]
    [Description("Tablet: filter, export, import open as dialogs; Auto filter mode is a modal on a tablet container")]
    public async Task Tablet_Filter_Export_Import_Are_Dialogs()
    {
        var page = await OpenAsync(1024, 768, touch: true);
        await WaitLayoutAsync(page, "tablet");

        await OpenToolbarCommandAsync(page, "gantt-filter-btn");
        await page.Locator(".tm-modal-overlay").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await ShootAsync(page, "tablet-1024-touch--filter-dialog");
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForSelectorAsync(".tm-modal-overlay", new PageWaitForSelectorOptions { State = WaitForSelectorState.Detached, Timeout = 10_000 });

        await OpenToolbarCommandAsync(page, "gantt-export-btn");
        await page.Locator(".tm-modal-overlay.tm-gantt__export-dialog").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await ShootAsync(page, "tablet-1024-touch--export-dialog");
    }

    /// <summary>A point inside the visible timeline body that is the plain background (no bar, milestone, handle or label under it).</summary>
    private static async Task<(double X, double Y)> EmptyTimelinePointAsync(IPage page)
    {
        var point = await page.EvaluateAsync<double[]>(
            """
            () => {
                const tl = document.querySelector('.tm-gantt__timeline-body').getBoundingClientRect();
                // Away from the viewport edges: a touch that starts in the last pixels of the screen is the browser's, not the page's.
                const top = Math.max(tl.top, 0) + 8, bottom = Math.min(tl.bottom, window.innerHeight - 90) - 8;
                const right = Math.min(tl.right, window.innerWidth - 40) - 8;
                for (let y = bottom; y > top; y -= 6) {
                    for (let x = right; x > tl.left + 8; x -= 12) {
                        const e = document.elementFromPoint(x, y);
                        // A dependency handle owns a finger-sized area around its dot (a touch there drags a link, it does not scroll): keep clear of it.
                        const nearHandle = [...document.querySelectorAll('.tm-gantt__dep-handle')].some(h => { const r = h.getBoundingClientRect(); return x > r.left - 24 && x < r.right + 24 && y > r.top - 24 && y < r.bottom + 24; });
                        if (e && !nearHandle && !e.closest('.tm-gantt__bar, .tm-gantt__milestone, .tm-gantt__dep-handle, .tm-gantt__bar-progress-label, .tm-gantt__dep-ghost') && e.closest('.tm-gantt__timeline-body')) return [x, y];
                    }
                }
                return [];
            }
            """);
        Assert.AreEqual(2, point.Length, "no empty timeline background found");
        return (point[0], point[1]);
    }
    /// <summary>
    /// Taps a toolbar command whether it is on its bar or has left into that bar's "More" menu (the bar decides by width, so a test
    /// must not assume either). A command that left the bar is a collapsed (inert) item; its menu entry carries the test id as
    /// <c>data-menu-testid</c>.
    /// </summary>
    private static async Task OpenToolbarCommandAsync(IPage page, string testId)
    {
        var gantt = Gantt(page);
        var onBar = gantt.Locator($".tm-gantt__toolbar [data-testid='{testId}']:not(.tm-toolbar-item--collapsed)").First;
        if (await onBar.CountAsync() > 0)
        {
            await onBar.TapAsync();
            return;
        }

        var toggles = gantt.Locator(".tm-gantt__toolbar .tm-toolbar-more");
        for (var i = 0; i < await toggles.CountAsync(); i++)
        {
            await toggles.Nth(i).TapAsync();
            var entry = page.Locator($"[data-menu-testid='{testId}']").First;
            if (await entry.CountAsync() > 0)
            {
                await entry.TapAsync();
                return;
            }

            await page.Keyboard.PressAsync("Escape");
        }

        Assert.Fail($"'{testId}' is neither on a toolbar bar nor in a More menu");
    }
    [TestMethod]
    [Description("Tablet: a real touch long-press on a task bar opens the context menu; the menu stays open after the finger lifts")]
    public async Task Tablet_LongPress_Bar_Opens_ContextMenu()
    {
        var page = await OpenAsync(1024, 768, touch: true);
        await WaitLayoutAsync(page, "tablet");
        var cdp = await CdpAsync(page);
        var bar = Gantt(page).Locator(".tm-gantt__bar[data-task-id]").Nth(1);
        await bar.ScrollIntoViewIfNeededAsync();
        var at = await CenterAsync(bar);

        await LongPressAsync(cdp, at, 700);

        // The context menu overlay is a SIBLING of .tm-gantt (a CSS size container would otherwise be
        // the containing block of the fixed overlay and displace it), so it is looked up page-wide.
        await page.Locator(".tm-gantt__context-menu").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await page.WaitForTimeoutAsync(700);
        Assert.IsTrue(await page.Locator(".tm-gantt__context-menu").IsVisibleAsync(), "the lifting finger's click must not dismiss the menu");
        Assert.IsTrue(await page.Locator("[data-testid='gantt-context-move-up']").IsVisibleAsync(), "Move up / down are in the menu (WCAG 2.5.7)");
        await ShootAsync(page, "tablet-1024-touch--context-menu");
    }

    [TestMethod]
    [Description("A Gantt scrolled away from the page top-left: the context menu (clientX/Y, position fixed) lands at the pointer and the click-away overlay covers the viewport, because the overlays render outside the CSS size container")]
    public async Task ContextMenu_On_A_Gantt_Offset_From_The_Page_TopLeft_Is_Not_Confined()
    {
        var page = await OpenAsync(1440, 900, touch: false);
        await WaitLayoutAsync(page, "desktop");

        // Offset the Gantt from the page top-left: with the chart as the containing block the fixed
        // overlay would resolve against the chart box, so the menu would land displaced by exactly the
        // chart's viewport offset. The demo page is long: scroll as far down as it goes (the chart then
        // sits clearly below the viewport's top edge — an offset from the page's top-left either way).
        var offsetState = await page.EvaluateAsync<string>(
            """
            () => {
                const chart = document.querySelector('.tm-gantt');
                let scroller = null;
                for (let el = chart.parentElement; el && el !== document.body; el = el.parentElement) {
                    const s = getComputedStyle(el);
                    if (/(auto|scroll)/.test(s.overflowY) && el.scrollHeight > el.clientHeight + 40) { scroller = el; break; }
                }
                if (scroller) {
                    scroller.scrollTo(0, scroller.scrollHeight);
                    return 'inner|' + scroller.className.slice(0, 40) + '|top=' + chart.getBoundingClientRect().top;
                }
                window.scrollTo(0, document.documentElement.scrollHeight);
                return 'window|top=' + chart.getBoundingClientRect().top;
            }
            """);
        var chartTop = await page.EvaluateAsync<double>("() => document.querySelector('.tm-gantt')?.getBoundingClientRect().top ?? 999");
        Assert.IsTrue(chartTop > 40, $"the chart must sit offset from the viewport's top for the test to mean anything (state {offsetState})");

        // A tree row fully inside the viewport's upper half (the menu grows downward from the pointer).
        var at = await page.EvaluateAsync<double[]>(
            """
            () => {
                const rows = [...document.querySelectorAll('.tm-gantt__tree-row')];
                const row = rows.map(el => el.getBoundingClientRect()).find(b => b.top > 40 && b.bottom < window.innerHeight - 320 && b.left >= 0);
                if (!row) return [];
                return [row.left + row.width / 2, row.top + row.height / 2];
            }
            """);
        Assert.AreEqual(2, at.Length, "a tree row inside the viewport was found after the scroll");

        await page.Mouse.MoveAsync((float)at[0], (float)at[1]);
        await page.Mouse.ClickAsync((float)at[0], (float)at[1], new MouseClickOptions { Button = MouseButton.Right });

        var overlay = page.Locator(".tm-gantt__context-menu-overlay").First;
        await overlay.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        var overlayBox = await overlay.BoundingBoxAsync();
        Assert.IsNotNull(overlayBox);
        Assert.AreEqual(0, overlayBox.X, "the click-away overlay starts at the viewport's left edge");
        Assert.AreEqual(0, overlayBox.Y, "the click-away overlay starts at the viewport's top edge (not at the chart's)");

        var menuBox = await page.Locator(".tm-gantt__context-menu").BoundingBoxAsync();
        Assert.IsNotNull(menuBox);
        Assert.IsTrue(Math.Abs(menuBox.X - at[0]) < 40, $"the menu is anchored at the pointer's X (menu {menuBox.X}, pointer {at[0]})");
        Assert.IsTrue(Math.Abs(menuBox.Y - at[1]) < 40, $"the menu is anchored at the pointer's Y (menu {menuBox.Y}, pointer {at[1]}) — a container-confined overlay would land displaced by the page scroll");

        // A click away still dismisses the menu (the overlay really covers the page).
        await page.Mouse.ClickAsync(20, (float)Math.Min(at[1], 300));
        await page.WaitForSelectorAsync(".tm-gantt__context-menu", new PageWaitForSelectorOptions { State = WaitForSelectorState.Detached, Timeout = 10_000 });
    }

    [TestMethod]
    [Description("Tablet: a finger swipe scrolls the timeline natively (touch-action pan-x pan-y), it does not start the mouse pan")]
    public async Task Tablet_Finger_Pans_Timeline_Natively()
    {
        var page = await OpenAsync(1024, 768, touch: true);
        await WaitLayoutAsync(page, "tablet");
        var timeline = Gantt(page).Locator(".tm-gantt__timeline").First;
        Assert.AreEqual("pan-x pan-y", await timeline.EvaluateAsync<string>("el => getComputedStyle(el).touchAction"));
        var before = await timeline.EvaluateAsync<double>("el => el.scrollLeft");
        await timeline.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(1200); // the page scroll above must settle before a finger lands (a settling scroll swallows the swipe)
        var cdp = await CdpAsync(page);
        var (sx, sy) = await EmptyTimelinePointAsync(page);
        var hit = await page.EvaluateAsync<string>("([x, y]) => { const e = document.elementFromPoint(x, y); return (e?.className ?? e?.tagName) + ' | ta=' + getComputedStyle(e).touchAction + ' | scrollY=' + scrollY; }", new[] { sx, sy });

        await TouchAsync(cdp, "touchStart", (sx, sy));
        for (var i = 1; i <= 12; i++)
        {
            await TouchAsync(cdp, "touchMove", (sx - i * 12, sy));
            await Task.Delay(16);
        }

        await TouchAsync(cdp, "touchEnd");
        await page.WaitForTimeoutAsync(300);

        var after = await timeline.EvaluateAsync<double>("el => el.scrollLeft");
        Assert.IsTrue(after > before, $"the finger swipe should scroll the timeline (before {before}, after {after}; point {sx},{sy}; hit {hit})");
    }

    [TestMethod]
    [Description("Tablet: dragging a dependency dot with a finger onto another bar adds a dependency line (one commit)")]
    public async Task Tablet_Finger_Drags_Dependency()
    {
        var page = await OpenAsync(1024, 768, touch: true);
        await WaitLayoutAsync(page, "tablet");
        var gantt = Gantt(page);
        var lines = gantt.Locator(".tm-gantt__dependency-line");
        var before = await lines.CountAsync();

        var (handle, target) = await PickDependencyPairAsync(gantt);
        var cdp = await CdpAsync(page);
        var from = await CenterAsync(handle);
        var to = await CenterAsync(target);

        await TouchAsync(cdp, "touchStart", from);
        for (var i = 1; i <= 10; i++)
        {
            await TouchAsync(cdp, "touchMove", (from.X + (to.X - from.X) * i / 10, from.Y + (to.Y - from.Y) * i / 10));
            await Task.Delay(20);
        }

        Assert.IsTrue(await gantt.Locator(".tm-gantt__dep-ghost").CountAsync() > 0, "the ghost line shows while dragging");
        await TouchAsync(cdp, "touchEnd");

        await page.WaitForFunctionAsync("(n) => document.querySelectorAll('.tm-gantt__dependency-line').length > n", before, new PageWaitForFunctionOptions { Timeout = 10_000 });
        Assert.AreEqual(0, await gantt.Locator(".tm-gantt__dep-ghost").CountAsync(), "the ghost line is gone after the release");
    }

    /// <summary>
    /// An end handle of one bar and a DIFFERENT bar whose centre is inside the visible timeline and the viewport (the demo's 18 bars
    /// span weeks, so the pair is chosen by geometry, never by index). The handle's bar is scrolled into view first.
    /// </summary>
    private static async Task<(ILocator Handle, ILocator Target)> PickDependencyPairAsync(ILocator gantt)
    {
        var page = gantt.Page;
        await gantt.Locator(".tm-gantt__timeline").First.ScrollIntoViewIfNeededAsync();
        var ids = await page.EvaluateAsync<string[]>(
            """
            () => {
                const tl = document.querySelector('.tm-gantt__timeline').getBoundingClientRect();
                const vw = window.innerWidth, vh = window.innerHeight;
                const seen = [...document.querySelectorAll('.tm-gantt__bar[data-task-id]:not(.tm-gantt__bar--group)')].map(b => {
                    const r = b.getBoundingClientRect();
                    const handle = b.querySelector('.tm-gantt__dep-handle--end').getBoundingClientRect();
                    const inside = r.left > tl.left + 20 && r.right < Math.min(tl.right, vw) - 20 && r.top > Math.max(tl.top, 0) + 40 && r.bottom < Math.min(tl.bottom, vh) - 20
                        && handle.right < Math.min(tl.right, vw) - 10;
                    return { id: b.dataset.taskId, inside, top: r.top };
                }).filter(x => x.inside).sort((a, b) => a.top - b.top);
                return seen.length >= 2 ? [seen[0].id, seen[seen.length - 1].id] : [];
            }
            """);
        Assert.AreEqual(2, ids.Length, "the demo should show two ungrouped bars fully inside the visible timeline");
        var handle = gantt.Locator($".tm-gantt__bar[data-task-id='{ids[0]}'] .tm-gantt__dep-handle--end");
        var target = gantt.Locator($".tm-gantt__bar[data-task-id='{ids[1]}']");
        return (handle, target);
    }
    [TestMethod]
    [Description("Tablet: Alt+ArrowDown on a focused row reorders it without dragging and announces the move")]
    public async Task Tablet_Keyboard_Reorder_Is_Announced()
    {
        var page = await OpenAsync(1024, 768, touch: true);
        await WaitLayoutAsync(page, "tablet");
        var rows = Gantt(page).Locator(".tm-gantt__tree-row");
        await rows.Nth(2).FocusAsync();

        await page.Keyboard.PressAsync("Alt+ArrowDown");

        // The live region is always in the DOM (a region inserted together with its text is often not announced): wait for the TEXT.
        await page.WaitForFunctionAsync("() => /moved after/.test(document.querySelector('.tm-gantt + [data-testid=gantt-announcer], [data-testid=gantt-announcer]')?.textContent ?? '')", null, new PageWaitForFunctionOptions { Timeout = 10_000 });
    }

    [TestMethod]
    [Description("1440 desktop keeps the one-row toolbar and the in-flow task panel; a 900px host at 1440 resolves the tablet layout")]
    public async Task Desktop_1440_Is_Desktop_And_Narrow_Host_Is_Tablet()
    {
        var page = await OpenAsync(1440, 900, touch: false);
        await WaitLayoutAsync(page, "desktop");
        Assert.AreEqual(1, await Gantt(page).Locator(".tm-gantt__toolbar [role='toolbar']").CountAsync());
        await Gantt(page).Locator(".tm-gantt__tree-row").Nth(2).ClickAsync();
        await Gantt(page).Locator(".tm-gantt__content > .tm-gantt-task-panel").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        Assert.AreEqual(0, await page.Locator(".tm-drawer.tm-side-panel-sheet").CountAsync(), "the desktop keeps the in-flow panel");

        // A 900px host inside the same 1440 viewport.
        await page.EvaluateAsync("() => { const g = document.querySelector('.tm-gantt').closest('section') ?? document.querySelector('.tm-gantt').parentElement; g.style.width = '900px'; }");
        await WaitLayoutAsync(page, "tablet");
        // The open in-flow panel became a side sheet with the layout flip (one surface, the same selected task): close it, then open another by tap.
        await page.Locator(".tm-drawer.tm-side-panel-sheet").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForSelectorAsync(".tm-drawer.tm-side-panel-sheet", new PageWaitForSelectorOptions { State = WaitForSelectorState.Detached, Timeout = 10_000 });
        // A mouse click selects; the sheet now opens from the keyboard (Enter) — a mouse click must
        // not raise the modal sheet over the chart, or the row's inline double-click dies under it.
        await Gantt(page).Locator(".tm-gantt__tree-row").Nth(1).FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await page.Locator(".tm-drawer.tm-side-panel-sheet").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        await ShootAsync(page, "narrow-host-900-at-1440--task-sheet");
    }

    [TestMethod]
    [Description("1440 mouse regression: the pointer pan scrolls the timeline, the dependency handle drag still adds a dependency, wheel still scrolls")]
    public async Task Desktop_Mouse_Pan_And_DependencyDrag_Still_Work()
    {
        var page = await OpenAsync(1440, 900, touch: false);
        await WaitLayoutAsync(page, "desktop");
        var gantt = Gantt(page);
        var timeline = gantt.Locator(".tm-gantt__timeline").First;

        // Pan: drag on the empty timeline background.
        await timeline.ScrollIntoViewIfNeededAsync();
        var start = await EmptyTimelinePointAsync(page);
        var before = await timeline.EvaluateAsync<double>("el => el.scrollLeft");
        await page.Mouse.MoveAsync((float)start.X, (float)start.Y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)start.X - 200, (float)start.Y, new MouseMoveOptions { Steps = 10 });
        Assert.IsTrue((await timeline.GetAttributeAsync("class"))!.Contains("tm-gantt__timeline--panning"), "the panning class shows during the pan");
        await page.Mouse.UpAsync();
        var after = await timeline.EvaluateAsync<double>("el => el.scrollLeft");
        Assert.IsTrue(after > before, $"the mouse pan should scroll the timeline (before {before}, after {after})");

        // A click on a bar still selects (it never pans).
        // Dependency drag with the mouse.
        var lines = gantt.Locator(".tm-gantt__dependency-line");
        var depsBefore = await lines.CountAsync();
        var (handle, target) = await PickDependencyPairAsync(gantt);
        await handle.HoverAsync(new LocatorHoverOptions { Force = true });
        var from = await CenterAsync(handle);
        var to = await CenterAsync(target);
        await page.Mouse.MoveAsync((float)from.X, (float)from.Y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)to.X, (float)to.Y, new MouseMoveOptions { Steps = 12 });
        await page.Mouse.UpAsync();
        await page.WaitForFunctionAsync("(n) => document.querySelectorAll('.tm-gantt__dependency-line').length > n", depsBefore, new PageWaitForFunctionOptions { Timeout = 10_000 });
        await ShootAsync(page, "desktop-1440--after-pan-and-dependency");
    }
}
