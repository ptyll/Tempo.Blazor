using FluentAssertions;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 9A REVIEW fixes — the UX findings verified in a real browser. Each spec here failed before its fix:
/// <list type="bullet">
///   <item><b>UX 9 (blocker):</b> typing into the inspector's rich text reordered the characters
///   (" EDIT" → "TIDE") because the canvas host re-sanitized and the editor re-set its innerHTML on every
///   keystroke, moving the caret to the start;</item>
///   <item><b>UX 10/11:</b> an empty block and a refused image were invisible / unstyled (the placeholder was
///   painted by a <c>RenderTreeBuilder</c> fragment, which carries no scoped-CSS attribute);</item>
///   <item><b>UX 12:</b> a dark chrome painted its dark-theme tokens on the WHITE paper (the token chip read
///   ≈2.1:1);</item>
///   <item><b>UX 13/20:</b> every block announced the same "Text", and the selection was expressed with
///   <c>aria-selected</c>, which is not a supported state of a <c>listitem</c>;</item>
///   <item><b>UX 14:</b> the review screenshots were taken after an <c>Escape</c>, so they showed a deselected
///   canvas;</item>
///   <item><b>UX 21:</b> an unescaped <c>mj-raw</c>/<c>mj-text</c> payload from an import.</item>
/// </list>
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class EmailEditorCanvasReviewFixE2ETests : EmailTemplateE2ETestBase
{
    private const string WelcomeRoute = "/email-templates/edit/11111111-1111-1111-1111-111111111111";
    private const string NewsletterRoute = "/email-templates/edit/22222222-2222-2222-2222-222222222222";

    private async Task<IPage> OpenEditorAsync(int width = 1440, int height = 900, bool touch = false)
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
        await GotoAppReadyAsync(page, $"{BaseUrl}{WelcomeRoute}");
        await page.WaitForSelectorAsync("[data-tm-email-editor]", new() { Timeout = 60000 });

        await page.WaitForSelectorAsync("[data-tm-canvas-paper]", new() { Timeout = 30000 });

        // These specs MUTATE the seeded template (typing, importing, clearing), and the demo auto-saves after
        // three seconds — into the ONE shared demo API the whole lane runs against. A failing host save keeps
        // every edit in this page, so no sibling spec reads a mutated seed (the documented order-dependence
        // pitfall of the email lane).
        await OpenDemoControlsAsync(page);
        await page.SelectOptionAsync("[data-tm-demo-simulate]", "failed");

        if (width >= 1200)
        {
            // The shell resolves the layout from the editor's CONTAINER: the demo's 18 rem sidebar leaves a
            // 1440 px window a ~1080 px editor (tablet). Hide the demo chrome so the capture is the layout it
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

    /// <summary>
    /// Leaves the selection INTACT and parks the mouse on the canvas STAGE (not the block): the review
    /// screenshots used to press Escape and move the mouse to (0, 0), which deselected the block and hid the
    /// very chrome the screenshot was meant to show.
    /// </summary>
    private static async Task SettleKeepingSelectionAsync(IPage page)
    {
        var stage = await page.Locator("[data-tm-canvas-paper]").BoundingBoxAsync();
        if (stage is not null)
        {
            await page.Mouse.MoveAsync((float)(stage.X + stage.Width + 40), (float)(stage.Y + 20));
        }

        await page.WaitForTimeoutAsync(300);
    }

    // ── UX 9 (blocker): the typed characters keep their order ──────────────────────────────

    [TestMethod]
    public async Task Typing_into_a_cleared_text_block_keeps_the_typed_order()
    {
        var page = await OpenEditorAsync();
        await ForceEditorLayoutAsync(page, "Desktop");

        var textBlock = page.Locator("[data-tm-block-id][data-tm-block-kind='text']").First;
        await textBlock.ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-rich-content] .tm-rte-editor-content", new() { Timeout = 15000 });

        var editor = page.Locator("[data-tm-rich-content] .tm-rte-editor-content").First;
        await editor.ClickAsync();
        await page.Keyboard.PressAsync("ControlOrMeta+a");
        await page.Keyboard.PressAsync("Delete");
        await page.WaitForFunctionAsync(
            """
            () => {
                const el = document.querySelector('[data-tm-rich-content] .tm-rte-editor-content');
                return el && el.textContent.trim().length === 0;
            }
            """,
            null, new() { Timeout = 15000 });

        // The caret must stay at the END: one character at a time, with the delay a human has between keys.
        await page.Keyboard.TypeAsync(" EDIT", new() { Delay = 30 });
        await page.WaitForTimeoutAsync(600);

        var blockId = await textBlock.GetAttributeAsync("data-tm-block-id");
        var text = await page.Locator($"[data-tm-block-id='{blockId}'] [data-tm-block-body]").InnerTextAsync();
        text.Trim().Should().Be("EDIT",
            "the typed characters keep their order — the editor must not re-set its innerHTML on the echo of a "
            + "keystroke (measured before the fix: ' EDIT' came out as 'TIDE')");

        await SettleKeepingSelectionAsync(page);
        await SaveNamedViewportScreenshotAsync(page, "f9a-review-typing-1440.png");
    }

    // ── UX 10: an empty block is visible and marked ────────────────────────────────────────

    [TestMethod]
    public async Task An_emptied_text_block_shows_the_empty_marker()
    {
        var page = await OpenEditorAsync();
        await ForceEditorLayoutAsync(page, "Desktop");

        var textBlock = page.Locator("[data-tm-block-id][data-tm-block-kind='text']").First;
        var blockId = await textBlock.GetAttributeAsync("data-tm-block-id");
        await textBlock.ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-rich-content] .tm-rte-editor-content", new() { Timeout = 15000 });

        var editor = page.Locator("[data-tm-rich-content] .tm-rte-editor-content").First;
        await editor.ClickAsync();
        await page.Keyboard.PressAsync("ControlOrMeta+a");
        await page.Keyboard.PressAsync("Delete");
        await page.WaitForSelectorAsync($"[data-tm-block-id='{blockId}'] [data-tm-block-empty]",
            new() { Timeout = 15000 });

        var marker = page.Locator($"[data-tm-block-id='{blockId}'] [data-tm-block-empty]").First;
        (await marker.InnerTextAsync()).Should().Contain("Empty block",
            "the author is told what to do instead of seeing a hole in the paper");

        var style = await marker.EvaluateAsync<string[]>(
            "el => { const s = getComputedStyle(el); return [s.fontStyle, s.borderTopStyle]; }");
        style[0].Should().Be("italic");
        style[1].Should().Be("dashed", "the marker is a dashed guidance line, not the author's content");

        // It is a real box, not a zero-height span: a 0 px block cannot be selected with the mouse.
        var box = await marker.BoundingBoxAsync();
        box!.Height.Should().BeGreaterThanOrEqualTo(30);

        await SettleKeepingSelectionAsync(page);
        await SaveNamedViewportScreenshotAsync(page, "f9a-review-empty-block-1440.png");
    }

    // ── UX 11: the image placeholder is styled ─────────────────────────────────────────────

    [TestMethod]
    public async Task The_image_placeholder_is_a_framed_flex_box()
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await page.AddInitScriptAsync("try { localStorage.setItem('tm-demo-editor-layout', 'Auto'); } catch (e) { }");
        await page.GotoAsync($"{BaseUrl}{NewsletterRoute}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await page.WaitForSelectorAsync("[data-tm-email-editor]", new() { Timeout = 60000 });
        await page.AddStyleTagAsync(new()
        {
            Content = "aside.fixed { display: none !important; } "
                + "main { margin-left: 0 !important; width: 100vw !important; max-width: 100vw !important; }",
        });
        await ForceEditorLayoutAsync(page, "Desktop");

        await OpenDemoControlsAsync(page);
        await page.SelectOptionAsync("[data-tm-demo-canvas-images]", "Placeholder");
        await page.WaitForSelectorAsync("[data-tm-canvas-doc] [data-tm-image-placeholder]", new() { Timeout = 15000 });

        var placeholder = page.Locator("[data-tm-canvas-doc] [data-tm-image-placeholder]").First;
        var style = await placeholder.EvaluateAsync<string[]>(
            "el => { const s = getComputedStyle(el); return [s.display, s.borderTopStyle, s.flexDirection]; }");
        style[0].Should().Be("flex",
            "the placeholder is a framed flex box — a RenderTreeBuilder fragment carries no scoped-CSS attribute, "
            + "so a bare class rule never matched it and it painted as run-together text");
        style[1].Should().Be("dashed");
        style[2].Should().Be("column");

        await SettleKeepingSelectionAsync(page);
        await SaveNamedViewportScreenshotAsync(page, "f9a-review-image-placeholder-1440.png");
    }

    // ── UX 12: the chip keeps its contrast on the white paper in dark mode ─────────────────

    [TestMethod]
    public async Task Dark_mode_keeps_the_token_chip_readable_on_the_paper()
    {
        var page = await OpenEditorAsync();
        await ForceEditorLayoutAsync(page, "Desktop");

        var clicked = await page.EvaluateAsync<bool>(
            "() => { const b = document.querySelector(`[data-testid='theme-toggle']`); if (!b) return false; b.click(); return true; }");
        clicked.Should().BeTrue("the demo host renders the theme toggle");
        await page.WaitForSelectorAsync("[data-theme='dark']", new() { Timeout = 15000 });
        await page.WaitForSelectorAsync("[data-tm-canvas-doc] .tm-email-block__token", new() { Timeout = 15000 });
        await page.WaitForTimeoutAsync(400);

        // Contrast of the chip's ink against the chip's own background, computed from the rendered colours.
        var contrast = await page.EvaluateAsync<double>(
            """
            async () => {
                const parse = value => {
                    const m = value.match(/rgba?\(([^)]+)\)/);
                    if (!m) return null;
                    const parts = m[1].split(',').map(v => parseFloat(v.trim()));
                    return { r: parts[0], g: parts[1], b: parts[2], a: parts.length > 3 ? parts[3] : 1 };
                };
                const lum = c => {
                    const f = v => { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); };
                    return 0.2126 * f(c.r) + 0.7152 * f(c.g) + 0.0722 * f(c.b);
                };
                const chip = document.querySelector('[data-tm-canvas-doc] .tm-email-block__token');
                if (!chip) return -1;
                const s = getComputedStyle(chip);
                let bg = parse(s.backgroundColor);
                // A translucent chip paints over the white paper (the paper token is theme-invariant).
                if (bg && bg.a < 1) {
                    const paper = parse(getComputedStyle(document.querySelector('[data-tm-canvas-paper]')).backgroundColor);
                    const mix = (top, bottom) => ({
                        r: top.r * top.a + bottom.r * (1 - top.a),
                        g: top.g * top.a + bottom.g * (1 - top.a),
                        b: top.b * top.a + bottom.b * (1 - top.a),
                    });
                    bg = mix(bg, paper);
                }
                const fg = parse(s.color);
                if (!bg || !fg) return -2;
                const l1 = lum(fg), l2 = lum(bg);
                return (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05);
            }
            """);

        contrast.Should().BeGreaterThan(0, "the seeded welcome template carries a {{ token }} chip");
        contrast.Should().BeGreaterThanOrEqualTo(4.5,
            $"a dark chrome must not paint its dark-theme tokens on the white paper (measured {contrast:F2}:1)");

        await SettleKeepingSelectionAsync(page);
        await SaveNamedViewportScreenshotAsync(page, "f9a-review-dark-chip-1440.png");
    }

    // ── UX 13 + 20: the accessible name and the selection state ───────────────────────────

    [TestMethod]
    public async Task Blocks_are_announced_by_type_and_content_and_use_aria_current()
    {
        var page = await OpenEditorAsync();
        await ForceEditorLayoutAsync(page, "Desktop");

        var labels = await page.Locator("[data-tm-block-id]").EvaluateAllAsync<string[]>(
            "els => els.map(e => e.getAttribute('aria-label') || '')");
        labels.Should().NotBeEmpty();
        labels.Should().OnlyContain(label => label.Contains(':') || label.Contains("(empty)"),
            "the accessible name is '<type>: <brief content>' (or '<type> (empty)'), not just the type");
        labels.Distinct().Count().Should().BeGreaterThan(1,
            "two text blocks must not announce the same name when their content differs");

        // Roving tabindex: exactly ONE block is in the tab order.
        var stops = await page.Locator("[data-tm-block-id][tabindex='0']").CountAsync();
        stops.Should().Be(1, "a roving tabindex keeps one tab stop in a 50-block document");
        (await page.Locator("[data-tm-block-id][aria-selected]").CountAsync())
            .Should().Be(0, "listitem has no aria-selected state — the selection is aria-current");

        var first = page.Locator("[data-tm-block-id]").First;
        await first.ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-block-id][aria-current='true']", new() { Timeout = 15000 });
        (await page.Locator("[data-tm-block-id][aria-current='true']").CountAsync()).Should().Be(1);

        // Arrow Down moves the selection AND the focus, so the next arrow acts on the visible neighbour.
        await page.Keyboard.PressAsync("ArrowDown");
        await page.WaitForFunctionAsync(
            "() => { const a = document.activeElement; return !!a && a.hasAttribute('data-tm-block-id'); }",
            null, new() { Timeout = 15000 });
        var focusedId = await page.EvaluateAsync<string>(
            "() => document.activeElement.getAttribute('data-tm-block-id')");
        var currentId = await page.Locator("[data-tm-block-id][aria-current='true']").First
            .GetAttributeAsync("data-tm-block-id");
        focusedId.Should().Be(currentId, "the keyboard focus follows the selection");

        await SettleKeepingSelectionAsync(page);
        await SaveNamedViewportScreenshotAsync(page, "f9a-review-aria-1440.png");
    }

    // ── UX 14: the review screenshots keep the state they document ────────────────────────

    [TestMethod]
    public async Task Selected_hover_and_inserted_screenshots_keep_their_state()
    {
        var page = await OpenEditorAsync();
        await ForceEditorLayoutAsync(page, "Desktop");

        // Selected: the block keeps its frame and its bar, and the inspector shows the block.
        var block = page.Locator("[data-tm-block-id]").First;
        await block.ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-block-id].is-selected", new() { Timeout = 15000 });
        (await page.Locator("[data-tm-prop-target='block']").CountAsync()).Should().Be(1,
            "the screenshot documents a selected block, so the inspector must show its properties");
        await SettleKeepingSelectionAsync(page);
        (await page.Locator("[data-tm-block-id].is-selected").CountAsync()).Should().Be(1,
            "moving the mouse to the stage must not deselect the block the screenshot documents");
        await SaveNamedViewportScreenshotAsync(page, "f9a-review-selected-1440.png");

        // Hover (desktop): the action bar appears above the block. Only a block that is NOT the first of its
        // column is lifted — the first one would collide with the section's own bar.
        var lifted = page.Locator("[data-tm-block-id]:not(.is-first-in-column)").First;
        await lifted.HoverAsync();
        await page.WaitForSelectorAsync("[data-tm-block-id]:not(.is-first-in-column):hover [data-tm-block-grip]",
            new() { Timeout = 15000 });
        var bar = await lifted.Locator("[data-tm-block-grip]").BoundingBoxAsync();
        var hovered = await lifted.BoundingBoxAsync();
        bar!.Y.Should().BeLessThan(hovered!.Y + 4,
            "the desktop bar floats ABOVE the block instead of covering its first line");
        await SaveNamedViewportScreenshotAsync(page, "f9a-review-hover-1440.png");

        // Insert: the focus lands on the block the palette just added.
        var before = await page.Locator("[data-tm-block-id]").CountAsync();
        await page.Locator("[data-tm-toolbox-list] [data-tm-block='divider']").First.ClickAsync();
        await page.WaitForFunctionAsync(
            $"() => document.querySelectorAll('[data-tm-block-id]').length === {before + 1}",
            null, new() { Timeout = 15000 });
        var insertedId = await page.Locator("[data-tm-block-id].is-selected").Last.GetAttributeAsync("data-tm-block-id");
        insertedId.Should().NotBeNull("the inserted block is the selection");
        await page.WaitForFunctionAsync(
            "id => { const a = document.activeElement; return !!a && a.getAttribute('data-tm-block-id') === id; }",
            insertedId, new() { Timeout = 15000 });
        await SettleKeepingSelectionAsync(page);
        await SaveNamedViewportScreenshotAsync(page, "f9a-review-inserted-1440.png");

        // Mobile: the selected block's action strip is in flow and finger sized.
        var mobile = await OpenEditorAsync(390, 844, touch: true);
        await mobile.Locator("[data-tm-block-id]").First.DispatchEventAsync("click");
        await mobile.WaitForSelectorAsync("[data-tm-block-id].is-selected", new() { Timeout = 15000 });
        var actionBox = await mobile.Locator("[data-tm-block-id].is-selected [data-tm-block-action]").First
            .BoundingBoxAsync();
        actionBox!.Width.Should().BeGreaterThanOrEqualTo(44);
        await SettleKeepingSelectionAsync(mobile);
        await SaveNamedViewportScreenshotAsync(mobile, "f9a-review-mobile-selected-390.png");
    }

    // ── U1: headings from an import get real typography on the canvas ─────────────────────

    [TestMethod]
    public async Task An_imported_heading_is_typed_like_a_heading_on_the_canvas()
    {
        var page = await OpenEditorAsync();
        await ForceEditorLayoutAsync(page, "Desktop");

        const string mjml = """
            <mjml><mj-body><mj-section><mj-column>
              <mj-text><h1>Big news</h1><p>Body copy</p></mj-text>
            </mj-column></mj-section></mj-body></mjml>
            """;

        await page.Locator("[data-tm-import-btn]").ClickAsync();
        await page.WaitForSelectorAsync("#tm-import-input", new() { Timeout = 15000 });
        await page.Locator("#tm-import-input").FillAsync(mjml);
        await page.WaitForSelectorAsync("[data-tm-import-summary]", new() { Timeout = 15000 });
        await page.Locator("[data-tm-import-confirm]").ClickAsync();
        await page.WaitForTimeoutAsync(1500);

        var heading = page.Locator("[data-tm-canvas-doc] [data-tm-block-body] h1").First;
        (await heading.CountAsync()).Should().Be(1,
            "the sanitizer allow-lists headings (D28), so the author's typography survives the import");
        (await heading.InnerTextAsync()).Should().Contain("Big news");

        var sizes = await heading.EvaluateAsync<double[]>(
            "el => [parseFloat(getComputedStyle(el).fontSize), parseFloat(getComputedStyle(el).fontWeight)]");
        var bodySize = await page.Locator("[data-tm-canvas-doc] [data-tm-block-body] p").First
            .EvaluateAsync<double>("el => parseFloat(getComputedStyle(el).fontSize)");
        sizes[0].Should().BeGreaterThan(bodySize, "an h1 is larger than body copy");
        sizes[1].Should().BeGreaterThanOrEqualTo(600, "an h1 is bold");

        await SettleKeepingSelectionAsync(page);
        await SaveNamedViewportScreenshotAsync(page, "f9a-review-heading-1440.png");
    }

    // ── UX 21: an unescaped hostile import stays inert ────────────────────────────────────

    [TestMethod]
    public async Task An_unescaped_hostile_import_stays_inert_on_the_canvas()
    {
        var page = await OpenEditorAsync();
        await ForceEditorLayoutAsync(page, "Desktop");

        // UNESCAPED markup inside mj-raw / mj-text: this is what an attacker-controlled template looks like
        // (the previous spec escaped it, which only proved the importer's text handling).
        const string mjml = """
            <mjml><mj-body><mj-section><mj-column>
              <mj-raw><iframe src="javascript:alert(1)"></iframe><script>alert(1)</script></mj-raw>
              <mj-text><img src="x" onerror="alert(1)" />Visible text</mj-text>
            </mj-column></mj-section></mj-body></mjml>
            """;

        await page.Locator("[data-tm-import-btn]").ClickAsync();
        await page.WaitForSelectorAsync("#tm-import-input", new() { Timeout = 15000 });
        await page.Locator("#tm-import-input").FillAsync(mjml);
        await page.WaitForSelectorAsync("[data-tm-import-summary]", new() { Timeout = 15000 });
        await page.Locator("[data-tm-import-confirm]").ClickAsync();
        await page.WaitForTimeoutAsync(1500);

        (await page.Locator("[data-tm-canvas-doc] script").CountAsync()).Should().Be(0);
        (await page.Locator("[data-tm-canvas-doc] iframe").CountAsync()).Should().Be(0);
        (await page.Locator("[data-tm-canvas-doc] img[onerror]").CountAsync()).Should().Be(0);
        (await page.Locator("[data-tm-canvas-doc] [data-tm-raw-preview]").CountAsync())
            .Should().Be(1, "the imported mj-raw block is a placeholder, never markup");

        // Nothing on the canvas may raise a script dialog (armed BEFORE the interaction window).
        await page.EvaluateAsync("() => { window.__f9aDialogs = 0; window.alert = () => { window.__f9aDialogs++; }; }");
        await page.Locator("[data-tm-canvas-doc]").ClickAsync(new() { Position = new() { X = 5, Y = 5 } });
        await page.WaitForTimeoutAsync(400);
        (await page.EvaluateAsync<int>("() => window.__f9aDialogs")).Should().Be(0);

        await SettleKeepingSelectionAsync(page);
        await SaveNamedViewportScreenshotAsync(page, "f9a-review-hostile-import-1440.png");
    }
}
