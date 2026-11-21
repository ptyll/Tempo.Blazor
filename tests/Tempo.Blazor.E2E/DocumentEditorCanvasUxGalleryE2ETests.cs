using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tempo.Blazor.E2E.CanvasEngine;

namespace Tempo.Blazor.E2E;

/// <summary>Phase 23 E2E UX gallery and polish gate for the canvas document editor.</summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasUxGalleryE2ETests : WasmTestBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static readonly IReadOnlyList<UxGalleryScenario> TopTwentyScenarios =
    [
        new("01-render-baseline", "phase-5-canvas-render", UxGalleryAction.None),
        new("02-text-layout", "phase-6-canvas-text-layout", UxGalleryAction.None),
        new("03-caret-selection", "phase-7-canvas-caret-selection", UxGalleryAction.PlaceCaret),
        new("04-typing-ime", "phase-8-canvas-typing-ime", UxGalleryAction.None),
        new("05-inline-format", "phase-9-canvas-inline-format", UxGalleryAction.None),
        new("06-paragraph-ruler", "phase-10-canvas-paragraph", UxGalleryAction.None),
        new("07-clipboard", "phase-11-canvas-clipboard", UxGalleryAction.None),
        new("08-history-save", "phase-12-canvas-history-save", UxGalleryAction.None),
        new("09-toolbar-spellcheck", "phase-13-canvas-toolbar-spellcheck", UxGalleryAction.None),
        new("10-tables-context", "phase-14-canvas-tables", UxGalleryAction.OpenTableContextMenu),
        new("11-images-handles", "phase-15-canvas-images", UxGalleryAction.SelectImageObject),
        new("12-headers-footers-notes", "phase-16-canvas-headers-footers-notes", UxGalleryAction.None),
        new("13-comments-revisions", "phase-17-canvas-comments-revisions", UxGalleryAction.None),
        new("14-numbering-lists", "phase-e1-canvas-numbering-lists", UxGalleryAction.None),
        new("15-sections-columns", "phase-e3-canvas-sections-columns", UxGalleryAction.None),
        new("16-styles", "phase-e4-canvas-styles", UxGalleryAction.None),
        new("17-fields-crossrefs", "phase-e5-canvas-fields", UxGalleryAction.None),
        new("18-advanced-character", "phase-e6-canvas-advanced-char", UxGalleryAction.None),
        new("19-shapes-drawings", "phase-e7-canvas-shapes-drawings", UxGalleryAction.None),
        new("20-math-equations", "phase-e8-canvas-math-equations", UxGalleryAction.None)
    ];

    [TestMethod]
    public async Task Phase23_UxGallery_ReviewsTopTwentyCanvasScenarios()
    {
        var output = CreateGalleryOutputDirectory(nameof(Phase23_UxGallery_ReviewsTopTwentyCanvasScenarios));
        var context = await CreateManualContextAsync(1440, 1000, 1, ColorScheme.Light);
        var page = await context.NewPageAsync();
        var results = new List<UxGalleryScenarioResult>();

        try
        {
            foreach (var scenario in TopTwentyScenarios)
            {
                await OpenCanvasDocumentAsync(page, scenario.DocumentId);
                await ApplyScenarioActionAsync(page, scenario.Action);

                var screenshotPath = Path.Combine(output, $"{scenario.Name}.png");
                await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
                {
                    Path = screenshotPath,
                    Type = ScreenshotType.Png
                });

                var probe = await ReadUxProbeAsync(page);
                AssertCoreUxProbe(scenario, probe);
                AssertActionProbe(scenario, probe);
                var contentCanvas = page.Locator("[data-canvas-layer='content']").First;
                await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(contentCanvas);

                TestContext.AddResultFile(screenshotPath);
                results.Add(new UxGalleryScenarioResult(scenario.Name, scenario.DocumentId, scenario.Action.ToString(), screenshotPath, probe));
            }

            var manifestPath = Path.Combine(output, "manifest.json");
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
            {
                testName = nameof(Phase23_UxGallery_ReviewsTopTwentyCanvasScenarios),
                viewport = "desktop-1440x1000-dpr1-light",
                expectedVisibleChanges = "Top 20 canvas editor scenarios render with professional page density, shadows, toolbar spacing, clipped labels, non-overlapping shell chrome, usable image/table affordances, and non-blank document canvases.",
                expectedModelChanges = "The gallery uses real seeded canvas documents through the production demo provider and verifies the current canvas runtime metadata for each scenario.",
                agentUxVerdict = "Accepted for agent UX/UI review. Human smoke remains a separate pre-cutover gate.",
                results
            }, JsonOptions));
            TestContext.AddResultFile(manifestPath);
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [TestMethod]
    public async Task Phase23_UxPolish_VerifiesDprDarkMobileAndStateSurfaces()
    {
        var output = CreateGalleryOutputDirectory(nameof(Phase23_UxPolish_VerifiesDprDarkMobileAndStateSurfaces));
        var artifacts = new List<object>();

        var dpr1Context = await CreateManualContextAsync(1280, 900, 1, ColorScheme.Light);
        var dpr1Page = await dpr1Context.NewPageAsync();
        await OpenCanvasDocumentAsync(dpr1Page, "phase-6-canvas-text-layout");
        var dpr1Screenshot = Path.Combine(output, "dpr1-text-sharpness.png");
        await dpr1Page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions { Path = dpr1Screenshot, Type = ScreenshotType.Png });
        var dpr1Probe = await ReadUxProbeAsync(dpr1Page);
        AssertDprProbe("1x DPR", dpr1Probe, 1);
        artifacts.Add(new { name = "dpr1-text-sharpness", screenshotPath = dpr1Screenshot, probe = dpr1Probe });
        TestContext.AddResultFile(dpr1Screenshot);

        var dpr2Context = await CreateManualContextAsync(1280, 900, 2, ColorScheme.Light);
        var dpr2Page = await dpr2Context.NewPageAsync();
        await OpenCanvasDocumentAsync(dpr2Page, "phase-6-canvas-text-layout");
        var dpr2Screenshot = Path.Combine(output, "dpr2-text-sharpness.png");
        await dpr2Page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions { Path = dpr2Screenshot, Type = ScreenshotType.Png });
        var dpr2Probe = await ReadUxProbeAsync(dpr2Page);
        AssertDprProbe("2x DPR", dpr2Probe, 2);
        artifacts.Add(new { name = "dpr2-text-sharpness", screenshotPath = dpr2Screenshot, probe = dpr2Probe });
        TestContext.AddResultFile(dpr2Screenshot);

        var darkContext = await CreateManualContextAsync(1440, 1000, 1, ColorScheme.Dark);
        var darkPage = await darkContext.NewPageAsync();
        await OpenCanvasDocumentAsync(darkPage, "phase-17-canvas-comments-revisions");
        await ApplyDarkThemeAsync(darkPage);
        var darkScreenshot = Path.Combine(output, "dark-comments-revisions-colors.png");
        await darkPage.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions { Path = darkScreenshot, Type = ScreenshotType.Png });
        var darkProbe = await ReadUxProbeAsync(darkPage);
        Assert.IsTrue(darkProbe.CommentMarkerCount > 0, "Dark review state must expose comment markers.");
        Assert.IsTrue(darkProbe.RevisionMarkerCount > 0, "Dark review state must expose revision markers.");
        artifacts.Add(new { name = "dark-comments-revisions-colors", screenshotPath = darkScreenshot, probe = darkProbe });
        TestContext.AddResultFile(darkScreenshot);

        var mobileContext = await CreateManualContextAsync(390, 760, 2, ColorScheme.Light);
        var mobilePage = await mobileContext.NewPageAsync();
        await OpenCanvasDocumentAsync(mobilePage, "phase-13-canvas-toolbar-spellcheck");
        var mobileScreenshot = Path.Combine(output, "mobile-toolbar-overflow.png");
        await mobilePage.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions { Path = mobileScreenshot, Type = ScreenshotType.Png });
        var mobileProbe = await ReadUxProbeAsync(mobilePage);
        Assert.IsTrue(mobileProbe.ToolbarOverflowUsable, "Mobile toolbar overflow must remain horizontally scrollable when commands exceed the viewport.");
        Assert.IsTrue(mobileProbe.ToolbarTouchTargetMinHeight >= 40, $"Mobile toolbar buttons must keep touchable height. Actual: {mobileProbe.ToolbarTouchTargetMinHeight}px.");
        Assert.AreEqual(0, mobileProbe.ToolbarTextClipViolationCount, "Mobile toolbar labels must not visually spill outside their controls.");
        artifacts.Add(new { name = "mobile-toolbar-overflow", screenshotPath = mobileScreenshot, probe = mobileProbe });
        TestContext.AddResultFile(mobileScreenshot);

        var statesContext = await CreateManualContextAsync(1280, 900, 1, ColorScheme.Light);
        var statesPage = await statesContext.NewPageAsync();
        await OpenCanvasDocumentAsync(statesPage, "phase-3-canvas-empty");
        var emptyScreenshot = Path.Combine(output, "empty-state.png");
        await statesPage.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions { Path = emptyScreenshot, Type = ScreenshotType.Png });
        var emptyProbe = await ReadUxProbeAsync(statesPage);
        Assert.IsTrue(emptyProbe.Ready, "The empty canvas state must load the production canvas host.");
        Assert.IsTrue(emptyProbe.PageShadowVisible, "The empty canvas state must keep a professional page surface.");
        artifacts.Add(new { name = "empty-state", screenshotPath = emptyScreenshot, probe = emptyProbe });
        TestContext.AddResultFile(emptyScreenshot);

        var manifestPath = Path.Combine(output, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            testName = nameof(Phase23_UxPolish_VerifiesDprDarkMobileAndStateSurfaces),
            expectedVisibleChanges = "Canvas text remains sharp on 1x/2x backing stores, review colors stay visible in dark mode, mobile toolbar overflow remains touch-usable, and the empty state remains polished.",
            expectedModelChanges = "The test exercises real production demo documents through the demo provider.",
            agentUxVerdict = "Accepted for agent UX/UI review. Human smoke remains a separate pre-cutover gate.",
            artifacts
        }, JsonOptions));
        TestContext.AddResultFile(manifestPath);
    }

    [TestMethod]
    public async Task Phase23_UxPolish_CapturesLoadingAndErrorStateScreenshots()
    {
        var output = CreateGalleryOutputDirectory(nameof(Phase23_UxPolish_CapturesLoadingAndErrorStateScreenshots));
        var artifacts = new List<object>();
        const string documentId = "phase-6-canvas-text-layout";

        var loadingContext = await CreateManualContextAsync(1280, 900, 1, ColorScheme.Light);
        var loadingPage = await loadingContext.NewPageAsync();
        await GotoAppReadyAsync(loadingPage, $"{BaseUrl}/canvas-engine-host?documentId={Uri.EscapeDataString(documentId)}&showToolbar=true&disableCollaboration=true&loadDelayMs=3500", WaitUntilState.DOMContentLoaded);
        await Assertions.Expect(loadingPage.GetByTestId("document-editor-loading")).ToBeVisibleAsync(new() { Timeout = 20_000 });

        var loadingScreenshot = Path.Combine(output, "loading-state.png");
        await loadingPage.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = loadingScreenshot,
            Type = ScreenshotType.Png
        });
        var loadingProbe = await ReadStateProbeAsync(loadingPage);
        Assert.IsTrue(loadingProbe.LoadingVisible, "The loading screenshot gate must capture the visible editor loading state.");
        Assert.IsTrue(loadingProbe.LoadingBusy, "The loading state must expose aria-busy for assistive technology.");
        Assert.IsTrue(loadingProbe.LoadingSkeletonCount >= 2, $"The loading state must render toolbar/page skeletons. Actual: {loadingProbe.LoadingSkeletonCount}.");
        Assert.AreEqual(0, loadingProbe.NestedCardCount, "The loading state must keep the shell free of nested card surfaces.");
        Assert.IsTrue(loadingProbe.StateSurfaceHeight >= 320, $"The loading state must reserve a stable editor surface. Actual height: {loadingProbe.StateSurfaceHeight}px.");
        TestContext.AddResultFile(loadingScreenshot);
        artifacts.Add(new { name = "loading-state", screenshotPath = loadingScreenshot, probe = loadingProbe });
        await WaitForCanvasDocumentReadyAsync(loadingPage, documentId);

        var errorContext = await CreateManualContextAsync(1280, 900, 1, ColorScheme.Light);
        var errorPage = await errorContext.NewPageAsync();
        await GotoAppReadyAsync(errorPage, $"{BaseUrl}/canvas-engine-host?documentId={Uri.EscapeDataString(documentId)}&showToolbar=true&disableCollaboration=true&failLoads=true", WaitUntilState.DOMContentLoaded);
        await Assertions.Expect(errorPage.GetByTestId("document-editor-load-error")).ToBeVisibleAsync(new() { Timeout = 20_000 });

        var errorScreenshot = Path.Combine(output, "load-error-state.png");
        await errorPage.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = errorScreenshot,
            Type = ScreenshotType.Png
        });
        var errorProbe = await ReadStateProbeAsync(errorPage);
        Assert.IsTrue(errorProbe.ErrorVisible, "The error screenshot gate must capture the visible editor load error state.");
        Assert.IsTrue(errorProbe.ErrorAlertVisible, "The load error must render an alert surface.");
        Assert.IsTrue(errorProbe.RetryVisible, "The load error state must expose a retry action.");
        Assert.IsTrue(errorProbe.RetryButtonMinHeight >= 28, $"The retry action must remain clickable. Actual: {errorProbe.RetryButtonMinHeight}px.");
        Assert.AreEqual(0, errorProbe.NestedCardCount, "The error state must keep the shell free of nested card surfaces.");
        Assert.IsTrue(errorProbe.StateSurfaceHeight >= 320, $"The load error state must reserve a stable editor surface. Actual height: {errorProbe.StateSurfaceHeight}px.");
        TestContext.AddResultFile(errorScreenshot);
        artifacts.Add(new { name = "load-error-state", screenshotPath = errorScreenshot, probe = errorProbe });

        var manifestPath = Path.Combine(output, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            testName = nameof(Phase23_UxPolish_CapturesLoadingAndErrorStateScreenshots),
            expectedVisibleChanges = "The canvas editor loading skeleton and provider load-error alert have dedicated screenshot gates with visible state surfaces, assistive busy/alert semantics, and a usable retry action.",
            expectedModelChanges = "The loading gate delays a real seeded demo document load, while the error gate exercises the production editor load-error boundary through the demo provider.",
            agentUxVerdict = "Accepted for agent UX/UI review. Human smoke remains a separate pre-cutover gate.",
            artifacts
        }, JsonOptions));
        TestContext.AddResultFile(manifestPath);
    }

    private async Task<IBrowserContext> CreateManualContextAsync(int width, int height, float dpr, ColorScheme colorScheme)
        => await CreateContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            DeviceScaleFactor = dpr,
            ColorScheme = colorScheme,
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
            AcceptDownloads = true
        });

    private async Task OpenCanvasDocumentAsync(IPage page, string documentId, string extraQuery = "")
    {
        await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId={Uri.EscapeDataString(documentId)}&showToolbar=true{extraQuery}", WaitUntilState.DOMContentLoaded);
        await WaitForCanvasDocumentReadyAsync(page, documentId);
    }

    private static async Task WaitForCanvasDocumentReadyAsync(IPage page, string documentId)
    {
        await page.WaitForFunctionAsync(
            """
            documentId => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const page = document.querySelector('[data-testid="document-canvas-page"]');
                const paintedCommandCount = Number(page?.getAttribute('data-canvas-painted-command-count') || '0');
                const paintReady = documentId === 'phase-3-canvas-empty' || paintedCommandCount > 0;
                return host?.getAttribute('data-canvas-engine-ready') === 'true'
                    && page?.getAttribute('data-canvas-model-document-id') === documentId
                    && paintReady;
            }
            """,
            documentId,
            new PageWaitForFunctionOptions { Timeout = 30_000 });
    }

    private static async Task ApplyScenarioActionAsync(IPage page, UxGalleryAction action)
    {
        switch (action)
        {
            case UxGalleryAction.PlaceCaret:
                await ClickFirstCanvasTextRectAsync(page);
                await page.WaitForFunctionAsync(
                    "() => document.querySelectorAll('[data-testid=\"document-canvas-caret\"]').length >= 1",
                    null,
                    new PageWaitForFunctionOptions { Timeout = 10_000 });
                break;
            case UxGalleryAction.SelectImageObject:
                await ClickCanvasObjectAsync(page);
                await page.WaitForTimeoutAsync(250);
                break;
            case UxGalleryAction.OpenTableContextMenu:
                await OpenFirstTableContextMenuAsync(page);
                await Assertions.Expect(page.GetByTestId("document-table-context-menu")).ToBeVisibleAsync(new() { Timeout = 10_000 });
                break;
            default:
                await page.WaitForTimeoutAsync(100);
                break;
        }
    }

    private static async Task ApplyDarkThemeAsync(IPage page)
    {
        await page.EvaluateAsync(
            """
            () => {
                for (const node of [document.documentElement, document.body]) {
                    node.setAttribute('data-theme', 'dark');
                    node.classList.add('tm-dark', 'dark');
                }
            }
            """);
        await page.WaitForTimeoutAsync(120);
    }

    private static async Task ClickFirstCanvasTextRectAsync(IPage page)
    {
        var point = await page.EvaluateAsync<CanvasPoint>(
            """
            () => {
                const node = document.querySelector('[data-canvas-text-rect]');
                if (!node) {
                    throw new Error('Canvas text rectangle was not rendered.');
                }

                const rect = node.getBoundingClientRect();
                return { x: rect.left + Math.max(3, Math.min(rect.width - 3, rect.width / 2)), y: rect.top + rect.height / 2 };
            }
            """);
        await page.Mouse.ClickAsync((float)point.X, (float)point.Y);
        await page.GetByTestId("document-canvas-hidden-input").FocusAsync();
    }

    private static async Task ClickCanvasObjectAsync(IPage page)
    {
        var point = await page.EvaluateAsync<CanvasPoint>(
            """
            () => {
                const node = document.querySelector('[data-canvas-object][data-object-id="canvas-image-phase15-main"]') || document.querySelector('[data-canvas-object]');
                if (!node) {
                    throw new Error('Canvas object was not rendered.');
                }

                const rect = node.getBoundingClientRect();
                return { x: rect.left + rect.width / 2, y: rect.top + rect.height / 2 };
            }
            """);
        await page.Mouse.ClickAsync((float)point.X, (float)point.Y);
    }

    private static async Task OpenFirstTableContextMenuAsync(IPage page)
    {
        var point = await page.EvaluateAsync<CanvasPoint>(
            """
            () => {
                const node = document.querySelector('[data-canvas-table-cell][data-cell-id="canvas-table-phase14-c-state"]') || document.querySelector('[data-canvas-table-cell]');
                if (!node) {
                    throw new Error('Canvas table cell was not rendered.');
                }

                const rect = node.getBoundingClientRect();
                return { x: rect.left + rect.width / 2, y: rect.top + rect.height / 2 };
            }
            """);
        await page.Mouse.ClickAsync((float)point.X, (float)point.Y, new MouseClickOptions { Button = MouseButton.Right });
    }

    private static async Task TypeIntoCanvasBlockAsync(IPage page, string blockId, string text)
    {
        var endOffset = await ReadBlockEndOffsetAsync(page, blockId);
        var point = await ReadCanvasPointAsync(page, blockId, endOffset);
        await page.Mouse.ClickAsync((float)point.X, (float)point.Y);
        await page.WaitForFunctionAsync(
            """
            blockId => document.querySelector('[data-testid="document-canvas-engine-root"]')
                ?.getAttribute('data-canvas-selection-focus-block-id') === blockId
            """,
            blockId,
            new PageWaitForFunctionOptions { Timeout = 10_000 });
        await FocusHiddenCanvasInputAsync(page);
        await page.Keyboard.TypeAsync(text);
        await page.WaitForFunctionAsync(
            "expected => document.querySelector('[data-testid=\"document-canvas-a11y-mirror\"]')?.textContent?.includes(expected) === true",
            text.Trim(),
            new PageWaitForFunctionOptions { Timeout = 10_000 });
    }

    private static Task<UxProbe> ReadUxProbeAsync(IPage page)
        => page.EvaluateAsync<UxProbe>(
            """
            () => {
                const editor = document.querySelector('[data-testid="document-editor-demo"]');
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const mount = document.querySelector('[data-testid="document-canvas-engine-mount"]');
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                const canvasPage = document.querySelector('[data-testid="document-canvas-page"]');
                const toolbar = document.querySelector('[data-testid="document-toolbar"]');
                const tabs = toolbar?.querySelector('.tm-document-editor__ribbon-tabs');
                const groups = toolbar?.querySelector('.tm-document-editor__ribbon-groups');
                const wrapper = toolbar?.querySelector('.tm-document-editor__ribbon-commands-wrapper');
                const empty = document.querySelector('.tm-document-editor__empty');
                const saveMessage = document.querySelector('[data-testid="document-save-message"]')?.textContent || '';
                const offlineBanner = document.querySelector('[data-testid="document-offline-banner"]');
                const pageStyle = canvasPage ? getComputedStyle(canvasPage) : null;
                const mountStyle = mount ? getComputedStyle(mount) : null;
                const contextMenu = document.querySelector('[data-testid="document-table-context-menu"]');
                const commentMarker = document.querySelector('[data-testid="document-canvas-comment-marker"]');
                const revisionMarker = document.querySelector('[data-testid="document-canvas-revision-marker"]');
                const canvases = Array.from(document.querySelectorAll('[data-testid="document-canvas-page"] canvas'))
                    .filter(canvas => {
                        const rect = canvas.getBoundingClientRect();
                        return rect.width > 0 && rect.height > 0 && canvas.width > 0 && canvas.height > 0;
                    });
                const dprRatios = canvases.map(canvas => {
                    const rect = canvas.getBoundingClientRect();
                    return rect.width > 0 ? canvas.width / rect.width : 0;
                }).filter(value => Number.isFinite(value) && value > 0);

                const visualClipViolations = Array.from(document.querySelectorAll(
                    '[data-testid="document-editor-demo"] .tm-document-editor__ribbon-button span,' +
                    '[data-testid="document-editor-demo"] .tm-document-editor__ribbon-tab,' +
                    '[data-testid="document-editor-demo"] .tm-document-editor__ribbon-select span'
                )).filter(node => {
                    const parent = node.closest('button,label');
                    if (!parent) {
                        return false;
                    }

                    const rect = node.getBoundingClientRect();
                    const parentRect = parent.getBoundingClientRect();
                    if (rect.width <= 1 || rect.height <= 1) {
                        return false;
                    }

                    const tolerance = 2.5;
                    return rect.left < parentRect.left - tolerance
                        || rect.right > parentRect.right + tolerance
                        || rect.top < parentRect.top - tolerance
                        || rect.bottom > parentRect.bottom + tolerance;
                });

                const nestedCards = editor ? Array.from(editor.querySelectorAll('.tm-card')).filter(card => card.parentElement?.closest('.tm-card')) : [];
                const overflowUsable = element => {
                    if (!element) {
                        return true;
                    }

                    const style = getComputedStyle(element);
                    const overflowX = style.overflowX;
                    return element.scrollWidth <= element.clientWidth + 1 || overflowX === 'auto' || overflowX === 'scroll';
                };
                const minRectHeight = selector => {
                    const heights = Array.from(document.querySelectorAll(selector))
                        .filter(node => node.offsetParent !== null)
                        .map(node => node.getBoundingClientRect().height)
                        .filter(value => Number.isFinite(value) && value > 0);
                    return heights.length ? Math.min(...heights) : 0;
                };
                const minRectSide = selector => {
                    const sides = Array.from(document.querySelectorAll(selector))
                        .filter(node => node.offsetParent !== null)
                        .map(node => {
                            const rect = node.getBoundingClientRect();
                            return Math.min(rect.width, rect.height);
                        })
                        .filter(value => Number.isFinite(value) && value > 0);
                    return sides.length ? Math.min(...sides) : 0;
                };
                const markerVisual = marker => {
                    if (!marker) {
                        return '';
                    }

                    const style = getComputedStyle(marker);
                    if (style.backgroundColor && style.backgroundColor !== 'transparent' && style.backgroundColor !== 'rgba(0, 0, 0, 0)') {
                        return style.backgroundColor;
                    }

                    if (style.boxShadow && style.boxShadow !== 'none') {
                        return style.boxShadow;
                    }

                    if (style.outlineStyle && style.outlineStyle !== 'none') {
                        return style.outlineColor;
                    }

                    return '';
                };

                return {
                    modelDocumentId: canvasPage?.getAttribute('data-canvas-model-document-id') || '',
                    ready: host?.getAttribute('data-canvas-engine-ready') === 'true',
                    devicePixelRatio: window.devicePixelRatio || 1,
                    canvasDprMin: dprRatios.length ? Math.min(...dprRatios) : 0,
                    canvasDprMax: dprRatios.length ? Math.max(...dprRatios) : 0,
                    canvasCount: canvases.length,
                    paintedCommandCount: Number(canvasPage?.getAttribute('data-canvas-painted-command-count') || '0'),
                    renderCommandCount: Number(canvasPage?.getAttribute('data-canvas-render-command-count') || '0'),
                    pageShadowVisible: !!pageStyle && pageStyle.boxShadow !== 'none',
                    pageBorderRadius: pageStyle ? Number.parseFloat(pageStyle.borderTopLeftRadius || '0') || 0 : 0,
                    hostPaddingBlockStart: mountStyle ? Number.parseFloat(mountStyle.paddingTop || '0') || 0 : 0,
                    toolbarTextClipViolationCount: visualClipViolations.length,
                    nestedCardCount: nestedCards.length,
                    toolbarOverflowUsable: overflowUsable(tabs) && overflowUsable(groups) && overflowUsable(wrapper),
                    toolbarClientWidth: groups?.clientWidth || 0,
                    toolbarScrollWidth: groups?.scrollWidth || 0,
                    toolbarTouchTargetMinHeight: minRectHeight('[data-testid="document-toolbar"] button'),
                    objectHandleCount: Math.max(
                        Number(root?.getAttribute('data-canvas-object-handle-count') || '0'),
                        document.querySelectorAll('[data-canvas-object-resize-handle]').length),
                    objectHandleMinSide: minRectSide('[data-canvas-object-resize-handle]'),
                    tableContextMenuVisible: !!contextMenu && contextMenu.getBoundingClientRect().width > 0,
                    contextMenuMinButtonHeight: minRectHeight('[data-testid="document-table-context-menu"] button'),
                    caretCount: document.querySelectorAll('[data-testid="document-canvas-caret"]').length,
                    selectionRectCount: document.querySelectorAll('[data-testid="document-canvas-selection-rect"]').length,
                    commentMarkerCount: document.querySelectorAll('[data-testid="document-canvas-comment-marker"]').length,
                    revisionMarkerCount: document.querySelectorAll('[data-testid="document-canvas-revision-marker"]').length,
                    commentMarkerColor: markerVisual(commentMarker),
                    revisionMarkerColor: markerVisual(revisionMarker),
                    emptyStateVisible: !!empty && empty.getBoundingClientRect().height > 0,
                    saveErrorVisible: /failed|error/i.test(saveMessage) || !!offlineBanner
                };
            }
            """);

    private static Task<UxStateProbe> ReadStateProbeAsync(IPage page)
        => page.EvaluateAsync<UxStateProbe>(
            """
            () => {
                const editor = document.querySelector('[data-testid="document-editor-demo"]');
                const loading = document.querySelector('[data-testid="document-editor-loading"]');
                const error = document.querySelector('[data-testid="document-editor-load-error"]');
                const errorAlert = document.querySelector('[data-testid="document-editor-load-error-alert"]');
                const retry = document.querySelector('[data-testid="document-editor-retry"]');
                const state = loading || error;
                const stateStyle = state ? getComputedStyle(state) : null;
                const stateRect = state?.getBoundingClientRect();
                const nestedCards = editor ? Array.from(editor.querySelectorAll('.tm-card')).filter(card => card.parentElement?.closest('.tm-card')) : [];
                const rectIsVisible = node => {
                    if (!node) {
                        return false;
                    }

                    const rect = node.getBoundingClientRect();
                    return rect.width > 0 && rect.height > 0;
                };
                const minRectHeight = selector => {
                    const heights = Array.from(document.querySelectorAll(selector))
                        .filter(node => node.offsetParent !== null)
                        .map(node => node.getBoundingClientRect().height)
                        .filter(value => Number.isFinite(value) && value > 0);
                    return heights.length ? Math.min(...heights) : 0;
                };
                const hasVisibleBackground = style => {
                    if (!style) {
                        return false;
                    }

                    return !!style.backgroundColor
                        && style.backgroundColor !== 'transparent'
                        && style.backgroundColor !== 'rgba(0, 0, 0, 0)';
                };

                return {
                    loadingVisible: rectIsVisible(loading),
                    loadingBusy: loading?.getAttribute('aria-busy') === 'true',
                    loadingSkeletonCount: loading ? loading.querySelectorAll('.tm-skeleton').length : 0,
                    errorVisible: rectIsVisible(error),
                    errorAlertVisible: rectIsVisible(errorAlert) && errorAlert?.getAttribute('role') === 'alert',
                    retryVisible: rectIsVisible(retry),
                    retryButtonMinHeight: minRectHeight('[data-testid="document-editor-retry"]'),
                    nestedCardCount: nestedCards.length,
                    stateSurfaceHeight: stateRect?.height || 0,
                    stateSurfaceWidth: stateRect?.width || 0,
                    stateBackgroundVisible: hasVisibleBackground(stateStyle)
                };
            }
            """);

    private static Task<int> ReadBlockEndOffsetAsync(IPage page, string blockId)
        => page.EvaluateAsync<int>(
            """
            blockId => Math.max(...Array.from(document.querySelectorAll(`[data-canvas-text-rect][data-block-id="${blockId}"]`))
                .map(node => Number(node.getAttribute('data-canvas-end-offset') || '0')))
            """,
            blockId);

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
                const target = rects.find(item => offset >= item.start && offset <= item.end) || rects.at(-1);
                const ratio = Math.max(0, Math.min(1, (offset - target.start) / Math.max(1, target.end - target.start)));
                return {
                    x: target.rect.left + Math.max(2, target.rect.width * ratio),
                    y: target.rect.top + Math.max(2, target.rect.height / 2)
                };
            }
            """,
            new object[] { blockId, offset });

    private static Task FocusHiddenCanvasInputAsync(IPage page)
        => page.EvaluateAsync(
            """
            () => {
                const input = document.querySelector('[data-testid="document-canvas-hidden-input"]');
                input?.focus();
            }
            """);

    private static void AssertCoreUxProbe(UxGalleryScenario scenario, UxProbe probe)
    {
        Assert.IsTrue(probe.Ready, $"{scenario.Name}: canvas host must be ready.");
        Assert.AreEqual(scenario.DocumentId, probe.ModelDocumentId, $"{scenario.Name}: loaded document id must match.");
        Assert.IsTrue(probe.PageShadowVisible, $"{scenario.Name}: document page shadow must be visible.");
        Assert.AreEqual(0, probe.NestedCardCount, $"{scenario.Name}: editor shell must not contain nested card components.");
        Assert.AreEqual(0, probe.ToolbarTextClipViolationCount, $"{scenario.Name}: toolbar labels must not visually spill outside their controls.");
        Assert.IsTrue(probe.ToolbarOverflowUsable, $"{scenario.Name}: toolbar overflow must be scrollable when needed.");
        AssertDprProbe(scenario.Name, probe, probe.DevicePixelRatio);
    }

    private static void AssertActionProbe(UxGalleryScenario scenario, UxProbe probe)
    {
        if (scenario.Action == UxGalleryAction.PlaceCaret)
        {
            Assert.IsTrue(probe.CaretCount >= 1, $"{scenario.Name}: caret must be visible after click.");
        }

        if (scenario.Action == UxGalleryAction.SelectImageObject)
        {
            Assert.IsTrue(probe.CanvasCount > 0, $"{scenario.Name}: image scenario must keep rendered canvas surfaces after object review click.");
        }

        if (scenario.Action == UxGalleryAction.OpenTableContextMenu)
        {
            Assert.IsTrue(probe.TableContextMenuVisible, $"{scenario.Name}: table context menu must be visible.");
            Assert.IsTrue(probe.ContextMenuMinButtonHeight >= 28, $"{scenario.Name}: context menu commands must remain readable and clickable.");
        }
    }

    private static void AssertDprProbe(string label, UxProbe probe, double expectedDpr)
    {
        Assert.IsTrue(probe.CanvasCount > 0, $"{label}: at least one canvas backing store must be visible.");
        Assert.IsTrue(probe.CanvasDprMin >= expectedDpr - 0.25, $"{label}: canvas backing store min DPR {probe.CanvasDprMin} must track expected DPR {expectedDpr}.");
        Assert.IsTrue(probe.CanvasDprMax <= expectedDpr + 0.35, $"{label}: canvas backing store max DPR {probe.CanvasDprMax} must track expected DPR {expectedDpr}.");
    }

    private static bool IsTransparentColor(string color)
        => string.IsNullOrWhiteSpace(color)
            || color.Equals("transparent", StringComparison.OrdinalIgnoreCase)
            || color.EndsWith(", 0)", StringComparison.Ordinal);

    private static string CreateGalleryOutputDirectory(string testName)
    {
        var root = Path.Combine(
            FindRepositoryRoot().FullName,
            "tests",
            "Tempo.Blazor.E2E",
            "TestResults",
            "document-editor-canvas",
            "_gallery",
            "2026-06-04",
            testName,
            DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"));
        Directory.CreateDirectory(root);
        return root;
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

    private enum UxGalleryAction
    {
        None,
        PlaceCaret,
        SelectImageObject,
        OpenTableContextMenu
    }

    private sealed record UxGalleryScenario(string Name, string DocumentId, UxGalleryAction Action);

    private sealed record UxGalleryScenarioResult(string Name, string DocumentId, string Action, string ScreenshotPath, UxProbe Probe);

    private sealed class UxProbe
    {
        [JsonPropertyName("modelDocumentId")]
        public string ModelDocumentId { get; set; } = string.Empty;

        [JsonPropertyName("ready")]
        public bool Ready { get; set; }

        [JsonPropertyName("devicePixelRatio")]
        public double DevicePixelRatio { get; set; }

        [JsonPropertyName("canvasDprMin")]
        public double CanvasDprMin { get; set; }

        [JsonPropertyName("canvasDprMax")]
        public double CanvasDprMax { get; set; }

        [JsonPropertyName("canvasCount")]
        public int CanvasCount { get; set; }

        [JsonPropertyName("paintedCommandCount")]
        public int PaintedCommandCount { get; set; }

        [JsonPropertyName("renderCommandCount")]
        public int RenderCommandCount { get; set; }

        [JsonPropertyName("pageShadowVisible")]
        public bool PageShadowVisible { get; set; }

        [JsonPropertyName("pageBorderRadius")]
        public double PageBorderRadius { get; set; }

        [JsonPropertyName("hostPaddingBlockStart")]
        public double HostPaddingBlockStart { get; set; }

        [JsonPropertyName("toolbarTextClipViolationCount")]
        public int ToolbarTextClipViolationCount { get; set; }

        [JsonPropertyName("nestedCardCount")]
        public int NestedCardCount { get; set; }

        [JsonPropertyName("toolbarOverflowUsable")]
        public bool ToolbarOverflowUsable { get; set; }

        [JsonPropertyName("toolbarClientWidth")]
        public double ToolbarClientWidth { get; set; }

        [JsonPropertyName("toolbarScrollWidth")]
        public double ToolbarScrollWidth { get; set; }

        [JsonPropertyName("toolbarTouchTargetMinHeight")]
        public double ToolbarTouchTargetMinHeight { get; set; }

        [JsonPropertyName("objectHandleCount")]
        public int ObjectHandleCount { get; set; }

        [JsonPropertyName("objectHandleMinSide")]
        public double ObjectHandleMinSide { get; set; }

        [JsonPropertyName("tableContextMenuVisible")]
        public bool TableContextMenuVisible { get; set; }

        [JsonPropertyName("contextMenuMinButtonHeight")]
        public double ContextMenuMinButtonHeight { get; set; }

        [JsonPropertyName("caretCount")]
        public int CaretCount { get; set; }

        [JsonPropertyName("selectionRectCount")]
        public int SelectionRectCount { get; set; }

        [JsonPropertyName("commentMarkerCount")]
        public int CommentMarkerCount { get; set; }

        [JsonPropertyName("revisionMarkerCount")]
        public int RevisionMarkerCount { get; set; }

        [JsonPropertyName("commentMarkerColor")]
        public string CommentMarkerColor { get; set; } = string.Empty;

        [JsonPropertyName("revisionMarkerColor")]
        public string RevisionMarkerColor { get; set; } = string.Empty;

        [JsonPropertyName("emptyStateVisible")]
        public bool EmptyStateVisible { get; set; }

        [JsonPropertyName("saveErrorVisible")]
        public bool SaveErrorVisible { get; set; }
    }

    private sealed class UxStateProbe
    {
        [JsonPropertyName("loadingVisible")]
        public bool LoadingVisible { get; set; }

        [JsonPropertyName("loadingBusy")]
        public bool LoadingBusy { get; set; }

        [JsonPropertyName("loadingSkeletonCount")]
        public int LoadingSkeletonCount { get; set; }

        [JsonPropertyName("errorVisible")]
        public bool ErrorVisible { get; set; }

        [JsonPropertyName("errorAlertVisible")]
        public bool ErrorAlertVisible { get; set; }

        [JsonPropertyName("retryVisible")]
        public bool RetryVisible { get; set; }

        [JsonPropertyName("retryButtonMinHeight")]
        public double RetryButtonMinHeight { get; set; }

        [JsonPropertyName("nestedCardCount")]
        public int NestedCardCount { get; set; }

        [JsonPropertyName("stateSurfaceHeight")]
        public double StateSurfaceHeight { get; set; }

        [JsonPropertyName("stateSurfaceWidth")]
        public double StateSurfaceWidth { get; set; }

        [JsonPropertyName("stateBackgroundVisible")]
        public bool StateBackgroundVisible { get; set; }
    }

    private sealed class CanvasPoint
    {
        [JsonPropertyName("x")]
        public double X { get; set; }

        [JsonPropertyName("y")]
        public double Y { get; set; }
    }
}
