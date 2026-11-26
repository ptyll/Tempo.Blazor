using System.Globalization;
using AngleSharp.Dom;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Demo.SharedUI.Pages;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// bUnit contract of the <c>/ai-conversation</c> demo page — the page the E2E lane and the visual review
/// build on. It renders a real <see cref="TmAiConversation"/> with a simulated local delta stream (markdown
/// table, code block, citations, reasoning and two tool calls, one of which fails and can be retried), a
/// <see cref="TmAiComposer"/> wired with slash commands, mentions and attachments, and one section per
/// standalone component (the tool-call states, the thinking pill, the reasoning panel and a hostile
/// markdown sample that must not execute).
/// </summary>
/// <remarks>
/// The stream is the page's own <c>IAsyncEnumerable</c> generator, so a scenario runs exactly like a host's:
/// a suggestion click or a composer submit appends the user turn, appends the pending assistant turn and
/// awaits <c>StreamAsync</c>. bUnit awaits the event handler, which means the assertions run on a finished
/// generation — no polling helper is needed here. The host-driven simulation waits on the INJECTED
/// <see cref="TimeProvider"/> (a <see cref="FakeTimeProvider"/> here), so its window is opened, advanced
/// and stopped deterministically instead of by sleeping.
/// </remarks>
public class AiConversationDemoPageTests : DemoPageLocalizationTestBase
{
    private const string ConversationModulePath = "./_content/Tempo.Blazor.AI/js/ai-conversation.js";
    private const string ComposerModulePath = "./_content/Tempo.Blazor.AI/js/ai-composer.js";
    private const string MessageModulePath = "./_content/Tempo.Blazor.AI/js/ai-message.js";
    private const string MarkdownModulePath = "./_content/Tempo.Blazor.AI/js/ai-markdown.js";

    /// <summary>Mirrors the page's <c>HostDrivenBusyMs</c> — how long the simulated window stays open.</summary>
    private static readonly TimeSpan HostDrivenBusy = TimeSpan.FromMilliseconds(2_500);

    private readonly FakeTimeProvider _clock = new();

    public AiConversationDemoPageTests()
    {
        Services.AddSingleton<TimeProvider>(_clock);

        var conversation = JSInterop.SetupModule(ConversationModulePath);
        conversation.SetupVoid("attach", _ => true).SetVoidResult();
        conversation.SetupVoid("detach", _ => true).SetVoidResult();
        conversation.Setup<bool>("isNearBottom", _ => true).SetResult(true);
        conversation.SetupVoid("scrollToBottom", _ => true).SetVoidResult();
        conversation.Setup<bool>("scrollToMessage", _ => true).SetResult(true);

        var composer = JSInterop.SetupModule(ComposerModulePath);
        composer.SetupVoid("attach", _ => true).SetVoidResult();
        composer.SetupVoid("detach", _ => true).SetVoidResult();
        composer.SetupVoid("resize", _ => true).SetVoidResult();
        composer.SetupVoid("placeSuggestions", _ => true).SetVoidResult();
        composer.Setup<bool>("speechSupported", _ => true).SetResult(false);
        composer.Setup<bool>("startSpeech", _ => true).SetResult(false);
        composer.SetupVoid("stopSpeech", _ => true).SetVoidResult();
        composer.SetupVoid("attachViewportTracker", _ => true).SetVoidResult();
        composer.SetupVoid("detachViewportTracker", _ => true).SetVoidResult();
        composer.Setup<bool>("isFocusWithin", _ => true).SetResult(false);
        composer.SetupVoid("focusFirst", _ => true).SetVoidResult();

        var message = JSInterop.SetupModule(MessageModulePath);
        message.Setup<bool>("isSpeechSupported", _ => true).SetResult(false);
        message.Setup<bool>("copyText", _ => true).SetResult(true);
        message.Setup<bool>("speak", _ => true).SetResult(false);
        message.SetupVoid("stopSpeaking", _ => true).SetVoidResult();
        message.SetupVoid("focusCitation", _ => true).SetVoidResult();

        var markdown = JSInterop.SetupModule(MarkdownModulePath);
        markdown.SetupVoid("attach", _ => true).SetVoidResult();
        markdown.SetupVoid("detach", _ => true).SetVoidResult();
    }

