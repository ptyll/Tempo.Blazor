using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

    /// <summary>Baseline evidence for the canvas-based document editor direction.</summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasEngineBaselineE2ETests : WasmTestBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    [TestMethod]
    public async Task Baseline_CurrentCanvasEngine_CapturesDesktopScreenshots()
    {
        var page = await OpenBaselineDocumentEditorAsync("/document-editor?documentId=onlyoffice-parity-2026-05-24", 1440, 1000);
        var output = CreateOutputDirectory("desktop-1440x1000");
        var fullPath = Path.Combine(output, "00-current-canvas-full.png");
        var editorPath = Path.Combine(output, "01-current-canvas-editor.png");

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = fullPath,
            FullPage = true,
            Type = ScreenshotType.Png
        });
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = editorPath,
            Type = ScreenshotType.Png
        });

        var probe = await CaptureProbeAsync(page);
        Assert.AreEqual("CanvasEnginePreview", probe.RenderEngine, "The baseline route must capture the current canvas engine.");
        Assert.IsTrue(probe.HasCanvasHost, "The baseline route must render the canvas engine host.");
        Assert.IsTrue(new FileInfo(fullPath).Length > 10_000, "The full-page baseline screenshot must be a real non-empty PNG.");
        Assert.IsTrue(new FileInfo(editorPath).Length > 5_000, "The editor baseline screenshot must be a real non-empty PNG.");

        var manifestPath = Path.Combine(output, "manifest.json");
        await WriteManifestAsync(manifestPath, new
        {
            testName = nameof(Baseline_CurrentCanvasEngine_CapturesDesktopScreenshots),
            viewport = "desktop-1440x1000",
            seedDocumentId = "onlyoffice-parity-2026-05-24",
            userActions = new[]
            {
                "Open /document-editor with the deterministic ONLYOFFICE parity document.",
                "Wait for the current canvas engine host to be present.",
                "Capture the full page and editor surface as current canvas evidence."
            },
            expectedVisibleChanges = "No document mutation; this is the visual baseline for the current canvas engine.",
            expectedModelChanges = "None.",
            screenshotPaths = new[] { fullPath, editorPath },
            canvasNonBlankMetrics = "Captured by the canvas host screenshot gates in the dedicated canvas E2E tests.",
            overlapChecks = "Covered by the canvas visual assertion suite; this baseline records current visual output.",
            uxUiReviewerNotes = "Manual review of the saved screenshot should confirm the canvas-owned page, overlay, and side panel composition remains stable.",
            probe
        });
        TestContext.AddResultFile(fullPath);
        TestContext.AddResultFile(editorPath);
        TestContext.AddResultFile(manifestPath);
    }

    [TestMethod]
    public async Task CanvasEngineRouteFlag_RendersCanvasHost()
    {
        var page = await OpenBaselineDocumentEditorAsync("/document-editor?tmDocumentEditorEngine=canvas", 1280, 800);
        var probe = await CaptureProbeAsync(page);

        Assert.IsTrue(probe.HasCanvasHost, "The canvas engine route flag must render the canvas host.");
        Assert.AreEqual("CanvasEnginePreview", probe.RenderEngine, "The demo route must report the active canvas engine.");

        var output = CreateOutputDirectory("canvas-flag");
        var manifestPath = Path.Combine(output, "manifest.json");
        await WriteManifestAsync(manifestPath, new
        {
            testName = nameof(CanvasEngineRouteFlag_RendersCanvasHost),
            viewport = "notebook-1280x800",
            seedDocumentId = "contract-demo",
            userActions = new[]
            {
                "Open /document-editor?tmDocumentEditorEngine=canvas.",
                "Inspect the active document editor host and root render-engine attributes."
            },
            expectedVisibleChanges = "The canvas host appears and the render-engine attributes report CanvasEnginePreview.",
            expectedModelChanges = "None.",
            screenshotPaths = Array.Empty<string>(),
            canvasNonBlankMetrics = "Covered by the dedicated canvas host screenshot gate.",
            overlapChecks = "Covered by the dedicated canvas host screenshot gate.",
            uxUiReviewerNotes = "This verifies that the route flag remains wired to the canvas engine.",
            probe
        });
        TestContext.AddResultFile(manifestPath);
    }

    [TestMethod]
    public async Task CanvasEngineRouteFlag_RendersCanvasHostWhenEnabled()
    {
        var page = await OpenBaselineDocumentEditorAsync("/document-editor?tmDocumentEditorEngine=canvas", 1280, 800);

        await page.WaitForSelectorAsync("[data-testid='document-canvas-engine-host']", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10_000
        });
    }

    private async Task<IPage> OpenBaselineDocumentEditorAsync(string pathAndQuery, int width, int height)
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, height);
        await GotoAppReadyAsync(page, $"{BaseUrl}{pathAndQuery}", WaitUntilState.DOMContentLoaded);
        await page.WaitForSelectorAsync("[data-testid='document-editor-demo']", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Attached,
            Timeout = 60_000
        });
        await page.WaitForFunctionAsync(
            """
            () => {
                const editor = document.querySelector('[data-testid="document-editor-demo"]');
                if (!editor) return false;
                const canvas = document.querySelector('[data-testid="document-canvas-engine-host"]');
                return !!canvas;
            }
            """,
            null,
            new PageWaitForFunctionOptions { Timeout = 60_000 });
        await page.WaitForTimeoutAsync(500);
        return page;
    }

    private static Task<CanvasBaselineProbe> CaptureProbeAsync(IPage page)
        => page.EvaluateAsync<CanvasBaselineProbe>(
            """
            () => {
                const editor = document.querySelector('[data-testid="document-editor-demo"]');
                const root = editor?.closest('[data-render-engine]') || editor;
                const canvas = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const activeHost = canvas;
                const text = (activeHost?.innerText || activeHost?.textContent || '').replace(/\s+/g, ' ').trim();
                const rect = activeHost?.getBoundingClientRect();
                return {
                    url: location.href,
                    renderEngine: root?.getAttribute('data-render-engine') || '',
                    hasCanvasHost: !!canvas,
                    canvasElementCount: document.querySelectorAll('canvas').length,
                    activeHostWidth: rect?.width || 0,
                    activeHostHeight: rect?.height || 0,
                    visibleTextSample: text.slice(0, 500)
                };
            }
            """);

    private static async Task WriteManifestAsync(string path, object manifest)
    {
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(manifest, JsonOptions));
    }

    private static string CreateOutputDirectory(string viewport)
    {
        var root = FindRepositoryRoot();
        var output = Path.Combine(
            root.FullName,
            "tests",
            "Tempo.Blazor.E2E",
            "TestResults",
            "document-editor-canvas",
            "before-redesign",
            "2026-06-04",
            viewport);
        Directory.CreateDirectory(output);
        return output;
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
            {
                return current;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx from test output directory.");
    }

    private sealed class CanvasBaselineProbe
    {
        public string Url { get; set; } = string.Empty;
        public string RenderEngine { get; set; } = string.Empty;
        public bool HasCanvasHost { get; set; }
        public int CanvasElementCount { get; set; }
        public double ActiveHostWidth { get; set; }
        public double ActiveHostHeight { get; set; }
        public string VisibleTextSample { get; set; } = string.Empty;
    }
}
