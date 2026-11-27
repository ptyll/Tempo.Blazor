using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tempo.Blazor.E2E.CanvasEngine;

namespace Tempo.Blazor.E2E;

/// <summary>E4 E2E coverage for canvas document styles, quick styles, style modification, undo, and save/reload.</summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasStylesE2ETests : WasmTestBase
{
    private const string PhaseE4DocumentId = "phase-e4-canvas-styles";

    [TestMethod]
    public async Task PhaseE4_CanvasStyleGalleryModifyUndoSaveReload()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenPhaseE4DocumentAsync(page);

        var output = CreateOutputDirectory("desktop-1440x1000");
        var beforePath = Path.Combine(output, "00-phasee4-styles-before.png");
        var afterPath = Path.Combine(output, "01-phasee4-styles-after-reload.png");

        var initialProbe = await ReadProbeAsync(page);
        Assert.AreEqual(PhaseE4DocumentId, initialProbe.ModelDocumentId);
        Assert.AreEqual(20, initialProbe.Heading1FontSize);
        Assert.IsTrue(initialProbe.StyleCount >= 2);

        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = beforePath,
            Type = ScreenshotType.Png
        });

        await page.GetByTestId("document-block-style").SelectOptionAsync("Heading1");
        await DocumentEditorRedesignE2EHelpers.ClickRibbonAsync(page, "document-style-gallery-toggle");
        await Assertions.Expect(page.GetByTestId("document-style-gallery")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5_000 });
        await page.GetByTestId("document-style-name").FillAsync("Executive Accent");
        await page.GetByTestId("document-style-create").ClickAsync();
        await WaitForMinimumStyleCountAsync(page, initialProbe.StyleCount + 1);
        await page.GetByTestId("document-block-style").SelectOptionAsync("Heading1");
        await page.GetByTestId("document-style-font-size").FillAsync("26");
        await page.GetByTestId("document-style-modify").ClickAsync();
        await WaitForHeading1FontSizeAsync(page, 26);

        await Assertions.Expect(page.GetByTestId("document-undo")).ToBeEnabledAsync(new LocatorAssertionsToBeEnabledOptions { Timeout = 5_000 });
        await page.GetByTestId("document-undo").ClickAsync();
        await WaitForHeading1FontSizeAsync(page, 20);
        await page.GetByTestId("document-redo").ClickAsync();
        await WaitForHeading1FontSizeAsync(page, 26);

        await DocumentEditorCanvasVisualAssert.AssertNoUiOverlapAsync(page);
        var contentMetrics = await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(page.Locator("[data-canvas-layer='content']").First);

        await page.GetByTestId("document-save").ClickAsync();
        await WaitForDirtyStateAsync(page, expectedDirty: false);

        await NavigateWithinBlazorAsync(page, "/canvas-engine-host?documentId=phase-5-canvas-render");
        await WaitForPredicateAsync(page, "() => document.querySelector('[data-testid=\"document-canvas-page\"]')?.getAttribute('data-canvas-model-document-id') === 'phase-5-canvas-render'", 20_000);
        await NavigateWithinBlazorAsync(page, $"/canvas-engine-host?documentId={PhaseE4DocumentId}&showToolbar=true");
        await WaitForPhaseE4DocumentAsync(page);

        var reloadedProbe = await ReadProbeAsync(page);
        Assert.AreEqual(26, reloadedProbe.Heading1FontSize, "Modified Heading 1 style font size should survive save and Blazor navigation reload.");

        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = afterPath,
            Type = ScreenshotType.Png
        });

        var manifestPath = Path.Combine(output, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            testName = nameof(PhaseE4_CanvasStyleGalleryModifyUndoSaveReload),
            seedDocumentId = PhaseE4DocumentId,
            userActions = new[]
            {
                "Open the phase E4 canvas styles seed document with the production toolbar.",
                "Create a custom paragraph style from the current selection through the style gallery.",
                "Use the block style selector and style gallery to modify Heading 1 font size.",
                "Undo and redo the style modification through the production toolbar.",
                "Save, navigate away, navigate back, and verify the modified style survives reload."
            },
            expectedVisibleChanges = "Heading 1 uses a larger document-level style after modification, and the style store reports the changed font size after reload.",
            screenshotPaths = new[] { beforePath, afterPath },
            initialProbe,
            reloadedProbe,
            contentMetrics
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        TestContext.AddResultFile(beforePath);
        TestContext.AddResultFile(afterPath);
        TestContext.AddResultFile(manifestPath);
    }

    private async Task OpenPhaseE4DocumentAsync(IPage page)
    {
        await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId={PhaseE4DocumentId}&showToolbar=true");
        await WaitForPhaseE4ReadyAsync(page, 20);
    }

    private static Task WaitForPhaseE4ReadyAsync(IPage page, int fontSize)
        => page.WaitForFunctionAsync(
            """
            expected => {
                const hostReady = document.querySelector('[data-testid="document-canvas-engine-host"]')?.getAttribute('data-canvas-engine-ready') === 'true';
                const first = document.querySelector('[data-testid="document-canvas-page"]');
                return hostReady
                    && first?.getAttribute('data-canvas-model-document-id') === 'phase-e4-canvas-styles'
                    && Number(first.getAttribute('data-canvas-style-heading1-font-size') || '0') === expected
                    && Number(first.getAttribute('data-canvas-style-count') || '0') >= 2;
            }
            """,
            fontSize,
            new PageWaitForFunctionOptions { Timeout = 30_000 });

    private static Task WaitForPhaseE4DocumentAsync(IPage page)
        => WaitForPredicateAsync(page, """
            () => {
                const hostReady = document.querySelector('[data-testid="document-canvas-engine-host"]')?.getAttribute('data-canvas-engine-ready') === 'true';
                const first = document.querySelector('[data-testid="document-canvas-page"]');
                return hostReady
                    && first?.getAttribute('data-canvas-model-document-id') === 'phase-e4-canvas-styles'
                    && Number(first.getAttribute('data-canvas-style-count') || '0') >= 2;
            }
            """, 30_000);

    private static Task WaitForHeading1FontSizeAsync(IPage page, int expected)
        => page.WaitForFunctionAsync(
            """
            expected => Number(document.querySelector('[data-testid="document-canvas-page"]')?.getAttribute('data-canvas-style-heading1-font-size') || '0') === expected
            """,
            expected,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task WaitForMinimumStyleCountAsync(IPage page, int expected)
        => page.WaitForFunctionAsync(
            """
            expected => Number(document.querySelector('[data-testid="document-canvas-page"]')?.getAttribute('data-canvas-style-count') || '0') >= expected
            """,
            expected,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task<PhaseE4Probe> ReadProbeAsync(IPage page)
        => page.EvaluateAsync<PhaseE4Probe>(
            """
            () => {
                const first = document.querySelector('[data-testid="document-canvas-page"]');
                return {
                    modelDocumentId: first?.getAttribute('data-canvas-model-document-id') || '',
                    styleCount: Number(first?.getAttribute('data-canvas-style-count') || '0'),
                    heading1FontSize: Number(first?.getAttribute('data-canvas-style-heading1-font-size') || '0')
                };
            }
            """);

    private static Task WaitForDirtyStateAsync(IPage page, bool expectedDirty)
        => page.WaitForFunctionAsync(
            """
            expectedDirty => {
                const dirty = document.querySelector('[data-testid="document-dirty-status"]');
                return expectedDirty ? !!dirty : !dirty;
            }
            """,
            expectedDirty,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task NavigateWithinBlazorAsync(IPage page, string url)
        => page.EvaluateAsync(
            """
            url => window.Blazor?.navigateTo
                ? window.Blazor.navigateTo(url)
                : window.history.pushState({}, '', url)
            """,
            url);

    private static string CreateOutputDirectory(string viewport)
    {
        var output = Path.Combine(
            FindRepositoryRoot().FullName,
            "tests",
            "Tempo.Blazor.E2E",
            "TestResults",
            "document-editor-canvas",
            "phasee4-styles",
            viewport);
        Directory.CreateDirectory(output);
        return output;
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
        {
            current = current.Parent;
        }

        return current ?? new DirectoryInfo(Directory.GetCurrentDirectory());
    }

    /// <summary>Browser-side phase E4 style state.</summary>
    public sealed class PhaseE4Probe
    {
        /// <summary>Document id reported by the first canvas page.</summary>
        public string ModelDocumentId { get; set; } = string.Empty;

        /// <summary>Document style definition count reported by the page diagnostics.</summary>
        public int StyleCount { get; set; }

        /// <summary>Resolved Heading 1 font size in points.</summary>
        public int Heading1FontSize { get; set; }
    }
}
