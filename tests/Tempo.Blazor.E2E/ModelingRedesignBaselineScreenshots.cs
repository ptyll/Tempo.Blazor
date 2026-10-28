using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Baseline ("before") screenshot GENERATOR for the Modeling redesign plan — phase 1. It freezes the
/// CURRENT appearance of the modeling editor screens BEFORE any visual phase touches them, so the
/// later phases can diff against it: the layout must not change in this phase (G3 fixes only).
/// <para>
/// Scenarios: default workspace, mixed issues, stale source, the open "Open in editor" dialog,
/// the empty state and the error state. Viewports: 1440×900 (desktop), 1024×768 (tablet,
/// <c>HasTouch</c>), 390×844 and 320×640 (mobile). Output:
/// <c>tests/Tempo.Blazor.E2E/__screenshots__/modeling/</c> — committed as the "before" baseline.
/// </para>
/// <para>
/// Every capture is deterministic: fonts ready, a double <c>requestAnimationFrame</c>, the
/// diagram's <c>.tm-diagram-node</c> set present, and a style tag disabling ALL animations,
/// transitions and the caret. Below 640 px the mixed-issue and stale-source shots switch to the
/// mobile "Inspector" panel tab (the issue and source panels are only reachable through it
/// there) and the mixed-issue runs additionally save an element screenshot of the issue panel
/// at every viewport. The mixed-issue PNGs are the diff subject of the issue-panel phases.
/// </para>
/// <para>
/// Off unless <c>TM_WRITE_BASELINES=1</c>. Not part of the Smoke lane.
/// </para>
/// </summary>
[TestClass]
public sealed class ModelingRedesignBaselineScreenshots : BaselineGeneratorTestBase
{
    private const string ModelingEditorUrl = "/modeling-editor";
    private const string DisableMotionStyle = "*{animation:none!important;transition:none!important;caret-color:transparent!important}";

    private static string OutputDirectory
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
            {
                directory = directory.Parent!;
            }

            var dir = Path.Combine(directory!.FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "modeling");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    [TestMethod]
    [Description("Captures the modeling editor default workspace at all four baseline viewports")]
    public async Task Capture_DefaultWorkspace()
        => await CaptureAsync("default", string.Empty, requiresLoadedState: true);

    [TestMethod]
    [Description("Captures the mixed-issue workspace (including a sourceless issue) at all four baseline viewports plus the issue-panel element shots")]
    public async Task Capture_IssuesMixed()
        => await CaptureAsync("issues-mixed", "?scenario=issues-mixed", requiresLoadedState: true, withIssuePanelShots: true);

    [TestMethod]
    [Description("Captures the stale-source workspace at all four baseline viewports (mobile opens the Inspector tab)")]
    public async Task Capture_StaleSource()
        => await CaptureAsync("stale-source", "?fresh=false", requiresLoadedState: true, inspectorTabOnMobile: true);

    [TestMethod]
    [Description("Captures the open 'Open in editor' dialog at desktop, tablet and phone viewports")]
    public async Task Capture_OpenInEditorDialog()
    {
        foreach (var (width, height, touch) in BaselineViewports)
        {
            var page = await OpenModelingPageAsync(string.Empty, width, height, touch);
            try
            {
                await WaitForLoadedStateAsync(page);
                // Fáze 6 review fix: the tablet sheets start CLOSED — the dialog trigger is
                // reachable without a close-first workaround.
                await page.Locator("[data-testid='modeling-open-in-editor-button']").ClickAsync();
                await page.Locator("[data-testid='modeling-open-diagram-editor']")
                    .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
                await SettleAsync(page);
                await SaveViewportScreenshotAsync(page, $"open-editor-dialog-{width}x{height}");
            }
            finally
            {
                await page.Context.CloseAsync();
            }
        }
    }

