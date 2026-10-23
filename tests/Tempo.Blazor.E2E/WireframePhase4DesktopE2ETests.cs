using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Wireframe redesign plan ce19aa8c, phase 4 — the desktop restructure onto <c>TmEditorShell</c>: the Pages panel
/// (WF1, <c>PageNavigation=Panel</c> on the interactive demo editor), device presets (WF2), element names (WF3), the
/// "More" menu and the render isolation of the left tabs. Captures the editor at 1440×900 and 1280×800 (empty
/// selection, selected element, Pages tab, Layers tab, light/dark) into <c>__screenshots__/wireframe/phase4/</c> only
/// with <c>TM_E2E_UPDATE_BASELINE=1</c> (otherwise into the git-ignored per-run TestResults folder), to be reviewed
/// against <c>output/imagegen/tempo-reimagined/Tempo.Blazor.Wireframe/wireframe-editor--desktop.png</c> and
/// <c>wireframe-layers-panel--desktop.png</c>.
/// </summary>
[TestClass]
public sealed class WireframePhase4DesktopE2ETests : WasmTestBase
{
    private const string EditorUrl = "/wireframe-editor";

    private static bool UpdateBaseline =>
        string.Equals(Environment.GetEnvironmentVariable("TM_E2E_UPDATE_BASELINE"), "1", StringComparison.Ordinal);

