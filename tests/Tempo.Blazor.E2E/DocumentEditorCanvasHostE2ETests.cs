using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tempo.Blazor.E2E.CanvasEngine;

namespace Tempo.Blazor.E2E;

/// <summary>Phase 3 E2E coverage for the Blazor canvas engine host and render flag.</summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasHostE2ETests : WasmTestBase
{
    [TestMethod]
    public async Task Phase3_CanvasEngineHostRoute_RendersCanvasHostAndPassesScreenshotGate()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId=phase-3-canvas-empty");
        await page.WaitForSelectorAsync(
            "[data-testid='document-canvas-engine-host'][data-canvas-engine-ready='true']",
            new PageWaitForSelectorOptions { Timeout = 30_000 });

        var probe = await page.EvaluateAsync<CanvasHostProbe>(
            """
            () => {
                const editor = document.querySelector('[data-testid="document-editor-demo"]');
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                return {
                    renderEngine: editor?.getAttribute('data-render-engine') || '',
                    canvasHostReady: host?.getAttribute('data-canvas-engine-ready') === 'true',
                    canvasLayerCount: document.querySelectorAll('[data-canvas-layer]').length,
                    wysiwygHostCount: document.querySelectorAll('[data-testid="document-wysiwyg-host"]').length,
                    coreHostCount: document.querySelectorAll('[data-testid="document-core-engine-host"]').length,
                    a11yMirrorCount: document.querySelectorAll('[data-testid="document-canvas-a11y-mirror"]').length,
                    hiddenInputCount: document.querySelectorAll('[data-testid="document-canvas-hidden-input"]').length
                };
            }
            """);

        Assert.AreEqual("CanvasEnginePreview", probe.RenderEngine);
        Assert.IsTrue(probe.CanvasHostReady);
        Assert.AreEqual(6, probe.CanvasLayerCount);
        Assert.AreEqual(0, probe.WysiwygHostCount);
        Assert.AreEqual(0, probe.CoreHostCount);
        Assert.AreEqual(1, probe.A11yMirrorCount);
        Assert.AreEqual(1, probe.HiddenInputCount);

        var metrics = await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(page.Locator("[data-canvas-layer='page-background']").First);
        await DocumentEditorCanvasVisualAssert.AssertNoTextOverlapAsync(page);
        await DocumentEditorCanvasVisualAssert.AssertNoUiOverlapAsync(page);

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

        var output = CreateOutputDirectory();
        var fullPath = Path.Combine(output, "00-phase3-full.png");
        var editorPath = Path.Combine(output, "01-phase3-editor.png");
        var hostPath = Path.Combine(output, "02-phase3-canvas-host.png");
        var pagePath = Path.Combine(output, "03-phase3-canvas-page.png");
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
        await page.GetByTestId("document-canvas-engine-host").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = hostPath,
            Type = ScreenshotType.Png
        });
        await page.GetByTestId("document-canvas-page").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = pagePath,
            Type = ScreenshotType.Png
        });

        var manifestPath = Path.Combine(output, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            testName = nameof(Phase3_CanvasEngineHostRoute_RendersCanvasHostAndPassesScreenshotGate),
            viewport = "desktop-1440x1000",
            seedDocumentId = "phase-3-canvas-empty",
            userActions = new[]
            {
                "Open /canvas-engine-host with the phase 3 empty seed document.",
                "Wait for the Blazor canvas host and JavaScript engine readiness signal.",
                "Probe host/engine selectors, assert canvas pixels, and save full/editor screenshots."
            },
            expectedVisibleChanges = "The explicit CanvasEnginePreview route displays a clean blank document page rendered by the canvas host.",
            expectedModelChanges = "None.",
            screenshotPaths = new[] { fullPath, editorPath, hostPath, pagePath },
            metrics,
            probe,
            uxReviewerNotes = "The after screenshot should look like a production document surface: sharp page, calm workspace, no legacy/core host, no overlap."
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        TestContext.AddResultFile(fullPath);
        TestContext.AddResultFile(editorPath);
        TestContext.AddResultFile(hostPath);
        TestContext.AddResultFile(pagePath);
        TestContext.AddResultFile(manifestPath);
    }

    private static string CreateOutputDirectory()
    {
        var output = Path.Combine(
            FindRepositoryRoot().FullName,
            "tests",
            "Tempo.Blazor.E2E",
            "TestResults",
            "document-editor-canvas",
            "phase3-host",
            "2026-06-04",
            "desktop-1440x1000");
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

    private sealed class CanvasHostProbe
    {
        public string RenderEngine { get; set; } = string.Empty;
        public bool CanvasHostReady { get; set; }
        public int CanvasLayerCount { get; set; }
        public int WysiwygHostCount { get; set; }
        public int CoreHostCount { get; set; }
        public int A11yMirrorCount { get; set; }
        public int HiddenInputCount { get; set; }
    }
}
