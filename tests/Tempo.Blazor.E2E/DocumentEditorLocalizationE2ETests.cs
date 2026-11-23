using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 6: editor localization — switching the demo culture renders the editor toolbar and ribbon
/// in Czech; screenshots capture both languages. Edge case: an unknown culture value falls back to
/// English without raw localization keys leaking into the UI.
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[DoNotParallelize]
public sealed class DocumentEditorLocalizationE2ETests : WasmTestBase
{
    private const string DocumentId = "phase-5-canvas-render";

    [TestMethod]
    public async Task CultureSwitch_RendersEditorToolbarInCzechAndEnglish()
    {
        // Czech: the demo host reads tm-demo-culture from localStorage at startup.
        var czech = await OpenEditorWithCultureAsync("cs");
        await Assertions.Expect(czech.GetByTestId("document-ribbon-tab-home")).ToHaveTextAsync("Domů", new() { Timeout = 15_000 });
        await Assertions.Expect(czech.GetByTestId("document-ribbon-tab-review")).ToHaveTextAsync("Revize", new() { Timeout = 5_000 });
        await Assertions.Expect(czech.GetByTestId("document-save")).ToContainTextAsync("Uložit", new() { Timeout = 5_000 });
        await AssertNoRawKeysAsync(czech);
        await ScreenshotAsync(czech, "01-editor-czech.png");

        // English default.
        var english = await OpenEditorWithCultureAsync("en");
        await Assertions.Expect(english.GetByTestId("document-ribbon-tab-home")).ToHaveTextAsync("Home", new() { Timeout = 15_000 });
        await Assertions.Expect(english.GetByTestId("document-save")).ToContainTextAsync("Save", new() { Timeout = 5_000 });
        await AssertNoRawKeysAsync(english);
        await ScreenshotAsync(english, "02-editor-english.png");
    }

    /// <summary>Edge case: unknown culture value falls back to English without leaking raw keys.</summary>
    [TestMethod]
    public async Task UnknownCulture_FallsBackToEnglishWithoutRawKeys()
    {
        var page = await OpenEditorWithCultureAsync("xx-XX");

        await Assertions.Expect(page.GetByTestId("document-ribbon-tab-home")).ToHaveTextAsync("Home", new() { Timeout = 15_000 });
        await AssertNoRawKeysAsync(page);
    }

    private async Task<IPage> OpenEditorWithCultureAsync(string culture)
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync($"localStorage.setItem('tm-demo-culture', '{culture}')");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 900);
        await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId={DocumentId}&showToolbar=true&preferLocalDraft=false", WaitUntilState.DOMContentLoaded);
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelector('[data-testid="document-ribbon-tab-home"]')
            """, 45_000);
        return page;
    }

    private static async Task AssertNoRawKeysAsync(IPage page)
    {
        var rawKeyCount = await page.EvaluateAsync<int>(
            "() => (document.body.innerText.match(/TmDocumentEditor_[A-Za-z]+/g) || []).length");
        Assert.AreEqual(0, rawKeyCount, "no raw localization keys may leak into the visible UI");
    }

    private static async Task ScreenshotAsync(IPage page, string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        var dir = Path.Combine(directory!.FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "document-editor-localization");
        Directory.CreateDirectory(dir);
        await page.GetByTestId("document-toolbar").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(dir, fileName),
            Type = ScreenshotType.Png
        });
    }
}
