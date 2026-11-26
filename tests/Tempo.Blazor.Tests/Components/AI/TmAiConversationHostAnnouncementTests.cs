using System.Runtime.CompilerServices;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// bUnit contract of the HOST announcements (<see cref="TmAiConversation.AnnounceAsync"/>) across a
/// host-driven generation window (<see cref="TmAiConversation.Generating"/>): the window edge owns the
/// host channel exactly like <see cref="TmAiConversation.StreamAsync"/> does — opening the window clears
/// the previous notes (the new turn's window re-owns the live region), a note spoken INSIDE the open
/// window survives until the next edge, and a repeated wording arrives as a NEW keyed node so a live
/// region never swallows it. Before this contract only <see cref="TmAiConversation.StreamAsync"/> cleared
/// the host notes at stream start, so a host that drives its own windows accumulated one stale note per
/// turn in the transcript DOM (remaining 8cc5aae1, found in the phase-2 review of plan b75365fb).
/// </summary>
/// <remarks>
/// The completion note is a separate, single replaceable slot (see
/// <see cref="TmAiConversationCompletionNoteTests"/>); these tests pin the host channel only, keyed off
/// <c>data-kind="host"</c> so the completion note never pollutes the count.
/// </remarks>
public sealed class TmAiConversationHostAnnouncementTests : LocalizationTestBase
{
    private const string HostNoteTestId = "[data-testid='ai-conversation-announcement'][data-kind='host']";
    private const string CompletionNoteTestId = "[data-testid='ai-conversation-announcement'][data-kind='completion']";

    private static readonly DateTimeOffset Start = new(2026, 11, 26, 9, 0, 0, TimeSpan.Zero);

    private void SetupInterop()
    {
        var module = JSInterop.SetupModule(TmAiConversationTests.InteropModulePath);
        module.SetupVoid("attach", _ => true).SetVoidResult();
        module.SetupVoid("detach", _ => true).SetVoidResult();
        module.Setup<bool>("isNearBottom", _ => true).SetResult(true);
        module.SetupVoid("scrollToBottom", _ => true).SetVoidResult();
    }

    /// <summary>Appends the host's own user + completed assistant turn, the shape after a host-driven answer.</summary>
    private static void AppendCompletedTurn(List<AiMessage> messages, string id)
    {
        messages.Add(new AiMessage(AiMessageRole.User, $"question for {id}") { Status = AiMessageStatus.Completed });
        messages.Add(new AiMessage
        {
            Id = id,
            Role = AiMessageRole.Assistant,
            Status = AiMessageStatus.Completed,
        });
    }

    private static async IAsyncEnumerable<AiStreamDelta> FinishedStream(
        AiStreamDelta first,
        AiStreamDelta second,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return first;
        await Task.Yield();
        yield return second;
    }

    // ── The accumulation the phase removes ──────────────────────────────────────────────────────

