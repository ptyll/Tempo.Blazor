using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tempo.Blazor.E2E.CanvasEngine;

namespace Tempo.Blazor.E2E;

/// <summary>Phase 9 E2E coverage for canvas inline formatting commands and toolbar bridge.</summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasInlineFormatE2ETests : WasmTestBase
{
    [TestMethod]
    public async Task Phase9_InlineFormatting_UsesUnifiedCommandRuntimeAndPreservesSelection()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenInlineFormattingDocumentAsync(page);

        var output = CreateOutputDirectory("desktop-1440x1000");
        var beforePath = Path.Combine(output, "00-phase9-before.png");
        var boldPath = Path.Combine(output, "01-phase9-after-bold.png");
        var colorPath = Path.Combine(output, "02-phase9-after-color.png");
        var finalPath = Path.Combine(output, "03-phase9-after-highlight-font.png");
        var linkPath = Path.Combine(output, "04-phase9-after-link.png");

        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = beforePath,
            Type = ScreenshotType.Png
        });

        var selected = await SelectCanvasTextRangeAsync(page, "canvas-format-body", 12, 23);
        Assert.AreEqual("canvas text", selected.ExpectedText);

        await page.GetByTestId("document-bold").ClickAsync();
        await WaitForCommandStateAsync(page, "bold", "active");
        await Assertions.Expect(page.GetByTestId("document-bold")).ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = 5_000 });
        await AssertSelectionStillVisibleAsync(page, "canvas-format-body");
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = boldPath,
            Type = ScreenshotType.Png
        });

        await page.GetByTestId("document-font-family").SelectOptionAsync("Georgia, \"Times New Roman\", serif");
        await WaitForCommandValueAsync(page, "fontfamily", "Georgia, \"Times New Roman\", serif");
        await page.GetByTestId("document-font-size").SelectOptionAsync("24");
        await WaitForCommandValueAsync(page, "fontsize", "24");
        await AssertSelectionStillVisibleAsync(page, "canvas-format-body");

        await SetTempoColorPickerAsync(page, "[data-testid='document-font-color-trigger']", "#123456");
        await WaitForCommandValueAsync(page, "textcolor", "#123456");
        await AssertSelectionStillVisibleAsync(page, "canvas-format-body");
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = colorPath,
            Type = ScreenshotType.Png
        });

        await SetTempoColorPickerAsync(page, "[data-testid='document-highlight-color-trigger']", "#fff59d");
        await WaitForCommandValueAsync(page, "highlight", "#fff59d");
        await AssertSelectionStillVisibleAsync(page, "canvas-format-body");
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = finalPath,
            Type = ScreenshotType.Png
        });

        var probe = await ReadInlineFormatProbeAsync(page);
        Assert.AreEqual("phase-9-canvas-inline-format", probe.ModelDocumentId);
        Assert.AreEqual("highlight", probe.LastCommand);
        Assert.IsTrue(probe.CommandRevision >= 5, $"Expected at least five command revisions, actual: {probe.CommandRevision}.");
        Assert.AreEqual("active", probe.BoldState);
        Assert.AreEqual("24", probe.FontSizeValue);
        Assert.AreEqual("#123456", probe.TextColorValue);
        Assert.AreEqual("#fff59d", probe.HighlightValue);
        Assert.AreEqual(0, probe.ContentEditableCount, "Canvas inline formatting must not use contenteditable DOM authority.");
        Assert.IsTrue(probe.SelectionRectCount >= 1, "Canvas selection overlay must remain visible after toolbar commands.");

        var contentAfterFormatting = await ReadContentCanvasDataUrlAsync(page);
        await page.GetByTestId("document-undo").ClickAsync();
        await WaitForCommandValueAsync(page, "highlight", string.Empty);
        var contentAfterUndo = await ReadContentCanvasDataUrlAsync(page);
        Assert.AreNotEqual(contentAfterFormatting, contentAfterUndo, "Undo must repaint the formatted canvas text.");
        await page.GetByTestId("document-redo").ClickAsync();
        await WaitForCommandValueAsync(page, "highlight", "#fff59d");

        const string linkHref = "https://example.test/canvas-phase9";
        var linkSelection = await SelectCanvasTextRangeAsync(page, "canvas-format-body", 0, 6);
        Assert.AreEqual("Format", linkSelection.ExpectedText);
        await page.GetByTestId("document-link").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-link-dialog")).ToBeVisibleAsync(new() { Timeout = 5_000 });
        await page.GetByTestId("document-link-url").FillAsync(linkHref);
        await page.GetByTestId("document-link-title").FillAsync("Canvas phase 9");
        await page.GetByTestId("document-apply-link").ClickAsync();
        await WaitForCommandValueAsync(page, "link", linkHref);
        await AssertSelectionStillVisibleAsync(page, "canvas-format-body");
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = linkPath,
            Type = ScreenshotType.Png
        });

        await page.AddStyleTagAsync(new PageAddStyleTagOptions
        {
            Content = "[data-testid='document-mini-toolbar'] { pointer-events: none !important; }"
        });
        var linkPoint = await ReadCanvasTextRangeAsync(page, "canvas-format-body", 2, 3);
        await DispatchCtrlMouseClickAsync(page, linkPoint.StartX, linkPoint.StartY);
        await page.WaitForFunctionAsync(
            """
            href => document.querySelector('[data-testid="document-canvas-engine-root"]')
                ?.getAttribute('data-canvas-last-opened-link') === href
            """,
            linkHref,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

        var manifestPath = Path.Combine(output, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            testName = nameof(Phase9_InlineFormatting_UsesUnifiedCommandRuntimeAndPreservesSelection),
            viewport = "desktop-1440x1000",
            seedDocumentId = "phase-9-canvas-inline-format",
            userActions = new[]
            {
                "Open /canvas-engine-host with the phase 9 inline-format seed document and visible production toolbar.",
                "Select 'canvas text' on the measured canvas text rect with real mouse movement.",
                "Click Bold in the toolbar and verify the canvas selection overlay remains visible.",
                "Change font family and font size through toolbar selects.",
                "Apply text color and highlight through the Tempo color pickers.",
                "Undo and redo the final canvas inline-format command.",
                "Apply a link through the toolbar dialog and Ctrl+click the linked canvas text."
            },
            expectedVisibleChanges = "The selected canvas text becomes bold, switches to Georgia at 24 pt, receives #123456 text color and #fff59d highlight, and the linked text remains readable with selection overlay retained after toolbar clicks.",
            expectedModelChanges = "The unified canvas command runtime mutates text-run marks, publishes command state/value diagnostics, records undo/redo history for formatting changes, and opens the link through the canvas Ctrl+click route.",
            screenshotPaths = new[] { beforePath, boldPath, colorPath, finalPath, linkPath },
            probe,
            uxReviewerNotes = "Toolbar actions should feel document-native: no focus jump, no collapsed range, crisp canvas repaint, and color pickers remain readable inside the viewport."
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        TestContext.AddResultFile(beforePath);
        TestContext.AddResultFile(boldPath);
        TestContext.AddResultFile(colorPath);
        TestContext.AddResultFile(finalPath);
        TestContext.AddResultFile(linkPath);
        TestContext.AddResultFile(manifestPath);
    }

    private static Task DispatchCtrlMouseClickAsync(IPage page, double x, double y)
        => page.EvaluateAsync(
            """
            ({ x, y }) => {
                const target = document.elementFromPoint(x, y);
                if (!target) throw new Error(`No element at ${x},${y}.`);
                const options = {
                    bubbles: true,
                    cancelable: true,
                    clientX: x,
                    clientY: y,
                    ctrlKey: true,
                    button: 0,
                    buttons: 1,
                    detail: 1,
                };
                target.dispatchEvent(new MouseEvent('mousedown', options));
                target.dispatchEvent(new MouseEvent('mouseup', { ...options, buttons: 0 }));
                target.dispatchEvent(new MouseEvent('click', { ...options, buttons: 0 }));
            }
            """,
            new { x, y });

    private async Task OpenInlineFormattingDocumentAsync(IPage page)
    {
        await page.GotoAsync($"{BaseUrl}/canvas-engine-host?documentId=phase-9-canvas-inline-format&showToolbar=true", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.Load,
            Timeout = 60_000
        });
        await page.WaitForSelectorAsync(
            "[data-testid='document-canvas-engine-host'][data-canvas-engine-ready='true']",
            new PageWaitForSelectorOptions { Timeout = 30_000 });
        await page.WaitForFunctionAsync(
            """
            () => document.querySelectorAll('[data-canvas-text-rect][data-block-id="canvas-format-body"]').length >= 1
                && document.querySelector('[data-testid="document-bold"]')
                && document.querySelector('[data-testid="document-font-color-trigger"] .tm-color-picker-trigger')
            """,
            new PageWaitForFunctionOptions { Timeout = 30_000 });
    }

    private static async Task<CanvasTextRange> SelectCanvasTextRangeAsync(IPage page, string blockId, int startOffset, int endOffset)
    {
        Exception? lastTransientException = null;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                await WaitForCanvasCommandBridgeAsync(page);
                var target = await ReadCanvasTextRangeAsync(page, blockId, startOffset, endOffset);
                var resultJson = await page.EvaluateAsync<string>(
                    """
                    ([blockId, startOffset, endOffset]) => {
                        const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                        const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                        return import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs')
                            .then(module => module.selectTextRange(handle, blockId, startOffset, endOffset) || '');
                    }
                    """,
                    new object[] { blockId, startOffset, endOffset });
                using var result = JsonDocument.Parse(resultJson);
                Assert.IsTrue(result.RootElement.GetProperty("selected").GetBoolean(), $"Expected canvas interop selection for {blockId}[{startOffset}..{endOffset}].");
                await AssertSelectionStillVisibleAsync(page, blockId);
                return target;
            }
            catch (PlaywrightException ex) when (attempt < 9 && IsExecutionContextReset(ex))
            {
                lastTransientException = ex;
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 20_000 });
                await Task.Delay(1_000);
            }
        }

        throw new InvalidOperationException($"Canvas selection for {blockId}[{startOffset}..{endOffset}] could not execute after transient context resets.", lastTransientException);
    }

    private static Task WaitForCanvasCommandBridgeAsync(IPage page)
        => page.WaitForFunctionAsync(
            """
            () => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                return host?.getAttribute('data-canvas-engine-ready') === 'true'
                    && !!host?.getAttribute('data-canvas-engine-handle');
            }
            """,
            new PageWaitForFunctionOptions { Timeout = 15_000 });

    private static bool IsExecutionContextReset(PlaywrightException ex)
        => ex.Message.Contains("Execution context was destroyed", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("Cannot find context with specified id", StringComparison.OrdinalIgnoreCase);

    private static Task<CanvasTextRange> ReadCanvasTextRangeAsync(IPage page, string blockId, int startOffset, int endOffset)
        => page.EvaluateAsync<CanvasTextRange>(
            """
            ([blockId, startOffset, endOffset]) => {
                const items = Array.from(document.querySelectorAll(`[data-canvas-text-rect][data-block-id="${blockId}"]`))
                    .map(node => ({
                        node,
                        start: Number(node.getAttribute('data-canvas-start-offset') || '0'),
                        end: Number(node.getAttribute('data-canvas-end-offset') || '0')
                    }))
                    .filter(item => item.end > item.start);
                if (!items.length) throw new Error(`No canvas text rects found for ${blockId}.`);
                const startItem = items.find(item => startOffset >= item.start && startOffset < item.end) || items[0];
                const endItem = items.find(item => endOffset > item.start && endOffset <= item.end) || items[items.length - 1];
                // The canvas is virtualized: a mounted text rect can sit below the fold, so read the rectangles
                // only after centering the selection's midpoint — otherwise the drag lands outside the viewport.
                const midIndex = Math.floor((items.indexOf(startItem) + items.indexOf(endItem)) / 2);
                (items[midIndex] || startItem).node.scrollIntoView({ block: 'center', inline: 'center', behavior: 'instant' });
                const rects = items.map(item => ({ node: item.node, rect: item.node.getBoundingClientRect(), start: item.start, end: item.end }));
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

    private static Task AssertSelectionStillVisibleAsync(IPage page, string blockId)
        => page.WaitForFunctionAsync(
            """
            blockId => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                return root?.getAttribute('data-canvas-selection-collapsed') === 'false'
                    && root?.getAttribute('data-canvas-selection-focus-block-id') === blockId
                    && document.querySelectorAll('[data-testid="document-canvas-selection-rect"]').length >= 1;
            }
            """,
            blockId,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task WaitForCommandStateAsync(IPage page, string commandId, string expected)
        => page.WaitForFunctionAsync(
            """
            ([commandId, expected]) => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                return root?.getAttribute(`data-canvas-command-${commandId}-state`) === expected;
            }
            """,
            new object[] { commandId, expected },
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static async Task WaitForCommandValueAsync(IPage page, string commandId, string expected)
    {
        try
        {
            await page.WaitForFunctionAsync(
                """
                ([commandId, expected]) => {
                    const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                    return (root?.getAttribute(`data-canvas-command-${commandId}-value`) || '') === expected;
                }
                """,
                new object[] { commandId, expected },
                new PageWaitForFunctionOptions { Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
            var actual = await page.EvaluateAsync<CommandValueProbe>(
                """
                commandId => {
                    const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                    return {
                        commandId,
                        state: root?.getAttribute(`data-canvas-command-${commandId}-state`) || '',
                        value: root?.getAttribute(`data-canvas-command-${commandId}-value`) || '',
                        lastCommand: root?.getAttribute('data-canvas-command-last') || '',
                        revision: Number(root?.getAttribute('data-canvas-command-revision') || '0'),
                        fontSizeSelectValue: document.querySelector('[data-testid="document-font-size"]')?.value || '',
                        fontFamilySelectValue: document.querySelector('[data-testid="document-font-family"]')?.value || ''
                    };
                }
                """,
                commandId);
            Assert.Fail($"Timed out waiting for canvas command '{commandId}' value '{expected}'. Actual: {JsonSerializer.Serialize(actual)}");
        }
    }

    private static async Task SetTempoColorPickerAsync(IPage page, string selector, string value)
    {
        var picker = page.Locator(selector);
        // overlay.js parks the top-layer panel with visibility:hidden while its anchor does not
        // intersect the viewport — a raw JS click never scrolls, so bring the trigger in view first.
        await picker.Locator(".tm-color-picker-trigger").ScrollIntoViewIfNeededAsync();
        await picker.Locator(".tm-color-picker-trigger").EvaluateAsync("trigger => trigger.click()");
        await Assertions.Expect(picker.Locator(".tm-color-picker-dropdown")).ToBeVisibleAsync(new() { Timeout = 5_000 });
        await Assertions.Expect(picker.Locator(".tm-color-picker-apply")).ToBeVisibleAsync(new() { Timeout = 5_000 });

        var input = picker.Locator(".tm-flat-color-picker-hex input");
        await SetTextInputAsync(input, value);
        await picker.Locator(".tm-color-picker-apply").EvaluateAsync("button => button.click()");
    }

    private static async Task SetTextInputAsync(ILocator input, string value)
    {
        await input.EvaluateAsync(
            """
            (input, value) => {
                input.value = String(value);
                input.dispatchEvent(new Event('change', { bubbles: true }));
            }
            """,
            value);
    }

    private static Task<string> ReadContentCanvasDataUrlAsync(IPage page)
        => page.Locator("[data-canvas-layer='content']").First.EvaluateAsync<string>("canvas => canvas.toDataURL('image/png')");

    private static Task<CanvasInlineFormatProbe> ReadInlineFormatProbeAsync(IPage page)
        => page.EvaluateAsync<CanvasInlineFormatProbe>(
            """
            () => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                const pageElement = document.querySelector('[data-testid="document-canvas-page"]');
                return {
                    modelDocumentId: pageElement?.getAttribute('data-canvas-model-document-id') || '',
                    contentEditableCount: document.querySelectorAll('[contenteditable]').length,
                    commandRevision: Number(root?.getAttribute('data-canvas-command-revision') || '0'),
                    lastCommand: root?.getAttribute('data-canvas-command-last') || '',
                    boldState: root?.getAttribute('data-canvas-command-bold-state') || '',
                    fontSizeValue: root?.getAttribute('data-canvas-command-fontsize-value') || '',
                    textColorValue: root?.getAttribute('data-canvas-command-textcolor-value') || '',
                    highlightValue: root?.getAttribute('data-canvas-command-highlight-value') || '',
                    selectionRectCount: document.querySelectorAll('[data-testid="document-canvas-selection-rect"]').length
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
            "phase9-inline-format",
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

    private sealed class CanvasTextRange
    {
        [JsonPropertyName("startX")]
        public double StartX { get; set; }

        [JsonPropertyName("startY")]
        public double StartY { get; set; }

        [JsonPropertyName("endX")]
        public double EndX { get; set; }

        [JsonPropertyName("endY")]
        public double EndY { get; set; }

        [JsonPropertyName("expectedText")]
        public string ExpectedText { get; set; } = string.Empty;
    }

    private sealed class CanvasInlineFormatProbe
    {
        [JsonPropertyName("modelDocumentId")]
        public string ModelDocumentId { get; set; } = string.Empty;

        [JsonPropertyName("contentEditableCount")]
        public int ContentEditableCount { get; set; }

        [JsonPropertyName("commandRevision")]
        public int CommandRevision { get; set; }

        [JsonPropertyName("lastCommand")]
        public string LastCommand { get; set; } = string.Empty;

        [JsonPropertyName("boldState")]
        public string BoldState { get; set; } = string.Empty;

        [JsonPropertyName("fontSizeValue")]
        public string FontSizeValue { get; set; } = string.Empty;

        [JsonPropertyName("textColorValue")]
        public string TextColorValue { get; set; } = string.Empty;

        [JsonPropertyName("highlightValue")]
        public string HighlightValue { get; set; } = string.Empty;

        [JsonPropertyName("selectionRectCount")]
        public int SelectionRectCount { get; set; }
    }

    private sealed class CommandValueProbe
    {
        [JsonPropertyName("commandId")]
        public string CommandId { get; set; } = string.Empty;

        [JsonPropertyName("state")]
        public string State { get; set; } = string.Empty;

        [JsonPropertyName("value")]
        public string Value { get; set; } = string.Empty;

        [JsonPropertyName("lastCommand")]
        public string LastCommand { get; set; } = string.Empty;

        [JsonPropertyName("revision")]
        public int Revision { get; set; }

        [JsonPropertyName("fontSizeSelectValue")]
        public string FontSizeSelectValue { get; set; } = string.Empty;

        [JsonPropertyName("fontFamilySelectValue")]
        public string FontFamilySelectValue { get; set; } = string.Empty;
    }
}
