using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tempo.Blazor.E2E.CanvasEngine;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Command-layer plan phase 9: the Insert-ribbon token menu. The button routed openTokenMenu — a
/// command the engine never registered — so it was a silent no-op. Decision
/// DOC-EDITOR-TOKEN-MENU-BLAZOR-SIDE: the menu is a Blazor floating panel; picking a token routes
/// the new insertToken engine command which inserts a first-class token run.
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorTokenMenuE2ETests : WasmTestBase
{
    private const string DocumentId = "phase-12-canvas-history-save";

    [TestInitialize]
    public Task ResetDocumentEditorDemoAsync()
        => DocumentEditorE2EReset.ResetAsync();

    [TestMethod]
    public async Task Phase9_InsertMenu_OpensTokenPanelInsertsTokenAndPersists()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId={DocumentId}&showToolbar=true", WaitUntilState.DOMContentLoaded);
        await page.WaitForFunctionAsync(
            """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelectorAll('[data-canvas-text-rect][data-block-id="canvas-history-text"]').length >= 1
            """,
            null,
            new PageWaitForFunctionOptions { Timeout = 30_000 });

        var output = CreateOutputDirectory();
        var panelPath = Path.Combine(output, "00-token-panel-open.png");
        var insertedPath = Path.Combine(output, "01-token-inserted.png");
        var reloadPath = Path.Combine(output, "02-after-reload.png");

        // Caret into the body so the token has a deterministic target.
        await ClickTextBlockAsync(page, "canvas-history-text");

        await page.GetByTestId("document-ribbon-tab-insert").ClickAsync();
        await page.GetByTestId("document-insert-menu").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-token-insert-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(page.Locator(".tm-rte-token-item", new PageLocatorOptions { HasTextString = "{{client.name}}" }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions { Path = panelPath, Type = ScreenshotType.Png });

        // Edge case: the token filter narrows the list.
        await page.GetByTestId("document-token-insert-filter").FillAsync("case");
        await Assertions.Expect(page.Locator(".tm-rte-token-item", new PageLocatorOptions { HasTextString = "{{case.number}}" }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(page.Locator(".tm-rte-token-item", new PageLocatorOptions { HasTextString = "{{client.name}}" }))
            .Not.ToBeVisibleAsync(new() { Timeout = 10_000 });
        await page.GetByTestId("document-token-insert-filter").FillAsync("client");
        await Assertions.Expect(page.Locator(".tm-rte-token-item", new PageLocatorOptions { HasTextString = "{{client.name}}" }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        await page.Locator(".tm-rte-token-item", new PageLocatorOptions { HasTextString = "{{client.name}}" }).ClickAsync();

        try
        {
            await page.WaitForFunctionAsync(
                """
                async () => {
                    const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                    const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                    const model = JSON.parse(module.getModelJson(host.getAttribute('data-canvas-engine-handle')) || '{}');
                    const block = (model.body?.blocks || []).find(candidate => String(candidate?.id || '') === 'canvas-history-text');
                    return (block?.content?.runs || []).some(run => String(run?.type || '') === 'token' && run?.token?.key === 'client.name');
                }
                """,
                null,
                new PageWaitForFunctionOptions { Timeout = 15_000 });
        }
        catch (TimeoutException)
        {
            var diagnostic = await page.EvaluateAsync<string>(
                """
                async () => {
                    const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                    const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                    const model = JSON.parse(module.getModelJson(host?.getAttribute('data-canvas-engine-handle')) || '{}');
                    const block = (model.body?.blocks || []).find(candidate => String(candidate?.id || '') === 'canvas-history-text');
                    return JSON.stringify({
                        panelVisible: !!document.querySelector('[data-testid="document-token-insert-panel"]'),
                        runs: (block?.content?.runs || []).map(run => ({ type: run?.type, key: run?.token?.key })),
                        selection: document.querySelector('[data-testid="document-canvas-engine-root"]')?.getAttribute('data-canvas-selection-focus-block-id'),
                    });
                }
                """);
            Assert.Fail($"Token run did not appear in the model. Diagnostic: {diagnostic}");
        }
        await Assertions.Expect(page.GetByTestId("document-token-insert-panel")).Not.ToBeVisibleAsync(new() { Timeout = 10_000 });
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-testid=\"document-canvas-a11y-mirror\"]')?.textContent?.includes('Client name') === true",
            null,
            new PageWaitForFunctionOptions { Timeout = 10_000 });
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions { Path = insertedPath, Type = ScreenshotType.Png });

        // Edge case: the insert is one undoable transaction.
        await page.GetByTestId("document-undo").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-testid=\"document-canvas-a11y-mirror\"]')?.textContent?.includes('Client name') !== true",
            null,
            new PageWaitForFunctionOptions { Timeout = 10_000 });
        await page.GetByTestId("document-redo").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-testid=\"document-canvas-a11y-mirror\"]')?.textContent?.includes('Client name') === true",
            null,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

        await page.GetByTestId("document-save").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-save-message")).ToContainTextAsync("Saved", new() { Timeout = 10_000 });
        await NavigateWithinBlazorAsync(page, "/canvas-engine-host?documentId=phase-5-canvas-render");
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-testid=\"document-canvas-page\"]')?.getAttribute('data-canvas-model-document-id') === 'phase-5-canvas-render'",
            null,
            new PageWaitForFunctionOptions { Timeout = 20_000 });
        await NavigateWithinBlazorAsync(page, $"/canvas-engine-host?documentId={DocumentId}&showToolbar=true");
        await page.WaitForFunctionAsync(
            """
            async () => {
                if (!document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')) return false;
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const model = JSON.parse(module.getModelJson(host.getAttribute('data-canvas-engine-handle')) || '{}');
                const block = (model.body?.blocks || []).find(candidate => String(candidate?.id || '') === 'canvas-history-text');
                return (block?.content?.runs || []).some(run => String(run?.type || '') === 'token' && run?.token?.key === 'client.name');
            }
            """,
            null,
            new PageWaitForFunctionOptions { Timeout = 30_000 });
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-testid=\"document-canvas-a11y-mirror\"]')?.textContent?.includes('Client name') === true",
            null,
            new PageWaitForFunctionOptions { Timeout = 10_000 });
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions { Path = reloadPath, Type = ScreenshotType.Png });

        var manifestPath = Path.Combine(output, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            testName = nameof(Phase9_InsertMenu_OpensTokenPanelInsertsTokenAndPersists),
            seedDocumentId = DocumentId,
            userActions = new[]
            {
                "Place the caret in the body and open the Insert-ribbon token panel.",
                "Filter the token list (edge case) and pick the client.name token.",
                "The panel closes and a token run renders at the caret; undo/redo round-trips it.",
                "Save, navigate away and back — the token run persists in the model and the render."
            },
            expectedVisibleChanges = "The Blazor token panel lists provider tokens with a working filter; selecting one inserts a rendered token pill at the caret that survives undo/redo and save/reload.",
            screenshotPaths = new[] { panelPath, insertedPath, reloadPath }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        TestContext.AddResultFile(panelPath);
        TestContext.AddResultFile(insertedPath);
        TestContext.AddResultFile(reloadPath);
        TestContext.AddResultFile(manifestPath);
    }

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
            nameof(DocumentEditorTokenMenuE2ETests), "phase9-token-menu");
        Directory.CreateDirectory(output);
        return output;
    }
}
