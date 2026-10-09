using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Carry-forward from F3 (UX round 4): the /gallery demo now seeds local demo images, so
/// TmLightbox can be exercised end-to-end — open, navigate, close — including its promoted
/// browser-top-layer root. The images are local SVGs (no internet access needed).
/// </summary>
[TestClass]
public class GalleryLightboxE2ETests : WasmTestBase
{
    private string ShotDir
        => Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "gallery-lightbox");

    [TestInitialize]
    public void EnsureShotDir() => Directory.CreateDirectory(ShotDir);

    [TestMethod]
    public async Task Gallery_Lightbox_OpenNavigateClose_PromotedRoot()
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
            HasTouch = true,
            IsMobile = true,
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        var page = await context.NewPageAsync();
        RegisterContext(context);
        await page.GotoAsync($"{BaseUrl}/gallery");
        await WaitForAppReadyAsync(page);

        // The seeded local images render as gallery items (no empty provider set).
        var items = page.Locator(".tm-gallery-item");
        await items.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        Assert.AreEqual(8, await items.CountAsync(), "the gallery seeds 8 local demo images");

        // First image loads from the local asset (not an external placeholder service).
        var thumbSrc = await items.First.Locator("img").GetAttributeAsync("src");
        StringAssert.Contains(thumbSrc, "_content/Tempo.Blazor.Demo.SharedUI/gallery/photo-1.svg");

        // Open the lightbox.
        await items.First.ClickAsync();
        var lightbox = page.Locator(".tm-lightbox");
        await lightbox.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // The lightbox root is promoted to the browser top layer (popover=manual).
        var promoted = await page.EvaluateAsync<bool>(
            """
            () => {
                const root = document.querySelector('.tm-lightbox');
                return !!root && root.getAttribute('popover') === 'manual' && root.matches(':popover-open');
            }
            """);
        Assert.IsTrue(promoted, "the TmLightbox focus-scope root must be promoted to the top layer while open");

        // The full image comes through the demo API ticket stream (local content).
        var lightboxSrc = await lightbox.Locator(".tm-lightbox-img").GetAttributeAsync("src");
        StringAssert.Contains(lightboxSrc, "/api/images/stream/");
        var counter = await lightbox.Locator(".tm-lightbox-counter").InnerTextAsync();
        StringAssert.Contains(counter, "1");
        StringAssert.Contains(counter, "8");

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(ShotDir, "390-lightbox-open.png"),
            FullPage = false,
        });

        // Navigate: next moves the counter, prev moves it back.
        await lightbox.Locator(".tm-lightbox-next").ClickAsync();
        await Assertions.Expect(lightbox.Locator(".tm-lightbox-counter")).ToContainTextAsync("2");
        await lightbox.Locator(".tm-lightbox-prev").ClickAsync();
        await Assertions.Expect(lightbox.Locator(".tm-lightbox-counter")).ToContainTextAsync("1");

        // Escape closes and the lightbox leaves the top layer.
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(lightbox).ToBeHiddenAsync();
        var stillPromoted = await page.EvaluateAsync<bool>(
            "() => !!document.querySelector('.tm-lightbox:popover-open')");
        Assert.IsFalse(stillPromoted, "a closed lightbox must not hold a top-layer slot");

        // Focus must not remain inside the closed lightbox. (Restoring to the exact gallery
        // item is a pre-existing gap: TmImageGallery items are mouse-only divs, tracked
        // separately from this carry-forward.)
        var focusInsideLightbox = await page.EvaluateAsync<bool>(
            "() => !!document.activeElement?.closest?.('.tm-lightbox')");
        Assert.IsFalse(focusInsideLightbox, "focus must not stay inside the closed lightbox");
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Repository root was not found.");
    }
}
