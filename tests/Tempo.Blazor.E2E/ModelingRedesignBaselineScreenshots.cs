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
            // The desktop toggle lives in the fixed sidebar (the DOM-first one is the mobile
            // header's, hidden at ≥1024px).
            var toggle = page.Locator("aside [data-testid='theme-toggle']");
            if (await toggle.CountAsync() > 0)
            {
                await toggle.First.ClickAsync(new LocatorClickOptions { Timeout = 10000 });
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

                // Below 640 px the issue panel and the source panel live behind the mobile
                // "Inspector" panel tab — the baseline must show the screen a user reaches them
                // through (otherwise the mobile stale-source shot is indistinguishable from default).
                if (inspectorTabOnMobile && width < 640)
                {
                    await OpenMobileInspectorTabAsync(page, waitForIssuePanel: false);
                }

                if (withIssuePanelShots)
                {
                    if (width < 640)
                    {
                        await OpenMobileInspectorTabAsync(page, waitForIssuePanel: true);
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

    /// <summary>
    /// Opens the mobile-only "Inspector" panel tab. When <paramref name="waitForIssuePanel"/> is
    /// set the issue panel must be visible afterwards, otherwise the source panel (stale badge).
    /// </summary>
    private static async Task OpenMobileInspectorTabAsync(IPage page, bool waitForIssuePanel)
    {
        await page.Locator("[data-testid='modeling-panel-tab-inspector']")
            .ClickAsync(new LocatorClickOptions { Timeout = 10000 });
        await page.Locator(waitForIssuePanel ? "[data-testid='modeling-issue-panel']" : "[data-testid='modeling-source-panel']")
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
