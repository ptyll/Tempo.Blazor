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

        // X8 (F5 review): the seeded SVGs carry explicit width/height, so the image paints at a
        // real size — a viewBox-only SVG collapses the <img> to 0×0 inside the flex wrap.
        var img = lightbox.Locator(".tm-lightbox-img");
        var imgBox = await img.BoundingBoxAsync();
        Assert.IsNotNull(imgBox, "the lightbox image must render a box");
        Assert.IsTrue(imgBox!.Width >= 200, $"the lightbox image must be at least 200px wide, was {imgBox.Width}");
        var naturalWidth = await img.EvaluateAsync<int>("el => el.naturalWidth");
        Assert.IsTrue(naturalWidth > 0, "the lightbox image must have decoded (naturalWidth > 0)");
        await Assertions.Expect(img).ToBeVisibleAsync();

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

        // Focus returns to the thumbnail of the image on screen (the opener here — the sequence
        // ends back on image 1). TmImageGallery items are native buttons carrying per-instance
        // ids; the lightbox restore target tracks the image being shown. Poll for the id: the
        // restore interop can land after the DOM removal that satisfies the hidden wait.
        var expectedFocusId = await items.First.GetAttributeAsync("id");
        var focused = await page.WaitForFunctionAsync(
            "id => document.activeElement && document.activeElement.id === id",
            expectedFocusId);
        Assert.IsNotNull(focused, "Escape must restore focus to the thumbnail of the displayed image");
    }

    [TestMethod]
    public async Task Gallery_Lightbox_1440_ImagePaints_AtDesktopWidth()
    {
        // X8: re-captured at 1440 — the first committed shot was an empty lightbox because the
        // seeded SVGs had no width/height.
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/gallery");
        await WaitForAppReadyAsync(page);

        var items = page.Locator(".tm-gallery-item");
        await items.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await items.First.ClickAsync();

        var lightbox = page.Locator(".tm-lightbox");
        await lightbox.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var img = lightbox.Locator(".tm-lightbox-img");
        var imgBox = await img.BoundingBoxAsync();
        Assert.IsNotNull(imgBox, "the lightbox image must render a box");
        Assert.IsTrue(imgBox!.Width >= 200, $"the lightbox image must be at least 200px wide, was {imgBox.Width}");
        var naturalWidth = await img.EvaluateAsync<int>("el => el.naturalWidth");
        Assert.IsTrue(naturalWidth > 0, "the lightbox image must have decoded (naturalWidth > 0)");
        await Assertions.Expect(img).ToBeVisibleAsync();

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(ShotDir, "1440-lightbox-open.png"),
            FullPage = false,
        });

        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(lightbox).ToBeHiddenAsync();
    }

    [TestMethod]
    public async Task Gallery_Keyboard_EnterOpens_ArrowNavigates_EscapeRestoresToCurrentThumbnail()
    {
        // The keyboard story end to end (cluster-D §3): a native button thumbnail opens the
        // lightbox with Enter, arrows browse, and Escape returns focus to the thumbnail of the
        // image that was on screen — not the one that opened it.
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/gallery");
        await WaitForAppReadyAsync(page);

        var items = page.Locator(".tm-gallery-item");
        await items.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await items.Nth(0).FocusAsync();
        await page.Keyboard.PressAsync("Enter");

        var lightbox = page.Locator(".tm-lightbox");
        await lightbox.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await Assertions.Expect(lightbox.Locator(".tm-lightbox-counter")).ToContainTextAsync("1");

        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(lightbox.Locator(".tm-lightbox-counter")).ToContainTextAsync("2");

        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(lightbox).ToBeHiddenAsync();

        var expectedFocusId = await items.Nth(1).GetAttributeAsync("id");
        await Assertions.Expect(page.Locator(".tm-gallery-grid")).ToBeVisibleAsync();
        var focusedId = await page.WaitForFunctionAsync(
            "id => document.activeElement && document.activeElement.id === id",
            expectedFocusId);
        Assert.IsNotNull(focusedId, "Escape must restore focus to the thumbnail of image 2");
    }

    [DataTestMethod]
    [DataRow(390, 844)]
    [DataRow(320, 700)]
    public async Task Gallery_Lightbox_NavButtons_StayInsideTheViewport(int width, int height)
    {
        // d49c9776: below 640px the arrows lay over the image edges INSIDE the viewport (they used
        // to overflow past the right edge) and keep the 44px touch target.
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = true,
            IsMobile = true,
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        var page = await context.NewPageAsync();
        RegisterContext(context);
        await page.GotoAsync($"{BaseUrl}/gallery");
        await WaitForAppReadyAsync(page);

        var items = page.Locator(".tm-gallery-item");
        await items.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await items.First.ClickAsync();

        var lightbox = page.Locator(".tm-lightbox");
        await lightbox.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var prev = await lightbox.Locator(".tm-lightbox-prev").BoundingBoxAsync();
        var next = await lightbox.Locator(".tm-lightbox-next").BoundingBoxAsync();
        Assert.IsNotNull(prev, "the prev arrow must render a box");
        Assert.IsNotNull(next, "the next arrow must render a box");
        Assert.IsTrue(prev.X >= 0, $"the prev arrow must start inside the viewport, X={prev.X}");
        Assert.IsTrue(next.X + next.Width <= width,
            $"the next arrow must end inside the {width}px viewport, right edge={next.X + next.Width}");
        Assert.IsTrue(next.Height >= 44, $"the arrows must stay 44px touch targets, was {next.Height}");

        // The overlaid arrows stay clickable.
        await lightbox.Locator(".tm-lightbox-next").ClickAsync();
        await Assertions.Expect(lightbox.Locator(".tm-lightbox-counter")).ToContainTextAsync("2");
    }

    [TestMethod]
    public async Task Gallery_Lightbox_1440_ArrowsSitBesideTheImage()
    {
        // Desktop is unchanged: from 640px up the arrows sit beside the image, not over it.
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 900);
        await page.GotoAsync($"{BaseUrl}/gallery");
        await WaitForAppReadyAsync(page);

        var items = page.Locator(".tm-gallery-item");
        await items.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await items.First.ClickAsync();

        var lightbox = page.Locator(".tm-lightbox");
        await lightbox.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var img = await lightbox.Locator(".tm-lightbox-img").BoundingBoxAsync();
        var next = await lightbox.Locator(".tm-lightbox-next").BoundingBoxAsync();
        Assert.IsNotNull(img, "the image must render a box");
        Assert.IsNotNull(next, "the next arrow must render a box");
        Assert.IsTrue(next.X >= img.X + img.Width,
            $"the next arrow must sit to the right of the image, next.X={next.X}, img right={img.X + img.Width}");
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