    private static IElement AssistantAnswer(IRenderedComponent<AiConversationPage> cut)
        => cut.FindAll("[data-testid='ai-message'][data-role='assistant']").Last();

    [Fact]
    public void Page_RendersTheConversationEmptyStateWithSuggestionsAndTheComposer()
    {
        var cut = Render<AiConversationPage>();

        cut.Find("[data-testid='ai-conversation-demo']").Should().NotBeNull();
        cut.Find("[data-testid='ai-conversation']").Should().NotBeNull();
        cut.Find("[data-testid='ai-conversation-empty']").Should().NotBeNull();

        // The empty state offers the page's suggestion prompts, and they are wired (a chip that raises
        // nothing is not rendered at all).
        cut.FindAll("[data-testid='ai-conversation-suggestion']").Count.Should().Be(3);

        // The composer is the page's one prompt surface: input, attachments, model select and the send slot.
        cut.Find("[data-testid='ai-composer']").Should().NotBeNull();
        cut.Find("[data-testid='ai-composer-input']").Should().NotBeNull();
        cut.Find("[data-testid='ai-composer-attach']").Should().NotBeNull();
        cut.Find("[data-testid='ai-composer-model']").Should().NotBeNull();
        cut.Find("[data-testid='ai-composer-send']").Should().NotBeNull();

        // Every visible string resolves: a missing demo key renders as "[TmDemo_…]" in the test localizer.
        cut.Markup.Should().NotContain("[TmDemo_");
    }

    /// <summary>
    /// Fáze 10 E: the demo pins <c>SuggestionsLayout=Auto</c> so the 375 px E2E can prove the
    /// container-query row without a second conversation.
    /// </summary>
    [Fact]
    public void Conversation_UsesTheAutoSuggestionsLayout()
    {
        var cut = Render<AiConversationPage>();

        var group = cut.Find(".tm-ai-conversation__suggestions");
        group.GetAttribute("data-layout").Should().Be("auto");
        group.ClassList.Should().Contain("tm-ai-conversation__suggestions--auto");
        group.ClassList.Should().NotContain("tm-ai-conversation__suggestions--scroll-row",
            "Auto is a container-query switch, not an always-on nowrap row");
        cut.Find(".tm-ai-conversation").ClassList.Should().Contain("tm-ai-conversation--suggestions-auto");
    }

    [Fact]
    public async Task Suggestion_StreamsAnAnswerWithTableCodeCitationsReasoningAndTwoToolCalls()
    {
        var cut = Render<AiConversationPage>();

        await cut.FindAll("[data-testid='ai-conversation-suggestion']")[0].ClickAsync();

        cut.FindAll("[data-testid='ai-message']").Count.Should().Be(2,
            "the click appends the user prompt and the streaming assistant answer");

        var answer = AssistantAnswer(cut);
        answer.GetAttribute("data-status").Should().Be("completed",
            "the simulated generator ends the stream, which completes the pending answer");

        // Reasoning first, then the answer blocks — the part order is what a reader sees.
        answer.QuerySelector("[data-testid='ai-reasoning']").Should().NotBeNull();

        // A markdown table and a fenced code block render through the safe pipeline.
        answer.QuerySelectorAll("table").Length.Should().Be(1);
        answer.QuerySelector("pre code").Should().NotBeNull();

        // Two citations, each one a chip that points at a cited source, and the source cards underneath.
        var chips = answer.QuerySelectorAll(".tm-markdown-view__citation");
        chips.Length.Should().Be(2);
        chips[0].GetAttribute("data-source-id").Should().Be("docs");
        answer.QuerySelectorAll(".tm-ai-message__source").Length.Should().Be(2);

        // Two tool calls, one successful and one failed (its Retry control is offered).
        var calls = answer.QuerySelectorAll("[data-testid='tool-call']");
        calls.Length.Should().Be(2);
        calls[0].GetAttribute("data-status").Should().Be("succeeded");
        calls[1].GetAttribute("data-status").Should().Be("failed");
        calls[1].QuerySelector(".tm-tool-call__retry").Should().NotBeNull(
            "a failed call is retryable and the page binds OnToolCallRetry");

        // The page reports the finished turn (count, versions, feedback and retries).
        cut.Find("[data-testid='ai-demo-message-count']").TextContent.Trim().Should().Be("2");
        cut.Find("[data-testid='ai-demo-version-count']").TextContent.Trim().Should().Be("1");
    }

