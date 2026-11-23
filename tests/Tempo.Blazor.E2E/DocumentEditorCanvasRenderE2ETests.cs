using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tempo.Blazor.E2E.CanvasEngine;

namespace Tempo.Blazor.E2E;

/// <summary>Phase 5 E2E coverage for the canvas display-list and renderer pipeline.</summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasRenderE2ETests : WasmTestBase
{
    [TestMethod]
    public async Task Phase5_CanvasRenderer_PaintsDocumentTextAndKeepsDiagnosticsOffByDefault()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenRenderDocumentAsync(page);

        var probe = await ReadRenderProbeAsync(page);
        Assert.AreEqual("CanvasEnginePreview", probe.RenderEngine);
        Assert.IsTrue(probe.HostReady);
        Assert.AreEqual(6, probe.LayerCount);
        Assert.IsTrue(probe.RenderCommandCount >= 8, $"Expected display-list commands, actual: {probe.RenderCommandCount}.");
        Assert.IsTrue(probe.PaintedCommandCount >= 6, $"Expected painted commands, actual: {probe.PaintedCommandCount}.");
        Assert.IsTrue(probe.TextRunCount >= 4, $"Expected text runs, actual: {probe.TextRunCount}.");
        Assert.AreEqual(0, probe.DiagnosticCount);
        Assert.IsTrue(probe.PixelRatio >= 1);

        var backgroundMetrics = await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(page.Locator("[data-canvas-layer='page-background']").First);
        var contentMetrics = await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(page.Locator("[data-canvas-layer='content']").First);
        var diagnosticsMetrics = await DocumentEditorCanvasVisualAssert.ReadCanvasPixelMetricsAsync(page.Locator("[data-canvas-layer='diagnostics']").First);

        Assert.AreEqual(0, diagnosticsMetrics.NonTransparentPixels, "Diagnostics layer must stay empty unless debug rendering is enabled.");
        Assert.IsTrue(contentMetrics.MinX >= Math.Floor(72 * 96d / 72d * probe.PixelRatio) - 4, "Text pixels must start inside the page body margin.");
        Assert.IsTrue(contentMetrics.MinY >= Math.Floor(72 * 96d / 72d * probe.PixelRatio) - 4, "Text pixels must start below the top page margin.");
        Assert.IsTrue(contentMetrics.MaxX < backgroundMetrics.Width - 80 * probe.PixelRatio, "Text pixels must stay within the body width.");
        Assert.IsTrue(contentMetrics.MaxY < backgroundMetrics.Height - 80 * probe.PixelRatio, "Text pixels must stay within the body height.");
        Assert.IsTrue(contentMetrics.NonTransparentPixels < backgroundMetrics.NonTransparentPixels, "Content layer should not paint a full-page fill.");

        await page.AddStyleTagAsync(new PageAddStyleTagOptions
        {
            Content = """
            .sticky.top-0,
            .fixed.top-0 {
                visibility: hidden !important;
            }

            [data-testid='canvas-engine-host-page'] {
                padding-top: 0 !important;
            }
            """
        });

        var output = CreateOutputDirectory("desktop-1440x1000");
        var fullPath = Path.Combine(output, "00-phase5-full.png");
        var pagePath = Path.Combine(output, "01-phase5-canvas-page.png");
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = fullPath,
            FullPage = true,
            Type = ScreenshotType.Png
        });
        await page.GetByTestId("document-canvas-page").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = pagePath,
            Type = ScreenshotType.Png
        });

        var dprProbe = await RunDeviceScaleProbeAsync();
        Assert.IsTrue(dprProbe.PixelRatio >= 1.95, $"Expected DPR 2 backing store, actual: {dprProbe.PixelRatio}.");
        Assert.IsTrue(dprProbe.CanvasWidth >= 1580, $"Expected high-DPI backing width, actual: {dprProbe.CanvasWidth}.");
        Assert.IsTrue(dprProbe.TextRunCount >= 4, $"Expected text runs at DPR 2, actual: {dprProbe.TextRunCount}.");

        var manifestPath = Path.Combine(output, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            testName = nameof(Phase5_CanvasRenderer_PaintsDocumentTextAndKeepsDiagnosticsOffByDefault),
            viewport = "desktop-1440x1000",
            seedDocumentId = "phase-5-canvas-render",
            userActions = new[]
            {
                "Open /canvas-engine-host with the phase 5 render seed document.",
                "Wait for the canvas engine readiness signal.",
                "Read backing-store pixel metrics for page, content, and diagnostics canvas layers.",
                "Open the same document in a DPR 2 browser context and verify high-DPI canvas sizing."
            },
            expectedVisibleChanges = "The canvas host displays a document page with crisp heading and marked paragraph text painted into the content layer.",
            expectedModelChanges = "None.",
            screenshotPaths = new[] { fullPath, pagePath },
            backgroundMetrics,
            contentMetrics,
            diagnosticsMetrics,
            probe,
            dprProbe,
            uxReviewerNotes = "The canvas page should look like a production editor surface with visible text, clear page margins, and no diagnostic overlay."
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        TestContext.AddResultFile(fullPath);
        TestContext.AddResultFile(pagePath);
        TestContext.AddResultFile(manifestPath);
    }

    private async Task OpenRenderDocumentAsync(IPage page)
    {
        await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId=phase-5-canvas-render");
        await page.WaitForSelectorAsync(
            "[data-testid='document-canvas-engine-host'][data-canvas-engine-ready='true']",
            new PageWaitForSelectorOptions { Timeout = 30_000 });
        try
        {
            await WaitForPredicateAsync(page, """
                () => Number(document.querySelector('[data-testid="document-canvas-page"]')?.getAttribute('data-canvas-text-run-count') || '0') >= 4
                """, 30_000);
        }
        catch (TimeoutException)
        {
            var probe = await ReadRenderProbeAsync(page);
            var mirrorText = await page.Locator("[data-testid='document-canvas-a11y-mirror']").First.TextContentAsync();
            Assert.Fail($"Timed out waiting for canvas text runs. ModelDocumentId='{probe.ModelDocumentId}', ModelBlockCount={probe.ModelBlockCount}, RenderCommandCount={probe.RenderCommandCount}, TextRunCount={probe.TextRunCount}, MirrorText='{mirrorText}'.");
        }
    }

    private static Task<CanvasRenderProbe> ReadRenderProbeAsync(IPage page)
        => page.EvaluateAsync<CanvasRenderProbe>(
            """
            () => {
                const editor = document.querySelector('[data-testid="document-editor-demo"]');
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const pageElement = document.querySelector('[data-testid="document-canvas-page"]');
                const canvas = document.querySelector('[data-canvas-layer="page-background"]');
                const cssWidth = Number.parseFloat(canvas?.style.width || '0');
                return {
                    renderEngine: editor?.getAttribute('data-render-engine') || '',
                    hostReady: host?.getAttribute('data-canvas-engine-ready') === 'true',
                    layerCount: document.querySelectorAll('[data-canvas-layer]').length,
                    renderCommandCount: Number(pageElement?.getAttribute('data-canvas-render-command-count') || '0'),
                    paintedCommandCount: Number(pageElement?.getAttribute('data-canvas-painted-command-count') || '0'),
                    textRunCount: Number(pageElement?.getAttribute('data-canvas-text-run-count') || '0'),
                    diagnosticCount: Number(pageElement?.getAttribute('data-canvas-diagnostic-count') || '0'),
                    modelDocumentId: pageElement?.getAttribute('data-canvas-model-document-id') || '',
                    modelBlockCount: Number(pageElement?.getAttribute('data-canvas-model-block-count') || '0'),
                    pixelRatio: cssWidth > 0 ? (canvas?.width || 0) / cssWidth : 0
                };
            }
            """);

    private async Task<DeviceScaleProbe> RunDeviceScaleProbeAsync()
    {
        var context = await CreateContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1280, Height = 900 },
            DeviceScaleFactor = 2,
            Locale = "en-US",
            IgnoreHTTPSErrors = true
        });

        try
        {
            var page = await context.NewPageAsync();
            await OpenRenderDocumentAsync(page);
            return await page.EvaluateAsync<DeviceScaleProbe>(
                """
                () => {
                    const pageElement = document.querySelector('[data-testid="document-canvas-page"]');
                    const canvas = document.querySelector('[data-canvas-layer="page-background"]');
                    const cssWidth = Number.parseFloat(canvas?.style.width || '0');
                    return {
                        pixelRatio: cssWidth > 0 ? (canvas?.width || 0) / cssWidth : 0,
                        canvasWidth: canvas?.width || 0,
                        canvasHeight: canvas?.height || 0,
                        textRunCount: Number(pageElement?.getAttribute('data-canvas-text-run-count') || '0')
                    };
                }
                """);
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    private static string CreateOutputDirectory(string viewport)
    {
        var output = Path.Combine(
            FindRepositoryRoot().FullName,
            "tests",
            "Tempo.Blazor.E2E",
            "TestResults",
            "document-editor-canvas",
            "phase5-render",
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

    private sealed class CanvasRenderProbe
    {
        [JsonPropertyName("renderEngine")]
        public string RenderEngine { get; set; } = string.Empty;

        [JsonPropertyName("hostReady")]
        public bool HostReady { get; set; }

        [JsonPropertyName("layerCount")]
        public int LayerCount { get; set; }

        [JsonPropertyName("renderCommandCount")]
        public int RenderCommandCount { get; set; }

        [JsonPropertyName("paintedCommandCount")]
        public int PaintedCommandCount { get; set; }

        [JsonPropertyName("textRunCount")]
        public int TextRunCount { get; set; }

        [JsonPropertyName("diagnosticCount")]
        public int DiagnosticCount { get; set; }

        [JsonPropertyName("pixelRatio")]
        public double PixelRatio { get; set; }

        [JsonPropertyName("modelDocumentId")]
        public string ModelDocumentId { get; set; } = string.Empty;

        [JsonPropertyName("modelBlockCount")]
        public int ModelBlockCount { get; set; }
    }

    private sealed class DeviceScaleProbe
    {
        [JsonPropertyName("pixelRatio")]
        public double PixelRatio { get; set; }

        [JsonPropertyName("canvasWidth")]
        public int CanvasWidth { get; set; }

        [JsonPropertyName("canvasHeight")]
        public int CanvasHeight { get; set; }

        [JsonPropertyName("textRunCount")]
        public int TextRunCount { get; set; }
    }
}
