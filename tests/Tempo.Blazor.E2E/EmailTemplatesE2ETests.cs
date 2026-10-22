using System.Text.Json;
using FluentAssertions;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E11 — end-to-end coverage of the email-template demo flow on the live WASM demo (7106) +
/// demo API (5100) + smtp4dev (2525/5000). Captures named screenshots into
/// <c>__screenshots__/email-templates/</c> for the UX review (E11.11).
///
/// Faithfulness notes vs the E11 plan:
/// <list type="bullet">
///   <item>blocks are added via the toolbox click-to-add affordance (a real feature) rather than
///   HTML5 drag&amp;drop, which is flaky under headless Chromium;</item>
///   <item>the full send flow (E11.8) drives the <c>Order confirmation</c> seed (which already owns
///   the <c>customer_name</c>/<c>order_id</c> variables + sample data) instead of authoring a fresh
///   variable into the rich-text canvas by simulated keystrokes — the send → smtp4dev →
///   substitution → verify → cleanup path is exercised identically and reliably.</item>
/// </list>
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class EmailTemplatesE2ETests : EmailTemplateE2ETestBase
{
    // ── E11.2 list + new ─────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task E11_2_List_ShowsSeedTemplates_AndNewTemplateOpensEditor()
    {
        var page = await OpenAsync("/email-templates");
        await page.WaitForSelectorAsync("[data-tm-template-card]", new() { Timeout = 30000 });

        (await page.Locator("[data-tm-template-card]").CountAsync())
            .Should().BeGreaterThanOrEqualTo(3, "the demo seeds Welcome, Newsletter and Order templates");
        await SaveNamedScreenshotAsync(page, "01-list.png");

        await page.Locator("[data-tm-new-template]").ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-email-editor]", new() { Timeout = 30000 });
        await ForceEditorLayoutAsync(page, "Desktop");
        page.Url.Should().Contain("/email-templates/edit/");
        await SaveNamedScreenshotAsync(page, "02-gallery.png");
    }

    // ── E11.3 editor: open seed + add block ──────────────────────────────────────────────────

    [TestMethod]
    public async Task E11_3_Editor_OpensSeed_AddsBlock_SelectsIt()
    {
        var page = await OpenAsync($"/email-templates/edit/{WelcomeTemplateId}");
        await page.WaitForSelectorAsync("[data-tm-email-editor]", new() { Timeout = 30000 });
        await SaveNamedScreenshotAsync(page, "03-editor.png");

        var before = await page.Locator("[data-tm-block-id]").CountAsync();
        await page.Locator("[data-tm-block='text']").First.ClickAsync();
        await page.WaitForFunctionAsync(
            "n => document.querySelectorAll('[data-tm-block-id]').length > n", before,
            new() { Timeout = 15000 });

        // Select the newly-added block; the property panel reflects a block target.
        await page.Locator("[data-tm-block-id]").Last.ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-prop-target='block']", new() { Timeout = 10000 });
        await SaveNamedScreenshotAsync(page, "04-editor-edit.png");
    }

    // ── E11.4 editor: section preset, delete, undo/redo ──────────────────────────────────────

    [TestMethod]
    public async Task E11_4_Editor_AddSectionPreset_DeleteBlock_UndoRedo()
    {
        var page = await OpenAsync($"/email-templates/edit/{NewsletterTemplateId}");
        await page.WaitForSelectorAsync("[data-tm-email-editor]", new() { Timeout = 30000 });

        var sectionsBefore = await page.Locator("[data-tm-section]").CountAsync();
        await page.Locator("[data-tm-preset='TwoEqual'], [data-tm-preset='TwoColumns'], [data-tm-preset]").Nth(1).ClickAsync();
        await page.WaitForFunctionAsync(
            "n => document.querySelectorAll('[data-tm-section]').length > n", sectionsBefore,
            new() { Timeout = 15000 });

        // Delete a block via its inline action, then undo / redo through the toolbar.
        var blocksBefore = await page.Locator("[data-tm-block-id]").CountAsync();
        await page.Locator("[data-tm-block-id]").First.ClickAsync();
        await page.Locator("[data-tm-block-action='delete']").First.ClickAsync();
        await page.WaitForFunctionAsync(
            "n => document.querySelectorAll('[data-tm-block-id]').length < n", blocksBefore,
            new() { Timeout = 15000 });

        await page.Locator("[data-tm-undo]").ClickAsync();
        await page.WaitForFunctionAsync(
            "n => document.querySelectorAll('[data-tm-block-id]').length === n", blocksBefore,
            new() { Timeout = 15000 });

        await page.Locator("[data-tm-redo]").ClickAsync();
        await page.WaitForFunctionAsync(
            "n => document.querySelectorAll('[data-tm-block-id]').length < n", blocksBefore,
            new() { Timeout = 15000 });
    }

    // ── E11.5 preview desktop / mobile / text ────────────────────────────────────────────────

    [TestMethod]
    public async Task E11_5_Preview_DesktopMobileText()
    {
        var page = await OpenAsync($"/email-templates/edit/{WelcomeTemplateId}");
        await page.WaitForSelectorAsync("[data-tm-email-editor]", new() { Timeout = 30000 });

        await page.Locator("[data-tm-preview-btn]").ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-preview-frame]", new() { Timeout = 20000 });

        // The WASM demo renders the preview ON THE DEMO API through IEmailPreviewRenderer, so the iframe
        // only has content once the server answered — that round-trip is the proof Engine is not needed in
        // the browser (contract §3.0/§3.4).
        await WaitForPreviewRenderedAsync(page);
        await SaveNamedScreenshotAsync(page, "05-preview-desktop.png");

        // The paper is exactly the scene width (and no wider): the dialog must have room for the scene,
        // otherwise a "device" switch is a label that changes nothing.
        var desktop = await SceneWidthsAsync(page);
        desktop.Paper.Should().Be(desktop.Stage,
            "the paper fills the desktop scene, which is the document content width plus the stage margin");

        await page.Locator("#tab-preview-device-tablet").ClickAsync();
        await page.WaitForTimeoutAsync(400);
        var tablet = await SceneWidthsAsync(page);
        tablet.Stage.Should().Be(768, "the tablet scene is 768 px (EmailPreviewMetrics.TabletWidth)");
        tablet.Paper.Should().Be(768,
            "the 768 px tablet scene must be VISIBLE — a dialog narrower than the scene silently renders tablet as desktop");
        await SaveNamedScreenshotAsync(page, "06a-preview-tablet.png");

        await page.Locator("#tab-preview-device-mobile").ClickAsync();
        await page.WaitForTimeoutAsync(400);
        var mobile = await SceneWidthsAsync(page);
        mobile.Stage.Should().Be(390, "the mobile scene is 390 px (EmailPreviewMetrics.MobileWidth)");
        mobile.Paper.Should().Be(390, "the mobile paper is exactly the 390 px scene");
        await SaveNamedScreenshotAsync(page, "06-preview-mobile.png");

        // The dialog footer reads left-to-right: "Refresh preview" on the left, "Close" (+ host actions)
        // on the right. Both stacked on one side means the flex row is missing, not that the buttons fit.
        var refresh = await BoundingBoxAsync(page, "[data-tm-preview-dialog-refresh]");
        var close = await BoundingBoxAsync(page, "[data-tm-preview-dialog-close]");
        refresh.Y.Should().BeApproximately(close.Y, 4, "the two footer actions share one row");
        refresh.X.Should().BeLessThan(close.X, "\"Refresh preview\" sits to the left of \"Close\"");

        await page.Locator("#tab-preview-view-text").ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-preview-text]", new() { Timeout = 10000 });
        (await page.Locator("[data-tm-preview-text]").InnerTextAsync()).Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Waits until the preview reached its final <c>rendered</c> state (server round-trip finished) — the
    /// iframe's <c>srcdoc</c> alone is not enough, because a re-render that started after it was applied
    /// uses the PREVIOUS html while the status line already says "rendering", which is exactly what a
    /// screenshot taken one render too early shows.
    /// </summary>
    private static async Task WaitForPreviewRenderedAsync(IPage page)
    {
        await page.WaitForSelectorAsync("[data-tm-preview-state='rendered']", new() { Timeout = 30000 });
        await page.WaitForFunctionAsync(
            "() => { const f = document.querySelector('[data-tm-preview-frame]'); return f && (f.getAttribute('srcdoc') || '').length > 200; }",
            null, new() { Timeout = 30000 });
    }

    /// <summary>
    /// The scene width the component declares and the width the paper really gets. A paper narrower than
    /// the scene means the surrounding dialog clipped it.
    /// </summary>
    private static async Task<(double Stage, double Paper)> SceneWidthsAsync(IPage page)
    {
        var stageText = await page.Locator("[data-tm-preview-stage]")
            .GetAttributeAsync("data-tm-preview-stage-width");
        var stage = double.Parse(stageText ?? "0", System.Globalization.CultureInfo.InvariantCulture);
        var paper = await page.Locator(".tm-email-preview__paper")
            .EvaluateAsync<double>("e => e.getBoundingClientRect().width");
        return (stage, paper);
    }

    private static async Task<(double X, double Y)> BoundingBoxAsync(IPage page, string selector)
    {
        var box = await page.Locator(selector).BoundingBoxAsync();
        box.Should().NotBeNull($"'{selector}' must be laid out");
        return (box!.X, box.Y);
    }

    // ── F6 (2.10.0): preview states on the server renderer ───────────────────────────────────

    [TestMethod]
    public async Task F6_Preview_UnavailableState_OffersRetry_AndRecovers()
    {
        var page = await OpenAsync($"/email-templates/edit/{WelcomeTemplateId}");
        await page.WaitForSelectorAsync("[data-tm-email-editor]", new() { Timeout = 30000 });

        // Edge case: the API is unreachable. The preview must explain it (never a silent empty iframe)
        // and offer a retry; once the route works again the retry renders the real HTML.
        await page.RouteAsync("**/api/email-templates/preview", route => route.AbortAsync());

        await page.Locator("[data-tm-preview-btn]").ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-preview-unavailable]", new() { Timeout = 30000 });
        var unavailableText = await page.Locator("[data-tm-preview-unavailable]").InnerTextAsync();
        unavailableText.Should().Contain("server", "the message explains that the preview could not be loaded");
        unavailableText.Should().NotContain("IEmailPreviewRenderer",
            "a user cannot register a service: the type name belongs in the log, not in the alert");
        await SaveNamedScreenshotAsync(page, "10-preview-unavailable.png");

        await page.UnrouteAsync("**/api/email-templates/preview");
        await page.Locator("[data-tm-preview-retry]").ClickAsync();
        await WaitForPreviewRenderedAsync(page);
        (await page.Locator("[data-tm-preview-unavailable]").CountAsync())
            .Should().Be(0, "a successful retry leaves the unavailable state");
        await SaveNamedScreenshotAsync(page, "10b-preview-after-retry.png");
    }

    [TestMethod]
    public async Task F6_Preview_ServerHtml_StaysSandboxed()
    {
        var page = await OpenAsync($"/email-templates/edit/{WelcomeTemplateId}");
        await page.WaitForSelectorAsync("[data-tm-email-editor]", new() { Timeout = 30000 });
        await page.Locator("[data-tm-preview-btn]").ClickAsync();
        await WaitForPreviewRenderedAsync(page);

        var sandbox = await page.Locator("[data-tm-preview-frame]").GetAttributeAsync("sandbox");
        sandbox.Should().Be(string.Empty, "server HTML is untrusted: sandbox=\"\", no allow-scripts");
        (await page.Locator("[data-tm-preview-frame]").GetAttributeAsync("allow")).Should().BeNull();

        // The iframe content document is opaque (sandboxed without allow-same-origin), so script inside it
        // cannot reach the editor's origin.
        var sameOrigin = await page.EvaluateAsync<bool>(
            "() => { try { const f = document.querySelector('[data-tm-preview-frame]'); return !!f.contentDocument; } catch { return false; } }");
        sameOrigin.Should().BeFalse("a sandboxed frame without allow-same-origin has no accessible contentDocument");
    }

    // ── F6 review: the remaining preview states on the server renderer ───────────────────────

    /// <summary>
    /// A realistic e-mail body: the tests fulfil the preview route with it, so the sandboxed frame has
    /// something to lay out and the UX screenshots show a believable message instead of one word.
    /// </summary>
    private const string FakeEmailHtml = """
<!doctype html><html><head><meta charset="utf-8"><title>Winter newsletter</title></head>
<body style="margin:0;background:#f4f4f4;font-family:Helvetica,Arial,sans-serif;">
<table role="presentation" width="600" align="center" style="background:#ffffff;"><tr><td style="padding:24px;">
<h1 style="margin:0 0 12px;font-size:24px;color:#111111;">Winter is coming</h1>
<p style="margin:0 0 12px;font-size:15px;line-height:22px;color:#333333;">Fresh snow, new boards and a members-only discount inside this week's newsletter. Read on for the full collection.</p>
<a href="https://example.com/shop" style="display:inline-block;padding:12px 20px;background:#0f62fe;color:#ffffff;text-decoration:none;border-radius:4px;">Shop the collection</a>
</td></tr></table></body></html>
""";

    [TestMethod]
    public async Task F6_Preview_Warnings_AreShownSeparatelyFromErrors()
    {
        var page = await OpenAsync($"/email-templates/edit/{WelcomeTemplateId}");
        await page.WaitForSelectorAsync("[data-tm-email-editor]", new() { Timeout = 30000 });

        // Rendered + Warning (D23): the HTML is complete, only a URL was replaced during the guard pass.
        await FulfillPreviewAsync(page, PreviewJson(FakeEmailHtml, status: 0,
            errors: [("A link with the blocked URL scheme 'javascript:' was replaced with '#'.", null, null,
                "render.url.blocked_scheme", 1)]));

        await page.Locator("[data-tm-preview-btn]").ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-preview-warnings]", new() { Timeout = 30000 });
        await WaitForPreviewRenderedAsync(page);

        (await page.Locator("[data-tm-preview-errors]").CountAsync())
            .Should().Be(0, "a warning is not an error: the two live in separate alerts");
        (await page.Locator("[data-tm-preview-warnings]").InnerTextAsync())
            .Should().Contain("blocked URL scheme", "the warning message is shown to the user");
        await SaveNamedScreenshotAsync(page, "10c-preview-warnings.png");
    }

    [TestMethod]
    public async Task F6_Preview_Failed_ShowsTheErrorAndNoHtml()
    {
        var page = await OpenAsync($"/email-templates/edit/{WelcomeTemplateId}");
        await page.WaitForSelectorAsync("[data-tm-email-editor]", new() { Timeout = 30000 });

        await FulfillPreviewAsync(page, PreviewJson(string.Empty, status: 1,
            errors: [("Template is not allowed to access Type", null, null, null, 0)]));

        await page.Locator("[data-tm-preview-btn]").ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-preview-state='failed']", new() { Timeout = 30000 });
        await page.WaitForSelectorAsync("[data-tm-preview-errors]", new() { Timeout = 30000 });

        (await page.Locator("[data-tm-preview-errors]").InnerTextAsync())
            .Should().Contain("not allowed to access", "the render error explains what is wrong with the template");
        (await page.Locator("[data-tm-preview-frame]").GetAttributeAsync("srcdoc") ?? string.Empty)
            .Should().BeEmpty("a failed render produces no HTML at all");
        await SaveNamedScreenshotAsync(page, "10d-preview-failed.png");
    }

    [TestMethod]
    public async Task F6_Preview_Loading_MarksThePreviewBusy()
    {
        var page = await OpenAsync($"/email-templates/edit/{WelcomeTemplateId}");
        await page.WaitForSelectorAsync("[data-tm-email-editor]", new() { Timeout = 30000 });

        // A slow server: the preview must say it is working instead of showing an empty paper.
        await FulfillPreviewAsync(page, PreviewJson(FakeEmailHtml), delay: TimeSpan.FromSeconds(3));

        await page.Locator("[data-tm-preview-btn]").ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-preview-root][aria-busy='true']", new() { Timeout = 15000 });
        (await page.Locator("[data-tm-preview-status]").GetAttributeAsync("aria-live"))
            .Should().Be("polite", "the loading state is announced politely");
        (await page.Locator("[data-tm-preview-loading]").CountAsync())
            .Should().Be(1, "the empty stage shows the spinner, not an empty paper");
        await SaveNamedScreenshotAsync(page, "10e-preview-loading.png");

        await WaitForPreviewRenderedAsync(page);
        (await page.Locator("[data-tm-preview-root]").GetAttributeAsync("aria-busy"))
            .Should().Be("false", "the busy flag clears when the render lands");
    }

    [TestMethod]
    public async Task F6_Preview_DarkMode_KeepsThePaperLight()
    {
        var page = await OpenAsync($"/email-templates/edit/{WelcomeTemplateId}");
        await page.WaitForSelectorAsync("[data-tm-email-editor]", new() { Timeout = 30000 });

        // Switch the demo shell to dark BEFORE the dialog opens: the modal overlay intercepts clicks, so the
        // header toggle is unreachable while the preview is up.
        // The demo renders the toggle twice (desktop header + the collapsed mobile bar); only one is visible
        // at the 1440 px viewport, so the visible one is the one to click.
        await page.Locator("[data-testid='theme-toggle']:visible").First.ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector('.demo-shell')?.getAttribute('data-theme') === 'dark'", null,
            new() { Timeout = 10000 });

        await page.Locator("[data-tm-preview-btn]").ClickAsync();
        await WaitForPreviewRenderedAsync(page);

        var paperBackground = await page.Locator(".tm-email-preview__paper")
            .EvaluateAsync<string>("e => getComputedStyle(e).backgroundColor");
        paperBackground.Should().Be("rgb(255, 255, 255)",
            "the e-mail paper follows the document background (light) even on a dark shell");
        await SaveNamedScreenshotAsync(page, "10f-preview-dark.png");
    }

    [TestMethod]
    public async Task F6_Preview_LongEmail_KeepsTheFooterInsideTheDialog()
    {
        var page = await OpenAsync("/email-templates");
        await page.Locator("[data-tm-new-template]").ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-email-editor]", new() { Timeout = 30000 });
        await ForceEditorLayoutAsync(page, "Desktop");

        // Dividers on purpose: an empty text block renders no visible content, so a 30-text-block email
        // would still look like a blank sheet in the screenshot. 30 dividers make the paper visibly long.
        for (var i = 0; i < 30; i++)
        {
            await page.Locator("[data-tm-block='divider']").First.ClickAsync();
        }

        await page.WaitForFunctionAsync(
            "n => document.querySelectorAll('[data-tm-block-id]').length >= n", 30, new() { Timeout = 60000 });

        await page.Locator("[data-tm-preview-btn]").ClickAsync();
        await WaitForPreviewRenderedAsync(page);

        var footer = await page.Locator("[data-tm-preview-dialog-footer]").BoundingBoxAsync();
        footer.Should().NotBeNull("the preview dialog footer has to be laid out");
        var viewport = page.ViewportSize!;
        (footer!.Y + footer.Height).Should().BeLessThanOrEqualTo(viewport.Height + 1,
            "a 30-block e-mail scrolls inside the preview; the dialog footer stays in the viewport");
        await SaveNamedScreenshotAsync(page, "10g-preview-long.png");
    }

    [TestMethod]
    public async Task F6_Preview_EmptyTemplate_ShowsAnEmptyState()
    {
        var page = await OpenAsync("/email-templates");
        await page.Locator("[data-tm-new-template]").ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-email-editor]", new() { Timeout = 30000 });
        await ForceEditorLayoutAsync(page, "Desktop");

        // Remove the single section a new template starts with: an empty document has nothing to preview.
        // The section toolbar only becomes clickable on hover, so the click is dispatched to the button
        // itself (the same handler a real hover + click fires).
        await page.Locator("[data-tm-section-action='delete']").First.DispatchEventAsync("click");
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('[data-tm-section]').length === 0", null, new() { Timeout = 15000 });

        await page.Locator("[data-tm-preview-btn]").ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-preview-empty]", new() { Timeout = 30000 });

        (await page.Locator("[data-tm-preview-empty]").InnerTextAsync())
            .Should().Contain("empty", "the empty state explains why there is no preview");
        (await page.Locator("[data-tm-preview-frame]").CountAsync())
            .Should().Be(0, "an empty template renders no framing paper");
        await SaveNamedScreenshotAsync(page, "10h-preview-empty.png");
    }

    /// <summary>
    /// Fulfils the preview endpoint with a canned <see cref="PreviewJson"/> answer (optionally after a delay),
    /// so a state that is hard to provoke with a real renderer (a warning, a failed render, a slow server)
    /// is still exercised end to end.
    /// </summary>
    private static async Task FulfillPreviewAsync(IPage page, string json, TimeSpan? delay = null)
    {
        await page.RouteAsync("**/api/email-templates/preview", async route =>
        {
            if (delay is { } wait)
            {
                await Task.Delay(wait);
            }

            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "application/json",
                Body = json,
            });
        });
    }

    /// <summary>A <c>RenderPreviewResponse</c>-shaped JSON payload (camelCase, enums as numbers).</summary>
    private static string PreviewJson(
        string html,
        int status = 0,
        (string Message, int? Line, int? Column, string? Key, int Severity)[]? errors = null)
        => JsonSerializer.Serialize(new
        {
            html,
            text = "Winter newsletter — plain text version",
            subject = "Winter newsletter",
            preheader = (string?)null,
            status,
            errors = (errors ?? []).Select(e => new
            {
                message = e.Message,
                line = e.Line,
                column = e.Column,
                key = e.Key,
                severity = e.Severity,
            }).ToArray(),
        });

    // ── E11.6 validation ─────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task E11_6_Validation_SurfacesButtonWithoutHref()
    {
        var page = await OpenAsync($"/email-templates/edit/{WelcomeTemplateId}");
        await page.WaitForSelectorAsync("[data-tm-email-editor]", new() { Timeout = 30000 });

        // A button with no href is an invalid email block.
        await page.Locator("[data-tm-block='button']").First.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        await page.Locator("[data-tm-validate-btn]").ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-validation]", new() { Timeout = 15000 });
        (await page.Locator("[data-tm-validation-message]").CountAsync())
            .Should().BeGreaterThan(0, "an empty-href button should produce a validation finding");
        await SaveNamedScreenshotAsync(page, "07-validation.png");
    }

    // ── E11.7 save persists across reload ────────────────────────────────────────────────────

    [TestMethod]
    public async Task E11_7_Save_PersistsAcrossReload()
    {
        // Use a fresh template so we never mutate a shared seed read by other tests.
        var page = await OpenAsync("/email-templates");
        await page.Locator("[data-tm-new-template]").ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-email-editor]", new() { Timeout = 30000 });
        await ForceEditorLayoutAsync(page, "Desktop");
        var editUrl = page.Url;

        var subject = $"Persisted {Guid.NewGuid():N}";
        var subjectField = page.Locator("[data-tm-prop='Subject'] input");
        await subjectField.FillAsync(subject);
        await subjectField.BlurAsync();

        await page.Locator("[data-tm-save]").ClickAsync();
        await page.WaitForTimeoutAsync(800);

        await page.ReloadAsync(new() { WaitUntil = WaitUntilState.Load });
        await WaitForAppReadyAsync(page);
        await page.WaitForSelectorAsync("[data-tm-prop='Subject'] input", new() { Timeout = 30000 });
        (await page.Locator("[data-tm-prop='Subject'] input").InputValueAsync())
            .Should().Be(subject, "the saved subject survives a page reload via the API");
        editUrl.Should().Contain("/email-templates/edit/");
    }

    // ── E11.8 full send flow with smtp4dev verification ──────────────────────────────────────

    [TestMethod]
    public async Task E11_8_FullSendFlow_DeliversSubstitutedEmail()
    {
        var recipient = UniqueRecipient("order");
        var customerName = $"Customer {Guid.NewGuid():N}";

        var page = await OpenAsync($"/email-templates/send/{OrderTemplateId}");
        await page.WaitForSelectorAsync("[data-tm-email-send-page]", new() { Timeout = 30000 });

        // The dynamic form exposes the template's variables.
        await page.WaitForSelectorAsync("[data-tm-var='customer_name']", new() { Timeout = 15000 });
        await page.Locator("[data-tm-var='order_id']").FillAsync("E2E-7777");
        await page.Locator("[data-tm-var='customer_name']").FillAsync(customerName);
        await page.Locator("[data-tm-to]").FillAsync(recipient);
        // Live preview adopts the typed values (not the seed sample data).
        await page.WaitForFunctionAsync(
            "name => { const f = document.querySelector('[data-tm-preview-frame]'); return f && (f.getAttribute('srcdoc') || '').includes(name); }",
            customerName, new() { Timeout = 15000 });
        await SaveNamedScreenshotAsync(page, "08-send-form.png");

        await page.Locator("[data-tm-send-submit]").ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-send-success]", new() { Timeout = 20000 });
        await SaveNamedScreenshotAsync(page, "09-send-success.png");

        // smtp4dev actually received the substituted message.
        var message = await PollForMessageAsync(recipient);
        message.Subject.Should().Contain("E2E-7777", "subject substitutes order_id");
        message.To.Should().Contain(recipient);

        var html = await GetMessageHtmlAsync(message.Id);
        html.Should().Contain(customerName, "the HTML body substitutes customer_name");

        var text = await GetMessagePlaintextOrNullAsync(message.Id);
        text.Should().NotBeNull("a multipart email carries a text alternative");
        text!.Should().Contain(customerName);

        await DeleteMessageAsync(message.Id);
    }

    // ── E11.9 accessibility / keyboard ───────────────────────────────────────────────────────

    [TestMethod]
    public async Task E11_9_Editor_KeyboardAndShortcutsHelp()
    {
        var page = await OpenAsync($"/email-templates/edit/{WelcomeTemplateId}");
        await page.WaitForSelectorAsync("[data-tm-email-editor]", new() { Timeout = 30000 });

        // Tab moves focus into interactive chrome (focus is visible / tracked).
        await page.Keyboard.PressAsync("Tab");
        var hasFocus = await page.EvaluateAsync<bool>("() => document.activeElement && document.activeElement !== document.body");
        hasFocus.Should().BeTrue();

        // Keyboard shortcuts help opens.
        await page.Locator("[data-tm-help-btn]").ClickAsync();
        await page.WaitForTimeoutAsync(400);
        await SaveNamedScreenshotAsync(page, "09b-shortcuts-help.png");
    }

    // ── E11.10b MJML import flow ─────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task E11_10b_ImportMjml_RendersStructure()
    {
        var page = await OpenAsync($"/email-templates/edit/{WelcomeTemplateId}");
        await page.WaitForSelectorAsync("[data-tm-email-editor]", new() { Timeout = 30000 });

        await page.Locator("[data-tm-import-btn]").ClickAsync();
        await page.WaitForSelectorAsync("#tm-import-input", new() { Timeout = 15000 });

        const string mjml = """
            <mjml><mj-body><mj-section><mj-column>
              <mj-text>Imported headline</mj-text>
              <mj-button href="https://example.com">Imported CTA</mj-button>
            </mj-column></mj-section></mj-body></mjml>
            """;
        await page.Locator("#tm-import-input").FillAsync(mjml);
        await page.WaitForSelectorAsync("[data-tm-import-summary]", new() { Timeout = 15000 });
        await SaveNamedScreenshotAsync(page, "11-import.png");

        await page.Locator("[data-tm-import-confirm]").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.body.innerText.includes('Imported headline')",
            null, new() { Timeout = 15000 });
    }
}
