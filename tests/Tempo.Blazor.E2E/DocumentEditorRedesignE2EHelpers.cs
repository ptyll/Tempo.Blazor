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
    /// A REAL click on a ribbon command (redesign 146c7e1b, phase 4: the old pointer-intercepting siblings are gone, so the
    /// DispatchEvent workaround is retired). A command on the bar is clicked in place; a command that lives only in the core
    /// "More" menu (the OverflowOnly debug tools and the less frequent character formatting, or anything that did not fit
    /// the width) is chosen from that menu, which is opened first.
    /// </summary>
    public static async Task ClickRibbonAsync(IPage page, string testId)
    {
        var onBar = page.Locator($"button[data-testid='{testId}']:visible");
        if (await onBar.CountAsync() > 0)
        {
            await onBar.First.ClickAsync();
        }
        else
        {
            await OpenMoreAsync(page);
            await page.Locator($"[role='menu'] [data-testid='{testId}'], [role='menu'] [data-menu-testid='{testId}']").First.ClickAsync();
        }

        await page.WaitForTimeoutAsync(200);
    }

    /// <summary>Opens the ribbon's core "More" menu (a no-op when it is already open).</summary>
    public static async Task OpenMoreAsync(IPage page)
    {
        if (await page.Locator("[role='menu']").CountAsync() == 0)
        {
            await page.Locator("[data-testid='document-ribbon-toolbar'] .tm-toolbar-more").ClickAsync();
        }

        await Assertions.Expect(page.Locator("[role='menu']").First).ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    /// <summary>
    /// Picks one entry of an option list that lives in "More" (change case, line / paragraph spacing, ...): the list is a labelled
    /// <c>role="group"</c> section and the option is a menu item with the given text.
    /// </summary>
    public static async Task ChooseMoreOptionAsync(IPage page, string groupLabel, string optionText)
    {
        await OpenMoreAsync(page);
        var entry = page.Locator($"[role='menu'] [role='group'][aria-label='{groupLabel}'] [role^='menuitem']")
            .Filter(new() { HasTextRegex = new System.Text.RegularExpressions.Regex($"^\\s*{System.Text.RegularExpressions.Regex.Escape(optionText)}\\s*$") });
        await entry.First.ClickAsync();
        await page.WaitForTimeoutAsync(200);
    }

    /// <summary>
    /// Chooses a value of a ribbon select: the native select when the row has room for it, otherwise the same option from its
    /// check-marked section in the core More menu (a select that did not fit collapses there).
    /// </summary>
    public static async Task SelectRibbonOptionAsync(IPage page, string selectTestId, string value, string groupLabel, string optionText)
    {
        var select = page.Locator($"select[data-testid='{selectTestId}']:visible");
        if (await select.CountAsync() > 0)
        {
            await select.First.SelectOptionAsync(value);
            return;
        }

        await ChooseMoreOptionAsync(page, groupLabel, optionText);
    }

    /// <summary>
    /// Chooses a paragraph alignment (<c>left</c> / <c>center</c> / <c>right</c> / <c>justify</c>): the alignment item is ONE trigger
    /// with a menu of four entries on the bar, or the "Alignment" section of More when it did not fit.
    /// </summary>
    public static async Task ChooseAlignmentAsync(IPage page, string alignment)
    {
        var trigger = page.Locator("[data-testid='document-align'] button:visible");
        if (await trigger.CountAsync() > 0)
        {
            await trigger.First.ClickAsync();
            await page.Locator($"[role='menu'] [data-testid='document-align-{alignment}']").ClickAsync();
            await page.WaitForTimeoutAsync(200);
            return;
        }

        await ChooseMoreOptionAsync(page, "Alignment", char.ToUpperInvariant(alignment[0]) + alignment[1..]);
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

    /// <summary>
    /// Forces light/dark and the default/indigo brand the way a host does: every themed ancestor flips (the demo
    /// shell wraps the page in its own [data-theme] element) and the opt-in indigo brand rides
    /// <c>data-tm-theme</c> on the root element.
    /// </summary>
    public static async Task ApplyThemeAsync(IPage page, bool dark, bool indigo)
    {
        await page.EvaluateAsync(
            """
            ([dark, indigo]) => {
                const nodes = new Set([document.documentElement, document.body, ...document.querySelectorAll('[data-theme]')]);
                for (const node of nodes) {
                    node.setAttribute('data-theme', dark ? 'dark' : 'light');
                    node.classList.toggle('tm-dark', dark);
                    node.classList.toggle('dark', dark);
                }
                if (indigo) document.documentElement.setAttribute('data-tm-theme', 'indigo');
                else document.documentElement.removeAttribute('data-tm-theme');
            }
            """,
            new object[] { dark, indigo });
        await page.WaitForTimeoutAsync(500); // colour transitions (~120 ms) must have settled before a computed style is read
    }

    /// <summary>The four light/dark x default/indigo combinations of the redesign screenshot matrix.</summary>
    public static readonly (bool Dark, bool Indigo, string Name)[] ThemeMatrix =
    [
        (false, false, "light-default"),
        (true, false, "dark-default"),
        (false, true, "light-indigo"),
        (true, true, "dark-indigo"),
    ];

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
