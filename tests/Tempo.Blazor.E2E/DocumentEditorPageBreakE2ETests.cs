using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tempo.Blazor.E2E.CanvasEngine;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Command-layer plan phase 6: deleting a page break through the context menu. The engine never
/// registered deletePageBreak AND the canvas context-menu path never marked page-break blocks, so
/// the Delete-page-break menu item could not even appear — a double silent no-op.
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorPageBreakE2ETests : WasmTestBase
{
    private const string DocumentId = "phase-12-canvas-history-save";

    [TestInitialize]
    public Task ResetDocumentEditorDemoAsync()
        => DocumentEditorE2EReset.ResetAsync();

    [TestMethod]
    public async Task Phase6_PageBreakContextMenu_DeleteRestoresPaginationAndPersists()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId={DocumentId}&showToolbar=true", WaitUntilState.DOMContentLoaded);
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelector('[data-testid="document-ribbon-tab-insert"]')
                && document.querySelectorAll('[data-canvas-text-rect][data-block-id="canvas-history-text"]').length >= 1
            """, 30_000);

        var output = CreateOutputDirectory();
        var withBreakPath = Path.Combine(output, "00-with-page-break.png");
        var deletedPath = Path.Combine(output, "01-after-delete.png");
        var reloadPath = Path.Combine(output, "02-after-reload.png");

        var basePageCount = await ReadPageCountAsync(page);

        // Caret into the body (insertPageBreak requires a body selection), then insert the break.
        await ClickTextBlockAsync(page, "canvas-history-text");
        await page.GetByTestId("document-ribbon-tab-insert").ClickAsync();
        await page.GetByTestId("document-insert-page-break").ClickAsync();
        await page.WaitForFunctionAsync(
            "baseline => Number(document.querySelector('[data-testid=\"document-canvas-engine-root\"]')?.getAttribute('data-canvas-page-count') || '0') === baseline + 1",
            basePageCount,
            new PageWaitForFunctionOptions { Timeout = 10_000 });
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions { Path = withBreakPath, Type = ScreenshotType.Png });

        // Right-click the page-break block (viewport point derived from the engine selection layout).
        var breakPoint = await ReadPageBreakViewportPointAsync(page);
        await page.Mouse.ClickAsync((float)breakPoint[0], (float)breakPoint[1], new MouseClickOptions { Button = MouseButton.Right });
        await Assertions.Expect(page.GetByTestId("document-page-break-delete")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await page.GetByTestId("document-page-break-delete").ClickAsync();

        await page.WaitForFunctionAsync(
            "baseline => Number(document.querySelector('[data-testid=\"document-canvas-engine-root\"]')?.getAttribute('data-canvas-page-count') || '0') === baseline",
            basePageCount,
            new PageWaitForFunctionOptions { Timeout = 10_000 });
        Assert.AreEqual(0, await CountModelPageBreaksAsync(page), "the page-break block must be removed from the model");
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions { Path = deletedPath, Type = ScreenshotType.Png });

        // Edge case: the delete is one undoable transaction — undo brings the page back, redo removes it.
        await page.GetByTestId("document-undo").ClickAsync();
        await page.WaitForFunctionAsync(
            "baseline => Number(document.querySelector('[data-testid=\"document-canvas-engine-root\"]')?.getAttribute('data-canvas-page-count') || '0') === baseline + 1",
            basePageCount,
            new PageWaitForFunctionOptions { Timeout = 10_000 });
        await page.GetByTestId("document-redo").ClickAsync();
        await page.WaitForFunctionAsync(
            "baseline => Number(document.querySelector('[data-testid=\"document-canvas-engine-root\"]')?.getAttribute('data-canvas-page-count') || '0') === baseline",
            basePageCount,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

        await page.GetByTestId("document-save").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-save-message")).ToContainTextAsync("Saved", new() { Timeout = 10_000 });

        await NavigateWithinBlazorAsync(page, "/canvas-engine-host?documentId=phase-5-canvas-render");
        await WaitForPredicateAsync(page, "() => document.querySelector('[data-testid=\"document-canvas-page\"]')?.getAttribute('data-canvas-model-document-id') === 'phase-5-canvas-render'", 20_000);
        await NavigateWithinBlazorAsync(page, $"/canvas-engine-host?documentId={DocumentId}&showToolbar=true");
        await page.WaitForFunctionAsync(
            """
            baseline => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && Number(document.querySelector('[data-testid="document-canvas-engine-root"]')?.getAttribute('data-canvas-page-count') || '0') === baseline
            """,
            basePageCount,
            new PageWaitForFunctionOptions { Timeout = 30_000 });
        Assert.AreEqual(0, await CountModelPageBreaksAsync(page), "the deleted page break must stay deleted after save/reload");
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions { Path = reloadPath, Type = ScreenshotType.Png });

        var manifestPath = Path.Combine(output, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            testName = nameof(Phase6_PageBreakContextMenu_DeleteRestoresPaginationAndPersists),
            seedDocumentId = DocumentId,
            userActions = new[]
            {
                "Place the caret in the body and insert a page break from the Insert ribbon (page count grows).",
                "Right-click the page-break block and choose Delete page break (page count returns, content flows back).",
                "Undo (page returns) and redo (page removed again).",
                "Save, navigate away and back — the pagination stays at the baseline."
            },
            expectedVisibleChanges = "Inserting a page break adds a page; deleting it through the context menu flows content back to the original pagination; undo/redo and save/reload behave consistently.",
            screenshotPaths = new[] { withBreakPath, deletedPath, reloadPath },
            basePageCount
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        TestContext.AddResultFile(withBreakPath);
        TestContext.AddResultFile(deletedPath);
        TestContext.AddResultFile(reloadPath);
        TestContext.AddResultFile(manifestPath);
    }

    private static Task<int> ReadPageCountAsync(IPage page)
        => page.EvaluateAsync<int>(
            "() => Number(document.querySelector('[data-testid=\"document-canvas-engine-root\"]')?.getAttribute('data-canvas-page-count') || '0')");

    private static Task<int> CountModelPageBreaksAsync(IPage page)
        => page.EvaluateAsync<int>(
            """
            async () => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const model = JSON.parse(module.getModelJson(host.getAttribute('data-canvas-engine-handle')) || '{}');
                return (model.body?.blocks || []).filter(block => String(block?.type || '').toLowerCase() === 'pagebreak').length;
            }
            """);

    /// <summary>Viewport [x, y] of the page-break block, derived from the engine selection layout
    /// (page breaks paint on canvas only — there is no DOM rect element for them).</summary>
    private static Task<double[]> ReadPageBreakViewportPointAsync(IPage page)
        => page.EvaluateAsync<double[]>(
            """
            async () => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const snapshot = JSON.parse(module.getRuntimeDebugSnapshotJson(host.getAttribute('data-canvas-engine-handle')) || '{}');
                const block = (snapshot.render?.selectionLayout?.blocks || [])
                    .find(candidate => String(candidate?.type || '').toLowerCase() === 'pagebreak');
                if (!block?.rect) throw new Error('page break block not found in the selection layout');
                const pageElement = document.querySelector(`[data-testid="document-canvas-page"][data-page-index="${Number(block.pageIndex || 0)}"]`);
                if (!pageElement) throw new Error(`page ${block.pageIndex} not mounted`);
                const pageRect = pageElement.getBoundingClientRect();
                const logicalWidth = Number(pageElement.getAttribute('data-canvas-page-logical-width') || '0') || pageRect.width;
                const scale = pageRect.width / logicalWidth;
                return [
                    pageRect.left + (Number(block.rect.x || 0) + Number(block.rect.width || 0) / 2) * scale,
                    pageRect.top + (Number(block.rect.y || 0) + Number(block.rect.height || 0) / 2) * scale,
                ];
            }
            """);

    private static async Task ClickTextBlockAsync(IPage page, string blockId)
    {
        for (var attempt = 0; ; attempt++)
        {
            var point = await page.EvaluateAsync<double[]>(
                """
                blockId => {
                    const rect = document.querySelector(`[data-canvas-text-rect][data-block-id="${blockId}"]`)?.getBoundingClientRect();
                    if (!rect) throw new Error(`no text rect for ${blockId}`);
                    return [rect.left + Math.min(30, rect.width / 2), rect.top + rect.height / 2];
                }
                """,
                blockId);
            await page.Mouse.ClickAsync((float)point[0], (float)point[1]);
            var focused = await page.EvaluateAsync<string>(
                "() => document.querySelector('[data-testid=\"document-canvas-engine-root\"]')?.getAttribute('data-canvas-selection-focus-block-id') || ''");
            if (focused == blockId)
            {
                return;
            }

            if (attempt >= 9)
            {
                Assert.Fail($"Click kept resolving to block '{focused}' instead of {blockId}.");
            }

            await page.WaitForTimeoutAsync(250);
        }
    }

    private static Task NavigateWithinBlazorAsync(IPage page, string url)
        => page.EvaluateAsync(
            """
            url => window.Blazor?.navigateTo
                ? window.Blazor.navigateTo(url)
                : window.history.pushState({}, '', url)
            """,
            url);

    private string CreateOutputDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
        {
            current = current.Parent;
        }

        var output = Path.Combine(
            current!.FullName,
            "tests", "Tempo.Blazor.E2E", "TestResults", "document-editor-canvas",
            nameof(DocumentEditorPageBreakE2ETests), "phase6-delete-page-break");
        Directory.CreateDirectory(output);
        return output;
    }
}
