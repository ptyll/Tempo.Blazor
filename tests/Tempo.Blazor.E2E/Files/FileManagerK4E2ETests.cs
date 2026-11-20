using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E.Files;

/// <summary>
/// E2E for K4 on the File Manager demo (WASM @ 7106): chunked 100 MB upload with progress,
/// virus-scan blocking a file (rendered unavailable), and the version-history compare + restore.
/// Screenshots land in <c>__screenshots__/files/</c> for UX review.
/// </summary>
[TestClass]
public class FileManagerK4E2ETests : WasmTestBase
{
    private const string FileManagerPage = "/file-manager";

    private async Task<IPage> OpenChunkedTabAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1100);
        await GotoAppReadyAsync(page, $"{BaseUrl}{FileManagerPage}");

        await page.GetByRole(AriaRole.Tab, new() { Name = "Chunked Upload", Exact = false }).ClickAsync();
        await page.Locator("[data-testid='dms-chunked']").WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        return page;
    }

    private static ILocator ChunkInput(IPage page)
        => page.Locator("[data-testid='dms-chunked'] input[type='file']");

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Files_ChunkedUpload_100MB_ShowsProgressAndCompletes()
    {
        var page = await OpenChunkedTabAsync();

        var path = CreateSizedFile("large-upload.bin", 100L * 1024 * 1024);
        try
        {
            await ChunkInput(page).SetInputFilesAsync(path);

            // A progress panel appears; the completed item then reads 100%.
            await page.Locator("[data-testid='tm-upload-progress']")
                .WaitForAsync(new LocatorWaitForOptions { Timeout = 120000 });
            await Assertions.Expect(page.Locator("[data-testid='upload-item'][data-state='completed']"))
                .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 120000 });

            // The uploaded file shows in the listing.
            await Assertions.Expect(page.GetByText("large-upload.bin").First)
                .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30000 });

            await SaveScreenshotAsync(page, "chunked-upload-complete");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Files_ScanBlocked_MakesFileUnavailable()
    {
        var page = await OpenChunkedTabAsync();

        var path = CreateSizedFile("virus-sample.bin", 2 * 1024 * 1024);
        try
        {
            await ChunkInput(page).SetInputFilesAsync(path);

            // Scan blocks it — a Blocked badge is shown.
            await page.Locator("[data-testid='file-scan-blocked']")
                .WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });

            // Selecting the blocked file offers no Download action.
            await page.Locator(".tm-file-manager__item", new PageLocatorOptions { HasTextString = "virus-sample.bin" })
                .First.ClickAsync();
            await Assertions.Expect(
                page.Locator(".tm-file-manager__toolbar-button", new PageLocatorOptions { HasTextString = "Download" }))
                .ToHaveCountAsync(0);

            await SaveScreenshotAsync(page, "scan-blocked-unavailable");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Files_VersionHistory_CompareAndRestore()
    {
        var page = await OpenChunkedTabAsync();

        var path = CreateSizedFile("notes.txt", 1024);
        try
        {
            await ChunkInput(page).SetInputFilesAsync(path);
            await Assertions.Expect(page.GetByText("notes.txt").First)
                .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 60000 });

            // Select the file and open its version history.
            await page.Locator(".tm-file-manager__item", new PageLocatorOptions { HasTextString = "notes.txt" })
                .First.ClickAsync();
            await page.Locator("[data-testid='version-history-button']").ClickAsync();

            await page.Locator("[data-testid='tm-file-versions']")
                .WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
            await Assertions.Expect(page.Locator("[data-testid='version-item']"))
                .ToHaveCountAsync(2);

            // Compare an older version with current → text diff appears.
            await page.Locator("[data-testid='version-compare']").First.ClickAsync();
            await Assertions.Expect(page.Locator("[data-testid='version-diff']"))
                .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
            await SaveScreenshotAsync(page, "version-diff");

            // Restore the older version → a new current version is appended.
            await page.Locator("[data-testid='version-restore']").First.ClickAsync();
            await Assertions.Expect(page.Locator("[data-testid='version-item']"))
                .ToHaveCountAsync(3);
            await SaveScreenshotAsync(page, "version-restored");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static string CreateSizedFile(string name, long sizeBytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"tm-k4-{Guid.NewGuid():N}-{name}");
        using var fs = File.Create(path);
        fs.SetLength(sizeBytes); // zero-filled, allocated without buffering the payload
        return path;
    }

    private static async Task SaveScreenshotAsync(IPage page, string fileName)
    {
        var dir = Path.Combine(FindRepoRoot().FullName,
            "tests", "Tempo.Blazor.E2E", "__screenshots__", "files");
        Directory.CreateDirectory(dir);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(dir, $"{fileName}.png"), FullPage = true });
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
            {
                return directory;
            }
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Repository root was not found.");
    }
}
