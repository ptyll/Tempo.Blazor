using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tempo.Blazor.E2E.CanvasEngine;

namespace Tempo.Blazor.E2E;

/// <summary>Phase 13 E2E coverage for canvas toolbar focus, context menu, and spellcheck diagnostics.</summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasToolbarSpellcheckE2ETests : WasmTestBase
{
    private const string Phase13DocumentId = "phase-13-canvas-toolbar-spellcheck";

    [TestMethod]
    public async Task Phase13_ToolbarContextMenuAndSpellcheck_RunThroughCanvasCommandRuntime()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenPhase13DocumentAsync(page);

        var output = CreateOutputDirectory("desktop-1440x1000");
        var beforePath = Path.Combine(output, "00-phase13-before.png");
        var menuPath = Path.Combine(output, "01-phase13-spell-menu.png");
        var afterPath = Path.Combine(output, "02-phase13-after-suggestion.png");

        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = beforePath,
            Type = ScreenshotType.Png
        });

        var selected = await SelectCanvasTextRangeAsync(page, "canvas-toolbar-selection", 0, 7);
        Assert.AreEqual("Toolbar", selected.ExpectedText);
        await page.GetByTestId("document-bold").ClickAsync();
        await WaitForSelectionVisibleAsync(page, "canvas-toolbar-selection");
        await page.WaitForFunctionAsync(
            """
            () => document.querySelector('[data-testid="document-canvas-engine-root"]')
                ?.getAttribute('data-canvas-command-bold-state') === 'active'
            """,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

        var squiggle = page.GetByTestId("document-canvas-spell-squiggle").First;
        await Assertions.Expect(squiggle).ToBeVisibleAsync(new() { Timeout = 10_000 });
        var box = await squiggle.BoundingBoxAsync();
        Assert.IsNotNull(box, "Spellcheck squiggle must expose a stable hit/test rectangle.");
        await page.Mouse.ClickAsync((float)(box.X + box.Width / 2), (float)(box.Y + box.Height / 2), new MouseClickOptions { Button = MouseButton.Right });
        await Assertions.Expect(page.GetByTestId("document-text-context-menu")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByTestId("document-context-spell-suggestion").First).ToContainTextAsync("wrong", new() { Timeout = 5_000 });

        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = menuPath,
            Type = ScreenshotType.Png
        });

        await page.GetByTestId("document-context-spell-suggestion").First.ClickAsync();
        await page.WaitForFunctionAsync(
            """
            () => {
                const mirror = document.querySelector('[data-testid="document-canvas-a11y-mirror"]');
                return mirror?.textContent?.includes('marks wrong and offers') === true
                    && mirror?.textContent?.includes('wrngg') === false;
            }
            """,
            new PageWaitForFunctionOptions { Timeout = 10_000 });
        await page.GetByTestId("document-undo").ClickAsync();
        await page.WaitForFunctionAsync(
            """
            () => document.querySelector('[data-testid="document-canvas-a11y-mirror"]')
                ?.textContent?.includes('marks wrngg and offers') === true
            """,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

        await DocumentEditorCanvasVisualAssert.AssertNoTextOverlapAsync(page);
        await DocumentEditorCanvasVisualAssert.AssertNoUiOverlapAsync(page);
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = afterPath,
            Type = ScreenshotType.Png
        });

        var probe = await page.EvaluateAsync<Phase13Probe>(
            """
            () => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                return {
                    modelDocumentId: document.querySelector('[data-testid="document-canvas-page"]')?.getAttribute('data-canvas-model-document-id') || '',
                    proofingCount: Number(root?.getAttribute('data-canvas-proofing-count') || '0'),
                    squiggleCount: Number(root?.getAttribute('data-canvas-proofing-squiggle-count') || '0'),
                    lastCommand: root?.getAttribute('data-canvas-command-last') || '',
                    selectionCollapsed: root?.getAttribute('data-canvas-selection-collapsed') || '',
                    shortcutManager: root?.getAttribute('data-canvas-shortcut-manager') || ''
                };
            }
            """);
        Assert.AreEqual(Phase13DocumentId, probe.ModelDocumentId);
        Assert.IsTrue(probe.ProofingCount >= 1);
        Assert.IsTrue(probe.SquiggleCount >= 1);
        Assert.AreEqual("undo", probe.LastCommand);
        Assert.AreEqual("enabled", probe.ShortcutManager);

        var manifestPath = Path.Combine(output, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            testName = nameof(Phase13_ToolbarContextMenuAndSpellcheck_RunThroughCanvasCommandRuntime),
            seedDocumentId = Phase13DocumentId,
            userActions = new[]
            {
                "Open the phase 13 canvas seed document with the production toolbar visible.",
                "Select text on the canvas and click the toolbar Bold button.",
                "Right-click the canvas spellcheck squiggle and choose the host-provided suggestion.",
                "Undo the spelling replacement."
            },
            expectedVisibleChanges = "Toolbar actions keep the canvas selection visible; the diagnostics canvas paints a red squiggle; the context menu suggestion replaces wrngg with wrong and Undo restores wrngg.",
            screenshotPaths = new[] { beforePath, menuPath, afterPath },
            probe
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        TestContext.AddResultFile(beforePath);
        TestContext.AddResultFile(menuPath);
        TestContext.AddResultFile(afterPath);
        TestContext.AddResultFile(manifestPath);
    }

    private async Task OpenPhase13DocumentAsync(IPage page)
    {
        await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId={Phase13DocumentId}&showToolbar=true");
        await page.WaitForSelectorAsync(
            "[data-testid='document-canvas-engine-host'][data-canvas-engine-ready='true']",
            new PageWaitForSelectorOptions { Timeout = 30_000 });
        await page.WaitForFunctionAsync(
            """
            () => document.querySelectorAll('[data-canvas-text-rect][data-block-id="canvas-toolbar-selection"]').length >= 1
                && document.querySelector('[data-testid="document-canvas-spell-squiggle"]')
            """,
            new PageWaitForFunctionOptions { Timeout = 30_000 });
    }

    private static async Task<CanvasTextRange> SelectCanvasTextRangeAsync(IPage page, string blockId, int startOffset, int endOffset)
    {
        var target = await ReadCanvasTextRangeAsync(page, blockId, startOffset, endOffset);
        await page.Mouse.MoveAsync((float)target.StartX, (float)target.StartY);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)target.EndX, (float)target.EndY, new MouseMoveOptions { Steps = 10 });
        await page.Mouse.UpAsync();
        await WaitForSelectionVisibleAsync(page, blockId);
        return target;
    }

    private static Task WaitForSelectionVisibleAsync(IPage page, string blockId)
        => page.WaitForFunctionAsync(
            """
            blockId => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                return root?.getAttribute('data-canvas-selection-collapsed') === 'false'
                    && root?.getAttribute('data-canvas-selection-anchor-block-id') === blockId
                    && document.querySelectorAll('[data-testid="document-canvas-selection-rect"]').length >= 1;
            }
            """,
            blockId,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task<CanvasTextRange> ReadCanvasTextRangeAsync(IPage page, string blockId, int startOffset, int endOffset)
        => page.EvaluateAsync<CanvasTextRange>(
            """
            ([blockId, startOffset, endOffset]) => {
                const rects = Array.from(document.querySelectorAll(`[data-canvas-text-rect][data-block-id="${blockId}"]`))
                    .map(node => {
                        const rect = node.getBoundingClientRect();
                        const start = Number(node.getAttribute('data-canvas-start-offset') || '0');
                        const end = Number(node.getAttribute('data-canvas-end-offset') || '0');
                        return { rect, start, end };
                    })
                    .filter(item => item.end > item.start);
                if (!rects.length) throw new Error(`No canvas text rects found for ${blockId}.`);
                const startRect = rects.find(item => startOffset >= item.start && startOffset < item.end) || rects[0];
                const endRect = rects.find(item => endOffset > item.start && endOffset <= item.end) || rects[rects.length - 1];
                const block = document.querySelector(`[data-testid="document-canvas-a11y-mirror"] [data-block-id="${blockId}"]`);
                const expectedText = (block?.textContent || '').slice(startOffset, endOffset);
                const ratio = (offset, item) => Math.max(0, Math.min(1, (offset - item.start) / Math.max(1, item.end - item.start)));
                return {
                    startX: startRect.rect.left + Math.max(1, startRect.rect.width * ratio(startOffset, startRect)),
                    startY: startRect.rect.top + startRect.rect.height / 2,
                    endX: endRect.rect.left + Math.max(1, endRect.rect.width * ratio(endOffset, endRect)),
                    endY: endRect.rect.top + endRect.rect.height / 2,
                    expectedText
                };
            }
            """,
            new object[] { blockId, startOffset, endOffset });

    private sealed class CanvasTextRange
    {
        public double StartX { get; set; }

        public double StartY { get; set; }

        public double EndX { get; set; }

        public double EndY { get; set; }

        public string ExpectedText { get; set; } = string.Empty;
    }

    private sealed class Phase13Probe
    {
        public string ModelDocumentId { get; set; } = string.Empty;

        public int ProofingCount { get; set; }

        public int SquiggleCount { get; set; }

        public string LastCommand { get; set; } = string.Empty;

        public string SelectionCollapsed { get; set; } = string.Empty;

        public string ShortcutManager { get; set; } = string.Empty;
    }

    private static string CreateOutputDirectory(string viewport)
    {
        var output = Path.Combine(
            FindRepositoryRoot().FullName,
            "tests",
            "Tempo.Blazor.E2E",
            "TestResults",
            "document-editor-canvas",
            "phase13-toolbar-spellcheck",
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

        throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx from the E2E test output directory.");
    }
}
