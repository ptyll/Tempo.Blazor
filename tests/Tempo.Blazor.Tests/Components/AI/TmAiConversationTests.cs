using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// bUnit contract of the RENDERING half of <see cref="TmAiConversation"/>: the transcript is a
/// <c>role="log"</c> live region, the turns render in order through <see cref="TmAiMessage"/> (or the host
/// template), and an empty conversation offers the host's suggestion prompts instead of a blank panel.
/// </summary>
/// <remarks>
/// The streaming, auto-scroll and render-window halves live in their own files
/// (<c>TmAiConversationStreamingTests</c>, <c>TmAiConversationScrollTests</c>,
/// <c>TmAiConversationWindowTests</c>); the stylesheet hygiene gate is <c>TmAiConversationCssTests</c>.
/// </remarks>
public sealed class TmAiConversationTests : LocalizationTestBase
{
    internal const string InteropModulePath = "./_content/Tempo.Blazor.AI/js/ai-conversation.js";

    internal static AiMessage Message(
        string id,
        string markdown = "An answer.",
        AiMessageRole role = AiMessageRole.Assistant,
        AiMessageStatus status = AiMessageStatus.Completed)
    {
        var message = new AiMessage { Id = id, Role = role, Status = status };
        message.Parts.Add(new AiTextPart(markdown));
        return message;
    }

    internal static List<AiMessage> Conversation(int count, AiMessageStatus status = AiMessageStatus.Completed)
        => Enumerable.Range(0, count).Select(index => Message($"m-{index}", $"Answer {index}.", AiMessageRole.Assistant, status)).ToList();

    /// <summary>
    /// Registers the module mock the component imports on its first render. <paramref name="nearBottom"/>
    /// is what the browser's scroll probe answers — the one input that decides whether the transcript may
    /// follow new content.
    /// </summary>
    internal BunitJSModuleInterop SetupInterop(bool nearBottom = true)
    {
        var module = JSInterop.SetupModule(InteropModulePath);
        module.SetupVoid("attach", _ => true).SetVoidResult();
        module.SetupVoid("detach", _ => true).SetVoidResult();
        module.Setup<bool>("isNearBottom", _ => true).SetResult(nearBottom);
        module.SetupVoid("scrollToBottom", _ => true).SetVoidResult();
        return module;
    }

    /// <summary>Number of browser scroll calls recorded for the mocked module.</summary>
    internal static int Count(BunitJSModuleInterop module, string identifier)
        => module.Invocations.Count(invocation => invocation.Identifier == identifier);

    /// <summary>
    /// A conversation that counts its OWN renders. bUnit's <c>RenderCount</c> counts every render of the
    /// fragment's subtree, so one conversation render of a transcript holding a single turn is counted three
    /// times (the conversation, <see cref="TmAiMessage"/> and its markdown view) — useless for asserting a
    /// render throttle. <see cref="ComponentBase.OnAfterRender"/> fires once per render of THIS component.
    /// </summary>
    internal sealed class RenderCountingConversation : TmAiConversation
    {
        internal int OwnRenderCount { get; private set; }

        /// <inheritdoc />
        protected override void OnAfterRender(bool firstRender)
        {
            OwnRenderCount++;
            base.OnAfterRender(firstRender);
        }
    }

    // ── Transcript structure and the single live region ───────────────────────────────────────────

