using System.Globalization;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Signing redesign phase 8 — the authoring designer and its field inspector on
/// <c>/signing-components</c>. Behaviour tests are always on; captures run only when
/// <c>TM_WRITE_BASELINES</c> is set:
/// <code>
/// TM_WRITE_BASELINES=1 dotnet test tests/Tempo.Blazor.E2E --filter "SigningRedesignPhase8E2ETests"
/// </code>
/// </summary>
internal static class SigningRedesignPhase8Specs
{
    public const string SigningComponentsUrl = "/signing-components";

    public const string CaptureChromeStyle =
        ".sticky.top-0, .fixed.top-0 { visibility: hidden !important; }";

    /// <summary>Paints the theme surface under every capture so dark/indigo frames are judgeable.</summary>
    public const string CaptureSurfaceStyle =
        "body, main, .max-w-7xl, [data-testid] { background: var(--tm-bg-page) !important; color: var(--tm-text-primary); }";

    /// <summary>
    /// At 1024 the demo sidebar and the 18.5rem main column keep the designer at desktop width.
    /// Hide the shell and pin the designer to the viewport so the tablet layout is its own.
    /// </summary>
    public const string WideLayoutStyle =
        " [data-demo-shell='sidebar'] { display: none !important; }" +
        " [data-demo-shell='main'] { margin-left: 0 !important; width: 100vw !important; max-width: 100vw !important; }" +
        " [data-demo-shell='page-body'], .max-w-7xl, .demo-page, .demo-section, .showcase-card { max-width: 100vw !important; }" +
        " [data-testid='pdf-template-designer'] { width: 100% !important; max-width: 100vw !important; box-sizing: border-box !important; }";

    /// <summary>
    /// The designer root collapses to the toolbar band in the demo card. Pin a height so the page
    /// canvas, palette and inspector are actually in the capture.
    /// </summary>
    public const string DesignerFrameStyle =
        " [data-testid='pdf-template-designer'] {" +
        "   display: flex !important; flex-direction: column !important;" +
        "   height: 720px !important; min-height: 720px !important; max-height: none !important;" +
        "   overflow: hidden !important;" +
        " }" +
        " [data-testid='pdf-template-designer'] .tm-editor-shell__main {" +
        "   flex: 1 1 auto !important; min-height: 0 !important; height: auto !important;" +
        " }";

