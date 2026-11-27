using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tempo.Blazor.E2E.CanvasEngine;

namespace Tempo.Blazor.E2E;

/// <summary>Phase 10 E2E coverage for canvas paragraph commands, heading styles, lists, and ruler state.</summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasParagraphE2ETests : WasmTestBase
{
    [TestMethod]
    public async Task Phase10_ParagraphCommands_ApplyStylesListsRulerAndPersistSaveBoundary()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenParagraphDocumentAsync(page);

        var output = CreateOutputDirectory("desktop-1440x1000");
        var beforePath = Path.Combine(output, "00-phase10-before.png");
        var paragraphPath = Path.Combine(output, "01-phase10-paragraph-format.png");
        var listPath = Path.Combine(output, "02-phase10-list-indent.png");
        var headingPath = Path.Combine(output, "03-phase10-heading-hierarchy.png");
        var rulerPath = Path.Combine(output, "04-phase10-ruler-blocks-nonprinting.png");

        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = beforePath,
            Type = ScreenshotType.Png
        });

        await ClickCanvasBlockAsync(page, "canvas-paragraph-body", 8);
        await DocumentEditorRedesignE2EHelpers.ChooseAlignmentAsync(page, "center");
        await WaitForCommandValueAsync(page, "align", "center");
        await DocumentEditorRedesignE2EHelpers.SelectRibbonOptionAsync(page, "document-line-spacing", "1.5", "Line spacing", "1.5");
        await WaitForCommandValueAsync(page, "lineSpacing", "1.5");
        await DocumentEditorRedesignE2EHelpers.ChooseMoreOptionAsync(page, "Before", "12");
        await WaitForCommandValueAsync(page, "spacingBefore", "12");
        await DocumentEditorRedesignE2EHelpers.ChooseMoreOptionAsync(page, "After", "18");
        await WaitForCommandValueAsync(page, "spacingAfter", "18");
        await DocumentEditorRedesignE2EHelpers.ClickRibbonAsync(page, "document-increase-indent");
        await WaitForCommandValueGreaterThanAsync(page, "decreaseIndent", 0);
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = paragraphPath,
            Type = ScreenshotType.Png
        });

        await ClickCanvasBlockAsync(page, "canvas-paragraph-list", 3);
        await DocumentEditorRedesignE2EHelpers.ClickRibbonAsync(page, "document-bullet-list");
        await WaitForCommandStateAsync(page, "bulletList", "active");
        await FocusHiddenCanvasInputAsync(page);
        await page.Keyboard.PressAsync("Tab");
        await WaitForCommandLastAsync(page, "increaselistlevel");
        await FocusHiddenCanvasInputAsync(page);
        await page.Keyboard.PressAsync("Shift+Tab");
        await WaitForCommandLastAsync(page, "decreaselistlevel");
        await DocumentEditorRedesignE2EHelpers.ClickRibbonAsync(page, "document-numbered-list");
        await WaitForCommandStateAsync(page, "numberedList", "active");
        var listProbe = await ReadParagraphProbeAsync(page);
        Assert.AreEqual("active", listProbe.NumberedListState);
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = listPath,
            Type = ScreenshotType.Png
        });

        await ClickCanvasBlockAsync(page, "canvas-paragraph-heading-candidate", 4);
        await page.GetByTestId("document-block-style").SelectOptionAsync("Heading1");
        await WaitForCommandValueAsync(page, "blockStyle", "Heading 1");
        await page.GetByTestId("document-block-style").SelectOptionAsync("Heading2");
        await WaitForCommandValueAsync(page, "blockStyle", "Heading 2");
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = headingPath,
            Type = ScreenshotType.Png
        });

        await page.GetByTestId("document-save").ClickAsync();
        await WaitForSaveToCompleteAsync(page);
        var afterSaveProbe = await ReadParagraphProbeAsync(page);
        Assert.AreEqual("Heading 2", afterSaveProbe.BlockStyleValue);
        Assert.IsTrue(afterSaveProbe.CommandRevision >= 8, $"Expected paragraph command revisions after save boundary. Actual: {afterSaveProbe.CommandRevision}.");

        await NavigateWithinBlazorAsync(page, "/canvas-engine-host?documentId=phase-5-canvas-render");
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-page"]')
                ?.getAttribute('data-canvas-model-document-id') === 'phase-5-canvas-render'
            """, 20_000);
        await NavigateWithinBlazorAsync(page, "/canvas-engine-host?documentId=phase-10-canvas-paragraph&showToolbar=true");
        await OpenParagraphDocumentReadyAsync(page);
        var afterReloadProbe = await ReadParagraphProbeAsync(page);
        Assert.AreEqual("Heading 2", afterReloadProbe.BlockStyleValue);

        await page.GetByTestId("document-ribbon-tab-view").ClickAsync();
        await DocumentEditorRedesignE2EHelpers.ClickRibbonAsync(page, "document-toggle-ruler");
        await Assertions.Expect(page.GetByTestId("document-canvas-ruler")).ToHaveAttributeAsync("data-canvas-ruler-visible", "false", new() { Timeout = 5_000 });
        await DocumentEditorRedesignE2EHelpers.ClickRibbonAsync(page, "document-toggle-ruler");
        await Assertions.Expect(page.GetByTestId("document-canvas-ruler")).ToHaveAttributeAsync("data-canvas-ruler-visible", "true", new() { Timeout = 5_000 });
        await DocumentEditorRedesignE2EHelpers.ClickRibbonAsync(page, "document-show-blocks");
        await Assertions.Expect(page.GetByTestId("document-canvas-show-blocks-overlay").First).ToBeVisibleAsync(new() { Timeout = 5_000 });
        await page.GetByTestId("document-toggle-nonprinting").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-canvas-nonprinting-overlay").First).ToBeVisibleAsync(new() { Timeout = 5_000 });
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = rulerPath,
            Type = ScreenshotType.Png
        });

        await DocumentEditorCanvasVisualAssert.AssertNoTextOverlapAsync(page);
        await DocumentEditorCanvasVisualAssert.AssertNoUiOverlapAsync(page);
        var contentMetrics = await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(page.Locator("[data-canvas-layer='content']").First);
        var finalProbe = await ReadParagraphProbeAsync(page);
        Assert.AreEqual("phase-10-canvas-paragraph", finalProbe.ModelDocumentId);
        Assert.AreEqual("Heading 2", finalProbe.BlockStyleValue);
        Assert.AreEqual("true", finalProbe.RulerVisible);
        Assert.IsTrue(finalProbe.BlockOverlayCount > 0);
        Assert.IsTrue(finalProbe.NonPrintingCount > 0);

        var manifestPath = Path.Combine(output, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            testName = nameof(Phase10_ParagraphCommands_ApplyStylesListsRulerAndPersistSaveBoundary),
            viewport = "desktop-1440x1000",
            seedDocumentId = "phase-10-canvas-paragraph",
            userActions = new[]
            {
                "Open /canvas-engine-host with the phase 10 paragraph seed document and visible production toolbar.",
                "Apply center alignment, 1.5 line spacing, before/after spacing, and increased indent to the paragraph block.",
                "Toggle bullet and numbered lists, then use Tab and Shift+Tab through the hidden canvas input to change nesting.",
                "Apply Heading 1 and Heading 2 through the localized block style selector and save through the production Save command.",
                "Toggle ruler visibility, show block boundaries, and show non-printing characters from the View ribbon."
            },
            expectedVisibleChanges = "Paragraph layout shifts with center alignment and spacing, list labels appear, the heading grows into a clear hierarchy, ruler handles reflect page margins and indent state, and block/non-printing overlays remain readable.",
            expectedModelChanges = "Canvas command diagnostics report paragraph values, list states, blockStyle Heading 2, undoable command revisions, and the save boundary keeps the canvas model style state intact.",
            screenshotPaths = new[] { beforePath, paragraphPath, listPath, headingPath, rulerPath },
            contentMetrics,
            finalProbe,
            uxReviewerNotes = "The toolbar should feel native: no focus jumps from canvas selection, no overlapped controls, and ruler/block overlays must be visible without hiding document text."
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        TestContext.AddResultFile(beforePath);
        TestContext.AddResultFile(paragraphPath);
        TestContext.AddResultFile(listPath);
        TestContext.AddResultFile(headingPath);
        TestContext.AddResultFile(rulerPath);
        TestContext.AddResultFile(manifestPath);
    }

    private async Task OpenParagraphDocumentAsync(IPage page)
    {
        await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId=phase-10-canvas-paragraph&showToolbar=true");
        await page.WaitForSelectorAsync(
            "[data-testid='document-canvas-engine-host'][data-canvas-engine-ready='true']",
            new PageWaitForSelectorOptions { Timeout = 30_000 });
        await OpenParagraphDocumentReadyAsync(page);
    }

    private static Task OpenParagraphDocumentReadyAsync(IPage page)
        => WaitForPredicateAsync(page, """
            () => document.querySelectorAll('[data-canvas-text-rect][data-block-id="canvas-paragraph-body"]').length >= 1
                && document.querySelector('[data-testid="document-block-style"]')
                && document.querySelector('[data-testid="document-canvas-ruler"]')
            """, 30_000);

    private static Task NavigateWithinBlazorAsync(IPage page, string url)
        => page.EvaluateAsync(
            """
            url => window.Blazor?.navigateTo
                ? window.Blazor.navigateTo(url)
                : window.history.pushState({}, '', url)
            """,
            url);

    private static async Task ClickCanvasBlockAsync(IPage page, string blockId, int offset)
    {
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
            """
            () => document.querySelector('[data-testid="document-canvas-hidden-input"]')?.focus()
            """);

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

    private static Task WaitForCommandValueAsync(IPage page, string commandId, string expected)
        => page.WaitForFunctionAsync(
            """
            ([commandId, expected]) => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                return (root?.getAttribute(`data-canvas-command-${commandId}-value`) || '') === expected;
            }
            """,
            new object[] { commandId, expected },
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task WaitForCommandValueGreaterThanAsync(IPage page, string commandId, double minValue)
        => page.WaitForFunctionAsync(
            """
            ([commandId, minValue]) => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                const value = Number(root?.getAttribute(`data-canvas-command-${commandId}-value`) || '0');
                return value > Number(minValue);
            }
            """,
            new object[] { commandId, minValue },
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task WaitForCommandLastAsync(IPage page, string expected)
        => page.WaitForFunctionAsync(
            """
            expected => document.querySelector('[data-testid="document-canvas-engine-root"]')
                ?.getAttribute('data-canvas-command-last') === expected
            """,
            expected,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task WaitForSaveToCompleteAsync(IPage page)
        => WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-save"]')?.getAttribute('disabled') === null
                && document.body.textContent.includes('Saved')
            """, 10_000);

    private static Task<ParagraphProbe> ReadParagraphProbeAsync(IPage page)
        => page.EvaluateAsync<ParagraphProbe>(
            """
            () => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                const pageElement = document.querySelector('[data-testid="document-canvas-page"]');
                const attr = name => root?.getAttribute(name) || '';
                return {
                    modelDocumentId: pageElement?.getAttribute('data-canvas-model-document-id') || '',
                    commandRevision: Number(attr('data-canvas-command-revision') || '0'),
                    lastCommand: attr('data-canvas-command-last'),
                    alignmentValue: attr('data-canvas-command-align-value'),
                    lineSpacingValue: attr('data-canvas-command-lineSpacing-value'),
                    spacingBeforeValue: attr('data-canvas-command-spacingBefore-value'),
                    spacingAfterValue: attr('data-canvas-command-spacingAfter-value'),
                    bulletListState: attr('data-canvas-command-bulletList-state'),
                    numberedListState: attr('data-canvas-command-numberedList-state'),
                    blockStyleValue: attr('data-canvas-command-blockStyle-value'),
                    rulerVisible: document.querySelector('[data-testid="document-canvas-ruler"]')?.getAttribute('data-canvas-ruler-visible') || '',
                    blockOverlayCount: document.querySelectorAll('[data-testid="document-canvas-show-blocks-overlay"]').length,
                    nonPrintingCount: document.querySelectorAll('[data-testid="document-canvas-nonprinting-overlay"]').length
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
            "phase10-paragraph",
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

    private sealed class ParagraphProbe
    {
        [JsonPropertyName("modelDocumentId")]
        public string ModelDocumentId { get; set; } = string.Empty;

        [JsonPropertyName("commandRevision")]
        public int CommandRevision { get; set; }

        [JsonPropertyName("lastCommand")]
        public string LastCommand { get; set; } = string.Empty;

        [JsonPropertyName("alignmentValue")]
        public string AlignmentValue { get; set; } = string.Empty;

        [JsonPropertyName("lineSpacingValue")]
        public string LineSpacingValue { get; set; } = string.Empty;

        [JsonPropertyName("spacingBeforeValue")]
        public string SpacingBeforeValue { get; set; } = string.Empty;

        [JsonPropertyName("spacingAfterValue")]
        public string SpacingAfterValue { get; set; } = string.Empty;

        [JsonPropertyName("bulletListState")]
        public string BulletListState { get; set; } = string.Empty;

        [JsonPropertyName("numberedListState")]
        public string NumberedListState { get; set; } = string.Empty;

        [JsonPropertyName("blockStyleValue")]
        public string BlockStyleValue { get; set; } = string.Empty;

        [JsonPropertyName("rulerVisible")]
        public string RulerVisible { get; set; } = string.Empty;

        [JsonPropertyName("blockOverlayCount")]
        public int BlockOverlayCount { get; set; }

        [JsonPropertyName("nonPrintingCount")]
        public int NonPrintingCount { get; set; }
    }
}
