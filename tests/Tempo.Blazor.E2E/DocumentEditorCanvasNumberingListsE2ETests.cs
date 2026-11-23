using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tempo.Blazor.E2E.CanvasEngine;

namespace Tempo.Blazor.E2E;

/// <summary>E1 E2E coverage for canvas numbering definitions, multilevel lists, and list label layout.</summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasNumberingListsE2ETests : WasmTestBase
{
    private const string PhaseE1DocumentId = "phase-e1-canvas-numbering-lists";

    [TestMethod]
    public async Task PhaseE1_CanvasNumberingListsRenderPersistAndAvoidLabelOverlap()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenPhaseE1DocumentAsync(page);

        var output = CreateOutputDirectory("desktop-1440x1000");
        var beforePath = Path.Combine(output, "00-phasee1-numbering-before.png");
        var afterPath = Path.Combine(output, "01-phasee1-numbering-after-reload.png");

        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = beforePath,
            Type = ScreenshotType.Png
        });

        var initialProbe = await ReadPhaseE1ProbeAsync(page);
        Assert.AreEqual(PhaseE1DocumentId, initialProbe.ModelDocumentId);
        var expectedLegalLabels = new[] { "1.", "1.1.", "1.1.1.", "2.", "7.", "7.1." };
        Assert.IsTrue(
            expectedLegalLabels.All(expected => initialProbe.Labels.Contains(expected, StringComparer.Ordinal)),
            $"The legal multilevel and restarted labels should be visible in canvas metadata. Actual labels: {string.Join(", ", initialProbe.Labels)}. Model levels: {string.Join(", ", initialProbe.ListLevels)}. Definitions: {initialProbe.NumberingDefinitionCount}.");
        Assert.IsTrue(initialProbe.HasBulletLabels, "Bullet numbering definition should render bullet labels.");
        Assert.IsTrue(initialProbe.NoLabelOverlap, "List labels must stay outside the first text segment.");

        await DocumentEditorCanvasVisualAssert.AssertNoTextOverlapAsync(page);
        await DocumentEditorCanvasVisualAssert.AssertNoUiOverlapAsync(page);
        var contentMetrics = await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(page.Locator("[data-canvas-layer='content']").First);

        await page.GetByTestId("document-save").ClickAsync();
        await WaitForSaveBoundaryAsync(page);

        await NavigateWithinBlazorAsync(page, "/canvas-engine-host?documentId=phase-5-canvas-render");
        await WaitForPredicateAsync(page, "() => document.querySelector('[data-testid=\"document-canvas-page\"]')?.getAttribute('data-canvas-model-document-id') === 'phase-5-canvas-render'", 20_000);
        await NavigateWithinBlazorAsync(page, $"/canvas-engine-host?documentId={PhaseE1DocumentId}&showToolbar=true");
        await WaitForPhaseE1ReadyAsync(page);

        var reloadedProbe = await ReadPhaseE1ProbeAsync(page);
        CollectionAssert.AreEquivalent(initialProbe.Labels, reloadedProbe.Labels);
        Assert.IsTrue(reloadedProbe.NoLabelOverlap);

        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = afterPath,
            Type = ScreenshotType.Png
        });

        var manifestPath = Path.Combine(output, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            testName = nameof(PhaseE1_CanvasNumberingListsRenderPersistAndAvoidLabelOverlap),
            seedDocumentId = PhaseE1DocumentId,
            userActions = new[]
            {
                "Open the phase E1 canvas numbering seed document.",
                "Verify legal multilevel labels, explicit restart value, bullet labels, and hanging list label layout.",
                "Save through the production Save command, navigate away, navigate back, and verify numbering labels survive reload."
            },
            expectedVisibleChanges = "The canvas page shows Word-like legal multilevel numbering with 1., 1.1., 1.1.1., a restarted 7. article, nested 7.1. text, and bullet labels that do not overlap wrapped body text.",
            screenshotPaths = new[] { beforePath, afterPath },
            initialProbe,
            reloadedProbe,
            contentMetrics
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        TestContext.AddResultFile(beforePath);
        TestContext.AddResultFile(afterPath);
        TestContext.AddResultFile(manifestPath);
    }

    private async Task OpenPhaseE1DocumentAsync(IPage page)
    {
        await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId={PhaseE1DocumentId}&showToolbar=true");
        await WaitForPhaseE1ReadyAsync(page);
    }

    private static Task WaitForPhaseE1ReadyAsync(IPage page)
        => WaitForPredicateAsync(page, """
            () => {
                const hostReady = document.querySelector('[data-testid="document-canvas-engine-host"]')?.getAttribute('data-canvas-engine-ready') === 'true';
                const first = document.querySelector('[data-testid="document-canvas-page"]');
                return hostReady
                    && first?.getAttribute('data-canvas-model-document-id') === 'phase-e1-canvas-numbering-lists'
                    && Number(first.getAttribute('data-canvas-text-run-count') || '0') > 0
                    && document.querySelectorAll('[data-canvas-text-rect]').length > 0;
            }
            """, 30_000);

    private static Task<PhaseE1Probe> ReadPhaseE1ProbeAsync(IPage page)
        => page.EvaluateAsync<PhaseE1Probe>(
            """
            () => {
                const first = document.querySelector('[data-testid="document-canvas-page"]');
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const allTextRects = Array.from(document.querySelectorAll('[data-canvas-text-rect]'));
                const isLabelText = text => /^(\d+(\.\d+)*\.|\u2022|\u25e6|\u25aa)$/.test(text);
                const labels = allTextRects.filter(rect => isLabelText(rect.getAttribute('data-canvas-text') || ''));
                const labelTexts = labels.map(label => label.getAttribute('data-canvas-text') || '');
                const noLabelOverlap = labels.every(label => {
                    const blockId = label.getAttribute('data-block-id') || '';
                    const labelRect = label.getBoundingClientRect();
                    const firstText = allTextRects.find(rect =>
                        rect.getAttribute('data-block-id') === blockId
                        && !(rect.getAttribute('data-command-id') || '').endsWith('-list-label'));
                    if (!firstText) return true;
                    const textRect = firstText.getBoundingClientRect();
                    return labelRect.right < textRect.left - 0.5;
                });
                return import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs').then(module => {
                    const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                    const model = handle ? JSON.parse(module.getModelJson(handle)) : {};
                    const listBlocks = (model.body?.blocks || []).filter(block => block.type === 'list' || block.content?.type === 'list');
                    return {
                    modelDocumentId: first?.getAttribute('data-canvas-model-document-id') || '',
                    labels: labelTexts,
                    hasBulletLabels: labelTexts.some(text => text === '\u2022' || text === '\u25e6' || text === '\u25aa'),
                    noLabelOverlap,
                    listLevels: listBlocks.map(block => Number(block.content?.list?.indentLevel ?? -1)),
                    numberingDefinitionCount: (model.numberingDefinitions || []).length
                    };
                });
            }
            """);

    private static async Task WaitForSaveBoundaryAsync(IPage page)
    {
        await WaitForPredicateAsync(page, """
            () => {
                const saveMessage = document.querySelector('[data-testid="document-save-message"]')?.textContent || '';
                const lastSaved = document.querySelector('[data-testid="document-last-saved"]')?.textContent || '';
                const pending = document.querySelector('[data-testid="document-pending-status"]')?.textContent || '';
                const saveButtonDisabled = document.querySelector('[data-testid="document-save"]')?.hasAttribute('disabled') === true;
                return saveButtonDisabled === false
                    && pending.trim().length === 0
                    && (/Saved|Autosaved/i.test(saveMessage) || /saved/i.test(lastSaved));
            }
            """, 10_000);
    }

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
            "phasee1-numbering-lists",
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

    /// <summary>Browser-side phase E1 render state.</summary>
    public sealed class PhaseE1Probe
    {
        /// <summary>Document id reported by the first canvas page.</summary>
        public string ModelDocumentId { get; set; } = string.Empty;

        /// <summary>Rendered list label texts.</summary>
        public string[] Labels { get; set; } = [];

        /// <summary>Whether bullet labels are present.</summary>
        public bool HasBulletLabels { get; set; }

        /// <summary>Whether list labels stay left of the first text segment.</summary>
        public bool NoLabelOverlap { get; set; }

        /// <summary>List levels present in the canvas model.</summary>
        public int[] ListLevels { get; set; } = [];

        /// <summary>Numbering definition count in the canvas model.</summary>
        public int NumberingDefinitionCount { get; set; }
    }
}
