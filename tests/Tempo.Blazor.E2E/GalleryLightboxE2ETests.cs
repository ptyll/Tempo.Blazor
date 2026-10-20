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

    [TestMethod]
    public async Task Gallery_Lightbox_390_Rtl_MirroredArrows_SwappedArrowKeys()
    {
        // F25a: in RTL the strip mirrors — the next image lies to the LEFT: the logical insets
        // already place prev at the right edge, the chevron glyphs flip, and the physical arrow
        // keys swap (ArrowLeft is next, ArrowRight is previous).
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
            HasTouch = true,
            IsMobile = true,
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        await AddRtlInitScriptAsync(context);
        var page = await context.NewPageAsync();
        RegisterContext(context);
        await page.GotoAsync($"{BaseUrl}/gallery");
        await WaitForAppReadyAsync(page);

        // Precondition, asserted: the page itself renders rtl (if the init script ever stops
        // landing, this fails with a clear message instead of the lightbox sanity check below).
        Assert.AreEqual("rtl", await page.EvaluateAsync<string>(
                "() => document.documentElement.getAttribute('dir')"),
            "the test page must render rtl for the lightbox to inherit it");

        var items = page.Locator(".tm-gallery-item");
        await items.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await items.First.ClickAsync();

        var lightbox = page.Locator(".tm-lightbox");
        await lightbox.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Sanity: the strip really renders in RTL.
        Assert.AreEqual("rtl", await lightbox.EvaluateAsync<string>("el => getComputedStyle(el).direction"),
            "the lightbox inherits the page's rtl direction");

        // Placement mirrors through the logical insets: prev sits at the RIGHT edge, next at the left.
        var prevBox = await lightbox.Locator(".tm-lightbox-prev").BoundingBoxAsync();
        var nextBox = await lightbox.Locator(".tm-lightbox-next").BoundingBoxAsync();
        Assert.IsNotNull(prevBox);
        Assert.IsNotNull(nextBox);
        Assert.IsTrue(prevBox!.X > nextBox!.X,
            $"in RTL prev sits right of next (prev.X={prevBox.X}, next.X={nextBox.X})");

        // The chevron glyphs flip so they keep pointing where the action goes.
        var mirrored = await lightbox.Locator(".tm-lightbox-prev svg").EvaluateAsync<string>(
            "el => getComputedStyle(el).transform");
        StringAssert.Contains(mirrored, "-1", "the prev chevron is mirrored (scaleX(-1))");

        // The arrow keys follow the reading direction: ArrowLeft is NEXT in RTL.
        await page.Keyboard.PressAsync("ArrowLeft");
        await Assertions.Expect(lightbox.Locator(".tm-lightbox-counter")).ToContainTextAsync("2");
        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(lightbox.Locator(".tm-lightbox-counter")).ToContainTextAsync("1");

        // The mirrored next arrow (left edge) still works by pointer.
        await lightbox.Locator(".tm-lightbox-next").ClickAsync();
        await Assertions.Expect(lightbox.Locator(".tm-lightbox-counter")).ToContainTextAsync("2");
    }

    [TestMethod]
    public async Task Gallery_Lightbox_390_Panorama_NextArrowStaysOutOfTheChromeBand()
    {
        // F25a: a panorama's row is shorter than the 44px arrows; centred on the row, the arrow
        // tops reached up into the top-end chrome band (-3rem) where close and delete live, and
        // overlapped the delete button. The row now floors at one touch target, so the arrows
        // never leave the image band. The demo never enables CanDelete, so the overlap is proven
        // against the CLOSE button — it occupies the same band, at the same insets class.
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

        var items = page.Locator(".tm-gallery-item");
        await items.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await items.First.ClickAsync();

        var lightbox = page.Locator(".tm-lightbox");
        await lightbox.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Swap the displayed image for a panorama (a 12:1 SVG data URL): the content row collapses
        // to the image height unless the min-height floor holds.
        const string panorama = "data:image/svg+xml;charset=utf-8,%3Csvg%20xmlns%3D%22http%3A%2F%2Fwww.w3.org%2F2000%2Fsvg%22%20width%3D%221200%22%20height%3D%22100%22%3E%3Crect%20width%3D%221200%22%20height%3D%22100%22%20fill%3D%22%23334%22%2F%3E%3C%2Fsvg%3E";
        await lightbox.Locator(".tm-lightbox-img").EvaluateAsync("(el, src) => { el.src = src; }", panorama);
        await page.WaitForTimeoutAsync(300);

        var content = lightbox.Locator(".tm-lightbox-content");
        var minHeight = await content.EvaluateAsync<double>(
            "el => parseFloat(getComputedStyle(el).minHeight)");
        Assert.IsTrue(minHeight >= 44,
            $"the panorama's content row must floor at the 44px touch target, was {minHeight}");

        // The next arrow never rises above the content row into the chrome band, and it does not
        // intersect the close button that lives there.
        var nextBox = await lightbox.Locator(".tm-lightbox-next").BoundingBoxAsync();
        var closeBox = await lightbox.Locator(".tm-lightbox-close").BoundingBoxAsync();
        var contentBox = await content.BoundingBoxAsync();
        Assert.IsNotNull(nextBox);
        Assert.IsNotNull(closeBox);
        Assert.IsNotNull(contentBox);
        Assert.IsTrue(nextBox!.Y >= contentBox!.Y - 0.5,
            $"the next arrow must stay inside the image band (next.Y={nextBox.Y}, content.Y={contentBox.Y})");
        var intersects = nextBox.X < closeBox!.X + closeBox.Width
            && nextBox.X + nextBox.Width > closeBox.X
            && nextBox.Y < closeBox.Y + closeBox.Height
            && nextBox.Y + nextBox.Height > closeBox.Y;
        Assert.IsFalse(intersects, "the next arrow must not overlap the top-end chrome (close/delete band)");

        // The arrows stay usable on the panorama row.
        await lightbox.Locator(".tm-lightbox-next").ClickAsync();
        await Assertions.Expect(lightbox.Locator(".tm-lightbox-counter")).ToContainTextAsync("2");
    }

    [DataTestMethod]
    [DataRow(false, DisplayName = "LTR: the next arrow stays clear of the delete")]
    [DataRow(true, DisplayName = "RTL: the prev arrow stays clear of the delete")]
    public async Task Gallery_Lightbox_390_DeleteMode_ArrowsStayClearOfTheDeleteButton(bool rtl)
    {
        // M2 (review): below 640px the delete button used to share the top-end chrome band with
        // the arrows' upper ends — on a short row they intersected. It now sits in the footer row
        // below the image. The demo's Delete Mode turns CanDelete on BEFORE the lightbox opens so
        // the button really renders, and the arrow sharing its inline corner (next in LTR, prev
        // in RTL — both sit at inset-inline-end, mirrored with the strip) must keep ≥ 8px of
        // clearance from it.
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
            HasTouch = true,
            IsMobile = true,
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        if (rtl)
        {
            await AddRtlInitScriptAsync(context);
        }
        var page = await context.NewPageAsync();
        RegisterContext(context);
        await page.GotoAsync($"{BaseUrl}/gallery");
        await WaitForAppReadyAsync(page);

        var deleteToggle = page.Locator("button").Filter(new() { HasText = "Delete Mode" }).First;
        await deleteToggle.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await deleteToggle.ClickAsync();

        var items = page.Locator(".tm-gallery-item");
        await items.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await items.First.ClickAsync();

        var lightbox = page.Locator(".tm-lightbox");
        await lightbox.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var deleteButton = lightbox.Locator(".tm-lightbox-delete");
        await Assertions.Expect(deleteButton).ToBeVisibleAsync();

        // Navigate to image 2 so the asserted arrow is enabled (the strip start disables one).
        await lightbox.Locator(".tm-lightbox-next").ClickAsync();
        await Assertions.Expect(lightbox.Locator(".tm-lightbox-counter")).ToContainTextAsync("2");

        var arrow = lightbox.Locator(rtl ? ".tm-lightbox-prev" : ".tm-lightbox-next");
        var arrowBox = await arrow.BoundingBoxAsync();
        var deleteBox = await deleteButton.BoundingBoxAsync();
        Assert.IsNotNull(arrowBox, "the asserted arrow renders a box");
        Assert.IsNotNull(deleteBox, "the delete button renders a box");
        var gap = GapBetween(arrowBox!, deleteBox!);
        Assert.IsTrue(gap >= 8,
            $"the {(rtl ? "prev" : "next")} arrow must keep ≥8px from the delete button, gap={gap:0.#}px " +
            $"(arrow={arrowBox!.X:0.#},{arrowBox.Y:0.#} {arrowBox.Width:0.#}x{arrowBox.Height:0.#}, " +
            $"delete={deleteBox!.X:0.#},{deleteBox.Y:0.#} {deleteBox.Width:0.#}x{deleteBox.Height:0.#})");
    }

    /// <summary>
    /// b32847d0 (presentation polish): the lightbox is captured and geometrically asserted at the
    /// phone (390×844) and desktop (1440×900) viewports, in BOTH themes, over a landscape (the
    /// seeded 4:3 demo SVG), a portrait (3:4) and a panorama (3:1) source. The invariants, in px:
    /// the contained image never leaves the viewport; the close button never covers the image
    /// (allowance: the 4px chrome offset a 44px touch target leaves above a -3rem band); below
    /// 640px the arrows may overlay the image EDGES only (protrusion ≤ touch target + gutter,
    /// the d49c9776 design) and are 44px targets; from 640px up they sit strictly beside the
    /// image; the counter pill is visible and inside the viewport; with Delete Mode on the
    /// footer delete clears the image band. One case runs dark WITH Delete Mode on.
    /// Screenshots land in tmp/lightbox-shots/ for the UX review.
    /// </summary>
    [DataTestMethod]
    [DataRow(390, 844, "landscape", false, false, DisplayName = "390 light landscape")]
    [DataRow(390, 844, "portrait", false, false, DisplayName = "390 light portrait")]
    [DataRow(390, 844, "panorama", false, false, DisplayName = "390 light panorama")]
    [DataRow(390, 844, "landscape", true, true, DisplayName = "390 dark landscape + Delete Mode")]
    [DataRow(390, 844, "portrait", true, false, DisplayName = "390 dark portrait")]
    [DataRow(390, 844, "panorama", true, false, DisplayName = "390 dark panorama")]
    [DataRow(1440, 900, "landscape", false, false, DisplayName = "1440 light landscape")]
    [DataRow(1440, 900, "portrait", false, false, DisplayName = "1440 light portrait")]
    [DataRow(1440, 900, "panorama", false, false, DisplayName = "1440 light panorama")]
    [DataRow(1440, 900, "landscape", true, false, DisplayName = "1440 dark landscape")]
    [DataRow(1440, 900, "portrait", true, false, DisplayName = "1440 dark portrait")]
    [DataRow(1440, 900, "panorama", true, false, DisplayName = "1440 dark panorama")]
    public async Task Gallery_Lightbox_Presentation_Matrix(
        int width, int height, string aspect, bool dark, bool deleteMode)
    {
        const double allowance = 0.5;

        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = width < 640,
            IsMobile = width < 640,
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });

        var page = await context.NewPageAsync();
        RegisterContext(context);
        await page.GotoAsync($"{BaseUrl}/gallery");
        await WaitForAppReadyAsync(page);

        if (dark)
        {
            await SetDarkThemeAsync(page);
        }

        if (deleteMode)
        {
            var deleteToggle = page.Locator("button").Filter(new() { HasText = "Delete Mode" }).First;
            await deleteToggle.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            await deleteToggle.ClickAsync();
        }

        var items = page.Locator(".tm-gallery-item");
        await items.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await items.First.ClickAsync();

        var lightbox = page.Locator(".tm-lightbox");
        await lightbox.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Portrait and panorama do not exist in the seed (all eight demo photos are 4:3
        // landscape); the src swap is the established pattern (the panorama regression test).
        var (naturalWidth, naturalHeight) = aspect switch
        {
            "portrait" => (900, 1200),
            "panorama" => (1200, 400),
            _ => (1200, 900),
        };
        if (aspect != "landscape")
        {
            var body = aspect == "portrait"
                ? SvgDataUrl(900, 1200, "#312e81", "#f59e0b")
                : SvgDataUrl(1200, 400, "#0f766e", "#f472b6");
            await lightbox.Locator(".tm-lightbox-img").EvaluateAsync("(el, src) => { el.src = src; }", body);
            await page.WaitForFunctionAsync(
                "w => { const el = document.querySelector('.tm-lightbox-img'); return !!el && el.naturalWidth === w; }",
                naturalWidth);
        }

        await page.WaitForTimeoutAsync(400);

        // ── Invariant 1: the contained image stays inside the viewport ──
        var imgBox = await lightbox.Locator(".tm-lightbox-img").BoundingBoxAsync();
        Assert.IsNotNull(imgBox, "the image renders a box");
        Assert.IsTrue(imgBox!.X >= -allowance && imgBox.Y >= -allowance,
            $"the image must not start outside the viewport ({aspect}, {width}x{height}): {imgBox.X:0.#},{imgBox.Y:0.#}");
        Assert.IsTrue(imgBox.X + imgBox.Width <= width + allowance && imgBox.Y + imgBox.Height <= height + allowance,
            $"the image must fit the viewport ({aspect}, {width}x{height}): right={imgBox.X + imgBox.Width:0.#}, bottom={imgBox.Y + imgBox.Height:0.#}");
        Assert.IsTrue(imgBox.Width >= 200, $"the image must use real width, was {imgBox.Width:0.#}");

        // ── Invariant 2: the close button never covers the image ──
        // The chrome band sits 3rem above the row; a touch-target-sized close leaves exactly 4px.
        var closeBox = await lightbox.Locator(".tm-lightbox-close").BoundingBoxAsync();
        Assert.IsNotNull(closeBox, "the close button renders a box");
        var closeGap = GapBetween(imgBox, closeBox!);
        Assert.IsTrue(closeGap >= 4 - allowance,
            $"the close button must stay clear of the image (allowance 4px), gap={closeGap:0.#} ({aspect}, {width})");

        // ── Invariant 3: the arrows — 44px targets on the phone overlaying only the image
        //    edges (≤ touch target + one gutter), strictly beside the image on desktop ──
        var prevBox = await lightbox.Locator(".tm-lightbox-prev").BoundingBoxAsync();
        var nextBox = await lightbox.Locator(".tm-lightbox-next").BoundingBoxAsync();
        Assert.IsNotNull(prevBox);
        Assert.IsNotNull(nextBox);
        if (width < 640)
        {
            Assert.IsTrue(prevBox!.Width >= 44 - allowance && prevBox.Height >= 44 - allowance,
                $"the prev arrow is a 44px touch target, was {prevBox.Width:0.#}x{prevBox.Height:0.#}");
            Assert.IsTrue(nextBox!.Width >= 44 - allowance && nextBox.Height >= 44 - allowance,
                $"the next arrow is a 44px touch target, was {nextBox.Width:0.#}x{nextBox.Height:0.#}");
            const float maxProtrusion = 44 + 8; // touch target + one --tm-space-2 gutter
            Assert.IsTrue(prevBox!.X + prevBox.Width <= imgBox.X + maxProtrusion + allowance,
                $"the prev arrow may only overlay the image edge (≤{maxProtrusion}px), protrudes to {prevBox.X + prevBox.Width - imgBox.X:0.#}");
            Assert.IsTrue(nextBox!.X >= imgBox.X + imgBox.Width - maxProtrusion - allowance,
                $"the next arrow may only overlay the image edge (≤{maxProtrusion}px), protrudes to {imgBox.X + imgBox.Width - nextBox.X:0.#}");
        }
        else
        {
            Assert.IsTrue(prevBox!.X + prevBox!.Width <= imgBox.X + allowance,
                $"from 640px up the prev arrow sits beside the image (prev right={prevBox.X + prevBox.Width:0.#}, image left={imgBox.X:0.#})");
            Assert.IsTrue(nextBox!.X >= imgBox.X + imgBox.Width - allowance,
                $"from 640px up the next arrow sits beside the image (next left={nextBox.X:0.#}, image right={imgBox.X + imgBox.Width:0.#})");
        }

        // ── Invariant 4: the counter pill is visible and inside the viewport ──
        var counter = lightbox.Locator(".tm-lightbox-counter");
        await Assertions.Expect(counter).ToBeVisibleAsync();
        var counterBox = await counter.BoundingBoxAsync();
        Assert.IsNotNull(counterBox, "the counter pill renders a box");
        Assert.IsTrue(counterBox!.X >= -allowance && counterBox.Y >= -allowance
            && counterBox.X + counterBox.Width <= width + allowance
            && counterBox.Y + counterBox.Height <= height + allowance,
            $"the counter pill must stay inside the viewport ({aspect}, {width}x{height}): {counterBox.X:0.#},{counterBox.Y:0.#} {counterBox.Width:0.#}x{counterBox.Height:0.#}");

        // ── Invariant 5 (Delete Mode case): the footer delete clears the image band ──
        if (deleteMode)
        {
            var deleteButton = lightbox.Locator(".tm-lightbox-delete");
            await Assertions.Expect(deleteButton).ToBeVisibleAsync();
            var deleteBox = await deleteButton.BoundingBoxAsync();
            Assert.IsNotNull(deleteBox, "the delete button renders a box");
            var deleteGap = GapBetween(imgBox, deleteBox!);
            Assert.IsTrue(deleteGap >= 4 - allowance,
                $"the footer delete must stay clear of the image band (allowance 4px), gap={deleteGap:0.#}");
        }

        // ── The capture for the UX review ──
        var shotDir = Path.Combine(FindRepoRoot(), "tmp", "lightbox-shots");
        Directory.CreateDirectory(shotDir);
        var shotPath = Path.Combine(shotDir,
            $"{width}-{(dark ? "dark" : "light")}-{aspect}{(deleteMode ? "-delete" : string.Empty)}.png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = shotPath, FullPage = false });
        Assert.IsTrue(File.Exists(shotPath), $"the screenshot must exist on disk: {shotPath}");
        TestContext.WriteLine($"shot: {shotPath}");

        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(lightbox).ToBeHiddenAsync();
    }

    /// <summary>Flips the demo to the dark theme the way the app actually paints it: the theme
    /// lives on the layout wrapper (<c>[data-theme]</c>), so both that element and
    /// <c>&lt;html&gt;</c> are switched, then the switch is guarded against a computed token that
    /// only dark declares — an attribute with no CSS behind it would "pass" and shoot light.</summary>
    private static async Task SetDarkThemeAsync(IPage page)
    {
        await page.EvaluateAsync(
            """
            () => {
                document.documentElement.setAttribute('data-theme', 'dark');
                document.querySelectorAll('[data-theme]').forEach(el => el.setAttribute('data-theme', 'dark'));
            }
            """);
        await page.Locator("[data-theme='dark']").First.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        var overlay = await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('[data-theme]')).getPropertyValue('--tm-bg-overlay').trim()");
        StringAssert.Contains(overlay, "0.7", "the dark theme must be live where the lightbox inherits it");
        await page.WaitForTimeoutAsync(400);
    }

    /// <summary>A local demo image with explicit dimensions (the X8 lesson: dimension-less SVGs
    /// collapse the lightbox <c>&lt;img&gt;</c>), painted with shapes so the capture shows
    /// content the layout can be judged against.</summary>
    private static string SvgDataUrl(int width, int height, string fill, string accent) =>
        "data:image/svg+xml;charset=utf-8," + Uri.EscapeDataString(
            $"<svg xmlns='http://www.w3.org/2000/svg' width='{width}' height='{height}' viewBox='0 0 {width} {height}'>"
            + $"<rect width='{width}' height='{height}' fill='{fill}'/>"
            + $"<circle cx='{(int)(width * 0.32)}' cy='{(int)(height * 0.34)}' r='{(int)(Math.Min(width, height) * 0.2)}' fill='{accent}'/>"
            + $"<rect x='{(int)(width * 0.55)}' y='{(int)(height * 0.58)}' width='{(int)(width * 0.3)}' height='{(int)(height * 0.24)}' fill='#ffffff' opacity='0.85'/>"
            + "</svg>");

    /// <summary>dir=rtl on <c>&lt;html&gt;</c> — the lightbox inherits the page direction. The
    /// init script runs as a PLAIN SCRIPT BODY (this repo's convention — Playwright evaluates the
    /// string, it does not call a function value, so an arrow expression would be a silent no-op;
    /// that is exactly how the original "Expected rtl, actual ltr" failure happened). At document
    /// creation <c>documentElement</c> does not exist yet, so the attribute lands as soon as
    /// <c>&lt;html&gt;</c> is parsed.</summary>
    private static async Task AddRtlInitScriptAsync(IBrowserContext context)
    {
        await context.AddInitScriptAsync(
            """
            (() => {
                const apply = () => document.documentElement.setAttribute('dir', 'rtl');
                if (document.documentElement) {
                    apply();
                    return;
                }
                document.addEventListener('DOMContentLoaded', apply, { once: true });
            })();
            """);
    }

    /// <summary>The signed gap between two boxes: positive = the axis separation in px, negative
    /// or zero = the boxes intersect (or touch).</summary>
    private static double GapBetween(LocatorBoundingBoxResult a, LocatorBoundingBoxResult b)
    {
        var dx = Math.Max(b.X - (a.X + a.Width), a.X - (b.X + b.Width));
        var dy = Math.Max(b.Y - (a.Y + a.Height), a.Y - (b.Y + b.Height));
        return Math.Max(dx, dy);
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
