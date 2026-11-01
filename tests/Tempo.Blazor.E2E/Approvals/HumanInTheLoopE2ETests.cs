using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E.Approvals;

/// <summary>
/// E2E of the <c>/human-in-the-loop</c> demo page (WASM @ 7106): the approval card is decided with the keyboard
/// (Ctrl+Enter) and taken back through its Undo window, the twelve-item review queue resolves the active item
/// with its A/R/S shortcuts and auto-advances, the inline diff accepts two of the three AI changes and the
/// result line shows the resulting mixture, and axe-core finds no critical or serious violation inside the
/// page. The agent's permission request lives on <c>/ai-conversation</c>: it is granted for a duration and then
/// revoked in a real browser.
/// </summary>
/// <remarks>
/// Every spec asserts on the page's own state (<c>[data-testid='hitl-…']</c>), not on the component internals
/// alone, because the page is what a reader and the screenshots see. A real browser is required for the
/// keyboard path: the shortcuts are key-handler driven and bUnit can dispatch a synthetic event at any
/// element, while the browser only fires them when focus really is inside the card or the queue.
/// </remarks>
[TestClass]
public sealed class HumanInTheLoopE2ETests : WasmTestBase
{
    private const string AxeCdn = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js";
    private const string PagePath = "/human-in-the-loop";
    private const string RootSelector = "[data-testid='hitl-demo']";
    private const string ApprovalCard = "[data-testid='hitl-approval']";
    private const string QueueRoot = "[data-testid='hitl-queue']";

    /// <summary>The demo paragraph before the rewrite (the diff's old side).</summary>
    private const string OriginalParagraph =
        "The team reviewed the draft and decided to postpone the launch until the documentation is complete.";

    /// <summary>
    /// Accepting the first two changes and leaving the third one pending: the accepted wording replaces the
    /// first two places, the last one keeps the original text.
    /// </summary>
    private const string TwoOfThreeAcceptedResult =
        "The team reviewed the draft and agreed to delay the launch until the documentation is complete.";

    private const string ProposedParagraph =
        "The team reviewed the draft and agreed to delay the launch until the documentation is finished.";

    /// <summary>
    /// Counts the nodes inside <c>selector</c> that actually announce something. A ticking value (the approval
    /// countdown, the queue's position counter) carries <c>aria-live="off"</c>, so it is excluded on purpose.
    /// </summary>
    private const string LiveRegionCountScript =
        """(selector) => document.querySelector(selector).querySelectorAll('[aria-live]:not([aria-live="off"])').length""";

    private readonly List<string> _clientErrors = [];

    private async Task<IPage> OpenPageAsync(int width = 1440, int height = 1000)
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        page.PageError += (_, error) => { lock (_clientErrors) _clientErrors.Add("PAGEERROR: " + error); };
        page.Console += (_, message) =>
        {
            if (message.Type == "error" && message.Text.Contains("Unhandled exception", StringComparison.Ordinal))
            {
                lock (_clientErrors) _clientErrors.Add("CONSOLE: " + message.Text);
            }
        };

