using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E.Feedback;

/// <summary>
/// Review round 4, against real hosts: a host WITHOUT a viewport scope must still present overlays
/// as mobile sheets through the internal probe (the round-3 behaviour the dead probe tag broke), a
/// flick from the full snap must step down to half instead of closing, side drawers must be
/// full-bleed below 640px, long FR footer labels must fit a 320px sheet, and the ViewManager create
/// modal must open on its own instance's name field.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class SheetRound4E2ETests : WasmTestBase
{
    private static readonly string ShotDir =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TestResults", "sheet-round4");

    private async Task<IPage> OpenAsync(int width, int height = 844)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            HasTouch = width < 1024,
            ViewportSize = new ViewportSize { Width = width, Height = height },
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        var page = await context.NewPageAsync();
        return page;
    }

    private async Task GotoAsync(IPage page, string path)
    {
        await page.GotoAsync($"{BaseUrl}{path}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 90000 });
        await WaitForAppReadyAsync(page);
    }

    private static async Task<string> ShootAsync(IPage page, string name)
    {
        Directory.CreateDirectory(ShotDir);
        var path = Path.Combine(ShotDir, $"{name}.png");
        // FullPage resets touch emulation; a viewport shot keeps (pointer: coarse) honest.
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = path,
            FullPage = false,
            Timeout = 60_000,
        });
        return path;
    }

    [TestMethod]
    [DataRow(390)]
    [DataRow(1440)]
    public async Task NoScope_OverlaysResolveTheMeasuredViewport(int width)
    {
        var page = await OpenAsync(width);
        var console = new List<string>();
        page.Console += (_, msg) => console.Add(msg.Text);

        await GotoAsync(page, "/overlay-no-scope");
        await page.WaitForTimeoutAsync(1500);

        var expected = width < 640 ? "mobile" : "desktop";

        // Modal
        await page.GetByTestId("noscope-modal").ClickAsync();
        await page.Locator(".tm-modal-overlay").WaitForAsync();
        await page.WaitForTimeoutAsync(1200);
        var modal = await page.EvaluateAsync<string>(
            """
            () => {
                const overlay = document.querySelector('.tm-modal-overlay');
                return JSON.stringify({
                    layout: overlay?.getAttribute('data-layout'),
                    sheet: overlay?.classList.contains('tm-modal--sheet'),
                    probeTags: document.querySelectorAll('viewportprobe').length,
                    probeBoxes: document.querySelectorAll('.tm-viewport-probe').length,
                });
            }
            """);
        Assert.IsTrue(modal.Contains($"\"layout\":\"{expected}\""),
            $"the modal must resolve {expected} from the probe at {width}px: {modal}");
        if (width < 640)
        {
            Assert.IsTrue(modal.Contains("\"sheet\":true"), $"a 390px no-scope modal must be a sheet: {modal}");
        }
        Assert.IsTrue(modal.Contains("\"probeTags\":0"), $"no literal <viewportprobe> element may reach the DOM: {modal}");
        Assert.IsTrue(modal.Contains("\"probeBoxes\":1"), "the probe's fixed, hidden box is the only .tm-viewport-probe");
        await ShootAsync(page, $"noscope-modal-{width}");
        await page.Keyboard.PressAsync("Escape");
        await page.Locator(".tm-modal-overlay").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 5000 });

        // Dialog
        await page.GetByTestId("noscope-dialog").ClickAsync();
        await page.Locator(".tm-modal-overlay").WaitForAsync();
        await page.WaitForTimeoutAsync(1200);
        var dialog = await page.EvaluateAsync<string>(
            """
            () => {
                const overlay = document.querySelector('.tm-modal-overlay');
                return JSON.stringify({
                    layout: overlay?.getAttribute('data-layout'),
                    sheet: overlay?.classList.contains('tm-modal--sheet'),
                });
            }
            """);
        Assert.IsTrue(dialog.Contains($"\"layout\":\"{expected}\""),
            $"the dialog must resolve {expected} from the probe at {width}px: {dialog}");
        await ShootAsync(page, $"noscope-dialog-{width}");
        await page.Keyboard.PressAsync("Escape");
        await page.Locator(".tm-modal-overlay").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 5000 });

        // Bottom drawer
        await page.GetByTestId("noscope-drawer").ClickAsync();
        await page.Locator(".tm-drawer").WaitForAsync();
        await page.WaitForTimeoutAsync(1200);
        var drawerLayout = await page.Locator(".tm-drawer").GetAttributeAsync("data-layout");
        Assert.AreEqual(expected, drawerLayout,
            $"the drawer must resolve {expected} from the probe at {width}px");
        await ShootAsync(page, $"noscope-drawer-{width}");
        await page.Keyboard.PressAsync("Escape");

        // The hint is logged once per process, after the first probe measurement.
        await page.WaitForTimeoutAsync(800);
        var hints = console.Where(m => m.Contains("viewport scope", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.AreEqual(1, hints.Count,
            $"the missing-scope hint must log exactly once, got {hints.Count}: [{string.Join(" | ", hints.Take(3))}]");
    }

    [TestMethod]
    public async Task Flick_FromFull_ANormalSwipeStepsDownToHalf()
    {
        var page = await OpenAsync(390);
        await GotoAsync(page, "/feedback");
        await page.GetByTestId("open-bottom-sheet").ScrollIntoViewIfNeededAsync();
        await page.GetByTestId("open-bottom-sheet").ClickAsync();
        await page.Locator(".tm-drawer--bottom").WaitForAsync();

        // Keyboard: half -> full.
        await page.Locator(".tm-drawer--bottom .tm-sheet__handle").FocusAsync();
        await page.Keyboard.PressAsync("ArrowUp");
        await page.WaitForFunctionAsync(
            "() => document.querySelector('.tm-drawer--bottom')?.getAttribute('data-snap-index') === '1'",
            null, new PageWaitForFunctionOptions { Timeout = 5000 });

        var box = await page.Locator(".tm-drawer--bottom .tm-sheet__handle").BoundingBoxAsync();
        Assert.IsNotNull(box);
        var x = box.X + box.Width / 2;
        var y = box.Y + box.Height / 2;

        // A 300px drag over ~300ms: about 1 px/ms — the ordinary "back to half" gesture. Slow
        // enough to stay under the close-anywhere threshold, fast enough to step one snap.
        await page.Mouse.MoveAsync(x, y);
        await page.Mouse.DownAsync();
        for (var i = 1; i <= 10; i++)
        {
            await page.Mouse.MoveAsync(x, y + 30 * i);
            await page.WaitForTimeoutAsync(25);
        }

        await page.Mouse.UpAsync();
        await page.WaitForTimeoutAsync(600);

        var state = await page.EvaluateAsync<string>(
            """
            () => {
                const drawer = document.querySelector('.tm-drawer--bottom');
                const panel = drawer?.querySelector('.tm-drawer__panel');
                return JSON.stringify({
                    open: !!drawer,
                    index: drawer?.getAttribute('data-snap-index'),
                    points: drawer?.getAttribute('data-snap-points'),
                    height: Math.round(panel?.getBoundingClientRect().height ?? 0),
                    viewport: window.innerHeight,
                });
            }
            """);
        Assert.IsTrue(state.Contains("\"open\":true"), $"a 1 px/ms swipe from full must not close the drawer: {state}");
        Assert.IsTrue(state.Contains("\"index\":\"0\""), $"the swipe must settle on the half snap: {state}");
        await ShootAsync(page, "flick-from-full-lands-on-half");
    }

    [TestMethod]
    [DataRow("open-right-drawer", ".tm-drawer--right", "right-drawer")]
    [DataRow("open-left-drawer", ".tm-drawer--left", "left-drawer")]
    public async Task SideDrawers_AreFullBleedAt390(string triggerId, string drawer, string shot)
    {
        var page = await OpenAsync(390);
        await GotoAsync(page, "/feedback");
        await page.GetByTestId(triggerId).ScrollIntoViewIfNeededAsync();
        await page.GetByTestId(triggerId).ClickAsync();
        await page.Locator(drawer).WaitForAsync();
        await page.Locator($"{drawer} .tm-drawer__panel").EvaluateAsync(
            "el => Promise.all(el.getAnimations().map(a => a.finished)).catch(() => {})");

        var width = await page.Locator($"{drawer} .tm-drawer__panel")
            .EvaluateAsync<double>("el => el.getBoundingClientRect().width");
        Assert.AreEqual(390, width, 1.0,
            $"a side drawer must be full-bleed below 640px, measured {width}px");

        await ShootAsync(page, $"{shot}-full-bleed-390");
    }

    [TestMethod]
    public async Task LongFrLabels_StayInsideThe320pxSheetFooter()
    {
        var page = await OpenAsync(320, 700);
        await GotoAsync(page, "/modal-dialog");

        // The modal sheet footer, with labels longer than any real FR resource string.
        await page.GetByTestId("open-sheet").ScrollIntoViewIfNeededAsync();
        await page.GetByTestId("open-sheet").ClickAsync();
        await page.Locator(".tm-modal-overlay").WaitForAsync();
        await page.WaitForTimeoutAsync(700);
        await page.EvaluateAsync(
            """
            () => {
                const labels = document.querySelectorAll('.tm-modal-overlay .tm-modal-footer .tm-btn .tm-btn-label');
                labels[0].textContent = 'Annuler sans enregistrer les modifications';
                labels[1].textContent = 'Enregistrer les modifications du tableau de bord';
            }
            """);
        await page.WaitForTimeoutAsync(300);

        var modalFooter = await ReadFooterAsync(page);
        AssertFooterFits(modalFooter, "modal sheet footer");
        await ShootAsync(page, "fr-long-labels-modal-320");

        await page.Keyboard.PressAsync("Escape");
        await page.Locator(".tm-modal-overlay").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 5000 });

        // The dialog's danger button.
        await page.GetByText("Dangerous Action", new PageGetByTextOptions { Exact = true }).ClickAsync();
        await page.Locator(".tm-dialog").WaitForAsync();
        await page.WaitForTimeoutAsync(700);
        await page.EvaluateAsync(
            """
            () => {
                const ok = document.querySelector('.tm-dialog-footer .tm-btn-danger')
                    ?? document.querySelector('.tm-dialog-footer .tm-dialog-btn-ok');
                if (ok) ok.querySelector('.tm-btn-label').textContent =
                    'Supprimer définitivement cet élément et toutes ses pièces jointes';
            }
            """);
        await page.WaitForTimeoutAsync(300);

        var dialogFooter = await ReadFooterAsync(page);
        AssertFooterFits(dialogFooter, "dialog sheet footer");
        await ShootAsync(page, "fr-long-labels-dialog-320");
    }

    private static async Task<string> ReadFooterAsync(IPage page)
    {
        return await page.EvaluateAsync<string>(
            """
            () => {
                const footer = document.querySelector('.tm-modal-overlay .tm-modal-footer, .tm-modal-overlay .tm-dialog-footer');
                const box = footer.getBoundingClientRect();
                return JSON.stringify({
                    scrollWidth: footer.scrollWidth,
                    clientWidth: footer.clientWidth,
                    left: Math.round(box.left),
                    right: Math.round(box.right),
                    vw: window.innerWidth,
                    buttons: [...footer.querySelectorAll('.tm-btn')].map(b => {
                        const r = b.getBoundingClientRect();
                        return { left: Math.round(r.left), right: Math.round(r.right), width: Math.round(r.width), height: Math.round(r.height) };
                    }),
                });
            }
            """);
    }

    private static void AssertFooterFits(string json, string label)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;
        var scrollWidth = root.GetProperty("scrollWidth").GetInt32();
        var clientWidth = root.GetProperty("clientWidth").GetInt32();
        var vw = root.GetProperty("vw").GetInt32();
        Assert.IsTrue(scrollWidth <= clientWidth + 1,
            $"{label}: the footer must not overflow, got {json}");
        foreach (var button in root.GetProperty("buttons").EnumerateArray())
        {
            var left = button.GetProperty("left").GetInt32();
            var right = button.GetProperty("right").GetInt32();
            var buttonWidth = button.GetProperty("width").GetInt32();
            Assert.IsTrue(left >= -1 && right <= vw + 1,
                $"{label}: a button must stay inside the viewport, got {json}");
            Assert.IsTrue(buttonWidth >= 40,
                $"{label}: a wrapped button must still be usable, got {json}");
        }
    }

    [TestMethod]
    [DataRow(1440, 0)]
    [DataRow(1440, 2)]
    [DataRow(390, 0)]
    [DataRow(390, 2)]
    public async Task ViewManager_CreateModal_OpensOnThatInstanceNameField(int width, int instance)
    {
        var page = await OpenAsync(width);
        await GotoAsync(page, "/data-table");

        var toggles = page.Locator(".tm-view-manager-toggle");
        Assert.IsTrue(await toggles.CountAsync() > instance, $"the data-table page renders enough view managers for instance {instance}");
        var toggle = toggles.Nth(instance);
        await toggle.ScrollIntoViewIfNeededAsync();
        await toggle.ClickAsync();
        await page.Locator(".tm-view-manager-panel .tm-btn-primary").Last.ClickAsync();
        await page.Locator(".tm-modal").WaitForAsync();
        await page.WaitForTimeoutAsync(700);

        var focus = await page.EvaluateAsync<string>(
            """
            () => {
                const active = document.activeElement;
                const modal = document.querySelector('.tm-modal');
                return JSON.stringify({
                    tag: active?.tagName,
                    id: active?.id ?? '',
                    tmId: active?.getAttribute('data-tm-id') ?? '',
                    initialFocus: modal?.getAttribute('data-initial-focus') ?? '',
                    hasLabelFor: !!document.querySelector(`label[for="${active?.id ?? '__none__'}"]`),
                });
            }
            """);

        Assert.IsTrue(focus.Contains("\"tag\":\"INPUT\""), $"the create modal must open on the name input, got {focus}");
        Assert.IsTrue(focus.Contains("\"tmId\":\"view-name-"), $"the focused field is a view-name field, got {focus}");
        Assert.IsTrue(focus.Contains("\"id\":\"view-name-"), $"the name field needs a real id for getElementById, got {focus}");

        using var doc = System.Text.Json.JsonDocument.Parse(focus);
        var root = doc.RootElement;
        Assert.AreEqual(root.GetProperty("initialFocus").GetString(), root.GetProperty("tmId").GetString(),
            $"the initial focus target must be this modal's name field: {focus}");
        Assert.IsTrue(root.GetProperty("hasLabelFor").GetBoolean(),
            $"the name label must point at the focused field: {focus}");

        await ShootAsync(page, $"viewmanager-instance{instance}-focus-{width}");

        await page.Keyboard.PressAsync("Escape");
        await page.Locator(".tm-modal").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 5000 });
    }
}
