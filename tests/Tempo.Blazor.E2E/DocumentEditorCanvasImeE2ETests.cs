using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tempo.Blazor.E2E.CanvasEngine;

namespace Tempo.Blazor.E2E;

/// <summary>Phase 8 E2E coverage for canvas IME composition preview and commit.</summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasImeE2ETests : WasmTestBase
{
    [TestMethod]
    public async Task Phase8_Ime_CompositionPreviewIsRenderedAndCommitted()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenTypingDocumentAsync(page);

        var startPoint = await ReadTextPointAsync(page, "canvas-typing-body");
        await page.Mouse.ClickAsync((float)startPoint.X, (float)startPoint.Y);
        await page.Keyboard.TypeAsync(" Hi");
        await WaitForMirrorTextAsync(page, "canvas-typing-body", "Start Hi");

        await DispatchCompositionAsync(page, "compositionstart", "");
        await DispatchCompositionAsync(page, "compositionupdate", "か");
        await WaitForMirrorTextAsync(page, "canvas-typing-body", "Start Hiか");
        await DocumentEditorCanvasVisualAssert.AssertSelectionVisibleAsync(page.Locator("[data-testid='document-canvas-composition-underline']").First);

        await DispatchCompositionAsync(page, "compositionupdate", "かん");
        await WaitForMirrorTextAsync(page, "canvas-typing-body", "Start Hiかん");

        await DispatchCompositionAsync(page, "compositionupdate", "感");
        await WaitForMirrorTextAsync(page, "canvas-typing-body", "Start Hi感");

        await DispatchCompositionAsync(page, "compositionend", "感じ");
        await WaitForMirrorTextAsync(page, "canvas-typing-body", "Start Hi感じ");
        await WaitForPredicateAsync(page, "() => !document.querySelector('[data-testid=\"document-canvas-composition-underline\"]')", 30_000);

        var probe = await ReadImeProbeAsync(page);
        Assert.AreEqual("replaceRange", probe.LastInputOperation);
        Assert.IsFalse(probe.CompositionActive);
        Assert.IsTrue(probe.InputRevision >= 5, $"Expected IME input revisions, actual: {probe.InputRevision}.");

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

        var output = CreateOutputDirectory("phase8-ime", "desktop-1440x1000");
        var fullPath = Path.Combine(output, "00-phase8-ime-full.png");
        var pagePath = Path.Combine(output, "01-phase8-ime-page.png");
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
            testName = nameof(Phase8_Ime_CompositionPreviewIsRenderedAndCommitted),
            viewport = "desktop-1440x1000",
            seedDocumentId = "phase-8-canvas-typing-ime",
            userActions = new[]
            {
                "Open /canvas-engine-host with the phase 8 typing seed document.",
                "Place the canvas caret, type a short prefix through the real keyboard, then dispatch browser CompositionEvent updates to the hidden textarea.",
                "Verify each pre-edit string lives in the model, has a visible canvas overlay underline, and compositionend commits the final text."
            },
            expectedVisibleChanges = "IME preview text appears at the caret with a blue pre-edit underline; after commit the underline disappears and final text remains in the canvas render.",
            expectedModelChanges = "Composition update replaces the previous preview range and compositionend commits the final grapheme-safe text.",
            screenshotPaths = new[] { fullPath, pagePath },
            probe,
            uxReviewerNotes = "IME preview should read as real document text, not a detached debug overlay; the underline is crisp and aligned with the rendered glyphs."
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
        await WaitForPredicateAsync(page, """
            () => document.querySelectorAll('[data-canvas-text-rect][data-block-id="canvas-typing-body"]').length >= 1
                && document.querySelector('[data-testid="document-canvas-hidden-input"]')
            """, 30_000);
    }

    private static Task DispatchCompositionAsync(IPage page, string type, string data)
        => page.EvaluateAsync(
            """
            ([type, data]) => {
                const input = document.querySelector('[data-testid="document-canvas-hidden-input"]');
                input.focus({ preventScroll: true });
                input.dispatchEvent(new CompositionEvent(type, { data, bubbles: true, cancelable: true }));
            }
            """,
            new object[] { type, data });

    private static Task WaitForMirrorTextAsync(IPage page, string blockId, string expected)
        => page.WaitForFunctionAsync(
            """
            ([blockId, expected]) => {
                const block = document.querySelector(`[data-testid="document-canvas-a11y-mirror"] [data-block-id="${blockId}"]`);
                return block && block.textContent === expected;
            }
            """,
            new object[] { blockId, expected },
            new PageWaitForFunctionOptions { Timeout = 30_000 });

    private static Task<CanvasPoint> ReadTextPointAsync(IPage page, string blockId)
        => page.EvaluateAsync<CanvasPoint>(
            """
            ([blockId]) => {
                const rects = Array.from(document.querySelectorAll(`[data-canvas-text-rect][data-block-id="${blockId}"]`));
                const node = rects[rects.length - 1];
                const rect = node.getBoundingClientRect();
                return {
                    x: rect.right - 1,
                    y: rect.top + rect.height / 2
                };
            }
            """,
            new object[] { blockId });

    private static Task<CanvasImeProbe> ReadImeProbeAsync(IPage page)
        => page.EvaluateAsync<CanvasImeProbe>(
            """
            () => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                return {
                    inputRevision: Number(root?.getAttribute('data-canvas-input-revision') || '0'),
                    lastInputOperation: root?.getAttribute('data-canvas-input-operation') || '',
                    compositionActive: root?.getAttribute('data-canvas-composition-active') === 'true'
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

    private sealed class CanvasImeProbe
    {
        [JsonPropertyName("inputRevision")]
        public int InputRevision { get; set; }

        [JsonPropertyName("lastInputOperation")]
        public string LastInputOperation { get; set; } = string.Empty;

        [JsonPropertyName("compositionActive")]
        public bool CompositionActive { get; set; }
    }
}
