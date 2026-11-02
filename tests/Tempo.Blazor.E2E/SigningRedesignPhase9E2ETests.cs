using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Signing redesign phase 9 — mobile designer, viewer, comments and the runner sheet on the shared
/// <c>TmDrawer</c>. Behaviour tests are always on; captures run only when <c>TM_WRITE_BASELINES</c>
/// is set:
/// <code>
/// TM_WRITE_BASELINES=1 dotnet test tests/Tempo.Blazor.E2E --filter "SigningRedesignPhase9E2ETests"
/// </code>
/// </summary>
internal static class SigningRedesignPhase9Specs
{
    public const string SigningComponentsUrl = "/signing-components";

    public const string CaptureChromeStyle =
        ".sticky.top-0, .fixed.top-0 { visibility: hidden !important; }";

    public const string CaptureSurfaceStyle =
        "body, main, .max-w-7xl, [data-testid] { background: var(--tm-bg-page) !important; color: var(--tm-text-primary); }";

    /// <summary>Hide the demo sidebar so a 1024 viewport is the component's own width.</summary>
    public const string WideLayoutStyle =
        " [data-demo-shell='sidebar'] { display: none !important; }" +
        " [data-demo-shell='main'] { margin-left: 0 !important; width: 100vw !important; max-width: 100vw !important; }" +
        " [data-demo-shell='page-body'], .max-w-7xl, .demo-page, .demo-section, .showcase-card { max-width: 100vw !important; }";

