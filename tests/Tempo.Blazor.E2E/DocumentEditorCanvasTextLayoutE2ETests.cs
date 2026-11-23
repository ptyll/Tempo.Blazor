using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tempo.Blazor.E2E.CanvasEngine;

namespace Tempo.Blazor.E2E;

/// <summary>Phase 6 E2E coverage for canvas text layout, wrapping, and pagination.</summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasTextLayoutE2ETests : WasmTestBase
{
    [TestMethod]
    public async Task Phase6_TextLayout_WrapsLongParagraphsAcrossPagesWithoutTextOverlap()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenTextLayoutDocumentAsync(page);

        var probe = await ReadTextLayoutProbeAsync(page);
        Assert.AreEqual("CanvasEnginePreview", probe.RenderEngine);
        Assert.IsTrue(probe.HostReady);
        Assert.AreEqual("phase-6-canvas-text-layout", probe.ModelDocumentId);
        Assert.IsTrue(probe.PageCount >= 2, $"Expected paginated document pages, actual: {probe.PageCount}.");
        Assert.IsTrue(probe.TextRunCount >= 24, $"Expected wrapped text runs, actual: {probe.TextRunCount}.");
        Assert.IsTrue(probe.TextRectCount >= 24, $"Expected text rect metadata, actual: {probe.TextRectCount}.");
        Assert.IsTrue(probe.SecondPageTextRunCount > 0, "The second page must contain rendered text.");

        await DocumentEditorCanvasVisualAssert.AssertNoTextOverlapAsync(page);
        var firstPageContent = await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(page.Locator("[data-canvas-layer='content']").Nth(0));
        var secondPageContent = await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(page.Locator("[data-canvas-layer='content']").Nth(1));
        Assert.IsTrue(firstPageContent.NonTransparentPixels > secondPageContent.NonTransparentPixels / 4, "First page content should be visibly populated.");
        Assert.IsTrue(secondPageContent.MinY >= 72 * probe.PixelRatio - 6, "Second page text must start inside the top margin.");

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
        var fullPath = Path.Combine(output, "00-phase6-full.png");
        var firstPagePath = Path.Combine(output, "01-phase6-page-1.png");
        var secondPagePath = Path.Combine(output, "02-phase6-page-2.png");
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = fullPath,
            FullPage = true,
            Type = ScreenshotType.Png
        });
        await page.GetByTestId("document-canvas-page").Nth(0).ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = firstPagePath,
            Type = ScreenshotType.Png
        });
        await page.GetByTestId("document-canvas-page").Nth(1).ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = secondPagePath,
            Type = ScreenshotType.Png
        });

        var manifestPath = Path.Combine(output, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            testName = nameof(Phase6_TextLayout_WrapsLongParagraphsAcrossPagesWithoutTextOverlap),
            viewport = "desktop-1440x1000",
            seedDocumentId = "phase-6-canvas-text-layout",
            userActions = new[]
            {
                "Open /canvas-engine-host with the phase 6 text layout seed document.",
                "Wait for the canvas engine readiness signal and for at least two rendered page surfaces.",
                "Verify canvas pixel content on the first and second page and assert DOM text-rect metadata has no overlaps."
            },
            expectedVisibleChanges = "Long paragraphs wrap like a document, list labels sit outside the text measure, and pagination creates additional pages with readable text.",
            expectedModelChanges = "None.",
            screenshotPaths = new[] { fullPath, firstPagePath, secondPagePath },
            firstPageContent,
            secondPageContent,
            probe,
            uxReviewerNotes = "The canvas pages should read as a document layout: aligned paragraphs, wrapped list items, clear margins, and no overlapping text."
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        TestContext.AddResultFile(fullPath);
        TestContext.AddResultFile(firstPagePath);
        TestContext.AddResultFile(secondPagePath);
        TestContext.AddResultFile(manifestPath);
    }

    private async Task OpenTextLayoutDocumentAsync(IPage page)
    {
        await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId=phase-6-canvas-text-layout");
        await page.WaitForSelectorAsync(
            "[data-testid='document-canvas-engine-host'][data-canvas-engine-ready='true']",
            new PageWaitForSelectorOptions { Timeout = 30_000 });
        try
        {
            await WaitForPredicateAsync(page, """
                () => document.querySelectorAll('[data-testid="document-canvas-page"]').length >= 2
                    && Number(document.querySelector('[data-testid="document-canvas-page"]')?.getAttribute('data-canvas-text-run-count') || '0') >= 12
                    && document.querySelectorAll('[data-canvas-text-rect]').length >= 24
                """, 30_000);
        }
        catch (TimeoutException)
        {
            var probe = await ReadTextLayoutProbeAsync(page);
            var mirrorText = await page.Locator("[data-testid='document-canvas-a11y-mirror']").First.TextContentAsync();
            Assert.Fail($"Timed out waiting for canvas text layout. ModelDocumentId='{probe.ModelDocumentId}', PageCount={probe.PageCount}, TextRunCount={probe.TextRunCount}, TextRectCount={probe.TextRectCount}, MirrorText='{mirrorText}'.");
        }
    }

    private static Task<CanvasTextLayoutProbe> ReadTextLayoutProbeAsync(IPage page)
        => page.EvaluateAsync<CanvasTextLayoutProbe>(
            """
            () => {
                const editor = document.querySelector('[data-testid="document-editor-demo"]');
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const pages = Array.from(document.querySelectorAll('[data-testid="document-canvas-page"]'));
                const firstCanvas = document.querySelector('[data-canvas-layer="page-background"]');
                const cssWidth = Number.parseFloat(firstCanvas?.style.width || '0');
                return {
                    renderEngine: editor?.getAttribute('data-render-engine') || '',
                    hostReady: host?.getAttribute('data-canvas-engine-ready') === 'true',
                    pageCount: pages.length,
                    layerCount: document.querySelectorAll('[data-canvas-layer]').length,
                    textRunCount: pages.reduce((sum, item) => sum + Number(item.getAttribute('data-canvas-text-run-count') || '0'), 0),
                    secondPageTextRunCount: Number(pages[1]?.getAttribute('data-canvas-text-run-count') || '0'),
                    textRectCount: document.querySelectorAll('[data-canvas-text-rect]').length,
                    modelDocumentId: pages[0]?.getAttribute('data-canvas-model-document-id') || '',
                    modelBlockCount: Number(pages[0]?.getAttribute('data-canvas-model-block-count') || '0'),
                    pixelRatio: cssWidth > 0 ? (firstCanvas?.width || 0) / cssWidth : 0
                };
            }
            """);

    private static string CreateOutputDirectory(string viewport)
    {
        var output = Path.Combine(
            FindRepositoryRoot().FullName,
            "tests",
            "Tempo.Blazor.E2E",
            "TestResults",
            "document-editor-canvas",
            "phase6-text-layout",
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

    private sealed class CanvasTextLayoutProbe
    {
        [JsonPropertyName("renderEngine")]
        public string RenderEngine { get; set; } = string.Empty;

        [JsonPropertyName("hostReady")]
        public bool HostReady { get; set; }

        [JsonPropertyName("pageCount")]
        public int PageCount { get; set; }

        [JsonPropertyName("layerCount")]
        public int LayerCount { get; set; }

        [JsonPropertyName("textRunCount")]
        public int TextRunCount { get; set; }

        [JsonPropertyName("secondPageTextRunCount")]
        public int SecondPageTextRunCount { get; set; }

        [JsonPropertyName("textRectCount")]
        public int TextRectCount { get; set; }

        [JsonPropertyName("modelDocumentId")]
        public string ModelDocumentId { get; set; } = string.Empty;

        [JsonPropertyName("modelBlockCount")]
        public int ModelBlockCount { get; set; }

        [JsonPropertyName("pixelRatio")]
        public double PixelRatio { get; set; }
    }
}
