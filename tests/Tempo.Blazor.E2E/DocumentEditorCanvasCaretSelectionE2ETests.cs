using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tempo.Blazor.E2E.CanvasEngine;

namespace Tempo.Blazor.E2E;

/// <summary>Phase 7 E2E coverage for canvas hit testing, caret, and selection overlays.</summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasCaretSelectionE2ETests : WasmTestBase
{
    [TestMethod]
    public async Task Phase7_CaretAndSelection_UseOverlayWithoutRepaintingContent()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenCaretSelectionDocumentAsync(page);

        var firstPoint = await ReadTextPointAsync(page, "canvas-selection-body", 0);
        var contentBefore = await ReadContentCanvasDataUrlAsync(page);
        await page.Mouse.ClickAsync((float)firstPoint.X, (float)firstPoint.Y);
        await DocumentEditorCanvasVisualAssert.AssertCaretVisibleAsync(page.Locator("[data-testid='document-canvas-caret']").First);

        var afterClick = await ReadSelectionProbeAsync(page);
        Assert.IsTrue(afterClick.IsCollapsed);
        Assert.AreEqual("canvas-selection-body", afterClick.FocusBlockId);
        Assert.IsTrue(afterClick.FocusOffset > 0);

        await page.Keyboard.PressAsync("ArrowRight");
        var afterArrow = await ReadSelectionProbeAsync(page);
        Assert.IsTrue(afterArrow.IsCollapsed);
        Assert.AreEqual("canvas-selection-body", afterArrow.FocusBlockId);
        Assert.IsTrue(afterArrow.FocusOffset > afterClick.FocusOffset);

        await page.Keyboard.PressAsync("Shift+ArrowRight");
        await DocumentEditorCanvasVisualAssert.AssertSelectionVisibleAsync(page.Locator("[data-testid='document-canvas-selection-rect']").First);
        var afterShift = await ReadSelectionProbeAsync(page);
        Assert.IsFalse(afterShift.IsCollapsed);
        Assert.IsTrue(afterShift.SelectionRectCount >= 1);

        // Collapse the selection first: the floating mini toolbar of a range selection floats over the first line, and on a page the engine fitted to a narrow
        // column (phase 11) it sits right over the word the drag starts from - the press would land on the toolbar instead of the text.
        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(page.GetByTestId("document-mini-toolbar")).ToHaveCountAsync(0, new() { Timeout = 10_000 });

        var dragStart = await ReadTextPointAsync(page, "canvas-selection-body", 1);
        var dragEnd = await ReadTextPointAsync(page, "canvas-selection-second", 2);
        await page.Mouse.MoveAsync((float)dragStart.X, (float)dragStart.Y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)dragEnd.X, (float)dragEnd.Y, new MouseMoveOptions { Steps = 8 });
        await page.Mouse.UpAsync();
        await DocumentEditorCanvasVisualAssert.AssertSelectionVisibleAsync(page.Locator("[data-testid='document-canvas-selection-rect']").First);
        var afterDrag = await ReadSelectionProbeAsync(page);
        Assert.IsFalse(afterDrag.IsCollapsed);
        Assert.IsTrue(afterDrag.SelectionRectCount >= 2, $"Expected multi-line selection rects, actual: {afterDrag.SelectionRectCount}.");

        var contentAfter = await ReadContentCanvasDataUrlAsync(page);
        Assert.AreEqual(contentBefore, contentAfter, "Caret and selection movement must paint only the overlay layer, not the content cache.");

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

        var output = CreateOutputDirectory("desktop-1440x1000");
        var fullPath = Path.Combine(output, "00-phase7-full.png");
        var pagePath = Path.Combine(output, "01-phase7-canvas-page-selection.png");
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
            testName = nameof(Phase7_CaretAndSelection_UseOverlayWithoutRepaintingContent),
            viewport = "desktop-1440x1000",
            seedDocumentId = "phase-7-canvas-caret-selection",
            userActions = new[]
            {
                "Open /canvas-engine-host with the phase 7 caret and selection seed document.",
                "Click inside a measured text rect to place the canvas-owned caret.",
                "Use ArrowRight and Shift+ArrowRight from the hidden input bridge.",
                "Drag from the first paragraph into the second paragraph to paint a multi-line selection overlay."
            },
            expectedVisibleChanges = "The caret is visible at the clicked text position and the selection highlight follows measured text lines without visual drift.",
            expectedModelChanges = "Selection state changes only; content model and content canvas cache remain unchanged.",
            screenshotPaths = new[] { fullPath, pagePath },
            afterClick,
            afterArrow,
            afterShift,
            afterDrag,
            uxReviewerNotes = "Caret and selection should feel native: crisp vertical caret, calm blue selection overlay, no text/content repaint flicker."
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        TestContext.AddResultFile(fullPath);
        TestContext.AddResultFile(pagePath);
        TestContext.AddResultFile(manifestPath);
    }

    private async Task OpenCaretSelectionDocumentAsync(IPage page)
    {
        await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId=phase-7-canvas-caret-selection");
        await page.WaitForSelectorAsync(
            "[data-testid='document-canvas-engine-host'][data-canvas-engine-ready='true']",
            new PageWaitForSelectorOptions { Timeout = 30_000 });
        await WaitForPredicateAsync(page, """
            () => document.querySelectorAll('[data-canvas-text-rect][data-block-id="canvas-selection-body"]').length >= 3
                && document.querySelector('[data-testid="document-canvas-caret"]')
            """, 30_000);
    }

    [TestMethod]
    [Description("Phase 11 (fix round): on a page the engine FITTED to a 700 px column a click 6 px inside the right end of a text run puts the caret at the end of that run (the hit-test inverts the scale)")]
    public async Task Phase11_CaretHitTest_OnAFittedPage_LandsAtTheClickedTextEnd()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenCaretSelectionDocumentAsync(page);
        var scale = await DocumentEditorFittedScaleE2EHelpers.NarrowTheColumnAndWaitForTheFitAsync(page);

        var target = await page.EvaluateAsync<System.Text.Json.JsonElement>(
            """
            () => {
                const node = Array.from(document.querySelectorAll('[data-canvas-text-rect][data-block-id="canvas-selection-body"]'))
                    .filter(el => el.getBoundingClientRect().width > 40)[0];
                const rect = node.getBoundingClientRect();
                return { x: rect.right - 6, y: rect.top + rect.height / 2, end: Number(node.getAttribute('data-canvas-end-offset')) };
            }
            """);
        await page.Mouse.ClickAsync((float)target.GetProperty("x").GetDouble(), (float)target.GetProperty("y").GetDouble());
        await DocumentEditorCanvasVisualAssert.AssertCaretVisibleAsync(page.Locator("[data-testid='document-canvas-caret']").First);

        var probe = await ReadSelectionProbeAsync(page);
        var end = target.GetProperty("end").GetInt32();
        Assert.AreEqual("canvas-selection-body", probe.FocusBlockId);
        Assert.IsTrue(probe.FocusOffset <= end && probe.FocusOffset >= end - 3, $"scale {scale:N2}: the caret lands at {probe.FocusOffset}, the run ends at {end}");

        await page.Context.CloseAsync();
    }

    private static Task<CanvasPoint> ReadTextPointAsync(IPage page, string blockId, int index)
        => page.EvaluateAsync<CanvasPoint>(
            """
            ([blockId, index]) => {
                const rects = Array.from(document.querySelectorAll(`[data-canvas-text-rect][data-block-id="${blockId}"]`));
                const node = rects[Math.min(rects.length - 1, Math.max(0, index))];
                const rect = node.getBoundingClientRect();
                return {
                    x: rect.left + Math.max(2, rect.width / 2),
                    y: rect.top + rect.height / 2
                };
            }
            """,
            new object[] { blockId, index });

    private static Task<string> ReadContentCanvasDataUrlAsync(IPage page)
        => page.Locator("[data-canvas-layer='content']").First.EvaluateAsync<string>("canvas => canvas.toDataURL('image/png')");

    private static Task<CanvasSelectionProbe> ReadSelectionProbeAsync(IPage page)
        => page.EvaluateAsync<CanvasSelectionProbe>(
            """
            () => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                const caret = document.querySelector('[data-testid="document-canvas-caret"]');
                const selectionRects = Array.from(document.querySelectorAll('[data-testid="document-canvas-selection-rect"]'));
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                return {
                    isCollapsed: root?.getAttribute('data-canvas-selection-collapsed') !== 'false',
                    selectionRectCount: selectionRects.length,
                    focusBlockId: root?.getAttribute('data-canvas-selection-focus-block-id') || '',
                    focusOffset: Number(root?.getAttribute('data-canvas-selection-focus-offset') || '0'),
                    caretVisible: !!caret && getComputedStyle(caret).display !== 'none',
                    hostReady: host?.getAttribute('data-canvas-engine-ready') === 'true'
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
            "phase7-caret-selection",
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

    private sealed class CanvasSelectionProbe
    {
        [JsonPropertyName("isCollapsed")]
        public bool IsCollapsed { get; set; }

        [JsonPropertyName("selectionRectCount")]
        public int SelectionRectCount { get; set; }

        [JsonPropertyName("focusBlockId")]
        public string FocusBlockId { get; set; } = string.Empty;

        [JsonPropertyName("focusOffset")]
        public int FocusOffset { get; set; }

        [JsonPropertyName("caretVisible")]
        public bool CaretVisible { get; set; }

        [JsonPropertyName("hostReady")]
        public bool HostReady { get; set; }
    }
}