    /// <summary>Phase 4: the open-editor dialog is a TmModal now — capture it at the two desktop
    /// widths in light and dark so the scrim, the 90vw × 90vh panel and the dark tokens are
    /// pinned as the "after" baseline of the dialog rework.</summary>
    [TestMethod]
    [DataRow("light", 1440, 900)]
    [DataRow("light", 1280, 800)]
    [DataRow("dark", 1440, 900)]
    [DataRow("dark", 1280, 800)]
    [Description("Captures the open 'Open in editor' dialog at 1440/1280 in light and dark")]
    public async Task Capture_OpenInEditorDialog_Themed(string theme, int width, int height)
    {
        var page = await OpenThemedModelingPageAsync(string.Empty, theme, width, height);
        try
        {
            await WaitForLoadedStateAsync(page);
            await page.Locator("[data-testid='modeling-open-in-editor-button']").ClickAsync();
            await page.Locator("[data-testid='modeling-open-diagram-editor'] .tm-modal")
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
            await SettleAsync(page);
            await SaveViewportScreenshotAsync(page, $"open-editor-dialog-{theme}-{width}x{height}");
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("Captures the empty state at all four baseline viewports")]
    public async Task Capture_EmptyState()
        => await CaptureAsync("empty", "?provider=unknown-provider", requiresLoadedState: false, expectedState: "empty");

    [TestMethod]
    [Description("Captures the error state at all four baseline viewports")]
    public async Task Capture_ErrorState()
        => await CaptureAsync("error", "?scenario=error", requiresLoadedState: false, expectedState: "error");

    /// <summary>Phase 2 review baselines: the three loaded screens under each accent/theme
    /// combination at the desktop viewport (dark tokens, indigo accent, indigo + dark), plus a
    /// selected-element shot freezing the tree's selected node and the inspector detail.</summary>
    [TestMethod]
    [DataRow("dark")]
    [DataRow("indigo")]
    [DataRow("indigo-dark")]
    [Description("Captures default / issues-mixed / stale-source at 1440x900 for dark, indigo and indigo-dark")]
    public async Task Capture_Themed1440x900(string theme)
    {
        foreach (var (name, query) in new[]
                 {
                     (Name: "default", Query: string.Empty),
                     (Name: "issues-mixed", Query: "?scenario=issues-mixed"),
                     (Name: "stale-source", Query: "?fresh=false")
                 })
        {
            var page = await OpenThemedModelingPageAsync(query, theme, 1440, 900);
            try
            {
                await WaitForLoadedStateAsync(page);
                await SettleAsync(page);
                await SaveViewportScreenshotAsync(page, $"{name}-{theme}-1440x900");
            }
            finally
            {
                await page.Context.CloseAsync();
            }
        }
    }

    /// <summary>Phase 5: the same themed set at 1280x800 — the second desktop width of the
    /// shell workspace (light/dark x default/indigo), so the docked MO1 layout is pinned on both
    /// desktop baselines.</summary>
    [TestMethod]
    [DataRow("light", 1280, 800)]
    [DataRow("dark", 1280, 800)]
    [DataRow("indigo", 1280, 800)]
    [DataRow("indigo-dark", 1280, 800)]
    [Description("Captures default / issues-mixed / stale-source at 1280x800 for light, dark, indigo and indigo-dark")]
    public async Task Capture_Themed1280x800(string theme, int width, int height)
    {
        foreach (var (name, query) in new[]
                 {
                     (Name: "default", Query: string.Empty),
                     (Name: "issues-mixed", Query: "?scenario=issues-mixed"),
                     (Name: "stale-source", Query: "?fresh=false")
                 })
        {
            var page = await OpenThemedModelingPageAsync(query, theme, width, height);
            try
            {
                await WaitForLoadedStateAsync(page);
                await SettleAsync(page);
                await SaveViewportScreenshotAsync(page, $"{name}-{theme}-{width}x{height}");
            }
            finally
            {
                await page.Context.CloseAsync();
            }
        }
    }

    /// <summary>Phase 5 review: the Source tab as the docked inspector presents it — light at
    /// 1440x900 (fresh + stale) and dark 1280x800 variants (fresh + stale) — plus the expanded
    /// issues dock with its list at scrollTop=0 (the dock shows the first two full rows).</summary>
    [TestMethod]
    [Description("Captures source-tab-1440x900, stale-source-tab-1440x900, the dark 1280x800 source-tab variants and issues-expanded-1440x900")]
    public async Task Capture_SourceTab_And_IssuesExpanded()
    {
        foreach (var (name, query, theme, width, height) in new[]
                 {
                     (Name: "source-tab", Query: string.Empty, Theme: "light", Width: 1440, Height: 900),
                     (Name: "stale-source-tab", Query: "?fresh=false", Theme: "light", Width: 1440, Height: 900),
                     (Name: "source-tab", Query: string.Empty, Theme: "dark", Width: 1280, Height: 800),
                     (Name: "stale-source-tab", Query: "?fresh=false", Theme: "dark", Width: 1280, Height: 800)
                 })
        {
            var page = await OpenThemedModelingPageAsync(query, theme, width, height);
            try
            {
                await WaitForLoadedStateAsync(page);
                await page.Locator("[data-testid='modeling-tab-source']").ClickAsync(new LocatorClickOptions { Timeout = 10000 });
                await page.Locator("[data-testid='modeling-source-panel']")
                    .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
                await SettleAsync(page);
                await SaveViewportScreenshotAsync(page, $"{name}-{theme}-{width}x{height}");
            }
            finally
            {
                await page.Context.CloseAsync();
            }
        }

        var dock = await OpenThemedModelingPageAsync("?scenario=issues-mixed", "light", 1440, 900);
        try
        {
            await WaitForLoadedStateAsync(dock);
            // scrollTop=0: the expanded dock shows the first two FULL issue rows (no residual
            // scroll from an earlier interaction).
            await dock.EvaluateAsync(
                "() => { const list = document.querySelector(\"[data-testid='modeling-issue-list']\"); if (list) list.scrollTop = 0; }");
            await SettleAsync(dock);
            await SaveViewportScreenshotAsync(dock, "issues-expanded-1440x900");
        }
        finally
        {
            await dock.Context.CloseAsync();
        }
    }

    /// <summary>Phase 5 review fix: the collapsed issues dock — the issues-500 scenario with the
    /// dock collapsed to its header — so the collapsed state (the canvas body's auto row plus the
    /// `[hidden]` dock body) is pinned as a baseline at the primary desktop viewport.</summary>
    [TestMethod]
    [Description("Captures issues-collapsed-1440x900: the issues-500 scenario with the dock collapsed to its header")]
    public async Task Capture_IssuesCollapsed()
    {
        var page = await OpenThemedModelingPageAsync("?scenario=issues-500", "light", 1440, 900);
        try
        {
            await WaitForLoadedStateAsync(page);
            await page.Locator("[data-testid='modeling-issues-toggle']").ClickAsync(new LocatorClickOptions { Timeout = 10000 });
            await page.Locator("[data-testid='modeling-issue-panel'] .tm-modeling-issue-panel__content")
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10000 });
            await SettleAsync(page);
            await SaveViewportScreenshotAsync(page, "issues-collapsed-1440x900");
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("Captures the selected-element state (tree selection + inspector detail) at 1440x900")]
    public async Task Capture_ElementSelected()
    {
        var page = await OpenThemedModelingPageAsync(string.Empty, "light", 1440, 900);
        try
        {
            await WaitForLoadedStateAsync(page);
            await page.Locator("[data-testid='modeling-tree-node-bpmn-validate-order']")
                .ClickAsync(new LocatorClickOptions { Timeout = 10000 });
            await page.Locator("[data-testid='modeling-editor'][data-selected-element-id='bpmn-validate-order']")
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
            await SettleAsync(page);
            await SaveViewportScreenshotAsync(page, "element-selected-1440x900");
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    /// <summary>Fáze 6: the tablet band of the shell workspace — 1024x768 and 1180x820 with
    /// touch emulation, light and dark, the loaded workspace and the open-editor dialog (whose
    /// body resolves the diagram editor's tablet layout). The demo page hides its sidebar from
    /// 1024px of viewport up, so both windows land the editor in the 768–1199 container band.</summary>
    [TestMethod]
    [DataRow("light", 1024, 768)]
    [DataRow("dark", 1024, 768)]
    [DataRow("light", 1180, 820)]
    [DataRow("dark", 1180, 820)]
    [Description("Captures default / issues-mixed / stale-source at 1024x768 and 1180x820 (touch) for light and dark")]
    public async Task Capture_ThemedTablet(string theme, int width, int height)
    {
        foreach (var (name, query) in new[]
                 {
                     (Name: "default", Query: string.Empty),
                     (Name: "issues-mixed", Query: "?scenario=issues-mixed"),
                     (Name: "stale-source", Query: "?fresh=false")
                 })
        {
            var page = await OpenThemedTouchModelingPageAsync(query, theme, width, height);
            try
            {
                await WaitForLoadedStateAsync(page);
                await SettleAsync(page);
                await SaveViewportScreenshotAsync(page, $"{name}-tablet-{theme}-{width}x{height}");
            }
            finally
            {
                await page.Context.CloseAsync();
            }
        }
    }

    /// <summary>Fáze 6: the open-editor dialog on the tablet band — the ~90% TmModal with the
    /// diagram editor's tablet layout (one docked panel expanded + the floating minimap) in the
    /// body, light and dark at both tablet viewports.</summary>
    [TestMethod]
    [DataRow("light", 1024, 768)]
    [DataRow("dark", 1024, 768)]
    [DataRow("light", 1180, 820)]
    [DataRow("dark", 1180, 820)]
    [Description("Captures the open 'Open in editor' dialog at the tablet band viewports (touch) in light and dark")]
    public async Task Capture_OpenInEditorDialog_ThemedTablet(string theme, int width, int height)
    {
        var page = await OpenThemedTouchModelingPageAsync(string.Empty, theme, width, height);
        try
        {
            await WaitForLoadedStateAsync(page);
            // Fáze 6 review fix: the tablet sheets start CLOSED — no close-first workaround.
            await page.Locator("[data-testid='modeling-open-in-editor-button']").TapAsync();
            await page.Locator("[data-testid='modeling-open-diagram-editor'] .tm-modal")
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
            await page.Locator("[data-testid='modeling-open-diagram-editor'] .tm-diagram-editor[data-editor-layout='tablet']")
                .First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 15000 });
            await SettleAsync(page);
            await SaveViewportScreenshotAsync(page, $"open-editor-dialog-tablet-{theme}-{width}x{height}");
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    /// <summary>Fáze 6: the Source tab as the tablet SHEET presents it (the wide-panel container
    /// layout: three metadata columns, the load button in the header) — light and dark at 1024.</summary>
    [TestMethod]
    [DataRow("light", 1024, 768)]
    [DataRow("dark", 1024, 768)]
    [Description("Captures source-sheet-tablet-{theme}-1024x768: the inspector sheet open on the Source tab")]
    public async Task Capture_SourceSheet_Tablet(string theme, int width, int height)
    {
        var page = await OpenThemedTouchModelingPageAsync("?fresh=false", theme, width, height);
        try
        {
            await WaitForLoadedStateAsync(page);
            // Fáze 6 review fix: the sheets start closed — open the inspector sheet, then the
            // Source tab inside it.
            await page.Locator("[data-testid='modeling-panel-tab-inspector']").TapAsync();
            await page.Locator("[data-testid='modeling-inspector-sheet'] [data-testid='modeling-tab-source']")
                .ClickAsync(new LocatorClickOptions { Timeout = 10000 });
            await page.Locator("[data-testid='modeling-source-panel']")
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
            await SettleAsync(page);
            await SaveViewportScreenshotAsync(page, $"source-sheet-tablet-{theme}-{width}x{height}");
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    /// <summary>Theme setup shared with <c>ModelingAccessibilityM25E2ETests</c>: the dark theme is
    /// applied by CLICKING the demo's ThemeToggle — the demo's ThemeService owns the
    /// <c>.demo-shell</c> <c>data-theme</c> attribute and the Tailwind <c>dark</c> class, so
    /// stamping <c>&lt;html&gt;</c> was never enough: <c>querySelector('[data-theme]')</code>
    /// returned the <c>&lt;html&gt;</c> element we had just stamped and <c>.demo-shell</c> stayed
    /// LIGHT (the dark baselines were mislabeled light shots). The indigo accent is the demo's
    /// colour theme (localStorage `tm-demo-color-theme=indigo`, consumed by the demo's
    /// color-theme.js before Blazor boots), seeded through the context init script.</summary>
    private async Task<IPage> OpenThemedModelingPageAsync(string query, string theme, int width, int height)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = false,
            IsMobile = false,
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        var init = "localStorage.setItem('tm-demo-culture', 'en');";
        if (theme.Contains("indigo", StringComparison.Ordinal))
        {
            init += "localStorage.setItem('tm-demo-color-theme', 'indigo');";
        }

        await context.AddInitScriptAsync(init);

        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}{ModelingEditorUrl}{query}");
        await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = DisableMotionStyle });

        if (theme.Contains("dark", StringComparison.Ordinal))
        {
            // The desktop toggle lives in the fixed sidebar — which the /modeling-editor demo page
            // HIDES from 1264px of viewport up (the editor needs the full width for the 1200px
            // desktop dock). Dispatch the click on the hidden toggle (the wireframe phase-2
            // pattern); a visible toggle is clicked the normal way.
            var toggle = page.Locator("aside [data-testid='theme-toggle']");
            if (await toggle.CountAsync() > 0)
            {
                await toggle.First.DispatchEventAsync("click");
            }

            try
            {
                await page.Locator(".demo-shell[data-theme='dark']")
                    .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 5000 });
            }
            catch (TimeoutException)
            {
                // Fallback for a lagging circuit: force every data-theme scope dark, including the
                // document element the tm-* dark token sheets key off.
                await page.EvaluateAsync(
                    """
                    () => {
                        document.querySelectorAll('[data-theme]').forEach((el) => el.setAttribute('data-theme', 'dark'));
                        document.querySelector('.demo-shell')?.classList.add('dark');
                        document.documentElement.setAttribute('data-theme', 'dark');
                        document.documentElement.classList.add('tm-dark', 'dark');
                        document.body.classList.add('tm-dark', 'dark');
                    }
                    """);
            }

