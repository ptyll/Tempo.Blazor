using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tempo.Blazor.E2E.CanvasEngine;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Command-layer plan phase 8: document protection enforcement. The ribbon toggled C# state and
/// routed setProtectionMode, but the engine never registered the command — so typing stayed
/// possible everywhere and the editable-region markers never reached the engine model.
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorProtectionE2ETests : WasmTestBase
{
    private const string DocumentId = "phase-12-canvas-history-save";

    [TestInitialize]
    public Task ResetDocumentEditorDemoAsync()
        => DocumentEditorE2EReset.ResetAsync();

    [TestMethod]
    public async Task Phase8_ProtectionBlocksTypingOutsideEditableRegionAndPersists()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId={DocumentId}&showToolbar=true", WaitUntilState.DOMContentLoaded);
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelectorAll('[data-canvas-text-rect][data-block-id="canvas-history-text"]').length >= 1
                && document.querySelectorAll('[data-canvas-text-rect][data-block-id="canvas-history-format"]').length >= 1
            """, 30_000);

        var output = CreateOutputDirectory();
        var protectedPath = Path.Combine(output, "00-protected-with-region.png");
        var reloadPath = Path.Combine(output, "01-after-reload.png");

        // Protect the document from the Review ribbon.
        await page.GetByTestId("document-ribbon-tab-review").ClickAsync();
        await DocumentEditorRedesignE2EHelpers.ClickRibbonAsync(page, "document-protect-document");
        await WaitForProtectionStateAsync(page, isProtected: true, minMarkers: 0);

        // Typing outside any editable region must be a no-op.
        await ClickTextBlockAsync(page, "canvas-history-text");
        await page.EvaluateAsync("() => document.querySelector('[data-testid=\"document-canvas-hidden-input\"]')?.focus()");
        await page.Keyboard.TypeAsync("BLOCKEDTEXT");
        await page.WaitForTimeoutAsync(1200);
        var mirrorAfterBlocked = await ReadMirrorAsync(page);
        Assert.IsFalse(mirrorAfterBlocked.Contains("BLOCKEDTEXT", StringComparison.Ordinal),
            "typing in a protected document outside editable regions must not change the content");

        // Select a range in the second paragraph and mark it editable.
        await SelectTextRangeAsync(page, "canvas-history-format", 0, 10);
        await page.GetByTestId("document-ribbon-tab-review").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-mark-editable-region")).ToBeEnabledAsync(new() { Timeout = 10_000 });
        await DocumentEditorRedesignE2EHelpers.ClickRibbonAsync(page, "document-mark-editable-region");
        await WaitForProtectionStateAsync(page, isProtected: true, minMarkers: 1);

        // Typing inside the marked region must pass.
        await ClickTextBlockAsync(page, "canvas-history-format");
        await page.EvaluateAsync("() => document.querySelector('[data-testid=\"document-canvas-hidden-input\"]')?.focus()");
        await page.Keyboard.TypeAsync("OK");
        await WaitForPredicateAsync(page, "() => document.querySelector('[data-testid=\"document-canvas-a11y-mirror\"]')?.textContent?.includes('OK') === true", 10_000);

        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions { Path = protectedPath, Type = ScreenshotType.Png });

        // Persist: protection + markers survive save/reload; typing outside stays blocked.
        await page.GetByTestId("document-save").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-save-message")).ToContainTextAsync("Saved", new() { Timeout = 10_000 });
        await NavigateWithinBlazorAsync(page, "/canvas-engine-host?documentId=phase-5-canvas-render");
        await WaitForPredicateAsync(page, "() => document.querySelector('[data-testid=\"document-canvas-page\"]')?.getAttribute('data-canvas-model-document-id') === 'phase-5-canvas-render'", 20_000);
        await NavigateWithinBlazorAsync(page, $"/canvas-engine-host?documentId={DocumentId}&showToolbar=true");
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelectorAll('[data-canvas-text-rect][data-block-id="canvas-history-text"]').length >= 1
            """, 30_000);
        await WaitForProtectionStateAsync(page, isProtected: true, minMarkers: 1);

        await ClickTextBlockAsync(page, "canvas-history-text");
        await page.EvaluateAsync("() => document.querySelector('[data-testid=\"document-canvas-hidden-input\"]')?.focus()");
        await page.Keyboard.TypeAsync("STILLBLOCKED");
        await page.WaitForTimeoutAsync(1200);
        var mirrorAfterReload = await ReadMirrorAsync(page);
        Assert.IsFalse(mirrorAfterReload.Contains("STILLBLOCKED", StringComparison.Ordinal),
            "protection must survive save/reload and keep blocking edits outside editable regions");
        Assert.IsTrue(mirrorAfterReload.Contains("OK", StringComparison.Ordinal),
            "the text typed inside the editable region must persist");

        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions { Path = reloadPath, Type = ScreenshotType.Png });

        var manifestPath = Path.Combine(output, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            testName = nameof(Phase8_ProtectionBlocksTypingOutsideEditableRegionAndPersists),
            seedDocumentId = DocumentId,
            userActions = new[]
            {
                "Protect the document from the Review ribbon.",
                "Try typing in a locked paragraph — nothing changes.",
                "Select a range, mark it as an editable region (button stays enabled while protected — lockout regression), and type inside it.",
                "Save, reload, and verify protection plus the marker survive; typing outside is still blocked."
            },
            expectedVisibleChanges = "Typing outside editable regions is fully vetoed by the engine while protected; the marked region accepts edits; the protection state persists across save/reload.",
            screenshotPaths = new[] { protectedPath, reloadPath }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        TestContext.AddResultFile(protectedPath);
        TestContext.AddResultFile(reloadPath);
        TestContext.AddResultFile(manifestPath);
    }

    private static Task<string> ReadMirrorAsync(IPage page)
        => page.EvaluateAsync<string>(
            "() => document.querySelector('[data-testid=\"document-canvas-a11y-mirror\"]')?.textContent || ''");

    private static Task WaitForProtectionStateAsync(IPage page, bool isProtected, int minMarkers)
        => WaitForPredicateAsync(
            page,
            """
            async ([expectedProtected, expectedMarkers]) => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const model = JSON.parse(module.getModelJson(host.getAttribute('data-canvas-engine-handle')) || '{}');
                const markers = model.restrictedMarkers || [];
                return (model.isProtected === true) === (expectedProtected === true) && markers.length >= expectedMarkers;
            }
            """,
            new object[] { isProtected, minMarkers },
            10_000);

    private static async Task ClickTextBlockAsync(IPage page, string blockId)
    {
        for (var attempt = 0; ; attempt++)
        {
            var point = await page.EvaluateAsync<double[]>(
                """
                blockId => {
                    const rect = document.querySelector(`[data-canvas-text-rect][data-block-id="${blockId}"]`)?.getBoundingClientRect();
                    if (!rect) throw new Error(`no text rect for ${blockId}`);
                    return [rect.left + Math.min(30, rect.width / 2), rect.top + rect.height / 2];
                }
                """,
                blockId);
            await page.Mouse.ClickAsync((float)point[0], (float)point[1]);
            var focused = await page.EvaluateAsync<string>(
                "() => document.querySelector('[data-testid=\"document-canvas-engine-root\"]')?.getAttribute('data-canvas-selection-focus-block-id') || ''");
            if (focused == blockId)
            {
                return;
            }

            if (attempt >= 9)
            {
                Assert.Fail($"Click kept resolving to block '{focused}' instead of {blockId}.");
            }

            await page.WaitForTimeoutAsync(250);
        }
    }

    private static async Task SelectTextRangeAsync(IPage page, string blockId, int startOffset, int endOffset)
    {
        var points = await page.EvaluateAsync<double[]>(
            """
            ([blockId, startOffset, endOffset]) => {
                const rects = Array.from(document.querySelectorAll(`[data-canvas-text-rect][data-block-id="${blockId}"]`))
                    .map(node => ({
                        rect: node.getBoundingClientRect(),
                        start: Number(node.getAttribute('data-canvas-start-offset') || '0'),
                        end: Number(node.getAttribute('data-canvas-end-offset') || '0'),
                    }))
                    .filter(item => item.end > item.start);
                const point = offset => {
                    const target = rects.find(item => offset >= item.start && offset <= item.end) || rects.at(-1);
                    const ratio = Math.max(0, Math.min(1, (offset - target.start) / Math.max(1, target.end - target.start)));
                    return [target.rect.left + Math.max(2, target.rect.width * ratio), target.rect.top + target.rect.height / 2];
                };
                return [...point(startOffset), ...point(endOffset)];
            }
            """,
            new object[] { blockId, startOffset, endOffset });
        await page.Mouse.MoveAsync((float)points[0], (float)points[1]);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)points[2], (float)points[3], new MouseMoveOptions { Steps = 8 });
        await page.Mouse.UpAsync();
        await WaitForPredicateAsync(page, """
            blockId => document.querySelector('[data-testid="document-canvas-engine-root"]')
                ?.getAttribute('data-canvas-selection-focus-block-id') === blockId
                && document.querySelector('[data-testid="document-canvas-engine-root"]')
                    ?.getAttribute('data-canvas-selection-collapsed') === 'false'
            """, blockId, 10_000);
    }

    private static Task NavigateWithinBlazorAsync(IPage page, string url)
        => page.EvaluateAsync(
            """
            url => window.Blazor?.navigateTo
                ? window.Blazor.navigateTo(url)
                : window.history.pushState({}, '', url)
            """,
            url);

    private string CreateOutputDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
        {
            current = current.Parent;
        }

        var output = Path.Combine(
            current!.FullName,
            "tests", "Tempo.Blazor.E2E", "TestResults", "document-editor-canvas",
            nameof(DocumentEditorProtectionE2ETests), "phase8-protection");
        Directory.CreateDirectory(output);
        return output;
    }
}
