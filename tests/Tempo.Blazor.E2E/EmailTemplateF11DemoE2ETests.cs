using Microsoft.Playwright;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 11 — the DEMO surface and the remaining cross-cutting flows of the e-mail editor:
/// the template library (search, language badge, phone "⋮" actions, delete confirmation), the leave guard
/// (<see cref="Tempo.Blazor.Components.Navigation.TmNavigationGuard"/> wired to <c>IsDirty</c>/<c>SaveAsync</c>),
/// the send page (sections, host descriptions, phone sticky send) and the server preview's superseded-request
/// cancellation.
/// <para>
/// Every spec starts from the reset seed (the base class posts the demo API's reset), so these do not depend
/// on which spec ran before them — the F11 carry-forward that made the lane order-dependent.
/// </para>
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class EmailTemplateF11DemoE2ETests : EmailTemplateE2ETestBase
{
    // ── the template library ────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task F11_1_Library_Search_FiltersByNameAndSubject_AndExplainsNoMatch()
    {
        var page = await OpenAsync("/email-templates");
        await page.WaitForSelectorAsync("[data-tm-template-card]", new() { Timeout = 30000 });
        var all = await page.Locator("[data-tm-template-card]").CountAsync();
        all.Should().BeGreaterThanOrEqualTo(3);

        // By NAME.
        await page.Locator("[data-tm-list-search] input").FillAsync("welcome");
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('[data-tm-template-card]').length === 1",
            null, new() { Timeout = 10000 });
        (await page.Locator("[data-tm-template-card]").First.GetAttributeAsync("data-tm-template-id"))
            .Should().Be(WelcomeTemplateId.ToString());

        // By SUBJECT — "confirmed" appears in the order template's SUBJECT only, never in a name.
        await page.Locator("[data-tm-list-search] input").FillAsync("confirmed");
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('[data-tm-template-card]').length === 1",
            null, new() { Timeout = 10000 });
        (await page.Locator("[data-tm-template-card]").First.GetAttributeAsync("data-tm-template-id"))
            .Should().Be(OrderTemplateId.ToString(), "the search covers the subject, not only the name");

        // No match: an explained empty state that names the term, NOT the "no templates yet" state.
        await page.Locator("[data-tm-list-search] input").FillAsync("zzz-no-such-template");
        await page.WaitForSelectorAsync("[data-tm-list-no-match]", new() { Timeout = 10000 });
        var noMatch = (await page.Locator("[data-tm-list-no-match]").InnerTextAsync()).Trim();
        noMatch.Should().Contain("zzz-no-such-template", "the empty state echoes what was searched for");
        (await page.Locator("[data-tm-template-card]").CountAsync()).Should().Be(0);

        // Clearing restores every card.
        await page.Locator("[data-tm-list-search] input").FillAsync(string.Empty);
        await page.WaitForFunctionAsync(
            $"() => document.querySelectorAll('[data-tm-template-card]').length === {all}",
            null, new() { Timeout = 10000 });
    }

    [TestMethod]
    public async Task F11_2_Library_Cards_CarryALanguageBadge_AndFoldSecondaryActionsOnAPhone()
    {
        var page = await OpenAsync("/email-templates");
        await page.WaitForSelectorAsync("[data-tm-template-card]", new() { Timeout = 30000 });

        // The badge names the language, it does not print the raw code ("en" told a user nothing).
        var badge = (await page.Locator("[data-tm-card-language]").First.InnerTextAsync()).Trim();
        badge.Should().NotBeNullOrWhiteSpace();
        badge.Should().NotBe("en", "the badge carries the human language name, not the ISO code");
        badge.Should().MatchRegex("^[A-Za-zÀ-ž]+$", "one word — the language name");

        // Desktop: all four actions are visible at once.
        foreach (var hook in new[] { "[data-tm-open]", "[data-tm-send]", "[data-tm-duplicate]", "[data-tm-delete]" })
        {
            (await page.Locator(hook).First.IsVisibleAsync()).Should().BeTrue($"{hook} is reachable on a desktop");
        }

        await page.SetViewportSizeAsync(390, 844);
        await page.WaitForTimeoutAsync(500);

        // Phone: Edit stays primary and visible, the three secondary actions fold into the "⋮" menu.
        (await page.Locator("[data-tm-open]").First.IsVisibleAsync()).Should().BeTrue("Edit is the primary action");
        var menu = page.Locator("[data-tm-card-menu]").First;
        (await menu.IsVisibleAsync()).Should().BeTrue("the phone folds the secondary actions into a menu");
        (await page.Locator("[data-tm-send]").First.IsVisibleAsync())
            .Should().BeFalse("a secondary action must not squeeze the card at 390 px");
        await menu.ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-card-menu-send]", new() { Timeout = 15000 });
        (await page.Locator("[data-tm-card-menu-duplicate]").CountAsync()).Should().Be(1);
        (await page.Locator("[data-tm-card-menu-delete]").CountAsync()).Should().Be(1);

        // A menu is not a trap: Escape closes it and hands the focus back to the "⋮" that opened it.
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForSelectorAsync("[data-tm-card-menu-panel]", new() { State = WaitForSelectorState.Detached, Timeout = 15000 });
        (await page.EvaluateAsync<bool>(
            "() => !!document.activeElement && document.activeElement.hasAttribute('data-tm-card-menu')"))
            .Should().BeTrue("Escape returns the focus to the trigger, not to <body>");

        // A click outside (the backdrop) closes it too — same one-open-menu contract, mouse path.
        await menu.ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-card-menu-panel]", new() { Timeout = 15000 });
        await page.Mouse.ClickAsync(10, 300);
        await page.WaitForSelectorAsync("[data-tm-card-menu-panel]", new() { State = WaitForSelectorState.Detached, Timeout = 15000 });

        // No horizontal overflow at the narrowest supported width.
        await page.SetViewportSizeAsync(320, 720);
        await page.WaitForTimeoutAsync(500);
        var overflows = await page.EvaluateAsync<bool>(
            "() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1");
        overflows.Should().BeFalse("the library never scrolls sideways at 320 px");
    }

    [TestMethod]
    public async Task F11_3_DeleteConfirmation_IsDestructive_AndCancellingKeepsTheTemplate()
    {
        var page = await OpenAsync("/email-templates");
        await page.WaitForSelectorAsync("[data-tm-template-card]", new() { Timeout = 30000 });
        var before = await page.Locator("[data-tm-template-card]").CountAsync();

        await page.Locator("[data-tm-delete]").First.ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-confirm-delete]", new() { Timeout = 20000 });

        // Destructive look: the icon is painted in the danger colour, the copy says "permanently".
        var iconColour = await page.Locator("[data-tm-delete-dialog-icon]").First
            .EvaluateAsync<string>("el => getComputedStyle(el).color");
        var channels = System.Text.RegularExpressions.Regex.Matches(iconColour, @"\d+")
            .Select(m => int.Parse(m.Value)).ToList();
        (channels[0] > 120 && channels[0] > channels[1] + 40 && channels[0] > channels[2] + 40)
            .Should().BeTrue($"the destructive icon is tinted danger (measured {iconColour})");

        // Presentation follows the VIEWPORT (MobilePresentation.Auto): the reference draws a centered
        // dialog on a desktop, and a forced Sheet turned this 1440 px confirmation into a bottom sheet.
        (await page.Locator(".tm-modal--sheet").CountAsync())
            .Should().Be(0, "a desktop gets the centered dialog, not the phone sheet");

        await page.Locator(".tm-modal-close:visible, [data-tm-dialog-close]:visible").First.ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-confirm-delete]", new() { State = WaitForSelectorState.Detached, Timeout = 15000 });
        (await page.Locator("[data-tm-template-card]").CountAsync()).Should().Be(before, "Cancel deletes nothing");

        // The same dialog on a phone IS the core sheet (F2 primitive), not a second implementation.
        await page.SetViewportSizeAsync(390, 844);
        await page.WaitForTimeoutAsync(400);
        await OpenCardDeleteAsync(page);
        await page.WaitForSelectorAsync("[data-tm-confirm-delete]", new() { Timeout = 20000 });
        (await page.Locator(".tm-modal--sheet").CountAsync())
            .Should().Be(1, "at 390 px the confirmation presents as the bottom sheet");
    }

    /// <summary>Opens the delete confirmation at whatever width the page is: card button or the "⋮" menu.</summary>
    private static async Task OpenCardDeleteAsync(IPage page)
    {
        var direct = page.Locator("[data-tm-delete]").First;
        if (await direct.CountAsync() > 0 && await direct.IsVisibleAsync())
        {
            await direct.ClickAsync();
            return;
        }

        await page.Locator("[data-tm-card-menu]").First.ClickAsync();
        await page.Locator("[data-tm-card-menu-delete]").First.ClickAsync();
    }

    // ── the leave guard ────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task F11_4_LeaveGuard_SavesThePendingEdit_AndThenLeaves()
    {
        var page = await OpenAsync($"/email-templates/edit/{NewsletterTemplateId}");
        await page.WaitForSelectorAsync("[data-tm-canvas-doc]", new() { Timeout = 30000 });

        var subject = page.Locator("[data-tm-prop='Subject'] input");
        var unique = "Guarded subject " + Guid.NewGuid().ToString("N")[..8];
        await subject.FillAsync(unique);
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-tm-email-editor]')?.getAttribute('data-tm-dirty') === 'true'",
            null, new() { Timeout = 15000 });

        // Navigating away with unsaved work asks FIRST, and the dialog offers the host save.
        await page.Locator("[data-tm-back]").ClickAsync();
        var saveAndLeave = page.GetByRole(AriaRole.Button, new() { Name = "Save and leave" }).First;
        await saveAndLeave.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15000 });

        await saveAndLeave.ClickAsync();
        await page.WaitForURLAsync("**/email-templates", new() { Timeout = 30000 });

        // The save really went to the API: reopen the template and read the subject back.
        await page.GotoAsync($"{BaseUrl}/email-templates/edit/{NewsletterTemplateId}",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await page.WaitForSelectorAsync("[data-tm-canvas-doc]", new() { Timeout = 60000 });
        await page.WaitForFunctionAsync(
            "expected => document.querySelector(\"[data-tm-prop='Subject'] input\")?.value === expected",
            unique, new() { Timeout = 20000 });
    }

    [TestMethod]
    public async Task F11_5_LeaveGuard_StaysWhenTheUserCancels_AndClearsAfterSaving()
    {
        var page = await OpenAsync($"/email-templates/edit/{WelcomeTemplateId}");
        await page.WaitForSelectorAsync("[data-tm-canvas-doc]", new() { Timeout = 30000 });

        await page.Locator("[data-tm-prop='Subject'] input").FillAsync("Unsaved by the guard test");
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-tm-email-editor]')?.getAttribute('data-tm-dirty') === 'true'",
            null, new() { Timeout = 15000 });

        await page.Locator("[data-tm-back]").ClickAsync();
        var stay = page.GetByRole(AriaRole.Button, new() { Name = "Stay" }).First;
        await stay.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await stay.ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-email-editor]", new() { Timeout = 15000 });
        page.Url.Should().Contain("/email-templates/edit/", "Stay keeps the editor open");
        (await page.Locator("[data-tm-email-editor]").GetAttributeAsync("data-tm-dirty")).Should().Be("true");

        // A confirmed save clears the guard: leaving afterwards must not ask again.
        await page.Locator("[data-tm-save]").First.ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-tm-email-editor]')?.getAttribute('data-tm-dirty') === 'false'",
            null, new() { Timeout = 30000 });
        await page.Locator("[data-tm-back]").ClickAsync();
        await page.WaitForURLAsync("**/email-templates", new() { Timeout = 30000 });
    }

    // ── the send page ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task F11_6_SendPage_GroupsTheForm_SpeaksHumanVariableNames_AndKeepsSendStickyOnAPhone()
    {
        var page = await OpenAsync($"/email-templates/send/{OrderTemplateId}");
        await page.WaitForSelectorAsync("[data-tm-email-send-page]", new() { Timeout = 30000 });

        // Two named sections, not one long column.
        var sections = await page.Locator("[data-tm-send-section]").EvaluateAllAsync<string[]>(
            "els => els.map(e => e.getAttribute('data-tm-send-section'))");
        sections.Should().Contain("recipients").And.Contain("variables");

        // A variable label is the host's DESCRIPTION, not the token path.
        var label = (await page.Locator("[data-tm-var-label]").First.GetAttributeAsync("data-tm-var-label") ?? string.Empty).Trim();
        label.Should().NotBeNullOrWhiteSpace();
        label.Should().NotMatchRegex(@"^[a-z_]+\.[a-z_]+$", "a dotted token path is not a human label");
        label.Should().Contain(" ", "a human label is a phrase (\"Order number\"), not one identifier");

        // The preview is the plain message preview: no sample-data editor, no HTML/text toggle.
        (await page.Locator("[data-tm-preview-edit-data]").CountAsync()).Should().Be(0);
        (await page.Locator("[data-tm-preview-view-group]").CountAsync()).Should().Be(0);

        // Phone: the form comes FIRST (the preview follows it) and Send stays reachable while scrolling.
        await page.SetViewportSizeAsync(390, 844);
        await page.WaitForTimeoutAsync(600);
        var formTop = await page.Locator("[data-tm-send-form]").First
            .EvaluateAsync<double>("el => el.getBoundingClientRect().top");
        var previewTop = await page.Locator("[data-tm-send-preview]").First
            .EvaluateAsync<double>("el => el.getBoundingClientRect().top");
        formTop.Should().BeLessThan(previewTop, "a phone reads the form first, the preview second");

        // A disabled Send says why, right under the button; typing a recipient removes the reason.
        (await page.Locator("[data-tm-send-hint]").InnerTextAsync()).Trim()
            .Should().Be("Enter at least one recipient.", "the disabled primary action explains itself");
        await page.Locator("[data-tm-to]").FillAsync(UniqueRecipient("f11-sticky"));
        (await page.Locator("[data-tm-send-hint]").CountAsync())
            .Should().Be(0, "the reason disappears once the form is valid");

        // 1024 (below the reference's two-column width): form and preview stack in ONE column — the old
        // two-column grid squeezed both, and a sticky preview in a single column would cover the form.
        await page.SetViewportSizeAsync(1024, 768);
        await page.WaitForTimeoutAsync(600);
        var tabletForm = page.Locator("[data-tm-send-form]").First;
        var tabletPreview = page.Locator("[data-tm-send-preview]").First;
        var formBox = await tabletForm.BoundingBoxAsync();
        var previewBox = await tabletPreview.BoundingBoxAsync();
        formBox.Should().NotBeNull();
        previewBox.Should().NotBeNull();
        previewBox!.Y.Should().BeGreaterThan(formBox!.Y + 40, "at 1024 px the preview sits BELOW the form");
        Math.Abs(previewBox.X - formBox.X).Should().BeLessThanOrEqualTo(1, "one column: both start at the same edge");
        (await tabletPreview.EvaluateAsync<string>("el => getComputedStyle(el).position"))
            .Should().Be("static", "a single-column preview must not stick over the form");
        await page.SetViewportSizeAsync(390, 844);
        await page.WaitForTimeoutAsync(600);

        await page.Locator("[data-tm-to]").FillAsync(UniqueRecipient("f11-sticky"));
        var submit = page.Locator("[data-tm-send-submit]").First;
        await submit.ScrollIntoViewIfNeededAsync();
        // The sticky box is the action BAR around the button (a sticky button alone would let its row scroll
        // away and take the button with it).
        var position = await page.Locator("[data-tm-send-actions]").First
            .EvaluateAsync<string>("el => getComputedStyle(el).position");
        position.Should().Be("sticky", "the primary action sticks to the phone's bottom edge");
        var box = await submit.BoundingBoxAsync();
        box.Should().NotBeNull();
        (box!.Y + box.Height).Should().BeLessThanOrEqualTo(844.5f, "Send is inside the viewport without scrolling");
    }

    // ── the server preview's superseded request ────────────────────────────────────────────

    [TestMethod]
    public async Task F11_7_Preview_SupersededServerRequest_IsAborted_AndNeverPaints()
    {
        const string staleMarker = "STALE-PREVIEW-MUST-NEVER-PAINT";
        var page = await OpenAsync($"/email-templates/edit/{WelcomeTemplateId}");
        await page.WaitForSelectorAsync("[data-tm-canvas-doc]", new() { Timeout = 30000 });

        var previewRequests = 0;
        var firstFailed = 0;
        var firstSeen = new TaskCompletionSource();
        var secondSeen = new TaskCompletionSource();

        await page.RouteAsync("**/api/email-templates/preview", async route =>
        {
            // The CORS preflight is a separate request to the same URL: let it through untouched, or the real
            // POST never leaves the browser and the spec would prove nothing about the editor.
            if (!string.Equals(route.Request.Method, "POST", StringComparison.OrdinalIgnoreCase))
            {
                await route.ContinueAsync();
                return;
            }

            var index = Interlocked.Increment(ref previewRequests);
            if (index == 1)
            {
                firstSeen.TrySetResult();
                // A SLOW first answer the user must never see. A superseded request has to be ABORTED, not
                // merely ignored once it lands.
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(6));
                    await route.FulfillAsync(new RouteFulfillOptions
                    {
                        Status = 200,
                        ContentType = "application/json",
                        Body = """{"html":"<html><body>""" + staleMarker + """</body></html>","text":"","subject":"","errors":[],"status":0}""",
                    });
                }
                catch (PlaywrightException)
                {
                    // Aborted by the editor: exactly what this spec is about.
                }

                return;
            }

            secondSeen.TrySetResult();
            await route.ContinueAsync();
        });

        page.RequestFailed += (_, request) =>
        {
            if (request.Url.Contains("/api/email-templates/preview", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref firstFailed);
            }
        };

        await page.Locator("[data-tm-preview-btn]").ClickAsync();
        await firstSeen.Task.WaitAsync(TimeSpan.FromSeconds(40));

        // A NEW render request supersedes the slow one. "Refresh preview" is the explicit way to ask for one
        // while the modal stays open (the device toggle only changes the frame WIDTH — the HTML it would ask
        // for is identical, so the preview correctly issues no second request).
        await page.Locator("[data-tm-preview-dialog-refresh]").First.ClickAsync();
        await secondSeen.Task.WaitAsync(TimeSpan.FromSeconds(40));

        // The second answer paints; the stale one must never reach the frame.
        var state = "absent";
        for (var i = 0; i < 150; i++)
        {
            state = await page.EvaluateAsync<string>(
                "() => document.querySelector('[data-tm-preview-root]')?.getAttribute('data-tm-preview-state') ?? 'absent'");
            // The component exposes the state name in lower case (data-tm-preview-state="rendered").
            if (state.Equals("rendered", StringComparison.OrdinalIgnoreCase)
                || state.Equals("failed", StringComparison.OrdinalIgnoreCase)
                || state.Equals("unavailable", StringComparison.OrdinalIgnoreCase)) break;
            await page.WaitForTimeoutAsync(200);
        }

        state.Should().BeEquivalentTo("rendered", "the refreshed render really reached the frame");
        await page.WaitForTimeoutAsync(500);
        var body = (await page.FrameLocator("[data-tm-preview-frame]").Locator("body").InnerTextAsync()).Trim();
        body.Should().NotBeNullOrWhiteSpace("the second render really painted");
        body.Should().NotContain(staleMarker, "a superseded preview must never reach the frame");

        // The abort is the mechanism (ignoring the late answer would be a different, weaker behaviour).
        for (var i = 0; i < 80 && Volatile.Read(ref firstFailed) == 0; i++)
        {
            await page.WaitForTimeoutAsync(100);
        }

        Volatile.Read(ref firstFailed).Should().BeGreaterThan(0,
            "superseding the render cancels the HTTP request, so the stale answer costs no bandwidth");
    }
}
