using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 9: legal filing format (soudní podání). The Czech filing seed renders per-page line
/// numbering in the left margin (layout/line-numbering.mjs → canvas lineNumber commands) and the
/// case-file margin note (č.l.) in the header. Edge case: an ordinary document without line
/// numbering renders zero line-number labels.
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[DoNotParallelize]
public sealed class DocumentEditorLegalFilingE2ETests : WasmTestBase
{
    private const string DocumentId = "phase-9-canvas-legal-filing";

    [TestMethod]
    public async Task LegalFiling_RendersLineNumbersAndCaseFileHeader()
    {
        var page = await OpenAsync(DocumentId, "Okresnímu soudu");

        // Line numbering paints as lineNumber display-list commands; the page exposes the count.
        await WaitForPredicateAsync(page, """
            () => Number(document.querySelector('[data-testid="document-canvas-page"]')
                ?.getAttribute('data-canvas-line-number-count') || '0') >= 8
            """, 20_000);

        // The č.l. margin note renders from the header part — assert it via the print layout
        // snapshot (the a11y mirror reflects only body blocks).
        var printedTexts = await page.EvaluateAsync<string>(
            """
            async () => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const snapshot = JSON.parse(module.getLayoutSnapshotJson(handle) || '{}');
                const texts = [];
                for (const page of snapshot.pages || []) {
                    for (const command of page.commands || []) {
                        if (typeof command.text === 'string') {
                            texts.push(command.text);
                        }
                    }
                }
                return texts.join('\n');
            }
            """);
        StringAssert.Contains(printedTexts, "č.l.");

        var mirror = await page.EvaluateAsync<string>(
            "() => document.querySelector('[data-testid=\"document-canvas-a11y-mirror\"]')?.textContent || ''");
        StringAssert.Contains(mirror, "Sp. zn.: 12 C 34/2026");

        await ScreenshotAsync(page, "01-legal-filing-line-numbers.png");
    }

    /// <summary>Edge case: a document without LineNumbering renders no line-number labels.</summary>
    [TestMethod]
    public async Task DocumentWithoutLineNumbering_RendersNoLineNumberLabels()
    {
        var page = await OpenAsync("phase-8-canvas-role-comments", "Smluvní strany");

        var count = await page.EvaluateAsync<int>(
            """
            () => Number(document.querySelector('[data-testid="document-canvas-page"]')
                ?.getAttribute('data-canvas-line-number-count') || '-1')
            """);
        Assert.AreEqual(0, count, "a document without LineNumbering must not paint line-number labels");
    }

    private async Task<IPage> OpenAsync(string documentId, string readyText)
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'cs')");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1100);
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

        var dir = Path.Combine(directory!.FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "document-editor-legal-filing");
        Directory.CreateDirectory(dir);
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(dir, fileName),
            Type = ScreenshotType.Png
        });
    }
}