    /// <summary>
    /// Plan b75365fb fáze 3: the standalone follow-up card renders a finished answer with its chips, and
    /// its toggle flips <c>FollowUpsDisabled</c> — the chips stay visible and carry the disabled attribute,
    /// never disappear. The card is the E2E surface that proves the disabled chip's computed look.
    /// </summary>
    [Fact]
    public void FollowUpsDemo_Toggle_FlipsTheChips_Disabled()
    {
        var cut = Render<AiConversationPage>();

        // The sample is localized demo data, so the expected chips come from the SAME source the page
        // builds them from — never a count or text typed into the assertion.
        var expectedFollowUps = AiConversationPageSamples.FollowUps(
            new DemoTableEnglishLocalizer(BuildEnglishLocalizer()));

        var card = cut.Find("[data-testid='ai-follow-ups-demo']");
        var chips = card.QuerySelectorAll(".tm-ai-message__follow-up");
        chips.Length.Should().Be(expectedFollowUps.Count,
            "the static sample carries exactly the page's localized follow-up questions");
        chips.Select(chip => chip.TextContent.Trim()).Should().Equal(expectedFollowUps,
            "the chips render the localized labels of the sample, not literals typed into the page");
        chips.Should().OnlyContain(chip => !chip.HasAttribute("disabled"),
            "the chips start enabled, so the E2E can read the enabled look first");

        cut.Find("[data-testid='ai-demo-follow-ups-toggle']").Click();

        cut.Find("[data-testid='ai-demo-follow-ups-toggle']").GetAttribute("aria-pressed").Should().Be("true");
        cut.Find("[data-testid='ai-follow-ups-demo']")
            .QuerySelectorAll(".tm-ai-message__follow-up")
            .Should().HaveCount(expectedFollowUps.Count, "disabling the chips never hides the group")
            .And.OnlyContain(chip => chip.HasAttribute("disabled"),
                "FollowUpsDisabled renders every chip disabled without hiding the group");

        cut.Find("[data-testid='ai-demo-follow-ups-toggle']").Click();
        cut.Find("[data-testid='ai-follow-ups-demo']")
            .QuerySelectorAll(".tm-ai-message__follow-up")
            .Should().OnlyContain(chip => !chip.HasAttribute("disabled"),
                "the toggle is a real two-way switch");
    }

    [Fact]
    public async Task FailedToolCall_IsRetriedAndTurnsSucceeded()
    {
        var cut = Render<AiConversationPage>();
        await cut.FindAll("[data-testid='ai-conversation-suggestion']")[0].ClickAsync();

        cut.Find("[data-testid='ai-demo-tool-retries']").TextContent.Trim().Should().Be("0");

        await cut.Find(".tm-ai-conversation .tm-tool-call--failed .tm-tool-call__retry").ClickAsync();

        // Only the answer's card changed: the standalone gallery keeps its own failed sample.
        AssistantAnswer(cut).QuerySelectorAll(".tm-tool-call--failed").Length.Should().Be(0,
            "the retried call is no longer failed");
        AssistantAnswer(cut).QuerySelector(".tm-tool-call--succeeded").Should().NotBeNull();
        cut.Find("[data-testid='ai-demo-tool-retries']").TextContent.Trim().Should().Be("1");
        cut.Find("[data-testid='ai-demo-last-retry']").TextContent.Should().Contain("fetch_usage");
    }