        await page.SetViewportSizeAsync(width, height);
        await GotoAppReadyAsync(page, $"{BaseUrl}{PagePath}");
        await page.Locator(RootSelector).WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        return page;
    }

    // ── Approval ───────────────────────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Approval_KeyboardDecisionCollapsesAndUndoRestoresTheRequest()
    {
        var page = await OpenPageAsync();

        var card = page.Locator(ApprovalCard);
        await Microsoft.Playwright.Assertions.Expect(card).ToHaveAttributeAsync("data-state", "pending");
        await Microsoft.Playwright.Assertions.Expect(page.Locator("[data-testid='hitl-approval-state']"))
            .ToHaveTextAsync("Pending");

        // Ctrl+Enter fires from anywhere inside the card, so a real focus + a real key press is the whole path.
        await page.Locator($"{ApprovalCard} [data-testid='approval-approve']").FocusAsync();
        await page.Keyboard.PressAsync("Control+Enter");

        await Microsoft.Playwright.Assertions.Expect(card).ToHaveAttributeAsync("data-state", "approved");
        await Microsoft.Playwright.Assertions.Expect(page.Locator($"{ApprovalCard} [data-testid='approval-summary-text']"))
            .ToContainTextAsync("Approved");
        await Microsoft.Playwright.Assertions.Expect(page.Locator("[data-testid='hitl-approval-state']"))
            .ToHaveTextAsync("Approved");
        await Microsoft.Playwright.Assertions.Expect(page.Locator("[data-testid='hitl-approval-decisions']"))
            .ToHaveTextAsync("1");

        // The Undo window is open (30 s), so the collapsed row offers it and the click restores the request.
        await page.Locator($"{ApprovalCard} [data-testid='approval-undo']").ClickAsync();

        await Microsoft.Playwright.Assertions.Expect(card).ToHaveAttributeAsync("data-state", "pending");
        await Microsoft.Playwright.Assertions.Expect(page.Locator("[data-testid='hitl-approval-state']"))
            .ToHaveTextAsync("Pending");
        await Microsoft.Playwright.Assertions.Expect(page.Locator("[data-testid='hitl-approval-undone']"))
            .ToHaveTextAsync("1");

        AssertNoClientErrors();
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Approval_ShowsALiveCountdownAndTheRiskInWords()
    {
        var page = await OpenPageAsync();

        var countdown = page.Locator($"{ApprovalCard} [data-testid='approval-countdown']");
        await Microsoft.Playwright.Assertions.Expect(countdown).ToBeVisibleAsync();

        // The remaining time is a <time> element so a reader tool can parse the deadline; it reads mm:ss, or
        // h:mm:ss once an hour or more remains.
        var clock = countdown.Locator("time");
        await Microsoft.Playwright.Assertions.Expect(clock).ToBeVisibleAsync();

        var text = (await clock.TextContentAsync() ?? string.Empty).Trim();
        Assert.IsTrue(text.Contains(':') && text.Length >= 4,
            $"the countdown must read as a time, but it reads '{text}'");
        await Microsoft.Playwright.Assertions.Expect(clock).ToHaveAttributeAsync("datetime", new System.Text.RegularExpressions.Regex(".+"));

        // A ticking clock must not be a live region; the card owns exactly one polite region of its own.
        await Microsoft.Playwright.Assertions.Expect(clock).ToHaveAttributeAsync("aria-live", "off");

        // The risk is always spelled out next to its colour (WCAG 1.4.1).
        await Microsoft.Playwright.Assertions.Expect(page.Locator($"{ApprovalCard} [data-testid='approval-risk']"))
            .ToContainTextAsync("High risk");

        AssertNoClientErrors();
    }

    // ── Review queue ───────────────────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Queue_ShortcutsApproveRejectAndSkipWithAutoAdvance()
    {
        var page = await OpenPageAsync();

        var rows = page.Locator($"{QueueRoot} [data-testid='review-item']");
        await Microsoft.Playwright.Assertions.Expect(rows).ToHaveCountAsync(12);
        await Microsoft.Playwright.Assertions.Expect(page.Locator("[data-testid='hitl-queue-pending']"))
            .ToHaveTextAsync("12");

        // The shortcuts read the queue's own key handler, so focus has to be inside the list: a real click on
        // the active row is what a reviewer does before reaching for the keyboard.
        await rows.First.ClickAsync();
        await page.Keyboard.PressAsync("a");

        await Microsoft.Playwright.Assertions.Expect(page.Locator("[data-testid='hitl-queue-pending']"))
            .ToHaveTextAsync("11");
        await Microsoft.Playwright.Assertions.Expect(page.Locator("[data-testid='hitl-queue-handled']"))
            .ToHaveTextAsync("1");
        await Microsoft.Playwright.Assertions.Expect(page.Locator("[data-testid='hitl-queue-last']"))
            .ToContainTextAsync("Approved");

        // Auto-advance moved the selection: exactly one row is selected and it is not the resolved one.
        await Microsoft.Playwright.Assertions.Expect(page.Locator($"{QueueRoot} [aria-selected='true']"))
            .ToHaveCountAsync(1);

        await page.Keyboard.PressAsync("r");
        await Microsoft.Playwright.Assertions.Expect(page.Locator("[data-testid='hitl-queue-pending']"))
            .ToHaveTextAsync("10");
        await Microsoft.Playwright.Assertions.Expect(page.Locator("[data-testid='hitl-queue-last']"))
            .ToContainTextAsync("Rejected");

        // Skip is the one verb that leaves the item pending; it only moves the selection on.
        await page.Keyboard.PressAsync("s");
        await Microsoft.Playwright.Assertions.Expect(page.Locator("[data-testid='hitl-queue-pending']"))
            .ToHaveTextAsync("10");

        // A restarted queue forgets the resolved set.
        await page.Locator("[data-testid='hitl-queue-reset']").ClickAsync();
        await Microsoft.Playwright.Assertions.Expect(page.Locator("[data-testid='hitl-queue-pending']"))
            .ToHaveTextAsync("12");

        AssertNoClientErrors();
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Queue_FocusModeShowsThePositionCounterAndBothPresentationsStay()
    {
        var page = await OpenPageAsync();

        await page.Locator("[data-testid='hitl-queue-mode-focus']").ClickAsync();

        await Microsoft.Playwright.Assertions.Expect(page.Locator($"{QueueRoot} [data-testid='review-focus']"))
            .ToBeVisibleAsync();
        await Microsoft.Playwright.Assertions.Expect(page.Locator($"{QueueRoot} [data-testid='review-position']"))
            .ToContainTextAsync("12");
        await Microsoft.Playwright.Assertions.Expect(page.Locator($"{QueueRoot} [data-testid='review-list']"))
            .ToHaveCountAsync(0);

        await page.Locator("[data-testid='hitl-queue-mode-list']").ClickAsync();

        await Microsoft.Playwright.Assertions.Expect(page.Locator($"{QueueRoot} [data-testid='review-item']"))
            .ToHaveCountAsync(12);

        AssertNoClientErrors();
    }

    // ── Inline diff ────────────────────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Diff_AcceptingTwoOfThreeChangesProducesTheMixedText()
    {
        var page = await OpenPageAsync();

        await Microsoft.Playwright.Assertions.Expect(page.Locator("[data-testid='hitl-diff-result']"))
            .ToHaveTextAsync(OriginalParagraph);
        await Microsoft.Playwright.Assertions.Expect(
                page.Locator("[data-testid='hitl-diff'] [data-testid='inline-diff-change']"))
            .ToHaveCountAsync(3);

        // Accept the first two changes: each one is its own decision (marker → Accept in the popover).
        for (var index = 0; index < 2; index++)
        {
            await page.Locator("[data-testid='hitl-diff'] [data-testid='inline-diff-marker']").Nth(index).ClickAsync();
            await page.Locator("[data-testid='hitl-diff'] [data-testid='inline-diff-accept']").ClickAsync();
        }

        await Microsoft.Playwright.Assertions.Expect(page.Locator("[data-testid='hitl-diff-result']"))
            .ToHaveTextAsync(TwoOfThreeAcceptedResult);
        await Microsoft.Playwright.Assertions.Expect(page.Locator("[data-testid='hitl-diff-accepted']"))
            .ToContainTextAsync("2");

        // The third change is still pending, so the result is neither of the two inputs.
        await Microsoft.Playwright.Assertions.Expect(page.Locator("[data-testid='hitl-diff-result']"))
            .Not.ToHaveTextAsync(ProposedParagraph);

        // The bulk actions reproduce the two ends exactly.
        await page.Locator("[data-testid='hitl-diff'] [data-testid='inline-diff-reject-all']").ClickAsync();
        await Microsoft.Playwright.Assertions.Expect(page.Locator("[data-testid='hitl-diff-result']"))
            .ToHaveTextAsync(OriginalParagraph);

        await page.Locator("[data-testid='hitl-diff-reset']").ClickAsync();
        await Microsoft.Playwright.Assertions.Expect(page.Locator("[data-testid='hitl-diff-accepted']"))
            .ToContainTextAsync("0");

        AssertNoClientErrors();
    }

    // ── Permission request (on the AI conversation page) ─────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Permission_GrantForADurationThenRevoke()
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await GotoAppReadyAsync(page, $"{BaseUrl}/ai-conversation");

        var request = page.Locator("[data-testid='ai-permission']");
        await request.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await Microsoft.Playwright.Assertions.Expect(request).ToHaveAttributeAsync("data-state", "pending");
        await Microsoft.Playwright.Assertions.Expect(page.Locator("[data-testid='ai-permission-tool']"))
            .ToContainTextAsync("web.search");

        // The timed scope reveals the duration group; the default duration is offered selected.
        await page.Locator("[data-testid='ai-permission-scope-for-duration']").CheckAsync();
        await Microsoft.Playwright.Assertions.Expect(page.Locator("[data-testid='ai-permission-durations']"))
            .ToBeVisibleAsync();

        await page.Locator("[data-testid='ai-permission-allow']").ClickAsync();

        await Microsoft.Playwright.Assertions.Expect(request).ToHaveAttributeAsync("data-state", "granted");
        await Microsoft.Playwright.Assertions.Expect(page.Locator("[data-testid='ai-permission-summary']"))
            .ToContainTextAsync("left");
        await Microsoft.Playwright.Assertions.Expect(page.Locator("[data-testid='ai-permission-demo-grant']"))
            .ToHaveTextAsync("Allow for a while");

        // Revoke hands the permission back: the request returns to its pending question…
        await page.Locator("[data-testid='ai-permission-revoke']").ClickAsync();

        await Microsoft.Playwright.Assertions.Expect(request).ToHaveAttributeAsync("data-state", "pending");
        await Microsoft.Playwright.Assertions.Expect(page.Locator("[data-testid='ai-permission-demo-grant']"))
            .ToContainTextAsync("no permission granted");
        await Microsoft.Playwright.Assertions.Expect(page.Locator("[data-testid='ai-permission-demo-revoked']"))
            .ToHaveTextAsync("1");

        AssertNoClientErrors();
    }

    // ── Accessibility ─────────────────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Page_HasNoCriticalOrSeriousAxeViolations()
    {
        var page = await OpenPageAsync();

        // The initial page: the approval card with its countdown, the twelve-row queue, the reviewable diff
        // and the suggestion card.
        await AssertAxeCleanAsync(page, RootSelector);

        // The richest state: one resolved queue item (a result row), two decided changes and a decided
        // suggestion — the states that add the collapsed halves and the undo buttons.
        var rows = page.Locator($"{QueueRoot} [data-testid='review-item']");
        await rows.First.ClickAsync();
        await page.Keyboard.PressAsync("a");

        for (var index = 0; index < 2; index++)
        {
            await page.Locator("[data-testid='hitl-diff'] [data-testid='inline-diff-marker']").Nth(index).ClickAsync();
            await page.Locator("[data-testid='hitl-diff'] [data-testid='inline-diff-accept']").ClickAsync();
        }

        await page.Locator("[data-testid='hitl-suggestion'] [data-testid='suggestion-accept']").ClickAsync();

        await AssertAxeCleanAsync(page, RootSelector);

        // Each decision surface owns exactly ONE polite live region, and the page adds none of its own: the
        // result line is visible text, not an announcement. The ticking values (the approval countdown, the
        // queue's position counter) are aria-live="off" on purpose, so they are not counted here.
        foreach (var (selector, expected) in new[]
                 {
                     ("[data-testid='hitl-approval']", 1),
                     ("[data-testid='hitl-queue']", 1),
                     ("[data-testid='hitl-diff']", 1),
                     ("[data-testid='hitl-suggestion']", 1),
                 })
        {
            var liveRegions = await page.EvaluateAsync<int>(
                LiveRegionCountScript,
                selector);
            Assert.AreEqual(expected, liveRegions, $"{selector} must own exactly one live region");
        }

        var pageRegions = await page.EvaluateAsync<int>(
            LiveRegionCountScript,
            RootSelector);
        Assert.AreEqual(4, pageRegions, "the page announces nothing itself — the four components do");

        AssertNoClientErrors();
    }

    // ── Visual review ─────────────────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Screenshots_LightDarkDesktopAndMobile()
    {
        var page = await OpenPageAsync(1440, 1000);

        // The review wants the decided surfaces too: a resolved queue item, two accepted changes and an
        // accepted suggestion are the states the components were built for.
        await page.Locator($"{QueueRoot} [data-testid='review-item']").First.ClickAsync();
        await page.Keyboard.PressAsync("a");
        for (var index = 0; index < 2; index++)
        {
            await page.Locator("[data-testid='hitl-diff'] [data-testid='inline-diff-marker']").Nth(index).ClickAsync();
            await page.Locator("[data-testid='hitl-diff'] [data-testid='inline-diff-accept']").ClickAsync();
        }

        await WaitForLayoutToSettleAsync(page, RootSelector);

        var directory = Path.Combine(
            FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "human-in-the-loop");
        Directory.CreateDirectory(directory);

        await CaptureAsync(page, directory, "human-in-the-loop-light-1440.png");

        await ToggleDarkModeAsync(page);
        await WaitForLayoutToSettleAsync(page, RootSelector);
        await CaptureAsync(page, directory, "human-in-the-loop-dark-1440.png");

        await ToggleDarkModeAsync(page);

        // The narrow band: the same page at a phone width, where the queue's actions move into the mobile bar.
        await page.SetViewportSizeAsync(375, 812);
        await WaitForLayoutToSettleAsync(page, RootSelector);
        await CaptureAsync(page, directory, "human-in-the-loop-light-375.png");

        await ToggleDarkModeAsync(page);
        await WaitForLayoutToSettleAsync(page, RootSelector);
        await CaptureAsync(page, directory, "human-in-the-loop-dark-375.png");

        AssertNoClientErrors();
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Screenshots_PermissionSectionLightDarkAndMobile()
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await GotoAppReadyAsync(page, $"{BaseUrl}/ai-conversation");

        const string SectionSelector = "[data-testid='ai-permission-demo']";
        await page.Locator(SectionSelector).WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });

        // The richest state is the granted one: the countdown row plus Revoke.
        await page.Locator("[data-testid='ai-permission-scope-for-duration']").CheckAsync();
        await page.Locator("[data-testid='ai-permission-allow']").ClickAsync();
        await Microsoft.Playwright.Assertions.Expect(page.Locator("[data-testid='ai-permission']"))
            .ToHaveAttributeAsync("data-state", "granted");

        await WaitForLayoutToSettleAsync(page, SectionSelector);

        var directory = Path.Combine(
            FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "human-in-the-loop");
        Directory.CreateDirectory(directory);

        await CaptureSectionAsync(page, directory, "permission-section-light-1440.png");

        await ToggleDarkModeAsync(page);
        await WaitForLayoutToSettleAsync(page, SectionSelector);
        await CaptureSectionAsync(page, directory, "permission-section-dark-1440.png");

        await ToggleDarkModeAsync(page);

        // The narrow band: the scope and duration radios stack, the actions stay 44px.
        await page.SetViewportSizeAsync(375, 812);
        await WaitForLayoutToSettleAsync(page, SectionSelector);
        await CaptureSectionAsync(page, directory, "permission-section-light-375.png");

        AssertNoClientErrors();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────

    private static async Task AssertAxeCleanAsync(IPage page, string hostSelector)
    {
        await page.AddScriptTagAsync(new PageAddScriptTagOptions { Url = AxeCdn });
        var violations = await page.EvaluateAsync<string[]>(
            """
            async (hostSelector) => {
                const host = document.querySelector(hostSelector) || document.body;
                const result = await axe.run(host, {
                    runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa'] },
                    resultTypes: ['violations']
                });
                return result.violations
                    .filter(v => v.impact === 'critical' || v.impact === 'serious')
                    .map(v => `${v.impact}: ${v.id} - ${v.help} (${v.nodes.map(n => n.target.join(' ')).join('; ')})`);
            }
            """, hostSelector);

        Assert.AreEqual(0, violations.Length,
            $"{hostSelector} must have no critical/serious a11y violations: {string.Join(" | ", violations)}");
    }

    /// <summary>
    /// Captures the whole page as one artifact. The page is taller than the viewport, so Playwright stitches it
    /// from several scroll positions — and the demo's own sticky top bar follows the viewport, which would paint
    /// it into the middle of the composite. It is hidden for the shot and restored right after; the hide has to
    /// be re-applied for EVERY capture because a theme toggle re-renders the layout and drops the inline style.
    /// </summary>
    private static async Task CaptureAsync(IPage page, string directory, string fileName)
    {
        await SetStickyHeaderVisibleAsync(page, false);
        try
        {
            await page.Locator(RootSelector).ScreenshotAsync(new LocatorScreenshotOptions
            {
                Path = Path.Combine(directory, fileName),
            });
        }
        finally
        {
            await SetStickyHeaderVisibleAsync(page, true);
        }
    }

    /// <summary>The same hide-stitch-restore dance for a section of another demo page.</summary>
    private static async Task CaptureSectionAsync(IPage page, string directory, string fileName)
    {
        await SetStickyHeaderVisibleAsync(page, false);
        try
        {
            await page.Locator("[data-testid='ai-permission-demo']").ScreenshotAsync(new LocatorScreenshotOptions
            {
                Path = Path.Combine(directory, fileName),
            });
        }
        finally
        {
            await SetStickyHeaderVisibleAsync(page, true);
        }
    }

    private static Task SetStickyHeaderVisibleAsync(IPage page, bool visible)
        => page.EvaluateAsync(
            """
            (visible) => {
                const header = document.querySelector('[data-demo-shell="main"] > .sticky');
                if (header) {
                    header.style.visibility = visible ? '' : 'hidden';
                }
            }
            """, visible);

    private void AssertNoClientErrors()
    {
        lock (_clientErrors)
        {
            Assert.IsTrue(_clientErrors.Count == 0,
                "Unhandled client-side errors occurred:\n" + string.Join("\n", _clientErrors));
        }
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
            {
                return directory;
            }

            directory = directory.Parent!;
        }

        throw new InvalidOperationException("repo root not found");
    }
}
