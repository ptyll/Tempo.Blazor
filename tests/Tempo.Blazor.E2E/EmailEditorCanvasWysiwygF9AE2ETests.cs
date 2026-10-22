using FluentAssertions;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 9A — the WYSIWYG canvas in a real browser: the document is painted as the e-mail it will become
/// (typography, a real button pill, the paper width), selection paints the indigo frame with its action bar,
/// a click on the canvas never navigates, inserting from the palette produces a rendered block, the
/// placeholder image mode issues no request, and a keystroke repaints ONLY the edited block.
/// <para>
/// Screenshots land in <c>__screenshots__/email-templates/f9a-canvas-*.png</c> and are the UX review
/// material for this phase, compared against
/// <c>output/imagegen/tempo-reimagined/Tempo.Blazor.EmailTemplates/email-template-editor--{desktop|tablet|mobile}.png</c>.
/// </para>
/// <para>
/// The re-render measurement is the one thing bUnit cannot prove: its test renderer re-renders the whole
/// tree and skips <c>ShouldRender</c> (measured while writing this phase), so the production behaviour is
/// observed here with a <c>MutationObserver</c> on the canvas while the test types into the inspector.
/// </para>
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class EmailEditorCanvasWysiwygF9AE2ETests : EmailTemplateE2ETestBase
{
    private const string WelcomeRoute = "/email-templates/edit/11111111-1111-1111-1111-111111111111";

    /// <summary>Opens the editor on an explicit viewport, with the demo's own chrome removed on request.</summary>
    private Task<IPage> OpenEditorAsync(int width, int height, bool touch = false, bool fullWidth = false)
        => OpenRouteAsync(WelcomeRoute, width, height, touch, fullWidth);

    private async Task<IPage> OpenRouteAsync(string route, int width, int height, bool touch = false, bool fullWidth = false)
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
            "try { localStorage.setItem('tm-demo-editor-layout', 'Auto'); } catch (e) { }");

        // A new E2E class that boots the WASM demo fails intermittently on a cold boot; one reload of the
        // same page is the documented stabiliser.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                await page.GotoAsync($"{BaseUrl}{route}", new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.Load,
                    Timeout = 60000,
                });
                await WaitForAppReadyAsync(page);
                await page.WaitForSelectorAsync("[data-tm-email-editor]", new() { Timeout = 60000 });
                break;
            }
            catch (TimeoutException) when (attempt == 0)
            {
                await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
            }
        }

        await page.WaitForSelectorAsync(".tm-editor-shell[data-layout]", new() { Timeout = 30000 });
        await page.WaitForSelectorAsync("[data-tm-canvas-paper]", new() { Timeout = 30000 });

        if (fullWidth)
        {
            // The shell resolves the layout from the editor's CONTAINER: the demo's 18 rem sidebar leaves a
            // 1024 px window a ~664 px editor (a phone). Hide the demo chrome so the capture is the layout it
            // claims to be.
            await page.AddStyleTagAsync(new()
            {
                Content = "aside.fixed { display: none !important; } "
                    + "main { margin-left: 0 !important; width: 100vw !important; max-width: 100vw !important; }",
            });
            await page.WaitForTimeoutAsync(500);
        }

        return page;
    }

    private static async Task SettleAsync(IPage page)
    {
        await page.Keyboard.PressAsync("Escape");
        await page.Mouse.MoveAsync(0, 0);
        await page.WaitForTimeoutAsync(300);
    }

    private static async Task<int> InlineOverflowAsync(IPage page, string selector)
        => await page.EvaluateAsync<int>(
            "sel => { const el = document.querySelector(sel); return el ? Math.round(el.scrollWidth - el.clientWidth) : -1; }",
            selector);

    private static async Task<string> ResolvedLayoutAsync(IPage page)
        => await page.Locator(".tm-editor-shell").First.GetAttributeAsync("data-layout") ?? string.Empty;

    // ── Desktop: the canvas IS the e-mail ─────────────────────────────────────────────────

    [TestMethod]
    public async Task Desktop_canvas_paints_the_email_instead_of_a_structural_list()
    {
        var page = await OpenEditorAsync(1440, 900, fullWidth: true);
        await ForceEditorLayoutAsync(page, "Desktop");
        (await ResolvedLayoutAsync(page)).Should().Be("desktop");

        // The old structural preview is gone: no "TEXT / BUTTON" labels over the body.
        (await page.Locator(".tm-email-canvas-block__kind").CountAsync())
            .Should().Be(0, "the canvas paints the e-mail, not a type label");
        (await page.Locator(".tm-email-canvas-block__detail").CountAsync()).Should().Be(0);

        // Every block body declares which block it drew, and the seed's text is REAL rendered markup.
        var kinds = await page.Locator("[data-tm-block-body]").EvaluateAllAsync<string[]>(
            "els => els.map(e => e.getAttribute('data-tm-block-kind'))");
        kinds.Should().Contain("text").And.Contain("button");

        var firstText = page.Locator("[data-tm-block-body][data-tm-block-kind='text']").First;
        (await firstText.Locator("strong").CountAsync())
            .Should().Be(1, "the seeded heading is bold TEXT, the markup the pipeline actually keeps");

        // The token is a chip, never a substituted value (the canvas has no sample data on purpose).
        var chip = page.Locator("[data-tm-block-body] .tm-email-block__token").First;
        (await chip.CountAsync()).Should().Be(1);
        (await chip.InnerTextAsync()).Should().Contain("{{ first_name }}");
        (await firstText.InnerTextAsync()).Should().NotContain("Jane",
            "tokens are chips: the canvas never substitutes sample data");

        // The button is a pill with its own colours, not a chip with a label.
        var pill = page.Locator(".tm-email-block__button").First;
        var background = await pill.EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");
        background.Should().NotBe("rgba(0, 0, 0, 0)", "the button paints its background colour");
        var radius = await pill.EvaluateAsync<string>("el => getComputedStyle(el).borderRadius");
        radius.Should().NotBe("0px");
        var pillBox = await pill.BoundingBoxAsync();
        pillBox!.Height.Should().BeGreaterThan(24, "a call-to-action button has a body, not a text line");

        // The paper is as wide as the document says and never wider than the panel.
        var paperWidth = await page.EvaluateAsync<double>(
            "() => document.querySelector('[data-tm-canvas-paper]').getBoundingClientRect().width");
        paperWidth.Should().BeInRange(500, 600.5, "the seed's ContentWidth is the 600 px MJML default");
        (await InlineOverflowAsync(page, "[data-tm-email-editor]")).Should().BeLessThanOrEqualTo(0);

        // The paper is the theme-invariant white page, even though the chrome may be dark.
        var paperBackground = await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('[data-tm-canvas-paper]')).backgroundColor");
        paperBackground.Should().Be("rgb(255, 255, 255)", "an e-mail body is white in every client");

        await SettleAsync(page);
        await SaveNamedViewportScreenshotAsync(page, "f9a-canvas-desktop-1440.png");
    }

    [TestMethod]
    public async Task Selecting_a_block_paints_the_indigo_frame_and_its_action_bar()
    {
        var page = await OpenEditorAsync(1440, 900, fullWidth: true);
        await ForceEditorLayoutAsync(page, "Desktop");

        var block = page.Locator("[data-tm-block-id]").First;
        await block.ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-block-id].is-selected", new() { Timeout = 15000 });

        var outline = await page.Locator("[data-tm-block-id].is-selected").First
            .EvaluateAsync<string>("el => getComputedStyle(el).outlineColor");
        outline.Should().Be("rgb(37, 99, 235)", "the selection frame is the primary colour");

        // Actions: the drag grip plus up / down / duplicate / delete, all reachable while selected.
        (await page.Locator("[data-tm-block-id].is-selected [data-tm-block-grip]").CountAsync())
            .Should().Be(1);
        var actions = await page.Locator("[data-tm-block-id].is-selected [data-tm-block-action]")
            .EvaluateAllAsync<string[]>("els => els.map(e => e.getAttribute('data-tm-block-action'))");
        actions.Should().Equal(new[] { "up", "down", "duplicate", "delete" });

        var deleteBox = await page.Locator("[data-tm-block-id].is-selected [data-tm-block-action='delete']")
            .BoundingBoxAsync();
        deleteBox!.Width.Should().BeGreaterThan(16);

        await SettleAsync(page);
        await SaveNamedViewportScreenshotAsync(page, "f9a-canvas-selected-1440.png");
    }

    [TestMethod]
    public async Task A_click_on_a_link_inside_the_canvas_selects_the_block_and_never_navigates()
    {
        var page = await OpenEditorAsync(1440, 900, fullWidth: true);
        await ForceEditorLayoutAsync(page, "Desktop");
        var urlBefore = page.Url;

        // No anchor is rendered anywhere on the canvas: the WYSIWYG body never carries an href.
        (await page.Locator("[data-tm-canvas-doc] a").CountAsync())
            .Should().Be(0, "the canvas renders no navigable link at all");

        var button = page.Locator("[data-tm-block-id][data-tm-block-kind='button']").First;
        await button.ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-block-id].is-selected", new() { Timeout = 15000 });

        page.Url.Should().Be(urlBefore, "a click on the canvas selects, it never navigates");
        (await page.Locator("[data-tm-prop-target='block']").CountAsync()).Should().Be(1);
    }

    [TestMethod]
    public async Task Inserting_from_the_palette_lands_a_rendered_block()
    {
        var page = await OpenEditorAsync(1440, 900, fullWidth: true);
        await ForceEditorLayoutAsync(page, "Desktop");
        var before = await page.Locator("[data-tm-block-id]").CountAsync();

        await page.Locator("[data-tm-toolbox-list] [data-tm-block='divider']").First.ClickAsync();
        await page.WaitForFunctionAsync(
            $"() => document.querySelectorAll('[data-tm-block-id]').length === {before + 1}",
            null, new() { Timeout = 15000 });

        var inserted = page.Locator("[data-tm-block-id]").Last;
        await inserted.Locator("[data-tm-block-body][data-tm-block-kind='divider'] hr").WaitForAsync(
            new() { Timeout = 15000 });
        (await inserted.Locator(".tm-email-block__divider").CountAsync())
            .Should().Be(1, "the new divider is drawn as a rule, not as a label");

        await SettleAsync(page);
        await SaveNamedViewportScreenshotAsync(page, "f9a-canvas-inserted-divider-1440.png");
    }

    // ── The production performance contract (ShouldRender) ───────────────────────────────

    [TestMethod]
    public async Task Typing_in_the_inspector_repaints_only_the_edited_block()
    {
        // A document with 50+ blocks: insert text blocks through the palette until the canvas is big.
        var page = await OpenEditorAsync(1440, 900, fullWidth: true);
        await ForceEditorLayoutAsync(page, "Desktop");

        var blocks = await page.Locator("[data-tm-block-id]").CountAsync();
        while (blocks < 52)
        {
            await page.Locator("[data-tm-toolbox-list] [data-tm-block='text']").First.ClickAsync();
            await page.WaitForFunctionAsync(
                $"() => document.querySelectorAll('[data-tm-block-id]').length > {blocks}",
                null, new() { Timeout = 15000 });
            blocks = await page.Locator("[data-tm-block-id]").CountAsync();
        }

        // Select a block that is NOT the last one, so a "re-render everything" bug would touch the others.
        var targetIndex = blocks / 2;
        var target = page.Locator("[data-tm-block-id]").Nth(targetIndex);
        var targetId = await target.GetAttributeAsync("data-tm-block-id");
        await target.ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-prop='Content'], [data-tm-rich-content]", new() { Timeout = 15000 });

        // Observe every mutation the canvas receives, attributed to the nearest block wrapper.
        await page.EvaluateAsync(
            """
            () => {
                const canvas = document.querySelector('[data-tm-canvas-doc]');
                window.__f9aMutations = [];
                window.__f9aObserver = new MutationObserver(records => {
                    for (const record of records) {
                        const node = record.target.nodeType === 1 ? record.target : record.target.parentElement;
                        const block = node ? node.closest('[data-tm-block-id]') : null;
                        window.__f9aMutations.push({
                            id: block ? block.getAttribute('data-tm-block-id') : null,
                            type: record.type,
                            at: Math.round(performance.now() - window.__f9aStart),
                        });
                    }
                });
                window.__f9aObserver.observe(canvas, { childList: true, subtree: true, characterData: true, attributes: true });
                window.__f9aStart = performance.now();
            }
            """);

        // Type into the rich-text content of the selected block (the inspector's editor writes on input).
        var editor = page.Locator("[data-tm-rich-content]").First;
        await editor.ClickAsync();
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.TypeAsync(" EDIT", new() { Delay = 30 });
        await page.WaitForTimeoutAsync(700);

        var timings = await page.EvaluateAsync<int[]>(
            "() => { window.__f9aObserver.disconnect(); return window.__f9aMutations.map(m => m.at); }");
        var mutations = await page.EvaluateAsync<string[]>(
            "() => window.__f9aMutations.map(m => m.id || '<canvas>')");

        mutations.Should().NotBeEmpty("typing must reach the canvas");
        mutations.Distinct().Should().Equal(new[] { targetId },
            "only the edited block may be repainted — the block renderer decides by its own signature");

        // Keystroke-to-paint on a 50+ block document. Measured in this phase (WASM Debug, self-hosted demo):
        // ~560 ms per keystroke at 52 blocks vs ~260 ms at 3 blocks — the growth is the EDITOR's per-commit
        // whole-document cost (deep clone + dirty check), while the canvas adds ~1.3 ms per 200 blocks
        // (planning §6.2). The bound catches a catastrophic regression such as repainting every block.
        var perKey = (timings.Max() - timings.Min()) / (double)Math.Max(1, timings.Length - 1);
        perKey.Should().BeLessThan(1500,
            $"typing must stay interactive on a 50+ block document (measured {perKey:F0} ms per keystroke)");

        await SaveNamedViewportScreenshotAsync(page, "f9a-canvas-large-document-1440.png");
    }

    // ── Image privacy mode ───────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Placeholder_image_mode_issues_no_request_from_the_editor()
    {
        // The newsletter seed is the demo template with an <img> (example.com/banner.png).
        var page = await OpenRouteAsync(
            "/email-templates/edit/22222222-2222-2222-2222-222222222222", 1440, 900, fullWidth: true);
        await ForceEditorLayoutAsync(page, "Desktop");

        var requests = new List<string>();
        page.Request += (_, request) => requests.Add(request.Url);

        await OpenDemoControlsAsync(page);
        await page.SelectOptionAsync("[data-tm-demo-canvas-images]", "Placeholder");
        await page.WaitForSelectorAsync("[data-tm-canvas-doc] [data-tm-image-placeholder]",
            new() { Timeout = 15000 });

        (await page.Locator("[data-tm-canvas-doc] img[src]").CountAsync())
            .Should().Be(0, "placeholder mode must not leave a requestable img in the canvas");
        requests.Should().NotContain(url => url.Contains("example.com", StringComparison.OrdinalIgnoreCase),
            "privacy mode issues no image request at all");

        await SettleAsync(page);
        await SaveNamedViewportScreenshotAsync(page, "f9a-canvas-images-placeholder-1440.png");
    }

    // ── Phone 390 ────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Mobile_390_paints_the_canvas_full_width_above_the_sheet()
    {
        var page = await OpenEditorAsync(390, 844, touch: true);
        (await ResolvedLayoutAsync(page)).Should().Be("mobile");
        await SettleAsync(page);

        var paper = await page.Locator("[data-tm-canvas-paper]").BoundingBoxAsync();
        paper.Should().NotBeNull();
        paper!.Width.Should().BeGreaterThan(250,
            "the paper takes the phone's width minus the stage gutter (it never stays a fixed 600 px column)");
        paper.Width.Should().BeLessThanOrEqualTo(390);

        (await page.Locator("[data-tm-block-body][data-tm-block-kind='text']").First.InnerTextAsync())
            .Should().NotBeNullOrWhiteSpace();

        (await InlineOverflowAsync(page, "[data-tm-email-editor]"))
            .Should().BeLessThanOrEqualTo(0, "a phone must never scroll horizontally");

        // A tap selects a block, and the action bar of the selected block is finger-sized. The coarse
        // pointer has to be EMULATED before a 44 px target may be asserted (F8 lesson).
        (await page.EvaluateAsync<bool>("() => matchMedia('(pointer: coarse)').matches"))
            .Should().BeTrue("the phone context must emulate a coarse pointer");

        await page.Locator("[data-tm-block-id]").First.DispatchEventAsync("click");
        await page.WaitForSelectorAsync("[data-tm-block-id].is-selected", new() { Timeout = 15000 });

        var actions = page.Locator("[data-tm-block-id].is-selected [data-tm-block-action]");
        (await actions.CountAsync()).Should().Be(4,
            "a touch screen has no hover: the selected block's actions stay reachable");
        var actionBox = await actions.First.BoundingBoxAsync();
        actionBox.Should().NotBeNull();
        actionBox!.Width.Should().BeGreaterThanOrEqualTo(44, "a finger needs a 44 px target");
        actionBox.Height.Should().BeGreaterThanOrEqualTo(44, "a finger needs a 44 px target");

        await SettleAsync(page);
        await SaveNamedViewportScreenshotAsync(page, "f9a-canvas-mobile-390.png");
    }

    // ── Tablet 1024 ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Tablet_1024_keeps_the_paper_readable_between_the_panels()
    {
        var page = await OpenEditorAsync(1024, 800, touch: true, fullWidth: true);
        (await ResolvedLayoutAsync(page)).Should().Be("tablet");

        var paper = await page.Locator("[data-tm-canvas-paper]").BoundingBoxAsync();
        paper.Should().NotBeNull();
        paper!.Width.Should().BeGreaterThan(200, "the paper is visible between the railed palette and inspector");
        (await InlineOverflowAsync(page, "[data-tm-email-editor]")).Should().BeLessThanOrEqualTo(0);

        var fontSize = await page.Locator("[data-tm-block-body][data-tm-block-kind='text']").First
            .EvaluateAsync<double>("el => parseFloat(getComputedStyle(el).fontSize)");
        fontSize.Should().BeGreaterThan(8, "the body keeps a readable size at 1024 px");

        await SettleAsync(page);
        await SaveNamedViewportScreenshotAsync(page, "f9a-canvas-tablet-1024.png");
    }

    // ── Dark mode ────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Dark_mode_keeps_the_paper_white_and_the_ink_readable()
    {
        var page = await OpenEditorAsync(1440, 900, fullWidth: true);
        await ForceEditorLayoutAsync(page, "Desktop");

        var clicked = await page.EvaluateAsync<bool>(
            "() => { const b = document.querySelector(`[data-testid='theme-toggle']`); if (!b) return false; b.click(); return true; }");
        clicked.Should().BeTrue("the demo host renders the theme toggle");
        await page.WaitForSelectorAsync("[data-theme='dark']", new() { Timeout = 15000 });
        await page.WaitForTimeoutAsync(300);

        var paperBackground = await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('[data-tm-canvas-paper]')).backgroundColor");
        paperBackground.Should().Be("rgb(255, 255, 255)",
            "the paper is the theme-invariant page: the export would never be dark");

        var ink = await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('[data-tm-block-body]')).color");
        ink.Should().NotBe(paperBackground, "the body text keeps a contrasting ink on the white paper");

        var chrome = await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('.tm-email-editor__canvas')).backgroundColor");
        chrome.Should().NotBe("rgb(255, 255, 255)", "the stage around the paper follows the dark theme");

        await SettleAsync(page);
        await SaveNamedViewportScreenshotAsync(page, "f9a-canvas-dark-1440.png");
    }

    // ── Edge case: an untrusted document on the canvas ───────────────────────────────────

    [TestMethod]
    public async Task A_pasted_hostile_document_renders_no_active_content_on_the_canvas()
    {
        // Import a template whose text carries script/onerror/javascript: markup, and prove the canvas shows
        // text only. The import dialog is the real path a hostile document takes into the editor.
        var page = await OpenEditorAsync(1440, 900, fullWidth: true);
        await ForceEditorLayoutAsync(page, "Desktop");

        var mjml = """
            <mjml><mj-body><mj-section><mj-column>
              <mj-text>Hi &lt;script&gt;alert('xss')&lt;/script&gt;&lt;img src=x onerror=alert(1)&gt;</mj-text>
              <mj-text>&lt;a href="javascript:alert(1)"&gt;click me&lt;/a&gt;</mj-text>
              <mj-raw>&lt;iframe src="javascript:alert(1)"&gt;&lt;/iframe&gt;</mj-raw>
            </mj-column></mj-section></mj-body></mjml>
            """;

        await page.Locator("[data-tm-import-btn]").ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-import-input]", new() { Timeout = 15000 });
        await page.Locator("[data-tm-import-input]").FillAsync(mjml);
        await page.WaitForSelectorAsync("[data-tm-import-summary]", new() { Timeout = 15000 });
        await page.Locator("[data-tm-import-confirm]").ClickAsync();
        await page.WaitForTimeoutAsync(1500);

        // Whatever the import made of it, the canvas must not carry a single executable node.
        (await page.Locator("[data-tm-canvas-doc] script").CountAsync()).Should().Be(0);
        (await page.Locator("[data-tm-canvas-doc] iframe").CountAsync()).Should().Be(0);
        (await page.Locator("[data-tm-canvas-doc] a").CountAsync()).Should().Be(0);
        (await page.Locator("[data-tm-canvas-doc] img[onerror]").CountAsync()).Should().Be(0);

        // The raw block shows its escaped placeholder instead of the markup.
        (await page.Locator("[data-tm-canvas-doc] [data-tm-raw-preview]").CountAsync())
            .Should().Be(1, "the imported mj-raw block is a placeholder on the canvas");

        // Nothing on the canvas may raise a script dialog (registered BEFORE the interaction window).
        await page.EvaluateAsync("() => { window.__f9aDialogs = 0; window.alert = () => { window.__f9aDialogs++; }; }");
        await page.Locator("[data-tm-canvas-doc] [data-tm-raw-preview]").First.ClickAsync();
        await page.WaitForTimeoutAsync(400);
        (await page.EvaluateAsync<int>("() => window.__f9aDialogs"))
            .Should().Be(0, "nothing on the canvas may execute script");

        await SettleAsync(page);
        await SaveNamedViewportScreenshotAsync(page, "f9a-canvas-hostile-import-1440.png");
    }
}
