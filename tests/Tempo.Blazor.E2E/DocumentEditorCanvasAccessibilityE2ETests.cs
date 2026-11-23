using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tempo.Blazor.E2E.CanvasEngine;

namespace Tempo.Blazor.E2E;

/// <summary>Phase 21 E2E coverage for canvas accessibility mirror, keyboard operation, live announcements, and forced colors.</summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasAccessibilityE2ETests : WasmTestBase
{
    [TestMethod]
    public async Task Phase21_CanvasAccessibilityMirrorKeyboardLiveRegionAndForcedColors_AreProductionReady()
    {
        var context = await CreateContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1440, Height = 1000 },
            Locale = "en-US",
            IgnoreHTTPSErrors = true
        });
        var page = await context.NewPageAsync();

        await OpenCanvasDocumentAsync(page, "phase-14-canvas-tables");
        var tableProbe = await ReadMirrorProbeAsync(page);
        Assert.AreEqual("document", tableProbe.RootRole);
        Assert.AreEqual("Canvas document surface", tableProbe.RootLabel);
        Assert.IsTrue(tableProbe.TableCount >= 1, tableProbe.Debug);
        Assert.IsTrue(tableProbe.TableCellCount >= 4, tableProbe.Debug);
        Assert.AreEqual("textbox", tableProbe.InputRole);
        Assert.AreEqual("true", tableProbe.InputMultiline);
        Assert.AreEqual("document-canvas-a11y-mirror", tableProbe.InputControls);
        Assert.AreEqual("document-canvas-live-region", tableProbe.InputDescribedBy);
        Assert.AreEqual("status", tableProbe.LiveRole);
        Assert.AreEqual("polite", tableProbe.LiveMode);

        await OpenCanvasDocumentAsync(page, "phase-10-canvas-paragraph");
        var headingProbe = await ReadMirrorProbeAsync(page);
        Assert.IsTrue(headingProbe.BlockCount >= 4, headingProbe.Debug);

        var marker = $" phase21-a11y-{DateTimeOffset.UtcNow:HHmmssfff}";
        await ClickCanvasBlockAsync(page, "canvas-paragraph-body", 8);
        await page.GetByTestId("document-canvas-hidden-input").FocusAsync();
        await page.Keyboard.TypeAsync(marker);
        await WaitForMirrorTextContainsAsync(page, "canvas-paragraph-body", marker.Trim());
        await Assertions.Expect(page.GetByTestId("document-canvas-live-region"))
            .ToHaveAttributeAsync("data-canvas-live-kind", "caret", new() { Timeout = 10_000 });

        await OpenCanvasDocumentAsync(page, "phase-17-canvas-comments-revisions");
        var searchToken = await ReadFirstMirrorSearchTokenAsync(page);
        Assert.IsFalse(string.IsNullOrWhiteSpace(searchToken), "Phase 17 mirror should expose searchable text.");
        await ExecuteCanvasCommandAsync(page, "find", new { query = searchToken });
        await Assertions.Expect(page.GetByTestId("document-canvas-live-region"))
            .ToHaveAttributeAsync("data-canvas-live-kind", "find", new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByTestId("document-canvas-live-region"))
            .ToContainTextAsync("Match", new() { Timeout = 10_000 });

        await page.Locator("[data-testid='document-canvas-comment-marker'][data-comment-id='canvas-phase17-comment']").First.ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-canvas-live-region"))
            .ToHaveAttributeAsync("data-canvas-live-kind", "comment", new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByTestId("document-canvas-live-region"))
            .ToContainTextAsync("canvas-phase17-comment", new() { Timeout = 10_000 });
        var reviewProbe = await ReadMirrorProbeAsync(page);
        Assert.IsTrue(reviewProbe.CommentCount >= 1, reviewProbe.Debug);
        Assert.IsTrue(reviewProbe.RevisionCount >= 1, reviewProbe.Debug);

        var forcedContext = await CreateContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1280, Height = 900 },
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
            ForcedColors = ForcedColors.Active
        });
        var forcedPage = await forcedContext.NewPageAsync();
        await OpenCanvasDocumentAsync(forcedPage, "phase-17-canvas-comments-revisions");
        var forcedProbe = await forcedPage.EvaluateAsync<ForcedColorsProbe>(
            """
            () => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const page = document.querySelector('[data-testid="document-canvas-page"]');
                const hostStyle = getComputedStyle(host);
                const pageStyle = getComputedStyle(page);
                return {
                    forcedColors: matchMedia('(forced-colors: active)').matches,
                    hostBackground: hostStyle.backgroundColor,
                    hostColor: hostStyle.color,
                    pageBorder: pageStyle.borderTopColor
                };
            }
            """);
        Assert.IsTrue(forcedProbe.ForcedColors, "Playwright forced-colors emulation must be active.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(forcedProbe.HostBackground), "Forced-colors host background should resolve.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(forcedProbe.PageBorder), "Forced-colors page border should resolve.");

        var output = CreateOutputDirectory(nameof(Phase21_CanvasAccessibilityMirrorKeyboardLiveRegionAndForcedColors_AreProductionReady));
        var screenshotPath = Path.Combine(output, "phase21-forced-colors.png");
        var manifestPath = Path.Combine(output, "manifest.json");
        await forcedPage.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = screenshotPath,
            Type = ScreenshotType.Png
        });
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            testName = nameof(Phase21_CanvasAccessibilityMirrorKeyboardLiveRegionAndForcedColors_AreProductionReady),
            seedDocuments = new[] { "phase-14-canvas-tables", "phase-10-canvas-paragraph", "phase-17-canvas-comments-revisions" },
            manualNvdaVoiceOverGate = "Follow-up required on real assistive technologies; automated ARIA, keyboard-only, live-region, and forced-colors gates passed.",
            screenshotPath,
            tableProbe,
            headingProbe,
            reviewProbe,
            forcedProbe
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        TestContext.AddResultFile(screenshotPath);
        TestContext.AddResultFile(manifestPath);

        await DocumentEditorCanvasVisualAssert.AssertNoUiOverlapAsync(page);
        await DocumentEditorCanvasVisualAssert.AssertNoUiOverlapAsync(forcedPage);
    }

    [TestMethod]
    public async Task Phase21_CanvasFocusManagementAndDialogTrap_AreKeyboardReady()
    {
        var context = await CreateContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1440, Height = 1000 },
            Locale = "en-US",
            IgnoreHTTPSErrors = true
        });
        var page = await context.NewPageAsync();

        await OpenCanvasDocumentAsync(page, "phase-10-canvas-paragraph");
        await page.GetByTestId("document-canvas-hidden-input").FocusAsync();
        await page.Keyboard.PressAsync("F10");
        await Assertions.Expect(page.GetByTestId("document-toolbar"))
            .ToHaveAttributeAsync("data-keyboard-mode", "true", new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByTestId("document-ribbon-tab-home"))
            .ToBeFocusedAsync(new() { Timeout = 10_000 });

        await page.GetByTestId("document-canvas-hidden-input").FocusAsync();
        await page.Keyboard.PressAsync("Control+Alt+V");
        await Assertions.Expect(page.GetByTestId("document-side-panel"))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
        var sidePanelFocusProbe = await ReadFocusManagementProbeAsync(page);
        Assert.AreEqual("versions-panel", sidePanelFocusProbe.CanvasShortcutLast, JsonSerializer.Serialize(sidePanelFocusProbe, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.IsFalse(string.IsNullOrWhiteSpace(sidePanelFocusProbe.ActiveSidePanelTab), JsonSerializer.Serialize(sidePanelFocusProbe, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.AreEqual(
            $"document-side-panel-tab-{sidePanelFocusProbe.ActiveSidePanelTab}",
            sidePanelFocusProbe.ActiveElementTestId,
            JsonSerializer.Serialize(sidePanelFocusProbe, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.GetByTestId("document-side-panel"))
            .ToBeHiddenAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByTestId("document-canvas-hidden-input"))
            .ToBeFocusedAsync(new() { Timeout = 10_000 });

        await page.Keyboard.PressAsync("Control+Shift+P");
        await Assertions.Expect(page.GetByTestId("document-command-palette"))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByTestId("document-command-palette-dialog"))
            .ToHaveAttributeAsync("data-focus-trap-active", "true", new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByTestId("document-command-palette-search"))
            .ToBeFocusedAsync(new() { Timeout = 10_000 });

        await page.Keyboard.PressAsync("Shift+Tab");
        Assert.IsTrue(await ActiveElementIsInsideAsync(page, "document-command-palette-dialog"), await ActiveElementDebugAsync(page));
        await page.EvaluateAsync(
            """
            () => document.querySelector('[data-testid="document-canvas-hidden-input"]')?.focus()
            """);
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-command-palette-dialog"]')?.contains(document.activeElement) === true
            """, 10_000);
        Assert.IsTrue(await ActiveElementIsInsideAsync(page, "document-command-palette-dialog"), await ActiveElementDebugAsync(page));

        var output = CreateOutputDirectory(nameof(Phase21_CanvasFocusManagementAndDialogTrap_AreKeyboardReady));
        var screenshotPath = Path.Combine(output, "phase21-focus-management-command-palette.png");
        var manifestPath = Path.Combine(output, "manifest.json");
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = screenshotPath,
            Type = ScreenshotType.Png
        });

        var focusProbe = await page.EvaluateAsync<FocusManagementProbe>(
            FocusManagementProbeScript);
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            testName = nameof(Phase21_CanvasFocusManagementAndDialogTrap_AreKeyboardReady),
            seedDocuments = new[] { "phase-10-canvas-paragraph" },
            screenshotPath,
            focusProbe
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        TestContext.AddResultFile(screenshotPath);
        TestContext.AddResultFile(manifestPath);

        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.GetByTestId("document-command-palette"))
            .ToBeHiddenAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByTestId("document-canvas-hidden-input"))
            .ToBeFocusedAsync(new() { Timeout = 10_000 });

        await DocumentEditorCanvasVisualAssert.AssertNoUiOverlapAsync(page);
    }

    private async Task OpenCanvasDocumentAsync(IPage page, string documentId)
    {
        await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId={documentId}&showToolbar=true", WaitUntilState.DOMContentLoaded);
        await page.WaitForSelectorAsync(
            "[data-testid='document-canvas-engine-host'][data-canvas-engine-ready='true']",
            new PageWaitForSelectorOptions { Timeout = 30_000 });
        await page.WaitForFunctionAsync(
            "documentId => document.querySelector('[data-testid=\"document-canvas-page\"]')?.getAttribute('data-canvas-model-document-id') === documentId",
            documentId,
            new PageWaitForFunctionOptions { Timeout = 30_000 });
        await WaitForPredicateAsync(page, "() => document.querySelector('[data-testid=\"document-canvas-a11y-mirror\"]')?.children.length > 0", 30_000);
    }

    private static async Task ExecuteCanvasCommandAsync(IPage page, string commandId, object argument)
    {
        await page.EvaluateAsync(
            """
            async ([commandId, argument]) => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                module.execCommand(handle, commandId, JSON.stringify(argument));
            }
            """,
            new object[] { commandId, argument });
    }

    private static async Task ClickCanvasBlockAsync(IPage page, string blockId, int offset)
    {
        var point = await page.EvaluateAsync<CanvasPoint>(
            """
            ([blockId, offset]) => {
                const rects = Array.from(document.querySelectorAll(`[data-canvas-text-rect][data-block-id="${blockId}"]`));
                const node = rects.find(item => Number(item.getAttribute('data-canvas-start-offset') || '0') <= offset && Number(item.getAttribute('data-canvas-end-offset') || '0') >= offset) || rects[0];
                if (!node) throw new Error(`No canvas text rects found for ${blockId}.`);
                const rect = node.getBoundingClientRect();
                const start = Number(node.getAttribute('data-canvas-start-offset') || '0');
                const end = Math.max(start + 1, Number(node.getAttribute('data-canvas-end-offset') || '0'));
                const t = Math.max(0, Math.min(1, (Number(offset) - start) / (end - start)));
                return { x: rect.left + Math.max(2, rect.width * t), y: rect.top + rect.height / 2 };
            }
            """,
            new object[] { blockId, offset });

        await page.Mouse.ClickAsync((float)point.X, (float)point.Y);
        await page.WaitForFunctionAsync(
            "blockId => document.querySelector('[data-testid=\"document-canvas-engine-root\"]')?.getAttribute('data-canvas-selection-focus-block-id') === blockId",
            blockId,
            new PageWaitForFunctionOptions { Timeout = 10_000 });
    }

    private static Task WaitForMirrorTextContainsAsync(IPage page, string blockId, string expected)
        => page.WaitForFunctionAsync(
            """
            ([blockId, expected]) => (document.querySelector(`[data-testid="document-canvas-a11y-mirror"] [data-block-id="${blockId}"]`)?.textContent || '').includes(expected)
            """,
            new object[] { blockId, expected },
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static async Task<string> ReadFirstMirrorSearchTokenAsync(IPage page)
        => await page.EvaluateAsync<string>(
            """
            () => {
                const text = document.querySelector('[data-testid="document-canvas-a11y-mirror"]')?.textContent || '';
                return text.match(/[A-Za-z][A-Za-z0-9'-]{3,}/)?.[0] || '';
            }
            """);

    private static async Task<MirrorProbe> ReadMirrorProbeAsync(IPage page)
        => await page.EvaluateAsync<MirrorProbe>(
            """
            () => {
                const mirror = document.querySelector('[data-testid="document-canvas-a11y-mirror"]');
                const input = document.querySelector('[data-testid="document-canvas-hidden-input"]');
                const live = document.querySelector('[data-testid="document-canvas-live-region"]');
                const headings = Array.from(mirror?.querySelectorAll('[role="heading"][aria-level]') || []);
                const tables = Array.from(mirror?.querySelectorAll('[data-canvas-a11y-table="true"]') || []);
                return {
                    rootRole: mirror?.getAttribute('role') || '',
                    rootLabel: mirror?.getAttribute('aria-label') || '',
                    blockCount: Number(mirror?.getAttribute('data-canvas-a11y-block-count') || '0'),
                    headingCount: headings.length,
                    headingLevels: headings.map(item => Number(item.getAttribute('aria-level') || '0')),
                    tableCount: tables.length,
                    tableCellCount: mirror?.querySelectorAll('[role="cell"], [role="columnheader"], [role="rowheader"]').length || 0,
                    commentCount: Number(mirror?.getAttribute('data-canvas-a11y-comment-count') || '0'),
                    revisionCount: Number(mirror?.getAttribute('data-canvas-a11y-revision-count') || '0'),
                    inputRole: input?.getAttribute('role') || '',
                    inputMultiline: input?.getAttribute('aria-multiline') || '',
                    inputControls: input?.getAttribute('aria-controls') || '',
                    inputDescribedBy: input?.getAttribute('aria-describedby') || '',
                    liveRole: live?.getAttribute('role') || '',
                    liveMode: live?.getAttribute('aria-live') || '',
                    debug: mirror?.outerHTML?.slice(0, 1200) || ''
                };
            }
            """);

    private static Task<bool> ActiveElementIsInsideAsync(IPage page, string testId)
        => page.EvaluateAsync<bool>(
            """
            testId => {
                const root = document.querySelector(`[data-testid="${testId}"]`);
                return !!root && root.contains(document.activeElement);
            }
            """,
            testId);

    private static Task<string> ActiveElementDebugAsync(IPage page)
        => page.EvaluateAsync<string>(
            """
            () => {
                const active = document.activeElement;
                return JSON.stringify({
                    tag: active?.tagName || '',
                    testId: active?.getAttribute('data-testid') || '',
                    role: active?.getAttribute('role') || '',
                    text: (active?.textContent || '').slice(0, 80)
                });
            }
            """);

    private static Task<FocusManagementProbe> ReadFocusManagementProbeAsync(IPage page)
        => page.EvaluateAsync<FocusManagementProbe>(FocusManagementProbeScript);

    private const string FocusManagementProbeScript =
        """
        () => ({
            toolbarKeyboardMode: document.querySelector('[data-testid="document-toolbar"]')?.getAttribute('data-keyboard-mode') || '',
            commandPaletteTrap: document.querySelector('[data-testid="document-command-palette-dialog"]')?.getAttribute('data-focus-trap-active') || '',
            canvasShortcutLast: document.querySelector('[data-testid="document-canvas-engine-root"]')?.getAttribute('data-canvas-shortcut-last') || '',
            activeSidePanelTab: document.querySelector('[data-testid="document-side-panel"]')?.getAttribute('data-active-tab') || '',
            activeElementTestId: document.activeElement?.getAttribute('data-testid') || '',
            activeElementRole: document.activeElement?.getAttribute('role') || document.activeElement?.tagName?.toLowerCase() || ''
        })
        """;

    private static string CreateOutputDirectory(string testName)
    {
        var safe = string.Concat(testName.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-'));
        var path = Path.Combine(
            FindRepositoryRoot().FullName,
            "tests",
            "Tempo.Blazor.E2E",
            "TestResults",
            "document-editor-canvas",
            "phase21-accessibility",
            "2026-06-04",
            safe,
            DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff"));
        Directory.CreateDirectory(path);
        return path;
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
        public double X { get; set; }

        public double Y { get; set; }
    }

    private sealed class MirrorProbe
    {
        public string RootRole { get; set; } = string.Empty;

        public string RootLabel { get; set; } = string.Empty;

        public int BlockCount { get; set; }

        public int HeadingCount { get; set; }

        public int[] HeadingLevels { get; set; } = [];

        public int TableCount { get; set; }

        public int TableCellCount { get; set; }

        public int CommentCount { get; set; }

        public int RevisionCount { get; set; }

        public string InputRole { get; set; } = string.Empty;

        public string InputMultiline { get; set; } = string.Empty;

        public string InputControls { get; set; } = string.Empty;

        public string InputDescribedBy { get; set; } = string.Empty;

        public string LiveRole { get; set; } = string.Empty;

        public string LiveMode { get; set; } = string.Empty;

        public string Debug { get; set; } = string.Empty;
    }

    private sealed class ForcedColorsProbe
    {
        public bool ForcedColors { get; set; }

        public string HostBackground { get; set; } = string.Empty;

        public string HostColor { get; set; } = string.Empty;

        public string PageBorder { get; set; } = string.Empty;
    }

    private sealed class FocusManagementProbe
    {
        public string ToolbarKeyboardMode { get; set; } = string.Empty;

        public string CommandPaletteTrap { get; set; } = string.Empty;

        public string CanvasShortcutLast { get; set; } = string.Empty;

        public string ActiveSidePanelTab { get; set; } = string.Empty;

        public string ActiveElementTestId { get; set; } = string.Empty;

        public string ActiveElementRole { get; set; } = string.Empty;
    }
}
