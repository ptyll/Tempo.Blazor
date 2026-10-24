using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan 643bba06 phase 8 — the e-mail editor UX closing items, on the REAL demo:
/// the phone palette shows a whole first tile row above the action bar, the editor's status bar ("Saved") is
/// above the fold at 1440x900, the narrow table grid drops the decorative row caption (the aria-label stays),
/// the import preview no longer stops at 100 rows (virtualized), the demo's chrome speaks the page language and
/// the send page's form is not stretched to the preview's height.
/// Screenshots land in <c>__screenshots__/email-templates/p8-*.png</c> (390 / 1024 / 1440, light and dark).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class EmailEditorUxPhase8E2ETests : EmailTemplateE2ETestBase
{
    private const string WelcomeRoute = "/email-templates/edit/11111111-1111-1111-1111-111111111111";
    /// <summary>The seeded order template: it carries a real table block (rows with a header row).</summary>
    private const string OrderRoute = "/email-templates/edit/33333333-3333-3333-3333-333333333333";

    private async Task<IPage> OpenEditorAsync(int width, int height, bool touch, bool pinDesktop, string route = WelcomeRoute)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = touch,
            IsMobile = touch,
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);

        var page = await context.NewPageAsync();
        await page.AddInitScriptAsync(
            "try { localStorage.setItem('tm-demo-editor-layout', '" + (pinDesktop ? "Desktop" : "Auto") + "'); } catch (e) { }");
        await GotoAppReadyAsync(page, $"{BaseUrl}{route}");
        await page.WaitForSelectorAsync("[data-tm-email-editor]", new() { Timeout = 60000 });
        await page.WaitForSelectorAsync(".tm-editor-shell[data-layout]", new() { Timeout = 30000 });
        await page.WaitForSelectorAsync("[data-tm-canvas-doc]", new() { Timeout = 30000 });
        return page;
    }

    /// <summary>Waits until an element stops moving (the bottom sheet animates to its snap before it is measured or photographed).</summary>
    private static async Task WaitForStableTopAsync(IPage page, string selector)
    {
        await page.WaitForFunctionAsync(
            @"sel => new Promise(resolve => {
                const el = document.querySelector(sel);
                if (!el) return resolve(false);
                let last = el.getBoundingClientRect().top, stable = 0;
                const tick = () => {
                    const now = el.getBoundingClientRect().top;
                    stable = Math.abs(now - last) < 0.5 ? stable + 1 : 0;
                    last = now;
                    if (stable >= 8) resolve(true); else requestAnimationFrame(tick);
                };
                requestAnimationFrame(tick);
            })",
            selector, new PageWaitForFunctionOptions { Timeout = 15000 });
    }
    private static async Task GoDarkAsync(IPage page)
    {
        var clicked = await page.EvaluateAsync<bool>(
            "() => { const b = document.querySelector(`[data-testid='theme-toggle']`); if (!b) return false; b.click(); return true; }");
        clicked.Should().BeTrue("the demo host renders the theme toggle");
        await page.WaitForSelectorAsync("[data-theme='dark']", new() { Timeout = 15000 });
        await page.WaitForTimeoutAsync(300);
    }

    // ── Phone palette ───────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task P8_1_Phone390_FirstPaletteRow_IsVisibleAndReachableAboveTheActionBar()
    {
        var page = await OpenEditorAsync(390, 844, touch: true, pinDesktop: false);
        await page.WaitForSelectorAsync(".tm-editor-shell[data-layout='mobile']", new() { Timeout = 30000 });

        var tiles = page.Locator(".tm-editor-shell__sheet [data-tm-toolbox-list] [data-tm-block]");
        (await tiles.CountAsync()).Should().BeGreaterThanOrEqualTo(4);

        await WaitForStableTopAsync(page, ".tm-editor-shell__sheet [data-tm-toolbox-list] [data-tm-block]");
        // Four tiles share the FIRST row (the 4 narrow-tile grid), and all four sit above the action bar.
        var report = await page.EvaluateAsync<System.Text.Json.JsonElement>(
            @"() => {
                const tiles = [...document.querySelectorAll('.tm-editor-shell__sheet [data-tm-toolbox-list] [data-tm-block]')];
                const bar = document.querySelector('.tm-mobile-action-bar');
                const barTop = bar ? bar.getBoundingClientRect().top : window.innerHeight;
                const first = tiles.slice(0, 4).map(t => {
                    const r = t.getBoundingClientRect();
                    const hit = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
                    return { top: Math.round(r.top), bottom: Math.round(r.bottom), width: Math.round(r.width),
                             reachable: !!hit && (hit === t || t.contains(hit)) && r.top >= 0 && r.bottom <= barTop };
                });
                return { barTop: Math.round(barTop), first };
            }");
        var first = report.GetProperty("first").EnumerateArray().ToList();
        first.Should().HaveCount(4);
        first.Select(t => t.GetProperty("top").GetInt32()).Distinct().Should().HaveCount(1,
            "the first four tiles share one row on a phone");
        first.Should().OnlyContain(t => t.GetProperty("reachable").GetBoolean(),
            "every tile of the first row is hit-testable above the action bar");
        first.Should().OnlyContain(t => t.GetProperty("width").GetInt32() >= 56,
            "four tiles per row must still leave a readable, tappable tile");
        await SaveNamedViewportScreenshotAsync(page, "p8-editor-mobile-390.png");
    }

    [TestMethod]
    public async Task P8_2_Phone320_PaletteFallsBackToThreeColumns_WithoutOverflow()
    {
        var page = await OpenEditorAsync(320, 720, touch: true, pinDesktop: false);
        await page.WaitForSelectorAsync(".tm-editor-shell[data-layout='mobile']", new() { Timeout = 30000 });

        var columns = await page.EvaluateAsync<int>(
            @"() => {
                const tiles = [...document.querySelectorAll('.tm-editor-shell__sheet [data-tm-toolbox-list] [data-tm-block]')];
                if (tiles.length === 0) return -1;
                const top = Math.round(tiles[0].getBoundingClientRect().top);
                return tiles.filter(t => Math.round(t.getBoundingClientRect().top) === top).length;
            }");
        columns.Should().BeInRange(3, 4, "a 320 px sheet packs three or four tiles, never five squeezed ones");
        (await page.EvaluateAsync<int>(
            "() => { const el = document.querySelector('[data-tm-email-editor]'); return Math.round(el.scrollWidth - el.clientWidth); }"))
            .Should().BeLessThanOrEqualTo(0);
    }

    // ── Status bar above the fold ───────────────────────────────────────────────────────────

    [TestMethod]
    public async Task P8_3_Desktop1440x900_StatusBarAndSavedBadge_AreAboveTheFold()
    {
        var page = await OpenEditorAsync(1440, 900, touch: false, pinDesktop: true);
        await page.WaitForSelectorAsync(".tm-editor-shell[data-layout='desktop']", new() { Timeout = 30000 });

        await page.Locator("[data-region='toolbar'] [data-tm-save]").First.ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-save-badge]", new() { Timeout = 20000 });
        await SaveNamedViewportScreenshotAsync(page, "p8-editor-desktop-1440.png");

        var bottoms = await page.EvaluateAsync<System.Text.Json.JsonElement>(
            @"() => {
                const bar = document.querySelector('[data-region=""status-bar""]');
                const badge = document.querySelector('[data-tm-save-badge]');
                const r = el => el ? Math.round(el.getBoundingClientRect().bottom) : -1;
                return { viewport: window.innerHeight, bar: r(bar), badge: r(badge) };
            }");
        var viewport = bottoms.GetProperty("viewport").GetInt32();
        bottoms.GetProperty("bar").GetInt32().Should().BeInRange(1, viewport,
            "the status bar sits above the fold, so a user sees whether the save worked without scrolling the page");
        bottoms.GetProperty("badge").GetInt32().Should().BeInRange(1, viewport);
    }

    [TestMethod]
    public async Task P8_4_Tablet1024_StatusBar_IsAboveTheFold()
    {
        var page = await OpenEditorAsync(1024, 768, touch: true, pinDesktop: false);
        await page.WaitForSelectorAsync(".tm-editor-shell[data-layout]", new() { Timeout = 30000 });
        var bar = await page.EvaluateAsync<int>(
            "() => { const el = document.querySelector('[data-region=\"status-bar\"]'); return el ? Math.round(el.getBoundingClientRect().bottom) : -1; }");
        bar.Should().BeInRange(1, 768);
        await SaveNamedViewportScreenshotAsync(page, "p8-editor-tablet-1024.png");
    }

    // ── Dark themes ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task P8_5_Dark_Desktop1440_And_Phone390_AreReadable()
    {
        var desktop = await OpenEditorAsync(1440, 900, touch: false, pinDesktop: true);
        await GoDarkAsync(desktop);
        await SaveNamedViewportScreenshotAsync(desktop, "p8-editor-desktop-dark-1440.png");
        var surface = await desktop.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('.tm-editor-shell')).backgroundColor");
        surface.Should().NotBe("rgb(255, 255, 255)", "the shell follows the dark theme");

        var phone = await OpenEditorAsync(390, 844, touch: true, pinDesktop: false);
        await phone.WaitForSelectorAsync(".tm-editor-shell[data-layout='mobile']", new() { Timeout = 30000 });
        await GoDarkAsync(phone);
        await SaveNamedViewportScreenshotAsync(phone, "p8-editor-mobile-dark-390.png");
    }

    // ── Table row caption ───────────────────────────────────────────────────────────────────

    private static async Task<IPage> OpenTableInspectorAsync(IPage page)
    {
        await page.Locator(".tm-email-canvas-block[data-tm-block-kind='table']").First.DispatchEventAsync("click");
        await page.WaitForSelectorAsync("[data-tm-table-editor]", new() { Timeout = 15000 });
        return page;
    }

    private async Task<IPage> OpenWithTableAsync(int width, int height, bool touch, bool pinDesktop)
    {
        var page = await OpenEditorAsync(width, height, touch, pinDesktop, OrderRoute);
        if (touch) await page.WaitForSelectorAsync(".tm-editor-shell[data-layout='mobile']", new() { Timeout = 30000 });
        await page.WaitForSelectorAsync(".tm-email-canvas-block[data-tm-block-kind='table']", new() { Timeout = 30000 });
        await OpenTableInspectorAsync(page);
        await page.WaitForSelectorAsync("[data-tm-table-rowheader-text]", new() { State = WaitForSelectorState.Attached, Timeout = 15000 });
        return page;
    }
    [TestMethod]
    public async Task P8_6_Phone390_TableRowCaption_IsHidden_ButTheSwitchKeepsItsAriaLabel()
    {
        var page = await OpenWithTableAsync(390, 844, touch: true, pinDesktop: false);

        var info = await page.EvaluateAsync<System.Text.Json.JsonElement>(
            @"() => {
                const cap = document.querySelector('[data-tm-table-rowheader-text]');
                const input = document.querySelector('[data-tm-table-rowctl] input[type=checkbox]');
                const ctl = document.querySelector('[data-tm-table-rowctl]');
                return {
                    captionDisplay: cap ? getComputedStyle(cap).display : 'missing',
                    aria: input ? input.getAttribute('aria-label') : null,
                    ctlWidth: ctl ? Math.round(ctl.getBoundingClientRect().width) : -1,
                    editorWidth: Math.round(document.querySelector('[data-tm-table-editor]').getBoundingClientRect().width)
                };
            }");
        info.GetProperty("editorWidth").GetInt32().Should().BeLessThan(384, "a phone sheet is a narrow table editor");
        info.GetProperty("captionDisplay").GetString().Should().Be("none", "the narrow container drops the caption");
        info.GetProperty("aria").GetString().Should().NotBeNullOrWhiteSpace("the switch is still named for assistive tech");
        info.GetProperty("ctlWidth").GetInt32().Should().BeInRange(1, 96);
    }

    [TestMethod]
    public async Task P8_7_Desktop_TableRowCaption_IsShownInAWideEnoughInspector()
    {
        var page = await OpenWithTableAsync(1440, 900, touch: false, pinDesktop: true);
        // Widen the grid's own container (a wide inspector / a host with room): the caption comes back.
        await page.EvaluateAsync(
            "() => { const e = document.querySelector('[data-tm-table-editor]'); e.style.inlineSize = '640px'; e.style.maxInlineSize = 'none'; }");
        await page.WaitForTimeoutAsync(200);
        var display = await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('[data-tm-table-rowheader-text]')).display");
        display.Should().NotBe("none", "a grid wider than the narrow threshold keeps the caption");
    }

    // ── Import preview > 100 blocks ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task P8_8_Import_With300Blocks_ListsEveryRowVirtualized_AndCountsAreComplete()
    {
        var page = await OpenEditorAsync(1440, 900, touch: false, pinDesktop: true);
        var mjml = "<mjml><mj-body><mj-section><mj-column>"
            + string.Concat(Enumerable.Range(0, 300).Select(i => $"<mj-text>Row {i}</mj-text>"))
            + "</mj-column></mj-section></mj-body></mjml>";

        await page.ClickAsync("[data-tm-import-btn]");
        await page.WaitForSelectorAsync("#tm-import-input", new() { Timeout = 15000 });
        await page.Locator("#tm-import-input").FillAsync(mjml);
        await page.WaitForSelectorAsync("[data-tm-import-summary][data-tm-import-state]", new() { Timeout = 15000 });

        (await page.GetAttributeAsync("[data-tm-import-summary]", "data-tm-import-blocks")).Should().Be("300");
        (await page.GetAttributeAsync("[data-tm-import-tree]", "data-tm-import-rows")).Should().Be("302");
        (await page.Locator("[data-tm-import-structure-more]").CountAsync()).Should().Be(0, "nothing is truncated any more");

        var rendered = await page.Locator("[data-tm-import-block]").CountAsync();
        rendered.Should().BeInRange(1, 120, "a virtualized list renders a window of rows, not all 300");
        // ...and they are really PAINTED inside the list's box (an earlier flex layout collapsed the spacers and the list was blank).
        var visible = await page.EvaluateAsync<int>(
            @"() => { const t = document.querySelector('[data-tm-import-tree]').getBoundingClientRect();
                return [...document.querySelectorAll('[data-tm-import-block]')].filter(e => {
                    const r = e.getBoundingClientRect(); return r.height > 0 && r.top >= t.top - 1 && r.bottom <= t.bottom + 1; }).length; }");
        visible.Should().BeGreaterThanOrEqualTo(5, "the first rows are visible in the list viewport");
        var scrollable = await page.EvaluateAsync<bool>(
            "() => { const t = document.querySelector('[data-tm-import-tree]'); return t.scrollHeight > t.clientHeight * 3; }");
        scrollable.Should().BeTrue("300 fixed-height rows make a long scroll extent");

        // Scrolling to the end brings the LAST row into the DOM.
        await page.EvaluateAsync("() => { const t = document.querySelector('[data-tm-import-tree]'); t.scrollTop = t.scrollHeight; }");
        await page.WaitForFunctionAsync(
            "() => [...document.querySelectorAll('[data-tm-import-block]')].some(e => e.getAttribute('aria-posinset') === '302')",
            null, new PageWaitForFunctionOptions { Timeout = 15000 });
        await SaveNamedViewportScreenshotAsync(page, "p8-import-300-blocks-1440.png");
    }

    // ── Localized demo chrome ───────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task P8_9_DemoChrome_FollowsTheLanguage_Czech()
    {
        var page = await OpenEditorAsync(1440, 900, touch: false, pinDesktop: true);
        await SwitchLanguageAsync(page, "cs");
        await page.WaitForSelectorAsync("[data-tm-email-editor]", new() { Timeout = 30000 });
        await page.WaitForSelectorAsync("[data-tm-demo-bar]", new() { Timeout = 15000 });

        var bar = await page.Locator("[data-tm-demo-bar]").InnerTextAsync();
        bar.Should().Contain("Jen pro čtení").And.Contain("Výsledek uložení hostitelem")
            .And.Contain("Hostitel: neuložené změny ne");
        bar.Should().NotContain("Host save result").And.NotContain("Read-only");
        var options = await page.Locator("[data-tm-demo-simulate] option").AllInnerTextsAsync();
        options.Should().Contain("Volat API").And.NotContain("Call the API");
        await SaveNamedViewportScreenshotAsync(page, "p8-editor-desktop-cs-1440.png");
    }

    // ── Send page ───────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task P8_10_SendPage_Desktop_FormIsNotStretchedToThePreviewHeight()
    {
        var page = await OpenAsync($"/email-templates/send/{OrderTemplateId}", 1440, 900);
        await page.WaitForSelectorAsync("[data-tm-send-form]", new() { Timeout = 30000 });
        await page.WaitForSelectorAsync("[data-tm-send-preview]", new() { Timeout = 30000 });

        var info = await page.EvaluateAsync<System.Text.Json.JsonElement>(
            @"() => {
                const form = document.querySelector('[data-tm-send-form]');
                const prev = document.querySelector('[data-tm-send-preview]');
                const fr = form.getBoundingClientRect(), pr = prev.getBoundingClientRect();
                const children = [...form.children];
                const content = children.length ? Math.max(...children.map(c => c.getBoundingClientRect().bottom)) - fr.top : 0;
                return { formH: Math.round(fr.height), previewH: Math.round(pr.height), contentH: Math.round(content),
                         alignSelf: getComputedStyle(form).alignSelf, side: fr.right <= pr.left + 1 };
            }");
        info.GetProperty("side").GetBoolean().Should().BeTrue("the desktop grid puts the form beside the preview");
        info.GetProperty("alignSelf").GetString().Should().Be("start");
        info.GetProperty("formH").GetInt32().Should().BeLessThanOrEqualTo(info.GetProperty("contentH").GetInt32() + 40,
            "the form hugs its fields instead of stretching to the preview's height");
        await SaveNamedViewportScreenshotAsync(page, "p8-send-desktop-1440.png");
    }
}
