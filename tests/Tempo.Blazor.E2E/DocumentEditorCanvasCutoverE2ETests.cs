using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>Phase 25 cutover smoke coverage for the production /document-editor route.</summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[TestCategory("DocumentEditor:Cutover")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasCutoverE2ETests : WasmTestBase
{
    [TestMethod]
    public async Task Phase25_DocumentEditorRoute_DefaultsToCanvasEngine()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);

        await GotoAppReadyAsync(page, $"{BaseUrl}/document-editor?documentId=contract-demo", WaitUntilState.DOMContentLoaded);

        await WaitForEditorEngineAsync(page, "CanvasEnginePreview");
        await Assertions.Expect(page.GetByTestId("document-canvas-engine-host"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
        await Assertions.Expect(page.GetByTestId("document-wysiwyg-host"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });
    }

    [TestMethod]
    public async Task Phase25_DocumentEditorRoute_RenderEngineQueryIsIgnoredAndStaysCanvas()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);

        await GotoAppReadyAsync(page, $"{BaseUrl}/document-editor?documentId=contract-demo&renderEngine=legacy", WaitUntilState.DOMContentLoaded);

        await WaitForEditorEngineAsync(page, "CanvasEnginePreview");
        await Assertions.Expect(page.GetByTestId("document-canvas-engine-host"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
        await Assertions.Expect(page.GetByTestId("document-wysiwyg-host"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });
    }

    private static Task WaitForEditorEngineAsync(IPage page, string expectedEngine)
        => page.WaitForFunctionAsync(
            """
            expectedEngine => {
                const editor = document.querySelector('[data-testid="document-editor-demo"].tm-document-editor');
                return editor?.getAttribute('data-render-engine') === expectedEngine;
            }
            """,
            expectedEngine,
            new PageWaitForFunctionOptions { Timeout = 30_000 });
}