    [Fact]
    public async Task Regenerate_ArchivesThePreviousGenerationAndOffersTheVersionSwitcher()
    {
        var cut = Render<AiConversationPage>();
        await cut.FindAll("[data-testid='ai-conversation-suggestion']")[0].ClickAsync();

        AssistantAnswer(cut).QuerySelector(".tm-ai-message__versions").Should().BeNull(
            "one generation has nothing to switch between");

        await cut.Find("[data-tm-ai-action='regenerate']").ClickAsync();

        var answer = AssistantAnswer(cut);
        answer.GetAttribute("data-status").Should().Be("completed");
        var switcher = answer.QuerySelector(".tm-ai-message__versions");
        switcher.Should().NotBeNull("the superseded generation is now a selectable version");
        switcher!.QuerySelector(".tm-ai-message__version-label")!.TextContent.Trim().Should().Be("2/2");

        // Stepping back shows the archived generation, and the page records the reader's choice on the model.
        await switcher.QuerySelector("[data-tm-ai-action='version-prev']")!.ClickAsync();
        AssistantAnswer(cut).QuerySelector(".tm-ai-message__version-label")!.TextContent.Trim()
            .Should().Be("1/2");
        cut.Find("[data-testid='ai-demo-version-count']").TextContent.Trim().Should().Be("2");
    }

    [Fact]
    public async Task Feedback_RaisedByTheThumbsUpAction_IsRecordedByThePage()
    {
        var cut = Render<AiConversationPage>();
        await cut.FindAll("[data-testid='ai-conversation-suggestion']")[0].ClickAsync();

        cut.Find("[data-testid='ai-demo-feedback']").TextContent.Trim().Should().Be("none");

        await cut.Find("[data-tm-ai-action='feedback-up']").ClickAsync();

        cut.Find("[data-testid='ai-demo-feedback']").TextContent.Trim().Should().Be("positive");
        cut.Find("[data-tm-ai-action='feedback-up']").GetAttribute("aria-pressed").Should().Be("true");
    }

