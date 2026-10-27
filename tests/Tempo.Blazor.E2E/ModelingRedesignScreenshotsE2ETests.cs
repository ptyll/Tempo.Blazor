using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Baseline ("before") screenshot capture for the Modeling redesign plan — phase 1. It freezes the
/// CURRENT appearance of the modeling editor screens BEFORE any visual phase touches them, so the
/// later phases can diff against it: the layout must not change in this phase (G3 fixes only).
/// <para>
/// Scenarios: default workspace, mixed issues, stale source, the open "Open in editor" dialog,
/// the empty state and the error state. Viewports: 1440×900 (desktop), 1024×768 (tablet,
/// <c>HasTouch</c>), 390×844 and 320×640 (mobile). Output:
/// <c>tests/Tempo.Blazor.E2E/__screenshots__/modeling/</c> — committed as the "before" baseline.
/// Not part of the Smoke lane.
/// </para>
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class ModelingRedesignScreenshotsE2ETests : WasmTestBase
{
    private const string ModelingEditorUrl = "/modeling-editor";

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
    [Description("Captures the mixed-issue workspace (including a sourceless issue) at all four baseline viewports")]
    public async Task Capture_IssuesMixed()
        => await CaptureAsync("issues-mixed", "?scenario=issues-mixed", requiresLoadedState: true);

    [TestMethod]
    [Description("Captures the stale-source workspace at all four baseline viewports")]
    public async Task Capture_StaleSource()
        => await CaptureAsync("stale-source", "?fresh=false", requiresLoadedState: true);

    [TestMethod]
    [Description("Captures the open 'Open in editor' dialog at desktop, tablet and phone viewports")]
    public async Task Capture_OpenInEditorDialog()
    {
        foreach (var (width, height, touch) in BaselineViewports)
        {
            var page = await OpenModelingPageAsync(string.Empty, width, height, touch);
            try
            {
                await page.Locator("[data-testid='modeling-open-in-editor-button']").ClickAsync();
                await page.Locator("[data-testid='modeling-open-diagram-editor']")
                    .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
                await page.WaitForTimeoutAsync(500);
                await SaveViewportScreenshotAsync(page, $"open-editor-dialog-{width}x{height}");
            }
            finally
            {
                await page.Context.CloseAsync();
            }
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

    private static readonly (int Width, int Height, bool Touch)[] BaselineViewports =
    [
        (1440, 900, false),
        (1024, 768, true),
        (390, 844, true),
        (320, 640, true),
    ];

    private async Task CaptureAsync(string name, string query, bool requiresLoadedState, string? expectedState = null)
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
                    await page.Locator("[data-testid='modeling-editor'][data-state='loaded']")
                        .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
                }

                await page.WaitForTimeoutAsync(400);
                await SaveViewportScreenshotAsync(page, $"{name}-{width}x{height}");
            }
            finally
            {
                await page.Context.CloseAsync();
            }
        }
    }

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
