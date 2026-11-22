using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 7: proofing out-of-the-box. The Czech seed document is checked through the reference
/// LanguageTool provider (Tempo.Blazor.Proofing.LanguageTool) against the demo API's
/// protocol-compatible endpoint: misspelled Czech words get squiggles, the context menu offers the
/// dictionary correction, and applying it fixes the text. Edge case: an unreachable LanguageTool
/// server is fail-open — no squiggles, no errors, editing keeps working.
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[DoNotParallelize]
public sealed class DocumentEditorProofingE2ETests : WasmTestBase
{
    private const string DocumentId = "phase-7-proofing-czech";

    [TestMethod]
    public async Task CzechText_LanguageToolProvider_UnderlinesAndFixesFromContextMenu()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenProofingDocumentAsync(page, "languagetool");

        // The async provider pass runs after load; both seeded Czech misspellings get squiggles.
        var squiggle = page.GetByTestId("document-canvas-spell-squiggle").First;
        await Assertions.Expect(squiggle).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await page.WaitForFunctionAsync(
            """
            () => Number(document.querySelector('[data-testid="document-canvas-engine-root"]')
                ?.getAttribute('data-canvas-proofing-count') || '0') === 2
            """,
            options: new PageWaitForFunctionOptions { Timeout = 15_000 });
        await ScreenshotAsync(page, "01-czech-squiggles.png");

        // Right-click the first squiggle (smlouvva) and take the LanguageTool suggestion.
        var box = await squiggle.BoundingBoxAsync();
        Assert.IsNotNull(box, "The spell squiggle must expose a hit-test rectangle.");
        await page.Mouse.ClickAsync(
            (float)(box.X + box.Width / 2),
            (float)(box.Y + box.Height / 2),
            new MouseClickOptions { Button = MouseButton.Right });
        await Assertions.Expect(page.GetByTestId("document-text-context-menu")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByTestId("document-context-spell-suggestion").First)
            .ToContainTextAsync("smlouva", new() { Timeout = 5_000 });
        await ScreenshotAsync(page, "02-context-menu-suggestion.png");

        await page.GetByTestId("document-context-spell-suggestion").First.ClickAsync();
        await page.WaitForFunctionAsync(
            """
            () => {
                const mirror = document.querySelector('[data-testid="document-canvas-a11y-mirror"]');
                return mirror?.textContent?.includes('Tato smlouva byla') === true
                    && mirror?.textContent?.includes('smlouvva') === false;
            }
            """,
            options: new PageWaitForFunctionOptions { Timeout = 10_000 });
        await ScreenshotAsync(page, "03-after-fix.png");

        // The debounced re-check against the live model leaves only the remaining misspelling.
        await page.WaitForFunctionAsync(
            """
            () => Number(document.querySelector('[data-testid="document-canvas-engine-root"]')
                ?.getAttribute('data-canvas-proofing-count') || '0') === 1
            """,
            options: new PageWaitForFunctionOptions { Timeout = 20_000 });

        var probe = await ReadProofingProbeAsync(page);
        Assert.AreEqual(DocumentId, probe.ModelDocumentId);
        Assert.AreEqual(1, probe.ProofingCount, "only 'chybbou' remains flagged after the fix");
        Assert.IsTrue(probe.SquiggleCount >= 1);
    }

    /// <summary>Edge case: unreachable LanguageTool server → fail-open, no squiggles, editing works.</summary>
    [TestMethod]
    public async Task UnreachableLanguageTool_IsFailOpen_NoSquigglesAndEditorKeepsWorking()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenProofingDocumentAsync(page, "languagetool-down");

        // Give the failed provider pass room to (not) land, then verify the clean state.
        await page.WaitForTimeoutAsync(2_000);
        var probe = await ReadProofingProbeAsync(page);
        Assert.AreEqual(0, probe.ProofingCount, "an unreachable proofing server must not flag anything");
        Assert.AreEqual(0, probe.SquiggleCount);

        // The editor stays fully usable: type into the document and see the text land.
        await page.GetByTestId("document-canvas-page").First.ClickAsync();
        await page.Keyboard.TypeAsync("Ahoj");
        await page.WaitForFunctionAsync(
            """
            () => document.querySelector('[data-testid="document-canvas-a11y-mirror"]')
                ?.textContent?.includes('Ahoj') === true
            """,
            options: new PageWaitForFunctionOptions { Timeout = 10_000 });
        await ScreenshotAsync(page, "04-fail-open-editing.png");
    }

    private async Task OpenProofingDocumentAsync(IPage page, string proofingMode)
    {
        await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId={DocumentId}&showToolbar=true&proofing={proofingMode}&preferLocalDraft=false&disableCollaboration=true&resetSeed=true", WaitUntilState.DOMContentLoaded);
        await page.WaitForSelectorAsync(
            "[data-testid='document-canvas-engine-host'][data-canvas-engine-ready='true']",
            new PageWaitForSelectorOptions { Timeout = 45_000 });
        await page.WaitForFunctionAsync(
            """
            () => document.querySelector('[data-testid="document-canvas-a11y-mirror"]')
                ?.textContent?.includes('Dodavatel') === true
            """,
            options: new PageWaitForFunctionOptions { Timeout = 30_000 });
    }

    private static Task<ProofingProbe> ReadProofingProbeAsync(IPage page)
        => page.EvaluateAsync<ProofingProbe>(
            """
            () => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                return {
                    modelDocumentId: document.querySelector('[data-testid="document-canvas-page"]')?.getAttribute('data-canvas-model-document-id') || '',
                    proofingCount: Number(root?.getAttribute('data-canvas-proofing-count') || '0'),
                    squiggleCount: Number(root?.getAttribute('data-canvas-proofing-squiggle-count') || '0')
                };
            }
            """);

    private static async Task ScreenshotAsync(IPage page, string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        var dir = Path.Combine(directory!.FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "document-editor-proofing");
        Directory.CreateDirectory(dir);
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(dir, fileName),
            Type = ScreenshotType.Png
        });
    }

    private sealed class ProofingProbe
    {
        public string ModelDocumentId { get; set; } = string.Empty;

        public int ProofingCount { get; set; }

        public int SquiggleCount { get; set; }
    }
}
