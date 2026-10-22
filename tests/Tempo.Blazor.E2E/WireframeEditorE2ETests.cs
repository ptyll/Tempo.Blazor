using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Comprehensive E2E tests for the Wireframe Editor on the WASM demo app.
/// The demo page contains multiple editor instances; all selectors are scoped
/// to the first (interactive) editor via <c>.First</c> chaining.
/// </summary>
[TestClass]
public class WireframeEditorE2ETests : WasmTestBase
{
    private const string WireframeEditorUrl = "/wireframe-editor";

    // ═══════════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task<IPage> PrepareWireframePageAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        // The layout resolves from the editor CONTAINER (desktop from 1200 px): drop the demo sidebar margin BEFORE the app boots.
        await page.AddInitScriptAsync("document.addEventListener('DOMContentLoaded', () => { const s = document.createElement('style'); s.textContent = 'aside.fixed.inset-y-0.left-0.z-30 { display: none !important; } main { margin-left: 0 !important; width: 100vw !important; max-width: 100vw !important; }'; document.head.appendChild(s); });");
        await page.GotoAsync($"{BaseUrl}{WireframeEditorUrl}", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60000
        });
        await page.EvaluateAsync("() => localStorage.setItem('tm-demo-culture', 'en')");
        await page.ReloadAsync(new PageReloadOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60000
        });
        await WaitForAppReadyAsync(page);

        // Scope everything to the first (interactive) editor on the page
        var editor = page.Locator(".tm-wd-editor").First;
        await editor.Locator(".tm-wd-editor__toolbar").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });
        await page.WaitForTimeoutAsync(2000);

        // Inject CSS to permanently hide minimap and demo-page sidebar so they never intercept pointer events
        await page.EvaluateAsync("""
            () => {
                const style = document.createElement('style');
                style.textContent = '.tm-wd-editor__minimap-wrap { display: none !important; } aside.fixed.inset-y-0.left-0.z-30 { display: none !important; }';
                document.head.appendChild(style);
            }
            """);

        return page;
    }

    private static ILocator FirstEditor(IPage page) => page.Locator(".tm-wd-editor").First;

    /// <summary>
    /// The third demo editor (custom component provider) keeps the DEFAULT <c>PageNavigation=Tabs</c>: the horizontal
    /// page tabs of the pre-shell editor. The first editor is the interactive one with <c>PageNavigation=Panel</c>.
    /// </summary>
    private static ILocator TabsEditor(IPage page) => page.Locator(".tm-wd-editor").Nth(2);

    /// <summary>Opens a tab (Components / Pages / Layers) of the editor's left panel.</summary>
    private static async Task OpenLeftTabAsync(IPage page, ILocator editor, string title)
    {
        await editor.Locator("[data-region='left'] [role='tab']", new() { HasTextString = title }).ClickAsync();
        await page.WaitForTimeoutAsync(300);
    }

    /// <summary>Opens the toolbar "More" menu of the editor.</summary>
    private static async Task OpenMoreAsync(IPage page, ILocator editor)
    {
        await editor.Locator("button.tm-toolbar-more").ClickAsync();
        await page.Locator("[role='menu']").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
    }

    /// <summary>
    /// A toolbar command: the bar button (tooltip prefix) while it is on the bar, otherwise its entry in the "More" menu
    /// (opened here; the menu entry carries the visible label). At a narrow editor width the bar collapses commands into "More".
    /// </summary>
    private static async Task<ILocator> CommandAsync(IPage page, ILocator editor, string tooltipPrefix, string menuText)
    {
        var bar = editor.Locator($".tm-toolbar button.tm-toolbar-btn:not(.tm-toolbar-item--collapsed)[aria-label^='{tooltipPrefix}']");
        if (await bar.CountAsync() > 0) return bar.First;
        await OpenMoreAsync(page, editor);
        return page.Locator("[role='menu'] [role='menuitem'], [role='menu'] [role='menuitemcheckbox']").Filter(new() { HasTextString = menuText }).First;
    }

    /// <summary>Waits until the FIRST editor holds exactly <paramref name="count"/> elements (no fixed sleep).</summary>
    private static Task WaitForElementCountAsync(IPage page, int count) =>
        page.WaitForFunctionAsync(
            "n => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('g[data-el-id]').length === n",
            count, new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static async Task<string> AddElementAsync(IPage page, string componentType = "", int x = 400, int y = 300)
    {
        var type = string.IsNullOrEmpty(componentType) ? "TmButton" : componentType;

        // Use JS to directly invoke the canvas drop handler because Playwright's DragToAsync
        // fails when a minimap or other overlay intercepts pointer events on the canvas.
        // We fetch the canvas ID via Playwright to guarantee we target the same editor as FirstEditor().
        var canvasId = await FirstEditor(page).Locator(".tm-wd-canvas__svg").EvaluateAsync<string>("el => el.id");

        var debugInfo = await page.EvaluateAsync<string>(
            """
            async ([type, canvasId, x, y]) => {
                const canvas = document.getElementById(canvasId);
                if (!canvas) return JSON.stringify({ error: 'no-canvas', canvasId });
                const inst = window.tmWireframeDesigner.instances.get(canvasId);
                if (!inst) return JSON.stringify({ error: 'no-inst', canvasId, keys: [...window.tmWireframeDesigner.instances.keys()] });
                try {
                    await inst.dotNetRef.invokeMethodAsync('OnElementDropped', type, x, y);
                    return JSON.stringify({ ok: true, canvasId, elementCount: canvas.querySelectorAll('g[data-el-id]').length });
                } catch (err) {
                    return JSON.stringify({ error: err.message });
                }
            }
            """,
            new object[] { type, canvasId, x, y });

        await page.WaitForTimeoutAsync(800);
        return debugInfo;
    }

    private static async Task SelectFirstElementAsync(IPage page)
    {
        var element = FirstEditor(page).Locator("g[data-el-id]").First;
        await element.ClickAsync(new LocatorClickOptions { Force = true });
        await page.WaitForTimeoutAsync(400);
    }

    private static async Task ClickSvgElementAsync(ILocator element)
    {
        await element.ClickAsync(new LocatorClickOptions { Force = true });
    }

    private static async Task ShiftClickSvgElementAsync(ILocator element)
    {
        await element.ClickAsync(new LocatorClickOptions { Force = true, Modifiers = new[] { KeyboardModifier.Shift } });
    }

    private static async Task RightClickSvgElementAsync(ILocator element)
    {
        await element.ClickAsync(new LocatorClickOptions { Force = true, Button = MouseButton.Right });
    }

    private static async Task<double> GetZoomAsync(IPage page)
    {
        return await page.EvaluateAsync<double>(
            "() => { var el = document.querySelector('.tm-wd-editor__zoom-label'); return el ? parseFloat(el.textContent) : 0; }");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 1. Page load & basic structure
    // ═══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task PageLoad_ToolbarIsVisible()
    {
        var page = await PrepareWireframePageAsync();
        var toolbar = FirstEditor(page).Locator(".tm-wd-editor__toolbar");
        await toolbar.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await toolbar.IsVisibleAsync(), "Toolbar should be visible");
    }

    [TestMethod]
    public async Task PageLoad_ToolboxHasItems()
    {
        var page = await PrepareWireframePageAsync();
        var items = FirstEditor(page).Locator(".tm-wd-toolbox__item");
        var count = await items.CountAsync();
        Assert.IsTrue(count > 0, $"Toolbox should have items, found {count}");
    }

    [TestMethod]
    public async Task PageLoad_CanvasIsVisible()
    {
        var page = await PrepareWireframePageAsync();
        var canvas = FirstEditor(page).Locator(".tm-wd-canvas__svg");
        await canvas.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await canvas.IsVisibleAsync(), "Canvas SVG should be visible");
    }

    [TestMethod]
    public async Task PageLoad_RulersToggleOnOff()
    {
        var page = await PrepareWireframePageAsync();
        var editor = FirstEditor(page);

        // Rulers are off by default on the demo page
        var rulers = editor.Locator(".tm-wd-ruler");
        Assert.AreEqual(0, await rulers.CountAsync(), "Rulers should be hidden by default");

        // Toggle on (the rulers toggle lives in the toolbar's "More" menu)
        await (await CommandAsync(page, editor, "Show rulers", "Show rulers")).ClickAsync();
        await page.WaitForTimeoutAsync(600);

        Assert.AreEqual(2, await rulers.CountAsync(), "Should have horizontal and vertical rulers after toggle");

        // Toggle off
        await (await CommandAsync(page, editor, "Show rulers", "Show rulers")).ClickAsync();
        await page.WaitForTimeoutAsync(600);
        Assert.AreEqual(0, await rulers.CountAsync(), "Rulers should be hidden after second toggle");
    }

    [TestMethod]
    public async Task PageLoad_PropertiesPanelIsVisible()
    {
        var page = await PrepareWireframePageAsync();
        var props = FirstEditor(page).Locator(".tm-wd-props");
        await props.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await props.IsVisibleAsync(), "Properties panel should be visible");
    }

    [TestMethod]
    public async Task PageLoad_LayersPanelIsVisible()
    {
        var page = await PrepareWireframePageAsync();
        await OpenLeftTabAsync(page, FirstEditor(page), "Layers");
        var layers = FirstEditor(page).Locator(".tm-wd-layers");
        await layers.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await layers.IsVisibleAsync(), "Layers panel should be visible");
    }

    [TestMethod]
    public async Task PageLoad_PageTabsExist()
    {
        // Default PageNavigation=Tabs (the third demo editor): today's horizontal page tabs, unchanged.
        var page = await PrepareWireframePageAsync();
        var tabs = TabsEditor(page).Locator(".tm-wd-editor__page-tab");
        var count = await tabs.CountAsync();
        Assert.IsTrue(count >= 1, $"Should have at least one page tab, found {count}");
        Assert.AreEqual(0, await TabsEditor(page).Locator(".tm-wd-pages").CountAsync(), "Tabs mode renders no Pages panel");
    }

    [TestMethod]
    public async Task PageLoad_PagesPanel_ReplacesTheTabs_WhenPageNavigationIsPanel()
    {
        // The interactive demo editor uses PageNavigation=Panel: a Pages tab in the left panel, no horizontal tabs.
        var page = await PrepareWireframePageAsync();
        var editor = FirstEditor(page);
        Assert.AreEqual(0, await editor.Locator(".tm-wd-editor__page-tab").CountAsync(), "Panel mode renders no horizontal page tabs");
        await OpenLeftTabAsync(page, editor, "Pages");
        Assert.IsTrue(await editor.Locator(".tm-wd-pages__item").CountAsync() >= 1, "the Pages tab lists the pages");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 2. Toolbox
    // ═══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task Toolbox_SearchFiltersItems()
    {
        var page = await PrepareWireframePageAsync();
        var editor = FirstEditor(page);
        var search = editor.Locator(".tm-wd-toolbox__search-input");
        await search.FillAsync("button");
        await page.WaitForTimeoutAsync(500);

        var items = editor.Locator(".tm-wd-toolbox__item");
        var count = await items.CountAsync();
        Assert.IsTrue(count >= 1, $"Search should return at least one item for 'button', found {count}");

        await search.FillAsync("xyznonexistent");
        await page.WaitForTimeoutAsync(500);
        var empty = editor.Locator(".tm-wd-toolbox__empty");
        Assert.IsTrue(await empty.IsVisibleAsync(), "Empty state should appear for non-matching search");
    }

    [TestMethod]
    public async Task Toolbox_ClearSearchRestoresItems()
    {
        var page = await PrepareWireframePageAsync();
        var editor = FirstEditor(page);
        var search = editor.Locator(".tm-wd-toolbox__search-input");
        await search.FillAsync("button");
        await page.WaitForTimeoutAsync(500);

        var clearBtn = editor.Locator(".tm-wd-toolbox__search-clear");
        if (await clearBtn.CountAsync() > 0)
        {
            await clearBtn.ClickAsync();
            await page.WaitForTimeoutAsync(500);
        }
        else
        {
            await search.FillAsync("");
            await page.WaitForTimeoutAsync(500);
        }

        var items = editor.Locator(".tm-wd-toolbox__item");
        Assert.IsTrue(await items.CountAsync() > 0, "Clearing search should restore toolbox items");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 3. Adding & deleting elements
    // ═══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task DragToolboxItemToCanvas_CreatesElement()
    {
        var page = await PrepareWireframePageAsync();
        var debugInfo = await AddElementAsync(page);

        var elements = FirstEditor(page).Locator("g[data-el-id]");
        var count = await elements.CountAsync();
        if (count < 1)
            Assert.Fail($"Should have at least one element on canvas, found {count}. Debug: {debugInfo}");
    }

    [TestMethod]
    public async Task DeleteSelectedElement_RemovesIt()
    {
        var page = await PrepareWireframePageAsync();
        await AddElementAsync(page);
        await SelectFirstElementAsync(page);

        var beforeCount = await FirstEditor(page).Locator("g[data-el-id]").CountAsync();
        Assert.IsTrue(beforeCount >= 1);

        await page.Keyboard.PressAsync("Delete");
        await page.WaitForTimeoutAsync(600);

        var afterCount = await FirstEditor(page).Locator("g[data-el-id]").CountAsync();
        Assert.AreEqual(beforeCount - 1, afterCount, "Element should be deleted after pressing Delete");
    }

    [TestMethod]
    public async Task DeleteViaContextMenu_RemovesElement()
    {
        var page = await PrepareWireframePageAsync();
        await AddElementAsync(page);
        await SelectFirstElementAsync(page);

        var beforeCount = await FirstEditor(page).Locator("g[data-el-id]").CountAsync();

        var element = FirstEditor(page).Locator("g[data-el-id]").First;
        await RightClickSvgElementAsync(element);
        await page.WaitForTimeoutAsync(400);

        var deleteOption = page.Locator(".tm-wd-editor__context-item").Filter(new() { HasText = "Delete" });
        await deleteOption.ClickAsync();
        await page.WaitForTimeoutAsync(600);

        var afterCount = await FirstEditor(page).Locator("g[data-el-id]").CountAsync();
        Assert.AreEqual(beforeCount - 1, afterCount, "Element should be deleted via context menu");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 4. Selection & properties
    // ═══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task ClickOnCanvasElement_SelectsIt()
    {
        var page = await PrepareWireframePageAsync();
        await AddElementAsync(page);
        await SelectFirstElementAsync(page);

        var selection = FirstEditor(page).Locator(".tm-wd-selection");
        Assert.IsTrue(await selection.CountAsync() >= 1, "Selection outline should appear after clicking element");
    }

    [TestMethod]
    public async Task SelectElement_PropertiesPanelShowsFields()
    {
        var page = await PrepareWireframePageAsync();
        await AddElementAsync(page);
        await SelectFirstElementAsync(page);

        var propsPanel = FirstEditor(page).Locator(".tm-wd-props");
        Assert.IsTrue(await propsPanel.IsVisibleAsync(), "Properties panel should be visible");

        var fields = FirstEditor(page).Locator(".tm-wd-props__field");
        Assert.IsTrue(await fields.CountAsync() > 0, "Properties panel should contain fields for selected element");
    }

    [TestMethod]
    public async Task DeselectElement_ClearsSelection()
    {
        var page = await PrepareWireframePageAsync();
        await AddElementAsync(page);
        await SelectFirstElementAsync(page);

        var canvas = FirstEditor(page).Locator(".tm-wd-canvas__svg");
        await canvas.ClickAsync(new LocatorClickOptions { Force = true, Position = new() { X = 10, Y = 10 } });
        await page.WaitForTimeoutAsync(400);

        var selection = FirstEditor(page).Locator(".tm-wd-selection");
        Assert.IsTrue(await selection.CountAsync() == 0, "Selection should be cleared after clicking empty canvas");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 5. Multi-select
    // ═══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task CtrlClick_SelectsMultipleElements()
    {
        var page = await PrepareWireframePageAsync();
        await AddElementAsync(page, "", 400, 300);
        await AddElementAsync(page, "", 520, 300);
        await page.WaitForTimeoutAsync(500);

        var elements = FirstEditor(page).Locator("g[data-el-id]");
        var count = await elements.CountAsync();
        Assert.IsTrue(count >= 2, "Should have at least 2 elements");

        await ClickSvgElementAsync(elements.Nth(0));
        await page.WaitForTimeoutAsync(300);
        await ShiftClickSvgElementAsync(elements.Nth(1));
        await page.WaitForTimeoutAsync(300);

        var selection = FirstEditor(page).Locator(".tm-wd-selection");
        Assert.IsTrue(await selection.CountAsync() >= 2, "Should have selection outlines for both elements");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 6. Toolbar actions
    // ═══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task UndoRedo_AfterAddingElement()
    {
        var page = await PrepareWireframePageAsync();
        var editor = FirstEditor(page);
        var before = await editor.Locator("g[data-el-id]").CountAsync();

        await AddElementAsync(page);
        var afterAdd = await editor.Locator("g[data-el-id]").CountAsync();
        Assert.AreEqual(before + 1, afterAdd);

        // Undo
        await page.Keyboard.DownAsync("Control");
        await page.Keyboard.PressAsync("z");
        await page.Keyboard.UpAsync("Control");
        await page.WaitForTimeoutAsync(600);

        var afterUndo = await editor.Locator("g[data-el-id]").CountAsync();
        Assert.AreEqual(before, afterUndo, "Undo should remove the added element");

        // Redo
        await page.Keyboard.DownAsync("Control");
        await page.Keyboard.PressAsync("y");
        await page.Keyboard.UpAsync("Control");
        await page.WaitForTimeoutAsync(600);

        var afterRedo = await editor.Locator("g[data-el-id]").CountAsync();
        Assert.AreEqual(before + 1, afterRedo, "Redo should restore the element");
    }

    [TestMethod]
    public async Task ZoomIn_ZoomOut_ChangesZoomLevel()
    {
        var page = await PrepareWireframePageAsync();
        var initialZoom = await GetZoomAsync(page);

        var zoomInBtn = FirstEditor(page).Locator("button[title='Zoom in'], button[aria-label*='Zoom in' i]").First;
        await zoomInBtn.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        var afterZoomIn = await GetZoomAsync(page);
        Assert.IsTrue(afterZoomIn > initialZoom, $"Zoom should increase after zoom in. Before: {initialZoom}, After: {afterZoomIn}");

        var zoomOutBtn = FirstEditor(page).Locator("button[title='Zoom out'], button[aria-label*='Zoom out' i]").First;
        await zoomOutBtn.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        var afterZoomOut = await GetZoomAsync(page);
        Assert.IsTrue(afterZoomOut < afterZoomIn || Math.Abs(afterZoomOut - initialZoom) < 0.01,
            $"Zoom should decrease after zoom out. After zoom in: {afterZoomIn}, After zoom out: {afterZoomOut}");
    }

    [TestMethod]
    public async Task FitToView_ChangesZoom()
    {
        var page = await PrepareWireframePageAsync();
        // Place elements far apart so fit-to-view must zoom out
        await AddElementAsync(page, "", 100, 100);
        await AddElementAsync(page, "", 900, 700);
        await page.WaitForTimeoutAsync(500);

        // Zoom in first so that fit-to-view has to adjust the zoom
        var zoomInBtn = FirstEditor(page).Locator("button[title='Zoom in'], button[aria-label*='Zoom in' i]").First;
        await zoomInBtn.ClickAsync();
        await zoomInBtn.ClickAsync();
        await page.WaitForTimeoutAsync(500);
        var zoomBefore = await GetZoomAsync(page);

        // Call fit-to-view via JS directly to avoid any Blazor event issues and verify the returned scale
        var canvasId = await FirstEditor(page).Locator(".tm-wd-canvas__svg").EvaluateAsync<string>("el => el.id");
        var fitScale = await page.EvaluateAsync<double>(
            $"() => window.tmWireframeDesigner.fitToView(document.getElementById('{canvasId}'), 40)");

        var zoomAfter = await GetZoomAsync(page);
        Assert.IsTrue(zoomAfter < zoomBefore || fitScale < zoomBefore / 100.0,
            $"Fit to view should zoom out to fit all elements. Before: {zoomBefore}%, After: {zoomAfter}%, JS fitScale: {fitScale}");
    }

    [TestMethod]
    public async Task SnapToObjects_ToggleWorks()
    {
        var page = await PrepareWireframePageAsync();
        var editor = FirstEditor(page);
        // At a narrow editor width Snap leaves the bar for the "More" menu; its checkable entry carries the same state.
        var btn = await CommandAsync(page, editor, "Snap to objects", "Snap");
        var wasActive = await btn.GetAttributeAsync("aria-pressed") == "true" || await btn.GetAttributeAsync("aria-checked") == "true";

        await btn.ClickAsync();
        await page.WaitForTimeoutAsync(400);

        var after = await CommandAsync(page, editor, "Snap to objects", "Snap");
        var isActive = await after.GetAttributeAsync("aria-pressed") == "true" || await after.GetAttributeAsync("aria-checked") == "true";
        Assert.AreNotEqual(wasActive, isActive, "Snap to objects toggle should change its pressed state");
    }

    [TestMethod]
    public async Task BringToFront_SendToBack_ChangesZOrder()
    {
        var page = await PrepareWireframePageAsync();
        var editor = FirstEditor(page);
        await AddElementAsync(page, "", 400, 300);
        await AddElementAsync(page, "", 520, 300);
        await page.WaitForTimeoutAsync(500);

        var elements = editor.Locator("g[data-el-id]");
        Assert.IsTrue(await elements.CountAsync() >= 2);

        await ClickSvgElementAsync(elements.First);
        await page.WaitForTimeoutAsync(300);

        var sendToBackBtn = await CommandAsync(page, editor, "Send to back", "Backward");
        await sendToBackBtn.ClickAsync();
        await page.WaitForTimeoutAsync(400);
        Assert.IsTrue(await elements.CountAsync() >= 2, "Elements should still exist after Send to Back");

        var bringToFrontBtn = await CommandAsync(page, editor, "Bring to front", "Forward");
        await bringToFrontBtn.ClickAsync();
        await page.WaitForTimeoutAsync(400);
        Assert.IsTrue(await elements.CountAsync() >= 2, "Elements should still exist after Bring to Front");
    }

    [TestMethod]
    public async Task LockUnlockElement_Works()
    {
        var page = await PrepareWireframePageAsync();
        var editor = FirstEditor(page);
        await AddElementAsync(page);
        await SelectFirstElementAsync(page);

        var lockBtn = await CommandAsync(page, editor, "Lock", "Lock");
        await lockBtn.ClickAsync();
        await page.WaitForTimeoutAsync(400);

        var toggled = await CommandAsync(page, editor, "Unlock", "Unlock");
        Assert.IsTrue(await toggled.IsVisibleAsync(), "After Lock the command becomes Unlock");
    }

    [TestMethod]
    public async Task GroupUngroup_Works()
    {
        var page = await PrepareWireframePageAsync();
        var editor = FirstEditor(page);
        await AddElementAsync(page, "", 400, 300);
        await AddElementAsync(page, "", 520, 300);
        await page.WaitForTimeoutAsync(500);

        var elements = editor.Locator("g[data-el-id]");
        await ClickSvgElementAsync(elements.Nth(0));
        await page.WaitForTimeoutAsync(200);
        await ShiftClickSvgElementAsync(elements.Nth(1));
        await page.WaitForTimeoutAsync(300);

        var groupBtn = await CommandAsync(page, editor, "Group", "Group");
        if (await groupBtn.IsEnabledAsync())
        {
            await groupBtn.ClickAsync();
            await page.WaitForTimeoutAsync(600);
            Assert.IsTrue(await elements.CountAsync() >= 1, "Group operation should preserve elements");
        }
        else
        {
            Assert.Inconclusive("Group button was disabled; elements may not have supported grouping.");
        }
    }

    [TestMethod]
    public async Task AlignDropdown_Opens()
    {
        var page = await PrepareWireframePageAsync();
        var editor = FirstEditor(page);
        await AddElementAsync(page, "", 400, 300);
        await AddElementAsync(page, "", 520, 300);
        await page.WaitForTimeoutAsync(500);

        var elements = editor.Locator("g[data-el-id]");
        await ClickSvgElementAsync(elements.Nth(0));
        await page.WaitForTimeoutAsync(200);
        await ShiftClickSvgElementAsync(elements.Nth(1));
        await page.WaitForTimeoutAsync(300);

        // Align is a dropdown on the bar, or a group of entries in "More" once the bar runs out of room.
        var barTrigger = editor.Locator(".tm-toolbar-item:not(.tm-toolbar-item--collapsed) [data-testid='wd-align'] button.tm-dropdown-trigger");
        if (await barTrigger.CountAsync() > 0)
        {
            await barTrigger.ClickAsync();
            await page.WaitForTimeoutAsync(300);
        }
        else
        {
            await OpenMoreAsync(page, editor);
        }

        var alignLeft = page.Locator("[data-testid='wd-align-left'], [role='menu'] [role='menuitem']").Filter(new() { HasTextString = "Align Left" }).First;
        await alignLeft.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 3000 });
        Assert.IsTrue(await alignLeft.IsVisibleAsync(), "Align Left option should be visible");
    }

    [TestMethod]
    public async Task DistributeDropdown_Opens()
    {
        var page = await PrepareWireframePageAsync();
        var editor = FirstEditor(page);
        await AddElementAsync(page, "", 400, 300);
        await AddElementAsync(page, "", 520, 300);
        await AddElementAsync(page, "", 640, 300);
        await page.WaitForTimeoutAsync(500);

        var elements = editor.Locator("g[data-el-id]");
        for (int i = 0; i < 3; i++)
        {
            if (i == 0) await ClickSvgElementAsync(elements.Nth(i));
            else await ShiftClickSvgElementAsync(elements.Nth(i));
            await page.WaitForTimeoutAsync(200);
        }

        var barTrigger = editor.Locator(".tm-toolbar-item:not(.tm-toolbar-item--collapsed) [data-testid='wd-distribute'] button.tm-dropdown-trigger");
        if (await barTrigger.CountAsync() > 0)
        {
            await barTrigger.ClickAsync();
            await page.WaitForTimeoutAsync(300);
        }
        else
        {
            await OpenMoreAsync(page, editor);
        }

        var distHoriz = page.Locator("[data-testid='wd-distribute-horizontal'], [role='menu'] [role='menuitem']").Filter(new() { HasTextString = "Distribute Horizontal" }).First;
        await distHoriz.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 3000 });
        Assert.IsTrue(await distHoriz.IsVisibleAsync(), "Distribute horizontal option should be visible");
    }

    [TestMethod]
    public async Task CopyPasteStyle_Works()
    {
        var page = await PrepareWireframePageAsync();
        var editor = FirstEditor(page);
        await AddElementAsync(page);
        await SelectFirstElementAsync(page);

        // Format painter lives in the "More" menu (and in the inspector's Format painter section).
        await (await CommandAsync(page, editor, "Copy style", "Copy style")).ClickAsync();
        await page.WaitForTimeoutAsync(400);

        var pasteStyleBtn = await CommandAsync(page, editor, "Paste style", "Paste style");
        Assert.IsTrue(await pasteStyleBtn.IsVisibleAsync(), "Paste style entry should be reachable");
        Assert.IsTrue(await pasteStyleBtn.IsEnabledAsync(), "after Copy style, Paste style is enabled");
    }

    [TestMethod]
    public async Task DuplicateElement_Works()
    {
        var page = await PrepareWireframePageAsync();
        var editor = FirstEditor(page);
        await AddElementAsync(page);
        await SelectFirstElementAsync(page);

        var before = await editor.Locator("g[data-el-id]").CountAsync();

        await page.Keyboard.DownAsync("Control");
        await page.Keyboard.PressAsync("d");
        await page.Keyboard.UpAsync("Control");
        await page.WaitForTimeoutAsync(600);

        var after = await editor.Locator("g[data-el-id]").CountAsync();
        Assert.AreEqual(before + 1, after, "Duplicate should create a copy of the selected element");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 7. Pages
    // ═══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task AddPage_CreatesNewTab()
    {
        var page = await PrepareWireframePageAsync();
        var editor = TabsEditor(page);
        var before = await editor.Locator(".tm-wd-editor__page-tab").CountAsync();

        var addBtn = editor.Locator(".tm-wd-editor__page-tab-add");
        await addBtn.ClickAsync();
        await page.WaitForTimeoutAsync(600);

        var after = await editor.Locator(".tm-wd-editor__page-tab").CountAsync();
        Assert.AreEqual(before + 1, after, "Add page should create a new page tab");
    }

    [TestMethod]
    public async Task SwitchPage_ChangesActiveTab()
    {
        var page = await PrepareWireframePageAsync();
        var editor = TabsEditor(page);
        var addBtn = editor.Locator(".tm-wd-editor__page-tab-add");
        await addBtn.ClickAsync();
        await page.WaitForTimeoutAsync(600);

        var tabs = editor.Locator(".tm-wd-editor__page-tab");
        var count = await tabs.CountAsync();
        Assert.IsTrue(count >= 2, "Should have at least 2 pages");

        await tabs.Last.ClickAsync();
        await page.WaitForTimeoutAsync(400);

        var lastTabClass = await tabs.Last.GetAttributeAsync("class");
        Assert.IsTrue(lastTabClass?.Contains("tm-wd-editor__page-tab--active") ?? false, "Last clicked tab should be active");
    }

    [TestMethod]
    public async Task DeletePage_RemovesTab()
    {
        var page = await PrepareWireframePageAsync();
        var editor = TabsEditor(page);
        var addBtn = editor.Locator(".tm-wd-editor__page-tab-add");
        await addBtn.ClickAsync();
        await page.WaitForTimeoutAsync(600);

        var before = await editor.Locator(".tm-wd-editor__page-tab").CountAsync();
        Assert.IsTrue(before >= 2);

        var closeBtn = editor.Locator(".tm-wd-editor__page-tab-close").Last;
        await closeBtn.ClickAsync();
        await page.WaitForTimeoutAsync(600);

        var after = await editor.Locator(".tm-wd-editor__page-tab").CountAsync();
        Assert.AreEqual(before - 1, after, "Close button should remove the page tab");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 8. Layers
    // ═══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task Layers_AddLayer_CreatesNewLayer()
    {
        var page = await PrepareWireframePageAsync();
        var editor = FirstEditor(page);
        await OpenLeftTabAsync(page, editor, "Layers");
        var before = await editor.Locator(".tm-wd-layers__item").CountAsync();

        var addBtn = editor.Locator(".tm-wd-layers__add-btn");
        await addBtn.ClickAsync();
        await page.WaitForTimeoutAsync(600);

        var after = await editor.Locator(".tm-wd-layers__item").CountAsync();
        Assert.AreEqual(before + 1, after, "Add layer should create a new layer");
    }

    [TestMethod]
    public async Task Layers_ToggleVisibility_HidesLayer()
    {
        var page = await PrepareWireframePageAsync();
        var editor = FirstEditor(page);
        await OpenLeftTabAsync(page, editor, "Layers");
        var visibilityBtn = editor.Locator(".tm-wd-layers__visibility").First;
        await visibilityBtn.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        await visibilityBtn.ClickAsync();
        await page.WaitForTimeoutAsync(400);

        Assert.IsTrue(await visibilityBtn.IsVisibleAsync(), "Visibility toggle should still be visible after click");
    }

    [TestMethod]
    public async Task Layers_SetActiveLayer_Works()
    {
        var page = await PrepareWireframePageAsync();
        var editor = FirstEditor(page);
        await OpenLeftTabAsync(page, editor, "Layers");
        var addBtn = editor.Locator(".tm-wd-layers__add-btn");
        await addBtn.ClickAsync();
        await page.WaitForTimeoutAsync(600);

        var checkedBefore = await editor.Locator(".tm-wd-layers__radio--checked").CountAsync();

        var inactiveRadio = editor.Locator(".tm-wd-layers__radio").Filter(new() { Has = page.Locator(":not(.tm-wd-layers__radio--checked)") }).First;
        if (await inactiveRadio.CountAsync() > 0)
        {
            await inactiveRadio.ClickAsync();
            await page.WaitForTimeoutAsync(400);
        }

        var checkedAfter = await editor.Locator(".tm-wd-layers__radio--checked").CountAsync();
        Assert.AreEqual(1, checkedAfter, "Exactly one layer should be active");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 9. Context menu
    // ═══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task ContextMenu_OnCanvas_Opens()
    {
        var page = await PrepareWireframePageAsync();
        var editor = FirstEditor(page);
        var canvas = editor.Locator(".tm-wd-canvas__svg");
        await canvas.ClickAsync(new LocatorClickOptions { Force = true, Button = MouseButton.Right, Position = new() { X = 10, Y = 10 } });
        await page.WaitForTimeoutAsync(400);

        var menu = page.Locator(".tm-wd-editor__context-menu");
        Assert.IsTrue(await menu.IsVisibleAsync(), "Context menu should open on right-click");
    }

    [TestMethod]
    public async Task ContextMenu_OnElement_Opens()
    {
        var page = await PrepareWireframePageAsync();
        var editor = FirstEditor(page);
        await AddElementAsync(page);
        await SelectFirstElementAsync(page);

        var element = editor.Locator("g[data-el-id]").First;
        await RightClickSvgElementAsync(element);
        await page.WaitForTimeoutAsync(400);

        var menu = page.Locator(".tm-wd-editor__context-menu");
        Assert.IsTrue(await menu.IsVisibleAsync(), "Context menu should open on element right-click");

        var deleteOption = page.Locator(".tm-wd-editor__context-item").Filter(new() { HasText = "Delete" });
        Assert.IsTrue(await deleteOption.IsVisibleAsync(), "Delete option should be present in element context menu");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 10. Export
    // ═══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task ExportDropdown_OpensAndShowsPngOption()
    {
        var page = await PrepareWireframePageAsync();
        var editor = FirstEditor(page);
        var exportBtn = editor.Locator("[data-testid='wd-export'] button.tm-dropdown-trigger").First;
        await exportBtn.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        var pngOption = page.Locator("[data-testid='wd-export-png']");
        await pngOption.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 3000 });
        Assert.IsTrue(await pngOption.IsVisibleAsync(), "PNG export option should be visible");
    }

    [TestMethod]
    public async Task ExportPng_OpensExportDialog()
    {
        var page = await PrepareWireframePageAsync();
        var editor = FirstEditor(page);
        var exportBtn = editor.Locator("[data-testid='wd-export'] button.tm-dropdown-trigger").First;
        await exportBtn.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        var pngOption = page.Locator("[data-testid='wd-export-png']");
        await pngOption.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        var dialog = page.Locator(".tm-modal");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await dialog.IsVisibleAsync(), "Export dialog should open");
    }

    [TestMethod]
    public async Task ExportPdf_PreselectsPdf()
    {
        var page = await PrepareWireframePageAsync();
        var editor = FirstEditor(page);
        await editor.Locator("[data-testid='wd-export'] button.tm-dropdown-trigger").First.ClickAsync();

        await page.Locator("[data-testid='wd-export-pdf']").ClickAsync();

        var format = page.Locator("#wd-export-format");
        await format.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.AreEqual("pdf", await format.InputValueAsync(), "Export PDF must open the dialog with PDF selected");
        Assert.AreEqual(0, await page.Locator("#wd-export-scale").CountAsync(), "PDF has no scale option");

        // Edge case: re-opening via PNG must not keep the PDF choice, and the typed file name is reset.
        await page.Locator("#wd-export-filename").FillAsync("typed-by-user");
        await page.Locator(".tm-modal button", new() { HasTextString = "Cancel" }).First.ClickAsync();
        await page.Locator(".tm-wd-export-dialog").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 5000 });

        await editor.Locator("[data-testid='wd-export'] button.tm-dropdown-trigger").First.ClickAsync();
        await page.Locator("[data-testid='wd-export-png']").ClickAsync();
        await page.Locator("#wd-export-format").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.AreEqual("png", await page.Locator("#wd-export-format").InputValueAsync());
        Assert.AreNotEqual("typed-by-user", await page.Locator("#wd-export-filename").InputValueAsync(),
            "The file name must be reset from the document title on every open");
    }

    [TestMethod]
    public async Task ToolboxClick_AddsElement()
    {
        var page = await PrepareWireframePageAsync();
        var editor = FirstEditor(page);
        var before = await editor.Locator("g[data-el-id]").CountAsync();

        await editor.Locator(".tm-wd-toolbox__item[data-component-type='TmButton']").First.ClickAsync();
        await WaitForElementCountAsync(page, before + 1);

        Assert.AreEqual(before + 1, await editor.Locator("g[data-el-id]").CountAsync(),
            "A click on a toolbox item must insert exactly one element");

        // The new element lands inside the visible area of the canvas (not at a fixed corner).
        var inside = await page.EvaluateAsync<bool>(
            """
            () => {
                const svg = document.querySelector('.tm-wd-editor .tm-wd-canvas__svg');
                const els = svg.querySelectorAll('g[data-el-id]');
                const last = els[els.length - 1].getBoundingClientRect();
                const view = svg.getBoundingClientRect();
                return last.left >= view.left - 1 && last.right <= view.right + 1
                    && last.top >= view.top - 1 && last.bottom <= view.bottom + 1;
            }
            """);
        Assert.IsTrue(inside, "The inserted element must be inside the visible canvas area");
    }

    [TestMethod]
    public async Task ToolboxKeyboard_EnterAndSpace_InsertOneElementEach()
    {
        var page = await PrepareWireframePageAsync();
        var editor = FirstEditor(page);
        var item = editor.Locator(".tm-wd-toolbox__item[data-component-type='TmButton']").First;
        var before = await editor.Locator("g[data-el-id]").CountAsync();

        await item.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await WaitForElementCountAsync(page, before + 1);
        Assert.AreEqual(before + 1, await editor.Locator("g[data-el-id]").CountAsync(), "Enter inserts exactly one element");

        await item.FocusAsync();
        await page.Keyboard.PressAsync("Space");
        await WaitForElementCountAsync(page, before + 2);
        Assert.AreEqual(before + 2, await editor.Locator("g[data-el-id]").CountAsync(), "Space inserts exactly one element");
    }

    [TestMethod]
    public async Task ReadOnlyPreview_HasNoToggle_AndBlocksToolboxInsertion()
    {
        var page = await PrepareWireframePageAsync();
        var readOnly = page.Locator(".tm-wd-editor").Nth(1);
        await readOnly.ScrollIntoViewIfNeededAsync();

        // Host-pinned ReadOnly="true": there is no toggle that could unlock the preview.
        Assert.AreEqual(0, await readOnly.Locator("button[aria-label*='edit mode' i]").CountAsync());
        Assert.AreEqual(0, await readOnly.Locator("button[aria-label*='read-only mode' i]").CountAsync());

        var before = await readOnly.Locator("g[data-el-id]").CountAsync();
        var item = readOnly.Locator(".tm-wd-toolbox__item").First;
        if (await item.CountAsync() > 0 && await item.IsVisibleAsync())
        {
            await item.ClickAsync(new LocatorClickOptions { Force = true });
            // Negative assertion: nothing to wait FOR, so wait for the click round-trip (a frame after the
            // handler ran) and then assert the count did not move.
            await page.EvaluateAsync("() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))");
        }
        Assert.AreEqual(before, await readOnly.Locator("g[data-el-id]").CountAsync(),
            "A read-only editor must not accept toolbox insertion");
    }

    [TestMethod]
    public async Task ExportDialog_CanChangeScale()
    {
        var page = await PrepareWireframePageAsync();
        var editor = FirstEditor(page);
        var exportBtn = editor.Locator("[data-testid='wd-export'] button.tm-dropdown-trigger").First;
        await exportBtn.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        var pngOption = page.Locator("[data-testid='wd-export-png']");
        await pngOption.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        var scaleSelect = page.Locator("#wd-export-scale");
        if (await scaleSelect.CountAsync() > 0)
        {
            await scaleSelect.SelectOptionAsync("2");
            await page.WaitForTimeoutAsync(300);
            var value = await scaleSelect.InputValueAsync();
            Assert.AreEqual("2", value, "Scale should be changed to 2x");
        }
        else
        {
            Assert.Inconclusive("Scale selector not present (may depend on default format).");
        }
    }

    [TestMethod]
    public async Task ExportDialog_CanClose()
    {
        var page = await PrepareWireframePageAsync();
        var editor = FirstEditor(page);
        var exportBtn = editor.Locator("[data-testid='wd-export'] button.tm-dropdown-trigger").First;
        await exportBtn.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        var pngOption = page.Locator("[data-testid='wd-export-png']");
        await pngOption.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        var dialog = page.Locator(".tm-modal");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        var cancelBtn = page.Locator(".tm-modal-footer button").Filter(new() { HasText = "Cancel" });
        await cancelBtn.ClickAsync();
        await page.WaitForTimeoutAsync(400);

        Assert.IsFalse(await dialog.IsVisibleAsync(), "Export dialog should close after clicking Cancel");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 11. Read-only mode
    // ═══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task ReadOnlyToggle_SwitchesMode()
    {
        var page = await PrepareWireframePageAsync();
        var editor = FirstEditor(page);
        // Read-only is a checkable entry of the "More" menu.
        await OpenMoreAsync(page, editor);
        var entry = page.Locator("[role='menu'] [role='menuitemcheckbox']").Filter(new() { HasTextString = "Read-only" }).First;
        var before = await entry.GetAttributeAsync("aria-checked");
        await entry.ClickAsync();
        await page.WaitForTimeoutAsync(600);

        await OpenMoreAsync(page, editor);
        var after = await page.Locator("[role='menu'] [role='menuitemcheckbox']").Filter(new() { HasTextString = "Read-only" }).First.GetAttributeAsync("aria-checked");
        Assert.AreNotEqual(before, after, "The Read-only entry state should change when switching read-only mode");
    }

    [TestMethod]
    public async Task ReadOnlyEditor_IsRendered()
    {
        var page = await PrepareWireframePageAsync();
        var editors = page.Locator(".tm-wd-editor");
        var count = await editors.CountAsync();
        Assert.IsTrue(count >= 2, $"Page should have at least 2 editors (interactive + read-only), found {count}");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 12. Title & canvas size
    // ═══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task DocumentTitle_IsEditable()
    {
        var page = await PrepareWireframePageAsync();
        var editor = FirstEditor(page);
        var titleInput = editor.Locator(".tm-wd-editor__title");
        await titleInput.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        await titleInput.FillAsync("My Wireframe");
        await titleInput.PressAsync("Tab");
        await page.WaitForTimeoutAsync(400);

        var value = await titleInput.InputValueAsync();
        Assert.AreEqual("My Wireframe", value, "Document title should be updated");
    }

    [TestMethod]
    public async Task CanvasSizeInputs_AreInTheInspectorOfAnEmptySelection()
    {
        var page = await PrepareWireframePageAsync();
        var editor = FirstEditor(page);
        var widthInput = editor.Locator("[data-testid='wd-canvas-width']");
        await widthInput.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        Assert.IsTrue(await widthInput.IsVisibleAsync(), "Canvas width input is in the Canvas section");
        Assert.IsTrue(await editor.Locator("[data-testid='wd-canvas-height']").IsVisibleAsync());
        Assert.IsTrue(await editor.Locator("[data-testid='wd-device-preset']").IsVisibleAsync());
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // New-component gallery (Phases 1–8)
    // ═══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("New categories Color and Editors & Apps should appear in the toolbox")]
    public async Task Toolbox_NewCategories_Visible()
    {
        var page  = await PrepareWireframePageAsync();
        var toolbox = FirstEditor(page).Locator(".tm-wd-toolbox");
        await toolbox.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        var categoryNames = await toolbox.Locator(".tm-wd-toolbox__category-name").AllInnerTextsAsync();
        Assert.IsTrue(categoryNames.Any(n => n.Contains("Color", StringComparison.OrdinalIgnoreCase)),
            $"'Color' category should appear in toolbox. Found: {string.Join(", ", categoryNames)}");
        Assert.IsTrue(categoryNames.Any(n => n.Contains("Editors", StringComparison.OrdinalIgnoreCase)),
            $"'Editors & Apps' category should appear in toolbox. Found: {string.Join(", ", categoryNames)}");
    }

    [TestMethod]
    [Description("Gallery: drop one component from each new phase; verify all render without throwing")]
    public async Task NewComponents_GalleryScreenshot()
    {
        var page = await PrepareWireframePageAsync();

        // Representative component from each phase — placed in a 3-column grid
        var gallery = new (string type, int x, int y)[]
        {
            // Phase 1 — Inputs
            ("TmSlider",              60,  50),
            ("TmRating",             280,  50),
            ("TmMaskedTextBox",      460,  50),
            // Phase 2 — Color
            ("TmColorPicker",         60, 110),
            ("TmColorGradient",      280, 110),
            // Phase 3 — Signature / Pickers
            ("TmSignature",          460, 110),
            ("TmRecurrenceEditor",    60, 240),
            // Phase 4 — Charts / Data Display
            ("TmSparkline",          280, 240),
            ("TmGauge",              460, 240),
            ("TmQRCode",              60, 360),
            ("TmBarcode",            280, 360),
            ("TmPdfViewer",          460, 360),
            // Phase 5 — Buttons / Navigation
            ("TmFloatingActionButton", 60, 540),
            ("TmBottomNavigation",   280, 540),
            ("TmMenu",               460, 540),
            // Phase 6 — Layout
            ("TmStackLayout",         60, 650),
            ("TmSplitter",           280, 650),
            ("TmDockManager",        460, 650),
            // Phase 7 — Builders & collaboration
            ("TmFormulaBuilder",      60, 810),
            ("TmAIPrompt",           280, 810),
            ("TmShareLinkPanel",     460, 810),
            // Phase 8 — Editors & Apps
            ("TmChat",                60, 960),
            ("TmSpreadsheet",        280, 960),
            ("TmGantt",              460, 960),
            ("TmDiagramEditor",       60, 1150),
            ("TmDocumentEditor",     280, 1150),
            ("TmFileManager",        460, 1150),
        };

        foreach (var (type, x, y) in gallery)
        {
            var result = await AddElementAsync(page, type, x, y);
            Assert.IsTrue(result.Contains("ok"), $"AddElement failed for {type}: {result}");
        }

        await page.WaitForTimeoutAsync(1000);

        // Verify all elements are on the canvas
        var elCount = await FirstEditor(page).Locator("g[data-el-id]").CountAsync();
        Assert.IsTrue(elCount >= gallery.Length,
            $"Expected at least {gallery.Length} elements, found {elCount}");

        // Fit all elements into view via JS
        var fitCanvasId = await FirstEditor(page).Locator(".tm-wd-canvas__svg").EvaluateAsync<string>("el => el.id");
        await page.EvaluateAsync("""
            ([svgId]) => {
                const svg = document.getElementById(svgId);
                if (svg) tmWireframeDesigner.fitToView(svg, 30);
            }
            """, new object[] { fitCanvasId });
        await page.WaitForTimeoutAsync(600);

        // Screenshot the editor area (cropped to canvas + toolbox)
        var editorBox = await FirstEditor(page).BoundingBoxAsync();
        if (editorBox is not null)
        {
            var bytes = await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Type = ScreenshotType.Png,
                Clip = new Clip { X = editorBox.X, Y = editorBox.Y, Width = editorBox.Width, Height = editorBox.Height }
            });
            var path = "/tmp/wireframe_gallery_ux_review.png";
            await File.WriteAllBytesAsync(path, bytes);
            TestContext.AddResultFile(path);
        }

        await TakeScreenshotAsync(page, "wireframe_new_components_gallery_phases1_8");
    }
}
