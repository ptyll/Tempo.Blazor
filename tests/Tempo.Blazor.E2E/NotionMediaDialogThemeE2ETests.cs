using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>F-24/F-25: the phase-7 notion-media/media-* shots are orphaned (their writer is gone).
/// Re-capture the set against the current dialog and assert the body is dark in the dark theme.</summary>
[TestClass]
public class NotionMediaDialogThemeE2ETests : NotionE2ETestBase
{
    [TestMethod]
    public async Task RecaptureMediaUploadDialog()
    {
        var page = await OpenNotionEditorAsync(1440, 900);
        await SeedMediaPageAsync();
        var dir = Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "__screenshots__", "notion-media", "phase-7");
        Directory.CreateDirectory(dir);

        foreach (var (theme, suffix) in new[] { ("light", "light"), ("dark", "dark") })
        {
            await page.EvaluateAsync(
                """
                theme => {
                    document.documentElement.setAttribute('data-theme', theme);
                    const shell = document.querySelector('.demo-shell');
                    if (shell) shell.setAttribute('data-theme', theme);
                }
                """, theme);
            await page.WaitForTimeoutAsync(250);

            // Open the image block's upload dialog.
            var block = page.Locator(".tm-notion-image-block").First;
            await block.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
            await block.Locator(".tm-notion-media-upload-zone--image").First.ClickAsync(new LocatorClickOptions { Force = true });
            var dialog = page.Locator(".tm-media-dialog").First;
            await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

            if (theme == "dark")
            {
                var bodyBg = await dialog.EvaluateAsync<string>(
                    "el => getComputedStyle(el).backgroundColor");
                TestContext.WriteLine($"dark dialog body background: {bodyBg}");
                Assert.AreNotEqual("rgb(255, 255, 255)", bodyBg, "the upload dialog body must not be white in dark mode (F-25)");
            }

            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = Path.Combine(dir, $"media-1440-{suffix}.png"),
                FullPage = false
            });
            await page.Locator(".tm-media-dialog-host .tm-modal-close").First
                .EvaluateAsync("el => el.click()");
            await page.WaitForTimeoutAsync(300);
        }

        foreach (var (width, height, suffix) in new[] { (1024, 768, "1024"), (390, 844, "390"), (320, 700, "320") })
        {
            await page.SetViewportSizeAsync(width, height);
            var block = page.Locator(".tm-notion-image-block").First;
            await block.Locator(".tm-notion-media-upload-zone--image").First.ClickAsync(new LocatorClickOptions { Force = true });
            var overlay = page.Locator(".tm-modal-overlay:has(.tm-media-dialog)").First;
            await overlay.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
            foreach (var theme in new[] { "light", "dark" })
            {
                await page.EvaluateAsync(
                    """
                    theme => {
                        document.documentElement.setAttribute('data-theme', theme);
                        const shell = document.querySelector('.demo-shell');
                        if (shell) shell.setAttribute('data-theme', theme);
                    }
                    """, theme);
                await page.WaitForTimeoutAsync(200);
                await page.ScreenshotAsync(new PageScreenshotOptions
                {
                    Path = Path.Combine(dir, $"media-{suffix}-{theme}.png"),
                    FullPage = false
                });
            }
            await page.Locator(".tm-media-dialog-host .tm-modal-close").First
                .EvaluateAsync("el => el.click()");
            await page.WaitForTimeoutAsync(300);
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TempoBlazor.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("TempoBlazor.slnx");
    }
}
