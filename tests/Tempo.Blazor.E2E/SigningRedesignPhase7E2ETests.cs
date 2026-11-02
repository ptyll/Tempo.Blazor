using System.Globalization;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Signing redesign phase 7 — the authoring-side I panels and rule/role editors
/// (<c>TmSubmissionStatusTimeline</c>, <c>TmAuditTrailViewer</c>, <c>TmPdfSignatureVerification</c>,
/// <c>TmShareLinkPanel</c>, <c>TmConditionBuilder</c>, <c>TmFormulaBuilder</c>,
/// <c>TmRecipientRoleEditor</c>) on the <c>/signing-components</c> demo. All seven live on one page,
/// so the suite opens the page once per viewport/theme and captures each component element.
/// <para>
/// Like the other generators the CAPTURE test only runs when <c>TM_WRITE_BASELINES</c> is set:
/// <code>
/// TM_WRITE_BASELINES=1 dotnet test tests/Tempo.Blazor.E2E --filter "SigningRedesignPhase7E2ETests"
/// </code>
/// The behaviour assertions live in <see cref="SigningRedesignPhase7BehaviorE2ETests"/> (always on).
/// </para>
/// </summary>
internal static class SigningRedesignPhase7Specs
{
    public const string SigningComponentsUrl = "/signing-components";

    /// <summary>The seven phase-7 screens and their demo test ids (share/verification have a
    /// second instance that shares the same component DOM).</summary>
    public static readonly (string Screen, string TestId)[] Screens =
    [
        ("submission-status-timeline", "submission-status-timeline"),
        ("audit-trail-viewer", "audit-trail-viewer"),
        ("pdf-signature-verification", "pdf-signature-verification"),
        ("share-link-panel", "share-link-panel"),
        ("condition-builder", "condition-builder"),
        ("formula-builder", "formula-builder"),
        ("recipient-role-editor", "recipient-role-editor-template"),
        ("recipient-role-editor-submission", "recipient-role-editor-submission")
    ];

    /// <summary>Hides the demo app bars; element captures stitch scrolled frames and the demo
    /// chrome would bleed in (the phase-3/5/6 convention).</summary>
    public const string CaptureChromeStyle =
        ".sticky.top-0, .fixed.top-0 { visibility: hidden !important; }";

    /// <summary>Paints the THEME SURFACE under every capture (phase 7 review): the demo shell's
    /// Tailwind background classes would otherwise leave the components on a hardcoded light
    /// colour (or a transparent frame in dark/indigo), so the dark captures could not be judged.
    /// The tokens follow the applied theme, so each theme/brand pass paints its own surface.</summary>
    public const string CaptureSurfaceStyle =
        "body, main, .max-w-7xl, [data-testid] { background: var(--tm-bg-page) !important; color: var(--tm-text-primary); }";

    /// <summary>At 1024 the demo shell's sidebar would classify every component container as
    /// narrow (the demo column is ~614px); drop the sidebar and let the cards span the viewport
    /// so the capture shows the desktop/tablet presentations (phase-3 convention).</summary>
    public const string WideLayoutStyle =
        " aside.w-72 { display: none !important; }" +
        " main { margin-left: 0 !important; max-width: none !important; width: auto !important; }" +
        " .max-w-7xl { max-width: none !important; }";

    public static string Phase7Dir
    {
        get
        {
            var dir = Path.Combine(FindRepoRoot().FullName,
                "tests", "Tempo.Blazor.E2E", "__screenshots__", "signing-redesign", "phase-7");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static async Task<IPage> OpenSigningComponentsAsync(
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
        await page.AddInitScriptAsync("() => localStorage.setItem('tm-demo-culture', 'en')");
        await page.GotoAsync($"{baseUrl}{SigningComponentsUrl}", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60_000
        });
        await waitForReady(page);
        await page.WaitForSelectorAsync("[data-testid='recipient-role-editor-template']",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 20_000 });
        return page;
    }

