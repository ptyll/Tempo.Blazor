using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;

namespace Tempo.Blazor.E2E.AI;

/// <summary>
/// E2E of the <c>/ai-conversation</c> demo page (WASM @ 7106): the simulated delta stream completes and the
/// answer carries a markdown table, a code block, two citation chips, the reasoning panel and two tool-call
/// cards (one failed and retryable); Stop keeps the partial answer and marks it stopped; a citation chip opens
/// the source preview, Escape closes it and focus returns to the chip; the composer keeps its combobox and
/// Enter/Shift+Enter contract in a real browser; and axe-core finds no critical or serious violation inside the
/// conversation. Screenshots land in <c>__screenshots__/ai-conversation/</c> for the light/dark and mobile
/// visual review.
/// </summary>
/// <remarks>
/// The stream is the page's own generator (40 ms per delta, ~2 s per answer), so a spec that has to interrupt
/// an answer clicks Stop while the pill is on screen. The page starts empty, so every spec begins by clicking
/// the first suggestion prompt.
/// </remarks>
[TestClass]
public sealed class AiConversationE2ETests : WasmTestBase
{
    private const string AxeCdn = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js";
    private const string PagePath = "/ai-conversation";
    private const string AnswerSelector =
        ".tm-ai-conversation [data-testid='ai-message'][data-role='assistant']";

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

