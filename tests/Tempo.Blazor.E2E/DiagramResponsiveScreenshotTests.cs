using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// DiagramEditor redesign phase 1 "before" baseline: the full editor plus the template gallery,
/// the SQL/CSV import dialogs, the search panel and the connect modal captured at desktop
/// 1440×900, tablet 1024×768 (touch) and mobile 390×844 / 320×640.
/// Screenshots land in <c>tests/Tempo.Blazor.E2E/__screenshots__/diagram-editor/</c> — a
/// committed, durable directory (vstest deletes the per-run TestResults deployment folder), and
/// each file is also attached to the test result. Review them against
/// <c>output/imagegen/tempo-reimagined/Tempo.Blazor.DiagramEditor/</c>.
/// </summary>
[TestClass]
public sealed class DiagramResponsiveScreenshotTests : WasmTestBase
{
    private const string DiagramEditorUrl = "/diagram-editor";

    private static string OutputDirectory =>
        Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "__screenshots__", "diagram-editor");

    [TestMethod]
    [TestCategory("WASM")]
    [Description("1440×900: editor, template gallery, SQL/CSV import dialogs, search panel, connect modal")]
    public async Task Desktop_1440_CapturesEditorDialogsAndConnectModal() =>
        await CaptureAllStatesAsync(1440, 900, touch: false, "desktop-1440");

    [TestMethod]
    [TestCategory("WASM")]
    [Description("1024×768 touch: editor, template gallery, SQL/CSV import dialogs, search panel, connect modal")]
    public async Task Tablet_1024Touch_CapturesEditorDialogsAndConnectModal() =>
        await CaptureAllStatesAsync(1024, 768, touch: true, "tablet-1024-touch");

    [TestMethod]
    [TestCategory("WASM")]
    [Description("390×844: editor, template gallery, SQL/CSV import dialogs, search panel, connect modal")]
    public async Task Mobile_390_CapturesEditorDialogsAndConnectModal() =>
        await CaptureAllStatesAsync(390, 844, touch: false, "mobile-390");

    [TestMethod]
    [TestCategory("WASM")]
    [Description("320×640: editor, template gallery, SQL/CSV import dialogs, search panel, connect modal")]
    public async Task Mobile_320_CapturesEditorDialogsAndConnectModal() =>
        await CaptureAllStatesAsync(320, 640, touch: false, "mobile-320");

    /// <summary>
    /// Phase-3 (visual layer) theme matrix: the editor, the layers panel and the four dialogs
    /// captured at 1440×900 in every light/dark × default/indigo combination. The phase-3
    /// tokenization removed every component-level dark override, so each dark capture must be
    /// produced purely by the semantic tokens (tokens-dark.css) — a broken dark capture here is
    /// the regression signal. Files land in __screenshots__/diagram-editor/ (committed).
    /// </summary>
    [TestMethod]
    [TestCategory("WASM")]
    [Description("1440×900 light/dark × default/indigo: editor, layers panel, gallery, SQL/CSV dialogs, connect modal")]
    public async Task Desktop_1440_ThemeMatrix_CapturesEditorLayersAndDialogs()
    {
        foreach (var dark in new[] { false, true })
        {
            foreach (var indigo in new[] { false, true })
            {
                var theme = (dark ? "dark" : "light") + "-" + (indigo ? "indigo" : "default");
                var page = await OpenDiagramEditorAsync(1440, 900, touch: false);
                await page.EvaluateAsync(
                    $$"""
                    () => {
                        document.documentElement.setAttribute('data-theme', '{{(dark ? "dark" : "light")}}');
                        document.documentElement.setAttribute('data-tm-theme', '{{(indigo ? "indigo" : "default")}}');
                        localStorage.setItem('tm-demo-color-theme', '{{(indigo ? "indigo" : "default")}}');
                    }
                    """);
                await page.WaitForTimeoutAsync(150);

                // 1. Full editor
                await ShotAsync(page, $"desktop-1440-{theme}--editor");

                // 2. Layers panel: the properties panel is flex-shrink:0 above it, so the layers
                //    wrap only gets the LEFTOVER height (a 2px strip at 1440×900 — the demo's
                //    default state, unchanged by phase 3). Collapse the properties panel first
                //    (its own panels-group toggle) so the capture shows the real panel.
                await page.Locator("[data-tm-group='panels'] button[title='Toggle properties panel']")
                    .First.ClickAsync();
                var layers = page.Locator(".tm-diagram-editor__layers-wrap");
                await layers.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
                await page.WaitForTimeoutAsync(250);
                await layers.ScreenshotAsync(new LocatorScreenshotOptions
                {
                    Path = Path.Combine(OutputDirectory, $"desktop-1440-{theme}--layers.png"),
                    Type = ScreenshotType.Png,
                    Timeout = 60_000,
                });
                await page.Locator("[data-tm-group='panels'] button[title='Toggle properties panel']")
                    .First.ClickAsync();
                await page.WaitForTimeoutAsync(150);

                // 3. Template gallery
                await page.Locator("[data-tm-group='templates'] button").First.ClickAsync();
                await page.Locator(".tm-diagram-template-gallery").WaitForAsync(
                    new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
                await ShotSettledAsync(page, $"desktop-1440-{theme}--template-gallery");
                await page.Keyboard.PressAsync("Escape");
                await ExpectHiddenAsync(page, ".tm-diagram-template-gallery");

                // 4. SQL import dialog
                await page.Locator("[data-tm-group='import'] .tm-dropdown-trigger").First.ClickAsync();
                await page.Locator(".tm-dropdown-item[role='menuitem']", new PageLocatorOptions { HasText = "Import SQL" })
                    .First.ClickAsync();
                await page.Locator(".tm-diagram-sql-import").WaitForAsync(
                    new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
                await ShotSettledAsync(page, $"desktop-1440-{theme}--sql-import-dialog");
                await page.Locator(".tm-modal-footer button", new PageLocatorOptions { HasText = "Cancel" })
                    .First.ClickAsync();
                await ExpectHiddenAsync(page, ".tm-diagram-sql-import");

                // 5. CSV import dialog
                await page.Locator("[data-tm-group='import'] .tm-dropdown-trigger").First.ClickAsync();
                await page.Locator(".tm-dropdown-item[role='menuitem']", new PageLocatorOptions { HasText = "Import CSV" })
                    .First.ClickAsync();
                await page.Locator(".tm-diagram-csv-import").WaitForAsync(
                    new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
                await ShotSettledAsync(page, $"desktop-1440-{theme}--csv-import-dialog");
                await page.Locator(".tm-modal-footer button", new PageLocatorOptions { HasText = "Cancel" })
                    .First.ClickAsync();
                await ExpectHiddenAsync(page, ".tm-diagram-csv-import");

                // 6. Connect modal (JS selection entry point, same as the phase-1 baseline)
                await page.EvaluateAsync(
                    """
                    () => {
                        const canvas = document.querySelector('.tm-diagram-canvas');
                        if (!canvas) return 'no-canvas';
                        const inst = window.tmDiagramEditor.instances.get(canvas.id);
                        if (!inst || !inst.dotNetRef) return 'no-instance';
                        const id = document.querySelector('.tm-diagram-node')?.getAttribute('data-node-id');
                        if (!id) return 'no-node';
                        inst.selectedIds = new Set([id]);
                        window.tmDiagramEditor._updateSelection(inst);
                        inst.dotNetRef.invokeMethodAsync('OnSelectionChanged', [id]);
                        return 'ok';
                    }
                    """);
                await page.Locator(".tm-diagram-connect-arrow").First.ClickAsync();
                await page.Locator(".tm-diagram-connect-modal__grid").WaitForAsync(
                    new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
                await ShotSettledAsync(page, $"desktop-1440-{theme}--connect-modal");

                await page.CloseAsync();
            }
        }
    }

    private async Task CaptureAllStatesAsync(int width, int height, bool touch, string prefix)
    {
        var page = await OpenDiagramEditorAsync(width, height, touch);

        // 1. Full editor
        await ShotAsync(page, $"{prefix}--editor");

        // 2. Template gallery
        await page.Locator("[data-tm-group='templates'] button").First.ClickAsync();
        await page.Locator(".tm-diagram-template-gallery").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await ShotSettledAsync(page, $"{prefix}--template-gallery");
        await page.Keyboard.PressAsync("Escape");
        await ExpectHiddenAsync(page, ".tm-diagram-template-gallery");

        // 3. SQL import dialog
        await page.Locator("[data-tm-group='import'] .tm-dropdown-trigger").First.ClickAsync();
        await page.Locator(".tm-dropdown-item[role='menuitem']", new PageLocatorOptions { HasText = "Import SQL" })
            .First.ClickAsync();
        await page.Locator(".tm-diagram-sql-import").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await ShotSettledAsync(page, $"{prefix}--sql-import-dialog");
        await page.Locator(".tm-modal-footer button", new PageLocatorOptions { HasText = "Cancel" })
            .First.ClickAsync();
        await ExpectHiddenAsync(page, ".tm-diagram-sql-import");

        // 4. CSV import dialog
        await page.Locator("[data-tm-group='import'] .tm-dropdown-trigger").First.ClickAsync();
        await page.Locator(".tm-dropdown-item[role='menuitem']", new PageLocatorOptions { HasText = "Import CSV" })
            .First.ClickAsync();
        await page.Locator(".tm-diagram-csv-import").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await ShotSettledAsync(page, $"{prefix}--csv-import-dialog");
        await page.Locator(".tm-modal-footer button", new PageLocatorOptions { HasText = "Cancel" })
            .First.ClickAsync();
        await ExpectHiddenAsync(page, ".tm-diagram-csv-import");

        // 5. Search panel
        await page.Locator("[data-tm-group='search'] button").First.ClickAsync();
        await page.Locator(".tm-diagram-search-panel").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await ShotAsync(page, $"{prefix}--search-panel");
        await page.Locator("button[aria-label='Close search']").First.ClickAsync();
        await ExpectHiddenAsync(page, ".tm-diagram-search-panel");

        // 6. Connect modal (selecting a node renders the connect arrows in the decorator pane;
        //    the JS selection path is the stable entry point DiagramBaselineScreenshots uses)
        await page.EvaluateAsync(
            """
            () => {
                const canvas = document.querySelector('.tm-diagram-canvas');
                if (!canvas) return 'no-canvas';
                const inst = window.tmDiagramEditor.instances.get(canvas.id);
                if (!inst || !inst.dotNetRef) return 'no-instance';
                const id = document.querySelector('.tm-diagram-node')?.getAttribute('data-node-id');
                if (!id) return 'no-node';
                inst.selectedIds = new Set([id]);
                window.tmDiagramEditor._updateSelection(inst);
                inst.dotNetRef.invokeMethodAsync('OnSelectionChanged', [id]);
                return 'ok';
            }
            """);
        await page.Locator(".tm-diagram-connect-arrow").First.ClickAsync();
        await page.Locator(".tm-diagram-connect-modal__grid").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await ShotSettledAsync(page, $"{prefix}--connect-modal");
    }

    private async Task<IPage> OpenDiagramEditorAsync(int width, int height, bool touch)
    {
        IBrowserContext context;
        if (touch)
        {
            context = await Browser.NewContextAsync(new BrowserNewContextOptions
            {
                HasTouch = true,
                ViewportSize = new ViewportSize { Width = width, Height = height },
                Locale = "en-US",
                IgnoreHTTPSErrors = true,
            });
            RegisterContext(context);
        }
        else
        {
            context = await CreateContextAsync();
        }

        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, height);
        await page.AddInitScriptAsync("() => localStorage.setItem('tm-demo-culture', 'en')");
        await page.GotoAsync($"{BaseUrl}{DiagramEditorUrl}", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.Load,
            Timeout = 60_000
        });
        await WaitForAppReadyAsync(page);

        await page.WaitForSelectorAsync(".tm-diagram-canvas", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 20_000
        });
        await page.WaitForFunctionAsync(
            """
            () => {
                const canvas = document.querySelector('.tm-diagram-canvas');
                if (!canvas || !canvas.id) return false;
                const ed = window.tmDiagramEditor;
                return !!(ed && ed.instances && ed.instances.get(canvas.id));
            }
            """, null, new PageWaitForFunctionOptions { Timeout = 20_000 });

        // The demo default (UML sample) must have rendered at least one node.
        await page.WaitForSelectorAsync(".tm-diagram-node", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15_000
        });
        await page.WaitForTimeoutAsync(400);
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync("() => document.activeElement && document.activeElement.blur()");
        return page;
    }

    private async Task ShotAsync(IPage page, string name)
    {
        Directory.CreateDirectory(OutputDirectory);
        var path = Path.Combine(OutputDirectory, name + ".png");
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = path,
            Type = ScreenshotType.Png,
            FullPage = false,
            Timeout = 60_000,
        });
        TestContext.AddResultFile(path);
    }

    /// <summary>
    /// Screenshots a dialog after its open animations finished. The modal overlay fade and the
    /// mobile sheet slide-up run ~300ms; a frame captured mid-animation shows the panel shifted
    /// below the viewport with the footer buttons cut in half, which reads as an unreachable
    /// footer (UX review phase 2 blocker) even though the settled sheet keeps Cancel/Import
    /// inside the viewport. Waiting on the animation futures keeps the captures deterministic.
    /// </summary>
    private async Task ShotSettledAsync(IPage page, string name)
    {
        await page.EvaluateAsync(
            """
            async () => {
                const animations = document.getAnimations();
                await Promise.all(animations.map(a => a.finished.catch(() => { })));
            }
            """);
        await page.WaitForTimeoutAsync(100);
        await ShotAsync(page, name);
    }

    private static async Task ExpectHiddenAsync(IPage page, string selector)
    {
        await page.Locator(selector).First.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
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