    /// <summary>
    /// The shape of a consumer that drives its own generation (EmusCz): every window carries ONE host
    /// note spoken inside the window. After N turns the transcript must still hold exactly ONE host note
    /// — the current one — not N stale ones a screen reader browsing the transcript would re-read.
    /// </summary>
    [Fact]
    public async Task HostDrivenWindow_HostNoteInsideThreeTurns_LeavesExactlyOneHostNote()
    {
        var messages = TmAiConversationTests.Conversation(1);
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, messages));

        for (var turn = 1; turn <= 3; turn++)
        {
            cut.Render(parameters => parameters.Add(component => component.Generating, true));
            await cut.InvokeAsync(() => cut.Instance.AnnounceAsync($"Host note {turn}"));
            AppendCompletedTurn(messages, $"answer-{turn}");
            cut.Render(parameters => parameters.Add(component => component.Generating, false));

            var hostNotes = cut.FindAll(HostNoteTestId);
            hostNotes.Should().HaveCount(1,
                $"after turn {turn} the transcript holds exactly one host note — the window edge cleared the previous turns' notes");
            hostNotes[0].TextContent.Should().Be($"Host note {turn}",
                "the surviving note is the CURRENT window's one, not a stale note of a past turn");
        }
    }

    /// <summary>
    /// Opening the next window clears the previous window's host note IMMEDIATELY — before the host has
    /// spoken anything new — the same bookkeeping <see cref="TmAiConversation.StreamAsync"/> does at
    /// stream start. A mutant without the edge clear keeps the stale note and is RED here.
    /// </summary>
    [Fact]
    public async Task HostDrivenWindow_OpeningTheNextWindow_ClearsThePreviousHostNote()
    {
        var messages = TmAiConversationTests.Conversation(1);
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, messages));

        cut.Render(parameters => parameters.Add(component => component.Generating, true));
        await cut.InvokeAsync(() => cut.Instance.AnnounceAsync("Host note 1"));
        AppendCompletedTurn(messages, "answer-1");
        cut.Render(parameters => parameters.Add(component => component.Generating, false));
        cut.FindAll(HostNoteTestId).Should().ContainSingle("the first window's note is present after its close");

        cut.Render(parameters => parameters.Add(component => component.Generating, true));

        cut.FindAll(HostNoteTestId).Should().BeEmpty(
            "the new window re-owns the host channel — the previous window's note is stale before the host speaks again");
    }

    /// <summary>
    /// A repeated wording across two windows is a NEW keyed node: a live region ignores an unchanged
    /// text node, so re-using the node would silently swallow the second announcement (the same rule the
    /// completion note follows). The bUnit <c>NotBeSameAs</c> is only the safeguard — the decisive
    /// proof is the WASM E2E <c>isConnected</c> assertion on the previous turn's detached element
    /// handle (<c>HostDrivenHostAnnouncements_ThreeTurns_KeepExactlyOneHostNote</c>), which sees the
    /// real DOM the bUnit render tree can only approximate.
    /// </summary>
    [Fact]
    public async Task HostDrivenWindow_SameNoteEachTurn_TheRepeatIsANewKeyedNode()
    {
        var messages = TmAiConversationTests.Conversation(1);
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, messages));

        cut.Render(parameters => parameters.Add(component => component.Generating, true));
        await cut.InvokeAsync(() => cut.Instance.AnnounceAsync("Working on it…"));
        AppendCompletedTurn(messages, "answer-1");
        cut.Render(parameters => parameters.Add(component => component.Generating, false));
        var first = cut.Find(HostNoteTestId);

        cut.Render(parameters => parameters.Add(component => component.Generating, true));
        await cut.InvokeAsync(() => cut.Instance.AnnounceAsync("Working on it…"));
        AppendCompletedTurn(messages, "answer-2");
        cut.Render(parameters => parameters.Add(component => component.Generating, false));
        var second = cut.Find(HostNoteTestId);

        second.TextContent.Should().Be(first.TextContent, "both windows spoke the same wording");
        second.Should().NotBeSameAs(first,
            "the repeat is a NEW keyed node — an unchanged text node in a live region is not re-announced");
    }

    // ── What the edge does NOT touch ────────────────────────────────────────────────────────────

    /// <summary>
    /// A note spoken INSIDE the open window survives the window's close: the clear happens on the
    /// false→true edge (before the host speaks), never on the true→false edge, so the window's own note
    /// is still in the transcript next to the completion note it shares the live region with.
    /// </summary>
    [Fact]
    public async Task HostAnnouncement_InsideTheOpenWindow_SurvivesTheCompletionEdge()
    {
        var messages = TmAiConversationTests.Conversation(1);
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, messages));

        cut.Render(parameters => parameters.Add(component => component.Generating, true));
        await cut.InvokeAsync(() => cut.Instance.AnnounceAsync("Host note"));
        AppendCompletedTurn(messages, "answer-1");
        cut.Render(parameters => parameters.Add(component => component.Generating, false));

        cut.FindAll(HostNoteTestId).Should().ContainSingle(
            "the window's close is not an edge that clears — the note spoken inside the window stays");
        cut.FindAll(CompletionNoteTestId).Should().ContainSingle("the completion announced its answer");
        cut.FindAll("[data-testid='ai-conversation-announcement']").Select(node => node.TextContent)
            .Should().Equal(
                new[] { "Host note", "Answer complete" },
                "the host note renders before the completion note, which is always the last announcement node");
    }

    /// <summary>
    /// A host note spoken OUTSIDE any window (an idle-time announcement) survives plain parameter
    /// re-renders — only the window edge or a stream start clears it. This is the ordering guarantee:
    /// clear on the edge FIRST, then the new window's notes.
    /// </summary>
    [Fact]
    public async Task HostAnnouncement_WithoutAWindow_SurvivesAParameterRender_ButNotTheNextWindowEdge()
    {
        var messages = TmAiConversationTests.Conversation(1);
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, messages));

        await cut.InvokeAsync(() => cut.Instance.AnnounceAsync("Idle note"));
        cut.Render(parameters => parameters.Add(component => component.Messages, messages));

        cut.FindAll(HostNoteTestId).Should().ContainSingle(
            "an idle announcement is not cleared by a parameter re-render — nothing re-owned the channel");

        cut.Render(parameters => parameters.Add(component => component.Generating, true));

        cut.FindAll(HostNoteTestId).Should().BeEmpty(
            "the next window edge is the first clear — an idle note never leaks into the new window");
    }

    /// <summary>
    /// The ordering pitfall of the edge: a note spoken just BEFORE the window opens — in the same
    /// render batch as the false→true transition — is wiped by the edge together with the previous
    /// window's notes. A host must speak only after the <c>Generating</c> = true render has landed;
    /// this test pins the trap so a "speak first, then open" refactor turns red.
    /// </summary>
    [Fact]
    public async Task HostAnnouncement_SpokenInTheSameBatchBeforeTheEdge_IsClearedWithThePreviousWindow()
    {
        var messages = TmAiConversationTests.Conversation(1);
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, messages));

        await cut.InvokeAsync(() => cut.Instance.AnnounceAsync("pre"));
        cut.FindAll(HostNoteTestId).Should().ContainSingle(
            "the idle note is in the transcript before the window opens");

        cut.Render(parameters => parameters.Add(component => component.Generating, true));

        cut.FindAll(HostNoteTestId).Should().BeEmpty(
            "the edge clears the host channel in the same batch — a note spoken just before the window " +
            "opens never reaches a reader");
    }

    // ── The ordering pitfall inside an event handler ─────────────────────────────────────────────

    /// <summary>
    /// The pitfall as it happens in a REAL host: a button handler that sets <c>Generating</c> = true and
    /// immediately announces. <c>StateHasChanged</c> only QUEUES the host render — it does not run it —
    /// and <see cref="TmAiConversation.AnnounceAsync"/> reaches the conversation through
    /// <see cref="ComponentBase.InvokeAsync(Action)"/>, which executes synchronously on the renderer's
    /// dispatcher. The note is therefore added BEFORE the queued render lands, and the false→true edge
    /// inside that render wipes it: the note never reaches a reader. This is the snippet the docs used to
    /// recommend, pinned here as a trap — the behavior matches a running app, where the event dispatch
    /// holds the render batch open until the handler's synchronous segment completes.
    /// </summary>
    [Fact]
    public void OrderingPitfall_StateHasChangedAlone_TheEdgeClearsTheNoteInTheSameBatch()
    {
        var cut = Render<OrderingPitfallHost>();

        cut.Find("[data-testid='pitfall-send']").Click();

        cut.FindAll(HostNoteTestId).Should().BeEmpty(
            "StateHasChanged only queues the render — AnnounceAsync runs synchronously before it lands, " +
            "so the note is added before the edge and the edge clears it in the very same batch");
    }

    /// <summary>
    /// The alternative the docs mention next to <c>await Task.Yield()</c>: speaking from
    /// <c>OnAfterRenderAsync</c> after the <c>Generating</c> = true render has landed. This path is
    /// verifiable in bUnit — the render (and the edge) is committed before <c>OnAfterRenderAsync</c>
    /// runs, so the note is spoken inside the open window and survives. (The yield variant itself is NOT
    /// pinned here: bUnit's event dispatch does not deterministically pump the render queue between the
    /// yielded segments of a handler — probing it left the note both cleared and surviving — while a
    /// running app always lets the render land: the real <c>Renderer.DispatchEventAsync</c> holds the
    /// render batch open for the handler's synchronous segment and runs <c>ProcessPendingRender</c> when
    /// the handler yields, before the continuation speaks.)
    /// </summary>
    [Fact]
    public void OrderingPitfall_AnnouncedFromOnAfterRenderAsync_TheNoteSurvives()
    {
        var cut = Render<OrderingPitfallHost>();
        cut.Instance.UseAfterRender = true;

        cut.Find("[data-testid='pitfall-send']").Click();

        cut.FindAll(HostNoteTestId).Should().ContainSingle()
            .Which.TextContent.Should().Be("Sending…",
                "OnAfterRenderAsync runs after the Generating = true render, so the note is spoken inside the open window");
    }

    /// <summary>
    /// The host of the ordering pitfall: a button whose handler sets <c>Generating</c> = true and speaks a
    /// note through <see cref="TmAiConversation.AnnounceAsync"/> — either straight after
    /// <c>StateHasChanged</c> (the trap the docs used to recommend) or from <c>OnAfterRenderAsync</c>
    /// (the alternative that lets the render land first, verifiable in bUnit).
    /// </summary>
    private sealed class OrderingPitfallHost : ComponentBase
    {
        private readonly List<AiMessage> _messages = TmAiConversationTests.Conversation(1);
        private TmAiConversation? _conversation;
        private bool _generating;
        private bool _announceAfterRender;

        /// <summary>Whether the note is spoken from OnAfterRenderAsync instead of the event handler.</summary>
        public bool UseAfterRender { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<TmAiConversation>(0);
            builder.AddAttribute(1, nameof(TmAiConversation.Messages), _messages);
            builder.AddAttribute(2, nameof(TmAiConversation.Generating), _generating);
            builder.AddComponentReferenceCapture(3, component => _conversation = (TmAiConversation)component);
            builder.CloseComponent();

            builder.OpenElement(4, "button");
            builder.AddAttribute(5, "data-testid", "pitfall-send");
            builder.AddAttribute(6, "onclick", EventCallback.Factory.Create(this, SendAsync));
            builder.CloseElement();
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (_announceAfterRender)
            {
                _announceAfterRender = false;
                await _conversation!.AnnounceAsync("Sending…");
            }
        }

        private Task SendAsync()
        {
            _generating = true;
            StateHasChanged();
            if (UseAfterRender)
            {
                _announceAfterRender = true;
                return Task.CompletedTask;
            }

            return _conversation!.AnnounceAsync("Sending…");
        }
    }

    /// <summary>
    /// The stream keeps its pre-existing bookkeeping: <see cref="TmAiConversation.StreamAsync"/> cleared
    /// the host notes at start before this phase and still does — the window edge joins the same rule,
    /// it does not replace it.
    /// </summary>
    [Fact]
    public async Task HostAnnouncement_WithoutAWindow_IsClearedByTheNextStreamStart()
    {
        var clock = new FakeTimeProvider(Start);
        Services.AddSingleton<TimeProvider>(clock);
        SetupInterop();

        var messages = TmAiConversationTests.Conversation(1);
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, messages));

        await cut.InvokeAsync(() => cut.Instance.AnnounceAsync("Idle note"));
        cut.FindAll(HostNoteTestId).Should().ContainSingle();

        var streamed = new AiMessage { Id = "answer-1", Role = AiMessageRole.Assistant, Status = AiMessageStatus.Pending };
        messages.Add(streamed);
        await cut.InvokeAsync(() => cut.Instance.StreamAsync(
            streamed,
            FinishedStream(new AiTextDelta { Text = "done" }, new AiCompletedDelta())));

        cut.FindAll(HostNoteTestId).Should().BeEmpty(
            "the stream re-owned the live region at start, exactly as before this phase");
        cut.FindAll(CompletionNoteTestId).Should().ContainSingle("the streamed completion announced its answer");
    }
}