    public static string Phase9Dir
    {
        get
        {
            var dir = Path.Combine(FindRepoRoot().FullName,
                "tests", "Tempo.Blazor.E2E", "__screenshots__", "signing-redesign", "phase-9");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static async Task<IPage> OpenAsync(
        IBrowser browser,
        string baseUrl,
        int width,
        int height,
        bool touch,
        Action<IBrowserContext> registerContext,
        Func<IPage, Task> waitForReady,
        string? query = null)
    {
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
            HasTouch = touch,
            IsMobile = touch && width < 640
        });
        registerContext(context);
        var page = await context.NewPageAsync();
        await page.AddInitScriptAsync(
            "() => { localStorage.setItem('tm-demo-culture', 'en'); " +
            "const style = document.createElement('style'); style.textContent = " +
            System.Text.Json.JsonSerializer.Serialize(width <= 1024 ? WideLayoutStyle : string.Empty) +
            "; document.documentElement.appendChild(style); }");
        var url = string.IsNullOrEmpty(query)
            ? $"{baseUrl}{SigningComponentsUrl}"
            : $"{baseUrl}{SigningComponentsUrl}?{query}";
        await page.GotoAsync(url, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60_000
        });
        await waitForReady(page);
        return page;
    }

    public static ILocator Designer(IPage page) => page.Locator("[data-testid='pdf-template-designer']").First;

    public static ILocator Viewer(IPage page) => page.Locator("[data-testid='signing-document-viewer']").First;

    public static ILocator Comments(IPage page) => page.Locator("[data-testid='document-comments-viewer']").First;

    public static ILocator Runner(IPage page) => page.Locator("[data-testid='signing-runner-demo']").First;

    public static async Task CaptureAsync(IPage page, ILocator target, string name, List<string> written)
    {
        await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = CaptureChromeStyle });
        await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = CaptureSurfaceStyle });
        await target.EvaluateAsync("el => el.scrollIntoView({ block: 'start', inline: 'nearest' })");
        if (name.StartsWith("runner-peek--", StringComparison.Ordinal))
        {
            await target.EvaluateAsync(
                """
                el => {
                    el.scrollIntoView({ block: 'start', inline: 'nearest' });
                    const top = el.getBoundingClientRect().top;
                    window.scrollBy({ top: top - 96, behavior: 'instant' });
                }
                """);
        }

        await page.WaitForTimeoutAsync(200);
        var path = Path.Combine(Phase9Dir, name);
        // Runner peek / list / full paint outside the runner box (the drawer is position:fixed /
        // absolute against the documents frame). Those names are VIEWPORT captures, same contract
        // as phase-5 CaptureViewportAsync. Every other subject stays an element shot.
        if (name.StartsWith("runner-peek--", StringComparison.Ordinal)
            || name.StartsWith("runner-list--", StringComparison.Ordinal)
            || name.StartsWith("runner-full--", StringComparison.Ordinal))
        {
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, Type = ScreenshotType.Png });
        }
        else
        {
            await target.ScreenshotAsync(new LocatorScreenshotOptions { Path = path, Type = ScreenshotType.Png });
        }

        Console.WriteLine($"[signing-phase-9] wrote {path}");
        written.Add(path);
    }

    /// <summary>
    /// Comments mobile: raise the sheet so the panel is tall enough to show the sticky composer,
    /// then capture the drawer panel itself (the viewer box clips the sheet).
    /// </summary>
    public static async Task CaptureCommentsSheetAsync(IPage page, ILocator comments, string name, List<string> written, bool expand = false)
    {
        var sheet = comments.Locator(".tm-document-page-viewer__comments-sheet").First;
        await comments.EvaluateAsync("el => el.scrollIntoView({ block: 'center', inline: 'nearest' })");
        await page.WaitForTimeoutAsync(100);
        if (expand)
        {
            var handle = sheet.Locator(".tm-sheet__handle").First;
            if (await handle.CountAsync() > 0)
            {
                await handle.FocusAsync();
                await page.Keyboard.PressAsync("End");
                await page.WaitForTimeoutAsync(150);
            }
        }

        var panel = sheet.Locator(".tm-drawer__panel").First;
        if (await panel.CountAsync() == 0)
        {
            await CaptureAsync(page, comments, name, written);
            return;
        }

        await panel.EvaluateAsync("""
            el => {
                const targets = [...el.querySelectorAll('.tm-comment-composer, .tm-document-page-viewer__draft-panel, .tm-comment-composer__field, .tm-textarea')];
                const box = el.getBoundingClientRect();
                const overflow = targets.reduce((max, node) => {
                    const rect = node.getBoundingClientRect();
                    const hidden = Math.max(0, node.scrollHeight - node.clientHeight);
                    return Math.max(max, rect.bottom - box.bottom, hidden);
                }, 0);
                const height = Math.max(box.height + overflow + 24, 520);
                el.style.setProperty('height', Math.ceil(height) + 'px', 'important');
                el.style.setProperty('max-height', 'none', 'important');
                el.style.overflow = 'visible';
            }
            """);
        await CaptureAsync(page, panel, name, written);
    }

    /// <summary>Peek the sheet so a later tap can reach the page behind a raised panel.</summary>
    public static async Task CollapseCommentsSheetAsync(IPage page, ILocator comments)
    {
        var handle = comments.Locator(".tm-document-page-viewer__comments-sheet .tm-sheet__handle").First;
        if (await handle.CountAsync() == 0)
        {
            return;
        }

        await handle.FocusAsync();
        await page.Keyboard.PressAsync("Home");
        await page.WaitForTimeoutAsync(150);
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
}

