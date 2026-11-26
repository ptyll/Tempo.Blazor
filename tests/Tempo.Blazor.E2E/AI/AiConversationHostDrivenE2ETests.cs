using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E.AI;

/// <summary>
/// E2E of the HOST-DRIVEN generation window and the transcript live region of the
/// <c>/ai-conversation</c> demo page (WASM @ 7106): the busy window runs without a stream, the
/// completion note stays a single replaceable slot across turns, the host announcements
/// (<c>AnnounceAsync</c>) follow the same clear-on-the-window-edge rule as a stream start, and the
/// screen-reader-only notes position against the log, never the page. Split out of
/// <see cref="AiConversationE2ETests"/> (plan b75365fb phase 4a) so both files stay under the
/// 1200-line source ceiling (N270/F13).
/// </summary>
[TestClass]
public sealed class AiConversationHostDrivenE2ETests : WasmTestBase
{
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
    /// Three host-driven windows in a row — the shape of a consumer that drives the stream itself (EmusCz
    /// before this fix): the transcript holds EXACTLY ONE completion note after each finished answer, and
    /// the note reads the latest completion only. A build that appends one note per turn leaves N identical
    /// <c>p.tm-sr-only</c> nodes here (a screen reader browsing the transcript would read "Answer complete"
    /// N times), so the count assertion is the decisive mutant killer.
    ///
    /// The click→assert race the first version of this spec had: it clicked the simulate control and
    /// immediately waited for <c>data-generating="false"</c> — but the attribute is STILL false from the
    /// previous turn for the whole round-trip (click → WASM event → window opens), so the "window finished"
    /// wait could be satisfied by the stale false and the note count then raced the announce render. The
    /// spec therefore FIRST waits for the window to OPEN (<c>data-generating="true"</c>, or the pending
    /// wait line) and asserts there that no completion note exists while the answer is being produced;
    /// only then it waits for the close and asserts the completion count and the single note. The note
    /// itself is keyed per completion, so the previous turn's note node must be DETACHED after the next
    /// turn — the stored element handle proves the replacement is a new node, not a mutation.
    /// </summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task HostDrivenCompletion_ThreeTurns_KeepOneTranscriptNote()
    {
        var page = await OpenPageAsync();
        var note = page.Locator("[data-testid='ai-conversation-announcement'][data-kind='completion']");
        var pendingLine = page.Locator(".tm-ai-conversation .tm-ai-message__pending");
        var completedAnswers = page.Locator($"{AnswerSelector}[data-status='completed']");
        var simulate = page.Locator("[data-testid='ai-demo-simulate-busy']");
        var baseline = await completedAnswers.CountAsync();

        IElementHandle? previousNote = null;
        for (var turn = 1; turn <= 3; turn++)
        {
            await simulate.ClickAsync();

            // The window must be OBSERVED open before anything else is asserted: while the new answer is
            // being produced there is no completed answer to announce, so the transcript carries no note.
            await Microsoft.Playwright.Assertions.Expect(
                    page.Locator("[data-testid='ai-conversation']"))
                .ToHaveAttributeAsync("data-generating", "true",
                    new LocatorAssertionsToHaveAttributeOptions { Timeout = 15000 });
            await pendingLine.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 15000,
            });
            await Microsoft.Playwright.Assertions.Expect(note)
                .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });

            if (previousNote is not null)
            {
                Assert.IsFalse(await previousNote.EvaluateAsync<bool>("el => el.isConnected"),
                    $"turn {turn}: the previous turn's note is a detached node — the replacement is a new " +
                    "keyed node, never a mutation of the old one");
            }

            // The window closes when the host's answer lands: the answer count grows by one per turn and
            // exactly one completion note reads the latest completion.
            await Microsoft.Playwright.Assertions.Expect(
                    page.Locator("[data-testid='ai-conversation']"))
                .ToHaveAttributeAsync("data-generating", "false",
                    new LocatorAssertionsToHaveAttributeOptions { Timeout = 15000 });
            await Microsoft.Playwright.Assertions.Expect(completedAnswers)
                .ToHaveCountAsync(baseline + turn, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });
            await Microsoft.Playwright.Assertions.Expect(note)
                .ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });
            await Microsoft.Playwright.Assertions.Expect(note)
                .ToHaveTextAsync("Answer complete", new LocatorAssertionsToHaveTextOptions { Timeout = 10000 });

            previousNote = await note.ElementHandleAsync();
        }

        AssertNoClientErrors();
    }

    /// <summary>
    /// Plan b75365fb phase 4a (remaining 8cc5aae1): the host channel follows the SAME rule as the
    /// completion note — opening a <c>Generating</c> window clears the previous window's host
    /// announcements (<c>AnnounceAsync</c>), exactly like a stream start does. Three simulated windows,
    /// each carrying ONE host note spoken inside the open window, must leave EXACTLY ONE host note in
    /// the transcript DOM after every turn — a build without the edge clear accumulates N stale notes a
    /// screen reader would re-read while browsing the transcript, so the per-turn count assertion is the
    /// decisive mutant killer. The previous turn's note node must be DETACHED after the next window
    /// opens — the stored element handle proves the repeat is a new keyed node, never a mutation (a live
    /// region ignores an unchanged text node).
    /// </summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task HostDrivenHostAnnouncements_ThreeTurns_KeepExactlyOneHostNote()
    {
        var page = await OpenPageAsync();
        var hostNote = page.Locator("[data-testid='ai-conversation-announcement'][data-kind='host']");
        var completionNote = page.Locator("[data-testid='ai-conversation-announcement'][data-kind='completion']");
        var hostNotesCounter = page.Locator("[data-testid='ai-demo-host-notes']");
        var pendingLine = page.Locator(".tm-ai-conversation .tm-ai-message__pending");
        var simulate = page.Locator("[data-testid='ai-demo-simulate-busy']");
        var announce = page.Locator("[data-testid='ai-demo-announce']");

        IElementHandle? previousHostNote = null;
        for (var turn = 1; turn <= 3; turn++)
        {
            await simulate.ClickAsync();

            // The window must be OBSERVED open before the host speaks: the edge already cleared the
            // previous window's note, so the host channel is empty right now — and the page's visible
            // counter mirrors the transcript exactly (the dd equals the [data-kind='host'] count).
            await Microsoft.Playwright.Assertions.Expect(
                    page.Locator("[data-testid='ai-conversation']"))
                .ToHaveAttributeAsync("data-generating", "true",
                    new LocatorAssertionsToHaveAttributeOptions { Timeout = 15000 });
            await pendingLine.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 15000,
            });
            await Microsoft.Playwright.Assertions.Expect(hostNote)
                .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });
            await Microsoft.Playwright.Assertions.Expect(hostNotesCounter)
                .ToHaveTextAsync("0", new LocatorAssertionsToHaveTextOptions { Timeout = 10000 });

            if (previousHostNote is not null)
            {
                Assert.IsFalse(await previousHostNote.EvaluateAsync<bool>("el => el.isConnected"),
                    $"turn {turn}: the previous window's host note is a detached node — the repeat is a new " +
                    "keyed node, never a mutation of the old one");
            }

            // The host speaks INSIDE the open window; the note survives the window's close next to the
            // completion note, and it is the ONLY host note — never one per past turn. (The survival
            // itself is pinned at the component level by the bUnit
            // HostAnnouncement_InsideTheOpenWindow_SurvivesTheCompletionEdge; this spec proves the
            // real-DOM half — the per-turn count and the detached node.)
            await announce.ClickAsync();
            await Microsoft.Playwright.Assertions.Expect(hostNote)
                .ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });
            await Microsoft.Playwright.Assertions.Expect(hostNotesCounter)
                .ToHaveTextAsync("1", new LocatorAssertionsToHaveTextOptions { Timeout = 10000 });

            await Microsoft.Playwright.Assertions.Expect(
                    page.Locator("[data-testid='ai-conversation']"))
                .ToHaveAttributeAsync("data-generating", "false",
                    new LocatorAssertionsToHaveAttributeOptions { Timeout = 15000 });
            await Microsoft.Playwright.Assertions.Expect(hostNote)
                .ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });
            await Microsoft.Playwright.Assertions.Expect(hostNotesCounter)
                .ToHaveTextAsync("1", new LocatorAssertionsToHaveTextOptions { Timeout = 10000 });
            await Microsoft.Playwright.Assertions.Expect(completionNote)
                .ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });

            previousHostNote = await hostNote.ElementHandleAsync();
        }

        // The visual half: the host-driven card at the end of the THIRD turn — the visible counter
        // reads "1" (the one surviving host note), for the UX review.
        var directory = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "ai-conversation");
        Directory.CreateDirectory(directory);
        var screenshotPath = Path.Combine(directory, "fze-4a-host-announcements-1440.png");
        await CaptureHostCardAsync(page, screenshotPath);

        AssertNoClientErrors();
    }

    /// <summary>
    /// Edge cases of the host channel without a window: an idle <c>AnnounceAsync</c> (the demo's
    /// announce button, no <c>Generating</c> window open) accumulates — two identical wordings in a row
    /// are TWO distinct keyed nodes, because a live region ignores an unchanged text node and the repeat
    /// must still be heard. The next window's edge is the first clear: opening the simulated window
    /// empties the host channel BEFORE the host speaks again, and the window then closes with its
    /// completion note only.
    /// </summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task HostAnnouncement_OutsideAWindow_TwoIdenticalNotes_AndClearedByTheWindowEdge()
    {
        var page = await OpenPageAsync();
        var hostNote = page.Locator("[data-testid='ai-conversation-announcement'][data-kind='host']");
        var completionNote = page.Locator("[data-testid='ai-conversation-announcement'][data-kind='completion']");
        var hostNotesCounter = page.Locator("[data-testid='ai-demo-host-notes']");
        var announce = page.Locator("[data-testid='ai-demo-announce']");

        // Two identical idle notes: both stay (each call is a new keyed node), so the channel holds
        // two — and the page's visible counter mirrors the transcript (the dd equals the
        // [data-kind='host'] count).
        await announce.ClickAsync();
        await Microsoft.Playwright.Assertions.Expect(hostNote)
            .ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });
        await Microsoft.Playwright.Assertions.Expect(hostNotesCounter)
            .ToHaveTextAsync("1", new LocatorAssertionsToHaveTextOptions { Timeout = 10000 });
        await announce.ClickAsync();
        await Microsoft.Playwright.Assertions.Expect(hostNote)
            .ToHaveCountAsync(2, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });
        await Microsoft.Playwright.Assertions.Expect(hostNotesCounter)
            .ToHaveTextAsync("2", new LocatorAssertionsToHaveTextOptions { Timeout = 10000 });
        var texts = await hostNote.AllTextContentsAsync();
        Assert.AreEqual(2, texts.Count);
        Assert.AreEqual(texts[0], texts[1], "both idle notes carry the same wording — the repeat was not swallowed");

        // The visual half of the accumulation: the host-driven card with the counter at "2".
        var directory = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "ai-conversation");
        Directory.CreateDirectory(directory);
        var screenshotPath = Path.Combine(directory, "fze-4a-host-announcements-idle-two-1440.png");
        await CaptureHostCardAsync(page, screenshotPath);

        // The window edge is the first clear: opening the simulated window empties the host channel
        // before the host speaks inside it — the counter restarts with the window.
        await page.Locator("[data-testid='ai-demo-simulate-busy']").ClickAsync();
        await Microsoft.Playwright.Assertions.Expect(
                page.Locator("[data-testid='ai-conversation']"))
            .ToHaveAttributeAsync("data-generating", "true",
                new LocatorAssertionsToHaveAttributeOptions { Timeout = 15000 });
        await Microsoft.Playwright.Assertions.Expect(hostNote)
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });
        await Microsoft.Playwright.Assertions.Expect(hostNotesCounter)
            .ToHaveTextAsync("0", new LocatorAssertionsToHaveTextOptions { Timeout = 10000 });

        // The window closes with NO host note (the host never spoke inside it) and one completion note.
        await Microsoft.Playwright.Assertions.Expect(
                page.Locator("[data-testid='ai-conversation']"))
            .ToHaveAttributeAsync("data-generating", "false",
                new LocatorAssertionsToHaveAttributeOptions { Timeout = 15000 });
        await Microsoft.Playwright.Assertions.Expect(hostNote)
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });
        await Microsoft.Playwright.Assertions.Expect(hostNotesCounter)
            .ToHaveTextAsync("0", new LocatorAssertionsToHaveTextOptions { Timeout = 10000 });
        await Microsoft.Playwright.Assertions.Expect(completionNote)
            .ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });

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

    // ── Helpers ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The demo shell's sticky header would cover the top of the host card in an element screenshot;
    /// hide it for the capture and restore it right after (same helper as in
    /// <see cref="AiConversationE2ETests"/>).
    /// </summary>
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

    /// <summary>Captures the host-driven card with the sticky header hidden and the header restored afterwards.</summary>
    private static async Task CaptureHostCardAsync(IPage page, string screenshotPath)
    {
        await WaitForLayoutToSettleAsync(page, "[data-testid='ai-conversation-demo']");
        var hostCard = page.Locator("[data-testid='ai-conversation-demo'] .showcase-card");
        await hostCard.ScrollIntoViewIfNeededAsync();
        await SetStickyHeaderVisibleAsync(page, false);
        try
        {
            await hostCard.ScreenshotAsync(new LocatorScreenshotOptions { Path = screenshotPath });
        }
        finally
        {
            await SetStickyHeaderVisibleAsync(page, true);
        }

        Assert.IsTrue(new FileInfo(screenshotPath).Length > 2 * 1024,
            "the host-card screenshot must be a real capture, not an empty artifact");
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
