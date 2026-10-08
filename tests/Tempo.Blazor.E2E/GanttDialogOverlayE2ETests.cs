using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// F3 review round 4, W2: TmGantt's dialog style block lives in TmGantt.razor.css but the
/// export/import dialogs are separate components — their markup never carries TmGantt's scope
/// attribute, so the plain rules were dead CSS. Rewritten as ::deep rules (the dialogs render
/// inside TmGantt's scoped root), they must now reach the sub-component dialogs: the promoted
/// import overlay paints the full-screen scrim (not the UA popover box), and the export dialog
/// renders styled, not as an unstyled inline block. Both are asserted on COMPUTED styles at a
/// 1440px viewport, the width the reviewer reproduced the defect at.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class GanttDialogOverlayE2ETests : WasmTestBase
{
    private static readonly string ShotDir =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TestResults", "f3-round4");

    private async Task<IPage> OpenGanttAsync(int width, int height = 900)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            IgnoreHTTPSErrors = true,
            Locale = "en-US",
        });
        await context.AddInitScriptAsync(
            """
            localStorage.setItem('tm-demo-culture', 'en');
            document.cookie = 'tm-demo-culture=en;path=/;max-age=31536000;samesite=lax';
            """);
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/gantt", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 90_000 });
        await WaitForAppReadyAsync(page);
        var gantt = page.Locator(".tm-gantt").First;
        await gantt.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        return page;
    }

    /// <summary>
    /// UX round 4 (MAJOR): the trap used to activate while the popover root was still
    /// display:none (the tm-sheet import outlives the trap's rAF retry), so the initial-focus
    /// move dropped and focus landed on body in most opens. The scope now activates only after
    /// promotion — every open at both widths must land on the close button.
    /// </summary>
    [TestMethod]
    [Description("Gantt import dialog: initial focus lands on the close button on every open at 1440 and 390")]
    public async Task Gantt_ImportDialog_InitialFocus_LandsInsideOnEveryOpen()
    {
        foreach (var (width, height) in new[] { (1440, 900), (390, 844) })
        {
            var page = await OpenGanttAsync(width, height);
            for (var open = 1; open <= 6; open++)
            {
                await page.Locator("button[data-testid='gantt-import-btn']").ClickAsync();
                var dialog = page.Locator(".tm-gantt__import-dialog");
                await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

                // Poll briefly: the scope activates after the promote, one interop round later.
                string? activeTestId = null;
                for (var attempt = 0; attempt < 20; attempt++)
                {
                    activeTestId = await page.EvaluateAsync<string>(
                        "() => document.activeElement?.closest('.tm-gantt__import-dialog') !== null"
                            + " ? (document.activeElement.getAttribute('data-testid') ?? document.activeElement.className)"
                            + " : null");
                    if (activeTestId is not null) break;
                    await page.WaitForTimeoutAsync(50);
                }

                Assert.IsNotNull(activeTestId,
                    $"open {open} at {width}: initial focus must be inside the import dialog, not on body");
                StringAssert.Contains(activeTestId, "tm-gantt__dialog-close",
                    $"open {open} at {width}: initial focus must land on the close button");

                await ShootAsync(page, $"gantt-dialog-import-initial-focus-{width}-open{open}");

                // Escape closes through the scope; the overlay unmounts.
                await page.Keyboard.PressAsync("Escape");
                await page.Locator(".tm-gantt__dialog-overlay")
                    .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
            }
        }
    }

    /// <summary>
    /// UX round 4 (MAJOR): unstyled, the promoted dialog painted a white UA popover box while the
    /// page's dark text color inherited in — light-on-white Cancel was invisible. With the dialog
    /// card styled (::deep), the surface is the themed --tm-surface and the Cancel label must
    /// contrast against it.
    /// </summary>
    [TestMethod]
    [Description("Gantt import dialog in dark theme: the Cancel action is readable against the dialog surface")]
    public async Task Gantt_ImportDialog_DarkTheme_CancelIsReadable()
    {
        var page = await OpenGanttAsync(1440, 900);
        await page.EvaluateAsync(
            """
            () => {
                document.documentElement.setAttribute('data-theme', 'dark');
                document.querySelectorAll('[data-theme]').forEach(el => el.setAttribute('data-theme', 'dark'));
            }
            """);

        await page.Locator("button[data-testid='gantt-import-btn']").ClickAsync();
        var dialog = page.Locator(".tm-gantt__import-dialog");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // The card is the themed dark surface, not the white UA popover box.
        var surface = await dialog.EvaluateAsync<string>(
            "el => getComputedStyle(el).backgroundColor");
        Assert.AreNotEqual("rgb(255, 255, 255)", surface,
            "the dark-theme dialog card must not be the white UA popover paint");

        var contrast = await page.EvaluateAsync<double>(
            """
            () => {
                const dialog = document.querySelector('.tm-gantt__import-dialog');
                const buttons = [...dialog.querySelectorAll('button')];
                const cancel = buttons.find(b => (b.textContent ?? '').trim() === 'Cancel');
                if (!cancel) return -1;
                const label = cancel.querySelector('.tm-btn-label') ?? cancel;
                const fg = getComputedStyle(label).color;
                // Walk up to the first opaque background (the dialog card).
                let bg = 'rgba(0, 0, 0, 0)';
                let node = cancel;
                while (node && node !== document.documentElement) {
                    const candidate = getComputedStyle(node).backgroundColor;
                    if (candidate !== 'rgba(0, 0, 0, 0)' && candidate !== 'transparent') { bg = candidate; break; }
                    node = node.parentElement;
                }
                const parse = css => {
                    const m = css.match(/rgba?\(([^)]+)\)/);
                    if (!m) return null;
                    const p = m[1].split(',').map(s => parseFloat(s));
                    return { r: p[0] / 255, g: p[1] / 255, b: p[2] / 255, a: p.length > 3 ? p[3] : 1 };
                };
                const lum = c => {
                    const f = v => v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4);
                    return 0.2126 * f(c.r) + 0.7152 * f(c.g) + 0.0722 * f(c.b);
                };
                const f = parse(getComputedStyle(label).color);
                const b = parse(bg);
                if (!f || !b) return -2;
                const l1 = lum(f), l2 = lum(b);
                return (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05);
            }
            """);
        Assert.IsTrue(contrast >= 4.5,
            $"dark-theme Cancel label must contrast >= 4.5:1 against the dialog surface, got {contrast}");

        await ShootAsync(page, "gantt-dialog-import-dark-1440");
    }

    private static async Task<string> ShootAsync(IPage page, string name)
    {
        Directory.CreateDirectory(ShotDir);
        var path = Path.Combine(ShotDir, $"{name}.png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = false, Timeout = 60_000 });
        return path;
    }

    [TestMethod]
    [Description("Gantt import dialog at 1440: the promoted overlay paints a full-screen scrim and the dialog is styled")]
    public async Task Gantt_ImportDialog_At1440_PaintsScrimAndStyledDialog()
    {
        var page = await OpenGanttAsync(1440, 900);
        await page.Locator("button[data-testid='gantt-import-btn']").ClickAsync();

        var overlay = page.Locator(".tm-gantt__dialog-overlay");
        await overlay.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // The promoted root carries popover="manual" (round 3, V2) — the round-4 fix must style
        // it despite the top-layer promotion and the sub-component scope.
        Assert.AreEqual("manual", await overlay.GetAttributeAsync("popover"),
            "the import overlay root is the promoted popover root");

        var scrim = await overlay.EvaluateAsync<string>(
            "el => { const c = getComputedStyle(el); return [c.position, c.backgroundColor, c.display].join('|'); }");
        var parts = scrim.Split('|');
        Assert.AreEqual("fixed", parts[0], "the scrim is viewport-fixed");
        Assert.AreEqual("rgba(0, 0, 0, 0.45)", parts[1], "the scrim paints the dimmed backdrop");
        Assert.AreEqual("flex", parts[2], "the scrim centres the dialog with flexbox");

        // The scrim covers the whole viewport — the UA [popover] fit-content shrink is reset.
        var box = await overlay.BoundingBoxAsync();
        Assert.IsNotNull(box);
        Assert.AreEqual(1440, (int)Math.Round(box!.Width), "the scrim spans the full viewport width");
        Assert.AreEqual(900, (int)Math.Round(box.Height), "the scrim spans the full viewport height");

        // The dialog itself is the styled card, not a bare UA popover box.
        var dialog = page.Locator(".tm-gantt__import-dialog");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var dialogStyle = await dialog.EvaluateAsync<string>(
            """
            el => {
                const c = getComputedStyle(el);
                return [c.width, c.borderTopWidth, c.borderRadius, c.backgroundColor, c.boxShadow].join('|');
            }
            """);
        var dialogParts = dialogStyle.Split('|');
        Assert.AreEqual("560px", dialogParts[0], "the import dialog width is min(560px, 95vw) at 1440");
        Assert.AreEqual("1px", dialogParts[1], "the dialog card has its border");
        Assert.AreNotEqual("0px", dialogParts[2], "the dialog card has a radius");
        Assert.AreNotEqual("rgba(0, 0, 0, 0)", dialogParts[3], "the dialog card has an opaque surface");
        Assert.AreNotEqual("none", dialogParts[4], "the dialog card has its elevation shadow");

        await ShootAsync(page, "gantt-dialog-import-1440");
    }

    [TestMethod]
    [Description("Gantt export dialog at 1440: overlay scrim and styled dialog card")]
    public async Task Gantt_ExportDialog_At1440_IsStyled()
    {
        var page = await OpenGanttAsync(1440, 900);
        await page.Locator("button[data-testid='gantt-export-btn']").ClickAsync();

        var overlay = page.Locator(".tm-gantt__dialog-overlay");
        await overlay.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var scrim = await overlay.EvaluateAsync<string>(
            "el => { const c = getComputedStyle(el); return [c.position, c.backgroundColor].join('|'); }");
        var parts = scrim.Split('|');
        Assert.AreEqual("fixed", parts[0], "the export scrim is viewport-fixed");
        Assert.AreEqual("rgba(0, 0, 0, 0.45)", parts[1], "the export scrim paints the dimmed backdrop");

        var dialog = page.Locator(".tm-gantt__dialog");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var dialogStyle = await dialog.EvaluateAsync<string>(
            """
            el => {
                const c = getComputedStyle(el);
                return [c.width, c.borderTopWidth, c.borderRadius, c.backgroundColor].join('|');
            }
            """);
        var dialogParts = dialogStyle.Split('|');
        Assert.AreEqual("520px", dialogParts[0], "the export dialog width is min(520px, 95vw) at 1440");
        Assert.AreEqual("1px", dialogParts[1], "the export dialog card has its border");
        Assert.AreNotEqual("0px", dialogParts[2], "the export dialog card has a radius");
        Assert.AreNotEqual("rgba(0, 0, 0, 0)", dialogParts[3], "the export dialog card has an opaque surface");

        // The export format options grid is styled too (selected option paints the primary tint).
        var selectedOption = dialog.Locator(".tm-gantt__export-format-option--selected");
        await Expect(selectedOption).ToBeVisibleAsync();

        await ShootAsync(page, "gantt-dialog-export-1440");
    }

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