        await page.Locator("[data-testid='ai-conversation']").WaitForAsync(
            new LocatorWaitForOptions { Timeout = 30000 });
        // The empty state is the page's first paint; it must be there before a suggestion is clicked.
        await page.Locator("[data-testid='ai-conversation-empty']").WaitForAsync(
            new LocatorWaitForOptions { Timeout = 30000 });
        return page;
    }

    private static async Task StartAnswerAsync(IPage page)
    {
        await page.Locator("[data-testid='ai-conversation-suggestion']").First.ClickAsync();
    }

    private static async Task WaitForCompletedAnswerAsync(IPage page)
    {
        await page.Locator($"{AnswerSelector}[data-status='completed']").WaitForAsync(
            new LocatorWaitForOptions { Timeout = 30000 });
    }

    // ── Streaming ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Streaming_CompletesWithTableCodeCitationsReasoningAndToolCalls()
    {
        var page = await OpenPageAsync();
        await StartAnswerAsync(page);
        await WaitForCompletedAnswerAsync(page);

        var answer = page.Locator(AnswerSelector);

        // One user turn and one assistant turn are in the transcript.
        await Microsoft.Playwright.Assertions.Expect(
                page.Locator(".tm-ai-conversation [data-testid='ai-message']"))
            .ToHaveCountAsync(2);

        await Microsoft.Playwright.Assertions.Expect(answer.Locator("table")).ToHaveCountAsync(1);
        await Microsoft.Playwright.Assertions.Expect(answer.Locator("pre code")).Not.ToHaveCountAsync(0);
        await Microsoft.Playwright.Assertions.Expect(answer.Locator(".tm-markdown-view__citation"))
            .ToHaveCountAsync(2);

        // Reasoning and the two tool calls (the second one failed, so it offers Retry).
        await Microsoft.Playwright.Assertions.Expect(answer.Locator("[data-testid='ai-reasoning']"))
            .ToBeVisibleAsync();
        await Microsoft.Playwright.Assertions.Expect(answer.Locator("[data-testid='tool-call']"))
            .ToHaveCountAsync(2);
        await Microsoft.Playwright.Assertions.Expect(answer.Locator(".tm-tool-call--failed")).ToHaveCountAsync(1);
        await Microsoft.Playwright.Assertions.Expect(answer.Locator(".tm-tool-call--failed .tm-tool-call__retry"))
            .ToBeVisibleAsync();

        // The cited sources render as cards, and the answer offers its follow-up chips.
        await Microsoft.Playwright.Assertions.Expect(answer.Locator(".tm-ai-message__source")).ToHaveCountAsync(2);
        await Microsoft.Playwright.Assertions.Expect(answer.Locator(".tm-ai-message__follow-up"))
            .Not.ToHaveCountAsync(0);

        // The page records the finished turn.
        await Microsoft.Playwright.Assertions.Expect(page.Locator("[data-testid='ai-demo-message-count']"))
            .ToHaveTextAsync("2");
        await Microsoft.Playwright.Assertions.Expect(page.Locator("[data-testid='ai-demo-version-count']"))
            .ToHaveTextAsync("1");

        AssertNoClientErrors();
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task FailedToolCall_RetryTurnsItSucceeded()
    {
        var page = await OpenPageAsync();
        await StartAnswerAsync(page);
        await WaitForCompletedAnswerAsync(page);

        await Microsoft.Playwright.Assertions.Expect(page.Locator("[data-testid='ai-demo-tool-retries']"))
            .ToHaveTextAsync("0");

        await page.Locator($"{AnswerSelector} .tm-tool-call--failed .tm-tool-call__retry").ClickAsync();

        await Microsoft.Playwright.Assertions.Expect(page.Locator(AnswerSelector).Locator(".tm-tool-call--failed"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });
        await Microsoft.Playwright.Assertions.Expect(page.Locator("[data-testid='ai-demo-tool-retries']"))
            .ToHaveTextAsync("1");

        AssertNoClientErrors();
    }

    // ── Stop ───────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Stop_KeepsThePartialAnswerAndMarksItStopped()
    {
        var page = await OpenPageAsync();
        await StartAnswerAsync(page);

        // The pill exists only while the generation runs, so the wait doubles as the "is streaming" gate.
        var stop = page.Locator("[data-testid='ai-conversation-stop']");
        await stop.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000, State = WaitForSelectorState.Visible });
        await stop.ClickAsync();

        await Microsoft.Playwright.Assertions.Expect(page.Locator($"{AnswerSelector}[data-status='stopped']"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });

        // The partial answer is kept (not thrown away) and the stop control disappeared.
        await Microsoft.Playwright.Assertions.Expect(page.Locator("[data-testid='ai-conversation-stop']"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });
        await Microsoft.Playwright.Assertions.Expect(page.Locator(AnswerSelector).Locator(".tm-ai-message__state"))
            .ToBeVisibleAsync();
        await Microsoft.Playwright.Assertions.Expect(page.Locator("[data-testid='ai-demo-message-count']"))
            .ToHaveTextAsync("2");

        AssertNoClientErrors();
    }

    // ── Citation preview ───────────────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task CitationChip_OpensThePreview_AndEscapeReturnsFocusToTheChip()
    {
        var page = await OpenPageAsync();
        await StartAnswerAsync(page);
        await WaitForCompletedAnswerAsync(page);

        var chip = page.Locator($"{AnswerSelector} .tm-markdown-view__citation").First;
        await chip.ClickAsync();

        var preview = page.Locator("[data-testid='ai-message-citation-preview']");
        await Microsoft.Playwright.Assertions.Expect(preview).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        await Microsoft.Playwright.Assertions.Expect(preview.Locator(".tm-ai-message__preview-title"))
            .ToContainTextAsync("AI conversation");

        // Focus moved INTO the panel (its close button), so a keyboard reader is not left behind the dialog.
        var focusInsidePanel = await page.EvaluateAsync<bool>(
            """
            () => {
                const panel = document.querySelector("[data-testid='ai-message-citation-preview']");
                return !!panel && !!document.activeElement && panel.contains(document.activeElement);
            }
            """);
        Assert.IsTrue(focusInsidePanel, "opening the citation preview must move the focus into the panel");

        // Escape dismisses it and hands the focus back to the chip that opened it.
        await page.Keyboard.PressAsync("Escape");
        await Microsoft.Playwright.Assertions.Expect(preview).ToBeHiddenAsync(
            new LocatorAssertionsToBeHiddenOptions { Timeout = 10000 });
        var focusedSourceId = await page.EvaluateAsync<string?>(
            "() => document.activeElement ? document.activeElement.getAttribute('data-source-id') : null");
        Assert.AreEqual("docs", focusedSourceId,
            "closing the preview must return the focus to the citation chip that opened it");

        AssertNoClientErrors();
    }

    // ── Composer keyboard ──────────────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Composer_KeepsItsComboboxAndEnterContract()
    {
        var page = await OpenPageAsync();

        var input = page.Locator("[data-testid='ai-composer-input']");
        var listbox = page.Locator("[data-testid='ai-composer-suggestions']");

        // "/" opens the command listbox; the textarea is its combobox and keeps the DOM focus.
        await input.ClickAsync();
        await input.FillAsync("/");
        await Microsoft.Playwright.Assertions.Expect(listbox).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        await Microsoft.Playwright.Assertions.Expect(input).ToHaveAttributeAsync("aria-expanded", "true");
        var focusedIsInput = await page.EvaluateAsync<bool>(
            "() => document.activeElement === document.querySelector(\"[data-testid='ai-composer-input']\")");
        Assert.IsTrue(focusedIsInput, "the combobox keeps the focus while its listbox is open");

        // Escape closes it again without sending anything.
        await page.Keyboard.PressAsync("Escape");
        await Microsoft.Playwright.Assertions.Expect(listbox).ToBeHiddenAsync(
            new LocatorAssertionsToBeHiddenOptions { Timeout = 10000 });
        await Microsoft.Playwright.Assertions.Expect(input).ToHaveAttributeAsync("aria-expanded", "false");

        // Shift+Enter inserts a line break instead of sending.
        await input.FillAsync("First line");
        await page.Keyboard.PressAsync("Shift+Enter");
        await page.Keyboard.TypeAsync("Second line");
        var draft = await input.InputValueAsync();
        Assert.IsTrue(draft.Contains('\n', StringComparison.Ordinal),
            $"Shift+Enter must keep the line inside the prompt, got: '{draft}'");
        await Microsoft.Playwright.Assertions.Expect(
                page.Locator(".tm-ai-conversation [data-testid='ai-message']"))
            .ToHaveCountAsync(0);

        // Enter sends: the user turn lands in the transcript and the answer streams.
        await input.FillAsync("Summarise the plan");
        await page.Keyboard.PressAsync("Enter");

        await Microsoft.Playwright.Assertions.Expect(
                page.Locator(".tm-ai-conversation [data-testid='ai-message'][data-role='user']"))
            .ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });
        await Microsoft.Playwright.Assertions.Expect(
                page.Locator(".tm-ai-conversation [data-testid='ai-message'][data-role='user']"))
            .ToContainTextAsync("Summarise the plan");

        await WaitForCompletedAnswerAsync(page);
        Assert.AreEqual(string.Empty, await input.InputValueAsync(),
            "the composer clears its draft after a successful submit");

        AssertNoClientErrors();
    }

    // ── Transcript scroll: the box-own resize report ─────────────────────────────────────

    /// <summary>
    /// A reader parked at the bottom stays there when the host shrinks the transcript box: the module's
    /// box report re-aligns the follow and the re-asserting jump (no animation) keeps the scroll gap
    /// inside the 48 px band.
    /// </summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task BoxShrink_AtTheBottom_KeepsTheReaderAtTheBottom()
    {
        var page = await OpenPageAsync();
        await StartAnswerAsync(page);
        await WaitForCompletedAnswerAsync(page);

        await page.EvaluateAsync<string>(
            "() => { document.querySelector('.tm-ai-conversation').style.blockSize = '20rem'; }");

        var atBottom = false;
        string? lastGap = null;
        for (var attempt = 0; attempt < 50 && !atBottom; attempt++)
        {
            await Task.Delay(100);
            lastGap = await page.EvaluateAsync<string>(
                "() => { const log = document.querySelector('.tm-ai-conversation__log'); " +
                "return String(log.scrollHeight - log.scrollTop - log.clientHeight); }");
            atBottom = double.Parse(lastGap, System.Globalization.CultureInfo.InvariantCulture) <= 48;
        }

        Assert.IsTrue(atBottom,
            $"the shrink must leave the bottom-riding reader at the new bottom — last gap {lastGap}px");

        AssertNoClientErrors();
    }

    /// <summary>
    /// A reader who scrolled UP is not dragged anywhere by a box shrink and gets no "New messages" pill:
    /// the shrink is not new content (nothing is generating), so their position and their reading state
    /// are left alone.
    /// </summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task BoxShrink_WithTheReaderScrolledUp_DoesNotMoveThemAndPopsNoPill()
    {
        var page = await OpenPageAsync();
        await StartAnswerAsync(page);
        await WaitForCompletedAnswerAsync(page);

        // A real scroll (not a forced property write alone): the module's scroll handler sees the
        // crossing and reports the reader as detached.
        await page.EvaluateAsync<string>(
            "() => { const log = document.querySelector('.tm-ai-conversation__log'); log.scrollTop = 0; }");
        var detached = false;
        for (var attempt = 0; attempt < 30 && !detached; attempt++)
        {
            await Task.Delay(100);
            detached = await page.EvaluateAsync<bool>(
                "() => { const log = document.querySelector('.tm-ai-conversation__log'); return log.scrollTop < 50; }");
        }

        Assert.IsTrue(detached, "the reader must actually sit at the top before the shrink");
        var scrollTopBefore = await page.EvaluateAsync<double>(
            "() => document.querySelector('.tm-ai-conversation__log').scrollTop");

        await page.EvaluateAsync<string>(
            "() => { document.querySelector('.tm-ai-conversation').style.blockSize = '20rem'; }");
        await Task.Delay(600);

        var scrollTopAfter = await page.EvaluateAsync<double>(
            "() => document.querySelector('.tm-ai-conversation__log').scrollTop");
        Assert.AreEqual(scrollTopBefore, scrollTopAfter,
            "a detached reader is never dragged to the bottom by the host resizing the box");
        await Microsoft.Playwright.Assertions.Expect(
                page.Locator("[data-testid='ai-conversation-new-messages']"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 5000 });

        AssertNoClientErrors();
    }

    /// <summary>
    /// The user turn paints its OWN token (`--tm-ai-user-bubble-bg`) with the 1 px subtle border — never
    /// again the neutral secondary surface, which IS the page colour in the dark theme. The resolved
    /// computed style is read in BOTH themes straight from the running page.
    /// </summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task UserBubble_PaintsItsOwnToken_WithTheSubtleBorder_InBothThemes()
    {
        var page = await OpenPageAsync();
        await StartAnswerAsync(page);
        await WaitForCompletedAnswerAsync(page);

        async Task<(string Bubble, string Surface, string Border)> ReadBubblePaintAsync()
        {
            var json = await page.EvaluateAsync<string>(
                """
                () => {
                    const bubble = document.querySelector(".tm-ai-message--user .tm-ai-message__body");
                    if (!bubble) {
                        return "{}";
                    }
                    const style = getComputedStyle(bubble);
                    return JSON.stringify({
                        bubble: style.backgroundColor,
                        surface: getComputedStyle(document.querySelector("[data-testid='ai-demo-conversation-host']")).backgroundColor,
                        border: style.borderTopWidth,
                    });
                }
                """);
            using var document = JsonDocument.Parse(json);
            return (document.RootElement.GetProperty("bubble").GetString() ?? "",
                document.RootElement.GetProperty("surface").GetString() ?? "",
                document.RootElement.GetProperty("border").GetString() ?? "");
        }

        var (bubble, surface, border) = await ReadBubblePaintAsync();
        Assert.AreNotEqual(surface, bubble,
            $"the light bubble must not paint the neutral surface colour (bubble {bubble}, surface {surface})");
        Assert.AreEqual("1px", border, "the bubble carries the 1 px subtle border in light");

        await ToggleDarkModeAsync(page);
        await Microsoft.Playwright.Assertions.Expect(page.Locator(".tm-ai-message--user .tm-ai-message__body"))
            .ToBeVisibleAsync();

        var (darkBubble, darkSurface, darkBorder) = await ReadBubblePaintAsync();
        Assert.AreNotEqual(darkSurface, darkBubble,
            $"the dark bubble must differ from the surface — and from the page, which the old default was " +
            $"(bubble {darkBubble}, surface {darkSurface})");
        Assert.AreEqual("1px", darkBorder, "the bubble keeps the 1 px subtle border in dark");

        AssertNoClientErrors();
    }

    /// <summary>
    /// Fáze 10 E: at 375 px Auto becomes a nowrap row. Tabbing to the third chip reveals it fully
    /// inside the row and leaves the transcript's scrollTop where it was.
    /// </summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task Suggestions_ScrollRowAt375_RevealsTheThirdChipWithoutMovingTheLog()
    {
        var page = await OpenPageAsync(width: 375, height: 812);

        var log = page.Locator("[data-testid='ai-conversation-log']");
        var row = page.Locator(".tm-ai-conversation__suggestions");
        var chips = page.Locator("[data-testid='ai-conversation-suggestion']");
        await Microsoft.Playwright.Assertions.Expect(chips).ToHaveCountAsync(3);
        await Microsoft.Playwright.Assertions.Expect(row).ToHaveAttributeAsync("data-layout", "auto");

        var logTopBefore = await log.EvaluateAsync<double>("el => el.scrollTop");

        await chips.Nth(2).FocusAsync();
        try
        {
            await page.WaitForFunctionAsync("""
                () => {
                    const row = document.querySelector('.tm-ai-conversation__suggestions');
                    const chip = document.querySelectorAll('[data-testid="ai-conversation-suggestion"]')[2];
                    if (!row || !chip) return false;
                    const r = row.getBoundingClientRect();
                    const c = chip.getBoundingClientRect();
                    return c.left >= r.left - 1 && c.right <= r.right + 1;
                }
                """, null, new PageWaitForFunctionOptions { Timeout = 5000 });
        }
        catch (TimeoutException)
        {
            var diag = await page.EvaluateAsync<string>("""
                () => {
                    const row = document.querySelector('.tm-ai-conversation__suggestions');
                    const chip = document.querySelectorAll('[data-testid="ai-conversation-suggestion"]')[2];
                    const conv = document.querySelector('.tm-ai-conversation');
                    if (!row || !chip) return 'missing-row-or-chip';
                    const cs = getComputedStyle(row);
                    const r = row.getBoundingClientRect();
                    const c = chip.getBoundingClientRect();
                    return JSON.stringify({
                        layout: row.getAttribute('data-layout'),
                        wrap: cs.flexWrap,
                        overflowX: cs.overflowX,
                        convW: conv && conv.getBoundingClientRect().width,
                        row: { left: r.left, right: r.right, w: r.width, scrollLeft: row.scrollLeft, scrollWidth: row.scrollWidth, clientWidth: row.clientWidth },
                        chip: { left: c.left, right: c.right, w: c.width },
                        active: document.activeElement && document.activeElement.getAttribute('data-testid')
                    });
                }
                """);
            Assert.Fail("third chip was not revealed inside the row: " + diag);
        }

        var logTopAfter = await log.EvaluateAsync<double>("el => el.scrollTop");
        Assert.AreEqual(logTopBefore, logTopAfter, 0.5, "revealing a chip must not scroll the transcript");

        AssertNoClientErrors();
    }

    /// <summary>
    /// Fáze 10 G: at 375 px the sticky composer clears a 56 px host bar through
    /// <c>--tm-ai-composer-bottom-clearance</c>; a larger keyboard inset wins the <c>max()</c>.
    /// </summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task Composer_At375_ClearsA56PxHostBar_AndKeyboardInsetWins()
    {
        var page = await OpenPageAsync(width: 375, height: 812);
        var composer = page.Locator("[data-testid='ai-composer']");
        await Microsoft.Playwright.Assertions.Expect(composer).ToBeVisibleAsync();
        // Sticky bottom only engages once the composer meets the viewport edge;
        // the demo page is taller than 812 px, so bring it to the fold first.
        await composer.ScrollIntoViewIfNeededAsync();

        await page.EvaluateAsync("""
            document.documentElement.style.setProperty('--tm-ai-composer-bottom-clearance', '56px');
            """);

        var gapAfterBar = await page.EvaluateAsync<double>("""
            () => {
                const c = document.querySelector('[data-testid="ai-composer"]');
                return window.innerHeight - c.getBoundingClientRect().bottom;
            }
            """);
        Assert.IsTrue(gapAfterBar >= 55,
            $"the sticky composer must clear a 56 px host bar (gap was {gapAfterBar:0.#} px)");

        await page.EvaluateAsync("""
            document.querySelector('[data-testid="ai-composer"]')
                .style.setProperty('--tm-ai-composer-keyboard-inset', '120px');
            """);

        var gapAfterKeyboard = await page.EvaluateAsync<double>("""
            () => {
                const c = document.querySelector('[data-testid="ai-composer"]');
                return window.innerHeight - c.getBoundingClientRect().bottom;
            }
            """);
        Assert.IsTrue(gapAfterKeyboard >= 119,
            $"a 120 px keyboard inset must win over the 56 px bar (gap was {gapAfterKeyboard:0.#} px)");

        AssertNoClientErrors();
    }

    // ── Accessibility ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Conversation_HasNoCriticalOrSeriousAxeViolations()
    {
        var page = await OpenPageAsync();

        // The empty state (suggestions + composer) first…
        await AssertAxeCleanAsync(page, "[data-testid='ai-conversation-demo']");

        await StartAnswerAsync(page);
        await WaitForCompletedAnswerAsync(page);

        // …then the richest state: the finished answer with sources, tool calls and follow-ups.
        await AssertAxeCleanAsync(page, "[data-testid='ai-conversation-demo']");

        // The transcript is the ONLY element in the conversation that declares aria-live: the per-turn and
        // per-tool-call status regions belong to those child components (each owns exactly one) and are what a
        // reader hears for a single turn — the transcript must not add a second, competing announcement channel.
        var liveRegions = await page.EvaluateAsync<int>(
            "() => document.querySelector(\".tm-ai-conversation\").querySelectorAll('[aria-live]').length");
        Assert.AreEqual(1, liveRegions, "the transcript must be the conversation's only aria-live element");
        await Microsoft.Playwright.Assertions.Expect(page.Locator(".tm-ai-conversation [aria-live]"))
            .ToHaveAttributeAsync("role", "log");

        // The composer next to it keeps its own single live region for the attachment/voice/mention messages.
        var composerLiveRegions = await page.EvaluateAsync<int>(
            "() => document.querySelector(\"[data-testid='ai-composer']\").querySelectorAll('[aria-live]').length");
        Assert.AreEqual(1, composerLiveRegions, "the composer keeps exactly one live region");

        AssertNoClientErrors();
    }

    // ── Visual review ─────────────────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Screenshots_LightDarkDesktopAndMobile()
    {
        var page = await OpenPageAsync(1440, 1000);
        await StartAnswerAsync(page);
        await WaitForCompletedAnswerAsync(page);
        await WaitForLayoutToSettleAsync(page, "[data-testid='ai-conversation-demo']");

        var directory = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "ai-conversation");
        Directory.CreateDirectory(directory);

        // The transcript follows the stream, so a shot straight after completion shows the END of the answer.
        // The review wants the beginning (reasoning, the table, the tool calls), so scroll the log to its top.
        await ScrollTranscriptToTopAsync(page);
        await WaitForLayoutToSettleAsync(page, "[data-testid='ai-conversation-demo']");

        var section = page.Locator("[data-testid='ai-conversation-demo']");
        await section.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(directory, "ai-conversation-light-1440.png"),
        });

        // The demo owns the theme switch (it flips the `dark` class AND data-theme on the layout shell), so the
        // shot is a real dark page rather than dark component tokens over a light Tailwind background.
        // The standalone component gallery is the other half of the page: it gets its own shots. It is TALLER
        // than the viewport, so Playwright stitches it from several scroll positions — and the demo's sticky
        // header would be painted into the middle of the artifact. Hide it for the duration of those two shots.
        var standalone = page.Locator("[data-testid='ai-standalone-components']");
        await SetStickyHeaderVisibleAsync(page, false);
        await standalone.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(directory, "ai-conversation-standalone-light-1440.png"),
        });
        await SetStickyHeaderVisibleAsync(page, true);

        await ToggleDarkModeAsync(page);
        await WaitForLayoutToSettleAsync(page, "[data-testid='ai-conversation-demo']");
        await section.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(directory, "ai-conversation-dark-1440.png"),
        });
        await SetStickyHeaderVisibleAsync(page, false);
        await standalone.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(directory, "ai-conversation-standalone-dark-1440.png"),
        });
        await SetStickyHeaderVisibleAsync(page, true);

        await ToggleDarkModeAsync(page);

        // The narrow band: the same finished answer at a phone width.
        await page.SetViewportSizeAsync(375, 812);
        await WaitForLayoutToSettleAsync(page, "[data-testid='ai-conversation-demo']");
        await page.Locator("[data-testid='ai-conversation']").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(directory, "ai-conversation-light-375.png"),
        });

        await ToggleDarkModeAsync(page);
        await WaitForLayoutToSettleAsync(page, "[data-testid='ai-conversation-demo']");
        await page.Locator("[data-testid='ai-conversation']").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(directory, "ai-conversation-dark-375.png"),
        });

        AssertNoClientErrors();
    }

    /// <summary>
    /// Visual review of the HOST-DRIVEN window (2.12.0): the wait line, the busy conversation and the
    /// built-in Stop pill in light and dark at 1440 px. The review wants the host's CARD — the showcase
    /// card that carries the simulate control above the transcript — not the whole page. Each theme opens
    /// its own window, so neither shot races the 2.5 s one.
    /// </summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task Screenshots_GeneratingWindowLightDark1440()
    {
        var page = await OpenPageAsync(1440, 1000);
        var directory = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "ai-conversation");
        Directory.CreateDirectory(directory);
        var hostCard = page.Locator("[data-testid='ai-conversation-demo'] .showcase-card");
        var conversation = page.Locator("[data-testid='ai-conversation']");

        await page.Locator("[data-testid='ai-demo-simulate-busy']").ClickAsync();
        await page.Locator(".tm-ai-conversation .tm-ai-message__pending")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await Microsoft.Playwright.Assertions.Expect(conversation)
            .ToHaveAttributeAsync("data-generating", "true",
                new LocatorAssertionsToHaveAttributeOptions { Timeout = 10000 });
        await WaitForLayoutToSettleAsync(page, "[data-testid='ai-conversation-demo']");
        await hostCard.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(directory, "ai-conversation-generating-light-1440.png"),
        });

        // The first window closes on its own (the simulated host answer); the dark shot gets its own
        // window so both captures show the SAME phase of the contract.
        await Microsoft.Playwright.Assertions.Expect(conversation)
            .ToHaveAttributeAsync("data-generating", "false",
                new LocatorAssertionsToHaveAttributeOptions { Timeout = 15000 });
        await ToggleDarkModeAsync(page);
        await page.Locator("[data-testid='ai-demo-simulate-busy']").ClickAsync();
        await page.Locator(".tm-ai-conversation .tm-ai-message__pending")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await Microsoft.Playwright.Assertions.Expect(conversation)
            .ToHaveAttributeAsync("data-generating", "true",
                new LocatorAssertionsToHaveAttributeOptions { Timeout = 10000 });
        await WaitForLayoutToSettleAsync(page, "[data-testid='ai-conversation-demo']");
        await hostCard.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(directory, "ai-conversation-generating-dark-1440.png"),
        });

        AssertNoClientErrors();
    }

    // ── Follow-up chips: the disabled look (plan b75365fb fáze 3) ────────────────────────────

    /// <summary>
    /// Plan b75365fb fáze 3 (EmusCz 2.13.0): under <c>FollowUpsDisabled</c> the chips kept the full
    /// colour, the pointer cursor and the hover fill, so they looked clickable while the click was
    /// refused. The demo card's toggle flips the parameter on a static sample, so ONE chip is read
    /// enabled and then disabled: opacity 1 → the shared <c>--tm-opacity-disabled</c> (0.5), cursor
    /// pointer → not-allowed, and the hover fill belongs to the enabled chip only (a disabled chip
    /// under the pointer keeps its resting surface). The screenshots record the enabled/disabled pair
    /// in light and dark for the UX review. Mutant guard: dropping the
    /// <c>.tm-ai-message__follow-up:disabled</c> rule (or its <c>:not(:disabled)</c> hover guard)
    /// turns the computed-style assertions red.
    /// </summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task FollowUps_DisabledChips_ReadDisabledInComputedStyles()
    {
        var page = await OpenPageAsync(1440, 1000);
        var directory = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "ai-conversation");
        Directory.CreateDirectory(directory);

        var card = page.Locator("[data-testid='ai-follow-ups-demo']");
        var chip = card.Locator(".tm-ai-message__follow-up").First;
        await chip.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await Microsoft.Playwright.Assertions.Expect(chip).ToBeEnabledAsync();
        await WaitForLayoutToSettleAsync(page, "[data-testid='ai-follow-ups-demo']");

        // Enabled: full opacity, the pointer cursor, and the hover fill the disabled chip must lose.
        var enabled = await ReadChipPaintAsync(page);
        Assert.AreEqual("1", enabled.Opacity, "the enabled chip paints at full opacity");
        Assert.AreEqual("pointer", enabled.Cursor, "the enabled chip invites the click");

        await chip.HoverAsync();
        var enabledHovered = await ReadChipPaintAsync(page);
        Assert.AreNotEqual(enabled.Background, enabledHovered.Background,
            "the enabled chip fills on hover — the affordance the disabled chip must not fake");
        await MovePointerAwayAsync(page);

        await card.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(directory, "ai-conversation-follow-ups-enabled-light-1440.png"),
        });

        // Disabled: the shared dimming token, the refused-pointer cursor, the resting surface even
        // under the pointer.
        await page.Locator("[data-testid='ai-demo-follow-ups-toggle']").ClickAsync();
        await Microsoft.Playwright.Assertions.Expect(chip).ToBeDisabledAsync();
        await WaitForLayoutToSettleAsync(page, "[data-testid='ai-follow-ups-demo']");

        var disabled = await ReadChipPaintAsync(page);
        Assert.AreEqual("0.5", disabled.Opacity,
            "the disabled chip dims through --tm-opacity-disabled (0.5), never a literal alpha");
        Assert.AreEqual("not-allowed", disabled.Cursor,
            "a refused click says so under the pointer, not with the clickable hand");
        Assert.AreEqual(enabled.Background, disabled.Background,
            "disabling dims the chip, it does not repaint the surface");

        await chip.HoverAsync();
        var disabledHovered = await ReadChipPaintAsync(page);
        Assert.AreEqual(disabled.Background, disabledHovered.Background,
            "a disabled chip under the pointer keeps its resting surface — no hover fill");
        await MovePointerAwayAsync(page);

        await card.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(directory, "ai-conversation-follow-ups-disabled-light-1440.png"),
        });

        // The dark pair of the same two states.
        await ToggleDarkModeAsync(page);
        await WaitForLayoutToSettleAsync(page, "[data-testid='ai-follow-ups-demo']");
        await card.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(directory, "ai-conversation-follow-ups-disabled-dark-1440.png"),
        });

        await page.Locator("[data-testid='ai-demo-follow-ups-toggle']").ClickAsync();
        await Microsoft.Playwright.Assertions.Expect(chip).ToBeEnabledAsync();
        await WaitForLayoutToSettleAsync(page, "[data-testid='ai-follow-ups-demo']");
        // The toggle click parked the pointer over the toggle; move it away so the enabled-dark shot
        // shows the card at rest (the hover rule of the demo button would otherwise be in the frame).
        await MovePointerAwayAsync(page);
        await card.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(directory, "ai-conversation-follow-ups-enabled-dark-1440.png"),
        });

        AssertNoClientErrors();
    }

    /// <summary>
    /// The forced-colors half of the contract: the chip's surface opts out of the automatic
    /// adjustment (<c>forced-color-adjust: none</c> + Canvas/CanvasText), so the disabled state asks
    /// for the system <c>GrayText</c> explicitly — without it a disabled chip in Windows high
    /// contrast reads exactly like an enabled one. The probe span carries <c>color: GrayText</c>
    /// inline, so the assertion compares the chip against the system colour itself, not a guessed
    /// palette value. The same pass pins the FULL opacity the forced-colors rule keeps (a dim over a
    /// system colour would only lower its guaranteed contrast) and records the card as
    /// <c>ai-conversation-follow-ups-disabled-forced-colors-1440.png</c>. Mutant guard: dropping the
    /// forced-colors <c>.tm-ai-message__follow-up:disabled</c> rule turns this red (the chip keeps
    /// CanvasText), and so does dropping its <c>opacity: 1</c>.
    /// </summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task FollowUps_DisabledChip_InForcedColors_UsesTheSystemDisabledColor()
    {
        var page = await OpenPageAsync(1440, 1000);
        await page.EmulateMediaAsync(new PageEmulateMediaOptions { ForcedColors = ForcedColors.Active });

        var card = page.Locator("[data-testid='ai-follow-ups-demo']");
        var chip = card.Locator(".tm-ai-message__follow-up").First;
        await chip.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });

        var enabledColor = await chip.EvaluateAsync<string>("el => getComputedStyle(el).color");

        await page.Locator("[data-testid='ai-demo-follow-ups-toggle']").ClickAsync();
        await Microsoft.Playwright.Assertions.Expect(chip).ToBeDisabledAsync();

        var disabledColor = await chip.EvaluateAsync<string>(
            """
            el => {
                const probe = document.createElement('span');
                probe.style.color = 'GrayText';
                el.parentElement.appendChild(probe);
                const system = getComputedStyle(probe).color;
                probe.remove();
                return JSON.stringify({ chip: getComputedStyle(el).color, system });
            }
            """);
        using var document = JsonDocument.Parse(disabledColor);
        var chipColor = document.RootElement.GetProperty("chip").GetString() ?? "";
        var systemColor = document.RootElement.GetProperty("system").GetString() ?? "";

        Assert.AreEqual(systemColor, chipColor,
            "the disabled chip paints the system GrayText inside its forced-color-adjust:none surface");
        Assert.AreNotEqual(enabledColor, chipColor,
            "high contrast must tell the disabled chip from an enabled one");

        // The disabled chip also keeps FULL opacity: a dim over a system colour would lower the
        // contrast the system itself guarantees (the state is GrayText alone).
        var disabledOpacity = await chip.EvaluateAsync<string>("el => getComputedStyle(el).opacity");
        Assert.AreEqual("1", disabledOpacity,
            "forced colors drop the resting dim — opacity over GrayText would only lower its contrast");

        var directory = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "ai-conversation");
        Directory.CreateDirectory(directory);
        await MovePointerAwayAsync(page);
        await card.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(directory, "ai-conversation-follow-ups-disabled-forced-colors-1440.png"),
        });

        AssertNoClientErrors();
    }

    /// <summary>The computed paint of the first follow-up chip of the demo card.</summary>
    private static async Task<ChipPaint> ReadChipPaintAsync(IPage page)
    {
        var json = await page.EvaluateAsync<string>(
            """
            () => {
                const chip = document.querySelector(
                    "[data-testid='ai-follow-ups-demo'] .tm-ai-message__follow-up");
                const style = getComputedStyle(chip);
                return JSON.stringify({
                    opacity: style.opacity,
                    cursor: style.cursor,
                    background: style.backgroundColor,
                });
            }
            """);
        using var document = JsonDocument.Parse(json);
        return new ChipPaint(
            document.RootElement.GetProperty("opacity").GetString() ?? "",
            document.RootElement.GetProperty("cursor").GetString() ?? "",
            document.RootElement.GetProperty("background").GetString() ?? "");
    }

    private static Task MovePointerAwayAsync(IPage page)
        => page.Mouse.MoveAsync(0, 0);

    private sealed record ChipPaint(string Opacity, string Cursor, string Background);

    // ── Helpers ───────────────────────────────────────────────────────────────────────────

    private static async Task AssertAxeCleanAsync(IPage page, string hostSelector)
    {
        await page.AddScriptTagAsync(new PageAddScriptTagOptions { Url = AxeCdn });
        var violations = await page.EvaluateAsync<string[]>(
            """
            async (hostSelector) => {
                const host = document.querySelector(hostSelector) || document.body;
                // axe samples the RESOLVED colours: a finite CSS animation/transition still running (the demo's fade-in, a colour
                // transition, the segmented indicator) paints a blended, low-contrast frame. Wait for them (looping ones,
                // like the skeleton shimmer, never finish and are not waited for).
                await Promise.all(document.getAnimations()
                    .filter(a => a.effect && a.effect.getComputedTiming().iterations !== Infinity)
                    .map(a => a.finished.catch(() => { })));
                await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
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

    private static Task ScrollTranscriptToTopAsync(IPage page)
        => page.EvaluateAsync(
            """
            () => {
                const log = document.querySelector('.tm-ai-conversation__log');
                if (log) {
                    log.scrollTop = 0;
                }
            }
            """);

    [TestMethod]
    [TestCategory("WASM")]
    [Description("E2E-AI.10a: the New messages pill lands at the end of the log with focus on the transcript")]
    public async Task NewMessagesPill_ScrollsToTheEnd_AndFocusesTheLog()
    {
        var page = await OpenPageAsync();
        await AssertNewMessagesPillLandsAtEndAsync(page);
        AssertNoClientErrors();
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("E2E-AI.10a reduced-motion: the same jump lands with behavior:auto")]
    public async Task NewMessagesPill_WithReducedMotion_ScrollsToTheEnd_AndFocusesTheLog()
    {
        var page = await OpenPageAsync();
        await page.EmulateMediaAsync(new PageEmulateMediaOptions { ReducedMotion = ReducedMotion.Reduce });
        await AssertNewMessagesPillLandsAtEndAsync(page);
        AssertNoClientErrors();
    }

    private static async Task AssertNewMessagesPillLandsAtEndAsync(IPage page)
    {
        await StartAnswerAsync(page);
        await page.Locator(AnswerSelector).WaitForAsync(
            new LocatorWaitForOptions { Timeout = 30000 });

        await page.WaitForFunctionAsync(
            """
            () => {
                const log = document.querySelector('.tm-ai-conversation__log');
                return !!log && log.scrollHeight > log.clientHeight + 40;
            }
            """,
            null,
            new PageWaitForFunctionOptions { Timeout = 30000 });

        await ScrollTranscriptToTopAsync(page);
        var pill = page.Locator("[data-testid='ai-conversation-new-messages']");
        await pill.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await pill.ClickAsync();

        await page.WaitForFunctionAsync(
            """
            () => {
                const log = document.querySelector('.tm-ai-conversation__log');
                if (!log) return false;
                const atEnd = log.scrollTop + log.clientHeight >= log.scrollHeight - 1;
                return atEnd && document.activeElement === log;
            }
            """,
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });
    }

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

/// <summary>
/// Server smoke of the same demo page (Server demo @ 7107): the simulated stream is a Server circuit round
/// trip there, so the one thing to prove is that the deltas arrive, the answer completes and the page's
/// status reflects it — the interactive behaviours are covered by the WASM lane.
/// </summary>
[TestClass]
public sealed class AiConversationServerSmokeE2ETests : ServerTestBase
{
    private const string AnswerSelector =
        ".tm-ai-conversation [data-testid='ai-message'][data-role='assistant']";

    private readonly List<string> _clientErrors = [];

    private async Task<IPage> OpenPageAsync(int width, int height)
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
        await GotoAppReadyAsync(page, $"{BaseUrl}/ai-conversation");
        await page.Locator("[data-testid='ai-conversation']").WaitForAsync(
            new LocatorWaitForOptions { Timeout = 30000 });
        return page;
    }

    private void AssertNoClientErrors()
    {
        lock (_clientErrors)
        {
            Assert.IsTrue(_clientErrors.Count == 0,
                "Unhandled client-side errors occurred:\n" + string.Join("\n", _clientErrors));
        }
    }

    [TestMethod]
    public async Task Streaming_CompletesOnTheServerHost()
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);

        await GotoAppReadyAsync(page, $"{BaseUrl}/ai-conversation");
        await page.Locator("[data-testid='ai-conversation-empty']").WaitForAsync(
            new LocatorWaitForOptions { Timeout = 30000 });

        await page.Locator("[data-testid='ai-conversation-suggestion']").First.ClickAsync();
        await page.Locator($"{AnswerSelector}[data-status='completed']").WaitForAsync(
            new LocatorWaitForOptions { Timeout = 30000 });

        await Microsoft.Playwright.Assertions.Expect(page.Locator(AnswerSelector).Locator("table"))
            .ToHaveCountAsync(1);
        await Microsoft.Playwright.Assertions.Expect(page.Locator("[data-testid='ai-demo-message-count']"))
            .ToHaveTextAsync("2");

        // The transcript is the conversation's only aria-live element on the circuit too.
        var liveRegions = await page.EvaluateAsync<int>(
            "() => document.querySelector(\".tm-ai-conversation\").querySelectorAll('[aria-live]').length");
        Assert.AreEqual(1, liveRegions, "the transcript must be the conversation's only aria-live element");
    }

    [TestMethod]
    public async Task Suggestions_ScrollRowAt375_RevealsTheThirdChipWithoutMovingTheLog()
    {
        var page = await OpenPageAsync(375, 812);
        await page.Locator("[data-testid='ai-conversation-empty']").WaitForAsync(
            new LocatorWaitForOptions { Timeout = 30000 });

        var log = page.Locator("[data-testid='ai-conversation-log']");
        var chips = page.Locator("[data-testid='ai-conversation-suggestion']");
        await Microsoft.Playwright.Assertions.Expect(chips).ToHaveCountAsync(3);
        await Microsoft.Playwright.Assertions.Expect(page.Locator(".tm-ai-conversation__suggestions"))
            .ToHaveAttributeAsync("data-layout", "auto");
        var logTopBefore = await log.EvaluateAsync<double>("el => el.scrollTop");

        await chips.Nth(2).FocusAsync();
        await page.WaitForFunctionAsync("""
            () => {
                const row = document.querySelector('.tm-ai-conversation__suggestions');
                const chip = document.querySelectorAll('[data-testid="ai-conversation-suggestion"]')[2];
                if (!row || !chip) return false;
                const r = row.getBoundingClientRect();
                const c = chip.getBoundingClientRect();
                return c.left >= r.left - 1 && c.right <= r.right + 1;
            }
            """, null, new PageWaitForFunctionOptions { Timeout = 5000 });

        var logTopAfter = await log.EvaluateAsync<double>("el => el.scrollTop");
        Assert.AreEqual(logTopBefore, logTopAfter, 0.5, "revealing a chip must not scroll the transcript");
        AssertNoClientErrors();
    }

    [TestMethod]
    public async Task Composer_At375_ClearsA56PxHostBar()
    {
        var page = await OpenPageAsync(375, 812);
        var composer = page.Locator("[data-testid='ai-composer']");
        await composer.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await composer.ScrollIntoViewIfNeededAsync();

        await page.EvaluateAsync("""
            document.documentElement.style.setProperty('--tm-ai-composer-bottom-clearance', '56px');
            """);

        var gap = await page.EvaluateAsync<double>("""
            () => {
                const c = document.querySelector('[data-testid="ai-composer"]');
                return window.innerHeight - c.getBoundingClientRect().bottom;
            }
            """);
        Assert.IsTrue(gap >= 55, $"the sticky composer must clear a 56 px host bar (gap was {gap:0.#} px)");
        AssertNoClientErrors();
    }
}
