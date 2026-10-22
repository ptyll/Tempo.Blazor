using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Wireframe redesign plan ce19aa8c, phase 3 — the layers panel: semantics (radiogroup, aria-pressed), the row
/// menu, keyboard reorder, sizing (36px rows / 32px icon buttons, 44px on a coarse pointer) and an axe scan.
/// Captures the panel at 1440 and at 1024 (touch) into <c>__screenshots__/wireframe/phase3/</c> only with
/// <c>TM_E2E_UPDATE_BASELINE=1</c> (otherwise into the git-ignored per-run TestResults folder), to be reviewed against
/// <c>output/imagegen/tempo-reimagined/Tempo.Blazor.Wireframe/wireframe-layers-panel--desktop.png</c> / <c>--tablet.png</c>.
/// </summary>
[TestClass]
public sealed class WireframeLayersPanelE2ETests : WasmTestBase
{
    private const string EditorUrl = "/wireframe-editor";
    private const string AxeCdn = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js";

    private static bool UpdateBaseline =>
        string.Equals(Environment.GetEnvironmentVariable("TM_E2E_UPDATE_BASELINE"), "1", StringComparison.Ordinal);

    private static string ShotDirectory => UpdateBaseline
        ? Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "__screenshots__", "wireframe", "phase3")
        : Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "wireframe-phase3");

    // ── Semantics, keyboard reorder and row menu (the specs the editor suite's Layers_* group points at) ──

    private static async Task<string[]> LayerNamesAsync(ILocator editor) =>
        await editor.Locator(".tm-wd-layers__name, .tm-wd-layers__name-input").EvaluateAllAsync<string[]>(
            "els => els.map(e => (e.value ?? e.textContent).trim())");

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Layers_ActiveLayer_IsARadioInARadioGroup()
    {
        var page = await OpenAsync(1440, 900, touch: false);
        var editor = page.Locator(".tm-wd-editor").First;
        await editor.Locator(".tm-wd-layers__add-btn").ClickAsync();
        await page.WaitForTimeoutAsync(600);

        Assert.AreEqual(1, await editor.Locator(".tm-wd-layers [role='radiogroup']").CountAsync(), "one radiogroup");
        Assert.IsTrue(await editor.Locator(".tm-wd-layers [role='radiogroup'] [role='radio']").CountAsync() >= 2);
        Assert.AreEqual(1, await editor.Locator(".tm-wd-layers [role='radio'][aria-checked='true']").CountAsync(),
            "exactly one layer is the active (checked) radio");
        Assert.AreEqual("true", await editor.Locator(".tm-wd-layers__visibility").First.GetAttributeAsync("aria-pressed"));
        Assert.AreEqual("false", await editor.Locator(".tm-wd-layers__lock").First.GetAttributeAsync("aria-pressed"));
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Keyboard only: Alt+ArrowDown reorders the focused layer, undo restores the order")]
    public async Task Layers_KeyboardReorder()
    {
        var page = await OpenAsync(1440, 900, touch: false);
        var editor = page.Locator(".tm-wd-editor").First;
        await editor.Locator(".tm-wd-layers__add-btn").ClickAsync();
        await page.WaitForTimeoutAsync(600);
        // Adding a layer may leave the new row in inline rename; commit it so the list is quiet.
        if (await editor.Locator(".tm-wd-layers__name-input").CountAsync() > 0)
        {
            await page.Keyboard.PressAsync("Enter");
        }

        var before = await LayerNamesAsync(editor);
        Assert.IsTrue(before.Length >= 2, "needs two layers to reorder");

        await editor.Locator(".tm-wd-layers__name").First.FocusAsync();
        await page.Keyboard.PressAsync("Alt+ArrowDown");
        await page.WaitForFunctionAsync(
            "first => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('.tm-wd-layers__name')[1]?.textContent.trim() === first",
            before[0], new PageWaitForFunctionOptions { Timeout = 5_000 });

        var after = await LayerNamesAsync(editor);
        Assert.AreEqual(before[0], after[1], "the focused layer moved down one place");
        Assert.AreEqual(before[1], after[0]);
        Assert.AreEqual($"Layer {before[0]} moved down, position 2 of {before.Length}", (await editor.Locator(".tm-wd-layers [role='status']").TextContentAsync())?.Trim(),
            "the move is announced in the live region");

        // Undo restores the original order.
        await editor.Locator("button[aria-label^='Undo']").First.ClickAsync();
        await page.WaitForFunctionAsync(
            "first => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('.tm-wd-layers__name')[0]?.textContent.trim() === first",
            before[0], new PageWaitForFunctionOptions { Timeout = 5_000 });
        CollectionAssert.AreEqual(before, await LayerNamesAsync(editor), "undo restores the order");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Mouse/touch path without a drag: the row menu offers Rename and Move up")]
    public async Task Layers_RowMenuRename()
    {
        var page = await OpenAsync(1440, 900, touch: false);
        var editor = page.Locator(".tm-wd-editor").First;
        await editor.Locator(".tm-wd-layers__add-btn").ClickAsync();
        await page.WaitForTimeoutAsync(600);
        if (await editor.Locator(".tm-wd-layers__name-input").CountAsync() > 0)
        {
            await page.Keyboard.PressAsync("Enter");
        }

        var row = editor.Locator(".tm-wd-layers__item").Last;
        await row.Locator(".tm-wd-layers__row-menu .tm-context-menu__trigger").ClickAsync();
        var rename = row.Locator(".tm-wd-layers__row-menu-item[data-action='rename']");
        await rename.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5_000 });
        await rename.ClickAsync();

        var input = editor.Locator(".tm-wd-layers__name-input");
        await input.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5_000 });
        await input.FillAsync("Navigation");
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForFunctionAsync(
            "() => [...document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('.tm-wd-layers__name')].some(n => n.textContent.trim() === 'Navigation')",
            null, new PageWaitForFunctionOptions { Timeout = 5_000 });

        // The same row menu moves it up without a drag.
        await row.Locator(".tm-wd-layers__row-menu .tm-context-menu__trigger").ClickAsync();
        var up = row.Locator(".tm-wd-layers__row-menu-item[data-action='up']");
        await up.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5_000 });
        await up.ClickAsync();
        await page.WaitForFunctionAsync(
            "() => { const n = [...document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('.tm-wd-layers__name')]; return n.length >= 2 && n[n.length - 2].textContent.trim() === 'Navigation'; }",
            null, new PageWaitForFunctionOptions { Timeout = 5_000 });
    }
    // ── Screenshots + sizing ──────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Layers panel at 1440 (mouse): 36px rows, 32px icon buttons, sentence-case title, active row tinted, drop indicator")]
    public async Task Panel_1440_RowsAre36px_IconButtonsAre32px_AndTheTitleIsSentenceCase()
    {
        Directory.CreateDirectory(ShotDirectory);
        var page = await OpenAsync(1440, 900, touch: false);
        var panel = await PreparePanelAsync(page);

        var metrics = await page.EvaluateAsync<System.Text.Json.JsonElement>(
            """
            () => {
                const root = document.querySelector('.tm-wd-editor .tm-wd-layers');
                const row = root.querySelector('.tm-wd-layers__item');
                const box = s => { const r = root.querySelector(s).getBoundingClientRect(); return { w: Math.round(r.width), h: Math.round(r.height) }; };
                const title = getComputedStyle(root.querySelector('.tm-wd-layers__title'));
                const active = getComputedStyle(root.querySelector('.tm-wd-layers__item--active'));
                const trigger = root.querySelector('.tm-wd-layers__row-menu .tm-context-menu__trigger').getBoundingClientRect();
                return {
                    row: Math.round(row.getBoundingClientRect().height),
                    visibility: box('.tm-wd-layers__visibility'),
                    lock: box('.tm-wd-layers__lock'),
                    trigger: { w: Math.round(trigger.width), h: Math.round(trigger.height) },
                    transform: title.textTransform,
                    titleSize: title.fontSize,
                    activeBg: active.backgroundColor,
                    primarySubtle: (() => { const p = document.createElement('i'); p.style.background = 'var(--tm-color-primary-subtle)'; document.body.appendChild(p); const c = getComputedStyle(p).backgroundColor; p.remove(); return c; })(),
                };
            }
            """);

        Assert.AreEqual(36, metrics.GetProperty("row").GetInt32(), "layer rows are 36px");
        Assert.AreEqual(32, metrics.GetProperty("visibility").GetProperty("w").GetInt32(), "icon buttons are 32px wide");
        Assert.AreEqual(32, metrics.GetProperty("visibility").GetProperty("h").GetInt32());
        Assert.AreEqual(32, metrics.GetProperty("lock").GetProperty("h").GetInt32());
        Assert.AreEqual(32, metrics.GetProperty("trigger").GetProperty("w").GetInt32(), "the row menu trigger is a 32px button");
        Assert.AreEqual("none", metrics.GetProperty("transform").GetString(), "the title is sentence case, not uppercase");
        Assert.AreEqual("14px", metrics.GetProperty("titleSize").GetString(), "the title is body size, not an 11px caption");
        Assert.AreEqual(metrics.GetProperty("primarySubtle").GetString(), metrics.GetProperty("activeBg").GetString(),
            "the active row uses --tm-color-primary-subtle");

        // The trash is hidden at rest on a fine pointer and appears with row hover / focus.
        // Row 1 is the active one (its trash is always shown); row 3 is a plain row.
        var secondRow = panel.Locator(".tm-wd-layers__item").Nth(3);
        var trash = secondRow.Locator(".tm-wd-layers__delete");
        await page.Mouse.MoveAsync(0, 0);
        Assert.AreEqual("0", await trash.EvaluateAsync<string>("e => getComputedStyle(e).opacity"), "trash is quiet at rest");
        await secondRow.HoverAsync();
        Assert.AreEqual("1", await trash.EvaluateAsync<string>("e => getComputedStyle(e).opacity"), "trash shows on row hover");
        await page.Mouse.MoveAsync(0, 0);
        await secondRow.Locator(".tm-wd-layers__visibility").FocusAsync();
        Assert.AreEqual("1", await trash.EvaluateAsync<string>("e => getComputedStyle(e).opacity"), "trash shows on :focus-within");

        await ShotAsync(panel, "layers-panel--1440");
        // The full-list baseline must not carry the focus ring of the :focus-within probe above.
        await page.EvaluateAsync("() => document.activeElement?.blur()");
        await ShotFullListAsync(panel, "layers-panel--1440-full");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Layers panel at 1024 touch: every target reaches 44px (--tm-touch-target); no hover is required for any action - in a narrow list the trash steps aside and Delete lives in the row menu")]
    public async Task Panel_1024Touch_TargetsReach44px_AndDeleteIsInTheRowMenu()
    {
        Directory.CreateDirectory(ShotDirectory);
        var page = await OpenAsync(1024, 768, touch: true);
        var panel = await PreparePanelAsync(page);

        var coarse = await page.EvaluateAsync<bool>("() => matchMedia('(pointer: coarse)').matches");
        TestContext.WriteLine($"(pointer: coarse) = {coarse}");
        if (!coarse)
        {
            // The browser build did not emulate a coarse pointer for this context - force the same sheet through the
            // media-less rules would be a lie, so say so instead of passing silently.
            Assert.Inconclusive("Chromium did not report (pointer: coarse) for the touch context; the 44px rule cannot be asserted here.");
        }

        var metrics = await page.EvaluateAsync<System.Text.Json.JsonElement>(
            """
            () => {
                const root = document.querySelector('.tm-wd-editor .tm-wd-layers');
                const h = s => Math.round(root.querySelector(s).getBoundingClientRect().height);
                const w = s => Math.round(root.querySelector(s).getBoundingClientRect().width);
                return {
                    row: h('.tm-wd-layers__item'), active: h('.tm-wd-layers__active'), visibility: h('.tm-wd-layers__visibility'),
                    lock: h('.tm-wd-layers__lock'), add: h('.tm-wd-layers__add-btn'), addW: w('.tm-wd-layers__add-btn'),
                    trigger: h('.tm-wd-layers__row-menu .tm-context-menu__trigger'),
                    token: parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--tm-touch-target')) * parseFloat(getComputedStyle(document.documentElement).fontSize),
                };
            }
            """);
        var token = (int)Math.Round(metrics.GetProperty("token").GetDouble());
        Assert.AreEqual(44, token, "--tm-touch-target is 44px");
        foreach (var name in new[] { "row", "active", "visibility", "lock", "add", "addW", "trigger" })
        {
            Assert.IsTrue(metrics.GetProperty(name).GetInt32() >= 44, $"{name} must be at least 44px on a coarse pointer, was {metrics.GetProperty(name).GetInt32()}");
        }

        // No action hides behind hover: the menu (Rename / Move / Delete) is reachable by a tap.
        var row = panel.Locator(".tm-wd-layers__item").Nth(1);
        await row.Locator(".tm-wd-layers__row-menu .tm-context-menu__trigger").TapAsync();
        await row.Locator(".tm-wd-layers__row-menu-item[data-action='delete']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5_000 });
        await ShotPageAsync(page, "layers-panel--1024-touch-menu");
        await page.Keyboard.PressAsync("Escape");

        await ShotAsync(panel, "layers-panel--1024-touch");
        await ShotFullListAsync(panel, "layers-panel--1024-touch-full");
    }

    // ── Edge cases ────────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Edge: the default layer has no delete; the first row cannot move up, the last cannot move down; Escape closes the row menu and returns focus")]
    public async Task Edge_DefaultLayerCannotBeDeleted_EdgesCannotMove_EscapeReturnsFocus()
    {
        var page = await OpenAsync(1440, 900, touch: false);
        var panel = await PreparePanelAsync(page);

        var first = panel.Locator(".tm-wd-layers__item").First;
        Assert.AreEqual(0, await first.Locator(".tm-wd-layers__delete").CountAsync(), "the default layer has no trash button");

        await first.Locator(".tm-wd-layers__row-menu .tm-context-menu__trigger").ClickAsync();
        var up = first.Locator(".tm-wd-layers__row-menu-item[data-action='up']");
        await up.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5_000 });
        Assert.IsTrue(await up.IsDisabledAsync(), "the first row cannot move up");
        Assert.AreEqual(0, await first.Locator(".tm-wd-layers__row-menu-item[data-action='delete']").CountAsync(),
            "nor does its menu offer Delete");

        await page.Keyboard.PressAsync("Escape");
        await up.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 5_000 });
        var focusedIsTrigger = await page.EvaluateAsync<bool>(
            "() => document.activeElement?.classList.contains('tm-context-menu__trigger') === true");
        Assert.IsTrue(focusedIsTrigger, "Escape returns focus to the ⋯ trigger");

        var last = panel.Locator(".tm-wd-layers__item").Last;
        await last.Locator(".tm-wd-layers__row-menu .tm-context-menu__trigger").ClickAsync();
        var down = last.Locator(".tm-wd-layers__row-menu-item[data-action='down']");
        await down.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5_000 });
        Assert.IsTrue(await down.IsDisabledAsync(), "the last row cannot move down");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Edge: F2 renames from the eye button, Escape cancels without an undo step; radios roam with the arrow keys")]
    public async Task Edge_F2FromAnyControl_EscapeCancels_RadioArrowsActivate()
    {
        var page = await OpenAsync(1440, 900, touch: false);
        var panel = await PreparePanelAsync(page);

        await panel.Locator(".tm-wd-layers__item").Nth(1).Locator(".tm-wd-layers__visibility").FocusAsync();
        await page.Keyboard.PressAsync("F2");
        var input = panel.Locator(".tm-wd-layers__name-input");
        await input.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5_000 });
        await input.FillAsync("Discarded");
        await page.Keyboard.PressAsync("Escape");
        await input.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 5_000 });
        Assert.AreEqual(0, await panel.Locator(".tm-wd-layers__name", new() { HasTextString = "Discarded" }).CountAsync(),
            "Escape discards the rename");

        var radios = panel.Locator("[role='radio']");
        await radios.First.FocusAsync();
        await page.Keyboard.PressAsync("ArrowDown");
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('.tm-wd-layers [role=radio]')[1].getAttribute('aria-checked') === 'true'",
            null, new PageWaitForFunctionOptions { Timeout = 5_000 });
        Assert.AreEqual("false", await radios.First.GetAttributeAsync("aria-checked"));
        Assert.AreEqual(1, await panel.Locator("[role='radio'][aria-checked='true']").CountAsync(), "exactly one active layer");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Edge: a drag over a row paints the drop indicator line (before it lands) and clears it on leave")]
    public async Task Edge_DragOver_PaintsTheDropIndicator()
    {
        var page = await OpenAsync(1440, 900, touch: false);
        var panel = await PreparePanelAsync(page);

        var editor = page.Locator(".tm-wd-editor").First;
        var before = await LayerNamesAsync(editor);
        Assert.IsTrue(before.Length >= 3, "needs several layers to reorder");

        var rows = panel.Locator(".tm-wd-layers__item");
        var from = rows.First;
        var to = rows.Last;
        await from.DragToAsync(to);

        // After the drop the order changed through the command stack: the dragged first row takes the last row's place.
        var expected = before.Skip(1).Append(before[0]).ToArray();
        await page.WaitForFunctionAsync(
            "first => document.querySelectorAll('.tm-wd-editor')[0].querySelector('.tm-wd-layers__item:last-child .tm-wd-layers__name')?.textContent.trim() === first",
            before[0], new PageWaitForFunctionOptions { Timeout = 5_000 });
        CollectionAssert.AreEqual(expected, await LayerNamesAsync(editor), "the dragged layer lands at the drop target's position");

        await page.WaitForFunctionAsync(
            "() => !document.querySelector('.tm-wd-editor .tm-wd-layers__item--drag-over') && !document.querySelector('.tm-wd-editor .tm-wd-layers__item--dragging')",
            null, new PageWaitForFunctionOptions { Timeout = 5_000 });
        Assert.AreEqual(0, await panel.Locator(".tm-wd-layers__item--drag-over").CountAsync(), "the indicator is cleared after the drop");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Edge: arrow keys and Alt+Arrow inside the layers panel never nudge the selected canvas element")]
    public async Task Edge_ArrowKeysInTheLayersPanel_DoNotNudgeTheSelection()
    {
        var page = await OpenAsync(1440, 900, touch: false);
        var editor = page.Locator(".tm-wd-editor").First;
        var panel = editor.Locator(".tm-wd-layers");
        await panel.Locator(".tm-wd-layers__add-btn").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('.tm-wd-layers__item').length >= 2",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        if (await panel.Locator(".tm-wd-layers__name-input").CountAsync() > 0)
        {
            await page.Keyboard.PressAsync("Enter");
        }

        // The toolbox lives in the Components tab: insert from there, then return to the Layers tab.
        await editor.Locator("[data-region='left'] [role='tab']", new() { HasTextString = "Components" }).ClickAsync();
        await editor.Locator(".tm-wd-toolbox__item[data-component-type='TmButton']").First.ClickAsync();
        await page.WaitForSelectorAsync(".tm-wd-editor .tm-wd-selection rect",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 10_000 });
        await editor.Locator("[data-region='left'] [role='tab']", new() { HasTextString = "Layers" }).ClickAsync();

        const string PositionScript = "() => document.querySelectorAll('.tm-wd-editor')[0].querySelector('g[data-el-id]').getAttribute('transform')";
        var before = await page.EvaluateAsync<string>(PositionScript);

        await panel.Locator("[role='radio']").First.FocusAsync();
        await page.Keyboard.PressAsync("ArrowDown");
        await panel.Locator(".tm-wd-layers__name").First.FocusAsync();
        await page.Keyboard.PressAsync("Alt+ArrowDown");
        await page.WaitForTimeoutAsync(300);

        Assert.AreEqual(before, await page.EvaluateAsync<string>(PositionScript),
            "the selected element did not move while arrows were pressed in the layers panel");

        // Delete / Backspace / Escape on a focused layer control belong to the panel too: the canvas element stays
        // and stays selected.
        const string CountScript = "() => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('g[data-el-id]').length";
        var elementCount = await page.EvaluateAsync<int>(CountScript);
        Assert.IsTrue(elementCount > 0, "the inserted element is on the canvas");

        await panel.Locator(".tm-wd-layers__visibility").First.FocusAsync();
        await page.Keyboard.PressAsync("Delete");
        await page.Keyboard.PressAsync("Backspace");
        await page.WaitForTimeoutAsync(300);
        Assert.AreEqual(elementCount, await page.EvaluateAsync<int>(CountScript),
            "Delete / Backspace on a layer control must not delete the canvas selection");

        await panel.Locator(".tm-wd-layers__lock").First.FocusAsync();
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(300);
        Assert.IsTrue(await editor.Locator(".tm-wd-selection rect").CountAsync() > 0,
            "Escape on a layer control must not clear the canvas selection");
        Assert.AreEqual(before, await page.EvaluateAsync<string>(PositionScript));
    }
    // ── Accessibility ─────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [Description("axe-core reports no WCAG 2 A/AA violation inside the layers panel (with the row menu open)")]
    public async Task Axe_LayersPanel_HasNoViolations()
    {
        var page = await OpenAsync(1440, 900, touch: false);
        var panel = await PreparePanelAsync(page);

        await page.AddScriptTagAsync(new PageAddScriptTagOptions { Url = AxeCdn });

        var violations = await page.EvaluateAsync<string[]>(
            """
            async () => {
                const host = document.querySelector('.tm-wd-editor .tm-wd-layers');
                const result = await axe.run(host, {
                    runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa'] },
                    resultTypes: ['violations']
                });
                return result.violations.map(v => `${v.impact}: ${v.id} - ${v.help} (${v.nodes.map(n => n.target.join(' ') + ' <' + n.html.slice(0, 120) + '>').join('; ')})`);
            }
            """);
        Assert.AreEqual(0, violations.Length, string.Join(Environment.NewLine, violations));

        // Again with the first non-default row's menu open (a menu is the most structured part of the panel).
        await panel.Locator(".tm-wd-layers__item").Nth(1).Locator(".tm-wd-layers__row-menu .tm-context-menu__trigger").ClickAsync();
        await panel.Locator(".tm-wd-layers__item").Nth(1).Locator(".tm-wd-layers__row-menu-item[data-action='rename']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5_000 });
        // The menu fades in: axe must not sample the danger label mid-fade (a half-transparent red fails 4.5:1).
        await page.EvaluateAsync("async () => { await Promise.all(document.getAnimations().map(a => a.finished.catch(() => { }))); }");
        var menuViolations = await page.EvaluateAsync<string[]>(
            """
            async () => {
                const result = await axe.run({ include: [['.tm-wd-editor .tm-wd-layers'], ['.tm-context-menu']] }, {
                    runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa'] },
                    resultTypes: ['violations']
                });
                return result.violations.map(v => `${v.impact}: ${v.id} - ${v.help} (${v.nodes.map(n => n.target.join(' ') + ' <' + n.html.slice(0, 120) + '> ' + n.any.map(a => a.message).join(' / ')).join('; ')})`);
            }
            """);
        Assert.AreEqual(0, menuViolations.Length, string.Join(Environment.NewLine, menuViolations));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<IPage> OpenAsync(int width, int height, bool touch)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            HasTouch = touch,
            ViewportSize = new ViewportSize { Width = width, Height = height },
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);

        var page = await context.NewPageAsync();
        // The layout resolves from the editor's CONTAINER and a panel the shell railed at a narrow first measurement stays a rail,
        // so the demo sidebar / page margin is removed BEFORE the app boots (not after), giving the editor its real width at once.
        await page.AddInitScriptAsync(
            """
            localStorage.setItem('tm-demo-culture', 'en');
            document.addEventListener('DOMContentLoaded', () => {
                const style = document.createElement('style');
                style.textContent = 'aside.fixed.inset-y-0.left-0.z-30 { display: none !important; } main { margin-left: 0 !important; width: 100vw !important; max-width: 100vw !important; }';
                document.head.appendChild(style);
            });
            """);
        await page.GotoAsync($"{BaseUrl}{EditorUrl}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60_000 });
        await WaitForAppReadyAsync(page);
        // The Layers tab is not the default tab of the left panel (Components is): open it, the tab mounts lazily.
        // A tablet editor container (phase 6) keeps the left panel in an overlay drawer: open it from the toolbar first.
        await page.WaitForSelectorAsync(".tm-wd-editor .tm-wd-canvas__svg", new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 30_000 });
        await page.WaitForTimeoutAsync(1200);
        var drawerToggle = page.Locator(".tm-wd-editor [data-testid='wd-toggle-left']").First;
        if (await drawerToggle.CountAsync() > 0) await drawerToggle.ClickAsync();
        await page.WaitForSelectorAsync(".tm-wd-editor [role='tab']",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 30_000 });
        await page.Locator(".tm-wd-editor [data-region='left'] [role='tab'], .tm-wd-editor [data-testid='wd-left-drawer'] [role='tab']", new() { HasTextString = "Layers" }).First.ClickAsync();
        await page.WaitForSelectorAsync(".tm-wd-editor .tm-wd-layers__item",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 30_000 });

        // The minimap card and the demo sidebar would cover the capture / intercept clicks. The sidebar also takes the
        // editor's container width (the layout resolves from the CONTAINER, not the window): at a 1024 viewport the
        // editor would measure ~664 px and resolve mobile, so the demo page margin is zeroed to give a real tablet width.
        await page.EvaluateAsync(
            """
            () => {
                const style = document.createElement('style');
                style.textContent = '.tm-wd-editor__minimap-wrap { display: none !important; } aside.fixed.inset-y-0.left-0.z-30 { display: none !important; } main { margin-left: 0 !important; width: 100vw !important; max-width: 100vw !important; }';
                document.head.appendChild(style);
            }
            """);
        await page.WaitForTimeoutAsync(800);
        return page;
    }

    /// <summary>Gives the panel five layers (the demo starts with one) with the second one active, and returns the panel.</summary>
    private static async Task<ILocator> PreparePanelAsync(IPage page)
    {
        var editor = page.Locator(".tm-wd-editor").First;
        var panel = editor.Locator(".tm-wd-layers");
        await panel.ScrollIntoViewIfNeededAsync();

        var names = new[] { "Navigation", "Search", "Hotel list", "Hotel detail" };
        foreach (var name in names)
        {
            var count = await panel.Locator(".tm-wd-layers__item").CountAsync();
            await panel.Locator(".tm-wd-layers__add-btn").ClickAsync();
            await page.WaitForFunctionAsync(
                "n => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('.tm-wd-layers__item').length > n",
                count, new PageWaitForFunctionOptions { Timeout = 10_000 });

            var row = panel.Locator(".tm-wd-layers__item").Last;
            await row.Locator(".tm-wd-layers__name").DblClickAsync();
            var input = row.Locator(".tm-wd-layers__name-input");
            await input.FillAsync(name);
            await page.Keyboard.PressAsync("Enter");
            await row.Locator(".tm-wd-layers__name", new() { HasTextString = name }).WaitForAsync();
        }

        // Hide one layer and lock another so the screenshot shows the state variants.
        // The second row is the active one (tinted), like the reference.
        await panel.Locator(".tm-wd-layers__item").Nth(1).Locator("[role='radio']").ClickAsync();
        await panel.Locator(".tm-wd-layers__item").Nth(4).Locator(".tm-wd-layers__visibility").ClickAsync();
        await panel.Locator(".tm-wd-layers__item").Nth(2).Locator(".tm-wd-layers__lock").ClickAsync();
        await page.Mouse.MoveAsync(0, 0);
        return panel;
    }

    private async Task ShotAsync(ILocator locator, string name)
    {
        // Review captures show the list from its top (the active row) and once with the height cap lifted, so every row is in frame.
        await locator.Locator(".tm-wd-layers__list").EvaluateAsync("e => { e.scrollTop = 0; }");
        await locator.Page.Mouse.MoveAsync(0, 0);
        var path = Path.Combine(ShotDirectory, name + ".png");
        await locator.ScreenshotAsync(new LocatorScreenshotOptions { Path = path, Type = ScreenshotType.Png, Timeout = 60_000 });
        TestContext.AddResultFile(path);
    }

    /// <summary>Captures the whole list: the demo column is short, so the height cap is lifted for the picture only.</summary>
    private async Task ShotFullListAsync(ILocator panel, string name)
    {
        await panel.EvaluateAsync("e => { e.style.maxHeight = 'none'; }");
        await ShotAsync(panel, name);
        await panel.EvaluateAsync("e => { e.style.maxHeight = ''; }");
    }

    private async Task ShotPageAsync(IPage page, string name)
    {
        var path = Path.Combine(ShotDirectory, name + ".png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, Type = ScreenshotType.Png, FullPage = false, Timeout = 60_000 });
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
}
