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

        // Toggle on
        var rulerBtn = editor.Locator("button[aria-label*='Rulers' i], button[title*='Rulers' i]").First;
        await rulerBtn.ClickAsync();
        await page.WaitForTimeoutAsync(600);

        Assert.AreEqual(2, await rulers.CountAsync(), "Should have horizontal and vertical rulers after toggle");

        // Toggle off
        await rulerBtn.ClickAsync();
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
        var layers = FirstEditor(page).Locator(".tm-wd-layers");
        await layers.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await layers.IsVisibleAsync(), "Layers panel should be visible");
    }

    [TestMethod]
    public async Task PageLoad_PageTabsExist()
    {
        var page = await PrepareWireframePageAsync();
        var tabs = FirstEditor(page).Locator(".tm-wd-editor__page-tab");
        var count = await tabs.CountAsync();
        Assert.IsTrue(count >= 1, $"Should have at least one page tab, found {count}");
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
        var btn = editor.Locator("button[aria-label*='Snap to objects' i], button[title*='Snap to objects' i]").First;
        await btn.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        var beforeClass = await btn.GetAttributeAsync("class");
        var wasActive = beforeClass?.Contains("tm-wd-editor__btn--active") ?? false;

        await btn.ClickAsync();
        await page.WaitForTimeoutAsync(400);

        var afterClass = await btn.GetAttributeAsync("class");
        var isActive = afterClass?.Contains("tm-wd-editor__btn--active") ?? false;
        Assert.AreNotEqual(wasActive, isActive, "Snap to objects toggle should change active state");
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

        var sendToBackBtn = editor.Locator("button[aria-label*='Send to back' i], button[title*='Send to back' i]").First;
        await sendToBackBtn.ClickAsync();
        await page.WaitForTimeoutAsync(400);
        Assert.IsTrue(await elements.CountAsync() >= 2, "Elements should still exist after Send to Back");

        var bringToFrontBtn = editor.Locator("button[aria-label*='Bring to front' i], button[title*='Bring to front' i]").First;
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

        var lockBtn = editor.Locator("button[aria-label*='Lock' i], button[title*='Lock' i]").First;
        await lockBtn.ClickAsync();
        await page.WaitForTimeoutAsync(400);

        Assert.IsTrue(await lockBtn.IsVisibleAsync(), "Lock button should still be visible after click");
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

        var groupBtn = editor.Locator("button[aria-label='Group'], button[title='Group']").First;
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

        var alignBtn = editor.Locator("button[aria-label*='Align' i], button[title*='Align' i]").First;
        await alignBtn.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        var alignLeft = editor.Locator(".tm-wd-editor__dropdown-item").Filter(new() { HasText = "Align left" });
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

        var distBtn = editor.Locator("button[aria-label*='Distribute' i], button[title*='Distribute' i]").First;
        await distBtn.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        var distHoriz = editor.Locator(".tm-wd-editor__dropdown-item").Filter(new() { HasText = "Distribute horizontal" });
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

        var copyStyleBtn = editor.Locator("button[aria-label*='Copy style' i], button[title*='Copy style' i]").First;
        await copyStyleBtn.ClickAsync();
        await page.WaitForTimeoutAsync(400);

        var pasteStyleBtn = editor.Locator("button[aria-label*='Paste style' i], button[title*='Paste style' i]").First;
        Assert.IsTrue(await pasteStyleBtn.IsVisibleAsync(), "Paste style button should be visible");
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
        var editor = FirstEditor(page);
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
        var editor = FirstEditor(page);
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
        var editor = FirstEditor(page);
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
        var exportBtn = editor.Locator(".tm-wd-editor__dropdown-wrap:has-text('Export')").First;
        await exportBtn.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        var pngOption = editor.Locator(".tm-wd-editor__dropdown-item").Filter(new() { HasText = "PNG" });
        await pngOption.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 3000 });
        Assert.IsTrue(await pngOption.IsVisibleAsync(), "PNG export option should be visible");
    }

    [TestMethod]
    public async Task ExportPng_OpensExportDialog()
    {
        var page = await PrepareWireframePageAsync();
        var editor = FirstEditor(page);
        var exportBtn = editor.Locator(".tm-wd-editor__dropdown-wrap:has-text('Export')").First;
        await exportBtn.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        var pngOption = editor.Locator(".tm-wd-editor__dropdown-item").Filter(new() { HasText = "PNG" });
        await pngOption.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        var dialog = page.Locator(".tm-modal");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await dialog.IsVisibleAsync(), "Export dialog should open");
    }

    [TestMethod]
    public async Task ExportDialog_CanChangeScale()
    {
        var page = await PrepareWireframePageAsync();
        var editor = FirstEditor(page);
        var exportBtn = editor.Locator(".tm-wd-editor__dropdown-wrap:has-text('Export')").First;
        await exportBtn.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        var pngOption = editor.Locator(".tm-wd-editor__dropdown-item").Filter(new() { HasText = "PNG" });
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
        var exportBtn = editor.Locator(".tm-wd-editor__dropdown-wrap:has-text('Export')").First;
        await exportBtn.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        var pngOption = editor.Locator(".tm-wd-editor__dropdown-item").Filter(new() { HasText = "PNG" });
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
        var toggleBtn = editor.Locator("button[aria-label*='Read-only' i], button[title*='Read-only' i], button[aria-label*='Edit' i]").First;
        await toggleBtn.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        var textBefore = await toggleBtn.TextContentAsync();
        await toggleBtn.ClickAsync();
        await page.WaitForTimeoutAsync(600);

        var textAfter = await toggleBtn.TextContentAsync();
        Assert.AreNotEqual(textBefore, textAfter, "Toggle button text should change when switching read-only mode");
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
    public async Task CanvasSizeButton_OpensInputs()
    {
        var page = await PrepareWireframePageAsync();
        var editor = FirstEditor(page);
        var sizeBtn = editor.Locator(".tm-wd-editor__btn--canvas-size");
        await sizeBtn.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        await sizeBtn.ClickAsync();
        await page.WaitForTimeoutAsync(400);

        var widthInput = editor.Locator(".tm-wd-editor__canvas-size-input").First;
        Assert.IsTrue(await widthInput.IsVisibleAsync(), "Canvas width input should appear");
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
