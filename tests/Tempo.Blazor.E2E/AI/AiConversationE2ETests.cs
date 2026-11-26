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

    // ── Host-driven generation and the pending placeholder (2.12.0) ────────────────────────

    /// <summary>
    /// The demo's "host-driven generation" card runs a generation with NO stream: the page appends a
    /// Pending turn, opens the <c>Generating</c> window, produces the answer itself and closes the
    /// window. The browser proof: the busy state follows (<c>data-generating</c>), the opt-in wait line
    /// renders while the turn has no content, the answer completes on the host's own write, the
    /// completion is announced exactly once, and "scroll to the first turn" moves only the transcript.
    /// </summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task HostDrivenGeneration_RunsTheBusyWindowWithoutAStream()
    {
        var page = await OpenPageAsync();

        // The static half: the standalone Pending sample carries the wait line with aria-busy at load.
        await Microsoft.Playwright.Assertions.Expect(
                page.Locator("[data-testid='ai-message-pending-sample'] .tm-ai-message__pending"))
            .ToHaveAttributeAsync("aria-busy", "true");

        await page.Locator("[data-testid='ai-demo-simulate-busy']").ClickAsync();

        // The wait line renders in the SAME render that flips data-generating, and the window is finite —
        // so the FIRST wait goes to the line (its appearance), not to the attribute (whose polling could
        // burn the whole window on a slow first WASM render).
        var waitLine = page.Locator(".tm-ai-conversation .tm-ai-message__pending");
        await Microsoft.Playwright.Assertions.Expect(waitLine).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

        var conversation = page.Locator("[data-testid='ai-conversation']");
        await Microsoft.Playwright.Assertions.Expect(conversation)
            .ToHaveAttributeAsync("data-generating", "true",
                new LocatorAssertionsToHaveAttributeOptions { Timeout = 10000 });

        // While the turn has no content, the wait line is marked busy (the host named it through
        // MessagePendingText).
        await Microsoft.Playwright.Assertions.Expect(waitLine).ToHaveAttributeAsync("aria-busy", "true");

        // The host completes the turn itself; the window closes, the turn reads Completed and the one
        // completion announcement is on the transcript.
        await Microsoft.Playwright.Assertions.Expect(conversation)
            .ToHaveAttributeAsync("data-generating", "false",
                new LocatorAssertionsToHaveAttributeOptions { Timeout = 15000 });
        await Microsoft.Playwright.Assertions.Expect(
                page.Locator($"{AnswerSelector}[data-status='completed']"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
        await Microsoft.Playwright.Assertions.Expect(waitLine)
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });
        await Microsoft.Playwright.Assertions.Expect(
                page.Locator("[data-testid='ai-conversation-announcement']"))
            .ToHaveTextAsync("Answer complete", new LocatorAssertionsToHaveTextOptions { Timeout = 10000 });

        // "Scroll to the first turn": the module's scrollToMessage moves ONLY the transcript (the smooth
        // scroll lands near its top); the page itself does not scroll along. The sample answer is tall
        // enough to overflow the transcript, so the bottom-riding scroll position is real.
        var logScrollBefore = await page.EvaluateAsync<double>(
            "() => document.querySelector('.tm-ai-conversation__log').scrollTop");
        Assert.IsTrue(logScrollBefore > 100,
            $"the transcript follows the answer, so it must be scrolled down — was {logScrollBefore}");
        var pageScrollBefore = await page.EvaluateAsync<double>(
            "() => document.scrollingElement.scrollTop");

        await page.Locator("[data-testid='ai-demo-scroll-first']").ClickAsync();

        var landedNearTop = false;
        for (var attempt = 0; attempt < 50 && !landedNearTop; attempt++)
        {
            await Task.Delay(100);
            var scrollTop = await page.EvaluateAsync<double>(
                "() => document.querySelector('.tm-ai-conversation__log').scrollTop");
            landedNearTop = scrollTop < 40;
        }

        Assert.IsTrue(landedNearTop,
            $"scrollToMessageAsync must bring the first turn into view — log is still at " +
            $"{await page.EvaluateAsync<double>("() => document.querySelector('.tm-ai-conversation__log').scrollTop")}");
        var pageScrollAfter = await page.EvaluateAsync<double>(
            "() => document.scrollingElement.scrollTop");
        Assert.AreEqual(pageScrollBefore, pageScrollAfter,
            "scrollToMessageAsync moves the transcript only — the page never scrolls along");

        AssertNoClientErrors();
    }

    /// <summary>
    /// Stop during a HOST-DRIVEN window (the demo's simulate control, no stream): the built-in pill asks
    /// the page through <c>OnStop</c>, the page cancels its own window, and the busy state closes while
    /// the queued answer is marked <c>stopped</c> — <c>data-generating=false</c> and the pill gone, once
    /// per window.
    /// </summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task HostDrivenStop_ClosesTheWindowAndMarksTheAnswerStopped()
    {
        var page = await OpenPageAsync();
        await page.Locator("[data-testid='ai-demo-simulate-busy']").ClickAsync();

        var stop = page.Locator("[data-testid='ai-conversation-stop']");
        await stop.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000, State = WaitForSelectorState.Visible });
        await stop.ClickAsync();

        var conversation = page.Locator("[data-testid='ai-conversation']");
        await Microsoft.Playwright.Assertions.Expect(conversation)
            .ToHaveAttributeAsync("data-generating", "false",
                new LocatorAssertionsToHaveAttributeOptions { Timeout = 15000 });
        await Microsoft.Playwright.Assertions.Expect(
                page.Locator($"{AnswerSelector}[data-status='stopped']"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
        await Microsoft.Playwright.Assertions.Expect(stop)
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });
        await Microsoft.Playwright.Assertions.Expect(
                page.Locator(".tm-ai-conversation .tm-ai-message__pending"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });

        AssertNoClientErrors();
    }

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
    /// Plan b75365fb phase 1 (EmusCz 2.13.0): the completion announcements the transcript renders are
    /// <c>p.tm-sr-only</c> — absolutely positioned by the library's base styles. Without a positioned
    /// ancestor inside the scroll container the notes anchor to the outer container (the page) and in
    /// the host layout stretch <c>document.scrollingElement</c> (EmusCz measured +1349 px under the form
    /// after ~12 turns). The log is the containing block, so the announcement text of twenty turns must
    /// leave the document height untouched — at the bottom-riding position and with the transcript
    /// scrolled to its top. Driving the demo's 2 s stream twenty times would cost a minute for no extra
    /// signal, so the twenty nodes are appended as the exact markup the component emits. The page content
    /// below the demo card is hidden for the measurement: on the full demo page the standalone-components
    /// gallery reaches further down than the notes land, which would mask the very growth this test
    /// exists to catch. Two assertions pin the contract: the scrollHeight pair guards the user-visible
    /// symptom (EmusCz's layout, where the page ends at the conversation), and the offsetParent check is
    /// the positioning contract itself — current Chromium computes the unpositioned notes' static
    /// position inside the page on THIS demo, so the scrollHeight pair alone stays green on the mutant
    /// here while the offsetParent check turns red. Mutant guard: removing <c>position: relative</c> from
    /// <c>.tm-ai-conversation__log</c> turns this test red.
    /// </summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task LongTranscript_ScreenReaderAnnouncements_DoNotStretchTheDocument()
    {
        var page = await OpenPageAsync();
        await StartAnswerAsync(page);
        await WaitForCompletedAnswerAsync(page);

        // EmusCz measured the growth with the conversation at the END of the page. The demo page renders
        // a tall standalone-components gallery below the demo card, which would swallow the overflow, so
        // the measurement isolates the document at the demo card first.
        var hiddenSiblings = await page.EvaluateAsync<int>(
            """
            () => {
                const demo = document.querySelector("[data-testid='ai-conversation-demo']");
                let hidden = 0;
                let node = demo;
                while (node && node !== document.body) {
                    let sibling = node.nextElementSibling;
                    while (sibling) {
                        sibling.style.display = 'none';
                        hidden++;
                        sibling = sibling.nextElementSibling;
                    }
                    node = node.parentElement;
                }
                return hidden;
            }
            """);
        Assert.IsTrue(hiddenSiblings > 0, "the demo page must render content below the conversation card");

        var documentHeightBefore = await page.EvaluateAsync<double>(
            "() => document.scrollingElement.scrollHeight");

        // Insert only once the component reports the turn is done: a re-render while generating rebuilds
        // the transcript from Blazor's render tree and would silently discard the probe. The probe wrapper
        // is deliberately NOT positioned — a positioned probe would become the notes' containing block and
        // mask the very contract this test pins; a plain div leaves the notes resolving against the log
        // (or, on the mutant, the page).
        await Microsoft.Playwright.Assertions.Expect(
                page.Locator("[data-testid='ai-conversation']"))
            .ToHaveAttributeAsync("data-generating", "false",
                new LocatorAssertionsToHaveAttributeOptions { Timeout = 10000 });

        const string AppendTwentyAnnouncements = """
            () => {
                const content = document.querySelector('.tm-ai-conversation__content');
                if (!content) {
                    return;
                }
                const probe = document.createElement('div');
                probe.setAttribute('data-test-probe', '');
                for (let i = 0; i < 20; i++) {
                    const note = document.createElement('p');
                    note.className = 'tm-sr-only';
                    note.setAttribute('data-testid', 'ai-conversation-announcement');
                    note.textContent = 'Answer complete';
                    probe.appendChild(note);
                }
                content.appendChild(probe);
            }
            """;

        const string WaitTwoFrames = "() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))";

        // Bottom-riding reader (the follow kept the transcript at its end after the answer).
        await page.EvaluateAsync(AppendTwentyAnnouncements);
        await page.EvaluateAsync(WaitTwoFrames);
        var documentHeightAtBottom = await page.EvaluateAsync<double>(
            "() => document.scrollingElement.scrollHeight");
        Assert.AreEqual(documentHeightBefore, documentHeightAtBottom, 1.0,
            "twenty screen-reader announcements appended inside the transcript must not stretch the " +
            $"document — before {documentHeightBefore}, after {documentHeightAtBottom}");

        // The reader scrolled back up: the document height must stay untouched in this position too —
        // the unpositioned variant grew the page below the form no matter where the reader sat.
        await ScrollTranscriptToTopAsync(page);
        await page.EvaluateAsync(WaitTwoFrames);
        var documentHeightScrolledUp = await page.EvaluateAsync<double>(
            "() => document.scrollingElement.scrollHeight");
        Assert.AreEqual(documentHeightBefore, documentHeightScrolledUp, 1.0,
            "scrolling the transcript back up must not change the document height either — " +
            $"before {documentHeightBefore}, after {documentHeightScrolledUp}");

        // The contract behind both numbers: every announcement positions against the LOG, not the page.
        var allPinnedToTheLog = await page.EvaluateAsync<bool>(
            """
            () => {
                const log = document.querySelector('.tm-ai-conversation__log');
                const notes = document.querySelectorAll('.tm-ai-conversation__content p.tm-sr-only');
                return notes.length > 0 && Array.from(notes).every(note => note.offsetParent === log);
            }
            """);
        Assert.IsTrue(allPinnedToTheLog,
            "every sr-only announcement inside the transcript must have the log as its offsetParent");

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