/// <summary>
/// Writes the phase-9 captures: designer, viewer, comments and the runner drawer at 390/320
/// (light/dark × default/indigo) and the designer at 1024 with touch.
/// </summary>
[TestClass]
public class SigningRedesignPhase9E2ETests : BaselineGeneratorTestBase
{
    [TestMethod]
    [Description("Writes the phase-9 mobile designer, viewer, comments and runner-drawer captures")]
    public async Task CaptureSigningRedesignPhase9Screens()
    {
        var written = new List<string>();
        var themes = new[]
        {
            (false, false, "light", "default"),
            (true, false, "dark", "default"),
            (false, true, "light", "indigo"),
            (true, true, "dark", "indigo")
        };

        foreach (var width in new[] { 390, 320 })
        {
            foreach (var (dark, indigo, theme, brand) in themes)
            {
                var page = await SigningRedesignPhase9Specs.OpenAsync(
                    Browser, BaseUrl, width, 844, touch: true, RegisterContext, WaitForAppReadyAsync);
                await SigningRedesignPhase5Specs.ApplyThemeAsync(page, dark, indigo);

                var designer = SigningRedesignPhase9Specs.Designer(page);
                await SigningRedesignPhase9Specs.CaptureAsync(
                    page, designer, $"designer--{width}--{theme}--{brand}.png", written);
                var field = designer.Locator("[data-field-uuid='designer-name']");
                await field.TapAsync();
                await SigningRedesignPhase9Specs.CaptureAsync(
                    page, designer, $"designer-selected--{width}--{theme}--{brand}.png", written);

                var beforeDelete = await designer.Locator("[data-field-uuid]").CountAsync();
                await page.Locator("[data-testid='designer-mobile-delete']").TapAsync();
                await Assertions.Expect(designer.Locator("[data-field-uuid]")).ToHaveCountAsync(beforeDelete - 1);
                await SigningRedesignPhase9Specs.CaptureAsync(
                    page, designer, $"designer-deleted--{width}--{theme}--{brand}.png", written);

                var beforeDetect = await designer.Locator("[data-field-uuid]").CountAsync();
                await page.Locator("[data-testid='designer-mobile-detect']").TapAsync();
                await Assertions.Expect(designer.Locator("[data-field-uuid]")).ToHaveCountAsync(beforeDetect + 1);
                await SigningRedesignPhase9Specs.CaptureAsync(
                    page, designer, $"designer-detected--{width}--{theme}--{brand}.png", written);
                await SigningRedesignPhase9Specs.CaptureAsync(
                    page, SigningRedesignPhase9Specs.Viewer(page),
                    $"viewer--{width}--{theme}--{brand}.png", written);
                var comments = SigningRedesignPhase9Specs.Comments(page);
                await SigningRedesignPhase9Specs.CaptureCommentsSheetAsync(
                    page, comments, $"comments--{width}--{theme}--{brand}.png", written, expand: true);
                await SigningRedesignPhase9Specs.CollapseCommentsSheetAsync(page, comments);
                var commentToggle = comments.Locator(".tm-document-page-viewer__comment-toggle").First;
                if (await commentToggle.CountAsync() > 0)
                {
                    await commentToggle.TapAsync();
                }

                var commentPage = comments.Locator(".tm-document-page-viewer__page").First;
                if (await commentPage.CountAsync() > 0)
                {
                    await commentPage.TapAsync(new LocatorTapOptions { Position = new Position { X = 40, Y = 40 } });
                    await SigningRedesignPhase9Specs.CaptureCommentsSheetAsync(
                        page, comments, $"comments-draft--{width}--{theme}--{brand}.png", written, expand: true);
                }

                var runner = SigningRedesignPhase9Specs.Runner(page);
                await runner.ScrollIntoViewIfNeededAsync();
                var handle = runner.Locator(".tm-signing-form-runner__sheet-handle").First;
                if (await handle.GetAttributeAsync("aria-expanded") == "true")
                {
                    await handle.TapAsync();
                    await page.WaitForTimeoutAsync(150);
                }

                await runner.ScrollIntoViewIfNeededAsync();
                await SigningRedesignPhase9Specs.CaptureAsync(
                    page, runner, $"runner-peek--{width}--{theme}--{brand}.png", written);
                await runner.Locator(".tm-signing-form-runner__mobile-expand").First.TapAsync();
                await page.WaitForTimeoutAsync(150);
                await runner.Locator(".tm-signing-form-runner__documents").ScrollIntoViewIfNeededAsync();
                await SigningRedesignPhase9Specs.CaptureAsync(
                    page, runner, $"runner-list--{width}--{theme}--{brand}.png", written);
                var row = runner.Locator(".tm-signing-form-runner__field-button").First;
                if (await row.IsVisibleAsync())
                {
                    await row.ClickAsync(new LocatorClickOptions { Force = true });
                }

                await page.WaitForTimeoutAsync(200);
                await runner.Locator(".tm-signing-form-runner__documents").ScrollIntoViewIfNeededAsync();
                await SigningRedesignPhase9Specs.CaptureAsync(
                    page, runner, $"runner-full--{width}--{theme}--{brand}.png", written);
                await page.Context.CloseAsync();
            }
        }

        var tablet = await SigningRedesignPhase9Specs.OpenAsync(
            Browser, BaseUrl, 1024, 768, touch: true, RegisterContext, WaitForAppReadyAsync);
        await tablet.AddStyleTagAsync(new PageAddStyleTagOptions { Content = SigningRedesignPhase9Specs.WideLayoutStyle });
        await SigningRedesignPhase9Specs.CaptureAsync(
            tablet, SigningRedesignPhase9Specs.Designer(tablet), "designer--1024-touch--light--default.png", written);
        await SigningRedesignPhase9Specs.CaptureAsync(
            tablet, SigningRedesignPhase9Specs.Comments(tablet), "comments--1024-touch--light--default.png", written);
        await SigningRedesignPhase9Specs.CaptureAsync(
            tablet, SigningRedesignPhase9Specs.Viewer(tablet), "viewer--1024--light--default.png", written);
        await tablet.Context.CloseAsync();

        var desktop = await SigningRedesignPhase9Specs.OpenAsync(
            Browser, BaseUrl, 1366, 768, touch: false, RegisterContext, WaitForAppReadyAsync);
        await SigningRedesignPhase9Specs.CaptureAsync(
            desktop, SigningRedesignPhase9Specs.Viewer(desktop), "viewer--1366--light--default.png", written);
        await SigningRedesignPhase9Specs.CaptureAsync(
            desktop, SigningRedesignPhase9Specs.Comments(desktop), "comments--1366--light--default.png", written);
        await desktop.Context.CloseAsync();

        var runnerTablet = await SigningRedesignPhase9Specs.OpenAsync(
            Browser, BaseUrl, 768, 1024, touch: true, RegisterContext, WaitForAppReadyAsync);
        await SigningRedesignPhase9Specs.CaptureAsync(
            runnerTablet, SigningRedesignPhase9Specs.Runner(runnerTablet), "runner--768--light--default.png", written);
        await runnerTablet.Context.CloseAsync();

        Assert.IsTrue(written.Count >= 34, $"expected the phase-9 matrix, wrote {written.Count}");
    }
}

