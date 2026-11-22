using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 7 UX sign-off: captures full editor screenshots of the golden documents at two viewports so
/// the rendering can be reviewed as a human would see it (no overlapping text/images, clean layout).
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasUxSignoffE2ETests : WasmTestBase
{
    private const string OutputDir = "/tmp/canvas-overlap-fix/ux";

    [TestMethod]
    public async Task GoldenDocuments_RenderCleanly_ForUxReview()
    {
        Directory.CreateDirectory(OutputDir);
        string[] docs = ["contract-demo", "onlyoffice-parity-2026-05-24", "table-demo", "large-perf-1000"];
        (int W, int H)[] viewports = [(1280, 720), (1440, 1000)];

        foreach (var docId in docs)
        {
            foreach (var (w, h) in viewports)
            {
                var context = await CreateContextAsync();
                var page = await context.NewPageAsync();
                await page.SetViewportSizeAsync(w, h);
                await GotoAppReadyAsync(page, $"{BaseUrl}/document-editor?documentId={docId}");
                await page.WaitForSelectorAsync("[data-testid='document-canvas-engine-host'][data-canvas-engine-ready='true']", new PageWaitForSelectorOptions
                {
                    State = WaitForSelectorState.Attached,
                    Timeout = 120_000,
                });
                await page.WaitForTimeoutAsync(700);

                var path = Path.Combine(OutputDir, $"{docId}-{w}x{h}.png");
                await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions { Path = path, Type = ScreenshotType.Png });
                Assert.IsTrue(new FileInfo(path).Length > 5_000, $"Screenshot for {docId} {w}x{h} must be a real PNG.");
                TestContext.WriteLine($"captured {path}");
            }
        }
    }
}
