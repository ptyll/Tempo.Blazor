using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tempo.Blazor.E2E.CanvasEngine;

namespace Tempo.Blazor.E2E;

/// <summary>Phase 11 E2E coverage for canvas clipboard copy, cut, paste, rich paste, and debug diagnostics.</summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasClipboardE2ETests : WasmTestBase
{
    [TestMethod]
    public async Task Phase11_Clipboard_CopyCutPasteRichHtmlAndDebugSnapshot()
    {
        var context = await CreateContextAsync();
        await context.GrantPermissionsAsync(
            ["clipboard-read", "clipboard-write"],
            new BrowserContextGrantPermissionsOptions { Origin = BaseUrl });
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenClipboardDocumentAsync(page);

        var output = CreateOutputDirectory("desktop-1440x1000");
        var beforePath = Path.Combine(output, "00-phase11-before.png");
        var richPath = Path.Combine(output, "01-phase11-rich-paste.png");

        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = beforePath,
            Type = ScreenshotType.Png
        });

        await DragCanvasRangeAsync(page, "canvas-clipboard-source", 5, "canvas-clipboard-source", 19);
        await FocusHiddenCanvasInputAsync(page);
        await page.Keyboard.PressAsync("Control+C");
        var copiedText = await page.EvaluateAsync<string>("() => navigator.clipboard.readText()");
        Assert.AreEqual("this formatted", copiedText);
        await WaitForClipboardOperationAsync(page, "copy");

        await ClickCanvasBlockAsync(page, "canvas-clipboard-target", 14);
        await FocusHiddenCanvasInputAsync(page);
        await page.Keyboard.PressAsync("Control+V");
        await WaitForA11yTextAsync(page, "Paste target: this formatted");
        await WaitForClipboardOperationAsync(page, "paste-internal");

        await DragCanvasRangeAsync(page, "canvas-clipboard-cut-source", 0, "canvas-clipboard-cut-source", 9);
        await FocusHiddenCanvasInputAsync(page);
        await page.Keyboard.PressAsync("Control+X");
        await WaitForA11yTextAsync(page, "sentence with one undo transaction.");
        await WaitForClipboardOperationAsync(page, "cut");
        await page.GetByTestId("document-undo").ClickAsync();
        await WaitForA11yTextAsync(page, "Cut this sentence with one undo transaction.");
        await page.GetByTestId("document-undo").ClickAsync();
        await WaitForA11yTextToDisappearAsync(page, "Paste target: this formatted");

        await page.EvaluateAsync("value => navigator.clipboard.writeText(value)", "plain canvas paste");
        await ClickCanvasBlockAsync(page, "canvas-clipboard-target", 14);
        await FocusHiddenCanvasInputAsync(page);
        await page.Keyboard.PressAsync("Control+V");
        await WaitForA11yTextAsync(page, "Paste target: plain canvas paste");
        await WaitForClipboardOperationAsync(page, "paste-plain");

        await ClickCanvasBlockAsync(page, "canvas-clipboard-rich-target", 19);
        await DispatchRichPasteAsync(page);
        await WaitForA11yTextAsync(page, "Rich paste target: Approved rich term");
        await WaitForClipboardOperationAsync(page, "paste-html");
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = richPath,
            Type = ScreenshotType.Png
        });

        await page.GetByTestId("document-ribbon-tab-view").ClickAsync();
        await page.GetByTestId("document-view-clipboard-html").EvaluateAsync("button => button.click()");
        await Assertions.Expect(page.GetByTestId("document-clipboard-normalized-debug-content")).ToContainTextAsync("Approved rich term", new() { Timeout = 5_000 });
        await Assertions.Expect(page.GetByTestId("document-clipboard-normalized-debug-content")).Not.ToContainTextAsync("script", new() { Timeout = 5_000 });
        await page.GetByTestId("document-clipboard-html-debug-close").ClickAsync();

        await DocumentEditorCanvasVisualAssert.AssertNoTextOverlapAsync(page);
        await DocumentEditorCanvasVisualAssert.AssertNoUiOverlapAsync(page);
        var contentMetrics = await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(page.Locator("[data-canvas-layer='content']").First);
        var probe = await ReadClipboardProbeAsync(page);
        Assert.AreEqual("paste-html", probe.Operation);
        Assert.AreEqual("html", probe.Source);
        Assert.IsTrue(probe.BlockCount >= 1);

        var manifestPath = Path.Combine(output, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            testName = nameof(Phase11_Clipboard_CopyCutPasteRichHtmlAndDebugSnapshot),
            viewport = "desktop-1440x1000",
            seedDocumentId = "phase-11-canvas-clipboard",
            userActions = new[]
            {
                "Select canvas text by dragging over text rects and copy with the real browser Control+C clipboard path.",
                "Paste the internal Tempo fragment with Control+V into a canvas paragraph.",
                "Cut selected canvas text with Control+X, then undo the cut and undo the previous paste as separate single transactions.",
                "Write plain text into the system clipboard and paste it with Control+V.",
                "Dispatch a browser ClipboardEvent with sanitized rich HTML and open the clipboard debug modal."
            },
            expectedVisibleChanges = "Copied and pasted text appears in the target paragraph, cut text disappears and undo restores it, plain text paste remains clean, and rich pasted text is rendered consistently without broken HTML artifacts.",
            expectedModelChanges = "Clipboard diagnostics report copy, paste-internal, cut, paste-plain, and paste-html operations; undo history restores cut and paste boundaries one transaction at a time.",
            screenshotPaths = new[] { beforePath, richPath },
            contentMetrics,
            probe,
            uxReviewerNotes = "The rich paste screenshot should read like native document text: no raw HTML text, no overlapping toolbar/modal controls, and no script/style residue."
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        TestContext.AddResultFile(beforePath);
        TestContext.AddResultFile(richPath);
        TestContext.AddResultFile(manifestPath);
    }

    private async Task OpenClipboardDocumentAsync(IPage page)
    {
        await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId=phase-11-canvas-clipboard&showToolbar=true");
        await page.WaitForSelectorAsync(
            "[data-testid='document-canvas-engine-host'][data-canvas-engine-ready='true']",
            new PageWaitForSelectorOptions { Timeout = 30_000 });
        await page.WaitForFunctionAsync(
            """
            () => document.querySelectorAll('[data-canvas-text-rect][data-block-id="canvas-clipboard-source"]').length >= 1
            """,
            new PageWaitForFunctionOptions { Timeout = 30_000 });
    }

    private static async Task DragCanvasRangeAsync(IPage page, string startBlockId, int startOffset, string endBlockId, int endOffset)
    {
        var start = await ReadCanvasPointAsync(page, startBlockId, startOffset);
        var end = await ReadCanvasPointAsync(page, endBlockId, endOffset);
        await page.Mouse.MoveAsync((float)start.X, (float)start.Y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)end.X, (float)end.Y, new MouseMoveOptions { Steps = 8 });
        await page.Mouse.UpAsync();
        await page.WaitForFunctionAsync(
            """
            () => document.querySelector('[data-testid="document-canvas-engine-root"]')
                ?.getAttribute('data-canvas-selection-collapsed') === 'false'
            """,
            new PageWaitForFunctionOptions { Timeout = 10_000 });
    }

    private static async Task ClickCanvasBlockAsync(IPage page, string blockId, int offset)
    {
        var point = await ReadCanvasPointAsync(page, blockId, offset);
        await page.Mouse.ClickAsync((float)point.X, (float)point.Y);
        await page.WaitForFunctionAsync(
            """
            blockId => document.querySelector('[data-testid="document-canvas-engine-root"]')
                ?.getAttribute('data-canvas-selection-focus-block-id') === blockId
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
                const target = rects.find(item => offset >= item.start && offset <= item.end) || rects[0];
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
            """
            () => document.querySelector('[data-testid="document-canvas-hidden-input"]')?.focus()
            """);

    private static Task DispatchRichPasteAsync(IPage page)
        => page.EvaluateAsync(
            """
            () => {
                const input = document.querySelector('[data-testid="document-canvas-hidden-input"]');
                const data = new DataTransfer();
                data.setData('text/html', '<p><strong>Approved</strong> <span style="color:#2563eb">rich term</span><script>bad()</script></p>');
                data.setData('text/plain', 'Approved rich term');
                input.dispatchEvent(new ClipboardEvent('paste', { bubbles: true, cancelable: true, clipboardData: data }));
            }
            """);

    private static Task WaitForA11yTextAsync(IPage page, string text)
        => page.WaitForFunctionAsync(
            """
            text => document.querySelector('[data-testid="document-canvas-a11y-mirror"]')?.textContent.includes(text)
            """,
            text,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task WaitForA11yTextToDisappearAsync(IPage page, string text)
        => page.WaitForFunctionAsync(
            """
            text => !document.querySelector('[data-testid="document-canvas-a11y-mirror"]')?.textContent.includes(text)
            """,
            text,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task WaitForClipboardOperationAsync(IPage page, string operation)
        => page.WaitForFunctionAsync(
            """
            operation => document.querySelector('[data-testid="document-canvas-engine-root"]')
                ?.getAttribute('data-canvas-clipboard-operation') === operation
            """,
            operation,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task<ClipboardProbe> ReadClipboardProbeAsync(IPage page)
        => page.EvaluateAsync<ClipboardProbe>(
            """
            () => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                const attr = name => root?.getAttribute(name) || '';
                return {
                    operation: attr('data-canvas-clipboard-operation'),
                    source: attr('data-canvas-clipboard-source'),
                    blockCount: Number(attr('data-canvas-clipboard-block-count') || '0'),
                    warningCount: Number(attr('data-canvas-clipboard-warning-count') || '0')
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
            "phase11-clipboard",
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

    private sealed class ClipboardProbe
    {
        [JsonPropertyName("operation")]
        public string Operation { get; set; } = string.Empty;

        [JsonPropertyName("source")]
        public string Source { get; set; } = string.Empty;

        [JsonPropertyName("blockCount")]
        public int BlockCount { get; set; }

        [JsonPropertyName("warningCount")]
        public int WarningCount { get; set; }
    }
}