            await page.WaitForTimeoutAsync(300);
        }

        return page;
    }

    /// <summary>The themed setup for the TABLET band (Fáze 6): the same theme flow as
    /// <see cref="OpenThemedModelingPageAsync"/> but with touch emulation on, so the coarse
    /// pointer styles and the sheet toggles render exactly as a tablet user sees them.</summary>
    private async Task<IPage> OpenThemedTouchModelingPageAsync(string query, string theme, int width, int height)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = true,
            IsMobile = false,
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        var init = "localStorage.setItem('tm-demo-culture', 'en');";
        if (theme.Contains("indigo", StringComparison.Ordinal))
        {
            init += "localStorage.setItem('tm-demo-color-theme', 'indigo');";
        }

        await context.AddInitScriptAsync(init);

        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}{ModelingEditorUrl}{query}");
        await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = DisableMotionStyle });

        if (theme.Contains("dark", StringComparison.Ordinal))
        {
            // The tablet band hides the demo sidebar (from 1024px of viewport up), so the theme
            // toggle inside it is display:none — dispatch the click (the wireframe phase-2
            // pattern) instead of pointing a cursor at it.
            var toggle = page.Locator("aside [data-testid='theme-toggle']");
            if (await toggle.CountAsync() > 0)
            {
                await toggle.First.DispatchEventAsync("click");
            }

            try
            {
                await page.Locator(".demo-shell[data-theme='dark']")
                    .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 5000 });
            }
            catch (TimeoutException)
            {
                // Fallback for a lagging circuit: force every data-theme scope dark, including the
                // document element the tm-* dark token sheets key off.
                await page.EvaluateAsync(
                    """
                    () => {
                        document.querySelectorAll('[data-theme]').forEach((el) => el.setAttribute('data-theme', 'dark'));
                        document.querySelector('.demo-shell')?.classList.add('dark');
                        document.documentElement.setAttribute('data-theme', 'dark');
                        document.documentElement.classList.add('tm-dark', 'dark');
                        document.body.classList.add('tm-dark', 'dark');
                    }
                    """);
            }

            await page.WaitForTimeoutAsync(300);
        }

        return page;
    }

    private static readonly (int Width, int Height, bool Touch)[] BaselineViewports =
    [
        (1440, 900, false),
        (1024, 768, true),
        (390, 844, true),
        (320, 640, true),
    ];

    private async Task CaptureAsync(string name, string query, bool requiresLoadedState, string? expectedState = null, bool withIssuePanelShots = false, bool inspectorTabOnMobile = false)
    {
        foreach (var (width, height, touch) in BaselineViewports)
        {
            var page = await OpenModelingPageAsync(query, width, height, touch);
            try
            {
                if (expectedState is not null)
                {
                    await page.Locator($"[data-testid='modeling-editor'][data-state='{expectedState}']")
                        .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
                }
                else if (requiresLoadedState)
                {
                    await WaitForLoadedStateAsync(page);
                }

                // Phase 5 (MO1): the compact layout (the editor container below its own mobile
                // threshold — the phones here) keeps the issue and source panels behind the
                // "Inspector" panel tab. Fáze 6: on the tablet band the stale SOURCE panel is
                // reached through the inspector sheet's Source tab; the issue panel is the
                // always-visible dock row under the preview.
                if (inspectorTabOnMobile)
                {
                    if (await IsCompactLayoutAsync(page))
                    {
                        await OpenMobileInspectorTabAsync(page, waitForIssuePanel: false);
                    }
                    else if (await IsTabletLayoutAsync(page))
                    {
                        await OpenTabletSourceTabAsync(page);
                    }
                }

                if (withIssuePanelShots)
                {
                    if (await IsCompactLayoutAsync(page))
                    {
                        await OpenMobileInspectorTabAsync(page, waitForIssuePanel: true);
                    }
                    else if (await IsTabletLayoutAsync(page))
                    {
                        // Fáze 6 review fix: the tablet band starts with the dock COLLAPSED —
                        // expand it first, or the rows are hidden behind the dock header.
                        await page.Locator("[data-testid='modeling-issues-toggle']")
                            .TapAsync(new LocatorTapOptions { Timeout = 10000 });
                        await page.WaitForFunctionAsync(
                            "() => !document.querySelector(\"[data-testid='modeling-issue-panel'] .tm-modeling-issue-panel__content\")?.hasAttribute('hidden')",
                            null,
                            new PageWaitForFunctionOptions { Timeout = 10_000 });
                    }

                    // Scroll the sourceless orphan row into view so the panel element shot and the
                    // viewport shot both contain the non-interactive issue row (index 3).
                    await page.Locator("[data-testid='modeling-issue-3']")
                        .ScrollIntoViewIfNeededAsync(new LocatorScrollIntoViewIfNeededOptions { Timeout = 10000 });
                }

                await SettleAsync(page);
                await SaveViewportScreenshotAsync(page, $"{name}-{width}x{height}");

                if (withIssuePanelShots)
                {
                    await SettleAsync(page);
                    await page.Locator("[data-testid='modeling-issue-panel']").ScreenshotAsync(
                        new LocatorScreenshotOptions
                        {
                            Path = Path.Combine(OutputDirectory, $"issues-mixed-panel-{width}x{height}.png"),
                            Timeout = 30_000
                        });
                }
            }
            finally
            {
                await page.Context.CloseAsync();
            }
        }
    }

    /// <summary>Whether the COMPACT panel-tab workspace rendered. Fáze 6: the tab row also
    /// renders on the tablet band (as the visibility switch), so the compact check reads the
    /// resolved layout instead of the tab's existence.</summary>
    private static async Task<bool> IsCompactLayoutAsync(IPage page)
        => await page.EvaluateAsync<bool>(
            "() => document.querySelector('.tm-modeling-editor .tm-layout')?.getAttribute('data-layout') === 'mobile'");

    /// <summary>Whether the editor resolved the tablet band (the shell's overlay presentation).</summary>
    private static Task<bool> IsTabletLayoutAsync(IPage page) => page.EvaluateAsync<bool>(
        "() => document.querySelector('.tm-modeling-editor .tm-layout')?.getAttribute('data-layout') === 'tablet'");

    /// <summary>
    /// Fáze 6 review: the tablet tree drawer baseline — the drawer OPEN (tap the Tree tab; the
    /// sheets start closed, so this is the tablet band's only tree state) in light and dark at
    /// both tablet viewports. The zoom label is polled to a STABLE value first: the canvas' fit
    /// settles asynchronously, and a light/dark capture pair taken on different fit states
    /// differs in the diagram's rendered size.
    /// </summary>
    [TestMethod]
    [DataRow("light", 1024, 768)]
    [DataRow("dark", 1024, 768)]
    [DataRow("light", 1180, 820)]
    [DataRow("dark", 1180, 820)]
    [Description("Captures tree-drawer-tablet-{theme}-{w}x{h}: the tree drawer open on the tablet band (touch)")]
    public async Task Capture_TreeDrawer_Tablet(string theme, int width, int height)
    {
        var page = await OpenThemedTouchModelingPageAsync(string.Empty, theme, width, height);
        try
        {
            await WaitForLoadedStateAsync(page);
            await page.Locator("[data-testid='modeling-panel-tab-tree']").TapAsync();
            await page.Locator("[data-testid='modeling-tree-drawer']")
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
            await WaitForStableZoomLabelAsync(page);
            await SettleAsync(page);
            await SaveViewportScreenshotAsync(page, $"tree-drawer-tablet-{theme}-{width}x{height}");
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    /// <summary>
    /// Waits until the embedded diagram editor's zoom label stops changing (two identical
    /// samples ~250ms apart) — the JS fit pass is asynchronous, so a plain "nodes rendered"
    /// wait still races it. Gives up after ~8s and lets the capture proceed (the zoom label
    /// may legitimately be absent in a future chrome).
    /// </summary>
    private static async Task WaitForStableZoomLabelAsync(IPage page)
    {
        var deadline = DateTime.UtcNow.AddSeconds(8);
        var last = await ReadZoomLabelAsync(page);
        while (DateTime.UtcNow < deadline)
        {
            await page.WaitForTimeoutAsync(250);
            var current = await ReadZoomLabelAsync(page);
            if (!string.IsNullOrEmpty(current) && current == last)
            {
                return;
            }

            last = current;
        }
    }

    private static Task<string?> ReadZoomLabelAsync(IPage page) => page.EvaluateAsync<string?>(
        "() => document.querySelector('.tm-modeling-diagram-preview .tm-diagram-editor__zoom-label')?.textContent?.trim() ?? null");

    /// <summary>
    /// Opens the compact layout's "Inspector" panel tab. When <paramref name="waitForIssuePanel"/>
    /// is set the issue panel must be visible afterwards, otherwise the source panel (stale badge).
    /// On the tablet band the stale SOURCE panel is reached through the inspector sheet's own
    /// Source tab instead — handled by <see cref="OpenTabletSourceTabAsync"/>.
    /// </summary>
    private static async Task OpenMobileInspectorTabAsync(IPage page, bool waitForIssuePanel)
    {
        await page.Locator("[data-testid='modeling-panel-tab-inspector']")
            .ClickAsync(new LocatorClickOptions { Timeout = 10000 });
        await page.Locator(waitForIssuePanel ? "[data-testid='modeling-issue-panel']" : "[data-testid='modeling-source-panel']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
    }

    /// <summary>Fáze 6 (review fix): the source panel lives in the inspector SHEET — the sheets
    /// start closed, so open the sheet first, then select its Source tab.</summary>
    private static async Task OpenTabletSourceTabAsync(IPage page)
    {
        await page.Locator("[data-testid='modeling-panel-tab-inspector']")
            .TapAsync(new LocatorTapOptions { Timeout = 10000 });
        await page.Locator("[data-testid='modeling-inspector-sheet'] [data-testid='modeling-tab-source']")
            .ClickAsync(new LocatorClickOptions { Timeout = 10000 });
        await page.Locator("[data-testid='modeling-source-panel']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
    }

    /// <summary>Loaded editor AND at least one rendered diagram node — no fixed sleep.</summary>
    private static async Task WaitForLoadedStateAsync(IPage page)
    {
        await page.Locator("[data-testid='modeling-editor'][data-state='loaded']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.tm-diagram-node').length > 0",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });
    }

    /// <summary>
    /// Deterministic settle: fonts loaded, then TWO animation frames (layout + paint commit) —
    /// replaces the former fixed WaitForTimeout sleeps that flaked on slow CI machines.
    /// </summary>
    private static Task SettleAsync(IPage page) => page.EvaluateAsync(
        "async () => { await document.fonts.ready; await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r))); }");

    private async Task<IPage> OpenModelingPageAsync(string query, int width, int height, bool touch)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = touch,
            IsMobile = touch && width < 640,
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");

        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}{ModelingEditorUrl}{query}");
        await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = DisableMotionStyle });
        return page;
    }

    private static async Task SaveViewportScreenshotAsync(IPage page, string fileName)
    {
        // A viewport capture keeps the touch emulation honest; FullPage would reset it.
        var path = Path.Combine(OutputDirectory, fileName + ".png");
        await page.Mouse.MoveAsync(0, 0);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = path,
            FullPage = false,
            Timeout = 30_000
        });
        Assert.IsTrue(new FileInfo(path).Length > 10_000, $"{fileName} should contain the rendered modeling screen.");
    }
}
