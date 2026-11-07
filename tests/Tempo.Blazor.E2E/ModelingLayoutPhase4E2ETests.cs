using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan <c>eadbf967</c> phase 4 — LAYOUT, hierarchy and container sizes in the Modeling editor, end
/// to end: the undoable "Arrange" action writing <c>ViewChanges</c>, a task DRAGGED into the other
/// lane in the open-editor dialog (semantic <c>ParentId</c> + layout parent in one step), and a full
/// editor REMOUNT proving the demo writer persisted the authored layout. Runs against the WASM demo
/// with the editing opt-in (<c>?editing=1</c>).
/// <para>
/// The layout gestures are the drag paths the bUnit layout tests cannot exercise (they need a real
/// pointer and the embedded diagram canvas); the keyboard/tap alternative for each is the "Arrange"
/// action plus the tree/inspector, covered here and in the bUnit suite.
/// </para>
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class ModelingLayoutPhase4E2ETests : WasmTestBase
{
    private const string ModelingEditorUrl = "/modeling-editor";

    /// <summary>Browser console lines of the current page (diagnostics in a failing assertion).</summary>
    private readonly List<string> _console = [];

    /// <summary>The default demo model with editing on (its elements carry a SourceId, so the
    /// generated node ids are the source ids).</summary>
    private const string EditingUrl = "?editing=1";

    /// <summary>Phase 4 demo scene: one pool, two lanes ("Sales" / "Warehouse"), one task each.</summary>
    private const string LanesUrl = "?editing=1&scenario=bpmn2-lanes&notation=bpmn2";

    /// <summary>Phase 4 demo scene: the provider serves the DEMO WRITER's persisted model.</summary>
    private const string PersistUrl = "?editing=1&scenario=layout-persist";

    [TestMethod]
    [Description("Arrange is one undoable layout transaction that reaches the change set, and undo restores every position")]
    public async Task Layout_ArrangeIsUndoableAndReachesTheChangeSet()
    {
        var page = await OpenEditorAsync(EditingUrl);
        try
        {
            var before = await ReadNodeTransformsAsync(page, "demo/bpmn/validate-order", "demo/arch/customer-portal");

            await page.Locator("[data-testid='modeling-arrange']").ClickAsync();
            await ExpectVisibleAsync(page, "[data-testid='modeling-arrange-dialog']");
            await page.Locator("[data-testid='modeling-arrange-direction']")
                .SelectOptionAsync(new SelectOptionValue { Value = "LR" });
            await page.Locator("[data-testid='modeling-arrange-confirm']").ClickAsync();
            await page.Locator("[data-testid='modeling-arrange-dialog']")
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 15000 });

            // The arranged geometry rendered, and the demo page saw the layout in the change set.
            await ExpectNodeMovedAsync(page, "demo/bpmn/validate-order", before["demo/bpmn/validate-order"]);
            await ExpectChangeSetContainsAsync(page, "\"ViewChanges\"");
            var pending = await page.Locator("[data-testid='modeling-demo-pending']").TextContentAsync();
            Assert.AreNotEqual(0, ExtractCount(pending), "the arranged layout is an unsaved change");

            var arranged = await ReadNodeTransformsAsync(page, "demo/bpmn/validate-order", "demo/arch/customer-portal");

            // ONE undo restores both positions (one transaction for the whole arrangement).
            await SelectTreeRowAsync(page, "Validate order");
            await page.Locator("[data-testid='modeling-inspector-undo']").ClickAsync();

            await ExpectTransformAsync(page, "demo/bpmn/validate-order", before["demo/bpmn/validate-order"]);
            await ExpectTransformAsync(page, "demo/arch/customer-portal", before["demo/arch/customer-portal"]);
            Assert.AreNotEqual(arranged["demo/bpmn/validate-order"], before["demo/bpmn/validate-order"],
                "the arrange really changed the geometry");
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("A task dragged into the other lane changes the semantic ParentId and the layout parent in one step; the lanes render as named BANDS")]
    public async Task Layout_MovingATaskBetweenLanesChangesTheParent()
    {
        var page = await OpenEditorAsync(LanesUrl);
        try
        {
            await page.Locator("[data-testid='modeling-open-in-editor-button']").ClickAsync();
            var dialog = page.Locator("[data-testid='modeling-open-editor-modal'], .tm-modeling-editor__open-editor-modal");
            await dialog.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20000 });
            await ExpectNodeAsync(page, "task-validate");
            await ExpectNodeAsync(page, "lane-warehouse");

            // BPMN visual convention (phase 4): a pool/lane is a band whose NAME sits in the header.
            var headers = await page.Locator(".tm-diagram-node__swimlane-header").AllTextContentsAsync();
            CollectionAssert.Contains(headers.Select(text => text.Trim()).ToList(), "Sales");
            CollectionAssert.Contains(headers.Select(text => text.Trim()).ToList(), "Warehouse");



            // Drag the Sales task into the Warehouse lane (the real pointer path). Both points are
            // resolved in the DIALOG's own editor (the preview behind the modal renders the same
            // node ids) and the press point is HIT-TESTED against a port / connect handle first —
            // a press on a port would DRAW an edge instead of moving the node.
            var task = await BodyPointOfSceneNodeAsync(page, "task-validate");
            var drop = await LaneDropPointAsync(page, "lane-warehouse");
            Assert.IsNotNull(task, "the Sales task must expose a pressable body point");
            Assert.IsNotNull(drop, "the Warehouse lane must expose a drop point");

            var source = task!;
            var target = drop!;
            await page.Mouse.MoveAsync((float)source[0], (float)source[1]);
            await page.Mouse.DownAsync();
            await page.Mouse.MoveAsync((float)(source[0] + 12), (float)(source[1] + 12), new MouseMoveOptions { Steps = 4 });
            await page.Mouse.MoveAsync((float)target[0], (float)target[1], new MouseMoveOptions { Steps = 12 });
            await page.Mouse.UpAsync();

            // §9.3: ONE transaction carries the semantic re-parent AND the layout parent.
            await ExpectChangeSetContainsAsync(page, "\"ParentId\": \"lane-warehouse\"");
            await ExpectChangeSetContainsAsync(page, "\"ParentNodeId\": \"lane-warehouse\"");

            // Review U7: the ONE structure live region lives INSIDE the open dialog and speaks the
            // lane the task landed in.
            await ExpectLiveRegionAsync(page, "modeling-dialog-structure-live", "Warehouse");
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("The demo writer persists the authored layout: an editor REMOUNT renders the arranged positions again")]
    public async Task Layout_ReloadKeepsTheAuthoredLayout()
    {
        var page = await OpenEditorAsync(PersistUrl);
        try
        {
            var before = await ReadNodeTransformsAsync(page, "demo/bpmn/validate-order");

            await page.Locator("[data-testid='modeling-arrange']").ClickAsync();
            await ExpectVisibleAsync(page, "[data-testid='modeling-arrange-dialog']");
            await page.Locator("[data-testid='modeling-arrange-direction']")
                .SelectOptionAsync(new SelectOptionValue { Value = "TB" });
            await page.Locator("[data-testid='modeling-arrange-confirm']").ClickAsync();
            await page.Locator("[data-testid='modeling-arrange-dialog']")
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 15000 });
            await ExpectNodeMovedAsync(page, "demo/bpmn/validate-order", before["demo/bpmn/validate-order"]);

            var arranged = await ReadNodeTransformsAsync(page, "demo/bpmn/validate-order");

            // Save through the inspector header, then REMOUNT the editor (a provider round trip).
            await SelectTreeRowAsync(page, "Validate order");
            await page.Locator("[data-testid='modeling-inspector-save']").ClickAsync();
            await ExpectPendingZeroAsync(page);

            await page.Locator("[data-testid='modeling-demo-reload']").ClickAsync();
            await WaitForLoadedAsync(page);
            await ExpectNodeAsync(page, "demo/bpmn/validate-order");

            await ExpectTransformAsync(page, "demo/bpmn/validate-order", arranged["demo/bpmn/validate-order"]);
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("Review U1: the BPMN type markers (user-task icon, gateway marker, event ring) render with a real bounding box instead of being hidden behind the native primitive")]
    public async Task Layout_BpmnMarkersAreVisible()
    {
        // The pre-existing profile scene carries a start event, a user task, an exclusive gateway
        // and a service task — the three marker surfaces in one document.
        var page = await OpenEditorAsync("?editing=1&scenario=bpmn2-generated&notation=bpmn2");
        try
        {
            await ExpectNodeAsync(page, "demo/bpmn2/start");
            await ExpectVisibleBoxAsync(page, ".tm-bpmn-task-marker-user");
            await ExpectVisibleBoxAsync(page, ".tm-bpmn-gateway-marker-exclusive");
            await ExpectVisibleBoxAsync(page, ".tm-diagram-node[data-stencil-id='bpmn2.event.start'] .tm-diagram-node__svg-bg circle");
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("Review U2: the swimlane header band, its separators and the collapse toggle paint with the page PAPER tokens, so the label stays legible in the dark theme (>= 4.5:1)")]
    public async Task Layout_SwimlaneHeaderIsLegibleInTheDarkTheme()
    {
        var page = await OpenEditorAsync(LanesUrl);
        try
        {
            await SetDarkAsync(page);
            await page.Locator("[data-testid='modeling-open-in-editor-button']").ClickAsync();
            await ExpectNodeAsync(page, "lane-warehouse");
            await ExpectNodeAsync(page, "lane-scene-pool");
            await SettleAsync(page);

            var contrast = await SwimlaneHeaderContrastAsync(page);
            Assert.IsNotNull(contrast, "the swimlane header must expose a computed colour pair");
            Assert.IsTrue(contrast!.Value >= 4.5,
                $"the dark swimlane header must keep a legible contrast (was {contrast.Value:F2}:1)");

            // Both orientations resolve the paper tokens (the lane bands and the pool band).
            var toggle = await page.EvaluateAsync<string?>(
                """
                () => {
                    const root = document.querySelector(".tm-modeling-editor__open-editor-modal") || document;
                    const button = root.querySelector(".tm-diagram-node__collapse-btn");
                    if (!button) return null;
                    const style = getComputedStyle(button);
                    return style.color + '|' + style.backgroundColor;
                }
                """);
            Assert.IsNotNull(toggle, "a collapsible band renders the collapse toggle");
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("Screenshots: the arrange sheet, the BPMN lanes bands and the arranged model in light and dark (opt-in via TM_WRITE_BASELINES=1)")]
    public async Task Layout_CapturesScreenshots()
    {
        if (!BaselineGeneratorTestBase.WritesAllowed)
        {
            Assert.Inconclusive($"Set {BaselineGeneratorTestBase.EnvironmentVariable}=1 to write the modeling layout screenshots.");
        }

        // ── Desktop: the ARRANGE sheet over the default model ────────────────────────────────
        var desktop = await OpenEditorAsync(EditingUrl, 1440, 900);
        try
        {
            await desktop.Locator("[data-testid='modeling-arrange']").ClickAsync();
            await ExpectVisibleAsync(desktop, "[data-testid='modeling-arrange-dialog']");
            await SettleAsync(desktop);
            await SaveScreenshotAsync(desktop, "layout-arrange-light");
            await SetDarkAsync(desktop);
            await SettleAsync(desktop);
            await SaveScreenshotAsync(desktop, "layout-arrange-dark");

            // The ARRANGED canvas itself (light + dark), after the sheet commits.
            await SetLightAsync(desktop);
            await desktop.Locator("[data-testid='modeling-arrange-confirm']").ClickAsync();
            await desktop.Locator("[data-testid='modeling-arrange-dialog']")
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 15000 });
            await SettleAsync(desktop);
            await SaveScreenshotAsync(desktop, "layout-arranged-light");
            await SetDarkAsync(desktop);
            await SettleAsync(desktop);
            await SaveScreenshotAsync(desktop, "layout-arranged-dark");
        }
        finally
        {
            await desktop.Context.CloseAsync();
        }

        // ── Desktop: the BPMN lane BANDS in the open-editor dialog ───────────────────────────
        var lanes = await OpenEditorAsync(LanesUrl, 1440, 900);
        try
        {
            await lanes.Locator("[data-testid='modeling-open-in-editor-button']").ClickAsync();
            await ExpectNodeAsync(lanes, "lane-warehouse");
            await SettleAsync(lanes);
            await SaveScreenshotAsync(lanes, "layout-lanes-dialog-light");

            // The theme switch re-renders the modal's embedded editor; capture the DARK dialog in a
            // freshly opened state so the screenshot shows a settled canvas (a re-render in flight
            // can briefly present the backdrop before the fit re-runs).
            await SetDarkAsync(lanes);
            await SettleAsync(lanes);
            await CloseOpenEditorAsync(lanes);
            await lanes.Locator("[data-testid='modeling-open-in-editor-button']").ClickAsync();
            await ExpectNodeAsync(lanes, "lane-warehouse");
            await SettleAsync(lanes);
            await SaveScreenshotAsync(lanes, "layout-lanes-dialog-dark");
        }
        finally
        {
            await lanes.Context.CloseAsync();
        }

        // ── Tablet: the same lane scene (docked preview, touch band) ─────────────────────────
        var tablet = await OpenEditorAsync(LanesUrl, 1180, 820, touch: true);
        try
        {
            await SettleAsync(tablet);
            await SaveScreenshotAsync(tablet, "layout-lanes-tablet-light");
            await SetDarkAsync(tablet);
            await SettleAsync(tablet);
            await SaveScreenshotAsync(tablet, "layout-lanes-tablet-dark");
        }
        finally
        {
            await tablet.Context.CloseAsync();
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<IPage> OpenEditorAsync(string query, int width = 1440, int height = 900, bool touch = false)
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

    /// <summary>Closes the "Open in editor" dialog through its header close button.</summary>
    private static async Task CloseOpenEditorAsync(IPage page)
    {
        await page.Locator("[data-modeling-focus='open-editor-close']").First.ClickAsync();
        await page.Locator(".tm-modeling-editor__open-editor-modal")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 20000 });
    }

    private static async Task ExpectVisibleAsync(IPage page, string selector, int timeout = 15000)
        => await page.Locator(selector).WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = timeout });

    private static async Task ExpectNodeAsync(IPage page, string nodeId)
        => await page.Locator($"[data-node-id='{nodeId}']")
            .First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30000 });

    private static async Task<Dictionary<string, string>> ReadNodeTransformsAsync(IPage page, params string[] nodeIds)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var nodeId in nodeIds)
        {
            await ExpectNodeAsync(page, nodeId);
            result[nodeId] = await page.Locator($"[data-node-id='{nodeId}']").First.GetAttributeAsync("transform") ?? string.Empty;
        }

        return result;
    }

    private static async Task ExpectTransformAsync(IPage page, string nodeId, string expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        string? actual = null;
        while (DateTime.UtcNow < deadline)
        {
            actual = await page.Locator($"[data-node-id='{nodeId}']").First.GetAttributeAsync("transform");
            if (string.Equals(actual, expected, StringComparison.Ordinal))
            {
                return;
            }

            await page.WaitForTimeoutAsync(100);
        }

        Assert.Fail($"node '{nodeId}' transform was '{actual}', expected '{expected}'");
    }

    private static async Task ExpectNodeMovedAsync(IPage page, string nodeId, string previousTransform)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            var actual = await page.Locator($"[data-node-id='{nodeId}']").First.GetAttributeAsync("transform");
            if (!string.Equals(actual, previousTransform, StringComparison.Ordinal))
            {
                return;
            }

            await page.WaitForTimeoutAsync(100);
        }

        Assert.Fail($"node '{nodeId}' never moved away from '{previousTransform}'");
    }

    private static async Task ExpectChangeSetContainsAsync(IPage page, string fragment)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        string text = string.Empty;
        while (DateTime.UtcNow < deadline)
        {
            text = await page.Locator("[data-testid='modeling-demo-last-changeset']").TextContentAsync() ?? string.Empty;
            if (text.Contains(fragment, StringComparison.Ordinal))
            {
                return;
            }

            await page.WaitForTimeoutAsync(100);
        }

        Assert.Fail($"the last change set never contained {fragment}. Change set: {text}");
    }

    private static async Task ExpectPendingZeroAsync(IPage page)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        string text = string.Empty;
        while (DateTime.UtcNow < deadline)
        {
            text = await page.Locator("[data-testid='modeling-demo-pending']").TextContentAsync() ?? string.Empty;
            if (ExtractCount(text) == 0)
            {
                return;
            }

            await page.WaitForTimeoutAsync(100);
        }

        Assert.Fail($"the editor still reports pending changes after the save: {text}");
    }

    private static int ExtractCount(string? text)
        => int.TryParse(new string((text ?? string.Empty).Where(char.IsDigit).ToArray()), out var value) ? value : -1;

    private static async Task SelectTreeRowAsync(IPage page, string name)
    {
        var row = page.Locator("[data-testid='modeling-model-tree-panel'] [role='treeitem']")
            .Filter(new LocatorFilterOptions { HasTextString = name })
            .First;
        await row.ClickAsync(new LocatorClickOptions { Timeout = 15000 });
    }

    /// <summary>
    /// A client-space point on the NODE BODY of the given dialog node: the group's centre is tried
    /// first, then a few insets, and every candidate is hit-tested — the first one that resolves to
    /// the same node WITHOUT being a port, connect arrow or resize handle wins (a press on any of
    /// those starts a different gesture: an edge draw or a resize).
    /// </summary>
    private static Task<double[]?> BodyPointOfSceneNodeAsync(IPage page, string nodeId)
        => page.EvaluateAsync<double[]?>(
            """
            (id) => {
                const root = document.querySelector(".tm-modeling-editor__open-editor-modal") || document;
                const el = root.querySelector("g.tm-diagram-node[data-node-id='" + id + "']")
                    || root.querySelector("g[data-node-id='" + id + "']");
                if (!el) return null;
                const r = el.getBoundingClientRect();
                const candidates = [
                    [r.x + r.width * 0.5, r.y + r.height * 0.5],
                    [r.x + r.width * 0.35, r.y + r.height * 0.45],
                    [r.x + r.width * 0.65, r.y + r.height * 0.45],
                    [r.x + r.width * 0.5, r.y + r.height * 0.7],
                    [r.x + r.width * 0.35, r.y + r.height * 0.7]
                ];
                const blocked = '.tm-diagram-port, .tm-diagram-port-hit, .tm-diagram-connection-point, .tm-diagram-connect-arrow, .tm-diagram-resize-handle, .tm-diagram-resize-handle-hit, .tm-diagram-rotate-handle, .tm-diagram-rotate-handle-hit';
                for (const point of candidates) {
                    const hit = document.elementFromPoint(point[0], point[1]);
                    const node = hit && hit.closest ? hit.closest('[data-node-id]') : null;
                    if (node && node.getAttribute('data-node-id') === id && !(hit.closest && hit.closest(blocked))) {
                        return point;
                    }
                }
                return null;
            }
            """,
            nodeId);

    /// <summary>A client-space point inside the lane that is NOT covered by one of its child nodes —
    /// the right-hand quarter of the band, where the drop resolves to that lane.</summary>
    private static Task<double[]?> LaneDropPointAsync(IPage page, string nodeId)
        => page.EvaluateAsync<double[]?>(
            """
            (id) => {
                const root = document.querySelector(".tm-modeling-editor__open-editor-modal") || document;
                const el = root.querySelector("g.tm-diagram-node[data-node-id='" + id + "']")
                    || root.querySelector("g[data-node-id='" + id + "']");
                if (!el) return null;
                const r = el.getBoundingClientRect();
                for (const fx of [0.78, 0.86, 0.7]) {
                    const x = r.x + r.width * fx;
                    const y = r.y + r.height * 0.5;
                    const hit = document.elementFromPoint(x, y);
                    const node = hit && hit.closest ? hit.closest('[data-node-id]') : null;
                    if (!node || node.getAttribute('data-node-id') === id) {
                        return [x, y];
                    }
                }
                return [r.x + r.width * 0.78, r.y + r.height * 0.5];
            }
            """,
            nodeId);

    /// <summary>Review U1: the element must render with a REAL box (a hidden stencil SVG has a
    /// zero-size bounding box even though the DOM node exists).</summary>
    private static async Task ExpectVisibleBoxAsync(IPage page, string selector)
    {
        var box = await page.Locator(selector).First.BoundingBoxAsync(
            new LocatorBoundingBoxOptions { Timeout = 20000 });
        Assert.IsNotNull(box, $"'{selector}' must render (the BPMN marker was hidden)");
        Assert.IsTrue(box!.Width > 0 && box.Height > 0,
            $"'{selector}' must have a visible bounding box (was {box.Width}x{box.Height})");
    }

    /// <summary>Review U7: the live region with the given test id must eventually contain the text.</summary>
    private static async Task ExpectLiveRegionAsync(IPage page, string testId, string text)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        string content = string.Empty;
        while (DateTime.UtcNow < deadline)
        {
            content = await page.Locator($"[data-testid='{testId}']").First.TextContentAsync() ?? string.Empty;
            if (content.Contains(text, StringComparison.Ordinal))
            {
                return;
            }

            await page.WaitForTimeoutAsync(100);
        }

        Assert.Fail($"the live region '{testId}' never contained '{text}' (was '{content}')");
    }

    /// <summary>The WCAG contrast ratio of the swimlane header's computed ink over its background.</summary>
    private static async Task<double?> SwimlaneHeaderContrastAsync(IPage page)
    {
        var pair = await page.EvaluateAsync<string?>(
            """
            () => {
                const root = document.querySelector(".tm-modeling-editor__open-editor-modal") || document;
                const el = root.querySelector(".tm-diagram-node__swimlane-header");
                if (!el) return null;
                const style = getComputedStyle(el);
                return style.color + '|' + style.backgroundColor;
            }
            """);
        if (string.IsNullOrWhiteSpace(pair))
        {
            return null;
        }

        var parts = pair.Split('|');
        var foreground = ParseRgb(parts[0]);
        var background = ParseRgb(parts.Length > 1 ? parts[1] : string.Empty);
        if (foreground is null || background is null)
        {
            return null;
        }

        static double Luminance((double R, double G, double B) colour)
        {
            static double Channel(double value)
            {
                var normalized = value / 255d;
                return normalized <= 0.03928 ? normalized / 12.92 : Math.Pow((normalized + 0.055) / 1.055, 2.4);
            }

            return (0.2126 * Channel(colour.R)) + (0.7152 * Channel(colour.G)) + (0.0722 * Channel(colour.B));
        }

        var light = Math.Max(Luminance(foreground.Value), Luminance(background.Value));
        var dark = Math.Min(Luminance(foreground.Value), Luminance(background.Value));
        return (light + 0.05) / (dark + 0.05);
    }

    private static (double R, double G, double B)? ParseRgb(string value)
    {
        var numbers = new string(value.Where(c => char.IsDigit(c) || c == '.' || c == ' ').ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (numbers.Length < 3)
        {
            return null;
        }

        return (double.Parse(numbers[0], System.Globalization.CultureInfo.InvariantCulture),
            double.Parse(numbers[1], System.Globalization.CultureInfo.InvariantCulture),
            double.Parse(numbers[2], System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>Switches the demo back to the light theme through the same hidden toggle.</summary>
    private static Task SetLightAsync(IPage page) => EnsureThemeAsync(page, "light");

    private static Task SetDarkAsync(IPage page) => EnsureThemeAsync(page, "dark");

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
        Assert.IsTrue(new FileInfo(path).Length > 10_000, $"{fileName} should contain the rendered layout screen.");
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
