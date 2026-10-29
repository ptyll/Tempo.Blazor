using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// The Gantt export / import / mapping dialogs are the core <c>TmModal</c> (TmGantt redesign, phase 6, G6) - no
/// dialog paints its own backdrop. What matters in a browser, asserted on COMPUTED styles and the real focus: the
/// promoted overlay is the full-screen scrim, the card is the themed surface, initial focus lands inside on EVERY
/// open, Escape closes it and hands the focus back to the toolbar button that opened it.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class GanttDialogOverlayE2ETests : WasmTestBase
{
    private static readonly string ShotDir =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TestResults", "gantt-dialogs");

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
    /// The trap used to activate while the popover root was still display:none, so the initial-focus move dropped and
    /// focus landed on body in most opens. TmModal activates only after promotion - every open at both widths must
    /// land inside the dialog (the first focusable of a TmModal is its header close button).
    /// </summary>
    [TestMethod]
    [Description("Gantt import dialog: initial focus lands inside the dialog on every open at 1440; at 390 the read-mostly container offers no import at all (phase 8, G2)")]
    public async Task Gantt_ImportDialog_InitialFocus_LandsInsideOnEveryOpen()
    {
        foreach (var (width, height) in new[] { (1440, 900), (390, 844) })
        {
            var page = await OpenGanttAsync(width, height);

            // Mobile read-mostly (phase 8, G2): the import command is not offered anywhere on the mobile
            // chrome — the legacy "opens at 390" leg became the absence assertion.
            if (width == 390)
            {
                Assert.AreEqual(0, await page.Locator("button[data-testid='gantt-import-btn']").CountAsync(),
                    "import is tablet+; the mobile chrome offers no import command");
                continue;
            }

            for (var open = 1; open <= 6; open++)
            {
                await page.Locator("button[data-testid='gantt-import-btn']").ClickAsync();
                var dialog = page.Locator(".tm-modal-overlay.tm-gantt__import-dialog");
                await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

                // Poll briefly: the scope activates after the promote, one interop round later.
                var inside = false;
                for (var attempt = 0; attempt < 20 && !inside; attempt++)
                {
                    inside = await page.EvaluateAsync<bool>(
                        "() => document.activeElement?.closest('.tm-gantt__import-dialog') != null");
                    if (!inside) await page.WaitForTimeoutAsync(50);
                }

                Assert.IsTrue(inside, $"open {open} at {width}: initial focus must be inside the import dialog, not on body");
                await ShootAsync(page, $"gantt-dialog-import-initial-focus-{width}-open{open}");

                // Escape closes through the scope; the overlay unmounts.
                await page.Keyboard.PressAsync("Escape");
                await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
            }
        }
    }

    [TestMethod]
    [Description("Gantt export/import dialogs: Escape closes and the focus returns to the toolbar button that opened it")]
    [DataRow("gantt-export-btn", "tm-gantt__export-dialog")]
    [DataRow("gantt-import-btn", "tm-gantt__import-dialog")]
    public async Task Gantt_Dialogs_Escape_Closes_And_Returns_Focus_To_The_Trigger(string trigger, string dialogClass)
    {
        var page = await OpenGanttAsync(1440);
        var button = page.Locator($"button[data-testid='{trigger}']");
        await button.FocusAsync();
        await page.Keyboard.PressAsync("Enter");

        var dialog = page.Locator($".tm-modal-overlay.{dialogClass}");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await page.Keyboard.PressAsync("Escape");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });

        await Assertions.Expect(button).ToBeFocusedAsync();
    }

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
        var card = page.Locator(".tm-gantt__import-dialog .tm-modal");
        await card.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var surface = await card.EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");
        Assert.AreNotEqual("rgb(255, 255, 255)", surface, "the dark-theme dialog card must not be the white UA popover paint");

        var contrast = await page.EvaluateAsync<double>(
            """
            () => {
                const dialog = document.querySelector('.tm-gantt__import-dialog');
                const cancel = [...dialog.querySelectorAll('button')].find(b => (b.textContent ?? '').trim() === 'Cancel');
                if (!cancel) return -1;
                const label = cancel.querySelector('.tm-btn-label') ?? cancel;
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
                    return { r: p[0] / 255, g: p[1] / 255, b: p[2] / 255 };
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
        Assert.IsTrue(contrast >= 4.5, $"dark-theme Cancel label must contrast >= 4.5:1 against the dialog surface, got {contrast}");

        await ShootAsync(page, "gantt-dialog-import-dark-1440");
    }

    private static async Task ShootAsync(IPage page, string name)
    {
        Directory.CreateDirectory(ShotDir);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(ShotDir, $"{name}.png"), FullPage = false, Timeout = 60_000 });
    }

    [TestMethod]
    [Description("Gantt import dialog at 1440: the promoted overlay paints a full-screen scrim and the dialog is the themed card")]
    public async Task Gantt_ImportDialog_At1440_PaintsScrimAndStyledDialog()
    {
        var page = await OpenGanttAsync(1440, 900);
        await page.Locator("button[data-testid='gantt-import-btn']").ClickAsync();

        var overlay = page.Locator(".tm-modal-overlay.tm-gantt__import-dialog");
        await overlay.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        Assert.AreEqual("manual", await overlay.GetAttributeAsync("popover"), "the import overlay root is the promoted popover root");
        Assert.AreEqual(0, await page.Locator(".tm-gantt__dialog-overlay").CountAsync(), "no dialog paints its own overlay any more");

        var scrim = (await overlay.EvaluateAsync<string>("el => { const c = getComputedStyle(el); return [c.position, c.display].join('|'); }")).Split('|');
        Assert.AreEqual("fixed", scrim[0], "the scrim is viewport-fixed");
        Assert.AreEqual("flex", scrim[1], "the scrim centres the dialog with flexbox");

        var box = await overlay.BoundingBoxAsync();
        Assert.IsNotNull(box);
        Assert.AreEqual(1440, (int)Math.Round(box!.Width), "the scrim spans the full viewport width");
        Assert.AreEqual(900, (int)Math.Round(box.Height), "the scrim spans the full viewport height");

        var card = overlay.Locator(".tm-modal");
        var parts = (await card.EvaluateAsync<string>(
            "el => { const c = getComputedStyle(el); return [c.borderRadius, c.backgroundColor, c.boxShadow].join('|'); }")).Split('|');
        Assert.AreNotEqual("0px", parts[0], "the dialog card has a radius");
        Assert.AreNotEqual("rgba(0, 0, 0, 0)", parts[1], "the dialog card has an opaque surface");
        Assert.AreNotEqual("none", parts[2], "the dialog card has its elevation shadow");

        await ShootAsync(page, "gantt-dialog-import-1440");
    }

    [TestMethod]
    [Description("Gantt export dialog at 1440: TmModal card with the format tiles as a styled radio group")]
    public async Task Gantt_ExportDialog_At1440_IsStyled()
    {
        var page = await OpenGanttAsync(1440, 900);
        await page.Locator("button[data-testid='gantt-export-btn']").ClickAsync();

        var overlay = page.Locator(".tm-modal-overlay.tm-gantt__export-dialog");
        await overlay.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var selected = overlay.Locator(".tm-gantt__export-format-option--selected");
        await Assertions.Expect(selected).ToBeVisibleAsync();
        var tile = await selected.BoundingBoxAsync();
        Assert.IsNotNull(tile);
        Assert.IsTrue(tile!.Width >= 120 && tile.Height >= 96, $"a format tile is at least 120 x 96, got {tile.Width} x {tile.Height}");
        Assert.AreEqual("radiogroup", await overlay.Locator(".tm-gantt__export-formats").GetAttributeAsync("role"));

        await ShootAsync(page, "gantt-dialog-export-1440");
    }
}
