using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tempo.Blazor.E2E.CanvasEngine;

namespace Tempo.Blazor.E2E;

/// <summary>Phase 8 E2E coverage for canvas keyboard input, paragraph splitting, soft breaks, and deletion.</summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasTypingE2ETests : WasmTestBase
{
    [TestMethod]
    public async Task Phase8_Typing_UsesHiddenInputAndUpdatesCanvasModel()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenTypingDocumentAsync(page);

        var startPoint = await ReadTextPointAsync(page, "canvas-typing-body", useRightEdge: true);
        await page.Mouse.ClickAsync((float)startPoint.X, (float)startPoint.Y);
        await DocumentEditorCanvasVisualAssert.AssertCaretVisibleAsync(page.Locator("[data-testid='document-canvas-caret']").First);

        await page.Keyboard.TypeAsync(" Hello");
        await WaitForMirrorTextAsync(page, "canvas-typing-body", "Start Hello");

        await page.Keyboard.PressAsync("Enter");
        await page.Keyboard.TypeAsync("World");
        await WaitForMirrorTextAsync(page, "canvas-typing-body-split", "World");

        await page.Keyboard.PressAsync("Shift+Enter");
        await page.Keyboard.TypeAsync("Soft");
        await WaitForMirrorTextAsync(page, "canvas-typing-body-split", "World\nSoft");

        await page.Keyboard.PressAsync("Backspace");
        await WaitForMirrorTextAsync(page, "canvas-typing-body-split", "World\nSof");

        var probe = await ReadTypingProbeAsync(page);
        Assert.AreEqual(0, probe.ContentEditableCount, "The canvas input pipeline must not create contenteditable DOM authority.");
        Assert.AreEqual("phase-8-canvas-typing-ime", probe.ModelDocumentId);
        Assert.IsTrue(probe.InputRevision >= 10, $"Expected keyboard input revisions, actual: {probe.InputRevision}.");
        Assert.IsTrue(probe.LastInputDurationMs <= 16, $"Immediate input repaint should stay within the 16 ms target. Actual: {probe.LastInputDurationMs} ms.");
        Assert.AreEqual("deleteBackward", probe.LastInputOperation);
        Assert.IsTrue(probe.IncrementalRepaint, "Typing must use the dirty-block incremental repaint path.");

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

        var output = CreateOutputDirectory("phase8-typing", "desktop-1440x1000");
        var fullPath = Path.Combine(output, "00-phase8-typing-full.png");
        var pagePath = Path.Combine(output, "01-phase8-typing-page.png");
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = fullPath,
            FullPage = true,
            Type = ScreenshotType.Png
        });
        await page.GetByTestId("document-canvas-page").First.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = pagePath,
            Type = ScreenshotType.Png
        });

        var manifestPath = Path.Combine(output, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            testName = nameof(Phase8_Typing_UsesHiddenInputAndUpdatesCanvasModel),
            viewport = "desktop-1440x1000",
            seedDocumentId = "phase-8-canvas-typing-ime",
            userActions = new[]
            {
                "Open /canvas-engine-host with the phase 8 typing seed document.",
                "Click the measured canvas text at the end of the first paragraph.",
                "Type text with the real Playwright keyboard, press Enter, type a second paragraph, press Shift+Enter, type a soft-break suffix, then press Backspace."
            },
            expectedVisibleChanges = "Typed text appears at the canvas caret, Enter creates a new paragraph, Shift+Enter keeps a soft line break inside the same block, and Backspace deletes one grapheme.",
            expectedModelChanges = "The canvas model text is updated through the hidden textarea input bridge without contenteditable DOM mutations.",
            screenshotPaths = new[] { fullPath, pagePath },
            probe,
            uxReviewerNotes = "Typing should feel immediate and document-like: caret stays anchored, text appears in the expected paragraph, and the page does not jump."
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        TestContext.AddResultFile(fullPath);
        TestContext.AddResultFile(pagePath);
        TestContext.AddResultFile(manifestPath);
    }

    private async Task OpenTypingDocumentAsync(IPage page)
    {
        await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId=phase-8-canvas-typing-ime");
        await page.WaitForSelectorAsync(
            "[data-testid='document-canvas-engine-host'][data-canvas-engine-ready='true']",
            new PageWaitForSelectorOptions { Timeout = 30_000 });
        await page.WaitForFunctionAsync(
            """
            () => document.querySelectorAll('[data-canvas-text-rect][data-block-id="canvas-typing-body"]').length >= 1
                && document.querySelector('[data-testid="document-canvas-hidden-input"]')
            """,
            new PageWaitForFunctionOptions { Timeout = 30_000 });
    }

    private static async Task WaitForMirrorTextAsync(IPage page, string blockId, string expected)
    {
        try
        {
            await page.WaitForFunctionAsync(
                """
                ([blockId, expected]) => {
                    const block = document.querySelector(`[data-testid="document-canvas-a11y-mirror"] [data-block-id="${blockId}"]`);
                    return block && block.textContent === expected;
                }
                """,
                new object[] { blockId, expected },
                new PageWaitForFunctionOptions { Timeout = 30_000 });
        }
        catch (TimeoutException)
        {
            var blocks = await page.EvaluateAsync<object>(
                """
                () => Array.from(document.querySelectorAll('[data-testid="document-canvas-a11y-mirror"] [data-block-id]'))
                    .map(block => ({ id: block.getAttribute('data-block-id'), text: block.textContent }))
                """);
            Assert.Fail($"Timed out waiting for block '{blockId}' to equal '{expected}'. Current mirror blocks: {JsonSerializer.Serialize(blocks)}");
        }
    }

    private static Task<CanvasPoint> ReadTextPointAsync(IPage page, string blockId, bool useRightEdge)
        => page.EvaluateAsync<CanvasPoint>(
            """
            ([blockId, useRightEdge]) => {
                const rects = Array.from(document.querySelectorAll(`[data-canvas-text-rect][data-block-id="${blockId}"]`));
                const node = rects[rects.length - 1];
                const rect = node.getBoundingClientRect();
                return {
                    x: useRightEdge ? rect.right - 1 : rect.left + Math.max(2, rect.width / 2),
                    y: rect.top + rect.height / 2
                };
            }
            """,
            new object[] { blockId, useRightEdge });

    private static Task<CanvasTypingProbe> ReadTypingProbeAsync(IPage page)
        => page.EvaluateAsync<CanvasTypingProbe>(
            """
            () => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                const pageElement = document.querySelector('[data-testid="document-canvas-page"]');
                return {
                    modelDocumentId: pageElement?.getAttribute('data-canvas-model-document-id') || '',
                    contentEditableCount: document.querySelectorAll('[contenteditable]').length,
                    inputRevision: Number(root?.getAttribute('data-canvas-input-revision') || '0'),
                    lastInputOperation: root?.getAttribute('data-canvas-input-operation') || '',
                    lastInputDurationMs: Number(root?.getAttribute('data-canvas-input-render-duration-ms') || '0'),
                    incrementalRepaint: root?.getAttribute('data-canvas-input-incremental-repaint') === 'true'
                };
            }
            """);

    private static string CreateOutputDirectory(string phase, string viewport)
    {
        var output = Path.Combine(
            FindRepositoryRoot().FullName,
            "tests",
            "Tempo.Blazor.E2E",
            "TestResults",
            "document-editor-canvas",
            phase,
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

    private sealed class CanvasPoint
    {
        [JsonPropertyName("x")]
        public double X { get; set; }

        [JsonPropertyName("y")]
        public double Y { get; set; }
    }

    private sealed class CanvasTypingProbe
    {
        [JsonPropertyName("modelDocumentId")]
        public string ModelDocumentId { get; set; } = string.Empty;

        [JsonPropertyName("contentEditableCount")]
        public int ContentEditableCount { get; set; }

        [JsonPropertyName("inputRevision")]
        public int InputRevision { get; set; }

        [JsonPropertyName("lastInputOperation")]
        public string LastInputOperation { get; set; } = string.Empty;

        [JsonPropertyName("lastInputDurationMs")]
        public double LastInputDurationMs { get; set; }

        [JsonPropertyName("incrementalRepaint")]
        public bool IncrementalRepaint { get; set; }
    }
}