    [Fact]
    public async Task Composer_SubmitsTheDraftAndClearsIt()
    {
        var cut = Render<AiConversationPage>();

        var input = cut.Find("[data-testid='ai-composer-input']");
        await input.InputAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "Summarise the plan" });

        await cut.Find("[data-testid='ai-composer-send']").ClickAsync();

        cut.FindAll("[data-testid='ai-message'][data-role='user']").Count.Should().Be(1);
        cut.Find("[data-testid='ai-composer-input']").GetAttribute("value").Should().BeNullOrEmpty(
            "the composer clears its draft once the submit handler completed");
        AssistantAnswer(cut).GetAttribute("data-status").Should().Be("completed");
    }

    [Fact]
    public void StandaloneSections_RenderEveryToolCallStateTheThinkingPillAndTheReasoningPanel()
    {
        var cut = Render<AiConversationPage>();

        var states = cut.Find("[data-testid='ai-tool-call-states']");
        var cards = states.QuerySelectorAll("[data-testid='tool-call']");
        cards.Length.Should().Be(6, "queued, running, succeeded, failed, cancelled and a redacted call");
        cards.Select(card => card.GetAttribute("data-status")).Should()
            .Contain(["queued", "running", "succeeded", "failed", "cancelled"]);
        states.QuerySelector(".tm-tool-call__redacted").Should().NotBeNull(
            "a redacted call renders the notice instead of its payloads");
        states.QuerySelector("[data-testid='tool-call-group']").Should().NotBeNull(
            "a run of consecutive calls renders as one group");

        var thinking = cut.Find("[data-testid='ai-thinking-demo']");
        thinking.QuerySelectorAll("[data-testid='ai-thinking']").Length.Should().Be(2);
        thinking.QuerySelector("[data-testid='ai-thinking'][data-completed='true']").Should().NotBeNull();

        var reasoning = cut.Find("[data-testid='ai-reasoning-demo']");
        reasoning.QuerySelectorAll("[data-testid='ai-reasoning']").Length.Should().Be(2);
        reasoning.QuerySelectorAll("[data-testid='ai-reasoning'] .tm-ai-reasoning__step").Length
            .Should().Be(2, "the expanded panel lists its steps; the collapsed one renders no body");
    }

    [Fact]
    public void MarkdownXssSample_RendersNothingExecutable()
    {
        var cut = Render<AiConversationPage>();

        var hostile = cut.Find("[data-testid='ai-markdown-xss']");
        hostile.QuerySelectorAll("script").Length.Should().Be(0, "raw HTML is escaped by the pipeline");
        hostile.QuerySelectorAll("[onerror]").Length.Should().Be(0);
        hostile.QuerySelectorAll("[onclick]").Length.Should().Be(0);
        hostile.QuerySelectorAll("img").Length.Should().Be(0,
            "an image with a non-http source is dropped to its alt text");

        var links = hostile.QuerySelectorAll("a");
        links.Length.Should().BeGreaterThan(0);
        var hrefs = links.Select(link => link.GetAttribute("href") ?? string.Empty).ToList();
        hrefs.Should().NotContain(href => href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase));
        hrefs.Should().NotContain(href => href.StartsWith("data:", StringComparison.OrdinalIgnoreCase));
        hrefs.Should().NotContain(href => href.StartsWith("vbscript:", StringComparison.OrdinalIgnoreCase));

        // …and the sample still renders as readable text, so the demo shows WHAT was neutralized.
        hostile.TextContent.Should().Contain("script");
    }

    [Fact]
    public void PendingPlaceholderCard_RendersTheOptInWaitLine()
    {
        var cut = Render<AiConversationPage>();

        var card = cut.Find("[data-testid='ai-pending-demo']");
        var placeholder = card.QuerySelector(".tm-ai-message__pending");
        placeholder.Should().NotBeNull("the Pending sample turn carries the opt-in wait line");
        placeholder!.GetAttribute("aria-busy").Should().Be("true");
        placeholder.TextContent.Trim().Should().NotBeEmpty();
        placeholder.ClassList.Should().NotContain("tm-ai-streaming",
            "the caret belongs to a streaming answer; the queued turn waits without one");
    }

    /// <summary>
    /// The host-driven simulation opens NO stream: it flips <c>Generating</c>, produces the answer itself
    /// and closes the window. The window's wait runs on the injected fake clock, so the test parks the
    /// click, asserts the OPEN window, then advances and asserts the finished answer
    /// (<c>Completed</c>, window closed, the "scroll to the first turn" control appears — it exercises
    /// <c>ScrollToMessageAsync</c>).
    /// </summary>
    [Fact]
    public async Task HostDrivenSimulation_CompletesTheAnswerWithoutAStream()
    {
        var cut = Render<AiConversationPage>();

        cut.Find("[data-testid='ai-demo-busy-window']").TextContent.Trim().Should().Be("closed");
        cut.FindAll("[data-testid='ai-demo-scroll-first']").Should().BeEmpty(
            "a scroll control without a turn to scroll to would be a dead button");

        // The handler parks on the fake clock's delay: start the click without awaiting the whole window.
        var window = cut.InvokeAsync(() => cut.Find("[data-testid='ai-demo-simulate-busy']").Click());
        cut.WaitForState(() => cut.Find(".tm-ai-conversation").GetAttribute("data-generating") == "true");

        cut.FindAll("[data-testid='ai-message']").Count.Should().Be(2,
            "the simulation appends the user prompt and its answer");
        cut.Find(".tm-ai-conversation").GetAttribute("data-generating").Should().Be("true");
        cut.Find("[data-testid='ai-demo-simulate-busy']").GetAttribute("aria-disabled").Should().Be("true",
            "aria-disabled (not the native attribute) marks the busy window");
        cut.Find("[data-testid='ai-demo-busy-window']").TextContent.Trim().Should().Be("open");
        cut.FindAll(".tm-ai-conversation .tm-ai-message__pending").Should().ContainSingle(
            "the queued turn names its wait through the template's PendingText");

        // Advance ON THE DISPATCHER: the delay's continuation (and the handler's StateHasChanged) must run
        // on the renderer's thread — advancing from the test thread would resume the handler on the wrong
        // thread and the render would never flush.
        await cut.InvokeAsync(() => _clock.Advance(HostDrivenBusy));
        await window;

        var answer = cut.FindAll("[data-testid='ai-message'][data-role='assistant']")[0];
        answer.GetAttribute("data-status").Should().Be("completed",
            "the host completed the turn itself; no StreamAsync ran");
        answer.TextContent.Should().Contain("retry budget");
        cut.Find("[data-testid='ai-demo-busy-window']").TextContent.Trim().Should().Be("closed",
            "the window closed once the host's answer arrived");
        cut.Find(".tm-ai-conversation").GetAttribute("data-generating").Should().Be("false");
        cut.Find("[data-testid='ai-demo-simulate-busy']").GetAttribute("aria-disabled").Should().Be("false");

        await cut.Find("[data-testid='ai-demo-scroll-first']").ClickAsync();
    }

    /// <summary>
    /// The built-in Stop pill during the host-driven window raises <c>OnStop</c> once; the page's handler
    /// cancels the window's own delay, marks the answer <c>Stopped</c> and closes the busy flag — the
    /// whole chain a real host runs against its own HTTP call.
    /// </summary>
    [Fact]
    public async Task HostDrivenStop_CancelsTheWindowAndMarksTheAnswerStopped()
    {
        var cut = Render<AiConversationPage>();

        var window = cut.InvokeAsync(() => cut.Find("[data-testid='ai-demo-simulate-busy']").Click());
        cut.WaitForState(() => cut.Find(".tm-ai-conversation").GetAttribute("data-generating") == "true");
        cut.FindAll("[data-testid='ai-conversation-stop']").Should().ContainSingle(
            "a host-driven window is a running generation");

        await cut.Find("[data-testid='ai-conversation-stop']").ClickAsync();
        await window;

        var answer = cut.FindAll("[data-testid='ai-message'][data-role='assistant']")[0];
        answer.GetAttribute("data-status").Should().Be("stopped", "the host's stop marks the queued turn");
        answer.TextContent.Should().NotContain("retry budget", "the cancelled window never wrote the answer");
        cut.Find(".tm-ai-conversation").GetAttribute("data-generating").Should().Be("false");
        cut.Find("[data-testid='ai-demo-busy-window']").TextContent.Trim().Should().Be("closed");
        cut.FindAll("[data-testid='ai-conversation-stop']").Should().BeEmpty(
            "the window's one stop is spent");
        cut.FindAll("[data-testid='ai-conversation-announcement']").Should().BeEmpty(
            "a stopped window is not announced as a completed answer");

        // The window is spent: the pill is gone, and a defensive second StopAsync raises nothing.
        var conversation = cut.FindComponent<TmAiConversation>().Instance;
        await cut.InvokeAsync(() => conversation.StopAsync());
        answer.GetAttribute("data-status").Should().Be("stopped");
        cut.FindAll("[data-testid='ai-conversation-announcement']").Should().BeEmpty();
    }

    /// <summary>
    /// The "Announce a host note" button speaks through <see cref="TmAiConversation.AnnounceAsync"/>
    /// OUTSIDE any window: each click is a new keyed node (an identical wording is still re-announced),
    /// so two clicks leave two distinct nodes — the idle host channel accumulates until a generation
    /// window or a stream re-owns the live region.
    /// </summary>
    [Fact]
    public async Task HostAnnounceButton_SpeaksIntoTheTranscriptOutsideAWindow()
    {
        var cut = Render<AiConversationPage>();

        await cut.Find("[data-testid='ai-demo-announce']").ClickAsync();
        var notes = cut.FindAll("[data-testid='ai-conversation-announcement'][data-kind='host']");
        notes.Should().ContainSingle("one click speaks one host note");
        AssertHostNotesCounterMatchesTranscript(cut, 1);

        await cut.Find("[data-testid='ai-demo-announce']").ClickAsync();
        notes = cut.FindAll("[data-testid='ai-conversation-announcement'][data-kind='host']");
        notes.Should().HaveCount(2, "a second identical wording is a NEW keyed node, never swallowed by the live region");
        notes[1].Should().NotBeSameAs(notes[0]);
        notes.Select(node => node.TextContent).Should().OnlyContain(
            text => text == notes[0].TextContent, "both notes carry the same localized wording");
        AssertHostNotesCounterMatchesTranscript(cut, 2);
    }

    /// <summary>
    /// A host note spoken INSIDE the simulated host-driven window survives its close and is cleared by
    /// the NEXT window's edge — the host-channel symmetry with a stream start that the demo page makes
    /// visible (the E2E lane drives the same flow in a real browser).
    /// </summary>
    [Fact]
    public async Task HostAnnounceButton_InsideTheSimulatedWindow_TheNextWindowEdgeClearsTheNote()
    {
        var cut = Render<AiConversationPage>();

        var window = cut.InvokeAsync(() => cut.Find("[data-testid='ai-demo-simulate-busy']").Click());
        cut.WaitForState(() => cut.Find(".tm-ai-conversation").GetAttribute("data-generating") == "true");

        await cut.Find("[data-testid='ai-demo-announce']").ClickAsync();
        cut.FindAll("[data-testid='ai-conversation-announcement'][data-kind='host']").Should().ContainSingle(
            "a note spoken inside the open window is in the transcript while the window runs");
        AssertHostNotesCounterMatchesTranscript(cut, 1);

        await cut.InvokeAsync(() => _clock.Advance(HostDrivenBusy));
        await window;
        cut.Find(".tm-ai-conversation").GetAttribute("data-generating").Should().Be("false");
        cut.FindAll("[data-testid='ai-conversation-announcement'][data-kind='host']").Should().ContainSingle(
            "the window's close is not a clearing edge — the note survives next to the completion note");
        cut.FindAll("[data-testid='ai-conversation-announcement'][data-kind='completion']").Should().ContainSingle();
        AssertHostNotesCounterMatchesTranscript(cut, 1);

        var secondWindow = cut.InvokeAsync(() => cut.Find("[data-testid='ai-demo-simulate-busy']").Click());
        cut.WaitForState(() => cut.Find(".tm-ai-conversation").GetAttribute("data-generating") == "true");
        cut.FindAll("[data-testid='ai-conversation-announcement'][data-kind='host']").Should().BeEmpty(
            "the next window's edge re-owns the host channel — the previous window's note is cleared");
        AssertHostNotesCounterMatchesTranscript(cut, 0);

        await cut.InvokeAsync(() => _clock.Advance(HostDrivenBusy));
        await secondWindow;
    }

    [Fact]
    public async Task Reset_RestoresTheEmptyState()
    {
        var cut = Render<AiConversationPage>();
        await cut.FindAll("[data-testid='ai-conversation-suggestion']")[0].ClickAsync();
        cut.FindAll("[data-testid='ai-message']").Count.Should().Be(2);
        AssertHostNotesCounterMatchesTranscript(cut, 0);

        await cut.Find("[data-testid='ai-demo-reset']").ClickAsync();

        cut.FindAll("[data-testid='ai-message']").Count.Should().Be(0);
        cut.Find("[data-testid='ai-conversation-empty']").Should().NotBeNull();
        cut.Find("[data-testid='ai-demo-feedback']").TextContent.Trim().Should().Be("none");
        AssertHostNotesCounterMatchesTranscript(cut, 0);
    }

    [Fact]
    public async Task StreamedToolCalls_UseTheLocalizedDisplayNames()
    {
        var cut = RenderWithTaggedLocalizer();
        await cut.FindAll("[data-testid='ai-conversation-suggestion']")[0].ClickAsync();

        var names = AssistantAnswer(cut).QuerySelectorAll(".tm-tool-call__name")
            .Select(node => node.TextContent.Trim())
            .ToList();

        names.Should().HaveCount(2, "the simulated answer runs two tool calls");
        names.Should().OnlyContain(name => name.EndsWith(TaggedLocalizer.Tag, StringComparison.Ordinal),
            "a tool display name is an interface label in the card, so it resolves through ITmLocalizer "
            + "instead of being typed into the page");
        names.Should().Contain(name => name.StartsWith("Search the docs", StringComparison.Ordinal));
        names.Should().Contain(name => name.StartsWith("Fetch usage", StringComparison.Ordinal));
    }

    [Fact]
    public void SlashCommands_UseTheLocalizedTitles()
    {
        var cut = RenderWithTaggedLocalizer();

        cut.Find("[data-testid='ai-composer-input']").Input("/");
        cut.WaitForAssertion(() => cut.FindAll("[role='option']").Should().HaveCount(3));

        var titles = cut.FindAll(".tm-ai-composer__option-title")
            .Select(node => node.TextContent.Trim())
            .ToList();

        titles.Should().HaveCount(3);
        titles.Should().OnlyContain(title => title.EndsWith(TaggedLocalizer.Tag, StringComparison.Ordinal),
            "the command title is the label the / list shows, so it resolves through ITmLocalizer too");
    }

    /// <summary>
    /// The page's visible host-notes counter is the mirror of the transcript's
    /// <c>[data-kind='host']</c> count — the pair must agree after every step, so a drift between what
    /// the demo claims and what the live region holds turns red here.
    /// </summary>
    private static void AssertHostNotesCounterMatchesTranscript(
        IRenderedComponent<AiConversationPage> cut, int expected)
    {
        cut.FindAll("[data-testid='ai-conversation-announcement'][data-kind='host']").Should().HaveCount(expected,
            "the transcript holds exactly the expected host notes");
        cut.Find("[data-testid='ai-demo-host-notes']").TextContent.Trim().Should()
            .Be(expected.ToString(CultureInfo.InvariantCulture),
                "the demo's visible counter mirrors the transcript's host-note count");
    }

    /// <summary>Renders the page with a localizer that tags every demo value it resolves.</summary>
    private IRenderedComponent<AiConversationPage> RenderWithTaggedLocalizer()
    {
        // The LAST registration wins, so this replaces the base's demo-table localizer for this test only.
        Services.AddSingleton<ITmLocalizer>(new TaggedLocalizer(new DemoTableEnglishLocalizer(BuildEnglishLocalizer())));
        return Render<AiConversationPage>();
    }

    /// <summary>
    /// Wraps the demo localizer and tags every value it returns, so an assertion can tell a label that went
    /// through <c>ITmLocalizer</c> from the same words typed into the page as a literal. A missing key keeps
    /// its <c>[key]</c> fallback untagged, so this never hides an unresolvable key.
    /// </summary>
    private sealed class TaggedLocalizer(ITmLocalizer inner) : ITmLocalizer
    {
        internal const string Tag = "·";

        public CultureInfo Culture => inner.Culture;

        public string this[string key] => Tagged(inner[key]);

        public string this[string key, params object[] arguments] => Tagged(inner[key, arguments]);

        public bool TryGetString(string key, out string value)
        {
            var found = inner.TryGetString(key, out value);
            value = Tagged(value);
            return found;
        }

        private static string Tagged(string value)
            => value.Length > 0 && value[0] == '[' ? value : value + Tag;
    }
}