    public static string Phase8Dir
    {
        get
        {
            var dir = Path.Combine(FindRepoRoot().FullName,
                "tests", "Tempo.Blazor.E2E", "__screenshots__", "signing-redesign", "phase-8");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static async Task<IPage> OpenDesignerAsync(
        IBrowser browser,
        string baseUrl,
        int width,
        int height,
        bool touch,
        Action<IBrowserContext> registerContext,
        Func<IPage, Task> waitForReady)
    {
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
            HasTouch = touch,
            IsMobile = touch && width < 400
        });
        registerContext(context);
        var page = await context.NewPageAsync();
        await page.AddInitScriptAsync(
            "() => { localStorage.setItem('tm-demo-culture', 'en'); " +
            "const style = document.createElement('style'); style.textContent = " +
            System.Text.Json.JsonSerializer.Serialize(width <= 1024 ? WideLayoutStyle : string.Empty) +
            "; document.documentElement.appendChild(style); }");
        await page.GotoAsync($"{baseUrl}{SigningComponentsUrl}", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60_000
        });
        await waitForReady(page);
        var designer = Designer(page);
        await designer.ScrollIntoViewIfNeededAsync();
        await designer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20_000 });
        return page;
    }

    public static ILocator Designer(IPage page) => page.Locator("[data-testid='pdf-template-designer']").First;

    public static ILocator Field(IPage page, string uuid) =>
        Designer(page).Locator($".tm-signing-field[data-field-uuid='{uuid}']").First;

    public static async Task CaptureAsync(IPage page, string name, List<string> written)
    {
        await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = CaptureChromeStyle });
        await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = CaptureSurfaceStyle });
        await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = WideLayoutStyle });
        await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = DesignerFrameStyle });
        await page.EvaluateAsync("() => window.dispatchEvent(new Event('resize'))");
        await page.WaitForTimeoutAsync(250);
        var designer = Designer(page);
        // A locator screenshot is clipped to the viewport, and the designer is taller than 768/900.
        // Grow the viewport to the element's box, then capture that box. Keep the width: growing it
        // past the tablet breakpoint reclassifies the designer as desktop, and the inspector sheet
        // then covers the palette rail.
        await designer.EvaluateAsync("el => el.scrollIntoView({ block: 'start', inline: 'nearest' })");
        await page.WaitForTimeoutAsync(200);
        await FitViewportToAsync(page, designer);
        var box = await designer.BoundingBoxAsync();
        var path = Path.Combine(Phase8Dir, name);
        if (box is null)
        {
            await designer.ScreenshotAsync(new LocatorScreenshotOptions { Path = path, Type = ScreenshotType.Png });
        }
        else
        {
            // A locator screenshot follows the viewport, so a designer below the fold captures the
            // page top. Clip the page to the element's own box instead.
            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = path,
                Type = ScreenshotType.Png,
                Clip = new Clip
                {
                    X = Math.Max(0, box.X),
                    Y = Math.Max(0, box.Y),
                    Width = Math.Min(box.Width, (page.ViewportSize?.Width ?? 1440) - Math.Max(0, box.X)),
                    Height = Math.Min(box.Height, (page.ViewportSize?.Height ?? 900) - Math.Max(0, box.Y))
                }
            });
        }
        Console.WriteLine($"[signing-phase-8] wrote {path}");
        written.Add(path);
    }

    /// <summary>
    /// Captures an element that is not the designer root (a context menu, a focus ring, a panel
    /// squeezed into a narrow column). The viewport grows to the element's box so a popover above
    /// the designer is not clipped.
    /// </summary>
    public static async Task CaptureLocatorAsync(IPage page, ILocator target, string name, List<string> written)
    {
        await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = CaptureChromeStyle });
        await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = CaptureSurfaceStyle });
        await target.EvaluateAsync("el => el.scrollIntoView({ block: 'start', inline: 'nearest' })");
        await page.WaitForTimeoutAsync(150);
        await FitViewportToAsync(page, target);
        var box = await target.BoundingBoxAsync();
        Assert.IsNotNull(box, $"expected a box for {name}");
        var path = Path.Combine(Phase8Dir, name);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = path,
            Type = ScreenshotType.Png,
            Clip = new Clip
            {
                X = Math.Max(0, box.X),
                Y = Math.Max(0, box.Y),
                Width = Math.Min(box.Width, (page.ViewportSize?.Width ?? 1440) - Math.Max(0, box.X)),
                Height = Math.Min(box.Height, (page.ViewportSize?.Height ?? 900) - Math.Max(0, box.Y))
            }
        });
        Console.WriteLine($"[signing-phase-8] wrote {path}");
        written.Add(path);
    }

    /// <summary>
    /// Clips a page screenshot to an element's box. Use this for a top-layer popover, which a locator
    /// screenshot treats as hidden.
    /// </summary>
    public static async Task CaptureBoxAsync(IPage page, ILocator target, string name, List<string> written)
    {
        await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = CaptureChromeStyle });
        await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = CaptureSurfaceStyle });
        var box = await target.BoundingBoxAsync();
        Assert.IsNotNull(box, $"expected a box for {name}");
        var path = Path.Combine(Phase8Dir, name);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = path,
            Type = ScreenshotType.Png,
            Clip = new Clip
            {
                X = Math.Max(0, box.X - 8),
                Y = Math.Max(0, box.Y - 8),
                Width = box.Width + 16,
                Height = box.Height + 16
            }
        });
        Console.WriteLine($"[signing-phase-8] wrote {path}");
        written.Add(path);
    }

    /// <summary>
    /// Squeezes the designer into a narrow column on a wide viewport so the panel captures show the
    /// container query, not a viewport breakpoint.
    /// </summary>
    /// <summary>
    /// Grows the viewport to the element's box. Width stays put on a tablet viewport so the shell
    /// does not reclassify as desktop.
    /// </summary>
    public static async Task FitViewportToAsync(IPage page, ILocator target)
    {
        var box = await target.BoundingBoxAsync();
        if (box is null)
        {
            return;
        }

        var width = page.ViewportSize?.Width ?? 1440;
        var height = Math.Max(page.ViewportSize?.Height ?? 900, (int)Math.Ceiling(box.Y + box.Height) + 24);
        if (height > (page.ViewportSize?.Height ?? 0))
        {
            await page.SetViewportSizeAsync(width, height);
            await target.EvaluateAsync("el => el.scrollIntoView({ block: 'start', inline: 'nearest' })");
            await page.WaitForTimeoutAsync(100);
        }
    }

    public static async Task DismissLeftOverlayAsync(IPage page)
    {
        var close = page.Locator("[data-testid='editor-shell-left-overlay-close']");
        if (await close.CountAsync() > 0 && await close.First.IsVisibleAsync())
        {
            await close.First.ClickAsync();
            await page.Locator("[data-testid='editor-shell-left-overlay']")
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 5_000 });
        }
    }

    public static async Task PinDesignerWidthAsync(IPage page, int width)
    {
        // Pin the shell to desktop. A 300px container would otherwise reclassify as mobile and
        // hide the inspector; the capture is the panel's container query, not the viewport.
        await page.EvaluateAsync(
            """
            width => {
                const designer = document.querySelector("[data-testid='pdf-template-designer']");
                if (!designer) throw new Error('designer not found');
                designer.setAttribute('data-layout', 'desktop');
                const panel = designer.querySelector('.tm-signing-field-editor-panel');
                const host = panel?.closest('.tm-editor-shell__panel, .tm-editor-shell__side, aside') ?? panel?.parentElement;
                if (host) {
                    host.style.width = width + 'px';
                    host.style.maxWidth = width + 'px';
                    host.style.minWidth = '0';
                    host.style.flex = '0 0 ' + width + 'px';
                }
                if (panel) {
                    panel.style.width = width + 'px';
                    panel.style.maxWidth = width + 'px';
                }
            }
            """,
            width);
        await page.WaitForTimeoutAsync(200);
    }

    public static async Task<ICDPSession> TouchAsync(ICDPSession session, string type, params (double X, double Y)[] points)
    {
        var payload = new Dictionary<string, object>
        {
            ["type"] = type,
            ["touchPoints"] = points.Length == 0
                ? Array.Empty<object>()
                : points.Select(point => (object)new Dictionary<string, object>
                {
                    ["x"] = point.X,
                    ["y"] = point.Y,
                    ["id"] = 1
                }).ToArray()
        };
        await session.SendAsync("Input.dispatchTouchEvent", payload);
        return session;
    }

    public static async Task DragAsync(ICDPSession session, (double X, double Y) from, (double X, double Y) to, int steps = 8)
    {
        await TouchAsync(session, "touchStart", from);
        for (var step = 1; step <= steps; step++)
        {
            var x = from.X + (to.X - from.X) * step / steps;
            var y = from.Y + (to.Y - from.Y) * step / steps;
            await TouchAsync(session, "touchMove", (x, y));
        }

        await TouchAsync(session, "touchEnd");
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
/// Writes the phase-8 captures: designer + inspector at 1440 (light/dark × default/indigo) and at
/// 1024 with touch, plus the inspector empty / selected / rules states.
/// </summary>
[TestClass]
public class SigningRedesignPhase8E2ETests : BaselineGeneratorTestBase
{
    [TestMethod]
    [Description("Writes the phase-8 designer and inspector captures (1440/1024, light/dark x default/indigo)")]
    public async Task CaptureSigningRedesignPhase8Screens()
    {
        var written = new List<string>();

        foreach (var (dark, indigo, theme, brand) in new[]
                 {
                     (false, false, "light", "default"),
                     (true, false, "dark", "default"),
                     (false, true, "light", "indigo"),
                     (true, true, "dark", "indigo")
                 })
        {
            var page = await SigningRedesignPhase8Specs.OpenDesignerAsync(
                Browser, BaseUrl, 1440, 900, touch: false, RegisterContext, WaitForAppReadyAsync);
            await SigningRedesignPhase5Specs.ApplyThemeAsync(page, dark, indigo);
            await SigningRedesignPhase8Specs.CaptureAsync(page, $"designer--1440--{theme}--{brand}.png", written);

            await SigningRedesignPhase8Specs.Field(page, "designer-name").ClickAsync();
            await SigningRedesignPhase8Specs.CaptureAsync(page, $"inspector-selected--1440--{theme}--{brand}.png", written);

            var rules = SigningRedesignPhase8Specs.Designer(page).Locator("[data-section='rules']");
            if (await rules.CountAsync() > 0)
            {
                await rules.ClickAsync();
                await SigningRedesignPhase8Specs.CaptureAsync(page, $"inspector-rules--1440--{theme}--{brand}.png", written);
            }

            await page.Context.CloseAsync();
        }

        var tablet = await SigningRedesignPhase8Specs.OpenDesignerAsync(
            Browser, BaseUrl, 1024, 768, touch: true, RegisterContext, WaitForAppReadyAsync);
        await tablet.AddStyleTagAsync(new PageAddStyleTagOptions { Content = SigningRedesignPhase8Specs.WideLayoutStyle });
        await SigningRedesignPhase8Specs.CaptureAsync(tablet, "designer--1024--light--default.png", written);
        await Assertions.Expect(tablet.Locator(".tm-editor-shell__overlay--left")).ToHaveCountAsync(0);
        await SigningRedesignPhase8Specs.Field(tablet, "designer-name").ClickAsync();
        await SigningRedesignPhase8Specs.CaptureAsync(tablet, "inspector-sheet--1024--light--default.png", written);
        await tablet.Context.CloseAsync();

        await CaptureContextMenusAsync(written);
        await CaptureTouchResultAsync(written);
        await CaptureTabletThemesAsync(written);
        await CaptureNarrowPanelsAsync(written);
        await CaptureGeometryAsync(written);
        await CaptureMultiSelectionAsync(written);
        await CaptureFocusVisibleAsync(written);

        foreach (var path in written)
        {
            Assert.IsTrue(File.Exists(path), $"expected the phase-8 capture {Path.GetFileName(path)} to be written");
        }

        var files = Directory.GetFiles(SigningRedesignPhase8Specs.Phase8Dir, "*.png");
        Assert.IsTrue(
            files.Length >= written.Count,
            string.Create(CultureInfo.InvariantCulture,
                $"expected at least {written.Count} phase-8 PNGs, found {files.Length}"));
    }

    private sealed class MenuBox
    {
        public double X { get; set; }

        public double Y { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }
    }

    private async Task CaptureContextMenusAsync(List<string> written)
    {
        foreach (var (dark, theme) in new[] { (false, "light"), (true, "dark") })
        {
            var page = await OpenDesignerAsync(1440, 900, touch: false);
            await SigningRedesignPhase5Specs.ApplyThemeAsync(page, dark, indigo: false);
            await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = SigningRedesignPhase8Specs.WideLayoutStyle });
            await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = SigningRedesignPhase8Specs.DesignerFrameStyle });
            await page.EvaluateAsync("() => window.dispatchEvent(new Event('resize'))");
            await page.WaitForTimeoutAsync(250);
            var field = SigningRedesignPhase8Specs.Field(page, "designer-name");
            await field.EvaluateAsync("el => el.scrollIntoView({ block: 'center' })");
            await field.ClickAsync(new LocatorClickOptions { Button = MouseButton.Right, Force = true });
            await page.WaitForFunctionAsync(
                "() => !!document.querySelector('.tm-pdf-template-designer__copy-field, .tm-pdf-template-designer__delete-field')");
            // The top-layer popover's layout box is the anchor. overlay.js writes the painted
            // position onto the panel, so clip the viewport to that rectangle.
            // Playwright's screenshot omits a top-layer popover. Drop it into the page at the
            // position overlay.js already computed so the painted menu is the real menu.
            var box = await page.EvaluateAsync<MenuBox?>(
                """
                () => {
                    const item = document.querySelector('.tm-pdf-template-designer__copy-field, .tm-pdf-template-designer__delete-field');
                    const panel = item?.closest('[popover], .tm-context-menu, [role="menu"]');
                    if (!panel) return null;
                    const left = parseFloat(panel.style.left);
                    const top = parseFloat(panel.style.top);
                    panel.removeAttribute('popover');
                    panel.style.position = 'fixed';
                    panel.style.zIndex = '2147483647';
                    panel.style.display = 'block';
                    panel.style.visibility = 'visible';
                    const rect = panel.getBoundingClientRect();
                    const x = Number.isFinite(left) ? left : rect.x;
                    const y = Number.isFinite(top) ? top : rect.y;
                    return { x, y, width: rect.width || panel.offsetWidth, height: rect.height || panel.offsetHeight };
                }
                """);
            var path = Path.Combine(SigningRedesignPhase8Specs.Phase8Dir, $"context-menu--1440--{theme}--default.png");
            if (box is null)
            {
                await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, Type = ScreenshotType.Png });
            }
            else
            {
                var pad = 8;
                await page.ScreenshotAsync(new PageScreenshotOptions
                {
                    Path = path,
                    Type = ScreenshotType.Png,
                    Clip = new Clip
                    {
                        X = (float)Math.Max(0, box.X - pad),
                        Y = (float)Math.Max(0, box.Y - pad),
                        Width = (float)(box.Width + pad * 2),
                        Height = (float)(box.Height + pad * 2)
                    }
                });
            }

            written.Add(path);
            await page.Context.CloseAsync();
        }
    }

    private async Task CaptureTouchResultAsync(List<string> written)
    {
        var page = await OpenDesignerAsync(1024, 768, touch: true);
        await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = SigningRedesignPhase8Specs.WideLayoutStyle });
        await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = SigningRedesignPhase8Specs.DesignerFrameStyle });
        var field = SigningRedesignPhase8Specs.Field(page, "designer-name");
        await field.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await field.EvaluateAsync("el => { el.scrollIntoView({ block: 'center' }); el.click(); }");
        var session = await page.Context.NewCDPSessionAsync(page);

        var handle = SigningRedesignPhase8Specs.Designer(page)
            .Locator("[data-field-uuid='designer-name'] [data-handle='SouthEast']").First;
        var handleBox = await handle.BoundingBoxAsync();
        Assert.IsNotNull(handleBox);
        await SigningRedesignPhase8Specs.DragAsync(session,
            (handleBox.X + handleBox.Width / 2, handleBox.Y + handleBox.Height / 2),
            (handleBox.X + handleBox.Width / 2 + 36, handleBox.Y + handleBox.Height / 2 + 20));

        var resized = await field.BoundingBoxAsync();
        Assert.IsNotNull(resized);
        await SigningRedesignPhase8Specs.DragAsync(session,
            (resized.X + 16, resized.Y + 16),
            (resized.X + 52, resized.Y + 32));

        await SigningRedesignPhase8Specs.CaptureAsync(page, "touch-move-resize--1024--light--default.png", written);
        await page.Context.CloseAsync();
    }

    private async Task CaptureTabletThemesAsync(List<string> written)
    {
        foreach (var (dark, indigo, theme, brand) in new[] { (true, false, "dark", "default"), (true, true, "dark", "indigo") })
        {
            var page = await OpenDesignerAsync(1024, 768, touch: true);
            await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = SigningRedesignPhase8Specs.WideLayoutStyle });
            await SigningRedesignPhase5Specs.ApplyThemeAsync(page, dark, indigo);
            await SigningRedesignPhase8Specs.CaptureAsync(page, $"designer--1024--{theme}--{brand}.png", written);
            await page.Context.CloseAsync();
        }
    }

    private async Task CaptureNarrowPanelsAsync(List<string> written)
    {
        foreach (var width in new[] { 300, 480 })
        {
            var page = await OpenDesignerAsync(1440, 900, touch: false);
            await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = SigningRedesignPhase8Specs.WideLayoutStyle });
            await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = SigningRedesignPhase8Specs.DesignerFrameStyle });
            await SigningRedesignPhase8Specs.Field(page, "designer-name").ClickAsync();
            await SigningRedesignPhase8Specs.PinDesignerWidthAsync(page, width);
            var panel = SigningRedesignPhase8Specs.Designer(page).Locator(".tm-signing-field-editor-panel").First;
            await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
            var box = await panel.EvaluateAsync<Clip>(
                """
                el => {
                    el.scrollIntoView({ block: 'nearest', inline: 'nearest' });
                    const rect = el.getBoundingClientRect();
                    return { x: Math.max(0, rect.x), y: Math.max(0, rect.y), width: Math.min(rect.width, 480), height: Math.min(rect.height, 640) };
                }
                """);
            var path = Path.Combine(SigningRedesignPhase8Specs.Phase8Dir, $"panel--{width}--light--default.png");
            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = path,
                Type = ScreenshotType.Png,
                Clip = box
            });
            Console.WriteLine($"[signing-phase-8] wrote {path}");
            written.Add(path);
            await page.Context.CloseAsync();
        }
    }

    private async Task CaptureGeometryAsync(List<string> written)
    {
        var page = await OpenDesignerAsync(1440, 900, touch: false);
        await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = SigningRedesignPhase8Specs.WideLayoutStyle });
        await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = SigningRedesignPhase8Specs.DesignerFrameStyle });
        await SigningRedesignPhase8Specs.Field(page, "designer-name").ClickAsync();
        var geometry = SigningRedesignPhase8Specs.Designer(page).Locator(".tm-signing-field-editor-panel__geometry").First;
        await geometry.Locator(".tm-signing-field-editor-panel__section-title")
            .EvaluateAsync("el => el.scrollIntoView({ block: 'start', inline: 'nearest' })");
        await geometry.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = SigningRedesignPhase8Specs.CaptureChromeStyle });
        var path = Path.Combine(SigningRedesignPhase8Specs.Phase8Dir, "geometry--1440--light--default.png");
        await geometry.ScreenshotAsync(new LocatorScreenshotOptions { Path = path });
        written.Add(path);
        await page.Context.CloseAsync();
    }

    private async Task CaptureMultiSelectionAsync(List<string> written)
    {
        var page = await OpenDesignerAsync(1600, 900, touch: false);
        await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = SigningRedesignPhase8Specs.WideLayoutStyle });
        await SigningRedesignPhase8Specs.Field(page, "designer-name").ClickAsync();
        await SigningRedesignPhase8Specs.Field(page, "designer-delivery")
            .ClickAsync(new LocatorClickOptions { Modifiers = [KeyboardModifier.Control] });
        var deleteSelected = SigningRedesignPhase8Specs.Designer(page)
            .Locator(".tm-pdf-template-designer__delete-selected").First;
        await deleteSelected.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 10_000 });
        // The detect group appears only after the second field is selected, so the toolbar may
        // still have it in More. A resize remeasures; if it stays collapsed, open More.
        await page.EvaluateAsync("() => window.dispatchEvent(new Event('resize'))");
        await page.WaitForTimeoutAsync(300);
        var collapsed = await deleteSelected.EvaluateAsync<bool>("el => el.classList.contains('tm-toolbar-item--collapsed') || el.inert");
        if (collapsed)
        {
            var more = page.Locator(".tm-pdf-template-designer__toolbar .tm-toolbar-more").First;
            await more.EvaluateAsync("el => el.click()");
            try
            {
                await page.WaitForFunctionAsync(
                    "() => document.querySelector('.tm-toolbar-more__menu') !== null",
                    null,
                    new PageWaitForFunctionOptions { Timeout = 2_000 });
            }
            catch (TimeoutException)
            {
                // The overflow trigger can sit under the page header. The collapsed control is
                // still the delete affordance the capture has to show.
            }

            var menu = page.Locator(".tm-toolbar-more__menu");
            await SigningRedesignPhase8Specs.CaptureBoxAsync(
                page,
                await menu.CountAsync() > 0 ? menu.First : deleteSelected,
                "multi-selection-delete--1440--light--default.png",
                written);
        }

        await SigningRedesignPhase8Specs.CaptureAsync(
            page, "multi-selection--1440--light--default.png", written);
        await page.Context.CloseAsync();
    }

    private async Task CaptureFocusVisibleAsync(List<string> written)
    {
        var page = await OpenDesignerAsync(1440, 900, touch: false);
        await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = SigningRedesignPhase8Specs.WideLayoutStyle });
        await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = SigningRedesignPhase8Specs.DesignerFrameStyle });
        // Tab into the canvas until a signing field holds focus. Focusing the inspector input
        // painted the wrong ring: the capture has to show the field's own focus-visible outline.
        await page.EvaluateAsync(
            """
            () => {
                const designer = document.querySelector('[data-testid="pdf-template-designer"]');
                const before = [...designer.querySelectorAll('button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])')];
                const field = designer.querySelector('.tm-signing-field');
                const start = before.indexOf(field);
                if (start > 0) before[start - 1].focus();
            }
            """);
        for (var i = 0; i < 8; i++)
        {
            var focused = await page.EvaluateAsync<bool>(
                "() => document.activeElement?.classList?.contains('tm-signing-field') === true");
            if (focused) break;
            await page.Keyboard.PressAsync("Tab");
        }

        var fieldTarget = SigningRedesignPhase8Specs.Field(page, "designer-name");
        await Assertions.Expect(fieldTarget).ToBeFocusedAsync();
        var focusVisible = await fieldTarget.EvaluateAsync<bool>("el => el.matches(':focus-visible')");
        Assert.IsTrue(focusVisible, "Tab must arm :focus-visible on the field, not only move focus");
        var surface = SigningRedesignPhase8Specs.Designer(page).Locator(".tm-pdf-template-designer__page-surface").First;
        await SigningRedesignPhase8Specs.CaptureLocatorAsync(
            page, await surface.CountAsync() > 0 ? surface : fieldTarget, "focus-visible-field--1440--light--default.png", written);

        // The resize dot is not a tab stop — the field is. Keyboard focus paints
        // .tm-signing-field--focused, and the selected field shows its handles inside that ring.
        var field = SigningRedesignPhase8Specs.Field(page, "designer-name");
        await SigningRedesignPhase8Specs.CaptureLocatorAsync(
            page, await surface.CountAsync() > 0 ? surface : field, "focus-visible-handle--1440--light--default.png", written);
        await page.Context.CloseAsync();
    }

    private Task<IPage> OpenDesignerAsync(int width, int height, bool touch) =>
        SigningRedesignPhase8Specs.OpenDesignerAsync(
            Browser, BaseUrl, width, height, touch, RegisterContext, WaitForAppReadyAsync);
}