/// <summary>
/// Phase-9 behaviour (always on): the mobile runner hosts exactly one step panel inside one
/// <c>TmDrawer</c>, and the handle is the only <c>aria-expanded</c> toggle.
/// </summary>
[TestClass]
public class SigningRedesignPhase9BehaviorE2ETests : WasmTestBase
{
    [TestMethod]
    [Description("The mobile runner hosts one TmDrawer and one step panel; the handle is the only aria-expanded toggle")]
    public async Task SigningRunner_Mobile_OneDrawer_HandleIsOnlyAriaExpandedToggle()
    {
        var page = await SigningRedesignPhase9Specs.OpenAsync(
            Browser, BaseUrl, 390, 844, touch: true, RegisterContext, WaitForAppReadyAsync);
        var runner = SigningRedesignPhase9Specs.Runner(page);
        await runner.ScrollIntoViewIfNeededAsync();

        await Assertions.Expect(runner.Locator(".tm-drawer")).ToHaveCountAsync(1);
        await Assertions.Expect(runner.Locator(".tm-signing-form-runner__step-panel")).ToHaveCountAsync(1);
        await Assertions.Expect(runner.Locator("aside.tm-signing-form-runner__steps")).ToHaveCountAsync(0);

        var handle = runner.Locator(".tm-signing-form-runner__sheet-handle");
        await Assertions.Expect(handle).ToHaveAttributeAsync("aria-expanded", "true");
        var ctaExpanded = await runner.Locator(".tm-signing-form-runner__mobile-expand")
            .GetAttributeAsync("aria-expanded");
        Assert.IsNull(ctaExpanded, "Sign now is an action, not a second aria-expanded toggle");

        await handle.TapAsync();
        await Assertions.Expect(handle).ToHaveAttributeAsync("aria-expanded", "false");
        await Assertions.Expect(runner.Locator(".tm-drawer")).ToHaveAttributeAsync("data-snap-index", "0");
        await Assertions.Expect(runner.Locator(".tm-signing-form-runner__step-panel")).ToHaveCountAsync(1);
    }

