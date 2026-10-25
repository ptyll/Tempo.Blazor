using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// The wireframe editor's MOBILE layout (390x844) driven by a mouse: <c>HasTouch=false</c>, <c>(pointer: fine)</c>, <c>(hover: hover)</c> -
/// a narrow desktop window, a docked browser side panel or a phone emulator without touch. The layout follows the editor's own container
/// (< 768 px = mobile), never the input device, so every phone affordance must work with real mouse events and none may depend on a hover
/// or a touch-only gesture: the Add sheet opens by click, a click inserts, a click on an element selects it and peeks the Properties sheet,
/// Duplicate / Lock / Delete work by click, a right click opens the action sheet, Escape closes the sheet, and the rows keep the 44 px
/// <c>--tm-touch-target</c> even with a fine pointer (the phone layout, not the pointer type, decides). The touch twin is
/// <see cref="WireframePhase7MobileE2ETests"/>.
/// </summary>
[TestClass]
public sealed class WireframeMobileMouseE2ETests : WasmTestBase
{
    private const string EditorUrl = "/wireframe-editor";
    private const int Width = 390;
    private const int Height = 844;

    private async Task<(IPage Page, ILocator Editor)> OpenAsync()
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            HasTouch = false,
            IsMobile = false,
            ViewportSize = new ViewportSize { Width = Width, Height = Height },
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);

        var page = await context.NewPageAsync();
        page.Console += (_, message) => { if (message.Type is "error") TestContext.WriteLine($"[console.{message.Type}] {message.Text}"); };
        page.PageError += (_, error) => TestContext.WriteLine($"[pageerror] {error}");
        await page.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        await GotoAppReadyAsync(page, $"{BaseUrl}{EditorUrl}");

        await page.WaitForSelectorAsync(".tm-wd-editor .tm-wd-canvas__svg", new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 30_000 });
        var editor = page.Locator(".tm-wd-editor").First;
        await editor.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(1500);   // container measurement + toolbar overflow settle

        Assert.AreEqual("mobile", await editor.GetAttributeAsync("data-editor-layout"), "390 px is the mobile layout whatever the pointer");
        Assert.IsTrue(await page.EvaluateAsync<bool>("() => matchMedia('(pointer: fine)').matches && !matchMedia('(pointer: coarse)').matches"),
            "the spec really runs with a fine pointer");
        return (page, editor);
    }

    private static ILocator Sheet(ILocator editor) => editor.Locator(".tm-editor-shell__sheet");

    private static ILocator Elements(ILocator editor) => editor.Locator("g[data-el-id]");

    private static async Task OpenAddSheetAsync(IPage page, ILocator editor)
    {
        await editor.Locator(".tm-mobile-action-bar__action[data-action-id='components']").ClickAsync();
        await Sheet(editor).WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await page.WaitForFunctionAsync("() => document.getAnimations().every(a => a.playState !== 'running')", null, new PageWaitForFunctionOptions { Timeout = 5_000 });
    }

    /// <summary>Inserts a Button through the Add sheet by click; the insert selects it and the sheet peeks its properties.</summary>
    private static async Task InsertButtonAsync(IPage page, ILocator editor)
    {
        var before = await Elements(editor).CountAsync();
        await OpenAddSheetAsync(page, editor);
        await editor.Locator(".tm-wd-toolbox__item[data-component-type='TmButton']").First.ClickAsync();
        await page.WaitForFunctionAsync("n => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('g[data-el-id]').length > n",
            before, new PageWaitForFunctionOptions { Timeout = 10_000 });
        await editor.Locator("[data-testid='wd-quick-actions']").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await page.WaitForFunctionAsync("() => document.getAnimations().every(a => a.playState !== 'running')", null, new PageWaitForFunctionOptions { Timeout = 5_000 });
    }

    /// <summary>A point inside the first element that nothing else covers (the sheet, the floating zoom, the selection), settled after the page re-fit.</summary>
    private static async Task<(double X, double Y)> ElementPointAsync(IPage page, ILocator editor)
    {
        (double X, double Y)? last = null;
        for (var i = 0; i < 40; i++)
        {
            var text = await page.EvaluateAsync<string>(
                """
                () => {
                    const g = document.querySelectorAll('.tm-wd-editor')[0].querySelector('g[data-el-id]');
                    if (!g) return '';
                    const r = g.getBoundingClientRect();
                    const x = r.left + r.width / 2, y = r.top + r.height / 2;
                    const el = document.elementFromPoint(x, y);
                    return el && el.closest('[data-el-id]') ? x + ',' + y : '';
                }
                """);
            if (!string.IsNullOrEmpty(text))
            {
                var parts = text.Split(',');
                var now = (X: double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), Y: double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
                if (last is { } l && Math.Abs(l.X - now.X) < 0.5 && Math.Abs(l.Y - now.Y) < 0.5) return now;
                last = now;
            }

            await page.WaitForTimeoutAsync(150);
        }

        Assert.Fail("the element never settled under a reachable point");
        return default;
    }

    /// <summary>A point of the empty page (no element, selection, connector or floating control) in the visible canvas.</summary>
    private static async Task<(double X, double Y)> EmptyCanvasPointAsync(IPage page)
    {
        var text = await page.EvaluateAsync<string>(
            """
            () => {
                const svg = document.querySelectorAll('.tm-wd-editor')[0].querySelector('.tm-wd-canvas__svg');
                const r = svg.getBoundingClientRect();
                const left = Math.max(r.left, 0), top = Math.max(r.top, 0), right = Math.min(r.right, window.innerWidth), bottom = Math.min(r.bottom, window.innerHeight);
                for (let dy = 8; dy < Math.max(bottom - top - 8, 9); dy += 12) {
                    for (let dx = 8; dx < Math.max(right - left - 8, 9); dx += 12) {
                        const x = left + dx, y = top + dy;
                        const el = document.elementFromPoint(x, y);
                        if (el && svg.contains(el) && !el.closest('[data-el-id], .tm-wd-selection, [data-connector-id]')) return x + ',' + y;
                    }
                }
                return '';
            }
            """);
        Assert.IsFalse(string.IsNullOrEmpty(text), "no empty point on the visible canvas");
        var parts = text.Split(',');
        return (double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
    }

    private static async Task AssertFingerSizedAsync(ILocator target, string what)
    {
        var box = await target.BoundingBoxAsync();
        Assert.IsNotNull(box, what);
        Assert.IsTrue(box!.Height >= 43.5, $"{what} keeps the 44 px touch target with a mouse ({box.Height:0.#}px)");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Mouse, phone layout: the Add tile opens the sheet by click, a click on a palette tile inserts and selects the element, the Properties sheet peeks with Duplicate / Lock / Delete")]
    public async Task Mouse_Add_ClickInsert_SelectionPeeksProperties()
    {
        var (page, editor) = await OpenAsync();
        Assert.AreEqual(0, await Sheet(editor).CountAsync(), "the sheet starts closed");

        await OpenAddSheetAsync(page, editor);
        await AssertFingerSizedAsync(editor.Locator(".tm-wd-toolbox__item[data-component-type='TmButton']").First, "the palette tile");

        var before = await Elements(editor).CountAsync();
        await editor.Locator(".tm-wd-toolbox__item[data-component-type='TmButton']").First.ClickAsync();
        await page.WaitForFunctionAsync("n => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('g[data-el-id]').length > n",
            before, new PageWaitForFunctionOptions { Timeout = 10_000 });

        var quick = editor.Locator("[data-testid='wd-quick-actions']");
        await quick.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        Assert.AreEqual("0", await Sheet(editor).GetAttributeAsync("data-snap-index"), "the selection peeks the sheet");
        Assert.AreEqual(1, await editor.Locator(".tm-wd-selection").CountAsync(), "the inserted element is selected");
        foreach (var testId in new[] { "wd-quick-duplicate", "wd-quick-lock", "wd-quick-delete" })
            await AssertFingerSizedAsync(editor.Locator($"[data-testid='{testId}']"), testId);
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Mouse, phone layout: a click on an element selects it (and a click on the empty page deselects and closes the peeked sheet) - selection needs no touch and no hover")]
    public async Task Mouse_ClickSelectsElement_ClickEmptyDeselects()
    {
        var (page, editor) = await OpenAsync();
        await InsertButtonAsync(page, editor);

        // The sheet the Add tile opened is the user's: close it with its × (by click), the element stays selected.
        await editor.Locator(".tm-editor-shell__sheet-close").ClickAsync();
        await Sheet(editor).WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 10_000 });
        await page.WaitForTimeoutAsync(900);   // the page re-fits into the room the sheet gave back

        // Deselect (an empty-page click), then select again by clicking the element: the click alone must do it, and the selection peeks the sheet.
        var (px, py) = await EmptyCanvasPointAsync(page);
        await page.Mouse.ClickAsync((float)px, (float)py);
        await page.WaitForFunctionAsync("() => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('.tm-wd-selection').length === 0",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });

        var (ex, ey) = await ElementPointAsync(page, editor);
        await page.Mouse.ClickAsync((float)ex, (float)ey);
        await editor.Locator("[data-testid='wd-quick-actions']").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        Assert.AreEqual(1, await editor.Locator(".tm-wd-selection").CountAsync(), "a click selects the element");
        Assert.AreEqual("0", await Sheet(editor).GetAttributeAsync("data-snap-index"), "the selection peeks the Properties sheet");
        Assert.AreEqual(0, await editor.Locator(".tm-wd-selection [data-handle]").CountAsync(), "no resize grips on the phone layout, mouse or not");

        // A click on the empty page, above the peeked sheet, deselects and the sheet the selection opened closes again.
        await page.WaitForTimeoutAsync(900);
        (px, py) = await EmptyCanvasPointAsync(page);
        await page.Mouse.ClickAsync((float)px, (float)py);
        await Sheet(editor).WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 10_000 });
        Assert.AreEqual(0, await editor.Locator(".tm-wd-selection").CountAsync(), "an empty-page click deselects");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Mouse, phone layout: Duplicate, Lock and Delete work by click from the peeked sheet, each is one undoable step; Lock is reflected in the label and locks the element")]
    public async Task Mouse_QuickActions_Duplicate_Lock_Delete()
    {
        var (page, editor) = await OpenAsync();
        await InsertButtonAsync(page, editor);
        var count = await Elements(editor).CountAsync();

        // Duplicate
        await editor.Locator("[data-testid='wd-quick-duplicate']").ClickAsync();
        await page.WaitForFunctionAsync("n => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('g[data-el-id]').length === n + 1",
            count, new PageWaitForFunctionOptions { Timeout = 10_000 });

        // Lock (the duplicate is the selection now) - the label flips to Unlock and the status announces it
        var lockButton = editor.Locator("[data-testid='wd-quick-lock']");
        await lockButton.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        StringAssert.Contains(await lockButton.InnerTextAsync(), "Lock");
        await lockButton.ClickAsync();
        await page.WaitForFunctionAsync("() => /unlock/i.test(document.querySelectorAll('.tm-wd-editor')[0].querySelector('[data-testid=\"wd-quick-lock\"]')?.textContent ?? '')",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        await page.WaitForFunctionAsync("() => /^Locked/.test(document.querySelector('.tm-wd-editor [data-testid=\"wd-canvas-status\"]').textContent.trim())",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        await lockButton.ClickAsync();   // unlock again so Delete is not blocked by the lock
        await page.WaitForFunctionAsync("() => /^Lock/i.test((document.querySelectorAll('.tm-wd-editor')[0].querySelector('[data-testid=\"wd-quick-lock\"]')?.textContent ?? '').trim())",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });

        // Delete (one undoable step)
        await editor.Locator("[data-testid='wd-quick-delete']").ClickAsync();
        await page.WaitForFunctionAsync("n => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('g[data-el-id]').length === n",
            count, new PageWaitForFunctionOptions { Timeout = 10_000 });
        await editor.Locator("[data-testid='wd-mobile-tools'] button[aria-label^='Undo']").ClickAsync();
        await page.WaitForFunctionAsync("n => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('g[data-el-id]').length === n + 1",
            count, new PageWaitForFunctionOptions { Timeout = 10_000 });
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Mouse, phone layout: a RIGHT click on an element opens the action sheet (the shared bottom action sheet, not the fixed anchored menu); the 44 px rows apply; Delete by click removes the element")]
    public async Task Mouse_RightClick_OpensTheActionSheet()
    {
        var (page, editor) = await OpenAsync();
        await InsertButtonAsync(page, editor);
        var (ex, ey) = await ElementPointAsync(page, editor);

        await page.Mouse.ClickAsync((float)ex, (float)ey, new MouseClickOptions { Button = MouseButton.Right });

        var sheet = page.Locator(".tm-overlay-panel-sheet");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        Assert.AreEqual(0, await page.Locator(".tm-wd-editor__context-menu").CountAsync(), "the fixed anchored menu is not used on the phone layout");
        await page.WaitForFunctionAsync("() => document.getAnimations().every(a => a.playState !== 'running')", null, new PageWaitForFunctionOptions { Timeout = 5_000 });

        var rows = sheet.Locator("[role='menuitem']");
        var labels = (await rows.AllInnerTextsAsync()).Select(l => l.Trim()).ToArray();
        CollectionAssert.IsSubsetOf(new[] { "Duplicate", "Delete" }, labels);
        Assert.IsFalse(labels.Any(l => l.Contains("Align", StringComparison.OrdinalIgnoreCase)), "align is tablet+");
        for (var i = 0; i < await rows.CountAsync(); i++)
            await AssertFingerSizedAsync(rows.Nth(i), $"action sheet row '{labels[i]}'");

        var count = await Elements(editor).CountAsync();
        await sheet.Locator("[role='menuitem']", new() { HasTextString = "Delete" }).ClickAsync();
        await page.WaitForFunctionAsync("n => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('g[data-el-id]').length < n",
            count, new PageWaitForFunctionOptions { Timeout = 10_000 });
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 10_000 });
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Mouse, phone layout: Escape closes the action sheet (without acting) and then the Properties sheet; the focus never falls to <body>")]
    public async Task Mouse_Escape_ClosesTheActionSheet_ThenThePropertiesSheet()
    {
        var (page, editor) = await OpenAsync();
        await InsertButtonAsync(page, editor);
        var count = await Elements(editor).CountAsync();
        var (ex, ey) = await ElementPointAsync(page, editor);

        await page.Mouse.ClickAsync((float)ex, (float)ey, new MouseClickOptions { Button = MouseButton.Right });
        var actionSheet = page.Locator(".tm-overlay-panel-sheet");
        await actionSheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await page.WaitForFunctionAsync("() => document.getAnimations().every(a => a.playState !== 'running')", null, new PageWaitForFunctionOptions { Timeout = 5_000 });

        await page.Keyboard.PressAsync("Escape");
        await actionSheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 10_000 });
        Assert.AreEqual(count, await Elements(editor).CountAsync(), "Escape closes the action sheet without running a command");

        // The Properties sheet is still there (the element is still selected); Escape closes it too.
        await Sheet(editor).WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await Sheet(editor).Locator("button, [tabindex]").First.FocusAsync();
        await page.Keyboard.PressAsync("Escape");
        await Sheet(editor).WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 10_000 });
        await page.WaitForFunctionAsync("() => { const a = document.activeElement; return !!a && a !== document.body && !!a.closest('.tm-wd-editor'); }",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Mouse, phone layout: the action bar tiles, the Add sheet's tiles and the quick actions keep the 44 px touch target with a fine pointer (the layout, not the pointer, decides)")]
    public async Task Mouse_Rows_Keep44pxTouchTargets()
    {
        var (page, editor) = await OpenAsync();

        foreach (var id in new[] { "components", "pages-layers", "export" })
            await AssertFingerSizedAsync(editor.Locator($".tm-mobile-action-bar__action[data-action-id='{id}']"), $"action bar tile '{id}'");
        await AssertFingerSizedAsync(editor.Locator(".tm-mobile-action-bar__more"), "the More tile");

        await OpenAddSheetAsync(page, editor);
        var tiles = editor.Locator(".tm-wd-toolbox__item:not(.tm-wd-toolbox__item--disabled)");
        var n = Math.Min(await tiles.CountAsync(), 6);
        Assert.IsTrue(n > 0, "the palette lists components");
        for (var i = 0; i < n; i++)
        {
            await tiles.Nth(i).ScrollIntoViewIfNeededAsync();
            await AssertFingerSizedAsync(tiles.Nth(i), $"palette tile #{i}");
        }
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Mouse, phone layout: nothing is hover-only - every control the flow needs is visible and enabled without a hover (the quick actions, the sheet close button, the Pages & layers row controls), and keyboard focus reveals the same set")]
    public async Task Mouse_NoHoverOnlyAffordances()
    {
        var (page, editor) = await OpenAsync();
        await InsertButtonAsync(page, editor);
        await page.Mouse.MoveAsync(2, 2);   // the pointer is parked far from everything: no :hover anywhere

        foreach (var selector in new[] { "[data-testid='wd-quick-duplicate']", "[data-testid='wd-quick-lock']", "[data-testid='wd-quick-delete']", ".tm-editor-shell__sheet-close" })
        {
            var control = editor.Locator(selector).First;
            Assert.IsTrue(await control.IsVisibleAsync(), $"{selector} is visible without hover");
            Assert.IsTrue(await control.IsEnabledAsync(), $"{selector} is enabled without hover");
            var opacity = await control.EvaluateAsync<double>("e => parseFloat(getComputedStyle(e).opacity)");
            Assert.IsTrue(opacity >= 0.99, $"{selector} is fully opaque without hover ({opacity})");
        }

        // Pages & layers: the per-row controls (visibility, lock, row menu, delete) are not revealed by :hover only.
        await editor.Locator(".tm-mobile-action-bar__action[data-action-id='pages-layers']").ClickAsync();
        var extra = editor.Locator("[data-region='extra']");
        await extra.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await extra.Locator("[role='tab']", new() { HasTextString = "Layers" }).ClickAsync();
        var row = editor.Locator(".tm-wd-layers__item").First;
        await row.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await page.WaitForFunctionAsync("() => document.getAnimations().every(a => a.playState !== 'running')", null, new PageWaitForFunctionOptions { Timeout = 5_000 });
        await page.Mouse.MoveAsync(2, 2);
        var hidden = await row.EvaluateAsync<string[]>(
            """
            r => Array.from(r.querySelectorAll('button, [role="button"], [tabindex]'))
                .filter(c => { const s = getComputedStyle(c); const b = c.getBoundingClientRect();
                               return s.display === 'none' || s.visibility === 'hidden' || parseFloat(s.opacity) < 0.99 || b.width < 1 || b.height < 1; })
                .map(c => (c.getAttribute('aria-label') || c.className || c.tagName))
            """);
        Assert.AreEqual(0, hidden.Length, "layer row controls hidden until hover: " + string.Join(", ", hidden));
        var rowButtons = row.Locator("button");
        for (var i = 0; i < await rowButtons.CountAsync(); i++)
            await AssertFingerSizedAsync(rowButtons.Nth(i), $"layer row control #{i}");
    }
}
