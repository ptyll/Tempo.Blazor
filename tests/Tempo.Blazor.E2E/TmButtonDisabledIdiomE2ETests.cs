using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan b75365fb fáze 4e (remaining e1872ed0, decision AI-PACKAGE-DISABLED-IDIOM extended to the core):
/// a disabled core <c>TmButton</c> speaks the shared disabled idiom in a REAL browser — computed
/// <c>opacity</c> 0.5 from the token, <c>cursor: not-allowed</c>, and NO <c>pointer-events: none</c>.
/// The property used to swallow the cursor itself and let clicks fall through onto the element under
/// the button; the pointer-events half of the contract is what the hit-test, the native <c>title</c>
/// and the forced-colors probes below measure. The anchor form keeps <c>pointer-events: none</c>
/// (bUnit gate <c>ButtonDisabledIdiomTests</c>).
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class TmButtonDisabledIdiomE2ETests : WasmTestBase
{
    private const string Card = "[data-testid='buttons-disabled-idiom-card']";
    private const string DisabledButton = "[data-testid='button-disabled-idiom']";
    private const string ClickCount = "[data-testid='button-disabled-click-count']";
    private const string TitleButton = "[data-testid='button-disabled-title']";
    private const string TooltipButton = "[data-testid='button-disabled-tooltip']";

    [TestMethod]
    public async Task DisabledButton_SpeaksTheSharedDisabledIdiom()
    {
        var page = await OpenDemoPageAsync();
        var button = page.Locator(DisabledButton);

        var styles = await button.EvaluateAsync<string>(
            "el => { const s = getComputedStyle(el); return [s.opacity, s.cursor, s.pointerEvents].join('|'); }");

        var parts = styles.Split('|');
        Assert.AreEqual("0.5", parts[0],
            "a disabled .tm-btn dims through --tm-opacity-disabled (0.5) — the shared idiom token");
        Assert.AreEqual("not-allowed", parts[1],
            "the shared idiom pairs the dim with the not-allowed cursor — pointer-events: none used to "
            + "swallow it, because the element never saw a hover");
        Assert.AreNotEqual("none", parts[2],
            "the button must keep receiving pointer events: the not-allowed cursor, a host title "
            + "tooltip, mouseenter handlers and elementFromPoint all need them, and a click no longer "
            + "falls THROUGH onto the element underneath");
    }

    /// <summary>
    /// The DECISIVE probe of the pointer-events contract: with pointer events restored, the button is
    /// the top-most hit at its own centre; with the pre-fáze-4e <c>pointer-events: none</c> the hit
    /// lands on whatever sits under it. The second probe reads the button carrying a native
    /// <c>title</c> — the tooltip a host gets for free once the button sees the pointer. Mutant:
    /// <c>page.AddStyleTagAsync(".tm-btn:disabled{pointer-events:none}")</c> turns both probes RED.
    /// </summary>
    [TestMethod]
    public async Task DisabledButton_HitTest_ElementFromPointReturnsTheButton()
    {
        var page = await OpenDemoPageAsync();

        var hit = await HitTestAsync(page, DisabledButton);
        Assert.AreEqual("self", hit,
            "elementFromPoint at the centre of a disabled button must return the BUTTON — with "
            + "pointer-events: none it returns the element underneath (the click-through defect)");

        var titleHit = await HitTestAsync(page, TitleButton);
        Assert.AreEqual("self", titleHit,
            "the title-carrying disabled button is hit-testable too — a native tooltip needs the "
            + "pointer to reach the element that carries the title");

        var title = await page.Locator(TitleButton).GetAttributeAsync("title");
        Assert.AreEqual("Unavailable while the document is locked", title,
            "the demo button carries the native title the hit-test proves reachable");
    }

    [TestMethod]
    public async Task DisabledButton_Click_DoesNotFireOnClick()
    {
        var page = await OpenDemoPageAsync();
        var button = page.Locator(DisabledButton);
        var count = page.Locator(ClickCount);

        await Assertions.Expect(count).ToHaveTextAsync("0");

        // Playwright treats a disabled control as not actionable; Force dispatches the real mouse
        // events at the button's coordinates, which is exactly the path a user's pointer takes —
        // the native disabled attribute (and the component's own guard) must refuse the activation.
        await button.ClickAsync(new LocatorClickOptions { Force = true });
        await button.ClickAsync(new LocatorClickOptions { Force = true });

        await Assertions.Expect(count).ToHaveTextAsync("0",
            new LocatorAssertionsToHaveTextOptions { Timeout = 5000 });
    }

    [TestMethod]
    public async Task DisabledButton_Hover_KeepsTheRestingFill()
    {
        var page = await OpenDemoPageAsync();
        var button = page.Locator(DisabledButton);

        var resting = await button.EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");

        await button.HoverAsync(new LocatorHoverOptions { Force = true });
        await page.WaitForTimeoutAsync(300); // past the --tm-transition-fast background transition

        var hovered = await button.EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");
        Assert.AreEqual(resting, hovered,
            "hovering a disabled button must not light up the fill — every variant guards with "
            + ":hover:not(:disabled)");

        var cursor = await button.EvaluateAsync<string>("el => getComputedStyle(el).cursor");
        Assert.AreEqual("not-allowed", cursor,
            "with pointer events restored, the not-allowed cursor is what a hovering user actually sees");
    }

    /// <summary>
    /// REGRESSION PROTECTION, not the phase's benefit: a <c>TmTooltip</c> around a disabled button
    /// worked BEFORE fáze 4e as well — its hover lives on the <c>.tm-tooltip</c> WRAPPER, which always
    /// received pointer events. The phase's gains are the cursor, the native <c>title</c>,
    /// <c>mouseenter</c> handlers and <c>elementFromPoint</c> ON the button itself (the hit-test and
    /// computed-style specs above). This spec only pins that restoring pointer events on the button
    /// did not break the wrapper's tooltip.
    /// </summary>
    [TestMethod]
    public async Task DisabledButton_InsideTooltip_TooltipKeepsWorking()
    {
        var page = await OpenDemoPageAsync();
        var button = page.Locator(TooltipButton);
        var tooltip = page.Locator(".tm-tooltip__content", new PageLocatorOptions { HasText = "unavailable" });

        // The tooltip is CSS-only and hides through opacity (Playwright would still call that
        // "visible"), so the show/hide contract is read from the computed opacity.
        var resting = await tooltip.EvaluateAsync<string>("el => getComputedStyle(el).opacity");
        Assert.AreEqual("0", resting, "the tooltip starts hidden");

        await button.HoverAsync(new LocatorHoverOptions { Force = true });
        await page.WaitForTimeoutAsync(300); // past the 0.15 s opacity transition

        var shown = await tooltip.EvaluateAsync<string>("el => getComputedStyle(el).opacity");
        Assert.AreEqual("1", shown,
            "the tooltip keeps working — its hover always lived on the wrapper; this is regression "
            + "protection for the pointer-events change, not the phase's benefit");
    }

    /// <summary>
    /// Forced colors (Windows high contrast): the UA forces its own palette, so the shared dim would
    /// only lower the contrast the mode guarantees — the system <c>GrayText</c> carries the disabled
    /// state at FULL opacity instead. The probe span carries <c>color: GrayText</c> inline, so the
    /// assertion compares the button against the system colour itself, not a guessed palette value.
    /// </summary>
    [TestMethod]
    public async Task DisabledButton_ForcedColors_PaintsTheSystemGrayTextAtFullOpacity()
    {
        var page = await OpenDemoPageAsync();
        await page.EmulateMediaAsync(new PageEmulateMediaOptions { ForcedColors = ForcedColors.Active });
        // The button transitions opacity/color (--tm-transition-fast); let the forced-colors flip settle
        // before reading computed values, or the probe catches the transition mid-flight.
        await page.WaitForTimeoutAsync(400);

        var probe = await ReadSystemDisabledColorAsync(page, DisabledButton);
        Assert.AreEqual(probe.System, probe.Element,
            "in forced colors the disabled button paints the system GrayText (the forced-colors block "
            + "at the end of _button.css wins the (0,2,0) tie against the variant ink by source order)");

        var opacity = await page.Locator(DisabledButton)
            .EvaluateAsync<string>("el => getComputedStyle(el).opacity");
        Assert.AreEqual("1", opacity,
            "forced colors drop the resting dim — opacity over GrayText would only lower the contrast "
            + "the system itself guarantees");

        // The demo shell's sticky top bar floats above the page content and bleeds into element
        // captures; hide it for the deterministic shot.
        await page.AddStyleTagAsync(new PageAddStyleTagOptions
        {
            Content = ".sticky.top-0{visibility:hidden !important}",
        });
        await SaveCardScreenshotAsync(page, page.Locator(Card), "buttons-disabled/idiom-forced-colors-1440");
    }

    [TestMethod]
    public async Task DisabledButton_Screenshots_LightDarkAndTooltip()
    {
        var page = await OpenDemoPageAsync();
        var card = page.Locator(Card);

        // The demo shell's sticky top bar floats above the page content and bleeds into element
        // captures; hide it for the deterministic shots.
        await page.AddStyleTagAsync(new PageAddStyleTagOptions
        {
            Content = ".sticky.top-0{visibility:hidden !important}",
        });

        await SaveCardScreenshotAsync(page, card, "buttons-disabled/idiom-light-1440");

        // The tooltip shot replaces the old hover shot: the cursor itself is never painted into a
        // screenshot, so hovering the PLAIN disabled button documented nothing. The tooltip opening
        // above the button is a visible, meaningful hover state — the clip reaches ~60 px above the
        // card so the Top-positioned tooltip is in frame.
        await SaveTooltipScreenshotAsync(page, card, "buttons-disabled/idiom-tooltip-light-1440");

        await page.Mouse.MoveAsync(0, 0); // leave the hover before the resting dark shot
        await SetDarkAsync(page);
        await SaveCardScreenshotAsync(page, card, "buttons-disabled/idiom-dark-1440");

        await SaveTooltipScreenshotAsync(page, card, "buttons-disabled/idiom-tooltip-dark-1440");
    }

    private async Task<IPage> OpenDemoPageAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 900);
        await GotoAppReadyAsync(page, $"{BaseUrl}/buttons", WaitUntilState.DOMContentLoaded);
        await page.WaitForSelectorAsync(Card, new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000,
        });
        return page;
    }

    /// <summary>"self" when <c>document.elementFromPoint</c> at the element's centre returns the
    /// element or one of its descendants (Chromium hit-tests the <c>display:contents</c> label span
    /// for a text hit, so "inside the button" — not literally the button node — is the contract),
    /// otherwise a description of what actually sits on top (for the failure message).
    /// The element is scrolled into the viewport first — <c>elementFromPoint</c> reads VIEWPORT
    /// coordinates and returns null for a point outside them.</summary>
    private static Task<string> HitTestAsync(IPage page, string selector)
        => page.EvaluateAsync<string>(
            """
            (selector) => {
                const el = document.querySelector(selector);
                // behavior:'instant' — a CSS scroll-behavior:smooth scroller would ANIMATE the scroll
                // and the rect read right after would still be the pre-scroll (off-viewport) position.
                el.scrollIntoView({ block: 'center', inline: 'center', behavior: 'instant' });
                const rect = el.getBoundingClientRect();
                const hit = document.elementFromPoint(
                    rect.left + rect.width / 2, rect.top + rect.height / 2);
                return hit && (hit === el || el.contains(hit))
                    ? 'self'
                    : (hit ? hit.tagName.toLowerCase() + '.' + hit.className : 'null');
            }
            """, selector);

    /// <summary>
    /// The element's computed colour next to an injected <c>span{color:GrayText}</c> probe: the mode
    /// does not remap system colours, so the probe reads the real system value to compare against.
    /// </summary>
    private static async Task<(string Element, string System)> ReadSystemDisabledColorAsync(
        IPage page, string selector)
    {
        var json = await page.EvaluateAsync<string>(
            """
            (selector) => {
                const el = document.querySelector(selector);
                const probe = document.createElement('span');
                probe.style.color = 'GrayText';
                el.parentElement.appendChild(probe);
                const system = getComputedStyle(probe).color;
                probe.remove();
                return JSON.stringify({ element: getComputedStyle(el).color, system });
            }
            """, selector);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        return (
            document.RootElement.GetProperty("element").GetString() ?? "",
            document.RootElement.GetProperty("system").GetString() ?? "");
    }

    private static async Task SetDarkAsync(IPage page)
    {
        var toggle = page.Locator("[data-testid='theme-toggle']:visible").First;
        if (await toggle.GetAttributeAsync("aria-pressed") != "true")
        {
            await toggle.ClickAsync();
        }

        await Assertions.Expect(page.Locator(".demo-shell"))
            .ToHaveAttributeAsync("data-theme", "dark", new LocatorAssertionsToHaveAttributeOptions { Timeout = 15000 });
    }

    private async Task SaveCardScreenshotAsync(IPage page, ILocator card, string fileName)
    {
        var path = ScreenshotPath(fileName);
        await card.ScreenshotAsync(new LocatorScreenshotOptions { Path = path });
        TestContext.AddResultFile(path);
    }

    /// <summary>
    /// Hovers the tooltip button, waits out the opacity transition and captures the card with a
    /// ~60 px reserve ABOVE it, so the Top-positioned tooltip is inside the clip.
    /// </summary>
    private async Task SaveTooltipScreenshotAsync(IPage page, ILocator card, string fileName)
    {
        await page.Locator(TooltipButton).HoverAsync(new LocatorHoverOptions { Force = true });
        await page.WaitForTimeoutAsync(300); // past the 0.15 s tooltip opacity transition

        var box = await card.BoundingBoxAsync();
        Assert.IsNotNull(box, "the disabled-idiom card must have a bounding box to clip against");
        var reserve = Math.Min(60, box.Y);
        var path = ScreenshotPath(fileName);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = path,
            Clip = new Clip
            {
                X = box.X,
                Y = box.Y - reserve,
                Width = box.Width,
                Height = box.Height + reserve,
            },
        });
        TestContext.AddResultFile(path);
    }

    private static string ScreenshotPath(string fileName)
    {
        var dir = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__");
        Directory.CreateDirectory(Path.Combine(dir, Path.GetDirectoryName(fileName)!));
        return Path.Combine(dir, $"{fileName}.png");
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx"))) return directory;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Repository root was not found.");
    }
}