    [Fact]
    public void Root_CarriesTheStableTestIdAndTheGeneratingFlag()
    {
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, Conversation(1)));

        var root = cut.Find(".tm-ai-conversation");
        root.GetAttribute("data-testid").Should().Be("ai-conversation");
        root.GetAttribute("data-generating").Should().Be("false");
    }

    /// <summary>
    /// The transcript is the component's ONE live region: a polite <c>role="log"</c> that announces
    /// INSERTED turns and the completion note only. <c>aria-relevant="additions"</c> is what keeps the
    /// streamed text of a reply from being read out token by token — a log with the default
    /// <c>additions text</c> would flood a screen reader for the whole answer.
    /// </summary>
    [Fact]
    public void Transcript_IsTheComponentsSinglePoliteLiveRegion()
    {
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, Conversation(1)));

        var log = cut.Find("[role='log']");
        log.GetAttribute("aria-live").Should().Be("polite");
        log.GetAttribute("aria-relevant").Should().Be("additions");
        log.GetAttribute("aria-atomic").Should().Be("false");
        log.GetAttribute("aria-label").Should().Be("Conversation");
        log.GetAttribute("tabindex").Should().Be("0", "a scrollable transcript must be reachable with the keyboard (WCAG 2.1)");

        var ownLiveRegions = cut.FindAll("[aria-live], [role='log'], [role='status'], [role='alert']")
            .Where(element => element.Closest(".tm-ai-message") is null)
            .ToList();

        ownLiveRegions.Should().ContainSingle(
            "the conversation owns exactly one live region — the transcript; every message keeps its own");
    }

    [Fact]
    public void RequestsThatAreNotBeingStreamed_AreNotBusy()
    {
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, Conversation(1)));

        cut.Find("[role='log']").GetAttribute("aria-busy").Should().BeNull();
    }

    // ── Messages ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Messages_RenderInOrder_ThroughTheMessageComponent()
    {
        var messages = new List<AiMessage> { Message("user-1", "What is Tempo?", AiMessageRole.User), Message("assistant-1") };

        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, messages));

        var rows = cut.FindAll(".tm-ai-conversation__message");
        rows.Should().HaveCount(2);
        rows[0].GetAttribute("data-message-id").Should().Be("user-1");
        rows[1].GetAttribute("data-message-id").Should().Be("assistant-1");
        rows[0].QuerySelector(".tm-ai-message--user").Should().NotBeNull();
        rows[1].QuerySelector(".tm-ai-message--assistant").Should().NotBeNull();
    }

    [Fact]
    public void MessageTemplate_ReplacesTheBuiltInMessage()
    {
        var cut = Render<TmAiConversation>(parameters => parameters
            .Add(component => component.Messages, new List<AiMessage> { Message("assistant-1") })
            .Add(component => component.MessageTemplate, (AiMessage message) => builder =>
            {
                builder.OpenElement(0, "p");
                builder.AddAttribute(1, "class", "host-message");
                builder.AddContent(2, $"host:{message.Id}");
                builder.CloseElement();
            }));

        cut.FindAll("p.host-message").Should().ContainSingle().Which.TextContent.Should().Be("host:assistant-1");
        cut.FindComponents<TmAiMessage>().Should().BeEmpty("the host template owns the whole turn");
    }

    // ── Empty state ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EmptyConversation_RendersTheLocalizedIntro()
    {
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, Array.Empty<AiMessage>()));

        var empty = cut.Find(".tm-ai-conversation__empty");
        empty.Should().NotBeNull();
        empty.QuerySelector(".tm-ai-conversation__empty-title")!.TextContent.Should().Be("Ask anything");
        empty.QuerySelector(".tm-ai-conversation__empty-text")!.TextContent
            .Should().Be("Start a conversation by sending a prompt — the answers will appear here.");
    }

    [Fact]
    public void EmptyConversation_RendersTheSuggestions_AndRaisesTheChosenOne()
    {
        string? raised = null;
        var cut = Render<TmAiConversation>(parameters => parameters
            .Add(component => component.Messages, Array.Empty<AiMessage>())
            .Add(component => component.Suggestions, new[] { "Summarize this page", "Draft a reply" })
            .Add(component => component.OnSuggestion, (string text) => raised = text));

        var group = cut.Find(".tm-ai-conversation__suggestions");
        group.GetAttribute("aria-label").Should().Be("Suggested prompts");
        var chips = cut.FindAll(".tm-ai-conversation__suggestion");
        chips.Should().HaveCount(2);

        chips[1].Click();

        raised.Should().Be("Draft a reply");
    }

    /// <summary>
    /// A suggestion chip without a bound handler would be a button that does nothing, so the chips are
    /// offered only when the host listens. The intro text stays.
    /// </summary>
    [Fact]
    public void Suggestions_WithoutAHandler_AreNotOffered()
    {
        var cut = Render<TmAiConversation>(parameters => parameters
            .Add(component => component.Messages, Array.Empty<AiMessage>())
            .Add(component => component.Suggestions, new[] { "Summarize this page" }));

        cut.FindAll(".tm-ai-conversation__suggestion").Should().BeEmpty();
        cut.Find(".tm-ai-conversation__empty").Should().NotBeNull();
    }

    [Fact]
    public void EmptyState_DisappearsOnceTheConversationHasATurn()
    {
        var messages = new List<AiMessage>();
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, messages));
        cut.FindAll(".tm-ai-conversation__empty").Should().ContainSingle();

        messages.Add(Message("user-1", "Hello", AiMessageRole.User));
        cut.Render(parameters => parameters.Add(component => component.Messages, messages));

        cut.FindAll(".tm-ai-conversation__empty").Should().BeEmpty();
    }

    // ── Localization, ids, host hooks ─────────────────────────────────────────────────────────────

    [Fact]
    public void CzechLocalizer_TranslatesTheChrome()
    {
        Services.AddSingleton<Tempo.Blazor.Localization.ITmLocalizer>(BuildCzechLocalizer());

        var cut = Render<TmAiConversation>(parameters => parameters
            .Add(component => component.Messages, Array.Empty<AiMessage>())
            .Add(component => component.Suggestions, new[] { "Shrň tuto stránku" })
            .Add(component => component.OnSuggestion, (string _) => { }));

        cut.Find("[role='log']").GetAttribute("aria-label").Should().Be("Konverzace");
        cut.Find(".tm-ai-conversation__empty-title").TextContent.Should().Be("Na co se chcete zeptat");
        cut.Find(".tm-ai-conversation__suggestions").GetAttribute("aria-label").Should().Be("Navržené dotazy");
    }

    [Fact]
    public void TestIdPrefix_NamespacesTheInternalTestIds()
    {
        var cut = Render<TmAiConversation>(parameters => parameters
            .Add(component => component.Messages, Array.Empty<AiMessage>())
            .Add(component => component.TestIdPrefix, "chat")
            .Add(component => component.Suggestions, new[] { "Summarize" })
            .Add(component => component.OnSuggestion, (string _) => { }));

        cut.FindAll("[data-testid='chat-ai-conversation-log']").Should().ContainSingle();
        cut.FindAll("[data-testid='chat-ai-conversation-empty']").Should().ContainSingle();
        cut.FindAll("[data-testid='chat-ai-conversation-suggestion']").Should().ContainSingle();
    }

    [Fact]
    public void ClassAndAdditionalAttributes_LandOnTheRoot()
    {
        var cut = Render<TmAiConversation>(parameters => parameters
            .Add(component => component.Messages, Conversation(1))
            .Add(component => component.Class, "host-class")
            .AddUnmatched("data-host", "value"));

        var root = cut.Find(".tm-ai-conversation");
        root.ClassList.Should().Contain("host-class");
        root.GetAttribute("data-host").Should().Be("value");
    }

    [Fact]
    public void Height_AppliesTheHostLength()
    {
        var cut = Render<TmAiConversation>(parameters => parameters
            .Add(component => component.Messages, Conversation(1))
            .Add(component => component.Height, "32rem"));

        cut.Find(".tm-ai-conversation").GetAttribute("style").Should().Contain("block-size: 32rem");
    }

    /// <summary>
    /// A static host (prerendering, or a test without a JS runtime) has no interop: the transcript must
    /// still render, and no browser exception may escape into the render loop.
    /// </summary>
    [Fact]
    public void WithoutInterop_TheTranscriptStillRenders()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, Conversation(2)));

        cut.FindAll(".tm-ai-conversation__message").Should().HaveCount(2);
        cut.Instance.IsGenerating.Should().BeFalse();
    }

    /// <summary>A host template is rendered raw, so a caller can compose arbitrary markup without a wrapper.</summary>
    [Fact]
    public void RootWithoutUnmatchedAttributes_RendersNoHostState()
    {
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, Conversation(1)));

        cut.Find(".tm-ai-conversation").GetAttribute("data-host").Should().BeNull();
        cut.Find(".tm-ai-conversation").GetAttribute("style").Should().BeNull();
    }

    /// <summary>An explicit empty template-free render must not invent turns.</summary>
    [Fact]
    public void NullMessages_RenderTheEmptyState()
    {
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, null));

        cut.FindAll(".tm-ai-conversation__message").Should().BeEmpty();
        cut.Find(".tm-ai-conversation__empty").Should().NotBeNull();
    }

    /// <summary>The log element is rendered before any turn exists, as a live region must be.</summary>
    [Fact]
    public void Transcript_ExistsBeforeTheFirstTurn()
    {
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, Array.Empty<AiMessage>()));

        cut.FindAll("[role='log']").Should().ContainSingle();
    }

    /// <summary>The keyed rows keep a streamed turn's DOM node when the list grows.</summary>
    [Fact]
    public void Rows_AreKeyedByMessageId()
    {
        var messages = Conversation(1);
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, messages));
        var before = cut.Find(".tm-ai-conversation__message").GetAttribute("data-message-id");

        messages.Add(Message("m-1", "Second answer."));
        cut.Render(parameters => parameters.Add(component => component.Messages, messages));

        cut.FindAll(".tm-ai-conversation__message").Should().HaveCount(2);
        cut.FindAll(".tm-ai-conversation__message")[0].GetAttribute("data-message-id").Should().Be(before);
    }

    /// <summary>A host that renders the transcript inside a page layout gets a labelled region, not a bare div.</summary>
    [Fact]
    public void Transcript_IsLabelledForAssistiveTechnology()
    {
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, Conversation(1)));

        cut.Find("[role='log']").GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace();
        cut.Find("[role='log']").GetAttribute("aria-label").Should().NotStartWith("[");
    }
}