    public static async Task CaptureAllAsync(IPage page, int width, string theme, string brand, List<string> written)
    {
        await page.EvaluateAsync("() => window.scrollTo(0, 0)");
        await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = CaptureChromeStyle });
        await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = CaptureSurfaceStyle });
        await page.WaitForTimeoutAsync(150);

        foreach (var (screen, testId) in Screens)
        {
            var path = Path.Combine(Phase7Dir, $"{screen}--{width}--{theme}--{brand}.png");
            var element = page.Locator($"[data-testid='{testId}']").First;
            await element.ScrollIntoViewIfNeededAsync();
            await page.WaitForTimeoutAsync(120);
            await element.ScreenshotAsync(new LocatorScreenshotOptions { Path = path, Type = ScreenshotType.Png });
            Console.WriteLine($"[signing-phase-7] wrote {path}");
            written.Add(path);
        }

        // The VERIFIED state of the signature panel (phase 7 review): the empty state above only
        // shows the upload card — the demo's Verify action fills the result, so the dark/indigo
        // passes also capture the success banner + signature cards.
        var verification = page.Locator("[data-testid='pdf-signature-verification']").First;
        await verification.ScrollIntoViewIfNeededAsync();
        var verifyButton = verification.Locator(".tm-pdf-signature-verification__verify");
        if (await verifyButton.CountAsync() > 0)
        {
            await verifyButton.ClickAsync();
            await verification.Locator(".tm-pdf-signature-verification__banner--verified")
                .WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
            await page.WaitForTimeoutAsync(150);

            var verifiedPath = Path.Combine(Phase7Dir, $"pdf-signature-verification-verified--{width}--{theme}--{brand}.png");
            await verification.ScreenshotAsync(new LocatorScreenshotOptions { Path = verifiedPath, Type = ScreenshotType.Png });
            Console.WriteLine($"[signing-phase-7] wrote {verifiedPath}");
            written.Add(verifiedPath);
        }
    }

    public static ILocator GetByTestId(IPage page, string testId) =>
        page.Locator($"[data-testid='{testId}']").First;

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
/// Writes the phase-7 captures for the UX review: the seven panels/editors at 1440 (light/dark ×
/// default/indigo), then 1024 (touch), 390 and 320 in light/default — against the desktop
/// references <c>Tempo.Blazor.Signing/{submission-status-timeline,audit-trail-viewer,
/// pdf-signature-verification,share-link-panel,condition-builder,formula-builder,
/// recipient-role-editor}--&#123;desktop,tablet,mobile&#125;.png</c>. The dialog frame and app shell
/// of the references are HOST chrome (G12) — never built here.
/// </summary>
[TestClass]
public class SigningRedesignPhase7E2ETests : BaselineGeneratorTestBase
{
    [TestMethod]
    [Description("Writes the phase-7 panel/editor captures (1440/1024/390/320, light/dark x default/indigo)")]
    public async Task CaptureSigningRedesignPhase7Screens()
    {
        var written = new List<string>();

        // 1440 pass: every component in the light/dark x default/indigo matrix.
        foreach (var (dark, indigo, theme, brand) in new[]
                 {
                     (false, false, "light", "default"),
                     (true, false, "dark", "default"),
                     (false, true, "light", "indigo"),
                     (true, true, "dark", "indigo")
                 })
        {
            var page = await SigningRedesignPhase7Specs.OpenSigningComponentsAsync(
                Browser, BaseUrl, 1440, 900, touch: false, RegisterContext, WaitForAppReadyAsync);
            await SigningRedesignPhase5Specs.ApplyThemeAsync(page, dark, indigo);
            await SigningRedesignPhase7Specs.CaptureAllAsync(page, 1440, theme, brand, written);
            await page.Context.CloseAsync();
        }

        // 1024 touch: the tablet band with the demo sidebar dropped, so the components see wide
        // containers (the same trick the phase-3 captures use).
        var tablet = await SigningRedesignPhase7Specs.OpenSigningComponentsAsync(
            Browser, BaseUrl, 1024, 768, touch: true, RegisterContext, WaitForAppReadyAsync);
        await tablet.AddStyleTagAsync(new PageAddStyleTagOptions { Content = SigningRedesignPhase7Specs.WideLayoutStyle });
        await SigningRedesignPhase7Specs.CaptureAllAsync(tablet, 1024, "light", "default", written);
        await tablet.Context.CloseAsync();

        foreach (var (width, height) in new[] { (390, 844), (320, 700) })
        {
            var page = await SigningRedesignPhase7Specs.OpenSigningComponentsAsync(
                Browser, BaseUrl, width, height, touch: false, RegisterContext, WaitForAppReadyAsync);
            await SigningRedesignPhase7Specs.CaptureAllAsync(page, width, "light", "default", written);
            await page.Context.CloseAsync();
        }

        foreach (var path in written)
        {
            Assert.IsTrue(File.Exists(path), $"expected the phase-7 capture {Path.GetFileName(path)} to be written");
        }

        var files = Directory.GetFiles(SigningRedesignPhase7Specs.Phase7Dir, "*.png");
        Assert.IsTrue(
            files.Length >= written.Count,
            string.Create(CultureInfo.InvariantCulture,
                $"expected at least {written.Count} phase-7 PNGs with the --{{width}}--{{theme}}--{{brand}} naming, found {files.Length}"));
    }
}

