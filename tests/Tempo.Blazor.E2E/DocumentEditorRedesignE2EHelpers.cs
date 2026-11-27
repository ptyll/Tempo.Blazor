using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>Page-level helpers shared by the DocumentEditor redesign baseline generator and its behaviour specs.</summary>
internal static class DocumentEditorRedesignE2EHelpers
{
    public const string HistoryDocumentId = "phase-12-canvas-history-save";
    public const string RevisionsDocumentId = "phase-17-canvas-comments-revisions";

    public static readonly (string Name, int Width, int Height, bool Touch)[] SurfaceViewports =
    [
        ("1440x900", 1440, 900, false),
        ("1024x768-touch", 1024, 768, true),
        ("390x844", 390, 844, true),
        ("320x640", 320, 640, true),
    ];

    public static readonly (string Name, int Width, int Height, bool Touch)[] PanelViewports =
    [
        ("1440", 1440, 900, false),
        ("390", 390, 844, true),
    ];

    public static BrowserNewContextOptions ContextOptions(int width, int height, bool touch) => new()
    {
        HasTouch = touch,
        ViewportSize = new ViewportSize { Width = width, Height = height },
        Locale = "en-US",
        IgnoreHTTPSErrors = true,
    };

    public static string OpenUrl(string baseUrl, string documentId, string route)
        => route == "/canvas-engine-host"
            ? $"{baseUrl}{route}?documentId={documentId}&showToolbar=true&preferLocalDraft=false"
            : $"{baseUrl}{route}";

    /// <summary>
    /// The View tab's debug commands sit behind a sibling button that intercepts the pointer before the redesign
    /// (Playwright reports "open versions subtree intercepts pointer events", also on a desktop width), so ribbon
    /// commands are activated programmatically; the overlap itself is part of the "before" state.
    /// </summary>
    public static async Task ClickRibbonAsync(IPage page, string testId)
    {
        await page.GetByTestId(testId).DispatchEventAsync("click");
        await page.WaitForTimeoutAsync(200);
    }

    public static async Task ApplyDarkThemeAsync(IPage page)
    {
        await page.EvaluateAsync(
            """
            () => {
                // The demo shell wraps the page in its own [data-theme] element that re-declares the light
                // tokens, so every themed ancestor flips, not only <html>.
                const nodes = new Set([document.documentElement, document.body, ...document.querySelectorAll('[data-theme]')]);
                for (const node of nodes) {
                    node.setAttribute('data-theme', 'dark');
                    node.classList.add('tm-dark', 'dark');
                }
            }
            """);
        await page.WaitForTimeoutAsync(200);
    }

    public static async Task<string> BackgroundOfAsync(ILocator locator)
        => await locator.EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");

    /// <summary>
    /// A modal surface sits on a non-transparent scrim covering the viewport and is centered in it; the surface
    /// itself stays inside the viewport (no clipped dialog on a 390 px phone). <paramref name="surface"/> is the
    /// card that must be centered, <paramref name="scrim"/> the element that dims the page.
    /// </summary>
    public static async Task AssertModalAsync(ILocator surface, ILocator scrim, string name, int viewportWidth, int viewportHeight)
    {
        var box = await surface.BoundingBoxAsync();
        Assert.IsNotNull(box, $"{name} must be measurable");

        Assert.AreEqual(1, await scrim.CountAsync(), $"{name} must sit on a scrim");
        var scrimBackground = await BackgroundOfAsync(scrim);
        Assert.AreNotEqual("rgba(0, 0, 0, 0)", scrimBackground, $"{name} scrim must dim the page");
        var scrimBox = await scrim.BoundingBoxAsync();
        Assert.IsNotNull(scrimBox);
        Assert.IsTrue(scrimBox.Width >= viewportWidth - 1 && scrimBox.Height >= viewportHeight - 1, $"{name} scrim must cover the viewport");

        Assert.IsTrue(box.X >= -0.5 && box.Y >= -0.5, $"{name} must not start outside the viewport (x={box.X}, y={box.Y})");
        Assert.IsTrue(box.X + box.Width <= viewportWidth + 0.5, $"{name} must fit the viewport width");
        Assert.AreEqual(viewportWidth / 2.0, box.X + (box.Width / 2), 2.0, $"{name} must be horizontally centered");
        if (box.Height < viewportHeight - 8)
        {
            Assert.AreEqual(viewportHeight / 2.0, box.Y + (box.Height / 2), 4.0, $"{name} must be vertically centered");
        }
    }

    public static string FindRepositoryRootDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx.");
    }
}
