using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>Phase 1 browser spike for the clean-room canvas document engine.</summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasEnginePhase1E2ETests : WasmTestBase
{
    private const string HarnessUrl = "/canvas-engine-harness.html";

    [TestMethod]
    public async Task Phase1_CanvasEngineHarness_RendersIntentionalEmptyA4Page()
    {
        var page = await OpenHarnessAsync(1440, 1000);
        var probe = await CaptureCanvasProbeAsync(page);

        Assert.IsTrue(probe.Ready, "Canvas engine harness must report readiness.");
        Assert.AreEqual("CanvasDocumentEngine", probe.ArchitectureName);
        Assert.AreEqual("canvas-per-visible-page", probe.PageSurfaceStrategy);
        Assert.AreEqual(6, probe.LayerCount, "Phase 1 must render the documented canvas layer stack.");
        Assert.AreEqual(1, probe.PageCount);
        Assert.IsTrue(probe.BackgroundNonBlankPixels > 0, "The empty page background canvas must contain real painted pixels.");
        Assert.IsTrue(probe.BackgroundDistinctSampleCount > 1, "The empty page must include page and margin/border pixels, not a blank single-color canvas.");
        Assert.IsTrue(probe.DevicePixelRatio >= 1);
        Assert.IsTrue(probe.CanvasWidth >= Math.Round(794 * probe.DevicePixelRatio) - 1);
        Assert.IsTrue(probe.CanvasHeight >= Math.Round(1123 * probe.DevicePixelRatio) - 1);
        Assert.AreEqual(0, probe.ContentEditableCount, "The canvas engine must not use contenteditable as input authority.");
        Assert.IsTrue(probe.HasAccessibilityMirror, "Canvas rendering must be paired with an accessibility mirror.");
        Assert.IsTrue(probe.HasHiddenInputBridge, "Keyboard/IME input must have a hidden input bridge.");

        var output = CreateOutputDirectory("desktop-1440x1000");
        var fullPath = Path.Combine(output, "00-phase1-empty-a4-full.png");
        var editorPath = Path.Combine(output, "01-phase1-empty-a4-engine.png");
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = fullPath,
            FullPage = true,
            Type = ScreenshotType.Png
        });
        await page.GetByTestId("document-canvas-engine-root").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = editorPath,
            Type = ScreenshotType.Png
        });

        var manifestPath = Path.Combine(output, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            testName = nameof(Phase1_CanvasEngineHarness_RendersIntentionalEmptyA4Page),
            viewport = "desktop-1440x1000",
            seedDocumentId = "phase-1-empty-a4",
            userActions = new[]
            {
                "Open the static canvas-engine-harness page.",
                "Let createCanvasDocumentEngine mount and render an empty A4 page.",
                "Probe canvas pixels and save full/engine screenshots."
            },
            expectedVisibleChanges = "A single clean A4-like document page is visible on a quiet workspace.",
            expectedModelChanges = "None.",
            screenshotPaths = new[] { fullPath, editorPath },
            canvasNonBlankMetrics = new
            {
                probe.BackgroundNonBlankPixels,
                probe.BackgroundDistinctSampleCount,
                probe.CanvasWidth,
                probe.CanvasHeight,
                probe.DevicePixelRatio
            },
            overlapChecks = "No toolbar, side panel, text, object, or annotation UI exists in phase 1; the page surface is isolated.",
            uxUiReviewerNotes = "The screenshot should read as a deliberate blank document page: centered, sharp, quiet, and not a debug canvas.",
            probe
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        TestContext.AddResultFile(fullPath);
        TestContext.AddResultFile(editorPath);
        TestContext.AddResultFile(manifestPath);
    }

    private async Task<IPage> OpenHarnessAsync(int width, int height)
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, height);
        await GotoWithoutBootRecoveryAsync(page, $"{BaseUrl}{HarnessUrl}", "static canvas-engine-harness.html is not the Blazor app; there is no data-blazor-ready boot to recover");
        await page.WaitForFunctionAsync(
            "() => window.__canvasDocumentEngineHarness && window.__canvasDocumentEngineHarness.ready === true",
            null,
            new PageWaitForFunctionOptions { Timeout = 30_000 });
        return page;
    }

    private static Task<CanvasPhase1Probe> CaptureCanvasProbeAsync(IPage page)
        => page.EvaluateAsync<CanvasPhase1Probe>(
            """
            () => {
                const harness = window.__canvasDocumentEngineHarness;
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                const pageNode = document.querySelector('[data-testid="document-canvas-page"]');
                const background = document.querySelector('[data-canvas-layer="page-background"]');
                const layers = Array.from(document.querySelectorAll('[data-canvas-layer]'));
                const mirror = document.querySelector('[data-testid="document-canvas-a11y-mirror"]');
                const input = document.querySelector('[data-testid="document-canvas-hidden-input"]');
                const sample = sampleCanvas(background);
                return {
                    ready: !!harness?.ready,
                    architectureName: harness?.result?.architecture?.name || '',
                    pageSurfaceStrategy: harness?.result?.architecture?.pageSurfaceStrategy || '',
                    layerCount: layers.length,
                    pageCount: document.querySelectorAll('[data-testid="document-canvas-page"]').length,
                    hasAccessibilityMirror: !!mirror,
                    hasHiddenInputBridge: !!input && input.tagName === 'TEXTAREA',
                    contentEditableCount: document.querySelectorAll('[contenteditable="true"]').length,
                    canvasWidth: background?.width || 0,
                    canvasHeight: background?.height || 0,
                    cssWidth: pageNode ? pageNode.getBoundingClientRect().width : 0,
                    cssHeight: pageNode ? pageNode.getBoundingClientRect().height : 0,
                    devicePixelRatio: window.devicePixelRatio || 1,
                    backgroundNonBlankPixels: sample.nonBlankPixels,
                    backgroundDistinctSampleCount: sample.distinctColors,
                    rootWidth: root ? root.getBoundingClientRect().width : 0,
                    rootHeight: root ? root.getBoundingClientRect().height : 0
                };

                function sampleCanvas(canvas) {
                    if (!canvas) return { nonBlankPixels: 0, distinctColors: 0 };
                    const ctx = canvas.getContext('2d');
                    const width = canvas.width;
                    const height = canvas.height;
                    const cssWidth = parseFloat(canvas.style.width || '794') || 794;
                    const cssHeight = parseFloat(canvas.style.height || '1123') || 1123;
                    const dprX = width / cssWidth;
                    const dprY = height / cssHeight;
                    const cssPoint = (x, y) => [
                        Math.max(0, Math.min(width - 1, Math.round(x * dprX))),
                        Math.max(0, Math.min(height - 1, Math.round(y * dprY)))
                    ];
                    const points = [
                        cssPoint(cssWidth / 2, cssHeight / 2),
                        cssPoint(0.5, 0.5),
                        cssPoint(72.5, 72.5),
                        cssPoint(cssWidth - 1, cssHeight - 1),
                        cssPoint(cssWidth / 2, 72.5),
                        cssPoint(72.5, cssHeight / 2)
                    ];
                    const colors = new Set();
                    let nonBlankPixels = 0;
                    for (const [x, y] of points) {
                        const data = ctx.getImageData(x, y, 1, 1).data;
                        const key = `${data[0]},${data[1]},${data[2]},${data[3]}`;
                        colors.add(key);
                        if (data[3] > 0) {
                            nonBlankPixels++;
                        }
                    }
                    return { nonBlankPixels, distinctColors: colors.size };
                }
            }
            """);

    private static string CreateOutputDirectory(string viewport)
    {
        var root = FindRepositoryRoot();
        var output = Path.Combine(
            root.FullName,
            "tests",
            "Tempo.Blazor.E2E",
            "TestResults",
            "document-editor-canvas",
            "phase1-spike",
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

    private sealed class CanvasPhase1Probe
    {
        public bool Ready { get; set; }
        public string ArchitectureName { get; set; } = string.Empty;
        public string PageSurfaceStrategy { get; set; } = string.Empty;
        public int LayerCount { get; set; }
        public int PageCount { get; set; }
        public bool HasAccessibilityMirror { get; set; }
        public bool HasHiddenInputBridge { get; set; }
        public int ContentEditableCount { get; set; }
        public int CanvasWidth { get; set; }
        public int CanvasHeight { get; set; }
        public double CssWidth { get; set; }
        public double CssHeight { get; set; }
        public double DevicePixelRatio { get; set; }
        public int BackgroundNonBlankPixels { get; set; }
        public int BackgroundDistinctSampleCount { get; set; }
        public double RootWidth { get; set; }
        public double RootHeight { get; set; }
    }
}