    private static string ShotDirectory => UpdateBaseline
        ? Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "__screenshots__", "wireframe", "phase4")
        : Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "wireframe-phase4");

    // ── WF1: Pages panel ──────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [Description("PagesPanel: add, rename (menu + Enter), duplicate and delete pages from the Pages tab; each uses the document model")]
    public async Task PagesPanel_RenameDuplicateDelete()
    {
        var page = await OpenAsync(1440, 900, dark: false);
        var editor = page.Locator(".tm-wd-editor").First;
        await OpenLeftTabAsync(page, editor, "Pages");

        var rows = editor.Locator(".tm-wd-pages__item");
        var before = await rows.CountAsync();

        await editor.Locator("[data-testid='wd-page-add']").ClickAsync();
        await page.WaitForFunctionAsync("n => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('.tm-wd-pages__item').length === n + 1",
            before, new PageWaitForFunctionOptions { Timeout = 10_000 });
        Assert.AreEqual("page", await rows.Last.Locator(".tm-wd-pages__select").GetAttributeAsync("aria-current"), "the new page is the current one");

        // Rename through the row menu.
        await rows.Last.Locator(".tm-wd-pages__row-menu .tm-context-menu__trigger").ClickAsync();
        await rows.Last.Locator(".tm-wd-pages__row-menu-item[data-action='rename']").ClickAsync();
        var input = rows.Last.Locator(".tm-wd-pages__name-input");
        await input.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5_000 });
        await input.FillAsync("Checkout");
        await page.Keyboard.PressAsync("Enter");
        await rows.Last.Locator(".tm-wd-pages__select", new() { HasTextString = "Checkout" }).WaitForAsync();
        Assert.AreEqual("Checkout", await editor.Locator("[data-testid='wd-page-name']").InputValueAsync(), "the inspector's Page section follows the rename");

        // Duplicate.
        await rows.Last.Locator(".tm-wd-pages__row-menu .tm-context-menu__trigger").ClickAsync();
        await rows.Last.Locator(".tm-wd-pages__row-menu-item[data-action='duplicate']").ClickAsync();
        await page.WaitForFunctionAsync("n => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('.tm-wd-pages__item').length === n + 2",
            before, new PageWaitForFunctionOptions { Timeout = 10_000 });
        Assert.IsTrue(await editor.Locator(".tm-wd-pages__select", new() { HasTextString = "Checkout (copy)" }).CountAsync() > 0, "the copy is named");

        // Delete the active copy.
        var copyRow = editor.Locator(".tm-wd-pages__item", new() { Has = page.Locator(".tm-wd-pages__select", new() { HasTextString = "Checkout (copy)" }) });
        await copyRow.Locator(".tm-wd-pages__row-menu .tm-context-menu__trigger").ClickAsync();
        await copyRow.Locator(".tm-wd-pages__row-menu-item[data-action='delete']").ClickAsync();
        await page.WaitForFunctionAsync("n => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('.tm-wd-pages__item').length === n + 1",
            before, new PageWaitForFunctionOptions { Timeout = 10_000 });
        Assert.AreEqual(1, await editor.Locator(".tm-wd-pages__item [aria-current='page']").CountAsync(), "exactly one page is current afterwards");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Edge: Alt+ArrowDown on a page row reorders it and Delete never deletes a page; the keys do not nudge the canvas selection")]
    public async Task PagesPanel_AltArrowReorders_DeleteKeyDoesNothing()
    {
        var page = await OpenAsync(1440, 900, dark: false);
        var editor = page.Locator(".tm-wd-editor").First;
        await OpenLeftTabAsync(page, editor, "Pages");
        await editor.Locator("[data-testid='wd-page-add']").ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('.tm-wd-pages__item').length >= 2",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });

        var names = async () => await editor.Locator(".tm-wd-pages__select").AllInnerTextsAsync();
        var before = (await names()).ToArray();

        await editor.Locator(".tm-wd-pages__select").First.FocusAsync();
        await page.Keyboard.PressAsync("Alt+ArrowDown");
        await page.WaitForFunctionAsync("first => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('.tm-wd-pages__select')[1]?.textContent.trim() === first",
            before[0].Trim(), new PageWaitForFunctionOptions { Timeout = 5_000 });
        Assert.AreEqual(before[0].Trim(), (await names())[1].Trim(), "the first page moved down one place");

        var count = await editor.Locator(".tm-wd-pages__item").CountAsync();
        await editor.Locator(".tm-wd-pages__select").First.FocusAsync();
        await page.Keyboard.PressAsync("Delete");
        await page.WaitForTimeoutAsync(300);
        Assert.AreEqual(count, await editor.Locator(".tm-wd-pages__item").CountAsync(), "the Delete key never deletes a page");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Default PageNavigation=Tabs (third demo editor): the horizontal page tabs and the Components / Layers left tabs are unchanged")]
    public async Task PageNavigationTabs_Default_KeepsTodayTabs()
    {
        var page = await OpenAsync(1440, 900, dark: false);
        var tabsEditor = page.Locator(".tm-wd-editor").Nth(2);
        await tabsEditor.ScrollIntoViewIfNeededAsync();

        Assert.IsTrue(await tabsEditor.Locator(".tm-wd-editor__page-tab").CountAsync() >= 1, "horizontal page tabs render");
        var tabTitles = await tabsEditor.Locator("[data-region='left'] [role='tab']").AllInnerTextsAsync();
        CollectionAssert.AreEqual(new[] { "Components", "Layers" }, tabTitles.Select(t => t.Trim()).ToArray());
        Assert.AreEqual(0, await tabsEditor.Locator(".tm-wd-pages").CountAsync());
    }

    // ── WF2: device presets ───────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [Description("DevicePreset: iPhone 14 resizes the canvas to 390×844 and Undo restores it")]
    public async Task DevicePreset_ResizesCanvas()
    {
        var page = await OpenAsync(1440, 900, dark: false);
        var editor = page.Locator(".tm-wd-editor").First;

        var before = await CanvasSizeAsync(page);
        await editor.Locator("[data-testid='wd-device-preset']").SelectOptionAsync("iphone14");
        await page.WaitForFunctionAsync("() => { const r = document.querySelectorAll('.tm-wd-editor')[0].querySelector('.tm-wd-canvas__svg > rect'); return r && r.getAttribute('width') === '390'; }",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        var after = await CanvasSizeAsync(page);
        Assert.AreEqual((390.0, 844.0), after, "the page rect follows the preset");
        Assert.AreEqual("390", await editor.Locator("[data-testid='wd-canvas-width']").InputValueAsync(), "the inspector's width field follows too");

        await editor.Locator("button.tm-toolbar-btn[aria-label^='Undo']").ClickAsync();
        await page.WaitForFunctionAsync("w => document.querySelectorAll('.tm-wd-editor')[0].querySelector('.tm-wd-canvas__svg > rect').getAttribute('width') === String(w)",
            before.W, new PageWaitForFunctionOptions { Timeout = 10_000 });
        Assert.AreEqual(before, await CanvasSizeAsync(page), "Undo restores the previous canvas size");
    }

    // ── "More" menu ───────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [Description("MoreMenu: format painter, read-only, rulers and the panel toggles are entries of the toolbar's More menu")]
    public async Task MoreMenu_ContainsReadOnlyAndFormatPainter()
    {
        var page = await OpenAsync(1440, 900, dark: false);
        var editor = page.Locator(".tm-wd-editor").First;

        await editor.Locator("button.tm-toolbar-more").ClickAsync();
        var menu = page.Locator("[role='menu']").First;
        await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5_000 });
        var labels = (await menu.Locator("[role='menuitem'], [role='menuitemcheckbox']").AllInnerTextsAsync()).Select(l => l.Trim()).ToArray();
        foreach (var expected in new[] { "Copy style", "Paste style", "Paste size", "Show rulers", "Read-only", "Components panel", "Properties panel" })
            CollectionAssert.Contains(labels, expected, $"'{expected}' is a More entry (got: {string.Join(", ", labels)})");

        // The panel toggle collapses the inspector (shell state) and flips its pressed state.
        await menu.Locator("[role='menuitemcheckbox']", new() { HasTextString = "Properties panel" }).ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelectorAll('.tm-wd-editor')[0].querySelector('[data-region=\"right\"]') === null",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
    }

    // ── WF3: element name ─────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [Description("ElementName: the inspector's Name field names the element; the canvas group gets an aria-label and a tooltip; Undo clears it")]
    public async Task ElementName_ShowsInInspector()
    {
        var page = await OpenAsync(1440, 900, dark: false);
        var editor = page.Locator(".tm-wd-editor").First;
        await editor.Locator(".tm-wd-toolbox__item[data-component-type='TmButton']").First.ClickAsync();
        var name = editor.Locator("[data-testid='wd-element-name']");
        await name.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        await name.FillAsync("Login button");
        await name.PressAsync("Tab");
        await page.WaitForFunctionAsync("() => document.querySelectorAll('.tm-wd-editor')[0].querySelector('g[data-el-id][aria-label=\"Login button\"]') !== null",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        Assert.AreEqual("Login button", await editor.Locator("g[data-el-id] > title").First.TextContentAsync(), "the tooltip carries the name");

        await editor.Locator("button.tm-toolbar-btn[aria-label^='Undo']").ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelectorAll('.tm-wd-editor')[0].querySelector('g[data-el-id][aria-label]') === null",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Edge: Delete element in the inspector removes the selection; blank names are not saved")]
    public async Task ElementSection_DeleteButton_RemovesTheElement()
    {
        var page = await OpenAsync(1440, 900, dark: false);
        var editor = page.Locator(".tm-wd-editor").First;
        var before = await editor.Locator("g[data-el-id]").CountAsync();
        await editor.Locator(".tm-wd-toolbox__item[data-component-type='TmButton']").First.ClickAsync();
        await page.WaitForFunctionAsync("n => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('g[data-el-id]').length === n + 1",
            before, new PageWaitForFunctionOptions { Timeout = 10_000 });

        await editor.Locator("[data-testid='wd-delete-element']").ClickAsync();
        await page.WaitForFunctionAsync("n => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('g[data-el-id]').length === n",
            before, new PageWaitForFunctionOptions { Timeout = 10_000 });
        await editor.Locator("[data-testid='wd-section-canvas']").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    // ── Render isolation ──────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Switching the left tab keeps the toolbox mounted and does not recreate the canvas SVG (its search text survives)")]
    public async Task LeftTabSwitch_KeepsTheToolboxAndTheCanvas()
    {
        var page = await OpenAsync(1440, 900, dark: false);
        var editor = page.Locator(".tm-wd-editor").First;
        await editor.Locator(".tm-wd-toolbox__search-input").FillAsync("Button");
        await page.WaitForTimeoutAsync(300);
        await page.EvaluateAsync("() => { const e = document.querySelectorAll('.tm-wd-editor')[0]; window.__wdSvg = e.querySelector('.tm-wd-canvas__svg'); }");

        await OpenLeftTabAsync(page, editor, "Layers");
        await OpenLeftTabAsync(page, editor, "Components");

        Assert.AreEqual("Button", await editor.Locator(".tm-wd-toolbox__search-input").InputValueAsync(), "the toolbox keeps its search text across tab switches");
        Assert.IsTrue(await page.EvaluateAsync<bool>("() => window.__wdSvg === document.querySelectorAll('.tm-wd-editor')[0].querySelector('.tm-wd-canvas__svg') && window.__wdSvg.isConnected"),
            "the canvas SVG element is the same node: a tab switch did not rebuild it");
    }

    // ── Header slots (G9) ─────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [Description("HeaderActions: the demo's Open… button lives in the editor header slot (no built-in Open or Save)")]
    public async Task HeaderSlots_DemoOpenButton_IsInTheHeader()
    {
        var page = await OpenAsync(1440, 900, dark: false);
        var editor = page.Locator(".tm-wd-editor").First;

        Assert.AreEqual(1, await editor.Locator("[data-region='header'] [data-testid='wf-open-btn']").CountAsync());
        Assert.AreEqual(0, await editor.Locator("button", new() { HasTextString = "Save" }).CountAsync(), "the editor has no built-in Save");
    }

    // ── Screenshots (review against the desktop references) ───────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [Description("1440×900 and 1280×800, light and dark: empty selection, selected element, Pages tab, Layers tab")]
    public async Task Screenshots_Desktop_1440_And_1280()
    {
        Directory.CreateDirectory(ShotDirectory);
        foreach (var (w, h) in new[] { (1440, 900), (1280, 800) })
        {
            foreach (var dark in new[] { false, true })
            {
                var tag = $"{w}-{(dark ? "dark" : "light")}";
                var page = await OpenAsync(w, h, dark);
                var editor = page.Locator(".tm-wd-editor").First;
                await editor.ScrollIntoViewIfNeededAsync();

                await ShotAsync(editor, $"{tag}--empty-selection");

                await editor.Locator(".tm-wd-toolbox__item[data-component-type='TmButton']").First.ClickAsync();
                await editor.Locator("[data-testid='wd-element-name']").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
                await ShotAsync(editor, $"{tag}--selected-element");

                await OpenLeftTabAsync(page, editor, "Pages");
                await editor.Locator("[data-testid='wd-page-add']").ClickAsync();
                await page.WaitForFunctionAsync("() => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('.tm-wd-pages__item').length >= 2",
                    null, new PageWaitForFunctionOptions { Timeout = 10_000 });
                await ShotAsync(editor, $"{tag}--pages-tab");

                await OpenLeftTabAsync(page, editor, "Layers");
                await editor.Locator(".tm-wd-layers__add-btn").ClickAsync();
                await page.WaitForTimeoutAsync(500);
                if (await editor.Locator(".tm-wd-layers__name-input").CountAsync() > 0) await page.Keyboard.PressAsync("Enter");
                await ShotAsync(editor, $"{tag}--layers-tab");
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<IPage> OpenAsync(int width, int height, bool dark)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
        });
        RegisterContext(context);

        var page = await context.NewPageAsync();
        await page.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        // The editor resolves its layout from its CONTAINER (desktop from 1200 px): the demo's sidebar margin would leave it a tablet
        // container, so the sidebar and the page margin are removed BEFORE the app boots (not after) - the desktop layout these specs test.
        await page.AddInitScriptAsync(
            """
            document.addEventListener('DOMContentLoaded', () => {
                const style = document.createElement('style');
                style.textContent = 'aside.fixed.inset-y-0.left-0.z-30 { display: none !important; } main { margin-left: 0 !important; width: 100vw !important; max-width: 100vw !important; }';
                document.head.appendChild(style);
            });
            """);        await page.GotoAsync($"{BaseUrl}{EditorUrl}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60_000 });
        await WaitForAppReadyAsync(page);
        await page.WaitForSelectorAsync(".tm-wd-editor .tm-wd-toolbox__item",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 30_000 });

        if (dark)
        {
            // The test hides the demo sidebar (it holds the toggle) so the editor gets a desktop container: dispatch the click on the hidden toggle.
            await page.Locator("[data-testid='theme-toggle']").First.DispatchEventAsync("click");
            await page.WaitForFunctionAsync("() => document.querySelector('[data-theme=\"dark\"]') !== null",
                null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        }

        // The minimap card and the demo sidebar would cover the capture / intercept clicks.
        await page.EvaluateAsync(
            """
            () => {
                const style = document.createElement('style');
                style.textContent = '.tm-wd-editor__minimap-wrap { display: none !important; } aside.fixed.inset-y-0.left-0.z-30 { display: none !important; }';
                document.head.appendChild(style);
            }
            """);
        await page.WaitForTimeoutAsync(1500);   // let the toolbar overflow measurement settle
        return page;
    }

    private static async Task OpenLeftTabAsync(IPage page, ILocator editor, string title)
    {
        await editor.Locator("[data-region='left'] [role='tab']", new() { HasTextString = title }).ClickAsync();
        await page.WaitForTimeoutAsync(400);
    }

    private static async Task<(double W, double H)> CanvasSizeAsync(IPage page)
    {
        var size = await page.EvaluateAsync<double[]>(
            "() => { const r = document.querySelectorAll('.tm-wd-editor')[0].querySelector('.tm-wd-canvas__svg > rect'); return [parseFloat(r.getAttribute('width')), parseFloat(r.getAttribute('height'))]; }");
        return (size[0], size[1]);
    }

    private async Task ShotAsync(ILocator locator, string name)
    {
        await locator.Page.Mouse.MoveAsync(0, 0);
        var path = Path.Combine(ShotDirectory, name + ".png");
        await locator.ScreenshotAsync(new LocatorScreenshotOptions { Path = path, Type = ScreenshotType.Png, Timeout = 60_000 });
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
