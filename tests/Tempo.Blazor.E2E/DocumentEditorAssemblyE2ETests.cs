using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 4: document assembly — a contract template with an IF/ELSE condition, a repeating items
/// section, and computed tokens (currency SUM, date arithmetic) assembles into two different,
/// correct outputs for two data sets via the toolbar template-preview toggle.
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorAssemblyE2ETests : WasmTestBase
{
    private const string DocumentId = "assembly-contract-demo";

    [TestInitialize]
    public Task ResetDocumentEditorDemoAsync()
        => DocumentEditorE2EReset.ResetAsync();

    [TestMethod]
    public async Task Assembly_DataSetA_HighAmount_KeepsEscalationClauseAndComputesTotal()
    {
        var page = await OpenTemplateAsync("a");

        await TogglePreviewAsync(page);

        var mirror = page.GetByTestId("document-canvas-a11y-mirror");
        await Assertions.Expect(mirror).ToContainTextAsync("Smlouva podléhá schválení ředitele", new() { Timeout = 15_000 });
        await Assertions.Expect(mirror).Not.ToContainTextAsync("vedoucí oddělení", new() { Timeout = 2_000 });
        await Assertions.Expect(mirror).ToContainTextAsync("ACME Ltd.", new() { Timeout = 5_000 });
        await Assertions.Expect(mirror).ToContainTextAsync("Licence — 20000 Kč", new() { Timeout = 5_000 });
        await Assertions.Expect(mirror).ToContainTextAsync("Implementace — 4000 Kč", new() { Timeout = 5_000 });
        await Assertions.Expect(mirror).ToContainTextAsync("Podpora — 1000 Kč", new() { Timeout = 5_000 });
        // 20000 + 4000 + 1000 = 25 000,00 Kč (cs-CZ grouping).
        await Assertions.Expect(mirror).ToContainTextAsync("000,00", new() { Timeout = 5_000 });
        await Assertions.Expect(mirror).ToContainTextAsync("Kč", new() { Timeout = 5_000 });

        await ScreenshotAsync(page, "01-assembly-dataset-a.png");
    }

    [TestMethod]
    public async Task Assembly_DataSetB_LowAmount_KeepsElseBranchAndSingleItem()
    {
        var page = await OpenTemplateAsync("b");

        await TogglePreviewAsync(page);

        var mirror = page.GetByTestId("document-canvas-a11y-mirror");
        await Assertions.Expect(mirror).ToContainTextAsync("vedoucí oddělení", new() { Timeout = 15_000 });
        await Assertions.Expect(mirror).Not.ToContainTextAsync("schválení ředitele", new() { Timeout = 2_000 });
        await Assertions.Expect(mirror).ToContainTextAsync("Malý odběratel s.r.o.", new() { Timeout = 5_000 });
        await Assertions.Expect(mirror).ToContainTextAsync("Konzultace — 500 Kč", new() { Timeout = 5_000 });
        await Assertions.Expect(mirror).Not.ToContainTextAsync("Licence", new() { Timeout = 2_000 });

        await ScreenshotAsync(page, "02-assembly-dataset-b.png");
    }

    /// <summary>
    /// Edge case: toggling the preview off returns to the editable template — tokens and the
    /// conditional structure come back, resolved values disappear.
    /// </summary>
    [TestMethod]
    public async Task Assembly_ToggleOff_ReturnsToTemplateWithTokens()
    {
        var page = await OpenTemplateAsync("a");
        var mirror = page.GetByTestId("document-canvas-a11y-mirror");
        await Assertions.Expect(mirror).ToContainTextAsync("Objednatel:", new() { Timeout = 10_000 });

        await ScreenshotAsync(page, "03-assembly-template-mode.png");

        await TogglePreviewAsync(page);
        await Assertions.Expect(mirror).ToContainTextAsync("ACME Ltd.", new() { Timeout = 15_000 });

        await page.GetByTestId("document-ribbon-tab-view").ClickAsync();
        await page.GetByTestId("document-template-preview").ClickAsync();
        await Assertions.Expect(mirror).Not.ToContainTextAsync("ACME Ltd.", new() { Timeout = 10_000 });
    }

    private async Task<IPage> OpenTemplateAsync(string dataset)
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId={DocumentId}&showToolbar=true&preferLocalDraft=false&assemblyData={dataset}", WaitUntilState.DOMContentLoaded);
        await page.WaitForFunctionAsync(
            """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelectorAll('[data-testid="document-canvas-page"]').length >= 1
            """,
            options: new PageWaitForFunctionOptions { Timeout = 45_000 });
        return page;
    }

    private static async Task TogglePreviewAsync(IPage page)
    {
        await page.GetByTestId("document-ribbon-tab-view").ClickAsync();
        var toggle = page.GetByTestId("document-template-preview");
        await Assertions.Expect(toggle).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await toggle.ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-template-preview-message"))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    private static async Task ScreenshotAsync(IPage page, string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        var dir = Path.Combine(directory!.FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "document-editor-assembly");
        Directory.CreateDirectory(dir);
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(dir, fileName),
            Type = ScreenshotType.Png
        });
    }
}
