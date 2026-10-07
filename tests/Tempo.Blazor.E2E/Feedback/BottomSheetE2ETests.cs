using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E.Feedback;

/// <summary>
/// The bottom sheet on a phone viewport (WASM @ 7106), against the /feedback demo's sheet.
/// </summary>
/// <remarks>
/// A touch context at 390px wide. The sheet must anchor to the bottom edge, keep its footer visible
/// while the body scrolls and while the on-screen keyboard is open, and return focus to the trigger
/// that opened it. A second context at 1024px with touch checks the sheet still presents there.
/// </remarks>
[TestClass]
[TestCategory("WASM")]
public sealed class BottomSheetE2ETests : WasmTestBase
{
    private const string Route = "/feedback";

    private async Task<IPage> OpenTouchAsync(int width, int height)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            HasTouch = true,
            ViewportSize = new ViewportSize { Width = width, Height = height },
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}{Route}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        return page;
    }

    private static async Task OpenSheetAsync(IPage page)
    {
        var trigger = page.GetByTestId("open-bottom-sheet");
        await trigger.ScrollIntoViewIfNeededAsync();
        await trigger.ClickAsync();
        await page.Locator(".tm-drawer--bottom").WaitForAsync();
        // The panel slides up over 250ms. Measuring before it settles reports the panel mid-animation,
        // which reads as a static element offset by its own height.
        await page.Locator(".tm-drawer--bottom .tm-drawer__panel").EvaluateAsync(
            "el => Promise.all(el.getAnimations().map(a => a.finished)).catch(() => {})");
    }

    [TestMethod]
    public async Task Sheet_AnchorsToTheBottom_AndKeepsItsFooterVisibleWhileScrolling()
    {
        var page = await OpenTouchAsync(390, 844);

        await OpenSheetAsync(page);

        var panel = page.Locator(".tm-drawer--bottom .tm-drawer__panel");
        var root = page.Locator(".tm-drawer--bottom");
        var rootBox = await root.BoundingBoxAsync();
        var diag = await panel.EvaluateAsync<string>(
            """
            el => {
                const hits = [];
                for (const sheet of document.styleSheets) {
                    let css; try { css = sheet.cssRules; } catch { continue; }
                    for (const rule of css) {
                        if (rule.selectorText && rule.style && el.matches(rule.selectorText) && (rule.style.height || rule.style.flex))
                            hits.push(rule.selectorText + ' { ' + rule.style.cssText + ' }');
                    }
                }
                return hits.join(' || ');
            }
            """);
        var box = await panel.BoundingBoxAsync();
        Assert.IsNotNull(rootBox, diag);
        Assert.IsNotNull(box, "the sheet panel must be on screen");
        Assert.AreEqual(rootBox.Y + rootBox.Height, box.Y + box.Height, 2, diag);
        Assert.AreEqual(390, box.Width, 2, "the sheet must span the viewport width");
        Assert.IsTrue(box.Height <= 844 * 0.85 + 2,
            $"the sheet ({box.Height:F0}px) must cap at 85% of the viewport so the content behind it stays visible");

        await SaveSheetScreenshotAsync(page, "sheet-390-anchored");

        var footer = page.Locator(".tm-drawer__footer--sticky");
        var before = await footer.BoundingBoxAsync();
        Assert.IsNotNull(before, "the sticky footer must be visible");

        await page.Locator(".tm-drawer__body").EvaluateAsync("el => el.scrollTop = el.scrollHeight");
        var after = await footer.BoundingBoxAsync();
        Assert.IsNotNull(after, "the footer must stay visible while the body scrolls");
        Assert.AreEqual(before.Y, after.Y, 2, "scrolling the body must not move the footer");
    }

    [TestMethod]
    public async Task Sheet_FooterStaysAboveTheOnScreenKeyboard()
    {
        var page = await OpenTouchAsync(390, 844);

        // The on-screen keyboard shrinks the visual viewport. Playwright cannot open a real keyboard,
        // so the test installs a shrunken viewport before the sheet opens and the sheet must follow it.
        await page.EvaluateAsync(
            """
            () => {
                const viewport = { height: 430, offsetTop: 0, width: 390 };
                viewport.addEventListener = (type, fn) => { window.__tmViewportResize = fn; };
                viewport.removeEventListener = () => {};
                Object.defineProperty(window, 'visualViewport', { configurable: true, get: () => viewport });
                window.dispatchEvent(new Event('resize'));
            }
            """);
        await OpenSheetAsync(page);

        var panel = page.Locator(".tm-drawer--bottom .tm-drawer__panel");
        var footer = page.Locator(".tm-drawer__footer--sticky");
        var panelBox = await panel.BoundingBoxAsync();
        var box = await footer.BoundingBoxAsync();
        Assert.IsNotNull(panelBox, "the sheet must be on screen with the keyboard open");
        Assert.IsNotNull(box, "the footer must stay on screen with the keyboard open");
        Assert.IsTrue(panelBox.Height < 422,
            $"the sheet ({panelBox.Height:F0}px) must shrink to the visible viewport instead of sliding under the keyboard");
        Assert.IsTrue(box.Y + box.Height <= panelBox.Y + panelBox.Height + 2,
            $"the footer (bottom {box.Y + box.Height:F0}) must stay inside the shrunken sheet (bottom {panelBox.Y + panelBox.Height:F0}, height {panelBox.Height:F0})");

        await SaveSheetScreenshotAsync(page, "sheet-390-keyboard");
    }

    [TestMethod]
    public async Task Sheet_ReturnsFocusToTheTrigger()
    {
        var page = await OpenTouchAsync(390, 844);
        var trigger = page.GetByTestId("open-bottom-sheet");
        await trigger.ScrollIntoViewIfNeededAsync();
        await trigger.FocusAsync();
        await trigger.ClickAsync();
        await page.Locator(".tm-drawer--bottom").WaitForAsync();

        await page.Keyboard.PressAsync("Escape");
        await page.Locator(".tm-drawer--bottom").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });

        var focused = await page.EvaluateAsync<string>("() => document.activeElement?.getAttribute('data-testid') ?? ''");
        Assert.AreEqual("open-bottom-sheet", focused, "closing the sheet must return focus to the trigger that opened it");
    }

    [TestMethod]
    public async Task Sheet_PresentsOnAWideTouchViewport()
    {
        var page = await OpenTouchAsync(1024, 768);
        await OpenSheetAsync(page);

        var widePanel = page.Locator(".tm-drawer--bottom .tm-drawer__panel");
        var box = await widePanel.BoundingBoxAsync();
        Assert.IsNotNull(box);
        var wideDiag = await widePanel.EvaluateAsync<string>(
            """
            el => {
                const hits = [];
                for (const sheet of document.styleSheets) {
                    let css; try { css = sheet.cssRules; } catch { continue; }
                    for (const rule of css) {
                        if (rule.selectorText && rule.style && el.matches(rule.selectorText) && rule.style.position)
                            hits.push((sheet.href || 'inline') + ' :: ' + rule.selectorText + ' { position: ' + rule.style.position + ' }');
                    }
                }
                const root = el.closest('.tm-drawer');
                const rootStyle = root ? getComputedStyle(root) : null;
                return `panel=${getComputedStyle(el).position} root=${rootStyle && rootStyle.position} rootClass=${root && root.className} top=${el.getBoundingClientRect().top} || ` + hits.join(' || ');
            }
            """);
        var rootBox = await page.Locator(".tm-drawer--bottom").BoundingBoxAsync();
        Assert.IsNotNull(rootBox);
        Assert.AreEqual(rootBox.Y + rootBox.Height, box.Y + box.Height, 2,
            "a wide touch viewport still anchors the sheet to the bottom of its root: " + wideDiag);
        // A desktop-width sheet is a panel, not a full-bleed page: 48rem, centered.
        Assert.AreEqual(768, box.Width, 2, "a desktop sheet caps at 48rem");
        Assert.AreEqual(rootBox.X + (rootBox.Width - box.Width) / 2, box.X, 2, "the desktop sheet is centered");

        await SaveSheetScreenshotAsync(page, "sheet-1024-touch");
    }

    /// <summary>
    /// An element screenshot, not a full-page one: a full-page capture resets the touch emulation, so
    /// the coarse-pointer rules would not be what the shot shows.
    /// </summary>
    private static async Task SaveSheetScreenshotAsync(IPage page, string name)
    {
        var dir = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E", "TestResults", "bottom-sheet");
        Directory.CreateDirectory(dir);
        await page.Locator(".tm-drawer--bottom").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(dir, $"{name}.png"),
            Type = ScreenshotType.Png,
            Timeout = 60_000,
        });

        var coarse = await page.EvaluateAsync<bool>("() => window.matchMedia('(pointer: coarse)').matches");
        Assert.IsTrue(coarse, "the touch emulation must still match after the screenshot");
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TempoBlazor.slnx")))
        {
            dir = dir.Parent;
        }

        return dir ?? throw new InvalidOperationException("repo root not found");
    }
}