/// <summary>
/// The phase-7 BEHAVIOUR suite (always on — not gated by <c>TM_WRITE_BASELINES</c>): the role
/// editor reorders by drag &amp; drop at 1440 and by the arrow buttons at 390 (the keyboard/touch
/// alternative, Q-S7), the audit tables lay out at 390 without horizontal page overflow, and the
/// new panel contracts hold in a real browser (live banner, switch toggle, labelled embed field,
/// parentheses on the operator grid, cursor-aware insertions).
/// </summary>
[TestClass]
public class SigningRedesignPhase7BehaviorE2ETests : WasmTestBase
{
    [TestMethod]
    [Description("At 1440: the role editor reorders roles through drag & drop")]
    public async Task RoleEditor_DragAndDrop_1440_ReordersRoles()
    {
        var page = await SigningRedesignPhase7Specs.OpenSigningComponentsAsync(
            Browser, BaseUrl, 1440, 900, touch: false, RegisterContext, WaitForAppReadyAsync);

        var editor = SigningRedesignPhase7Specs.GetByTestId(page, "recipient-role-editor-template");
        var cards = editor.Locator("[data-role-uuid]");
        var sourceUuid = await cards.Nth(1).GetAttributeAsync("data-role-uuid");
        var targetUuid = await cards.First.GetAttributeAsync("data-role-uuid");

        // HTML5 DnD simulation (the Notion suite's pattern): Playwright's pointer-based
        // DragToAsync does not reliably fire the dragstart/dragover/drop listeners Blazor
        // registers on container elements; the events dispatch directly on the CARDS (the
        // elements that carry the Blazor handlers) with a real DataTransfer. The drop INSERTS
        // BEFORE the target, so the dragged card is the SECOND one landing on the FIRST.
        await page.EvaluateAsync(
            """
            (args) => {
                const cards = Array.from(
                    document.querySelector("[data-testid='recipient-role-editor-template']")
                        .querySelectorAll('[data-role-uuid]'));
                const source = cards[1];
                const target = cards[0];
                if (!source || !target) return;

                const dt = new DataTransfer();
                const rect = target.getBoundingClientRect();
                const clientX = rect.left + 16;
                const clientY = rect.top + 16;

                source.dispatchEvent(new DragEvent('dragstart', { bubbles: true, cancelable: true, dataTransfer: dt }));
                target.dispatchEvent(new DragEvent('dragover', { bubbles: true, cancelable: true, dataTransfer: dt, clientX, clientY }));
                target.dispatchEvent(new DragEvent('drop', { bubbles: true, cancelable: true, dataTransfer: dt, clientX, clientY }));
                source.dispatchEvent(new DragEvent('dragend', { bubbles: true, cancelable: true, dataTransfer: dt }));
            }
            """,
            new { sourceUuid, targetUuid });

        var first = editor.Locator("[data-role-uuid]").First;
        await Assertions.Expect(first).ToHaveAttributeAsync("data-role-uuid", sourceUuid!,
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 5_000 });

