using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 14: the EDITABLE inspector end to end — rename → tree + preview + inspector update,
/// Ctrl+Z / Ctrl+Shift+Z undo-redo inside the inspector, the Manual save (aggregated change set),
/// the conflict alert with the confirmed reload, the discard confirmation and the mobile
/// edit → save-from-action-bar flow. Runs against the WASM demo with the editing opt-in
/// (<c>?editing=1</c>, decision MODELING-INSPECTOR-EDITING).
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class ModelingEditingF14E2ETests : WasmTestBase
{
    private const string ModelingEditorUrl = "/modeling-editor";

    [TestMethod]
    [Description("Editing the name updates the tree, the inspector input and the preview node label in one step")]
    public async Task Editing_RenameUpdatesTreeInspectorAndPreview()
    {
        var page = await OpenLoadedEditingPageAsync();
        try
        {
            await page.Locator("[data-testid='modeling-tree-node-bpmn-validate-order']").ClickAsync();

            var name = page.Locator("#modeling-inspector-name");
            await name.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
            await name.FillAsync("Renamed order step");
            await name.PressAsync("Enter");

            // Tree follows the committed model…
            await ExpectTextContainsAsync(page, "[data-testid='modeling-tree-node-bpmn-validate-order']", "Renamed order step");
            // …the inspector keeps the committed value in its field…
            Assert.AreEqual("Renamed order step", await page.Locator("#modeling-inspector-name").InputValueAsync());
            // …and the preview node label is repainted from the synchronized document.
            await ExpectTextContainsAsync(page, "[data-model-element-id='bpmn-validate-order'] .tm-diagram-node__text", "Renamed order step");

            // The pending state is visible through the save affordance.
            await ExpectVisibleAsync(page, "[data-testid='modeling-inspector-save']");
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("Ctrl+Z undoes the rename (tree + preview revert in one step); Ctrl+Shift+Z reapplies it")]
    public async Task Editing_UndoRedoKeyboardInsideInspector()
    {
        var page = await OpenLoadedEditingPageAsync();
        try
        {
            await page.Locator("[data-testid='modeling-tree-node-bpmn-validate-order']").ClickAsync();

            var name = page.Locator("#modeling-inspector-name");
            await name.FillAsync("Renamed order step");
            await name.PressAsync("Enter");
            await ExpectTextContainsAsync(page, "[data-testid='modeling-tree-node-bpmn-validate-order']", "Renamed order step");

            // Ctrl+Z dispatched while the focus is INSIDE the inspector — the editor's own stack
            // consumes it and the diagram canvas keyboard scope is never involved. The focus sits
            // on the inspector header's undo button (a control inside the section, not a text
            // field): a focused text input owns Ctrl+Z natively (the browser's text undo/redo is
            // the default action and always fires — the editor stack only takes over when no
            // editing draft differs from its committed value).
            await page.Locator("[data-testid='modeling-inspector-undo']").FocusAsync();
            await page.Keyboard.PressAsync("Control+z");

            await ExpectTextContainsAsync(page, "[data-testid='modeling-tree-node-bpmn-validate-order']", "Validate order");
            await ExpectTextContainsAsync(page, "[data-model-element-id='bpmn-validate-order'] .tm-diagram-node__text", "Validate order");

            // The undo disables its button and drops focus to the body — the redo press goes
            // through the (now enabled) redo button, which stays inside the inspector.
            await page.Locator("[data-testid='modeling-inspector-redo']").FocusAsync();
            await page.Keyboard.PressAsync("Control+Shift+z");
            await ExpectTextContainsAsync(page, "[data-testid='modeling-tree-node-bpmn-validate-order']", "Renamed order step");
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("The header undo/redo buttons drive the same stack and announce through the live region")]
    public async Task Editing_HeaderUndoRedoButtons()
    {
        var page = await OpenLoadedEditingPageAsync();
        try
        {
            await page.Locator("[data-testid='modeling-tree-node-bpmn-validate-order']").ClickAsync();
            var name = page.Locator("#modeling-inspector-name");
            await name.FillAsync("Renamed order step");
            await name.PressAsync("Enter");

            var undo = page.Locator("[data-testid='modeling-inspector-undo']");
            Assert.IsFalse(await undo.IsDisabledAsync(), "undo enables after the first commit");
            await undo.ClickAsync();
            await ExpectTextContainsAsync(page, "[data-testid='modeling-tree-node-bpmn-validate-order']", "Validate order");

            var redo = page.Locator("[data-testid='modeling-inspector-redo']");
            Assert.IsFalse(await redo.IsDisabledAsync(), "redo enables after the undo");
            await redo.ClickAsync();
            await ExpectTextContainsAsync(page, "[data-testid='modeling-tree-node-bpmn-validate-order']", "Renamed order step");
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("Manual save sends the aggregated change set (name + property), clears pending and dumps the change set on the demo page")]
    public async Task Editing_ManualSave_AggregatesAndClears()
    {
        var page = await OpenLoadedEditingPageAsync();
        try
        {
            await page.Locator("[data-testid='modeling-tree-node-bpmn-validate-order']").ClickAsync();
            var name = page.Locator("#modeling-inspector-name");
            await name.FillAsync("Renamed order step");
            await name.PressAsync("Enter");

            var owner = page.Locator("[data-testid='modeling-inspector-property-owner'] input");
            await owner.FillAsync("Support");
            await owner.BlurAsync();

            await page.Locator("[data-testid='modeling-inspector-save']").ClickAsync();

            // The demo page dumps the last change set (OnModelChanged → SaveModelChangesAsync path).
            await ExpectVisibleAsync(page, "[data-testid='modeling-demo-last-changeset']");
            var dump = await page.Locator("[data-testid='modeling-demo-last-changeset']").TextContentAsync() ?? string.Empty;
            // The dump shows the LAST commit's set (the owner property); the aggregated
            // name+property save is pinned by the bUnit suite.
            StringAssert.Contains(dump, "owner");

            // Pending cleared: the save affordance disappears again.
            await page.Locator("[data-testid='modeling-inspector-save']")
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 5000 });

            // The persisted model survives a reload (the demo writer stores it in memory).
            await page.Locator("[data-testid='modeling-tab-source']").ClickAsync();
            await page.Locator("[data-testid='modeling-source-load-button']").ClickAsync();
            await page.Locator("[data-testid='modeling-tree-node-bpmn-validate-order']")
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
            await ExpectTextContainsAsync(page, "[data-testid='modeling-tree-node-bpmn-validate-order']", "Renamed order step");
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("A conflict save shows the alert with the reload action; confirming the reload discards the pending edits")]
    public async Task Editing_ConflictShowsAlertAndConfirmedReloadDiscards()
    {
        var page = await OpenLoadedEditingPageAsync("?scenario=save-conflict");
        try
        {
            await page.Locator("[data-testid='modeling-tree-node-bpmn-validate-order']").ClickAsync();
            var name = page.Locator("#modeling-inspector-name");
            await name.FillAsync("Renamed order step");
            await name.PressAsync("Enter");

            await page.Locator("[data-testid='modeling-inspector-save']").ClickAsync();
            await ExpectVisibleAsync(page, "[data-testid='modeling-inspector-conflict']");
            await ExpectVisibleAsync(page, "[data-testid='modeling-conflict-reload']");

            // The reload asks for confirmation first (the pending changes are discarded).
            await page.Locator("[data-testid='modeling-conflict-reload']").ClickAsync();
            await ExpectVisibleAsync(page, "[data-testid='modeling-discard-changes-modal']");
            await page.Locator("[data-testid='modeling-discard-cancel']").ClickAsync();
            await page.Locator("[data-testid='modeling-tab-properties']").ClickAsync();
            await ExpectVisibleAsync(page, "[data-testid='modeling-inspector-save']");

            await page.Locator("[data-testid='modeling-conflict-reload']").ClickAsync();
            await page.Locator("[data-testid='modeling-discard-confirm']").ClickAsync();

            await page.Locator("[data-testid='modeling-inspector-save']")
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 10000 });
            await ExpectTextContainsAsync(page, "[data-testid='modeling-tree-node-bpmn-validate-order']", "Validate order");
            await page.Locator("[data-testid='modeling-inspector-conflict']")
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 10000 });
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("A reload with pending edits asks for confirmation; cancel keeps the edits, confirm discards them")]
    public async Task Editing_ReloadWithPendingEditsAsksForConfirmation()
    {
        var page = await OpenLoadedEditingPageAsync();
        try
        {
            await page.Locator("[data-testid='modeling-tree-node-bpmn-validate-order']").ClickAsync();
            var name = page.Locator("#modeling-inspector-name");
            await name.FillAsync("Renamed order step");
            await name.PressAsync("Enter");

            // Reload through the Source tab Load button.
            await page.Locator("[data-testid='modeling-tab-source']").ClickAsync();
            await page.Locator("[data-testid='modeling-source-load-button']").ClickAsync();
            await ExpectVisibleAsync(page, "[data-testid='modeling-discard-changes-modal']");
            await page.Locator("[data-testid='modeling-discard-cancel']").ClickAsync();
            await page.Locator("[data-testid='modeling-tab-properties']").ClickAsync();
            await ExpectVisibleAsync(page, "[data-testid='modeling-inspector-save']");

            // Second attempt: back to the Source tab (the tabs render one body at a time).
            await page.Locator("[data-testid='modeling-tab-source']").ClickAsync();
            await page.Locator("[data-testid='modeling-source-load-button']").ClickAsync();
            await page.Locator("[data-testid='modeling-discard-confirm']").ClickAsync();
            await page.Locator("[data-testid='modeling-tab-properties']").ClickAsync();
            await page.Locator("[data-testid='modeling-inspector-save']")
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 10000 });
            await ExpectTextContainsAsync(page, "[data-testid='modeling-tree-node-bpmn-validate-order']", "Validate order");
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("390x844 touch: the Inspector tab edits and the sticky action-bar Save persists")]
    public async Task Editing_MobileEditAndSaveFromActionBar()
    {
        var page = await OpenLoadedEditingPageAsync("?editing=1", 390, 844, touch: true);
        try
        {
            // G2: a tree tap is browsing — the edit flow goes Tree tab → tap → Inspector tab.
            await page.Locator("[data-testid='modeling-panel-tab-tree']").TapAsync(new LocatorTapOptions { Timeout = 10_000 });
            await page.Locator("[data-testid='modeling-tree-node-bpmn-validate-order']").TapAsync(new LocatorTapOptions { Timeout = 10_000 });
            await page.Locator("[data-testid='modeling-panel-tab-inspector']").TapAsync(new LocatorTapOptions { Timeout = 10_000 });

            var name = page.Locator("#modeling-inspector-name");
            await name.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
            await name.FillAsync("Renamed order step");
            await name.PressAsync("Enter");

            // The sticky Save entry leads the action bar while edits are pending.
            var save = page.Locator("[data-action-id='save']");
            await save.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
            await save.TapAsync(new LocatorTapOptions { Timeout = 10_000 });

            await save.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 10_000 });

            // The tree keeps the persisted name after the save.
            await page.Locator("[data-testid='modeling-panel-tab-tree']").TapAsync(new LocatorTapOptions { Timeout = 10_000 });
            await ExpectTextContainsAsync(page, "[data-testid='modeling-tree-node-bpmn-validate-order']", "Renamed order step");
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("Two consecutive saves both succeed: the adopted model carries the bumped source version, so the second edit bases on it (review fix, phase 14)")]
    public async Task Editing_TwoConsecutiveSavesSucceed()
    {
        var page = await OpenLoadedEditingPageAsync();
        try
        {
            await page.Locator("[data-testid='modeling-tree-node-bpmn-validate-order']").ClickAsync();

            var name = page.Locator("#modeling-inspector-name");
            await name.FillAsync("First renamed order");
            await name.PressAsync("Enter");
            await page.Locator("[data-testid='modeling-inspector-save']").ClickAsync();
            await page.Locator("[data-testid='modeling-inspector-save']")
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 10_000 });

            // The selection survived the adopted model (remapped by Id) — the same input keeps editing.
            await name.FillAsync("Second renamed order");
            await name.PressAsync("Enter");
            await page.Locator("[data-testid='modeling-inspector-save']").ClickAsync();

            // A version mismatch would surface the conflict alert; the second save clears the affordance.
            await page.Locator("[data-testid='modeling-inspector-save']")
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 10_000 });
            await ExpectTextContainsAsync(page, "[data-testid='modeling-tree-node-bpmn-validate-order']", "Second renamed order");
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<IPage> OpenLoadedEditingPageAsync(string query = "?editing=1", int? width = null, int? height = null, bool touch = false)
    {
        var context = touch
            ? await Browser.NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = width!.Value, Height = height!.Value },
                HasTouch = true,
                Locale = "en-US",
                IgnoreHTTPSErrors = true
            })
            : await CreateContextAsync();
        RegisterContext(context);
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        if (touch)
        {
            await page.SetViewportSizeAsync(width!.Value, height!.Value);
        }

        await page.GotoAsync($"{BaseUrl}{ModelingEditorUrl}{query}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await page.Locator("[data-testid='modeling-editor'][data-state='loaded']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.tm-diagram-node').length > 0",
            null, new PageWaitForFunctionOptions { Timeout = 20_000 });
        return page;
    }

    private static async Task ExpectVisibleAsync(IPage page, string selector, int timeout = 5000)
        => await page.Locator(selector).WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = timeout });

    private static async Task ExpectTextContainsAsync(IPage page, string selector, string expectedText)
    {
        var text = await page.Locator(selector).TextContentAsync() ?? string.Empty;
        StringAssert.Contains(text, expectedText);
    }
}
