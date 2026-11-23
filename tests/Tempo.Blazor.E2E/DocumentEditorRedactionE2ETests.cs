using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 10: redaction with REAL content removal. The redaction-marked bank account renders as a
/// black bar on the canvas, and the print layout snapshot (the exact source of the WYSIWYG PDF
/// export) contains only block characters — the original digits do not exist anywhere in it.
/// Edge case: a document without redaction marks exports its text unchanged.
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[DoNotParallelize]
public sealed class DocumentEditorRedactionE2ETests : WasmTestBase
{
    private const string DocumentId = "phase-10-canvas-redaction";
    private const string Secret = "123456789/0100";

    [TestMethod]
    public async Task RedactedRun_PaintsBlackBarAndIsDestroyedInThePrintSnapshot()
    {
        var page = await OpenAsync(DocumentId, "Kupní cena");

        var snapshotJson = await ReadLayoutSnapshotAsync(page);
        Assert.IsFalse(snapshotJson.Contains(Secret),
            "the redacted characters must not exist anywhere in the print snapshot");
        StringAssert.Contains(snapshotJson, new string('█', Secret.Length));
        // Snapshot text commands are word-granular — assert a single word of the public text.
        StringAssert.Contains(snapshotJson, "Komerční");

        await ScreenshotAsync(page, "01-redaction-black-bar.png");
    }

    /// <summary>Edge case: documents without redaction marks export unchanged.</summary>
    [TestMethod]
    public async Task DocumentWithoutRedactions_KeepsAllTextInThePrintSnapshot()
    {
        var page = await OpenAsync("phase-9-canvas-legal-filing", "Okresnímu soudu");

        var snapshotJson = await ReadLayoutSnapshotAsync(page);
        StringAssert.Contains(snapshotJson, "34/2026");
        Assert.IsFalse(snapshotJson.Contains('█'), "no block characters without redaction marks");
    }

    private static Task<string> ReadLayoutSnapshotAsync(IPage page)
        => page.EvaluateAsync<string>(
            """
            async () => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                return module.getLayoutSnapshotJson(handle) || '';
            }
            """);

    private async Task<IPage> OpenAsync(string documentId, string readyText)
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'cs')");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId={documentId}&showToolbar=true&preferLocalDraft=false&disableCollaboration=true&resetSeed=true", WaitUntilState.DOMContentLoaded);
        await page.WaitForSelectorAsync(
            "[data-testid='document-canvas-engine-host'][data-canvas-engine-ready='true']",
            new PageWaitForSelectorOptions { Timeout = 45_000 });
        await WaitForPredicateAsync(page, $"() => document.querySelector('[data-testid=\"document-canvas-a11y-mirror\"]')?.textContent?.includes('{readyText}') === true", 30_000);
        return page;
    }

    private static async Task ScreenshotAsync(IPage page, string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        var dir = Path.Combine(directory!.FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "document-editor-redaction");
        Directory.CreateDirectory(dir);
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(dir, fileName),
            Type = ScreenshotType.Png
        });
    }
}
