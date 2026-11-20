using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Tempo.Blazor.E2E.DataTableE2EKit;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 8 (plan 98c80bc3): the /pickers Attachment Manager section gains a read-only sample —
/// <c>ReadOnly="true"</c> renders the existing attachments (download stays) but NO delete buttons
/// and NO upload drop zone, with the <c>tm-attachment-manager--readonly</c> class on the root.
/// The full manager next to it keeps both. Light/dark screenshots of the section land in
/// <c>__screenshots__/files-f8/</c> for the UX review.
/// </summary>
[TestClass]
public sealed class FilesF8E2ETests : WasmTestBase
{
    private static string OutputDirectory =>
        Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "__screenshots__", "files-f8");

    private async Task<IPage> OpenPickersAsync(int width = 1440, int height = 900)
    {
        var page = await OpenAsync(this, "/pickers", new Options(width, height));
        await page.Locator(".tm-attachment-manager").First.WaitForAsync(
            new LocatorWaitForOptions { Timeout = 30_000 });
        // The attachment manager section sits far down the page — bring it to the top of the
        // viewport so the light/dark captures show the section, not the page header.
        await page.Locator(".tm-attachment-manager").First.ScrollIntoViewIfNeededAsync();
        return page;
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Pickers_ReadOnlyAttachmentManager_HidesDeleteAndUpload_KeepsFullManager()
    {
        var page = await OpenPickersAsync();

        var managers = page.Locator(".tm-attachment-manager");
        await Assertions.Expect(managers).ToHaveCountAsync(2, new() { Timeout = 20_000 });

        var readOnly = page.Locator(".tm-attachment-manager--readonly");
        await Assertions.Expect(readOnly).ToHaveCountAsync(1);
        await Assertions.Expect(readOnly.Locator(".tm-file-drop-zone")).ToHaveCountAsync(0);
        await Assertions.Expect(readOnly.Locator(".tm-attachment-delete")).ToHaveCountAsync(0);

        var full = page.Locator(".tm-attachment-manager:not(.tm-attachment-manager--readonly)");
        await Assertions.Expect(full).ToHaveCountAsync(1);
        await Assertions.Expect(full.Locator(".tm-file-drop-zone").First).ToBeVisibleAsync();
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Pickers_AttachmentManagerSection_CapturesLightAndDark()
    {
        var page = await OpenPickersAsync();

        Directory.CreateDirectory(OutputDirectory);
        await SettleAsync(page);
        await CaptureAsync(page, "pickers-attachment-manager-light-1440");

        await page.Locator("[data-testid='theme-toggle']:visible").First.ClickAsync();
        await page.WaitForSelectorAsync("[data-theme='dark']", new PageWaitForSelectorOptions { Timeout = 15_000 });
        await SettleAsync(page);
        await CaptureAsync(page, "pickers-attachment-manager-dark-1440");
    }

    private async Task CaptureAsync(IPage page, string name)
    {
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync("() => document.activeElement && document.activeElement.blur()");
        await page.WaitForTimeoutAsync(400);

        var path = Path.Combine(OutputDirectory, name + ".png");
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = path,
            Type = ScreenshotType.Png,
            FullPage = false,
            Timeout = 60_000,
        });
        TestContext.AddResultFile(path);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx.");
    }
}