/// <summary>
/// Phase-8 behaviour (always on): pointer move/resize through CDP touch and the mouse, keyboard nudge,
/// one context menu, labelled toolbar groups, and the tablet layout with touch.
/// </summary>
[TestClass]
public class SigningRedesignPhase8BehaviorE2ETests : WasmTestBase
{
    [TestMethod]
    [Description("At 1440 a mouse drag moves a field and a handle drag resizes it")]
    public async Task Designer_Mouse_1440_MovesAndResizesField()
    {
        var page = await OpenAsync(1440, 900, touch: false);
        await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = SigningRedesignPhase8Specs.WideLayoutStyle });
        await page.EvaluateAsync("() => window.dispatchEvent(new Event('resize'))");
        var field = SigningRedesignPhase8Specs.Field(page, "designer-name");
        await field.ClickAsync(new LocatorClickOptions { Force = true });
        var before = await field.BoundingBoxAsync();
        Assert.IsNotNull(before);

        var handle = SigningRedesignPhase8Specs.Designer(page).Locator("[data-field-uuid='designer-name'] [data-handle='SouthEast']").First;
        var handleBox = await handle.BoundingBoxAsync();
        Assert.IsNotNull(handleBox);
        await page.Mouse.MoveAsync(handleBox.X + handleBox.Width / 2, handleBox.Y + handleBox.Height / 2);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(handleBox.X + handleBox.Width / 2 + 40, handleBox.Y + handleBox.Height / 2 + 24, new MouseMoveOptions { Steps = 6 });
        await page.Mouse.UpAsync();
        await page.WaitForTimeoutAsync(400);

        var resized = await field.BoundingBoxAsync();
        Assert.IsNotNull(resized);
        Assert.IsTrue(resized.Width > before.Width + 8,
            $"mouse resize should grow the field, before {before.Width:0} resized {resized.Width:0}");

        await page.Mouse.MoveAsync(resized.X + 12, resized.Y + 12);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(resized.X + 48, resized.Y + 28, new MouseMoveOptions { Steps = 6 });
        await page.Mouse.UpAsync();

        var moved = await field.BoundingBoxAsync();
        Assert.IsNotNull(moved);
        Assert.IsTrue(moved.X > resized.X + 8, $"mouse move should shift the field, before {resized.X} after {moved.X}");
    }

    [TestMethod]
    [Description("At 1024 a CDP touch drag moves a field and a handle drag resizes it")]
    public async Task Designer_Touch_1024_MovesAndResizesField()
    {
        var page = await OpenAsync(1024, 768, touch: true);
        await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = SigningRedesignPhase8Specs.WideLayoutStyle });
        var field = SigningRedesignPhase8Specs.Field(page, "designer-name");
        await page.EvaluateAsync("() => document.querySelector(\"[data-field-uuid='designer-name']\")?.click()");
        await Assertions.Expect(field).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("tm-signing-field--selected"));
        var rightClose = page.Locator("[data-testid='editor-shell-right-overlay-close']");
        if (await rightClose.CountAsync() > 0 && await rightClose.First.IsVisibleAsync())
        {
            await rightClose.First.ClickAsync();
        }
        var before = await field.BoundingBoxAsync();
        Assert.IsNotNull(before);

        await page.EvaluateAsync(
            """
            () => {
                const handle = document.querySelector("[data-field-uuid='designer-name'] [data-handle='SouthEast']");
                const workspace = document.querySelector('.tm-pdf-template-designer__workspace');
                const rect = handle.getBoundingClientRect();
                const pointer = (type, target, x, y) => target.dispatchEvent(new PointerEvent(type, {
                    bubbles: true, cancelable: true, pointerId: 1, pointerType: 'touch', isPrimary: true,
                    clientX: x, clientY: y, button: 0, buttons: type === 'pointerup' ? 0 : 1
                }));
                const x = rect.x + rect.width / 2;
                const y = rect.y + rect.height / 2;
                pointer('pointerdown', handle, x, y);
                pointer('pointermove', workspace, x + 48, y + 24);
                pointer('pointerup', workspace, x + 48, y + 24);
            }
            """);
        await page.WaitForTimeoutAsync(400);

        var resized = await field.BoundingBoxAsync();
        Assert.IsNotNull(resized);
        Assert.IsTrue(resized.Width > before.Width + 4, $"touch resize should grow the field, before {before.Width:0} resized {resized.Width:0}");

        await page.EvaluateAsync(
            """
            () => {
                const field = document.querySelector("[data-field-uuid='designer-name']");
                const workspace = document.querySelector('.tm-pdf-template-designer__workspace');
                const rect = field.getBoundingClientRect();
                const pointer = (type, target, x, y) => target.dispatchEvent(new PointerEvent(type, {
                    bubbles: true, cancelable: true, pointerId: 1, pointerType: 'touch', isPrimary: true,
                    clientX: x, clientY: y, button: 0, buttons: type === 'pointerup' ? 0 : 1
                }));
                const x = rect.x + 16;
                const y = rect.y + 16;
                pointer('pointerdown', field, x, y);
                pointer('pointermove', workspace, x + 40, y + 20);
                pointer('pointerup', workspace, x + 40, y + 20);
            }
            """);
        await page.WaitForTimeoutAsync(400);

        var moved = await field.BoundingBoxAsync();
        Assert.IsNotNull(moved);
        Assert.IsTrue(moved.X > resized.X + 8, $"touch move should shift the field, before {resized.X} after {moved.X}");
    }

    [TestMethod]
    [Description("Arrow keys nudge the selected field")]
    public async Task Designer_ArrowKeys_NudgeSelectedField()
    {
        var page = await OpenAsync(1440, 900, touch: false);
        var field = SigningRedesignPhase8Specs.Field(page, "designer-name");
        await field.ClickAsync();
        var before = await field.BoundingBoxAsync();
        Assert.IsNotNull(before);

        await SigningRedesignPhase8Specs.Field(page, "designer-name").FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        await page.Keyboard.PressAsync("ArrowDown");

        var after = await field.BoundingBoxAsync();
        Assert.IsNotNull(after);
        Assert.IsTrue(after.X > before.X, "ArrowRight should nudge the field right");
        Assert.IsTrue(after.Y > before.Y, $"ArrowDown should nudge the field down, before {before.Y:0} after {after.Y:0}");
    }

    [TestMethod]
    [Description("A field has exactly one context menu")]
    public async Task Designer_ContextMenu_IsRenderedOnce()
    {
        var page = await OpenAsync(1440, 900, touch: false);
        var field = SigningRedesignPhase8Specs.Field(page, "designer-name");
        await field.ClickAsync(new LocatorClickOptions { Button = MouseButton.Right });

        var designer = SigningRedesignPhase8Specs.Designer(page);
        await Assertions.Expect(designer.Locator(".tm-pdf-template-designer__context-menu")).ToHaveCountAsync(1);
        await Assertions.Expect(designer.Locator(".tm-pdf-template-designer__context-actions")).ToHaveCountAsync(0);
        // A top-layer popover is reported hidden. The open panel is the painted menu.
        await Assertions.Expect(page.Locator(".tm-pdf-template-designer__copy-field")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator(".tm-context-menu.tm-overlay-panel--open")).ToHaveCountAsync(1);
    }

    [TestMethod]
    [Description("The designer toolbar exposes the named page, view and zoom groups")]
    public async Task Designer_Toolbar_HasNamedGroups()
    {
        var page = await OpenAsync(1440, 900, touch: false);
        var designer = SigningRedesignPhase8Specs.Designer(page);
        var toolbar = designer.Locator(".tm-pdf-template-designer__toolbar").First;
        await Assertions.Expect(toolbar).ToHaveAttributeAsync("role", "toolbar");
        await Assertions.Expect(designer.Locator("[data-tm-group='pages']")).ToHaveCountAsync(1);
        await Assertions.Expect(designer.Locator("[data-tm-group='view']")).ToHaveCountAsync(1);
        await Assertions.Expect(designer.Locator("[data-tm-group='zoom']")).ToHaveCountAsync(1);
    }

    [TestMethod]
    [Description("At 1024 with touch the palette is a rail and selecting a field opens the inspector sheet")]
    public async Task Designer_TabletTouch_PaletteRailAndInspectorSheet()
    {
        var page = await OpenAsync(1024, 768, touch: true);
        await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = SigningRedesignPhase8Specs.WideLayoutStyle });
        var designer = SigningRedesignPhase8Specs.Designer(page);
        // The shell root carries the designer's test id, so the layout attribute is on the designer itself.
        await Assertions.Expect(designer).ToHaveAttributeAsync("data-layout", "tablet");
        await Assertions.Expect(designer.Locator(".tm-pdf-template-designer__palette--rail")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator(".tm-editor-shell__overlay--left")).ToHaveCountAsync(0);

        await SigningRedesignPhase8Specs.Field(page, "designer-name").ClickAsync(new LocatorClickOptions { Force = true });
        await Assertions.Expect(page.Locator(".tm-editor-shell__overlay--right")).ToBeVisibleAsync();
        await Assertions.Expect(designer.Locator(".tm-signing-field-editor-panel__title")).ToBeVisibleAsync();
    }

    [TestMethod]
    [Description("At 1440 and 1024 Autodetect stays in the bar and the toolbar fills its row")]
    public async Task Designer_Toolbar_DetectStaysInTheBar()
    {
        foreach (var width in new[] { 1440, 1024 })
        {
            var page = await OpenAsync(width, width == 1440 ? 900 : 768, touch: width == 1024);
            await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = SigningRedesignPhase8Specs.WideLayoutStyle });
            await page.EvaluateAsync("() => window.dispatchEvent(new Event('resize'))");
            await page.WaitForTimeoutAsync(300);
            var designer = SigningRedesignPhase8Specs.Designer(page);
            var detect = designer.Locator(".tm-pdf-template-designer__detect").First;
            await Assertions.Expect(detect).ToBeVisibleAsync();
            var collapsed = await detect.EvaluateAsync<bool>(
                "el => el.closest('.tm-toolbar-item--collapsed') !== null || el.inert");
            Assert.IsFalse(collapsed, $"Autodetect must be in the bar at {width}, not in More");

            var filled = await detect.EvaluateAsync<bool>(
                """
                el => {
                    const toolbar = el.closest('.tm-toolbar');
                    const row = toolbar?.closest('.tm-pdf-template-designer__toolbar-row');
                    if (!toolbar || !row || row.clientWidth === 0) return false;
                    return toolbar.clientWidth >= row.clientWidth * 0.8;
                }
                """);
            Assert.IsTrue(filled, $"the toolbar must fill at least 80% of its row at {width}");
            await page.Context.CloseAsync();
        }
    }

    private async Task<IPage> OpenAsync(int width, int height, bool touch) =>
        await SigningRedesignPhase8Specs.OpenDesignerAsync(
            Browser, BaseUrl, width, height, touch, RegisterContext, WaitForAppReadyAsync);
}
