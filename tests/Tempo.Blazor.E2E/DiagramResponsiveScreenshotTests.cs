using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// DiagramEditor redesign phase 1 "before" baseline: the full editor plus the template gallery,
/// the SQL/CSV import dialogs, the search panel and the connect modal captured at desktop
/// 1440×900, tablet 1366×900 (touch) and mobile 390×844 / 320×640.
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
    [Description("Tablet landscape: editor, template gallery, SQL/CSV import dialogs, search panel, connect modal")]
    public async Task Tablet_1024Touch_CapturesEditorDialogsAndConnectModal()
    {
        // The demo sidebar takes ~290px: at a 1024 viewport a tablet-band editor (≥768) would
        // push the toolbar's right end past the viewport edge (overflow-x is hidden in the
        // demo shell, so the "More" button is unreachable). Widen the window so the pinned
        // tablet band fits with room for the whole toolbar row. The prefix carries the real
        // window size (1366) so these captures cannot collide with the DiagramTabletTouchE2ETests
        // shots taken at a 1024×768 window under the tablet-1024-touch-- prefix.
        await CaptureAllStatesAsync(1366, 900, touch: true, "tablet-1366-touch", scrollEditorIntoView: true, pinLayout: "tablet");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("390×844: editor, search panel, connect modal (G2: gallery/imports stay tablet+)")]
    public async Task Mobile_390_CapturesEditorDialogsAndConnectModal() =>
        await CaptureAllStatesAsync(390, 844, touch: false, "mobile-390", pinLayout: "mobile");

    [TestMethod]
    [TestCategory("WASM")]
    [Description("320×640: editor, search panel, connect modal (G2: gallery/imports stay tablet+)")]
    public async Task Mobile_320_CapturesEditorDialogsAndConnectModal() =>
        await CaptureAllStatesAsync(320, 640, touch: false, "mobile-320", pinLayout: "mobile");

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

                // 1. Full editor. At the 900px viewport height the shell's status bar (zoom
                //    controls) sits at the page bottom — below the fold — so the capture scrolls
                //    the editor fully into the frame first (the status bar is phase-4 review
                //    evidence) and restores the scroll afterwards, keeping the overlay captures
                //    below framed exactly as before.
                await page.EvaluateAsync(
                    """
                    () => {
                        const status = document.querySelector('.tm-editor-shell__status');
                        if (status) status.scrollIntoView({ block: 'nearest', inline: 'nearest', behavior: 'instant' });
                    }
                    """);
                await page.WaitForTimeoutAsync(100);
                await ShotAsync(page, $"desktop-1440-{theme}--editor");
                await page.EvaluateAsync("() => window.scrollTo({ top: 0, behavior: 'instant' })");
                await page.WaitForTimeoutAsync(100);

                // 2. Layers panel: since phase 4 the panels share the inspector tabs — collapsing
                //    the properties panel auto-selects the Layers tab. The minimap card below the
                //    tabs gets the column's leftover height, so collapse it too (its own panels
                //    toggle) and the capture shows the full layers panel.
                await page.Locator("[data-tm-group='panels'] button[title='Toggle minimap']").First.ClickAsync();
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
                await page.Locator("[data-tm-group='panels'] button[title='Toggle minimap']").First.ClickAsync();
                await page.WaitForTimeoutAsync(150);

                // 3. Template gallery
                await OpenToolbarCommandAsync(page, "[data-tm-group='templates'] button", "Templates");
                await page.Locator(".tm-diagram-template-gallery").WaitForAsync(
                    new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
                await ShotSettledAsync(page, $"desktop-1440-{theme}--template-gallery");
                await page.Keyboard.PressAsync("Escape");
                await ExpectHiddenAsync(page, ".tm-diagram-template-gallery");

                // 4. SQL import dialog
                await OpenImportMenuItemAsync(page, "Import SQL");
                await page.Locator(".tm-diagram-sql-import").WaitForAsync(
                    new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
                await ShotSettledAsync(page, $"desktop-1440-{theme}--sql-import-dialog");
                await page.Locator(".tm-modal-footer button", new PageLocatorOptions { HasText = "Cancel" })
                    .First.ClickAsync();
                await ExpectHiddenAsync(page, ".tm-diagram-sql-import");

                // 5. CSV import dialog
                await OpenImportMenuItemAsync(page, "Import CSV");
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

    private async Task CaptureAllStatesAsync(int width, int height, bool touch, string prefix, bool scrollEditorIntoView = false, string? pinLayout = null)
    {
        var page = await OpenDiagramEditorAsync(width, height, touch);

        // Band pin: the editor resolves its presentation from its own container, and the demo
        // column keeps a 1024 viewport BELOW the 768px mobile threshold (phase 7 band). A test
        // that captures a specific presentation pins the container, exactly like a full-bleed
        // host would. Mobile additionally scopes the set: per G2 the template gallery and the
        // SQL/CSV imports stay tablet+ — the mobile sheet offers Shapes/Properties, the search
        // panel and the connect picker only.
        var isMobile = pinLayout == "mobile";
        if (pinLayout is not null)
        {
            // The shared pin clamps the requested width to the real demo column, so the editor
            // lands in the requested band without ever overflowing the viewport (a 1100px request
            // resolves to the column width — still the tablet band next to the demo sidebar).
            await DiagramEditorE2E.ForceEditorWidthAsync(page, isMobile ? width : 1100, pinLayout);
        }

        // 1. Full editor. On the tablet viewport the demo column is taller than the window: the
        //    shell title and the status bar (zoom − % +) land below the fold, so the capture
        //    scrolls the status bar into the frame first and the shot shows the whole editor —
        //    the same framing fix the phase-4 theme-matrix captures use. The scroll is restored
        //    afterwards, keeping the overlay captures below framed exactly as before.
        if (scrollEditorIntoView)
        {
            await page.EvaluateAsync(
                """
                () => {
                    const status = document.querySelector('.tm-editor-shell__status');
                    if (status) status.scrollIntoView({ block: 'nearest', inline: 'nearest', behavior: 'instant' });
                }
                """);
            await page.WaitForTimeoutAsync(100);
            await ShotAsync(page, $"{prefix}--editor");
            await page.EvaluateAsync("() => window.scrollTo({ top: 0, behavior: 'instant' })");
            await page.WaitForTimeoutAsync(100);
        }
        else
        {
            await ShotAsync(page, $"{prefix}--editor");
        }

        // 2. Template gallery (desktop/tablet only — G2 keeps templates tablet+)
        if (!isMobile)
        {
            await OpenToolbarCommandAsync(page, "[data-tm-group='templates'] button", "Templates");
            await page.Locator(".tm-diagram-template-gallery").WaitForAsync(
                new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
            await ShotSettledAsync(page, $"{prefix}--template-gallery");
            await AssertDialogFitsViewportAsync(page, ".tm-diagram-template-gallery", expectFooter: true);
            await page.Keyboard.PressAsync("Escape");
            await ExpectHiddenAsync(page, ".tm-diagram-template-gallery");

            // 3. SQL import dialog (tablet+ per G2)
            await OpenImportMenuItemAsync(page, "Import SQL");
            await page.Locator(".tm-diagram-sql-import").WaitForAsync(
                new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
            await ShotSettledAsync(page, $"{prefix}--sql-import-dialog");
            await AssertDialogFitsViewportAsync(page, ".tm-diagram-sql-import", expectFooter: true);
            await page.Locator(".tm-modal-footer button", new PageLocatorOptions { HasText = "Cancel" })
                .First.ClickAsync();
            await ExpectHiddenAsync(page, ".tm-diagram-sql-import");

            // 4. CSV import dialog (tablet+ per G2)
            await OpenImportMenuItemAsync(page, "Import CSV");
            await page.Locator(".tm-diagram-csv-import").WaitForAsync(
                new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
            await ShotSettledAsync(page, $"{prefix}--csv-import-dialog");
            await AssertDialogFitsViewportAsync(page, ".tm-diagram-csv-import", expectFooter: true);
            await page.Locator(".tm-modal-footer button", new PageLocatorOptions { HasText = "Cancel" })
                .First.ClickAsync();
            await ExpectHiddenAsync(page, ".tm-diagram-csv-import");
        }

        // 5. Search panel (desktop/tablet: the header toggle; mobile: the "⋯" view menu
        //    carries the search group — there is no command row on the phone). The menu
        //    trigger is addressed by its stable testid: the first .more-btn on mobile is
        //    the (disabled) Undo button, not the menu.
        if (isMobile)
        {
            await page.Locator("[data-testid='diagram-editor-more']").First.ClickAsync();
        }
        await page.Locator("[data-tm-group='search'] button").First.ClickAsync();
        if (isMobile)
        {
            // The toggle menu stays open after invoking an entry and its backdrop covers the
            // trigger; close it through the DOM (same Blazor handler) so it does not overlay
            // the search panel in the capture.
            await page.EvaluateAsync(
                "() => document.querySelector(\"[data-testid='diagram-editor-more']\")?.click()");
        }
        await page.Locator(".tm-diagram-search-panel").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await ShotAsync(page, $"{prefix}--search-panel");
        await page.Locator("button[aria-label='Close search']").First.ClickAsync();
        await ExpectHiddenAsync(page, ".tm-diagram-search-panel");

        // 6. Connect modal (selecting a node renders the connect arrows in the decorator pane;
        //    the JS selection path is the stable entry point DiagramBaselineScreenshots uses).
        //    Mobile skips it: G2 keeps edge work (the connect arrows) tablet+, so the arrows —
        //    and with them the modal entry — do not render on a phone. The mobile sheet
        //    PRESENTATION of the dialog is exercised by the phase-5 dialog tests.
        if (!isMobile)
        {
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
            // The connect picker's primary action is a palette tile; on the mobile sheet the
            // sticky footer with the Close button appears as well. Settle first — a frame taken
            // mid slide-up would read the footer below the fold.
            await ShotSettledAsync(page, $"{prefix}--connect-modal");
            await AssertDialogFitsViewportAsync(page, ".tm-diagram-connect-modal", expectFooter: width < 640);
        }
    }

    /// <summary>
    /// Phase-5 DoD assertions, run against a settled dialog: the page has no horizontal scroll
    /// (a wide preview table must scroll INSIDE its wrapper), and the dialog's footer — when the
    /// dialog has one — sits fully inside the viewport with a clickable primary button, so a
    /// phone user confirms without scrolling the page. The page itself never scrolls.
    /// </summary>
    private static async Task AssertDialogFitsViewportAsync(IPage page, string contentSelector, bool expectFooter)
    {
        var metrics = await page.EvaluateAsync<DialogMetrics>(
            """
            (sel) => {
                const content = document.querySelector(sel);
                const root = content?.closest('.tm-modal-container');
                const rect = root?.getBoundingClientRect();
                const footer = document.querySelector('.tm-modal-footer');
                const footerRect = footer?.getBoundingClientRect();
                const primary = footer
                    ? Array.from(footer.querySelectorAll('button')).find(b =>
                        b.classList.contains('tm-btn-primary') || b.classList.contains('tm-btn-ok'))
                        ?? footer.querySelector('button')
                    : null;
                const primaryRect = primary?.getBoundingClientRect();
                return {
                    hasRoot: !!rect,
                    rootTop: rect?.top ?? -1,
                    rootBottom: rect?.bottom ?? -1,
                    viewportH: window.innerHeight,
                    viewportW: window.innerWidth,
                    pageScrollX: document.documentElement.scrollWidth - window.innerWidth,
                    hasFooter: !!footerRect && footerRect.height > 0,
                    footerBottom: footerRect?.bottom ?? -1,
                    primaryBottom: primaryRect?.bottom ?? -1,
                    primaryH: primaryRect?.height ?? 0,
                };
            }
            """, contentSelector);

        Assert.IsNotNull(metrics, $"viewport metrics for {contentSelector}");
        Assert.IsTrue(metrics.HasRoot, $"dialog root for {contentSelector} not found");
        Assert.IsTrue(metrics.PageScrollX <= 0,
            $"page must not scroll horizontally for {contentSelector}");
        Assert.AreEqual(expectFooter, metrics.HasFooter,
            $"unexpected footer visibility for {contentSelector}");

        if (expectFooter)
        {
            Assert.IsTrue(metrics.FooterBottom <= metrics.ViewportH + 0.5,
                $"footer bottom ({metrics.FooterBottom}) must stay inside the viewport ({metrics.ViewportH}) for {contentSelector}");
            Assert.IsTrue(metrics.PrimaryBottom <= metrics.ViewportH + 0.5,
                $"primary button must be clickable without page scroll for {contentSelector}");
            Assert.IsTrue(metrics.PrimaryH >= 32,
                $"primary button collapsed to {metrics.PrimaryH}px in {contentSelector}");
        }
    }

    /// <summary>Viewport-fit metrics for one open dialog (a plain mutable class — Playwright's JSON deserializer needs a parameterless constructor).</summary>
    private sealed class DialogMetrics
    {
        public bool HasRoot { get; set; }
        public double RootTop { get; set; }
        public double RootBottom { get; set; }
        public double ViewportH { get; set; }
        public double ViewportW { get; set; }
        public double PageScrollX { get; set; }
        public bool HasFooter { get; set; }
        public double FooterBottom { get; set; }
        public double PrimaryBottom { get; set; }
        public double PrimaryH { get; set; }
    }

    /// <summary>
    /// Phase 4: the command row is a core TmToolbar — a command without room collapses into the
    /// "More" menu (its bar button is inert). Click it wherever it currently is.
    /// </summary>
    private static async Task OpenToolbarCommandAsync(IPage page, string barSelector, string moreEntryText)
    {
        var barButton = page.Locator(barSelector).First;
        if (await barButton.CountAsync() > 0 && await barButton.IsVisibleAsync() && await barButton.IsEnabledAsync())
        {
            await barButton.ClickAsync();
            return;
        }

        await page.Locator(".tm-toolbar-more").First.ClickAsync();
        var entry = page.Locator(".tm-toolbar-more__item")
            .Filter(new() { HasText = moreEntryText }).First;
        await entry.ScrollIntoViewIfNeededAsync();
        await entry.ClickAsync();
    }

    /// <summary>
    /// Opens an Import dropdown item. On the bar the Import dropdown opens as before; when the
    /// Import group collapsed into "More", the group's MenuItems offer the same commands as menu
    /// entries (the SQL/CSV handlers run from there directly).
    /// </summary>
    private static async Task OpenImportMenuItemAsync(IPage page, string itemText)
    {
        var trigger = page.Locator("[data-tm-group='import'] .tm-dropdown-trigger").First;
        if (await trigger.CountAsync() > 0 && await trigger.IsVisibleAsync() && await trigger.IsEnabledAsync())
        {
            await trigger.ClickAsync();
            await page.Locator(".tm-dropdown-item[role='menuitem']", new PageLocatorOptions { HasText = itemText })
                .First.ClickAsync();
            return;
        }

        await page.Locator(".tm-toolbar-more").First.ClickAsync();
        var entry = page.Locator(".tm-toolbar-more__item")
            .Filter(new() { HasText = itemText }).First;
        await entry.ScrollIntoViewIfNeededAsync();
        await entry.ClickAsync();
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
