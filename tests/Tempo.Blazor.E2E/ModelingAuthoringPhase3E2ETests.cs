using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan <c>eadbf967</c> phase 3 — semantic authoring in the Modeling editor, end to end: a BPMN
/// process built FROM AN EMPTY MODEL through the non-drag surfaces (pool, two lanes, start event, two
/// tasks, exclusive gateway, end event, the sequence of flows between them), the notation rules
/// FILTERING the relationship type picker for a cross-pool pair, the rules' own explanation for a
/// pair no relationship is valid for, and a cascade delete of a container with its contents undone in
/// one step. Runs against the WASM demo with the editing opt-in (<c>?editing=1</c>).
/// <para>
/// Every gesture here uses the NON-DRAG alternatives (WCAG 2.5.7) — Insert element, Connect to and the
/// header Delete — because they are what a keyboard/touch user actually has; the palette drop, the
/// edge drag and the canvas Delete reach the same commit path (covered by the bUnit authoring tests).
/// </para>
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class ModelingAuthoringPhase3E2ETests : WasmTestBase
{
    private const string ModelingEditorUrl = "/modeling-editor";

    /// <summary>Browser console lines of the current page (diagnostics in a failing assertion).</summary>
    private readonly List<string> _console = [];

    /// <summary>An EMPTY model with the BPMN notation: the "process from scratch" starting point.</summary>
    private const string EmptyBpmnUrl = "?editing=1&scenario=empty-preview&notation=bpmn2";

    [TestMethod]
    [Description("A complete BPMN process is authored from an EMPTY model through Insert element / Connect to")]
    public async Task Authoring_BuildsABpmnProcessFromAnEmptyModel()
    {
        var page = await OpenAuthoringPageAsync(EmptyBpmnUrl);
        try
        {
            // The empty model really starts empty — no nodes, no elements.
            await ExpectAttributeAsync(page, "[data-testid='modeling-editor']", "data-element-count", "0");
            await ExpectAttributeAsync(page, "[data-testid='modeling-editor']", "data-can-edit-structure", "true");

            // Pool → two lanes → start event → two tasks → exclusive gateway → end event.
            await InsertElementAsync(page, "Pool", "Order handling");
            await InsertElementAsync(page, "Lane", "Sales", container: "Order handling");
            await InsertElementAsync(page, "Lane", "Warehouse", container: "Order handling");
            await InsertElementAsync(page, "StartEvent", "Order received", container: "Sales");
            await InsertElementAsync(page, "UserTask", "Validate order", container: "Sales");
            await InsertElementAsync(page, "ExclusiveGateway", "Ready?", container: "Sales");
            await InsertElementAsync(page, "ServiceTask", "Ship order", container: "Warehouse");
            await InsertElementAsync(page, "EndEvent", "Order complete", container: "Warehouse");

            await ExpectAttributeAsync(page, "[data-testid='modeling-editor']", "data-element-count", "8");

            // Every inserted element is in the model tree (one step with the diagram, not a node).
            foreach (var name in new[] { "Order handling", "Sales", "Warehouse", "Order received", "Validate order", "Ready?", "Ship order", "Order complete" })
            {
                await ExpectTreeContainsAsync(page, name);
            }

            // The flow chain: start → validate → decision → ship → end (the picker's default type
            // for the pair is SequenceFlow inside one pool; cross-pool MessageFlow is handled below).
            await ConnectAsync(page, "Order received", "Validate order");
            await ExpectAttributeAsync(page, "[data-testid='modeling-editor']", "data-relationship-count", "1");
            await ConnectAsync(page, "Validate order", "Ready?");
            await ConnectAsync(page, "Ready?", "Ship order");
            await ConnectAsync(page, "Ship order", "Order complete");

            await ExpectAttributeAsync(page, "[data-testid='modeling-editor']", "data-relationship-count", "4");
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("A cross-pool pair never offers SequenceFlow (the rules filter the picker) and a pair with no valid type explains why")]
    public async Task Authoring_RulesFilterTheRelationshipTypesAndExplainARefusal()
    {
        var page = await OpenAuthoringPageAsync(EmptyBpmnUrl);
        try
        {
            await InsertElementAsync(page, "Pool", "Sales pool");
            await InsertElementAsync(page, "Pool", "Audit pool");
            await InsertElementAsync(page, "UserTask", "Approve order", container: "Sales pool");
            await InsertElementAsync(page, "UserTask", "Audit order", container: "Audit pool");

            // Cross-pool pair: SequenceFlow is not offered (it cannot cross a pool boundary), while
            // MessageFlow is — the picker lists ONLY the rules-allowed types.
            await SelectTreeRowAsync(page, "Approve order");
            await page.Locator("[data-testid='modeling-connect-to']").ClickAsync();
            await ExpectVisibleAsync(page, "[data-testid='modeling-relationship-dialog']");
            await SelectOptionByLabelPrefixAsync(page, "modeling-relationship-target", "Audit order");

            var options = await page.Locator("[data-testid='modeling-relationship-type'] option").AllAsync();
            var values = new List<string>();
            foreach (var option in options)
            {
                values.Add((await option.GetAttributeAsync("value")) ?? string.Empty);
            }

            CollectionAssert.DoesNotContain(values, "SequenceFlow",
                "BPMN SequenceFlow cannot cross a Pool boundary — the picker must not offer it");
            CollectionAssert.Contains(values, "MessageFlow", "MessageFlow is the valid cross-pool relationship");

            await page.Locator("[data-testid='modeling-relationship-type']")
                .SelectOptionAsync(new SelectOptionValue { Value = "MessageFlow" });
            await page.Locator("[data-testid='modeling-relationship-confirm']").ClickAsync();
            await ExpectAttributeAsync(page, "[data-testid='modeling-editor']", "data-relationship-count", "1");

            // A pair the rules know NOTHING valid for is refused with the rules' own explanation.
            await page.GotoAsync($"{BaseUrl}{ModelingEditorUrl}?editing=1&scenario=bpmn2-ai-task&notation=bpmn2",
                new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
            await WaitForLoadedAsync(page);

            await SelectTreeRowAsync(page, "AI classify order");
            await page.Locator("[data-testid='modeling-connect-to']").ClickAsync();
            await ExpectVisibleAsync(page, "[data-testid='modeling-relationship-dialog']");
            await ExpectVisibleAsync(page, "[data-testid='modeling-relationship-rejection']");
            Assert.IsFalse(await page.Locator("[data-testid='modeling-relationship-confirm']").IsEnabledAsync(),
                "the confirmation stays disabled while the pair has no allowed relationship");
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("Deleting a lane with its task cascades behind a confirmation and one undo restores everything")]
    public async Task Authoring_DeleteCascadeAsksForConfirmationAndUndoRestores()
    {
        var page = await OpenAuthoringPageAsync(EmptyBpmnUrl);
        try
        {
            await InsertElementAsync(page, "Pool", "Order handling");
            await InsertElementAsync(page, "Lane", "Sales", container: "Order handling");
            await InsertElementAsync(page, "UserTask", "Validate order", container: "Sales");
            await ExpectAttributeAsync(page, "[data-testid='modeling-editor']", "data-element-count", "3");

            await SelectTreeRowAsync(page, "Sales");
            await page.Locator("[data-testid='modeling-delete-selection']").ClickAsync();

            // The cascade is shown as counts BEFORE anything is removed.
            await ExpectVisibleAsync(page, "[data-testid='modeling-delete-confirm-dialog']");
            var title = await page.Locator("[data-testid='modeling-delete-confirm-dialog'] .tm-modal-title").TextContentAsync();
            StringAssert.Contains(title, "Sales",
                "the confirmation names WHAT it deletes (phase 3 review U4)");
            var summary = await page.Locator("[data-testid='modeling-delete-summary']").TextContentAsync();
            StringAssert.Contains(summary, "1 nested element",
                "the lane's task is the expanded cascade the summary counts, with a grammatical plural");
            StringAssert.Contains(summary, "0 relationships",
                "the scene carries no relationship — the summary still counts it grammatically");

            await page.Locator("[data-testid='modeling-delete-confirm']").ClickAsync();
            await ExpectAttributeAsync(page, "[data-testid='modeling-editor']", "data-element-count", "1");
            Assert.IsFalse((await TreeTextAsync(page)).Contains("Validate order", StringComparison.Ordinal),
                "the task inside the deleted lane is gone from the tree");

            // ONE undo step restores the whole cascade. The inspector header (and with it the
            // editor's undo button) only exists while something is selected — the deleted lane is
            // gone, so the surviving pool is selected first.
            await SelectTreeRowAsync(page, "Order handling");
            await page.Locator("[data-testid='modeling-inspector-undo']").ClickAsync();
            await ExpectAttributeAsync(page, "[data-testid='modeling-editor']", "data-element-count", "3");
            await ExpectTreeContainsAsync(page, "Validate order");
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("Screenshots: the authoring surfaces (empty, insert sheet, rejected connect, cascade delete, tablet insert) in light and dark (opt-in via TM_WRITE_BASELINES=1)")]
    public async Task Authoring_CapturesScreenshots()
    {
        if (!BaselineGeneratorTestBase.WritesAllowed)
        {
            Assert.Inconclusive($"Set {BaselineGeneratorTestBase.EnvironmentVariable}=1 to write the modeling authoring screenshots.");
        }

        // ── Desktop: the EMPTY model (the "process from scratch" starting point) ──────────────
        var empty = await OpenAuthoringPageAsync(EmptyBpmnUrl, 1440, 900, touch: false);
        try
        {
            await SettleAsync(empty);
            await SaveScreenshotAsync(empty, "authoring-empty-light");
            await SetDarkAsync(empty);
            await SettleAsync(empty);
            await SaveScreenshotAsync(empty, "authoring-empty-dark");

            // ── Desktop: the INSERT SHEET over a small authored scene ────────────────────────
            await SetLightAsync(empty);
            await InsertElementAsync(empty, "Pool", "Order handling");
            await InsertElementAsync(empty, "Lane", "Sales", container: "Order handling");
            await InsertElementAsync(empty, "UserTask", "Validate order", container: "Sales");
            await OpenInsertDialogAsync(empty);
            await SettleAsync(empty);
            await SaveScreenshotAsync(empty, "authoring-insert-dialog-light");
            await SetDarkAsync(empty);
            await SettleAsync(empty);
            await SaveScreenshotAsync(empty, "authoring-insert-dialog-dark");
            await empty.Locator("[data-testid='modeling-insert-cancel']").ClickAsync();
            await empty.Locator("[data-testid='modeling-insert-element-dialog']")
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 15000 });

            // ── Desktop: the CASCADE DELETE confirmation over the same scene ─────────────────
            await SetLightAsync(empty);
            await SelectTreeRowAsync(empty, "Sales");
            await empty.Locator("[data-testid='modeling-delete-selection']").ClickAsync();
            await ExpectVisibleAsync(empty, "[data-testid='modeling-delete-confirm-dialog']");
            await SettleAsync(empty);
            await SaveScreenshotAsync(empty, "authoring-delete-cascade-light");
            await SetDarkAsync(empty);
            await SettleAsync(empty);
            await SaveScreenshotAsync(empty, "authoring-delete-cascade-dark");
        }
        finally
        {
            await empty.Context.CloseAsync();
        }

        // ── Desktop: a REJECTED connect (the rules' own explanation in the sheet) ────────────
        // The shipped BPMN rules allow MessageFlow between two DIFFERENT pools, so a cross-pool
        // task pair is legal; the rejection card needs a pair no rule accepts — the AI task (a
        // type outside the BPMN flow-node vocabulary) and a task in its OWN pool.
        var rejected = await OpenAuthoringPageAsync("?editing=1&scenario=bpmn2-ai-task&notation=bpmn2", 1440, 900, touch: false);
        try
        {
            await SelectTreeRowAsync(rejected, "AI classify order");
            await rejected.Locator("[data-testid='modeling-connect-to']").ClickAsync();
            await ExpectVisibleAsync(rejected, "[data-testid='modeling-relationship-dialog']");
            await SelectOptionByLabelPrefixAsync(rejected, "modeling-relationship-target", "Validate order");
            await ExpectVisibleAsync(rejected, "[data-testid='modeling-relationship-rejection']");
            await SettleAsync(rejected);
            await SaveScreenshotAsync(rejected, "authoring-connect-rejected-light");
            await SetDarkAsync(rejected);
            await SettleAsync(rejected);
            await SaveScreenshotAsync(rejected, "authoring-connect-rejected-dark");
        }
        finally
        {
            await rejected.Context.CloseAsync();
        }

        // ── Desktop: the authored scene (the phase-3 baseline pair) ─────────────────────────
        var desktop = await OpenAuthoringPageAsync(EmptyBpmnUrl, 1440, 900, touch: false);
        try
        {
            await InsertElementAsync(desktop, "Pool", "Order handling");
            await InsertElementAsync(desktop, "Lane", "Sales", container: "Order handling");
            await InsertElementAsync(desktop, "UserTask", "Validate order", container: "Sales");
            await InsertElementAsync(desktop, "ExclusiveGateway", "Ready?", container: "Sales");
            await ConnectAsync(desktop, "Validate order", "Ready?");
            await SettleAsync(desktop);
            await SaveScreenshotAsync(desktop, "authoring-1440x900-light");

            await SetDarkAsync(desktop);
            await SettleAsync(desktop);
            await SaveScreenshotAsync(desktop, "authoring-1440x900-dark");
        }
        finally
        {
            await desktop.Context.CloseAsync();
        }

        // ── Tablet: the INSERT SHEET (the drawer/sheet presentation of the same action) ─────
        var tablet = await OpenAuthoringPageAsync("?editing=1&scenario=bpmn2-generated&notation=bpmn2", 1180, 820, touch: true);
        try
        {
            await tablet.Locator("[data-testid='modeling-panel-tab-tree']").TapAsync(new LocatorTapOptions { Timeout = 15000 });
            await tablet.Locator("[data-testid='modeling-model-tree-panel'] [role='treeitem']")
                .Filter(new LocatorFilterOptions { HasTextString = "Validate order" })
                .First.TapAsync(new LocatorTapOptions { Timeout = 15000 });
            await tablet.Locator("[data-testid='modeling-inspector-sheet']")
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
            await SettleAsync(tablet);
            await SaveScreenshotAsync(tablet, "authoring-1180x820-light");

            await SetDarkAsync(tablet);
            await SettleAsync(tablet);
            await SaveScreenshotAsync(tablet, "authoring-1180x820-dark");

            // Tablet INSERT SHEET: the same non-drag insert, presented by the tablet header.
            await SetLightAsync(tablet);
            await OpenInsertDialogAsync(tablet);
            await SettleAsync(tablet);
            await SaveScreenshotAsync(tablet, "authoring-1180x820-insert-dialog-light");
            await SetDarkAsync(tablet);
            await SettleAsync(tablet);
            await SaveScreenshotAsync(tablet, "authoring-1180x820-insert-dialog-dark");
        }
        finally
        {
            await tablet.Context.CloseAsync();
        }
    }

    /// <summary>Opens the non-drag insert sheet and waits for it.</summary>
    private static async Task OpenInsertDialogAsync(IPage page)
    {
        await page.Locator("[data-testid='modeling-insert-element']").ClickAsync();
        await page.Locator("[data-testid='modeling-insert-element-dialog']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<IPage> OpenAuthoringPageAsync(string query, int width = 1440, int height = 900, bool touch = false)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = touch,
            Locale = "en-US",
            IgnoreHTTPSErrors = true
        });
        RegisterContext(context);
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        page.Console += (_, message) => _console.Add($"[{message.Type}] {message.Text}");
        page.PageError += (_, error) => _console.Add($"[pageerror] {error}");
        await page.SetViewportSizeAsync(width, height);
        await page.GotoAsync($"{BaseUrl}{ModelingEditorUrl}{query}",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await WaitForLoadedAsync(page);
        return page;
    }

    private static async Task WaitForLoadedAsync(IPage page)
        => await page.Locator("[data-testid='modeling-editor'][data-state='loaded']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30000 });

    /// <summary>
    /// One "Insert element > type > container" pass: the non-drag alternative to a palette drop.
    /// The name field makes the container option addressable by LABEL for the following inserts.
    /// </summary>
    private static async Task InsertElementAsync(IPage page, string semanticType, string name, string? container = null)
    {
        await page.Locator("[data-testid='modeling-insert-element']").ClickAsync();
        var dialog = page.Locator("[data-testid='modeling-insert-element-dialog']");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });

        await page.Locator("[data-testid='modeling-insert-type']").SelectOptionAsync(new SelectOptionValue { Value = semanticType });

        var nameField = page.Locator("[data-testid='modeling-insert-name']");
        await nameField.FillAsync(name);
        await nameField.PressAsync("Tab");

        if (!string.IsNullOrWhiteSpace(container))
        {
            // The container options are labelled "name (type)" (phase 3 review U7); the helper
            // accepts the bare name and resolves the matching option.
            await SelectOptionByLabelPrefixAsync(page, "modeling-insert-container", container);
        }

        await page.Locator("[data-testid='modeling-insert-confirm']").ClickAsync();
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 15000 });
        await ExpectTreeContainsAsync(page, name);
    }

    /// <summary>One "Connect to > target > type" pass (the keyboard alternative to drawing an edge).</summary>
    private async Task ConnectAsync(IPage page, string sourceName, string targetName, string relationshipType = "SequenceFlow")
    {
        await SelectTreeRowAsync(page, sourceName);
        await page.Locator("[data-testid='modeling-connect-to']").ClickAsync();
        var dialog = page.Locator("[data-testid='modeling-relationship-dialog']");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await SelectOptionByLabelPrefixAsync(page, "modeling-relationship-target", targetName);

        var type = page.Locator("[data-testid='modeling-relationship-type']");
        await type.SelectOptionAsync(new SelectOptionValue { Value = relationshipType });
        Assert.AreEqual(relationshipType, await type.InputValueAsync(),
            $"the picker must offer {relationshipType} for {sourceName} → {targetName}");

        await page.Locator("[data-testid='modeling-relationship-confirm']").ClickAsync();
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 15000 });
    }

    /// <summary>
    /// Selects the option whose text is exactly <paramref name="label"/> or starts with
    /// "<c>{label} (</c>" — the dialog's options are labelled "name (type)" (phase 3 review U7), so
    /// a call site can keep naming the element only.
    /// </summary>
    private static async Task SelectOptionByLabelPrefixAsync(IPage page, string testId, string label)
    {
        var select = page.Locator($"[data-testid='{testId}']");
        var value = await select.EvaluateAsync<string?>(
            """
            (el, wanted) => {
                const options = Array.from(el.options || []);
                const exact = options.find(option => (option.textContent || '').trim() === wanted);
                const prefix = options.find(option => (option.textContent || '').trim().startsWith(wanted + ' ('));
                return (exact || prefix)?.value ?? null;
            }
            """,
            label);
        Assert.IsNotNull(value, $"the option '{label}' is not offered by {testId}");
        await select.SelectOptionAsync(new SelectOptionValue { Value = value! });
    }

    private async Task SelectTreeRowAsync(IPage page, string name)
    {
        var row = page.Locator("[data-testid='modeling-model-tree-panel'] [role='treeitem']")
            .Filter(new LocatorFilterOptions { HasTextString = name })
            .First;
        await row.ClickAsync(new LocatorClickOptions { Timeout = 15000 });
        await ExpectAttributeAsync(page, "[data-testid='modeling-editor']", "data-selected-element-id", string.Empty, exact: false);
    }

    private static async Task<string> TreeTextAsync(IPage page)
        => await page.Locator("[data-testid='modeling-model-tree-panel']").TextContentAsync() ?? string.Empty;

    private static async Task ExpectTreeContainsAsync(IPage page, string name)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if ((await TreeTextAsync(page)).Contains(name, StringComparison.Ordinal))
            {
                return;
            }

            await page.WaitForTimeoutAsync(100);
        }

        Assert.Fail($"the model tree never rendered '{name}'. Tree text: {await TreeTextAsync(page)}");
    }

    private static async Task ExpectVisibleAsync(IPage page, string selector, int timeout = 10000)
        => await page.Locator(selector).WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = timeout });

    private async Task ExpectAttributeAsync(IPage page, string selector, string attribute, string expected, bool exact = true)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        string? actual = null;
        while (DateTime.UtcNow < deadline)
        {
            actual = await page.Locator(selector).First.GetAttributeAsync(attribute);
            if (exact ? string.Equals(actual, expected, StringComparison.Ordinal) : !string.IsNullOrEmpty(actual))
            {
                return;
            }

            await page.WaitForTimeoutAsync(100);
        }

        var diagnostics = string.Join(" | ", _console.TakeLast(12));
        var error = await page.Locator("[data-testid='modeling-structure-error']").CountAsync() > 0
            ? await page.Locator("[data-testid='modeling-structure-error']").TextContentAsync()
            : "(no structure error alert)";
        Assert.Fail($"{selector} [{attribute}] was '{actual}', expected '{expected}'. Alert: {error}. Console: {diagnostics}");
    }

    /// <summary>Switches the demo back to the light theme through the same hidden toggle.</summary>
    private static Task SetLightAsync(IPage page) => EnsureThemeAsync(page, "light");

    /// <summary>
    /// The /modeling-editor demo page HIDES the sidebar (which owns the theme toggle) from 1024px up,
    /// so the click is dispatched on the hidden toggle — the established pattern of the modeling
    /// baseline generators.
    /// </summary>
    private static Task SetDarkAsync(IPage page) => EnsureThemeAsync(page, "dark");

    /// <summary>Idempotently drives the demo's theme to <paramref name="theme"/>.</summary>
    private static async Task EnsureThemeAsync(IPage page, string theme)
    {
        var shell = page.Locator(".demo-shell");
        if (string.Equals(await shell.GetAttributeAsync("data-theme"), theme, StringComparison.Ordinal))
        {
            return;
        }

        var toggle = page.Locator("aside [data-testid='theme-toggle']");
        if (await toggle.CountAsync() > 0)
        {
            await toggle.First.DispatchEventAsync("click");
        }

        await Assertions.Expect(shell)
            .ToHaveAttributeAsync("data-theme", theme, new LocatorAssertionsToHaveAttributeOptions { Timeout = 15000 });
    }

    private static async Task SettleAsync(IPage page)
    {
        await page.EvaluateAsync(
            """
            async () => {
                await document.fonts.ready;
                const animations = document.getAnimations();
                await Promise.all(animations.map(a => a.finished.catch(() => { })));
            }
            """);
        await page.WaitForTimeoutAsync(250);
    }

    private static async Task SaveScreenshotAsync(IPage page, string fileName)
    {
        Directory.CreateDirectory(ScreenshotDirectory);
        var path = Path.Combine(ScreenshotDirectory, fileName + ".png");
        await page.Mouse.MoveAsync(0, 0);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = false, Timeout = 30000 });
        Assert.IsTrue(new FileInfo(path).Length > 10_000, $"{fileName} should contain the rendered authoring screen.");
    }

    private static string ScreenshotDirectory
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
            {
                directory = directory.Parent!;
            }

            return Path.Combine(directory!.FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "modeling");
        }
    }
}