    [TestMethod]
    [Description("At 1024 with touch the designer is the tablet layout, not the mobile sheet")]
    public async Task Designer_TabletTouch_IsNotTheMobileSheet()
    {
        var page = await SigningRedesignPhase9Specs.OpenAsync(
            Browser, BaseUrl, 1024, 768, touch: true, RegisterContext, WaitForAppReadyAsync);
        await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = SigningRedesignPhase9Specs.WideLayoutStyle });
        var designer = SigningRedesignPhase9Specs.Designer(page);
        await designer.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(300);

        var layout = await designer.GetAttributeAsync("data-layout");
        Assert.AreNotEqual("mobile", layout, $"a 1024 touch designer must not classify as mobile (got {layout})");

        var rail = designer.Locator(".tm-pdf-template-designer__palette--canvas").First;
        await Assertions.Expect(rail).ToBeVisibleAsync();
        var box = await rail.BoundingBoxAsync();
        Assert.IsNotNull(box);
        Assert.IsTrue(box!.Height <= 88, $"tablet rail is {box.Height}px, expected at most two touch targets");

        await designer.Locator("[data-field-uuid]").First.TapAsync();
        await rail.Locator("button, [role='button']").First.TapAsync();
        await Assertions.Expect(rail).ToBeVisibleAsync();
    }

    [TestMethod]
    [Description("At 390 the runner handle and Sign now stay in the viewport when the document is scrolled to the top")]
    public async Task SigningRunner_Mobile_PeekStaysInViewportWhenScrolledToTop()
    {
        var page = await SigningRedesignPhase9Specs.OpenAsync(
            Browser, BaseUrl, 390, 844, touch: true, RegisterContext, WaitForAppReadyAsync);
        var runner = SigningRedesignPhase9Specs.Runner(page);
        await runner.ScrollIntoViewIfNeededAsync();
        var handle = runner.Locator(".tm-signing-form-runner__sheet-handle");
        if (await handle.GetAttributeAsync("aria-expanded") == "true")
        {
            await handle.TapAsync();
        }

        await runner.ScrollIntoViewIfNeededAsync();
        var signNow = runner.Locator(".tm-signing-form-runner__mobile-expand");
        await Assertions.Expect(handle).ToBeInViewportAsync();
        await Assertions.Expect(signNow).ToBeInViewportAsync();
        await Assertions.Expect(runner.Locator(".tm-sheet-handle")).ToHaveCountAsync(0);
    }

    [TestMethod]
    [Description("A touch designer at 390 selects into the inspector sheet, deletes and detects, and shows no move or resize handles")]
    public async Task Designer_MobileTouch_SelectsDeletesDetects_WithoutMoveHandles()
    {
        var page = await SigningRedesignPhase9Specs.OpenAsync(
            Browser, BaseUrl, 390, 844, touch: true, RegisterContext, WaitForAppReadyAsync);
        var designer = SigningRedesignPhase9Specs.Designer(page);
        await designer.ScrollIntoViewIfNeededAsync();

        var field = designer.Locator("[data-field-uuid]").First;
        await field.TapAsync();
        await Assertions.Expect(designer.Locator(".tm-signing-field-editor-panel")).ToBeVisibleAsync();
        await Assertions.Expect(designer.Locator(".tm-signing-field__resize-handle")).ToHaveCountAsync(0);

        var beforeDelete = await designer.Locator("[data-field-uuid]").CountAsync();
        await page.Locator("[data-testid='designer-mobile-delete']").TapAsync();
        await Assertions.Expect(designer.Locator("[data-field-uuid]")).ToHaveCountAsync(beforeDelete - 1);

        var beforeDetect = await designer.Locator("[data-field-uuid]").CountAsync();
        await page.Locator("[data-testid='designer-mobile-detect']").TapAsync();
        await Assertions.Expect(designer.Locator("[data-field-uuid]")).ToHaveCountAsync(beforeDetect + 1);
        await Assertions.Expect(designer.Locator(".tm-signing-field__resize-handle")).ToHaveCountAsync(0);
    }

    [TestMethod]
    [Description("A two-page runner at 390 keeps both pages tall in snap 1, and Next lifts the page-2 field above the panel")]
    public async Task SigningRunner_Mobile_TwoPages_SnapDoesNotCollapsePages()
    {
        var page = await SigningRedesignPhase9Specs.OpenAsync(
            Browser, BaseUrl, 390, 844, touch: true, RegisterContext, WaitForAppReadyAsync,
            query: "runnerPages=2");
        var runner = SigningRedesignPhase9Specs.Runner(page);
        await runner.ScrollIntoViewIfNeededAsync();

        var documents = runner.Locator(".tm-signing-form-runner__document");
        await Assertions.Expect(documents).ToHaveCountAsync(2);
        var expand = runner.Locator(".tm-signing-form-runner__mobile-expand").First;
        if (await expand.IsVisibleAsync())
        {
            await expand.TapAsync();
        }

        await Assertions.Expect(runner.Locator(".tm-drawer")).ToHaveAttributeAsync("data-snap-index", "1");
        var heights = await documents.EvaluateAllAsync<double[]>(
            "nodes => nodes.map(node => node.getBoundingClientRect().height)");
        Assert.IsTrue(heights.Length == 2 && heights.All(height => height > 200),
            $"both pages must stay taller than 200px in snap 1, got [{string.Join(", ", heights)}]");

        var name = runner.Locator("input.tm-signing-text-step__input").First;
        await name.FillAsync("Alex Johnson");
        await name.BlurAsync();
        var next = runner.Locator(".tm-signing-form-runner__next").First;
        await next.TapAsync();
        var clear = false;
        for (var attempt = 0; attempt < 20 && !clear; attempt++)
        {
            await page.WaitForTimeoutAsync(150);
            clear = await page.EvaluateAsync<bool>(
                """
                () => {
                    const runner = document.querySelector("[data-testid='signing-runner-demo']");
                    const field = runner?.querySelector(".tm-signing-field[data-field-uuid='runner-page-2']");
                    const panel = runner?.querySelector(".tm-drawer__panel");
                    if (!field || !panel) return false;
                    const fieldBox = field.getBoundingClientRect();
                    const panelBox = panel.getBoundingClientRect();
                    return fieldBox.height > 0 && fieldBox.bottom <= panelBox.top + 2;
                }
                """);
        }

        Assert.IsTrue(clear, "the selected page-2 field must sit above the sheet panel");
    }

    [TestMethod]
    [Description("The comments sheet at 320 does not overflow its panel")]
    public async Task CommentsSheet_At320_DoesNotOverflow()
    {
        var page = await SigningRedesignPhase9Specs.OpenAsync(
            Browser, BaseUrl, 320, 844, touch: true, RegisterContext, WaitForAppReadyAsync);
        var comments = SigningRedesignPhase9Specs.Comments(page);
        await comments.ScrollIntoViewIfNeededAsync();
        var handle = comments.Locator(".tm-document-page-viewer__comments-sheet .tm-sheet__handle").First;
        if (await handle.CountAsync() > 0)
        {
            await handle.FocusAsync();
            await page.Keyboard.PressAsync("End");
            await page.WaitForTimeoutAsync(200);
        }

        var panel = comments.Locator(".tm-document-page-viewer__comments-sheet .tm-drawer__panel").First;
        await Assertions.Expect(panel).ToBeVisibleAsync();
        var overflow = await panel.EvaluateAsync<bool>("el => el.scrollWidth <= el.clientWidth + 1");
        Assert.IsTrue(overflow, "the comments sheet panel must not overflow horizontally at 320");
    }
}
