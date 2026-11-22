using System.Text.Json.Serialization;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// B4: canvas suggestion mode. Suggestion mode on the canvas engine is backed by track-changes — an edit
/// becomes a reviewable engine revision (inline overlay), surfaced in the suggestion panel, and accept/reject
/// routes to the engine revision review (no continuous C# mirror).
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasSuggestionModeE2ETests : WasmTestBase
{
    private const string DocumentId = "phase-10-canvas-paragraph";

    [TestMethod]
    public async Task B4_CanvasSuggestionMode_EditBecomesReviewableSuggestion()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);

        await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId={DocumentId}&showToolbar=true&suggestionMode=true", WaitUntilState.DOMContentLoaded);
        await page.WaitForSelectorAsync(
            "[data-testid='document-canvas-engine-host'][data-canvas-engine-ready='true']",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 30_000 });
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('[data-canvas-text-rect][data-block-id=\"canvas-paragraph-body\"]').length >= 1",
            null,
            new PageWaitForFunctionOptions { Timeout = 30_000 });

        var output = Path.Combine(Path.GetTempPath(), "canvas-b4-suggestion");
        Directory.CreateDirectory(output);

        // Slice 1: suggestion mode drives the engine's track-changes, so a plain edit is captured as a tracked
        // revision (the proposed change) rather than applied directly.
        await ClickCanvasBlockAsync(page, "canvas-paragraph-body", 8);
        await FocusHiddenCanvasInputAsync(page);
        // A single keystroke -> exactly one tracked insertion revision (track-changes records per character),
        // so accepting the single resulting suggestion fully clears the markers.
        await page.Keyboard.PressAsync("Z");
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('[data-testid=\"document-canvas-revision-marker\"]').length >= 1",
            null,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

        // Slice 2: the proposed change is surfaced in the suggestion panel (mapped from the engine revisions).
        await page.GetByTestId("document-side-panel-tab-revisions").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-suggestion-panel"))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(page.Locator("[data-testid='document-suggestion-item']").First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(output, "01-canvas-suggestion-created.png"),
            Type = ScreenshotType.Png
        });

        // Accepting the suggestion routes to the engine revision review -> the proposed change is applied and
        // its marker clears.
        await page.Locator("[data-testid='document-suggestion-accept']").First.ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid='document-canvas-revision-marker']"))
            .ToHaveCountAsync(0, new() { Timeout = 10_000 });
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(output, "02-canvas-suggestion-accepted.png"),
            Type = ScreenshotType.Png
        });
    }

    private static async Task ClickCanvasBlockAsync(IPage page, string blockId, int offset)
    {
        // Scroll the target line clear of the sticky toolbar before clicking (see B3.4) so the click reaches
        // the canvas and not a toolbar button.
        await page.EvaluateAsync(
            """
            ([blockId, offset]) => {
                const rects = Array.from(document.querySelectorAll(`[data-canvas-text-rect][data-block-id="${blockId}"]`));
                const node = rects.find(item =>
                    Number(item.getAttribute('data-canvas-start-offset') || '0') <= offset
                    && Number(item.getAttribute('data-canvas-end-offset') || '0') >= offset) || rects[0];
                node?.scrollIntoView({ block: 'center', inline: 'center', behavior: 'instant' });
            }
            """,
            new object[] { blockId, offset });
        var point = await ReadCanvasPointAsync(page, blockId, offset);
        await page.Mouse.ClickAsync((float)point.X, (float)point.Y);
        await page.WaitForFunctionAsync(
            """
            blockId => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                return root?.getAttribute('data-canvas-selection-focus-block-id') === blockId
                    && root?.getAttribute('data-canvas-selection-collapsed') === 'true'
                    && document.querySelectorAll('[data-testid="document-canvas-caret"]').length >= 1;
            }
            """,
            blockId,
            new PageWaitForFunctionOptions { Timeout = 10_000 });
    }

    private static Task<CanvasPoint> ReadCanvasPointAsync(IPage page, string blockId, int offset)
        => page.EvaluateAsync<CanvasPoint>(
            """
            ([blockId, offset]) => {
                const rects = Array.from(document.querySelectorAll(`[data-canvas-text-rect][data-block-id="${blockId}"]`))
                    .map(node => {
                        const rect = node.getBoundingClientRect();
                        const start = Number(node.getAttribute('data-canvas-start-offset') || '0');
                        const end = Number(node.getAttribute('data-canvas-end-offset') || '0');
                        return { rect, start, end };
                    })
                    .filter(item => item.end > item.start);
                if (!rects.length) throw new Error(`No canvas text rects found for ${blockId}.`);
                const target = rects.find(item => offset >= item.start && offset < item.end) || rects[0];
                const ratio = Math.max(0, Math.min(1, (offset - target.start) / Math.max(1, target.end - target.start)));
                return {
                    x: target.rect.left + Math.max(2, target.rect.width * ratio),
                    y: target.rect.top + target.rect.height / 2
                };
            }
            """,
            new object[] { blockId, offset });

    private static Task FocusHiddenCanvasInputAsync(IPage page)
        => page.EvaluateAsync(
            "() => document.querySelector('[data-testid=\"document-canvas-hidden-input\"]')?.focus()");

    private sealed class CanvasPoint
    {
        [JsonPropertyName("x")] public double X { get; set; }
        [JsonPropertyName("y")] public double Y { get; set; }
    }
}
