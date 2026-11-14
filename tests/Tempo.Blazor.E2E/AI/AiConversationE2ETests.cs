using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

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
}
