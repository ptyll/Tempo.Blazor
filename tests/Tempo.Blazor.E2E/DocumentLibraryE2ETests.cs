using System.Net.Http.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 2 E2E for the document open dialog over the live Demo.Api library
/// (WASM demo at 7106, API at 5100). Covers browsing, search, view toggle, and folder
/// management, plus named screenshots for UX review.
/// </summary>
[TestClass]
public class DocumentLibraryE2ETests : WasmTestBase
{
    private const string DialogPage = "/document-open-dialog";

    private async Task<IPage> OpenDialogPageAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 900);
        await page.GotoAsync($"{BaseUrl}{DialogPage}",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        return page;
    }

    private static async Task OpenDialogAsync(IPage page)
    {
        await page.ClickAsync("[data-testid=open-dialog-btn]");
        await page.WaitForSelectorAsync(".tm-document-open-dialog",
            new PageWaitForSelectorOptions { Timeout = 15000 });
    }

    private static async Task NavigateFolderAsync(IPage page, string folderText)
    {
        var node = page.Locator(".tm-dod-tree-node", new() { HasTextString = folderText }).First;
        await node.ClickAsync();
    }

    private static async Task SaveScreenshotAsync(IPage page, string fileName)
    {
        var dir = Path.Combine(FindRepoRoot().FullName,
            "tests", "Tempo.Blazor.E2E", "__screenshots__", "document-library", "phase2");
        Directory.CreateDirectory(dir);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(dir, fileName),
            Type = ScreenshotType.Png,
            FullPage = true
        });
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
            {
                return current;
            }
            current = current.Parent!;
        }
        throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx.");
    }

    [TestMethod]
    public async Task DocLib1_BrowseSearchAndViewToggle()
    {
        var page = await OpenDialogPageAsync();
        await OpenDialogAsync(page);

        // Tree shows seeded folders.
        var tree = await page.InnerTextAsync(".tm-dod-tree");
        StringAssert.Contains(tree, "All documents");
        StringAssert.Contains(tree, "Designs");

        // Navigate into Designs → seeded documents appear.
        await NavigateFolderAsync(page, "Designs");
        await page.WaitForSelectorAsync(".tm-dod-row");
        var rowCount = await page.Locator(".tm-dod-row").CountAsync();
        Assert.IsTrue(rowCount >= 2, $"expected >=2 rows, got {rowCount}");

        // Grid view toggle renders cards.
        await page.ClickAsync(".tm-dod-view-grid");
        await page.WaitForSelectorAsync(".tm-dod-card");
        Assert.IsTrue(await page.Locator(".tm-dod-card").CountAsync() >= 2);

        // Back to list, search filters.
        await page.ClickAsync(".tm-dod-view-list");
        await page.FillAsync(".tm-dod-search", "Login");
        await page.WaitForFunctionAsync(
            "() => [...document.querySelectorAll('.tm-dod-row')].some(r => r.dataset.name && r.dataset.name.includes('Login'))");
    }

    [TestMethod]
    public async Task DocLib1_NewFolderRenameDelete()
    {
        // Operates in /Archive so it does not deplete the /Designs documents other tests rely on.
        // Seed a sacrificial document first: the dialog store lives in the API process memory and
        // the seeded "Old prototype" row is consumed by this very test — on a warm API /Archive can
        // already be empty and the .tm-dod-row wait below would time out.
        var sacrificial = "Sacrificial-" + Guid.NewGuid().ToString("N")[..6];
        using (var http = new HttpClient())
        {
            var resp = await http.PostAsJsonAsync(
                "https://localhost:5100/api/document-library/wireframe/documents",
                new { name = sacrificial, folderPath = "/Archive", payloadJson = "{}", author = "E2E" });
            resp.EnsureSuccessStatusCode();
        }

        var page = await OpenDialogPageAsync();
        await OpenDialogAsync(page);
        await NavigateFolderAsync(page, "Archive");
        await page.WaitForSelectorAsync(".tm-dod-row");

        // New folder.
        var folderName = "E2E-" + Guid.NewGuid().ToString("N")[..6];
        await page.ClickAsync(".tm-dod-new-folder");
        await page.FillAsync(".tm-dod-new-folder-input", folderName);
        await page.ClickAsync(".tm-dod-new-folder-confirm");
        await page.WaitForFunctionAsync(
            $"() => document.querySelector('.tm-dod-tree').textContent.includes('{folderName}')");

        // Select the sacrificial row, rename it.
        await page.Locator(".tm-dod-row", new() { HasTextString = sacrificial }).First.ClickAsync();
        await page.ClickAsync(".tm-dod-rename");
        var newName = "Renamed-" + Guid.NewGuid().ToString("N")[..6];
        await page.FillAsync(".tm-dod-rename-input", newName);
        await page.ClickAsync(".tm-dod-rename-confirm");
        await page.WaitForFunctionAsync(
            $"() => [...document.querySelectorAll('.tm-dod-row')].some(r => r.dataset.name === '{newName}')");

        // Select renamed row, delete it.
        await page.Locator(".tm-dod-row", new() { HasTextString = newName }).First.ClickAsync();
        await page.ClickAsync(".tm-dod-delete");
        await page.ClickAsync(".tm-dod-delete-confirm-ok");
        await page.WaitForFunctionAsync(
            $"() => ![...document.querySelectorAll('.tm-dod-row')].some(r => r.dataset.name === '{newName}')");
    }

    [TestMethod]
    public async Task DocLib1_PickDocument_ReportsResult()
    {
        var page = await OpenDialogPageAsync();
        await OpenDialogAsync(page);
        await NavigateFolderAsync(page, "Designs");
        await page.WaitForSelectorAsync(".tm-dod-row");

        await page.Locator(".tm-dod-row").First.DblClickAsync();

        await page.WaitForSelectorAsync("[data-testid=open-result]");
        var name = await page.InnerTextAsync("[data-testid=result-name]");
        Assert.IsFalse(string.IsNullOrWhiteSpace(name));
    }

    [TestMethod]
    public async Task DocLibShot1_Screenshots()
    {
        var page = await OpenDialogPageAsync();
        await OpenDialogAsync(page);
        await NavigateFolderAsync(page, "Designs");
        await page.WaitForSelectorAsync(".tm-dod-row");
        await SaveScreenshotAsync(page, "01-list-view.png");

        await page.ClickAsync(".tm-dod-view-grid");
        await page.WaitForSelectorAsync(".tm-dod-card");
        await SaveScreenshotAsync(page, "02-grid-view.png");

        await page.ClickAsync(".tm-dod-view-list");
        await page.ClickAsync(".tm-dod-new-folder");
        await page.WaitForSelectorAsync(".tm-dod-new-folder-input");
        await SaveScreenshotAsync(page, "03-new-folder.png");

        // Cancel new folder editor, select a row, open delete confirm.
        await page.Locator(".tm-dod-row").First.ClickAsync();
        await page.ClickAsync(".tm-dod-delete");
        await page.WaitForSelectorAsync(".tm-dod-delete-confirm");
        await SaveScreenshotAsync(page, "04-delete-confirm.png");
    }
}