        // The order badges renumber.
        await Assertions.Expect(editor.Locator(".tm-recipient-role-editor__order-badge").First)
            .ToContainTextAsync("1");
        Assert.AreNotEqual(sourceUuid, targetUuid, "the demo seeds two distinct roles");
    }

    [TestMethod]
    [Description("At 390: the role editor reorders roles through the arrow buttons (the a11y alternative)")]
    public async Task RoleEditor_Arrows_390_ReorderRoles()
    {
        var page = await SigningRedesignPhase7Specs.OpenSigningComponentsAsync(
            Browser, BaseUrl, 390, 844, touch: true, RegisterContext, WaitForAppReadyAsync);

        var editor = SigningRedesignPhase7Specs.GetByTestId(page, "recipient-role-editor-template");
        var secondUuid = await editor.Locator("[data-role-uuid]").Nth(1).GetAttributeAsync("data-role-uuid");

        var secondCard = editor.Locator("[data-role-uuid]").Nth(1);
        await secondCard.ScrollIntoViewIfNeededAsync();
        await secondCard.Locator(".tm-recipient-role-editor__move-up").ClickAsync();

        var first = editor.Locator("[data-role-uuid]").First;
        await Assertions.Expect(first).ToHaveAttributeAsync("data-role-uuid", secondUuid!);

        // The a11y alternative does not need the (hidden) drag handle: on a coarse-pointer
        // viewport the handle stays out of the way and the arrows carry the reorder.
        await Assertions.Expect(editor.Locator(".tm-recipient-role-editor__drag-handle").First)
            .ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 2_000 });
    }

    /// <summary>
    /// The remove action deletes the card the user is acting on — the focus must land on the
    /// toggle of the card that now occupies the removed position, not drop to &lt;body&gt;
    /// (phase 7 review).
    /// </summary>
    [TestMethod]
    [Description("At 1440: removing a role card moves the focus to the next card's toggle")]
    public async Task RoleEditor_RemoveMiddle_1440_FocusesNextCardsToggle()
    {
        var page = await SigningRedesignPhase7Specs.OpenSigningComponentsAsync(
            Browser, BaseUrl, 1440, 900, touch: false, RegisterContext, WaitForAppReadyAsync);

        var editor = SigningRedesignPhase7Specs.GetByTestId(page, "recipient-role-editor-template");
        var secondUuid = await editor.Locator("[data-role-uuid]").Nth(1).GetAttributeAsync("data-role-uuid");

        await editor.Locator("[data-role-uuid]").First.Locator(".tm-recipient-role-editor__remove").ClickAsync();

        // The first card is gone; the surviving card's toggle owns the focus. The hand-off runs
        // through the (first-call) module import, so poll for it instead of reading once.
        await Assertions.Expect(page.Locator("[data-testid='recipient-role-editor-template'] [data-role-uuid]"))
            .ToHaveCountAsync(1);
        await page.WaitForFunctionAsync(
            "uuid => document.activeElement?.closest('[data-role-uuid]')?.getAttribute('data-role-uuid') === uuid",
            secondUuid,
            new PageWaitForFunctionOptions { Timeout = 5_000 });
        var focusClass = await page.EvaluateAsync<string?>("() => document.activeElement?.getAttribute('class') ?? ''");
        Assert.IsTrue(focusClass!.Contains("tm-recipient-role-editor__toggle", StringComparison.Ordinal),
            $"the focus lands on the card's toggle, got: {focusClass}");
    }

    /// <summary>
    /// Move-up twice through the keyboard (Enter on the arrow buttons): the focus stays INSIDE the
    /// moved card the whole time — the same-direction button, or the opposite one once the edge
    /// disables it (phase 7 review).
    /// </summary>
    [TestMethod]
    [Description("At 1440: two Enter presses on the move buttons keep the focus inside the moved card")]
    public async Task RoleEditor_MoveUpTwiceViaEnter_1440_FocusStaysInsideMovedCard()
    {
        var page = await SigningRedesignPhase7Specs.OpenSigningComponentsAsync(
            Browser, BaseUrl, 1440, 900, touch: false, RegisterContext, WaitForAppReadyAsync);

        var editor = SigningRedesignPhase7Specs.GetByTestId(page, "recipient-role-editor-template");
        var movedCard = editor.Locator("[data-role-uuid]").Nth(1);
        await movedCard.ScrollIntoViewIfNeededAsync();
        var movedUuid = await movedCard.GetAttributeAsync("data-role-uuid");

        // First Enter: the card moves to the top slot, where its move-up disabled itself — the
        // focus lands on the card's move-down. The hand-off runs through the (first-call) module
        // import, so poll for it instead of reading once.
        await movedCard.Locator(".tm-recipient-role-editor__move-up").FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForFunctionAsync(
            "uuid => document.activeElement?.closest('[data-role-uuid]')?.getAttribute('data-role-uuid') === uuid",
            movedUuid,
            new PageWaitForFunctionOptions { Timeout = 5_000 });

        // Second Enter (now on the card's move-down): the card moves back to the last slot, where
        // the move-down disables itself — the focus lands on the card's move-up again.
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForFunctionAsync(
            "uuid => document.activeElement?.closest('[data-role-uuid]')?.getAttribute('data-role-uuid') === uuid"
            + " && (document.activeElement?.getAttribute('class') ?? '').includes('tm-recipient-role-editor__move-up')",
            movedUuid,
            new PageWaitForFunctionOptions { Timeout = 5_000 });
    }

    /// <summary>
    /// The condition builder mirrors the role editor's remove-focus contract: deleting a row moves
    /// the focus to the remove button of the row that now occupies the removed position (phase 7
    /// review).
    /// </summary>
    [TestMethod]
    [Description("At 1440: removing a condition row moves the focus to the next row's remove button")]
    public async Task ConditionBuilder_RemoveRow_1440_FocusesNextRowsRemove()
    {
        var page = await SigningRedesignPhase7Specs.OpenSigningComponentsAsync(
            Browser, BaseUrl, 1440, 900, touch: false, RegisterContext, WaitForAppReadyAsync);

        var builder = SigningRedesignPhase7Specs.GetByTestId(page, "condition-builder");
        await builder.ScrollIntoViewIfNeededAsync();

        // A second row makes the removed position meaningful.
        await builder.Locator(".tm-condition-builder__add").ClickAsync();
        await Assertions.Expect(builder.Locator(".tm-condition-builder__row")).ToHaveCountAsync(2);

        var secondRowId = await builder.Locator(".tm-condition-builder__row").Nth(1).GetAttributeAsync("data-row-id");
        await builder.Locator(".tm-condition-builder__row").First.Locator(".tm-condition-builder__remove").ClickAsync();

        await Assertions.Expect(builder.Locator(".tm-condition-builder__row")).ToHaveCountAsync(1);
        // The hand-off runs through the module import — poll for the focus, don't read once.
        await page.WaitForFunctionAsync(
            "rowId => document.activeElement?.closest('.tm-condition-builder__row')?.getAttribute('data-row-id') === rowId"
            + " && (document.activeElement?.getAttribute('class') ?? '').includes('tm-condition-builder__remove')",
            secondRowId,
            new PageWaitForFunctionOptions { Timeout = 5_000 });
    }

    [TestMethod]
    [Description("At 390: the audit tables collapse to cards without horizontal page overflow")]
    public async Task AuditTrailViewer_390_TablesFitWithoutHorizontalOverflow()
    {
        var page = await SigningRedesignPhase7Specs.OpenSigningComponentsAsync(
            Browser, BaseUrl, 390, 844, touch: false, RegisterContext, WaitForAppReadyAsync);

        var viewer = SigningRedesignPhase7Specs.GetByTestId(page, "audit-trail-viewer");
        await viewer.ScrollIntoViewIfNeededAsync();

        // The tables still carry their semantics…
        await Assertions.Expect(viewer.Locator("table")).ToHaveCountAsync(3);
        await Assertions.Expect(viewer.Locator("th[scope='col']").First).ToBeAttachedAsync();

        // …and the page gains NO horizontal scroll (the container query collapsed the rows to
        // cards instead of clipping a 5-column table into a 390px column).
        var overflow = await page.EvaluateAsync<double>(
            "() => document.documentElement.scrollWidth - document.documentElement.clientWidth");
        Assert.IsTrue(overflow <= 1,
            string.Create(CultureInfo.InvariantCulture,
                $"expected no horizontal page overflow at 390, got {overflow}px"));
    }

    [TestMethod]
    [Description("The new panel contracts hold in a real browser: live banner, switch, labelled embed field, parens, cursor insert")]
    public async Task PanelContracts_RenderOnTheDemo()
    {
        var page = await SigningRedesignPhase7Specs.OpenSigningComponentsAsync(
            Browser, BaseUrl, 1440, 900, touch: false, RegisterContext, WaitForAppReadyAsync);

        // Verification: the status banner is a live region with the status modifier.
        var verification = SigningRedesignPhase7Specs.GetByTestId(page, "pdf-signature-verification");
        await verification.Locator(".tm-pdf-signature-verification__verify").ClickAsync();
        var banner = verification.Locator(".tm-pdf-signature-verification__banner");
        await Assertions.Expect(banner).ToHaveAttributeAsync("role", "status");
        await Assertions.Expect(banner).ToContainTextAsync("PDF verified");
        var checksumValue = await verification.Locator(".tm-pdf-signature-verification__checksum-value").TextContentAsync();
        Assert.IsTrue(checksumValue!.Contains("sha256-7f2c-demo", StringComparison.Ordinal),
            "a 16-character checksum displays in full");

        // Share panel: the enable control is a real switch (the core TmToggle renders its native
        // input visually hidden, so assert attached + state, not visibility) and the embed label
        // names its field.
        var share = SigningRedesignPhase7Specs.GetByTestId(page, "share-link-panel");
        await Assertions.Expect(share.Locator("input[role='switch']")).ToBeAttachedAsync();
        await Assertions.Expect(share.Locator("input[role='switch']")).ToHaveAttributeAsync("aria-checked", "true");
        var embedLabelFor = await share.Locator("label.tm-share-link-panel__embed-label").GetAttributeAsync("for");
        var textareaId = await share.Locator(".tm-share-link-panel__embed-code").GetAttributeAsync("id");
        Assert.AreEqual(textareaId, embedLabelFor, "the embed label points at the textarea");
        await Assertions.Expect(share.Locator(".tm-share-link-panel__qr-hint")).ToContainTextAsync("Scan the QR code");

        // Formula builder: parentheses ride the operator grid and a token card shows its type.
        var formula = SigningRedesignPhase7Specs.GetByTestId(page, "formula-builder");
        await Assertions.Expect(formula.Locator("[data-operator='(']")).ToBeVisibleAsync();
        var tokenType = await formula.Locator(".tm-formula-builder__token-type").First.TextContentAsync();
        Assert.IsFalse(string.IsNullOrWhiteSpace(tokenType), "every token card names its field type");

        // Condition builder: the dashed add row creates a second row with an operation select.
        var conditions = SigningRedesignPhase7Specs.GetByTestId(page, "condition-builder");
        await conditions.Locator(".tm-condition-builder__add").ClickAsync();
        await Assertions.Expect(conditions.Locator(".tm-condition-builder__operation select")).ToBeVisibleAsync();

        // Timeline: the "Recipient:" prefix and the HeaderContent-less default layout.
        var timeline = SigningRedesignPhase7Specs.GetByTestId(page, "submission-status-timeline");
        await Assertions.Expect(timeline.Locator(".tm-submission-status-timeline__recipient-prefix").First)
            .ToContainTextAsync("Recipient:");
        await Assertions.Expect(timeline.Locator(".tm-submission-status-timeline__marker--soft").First)
            .ToBeVisibleAsync();
    }
}
