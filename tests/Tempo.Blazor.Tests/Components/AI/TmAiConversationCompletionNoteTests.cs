using System.Runtime.CompilerServices;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// bUnit contract of the SINGLE completion note of <see cref="TmAiConversation"/>: the transcript holds at
/// most ONE screen-reader-only completion note (<c>p.tm-sr-only</c>) no matter which half of the package
/// drove the generation — a host-driven window (<see cref="TmAiConversation.Generating"/>) or a stream.
/// Before this contract, only <see cref="TmAiConversation.StreamAsync"/> cleared the previous note (at
/// stream start); a host that drives its own window accumulated one identical note per turn, and a screen
/// reader browsing the transcript after N turns read "Answer complete" N times (EmusCz measured it after
/// the 2.13.0 upgrade). A new completion therefore REPLACES the previous note (a new keyed node, so the
/// repeat is still announced), opening a generation window clears the stale note of the previous turn, and
/// a host's own <see cref="TmAiConversation.AnnounceAsync"/> announcements are never touched by the
/// replacement.
/// </summary>
public sealed class TmAiConversationCompletionNoteTests : LocalizationTestBase
{
    private const string NoteTestId = "[data-testid='ai-conversation-announcement'][data-kind='completion']";
    private const string AllAnnouncementsTestId = "[data-testid='ai-conversation-announcement']";

    private static readonly DateTimeOffset Start = new(2026, 11, 26, 9, 0, 0, TimeSpan.Zero);

    private BunitJSModuleInterop SetupInterop()
    {
        var module = JSInterop.SetupModule(TmAiConversationTests.InteropModulePath);
        module.SetupVoid("attach", _ => true).SetVoidResult();
        module.SetupVoid("detach", _ => true).SetVoidResult();
        module.Setup<bool>("isNearBottom", _ => true).SetResult(true);
        module.SetupVoid("scrollToBottom", _ => true).SetVoidResult();
        return module;
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

    private static async IAsyncEnumerable<AiStreamDelta> FailingStream(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new AiTextDelta { Text = "partial" };
        await Task.Yield();
        throw new InvalidOperationException("transport exploded");
    }

    // ── The accumulation the phase removes ──────────────────────────────────────────────────────

    [Fact]
    public void HostDrivenCompletion_ThreeTurns_LeavesExactlyOneNoteInTheTranscript()
    {
        var messages = TmAiConversationTests.Conversation(1);
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, messages));

        for (var turn = 1; turn <= 3; turn++)
        {
            cut.Render(parameters => parameters.Add(component => component.Generating, true));
            AppendCompletedTurn(messages, $"answer-{turn}");
            cut.Render(parameters => parameters.Add(component => component.Generating, false));

            cut.FindAll(NoteTestId).Should().HaveCount(1,
                $"after turn {turn} the transcript holds exactly one completion note — a screen reader browsing it reads the completion once, never per past turn");
        }
    }

    [Fact]
    public void HostDrivenCompletion_ANewTurn_ReplacesThePreviousNote()
    {
        var messages = TmAiConversationTests.Conversation(1);
        var cut = Render<TmAiConversation>(parameters => parameters
            .Add(component => component.Messages, messages)
            .Add(component => component.CompletionAnnouncement, message => $"done: {message.Id}"));

        cut.Render(parameters => parameters.Add(component => component.Generating, true));
        AppendCompletedTurn(messages, "answer-1");
        cut.Render(parameters => parameters.Add(component => component.Generating, false));
        cut.FindAll(NoteTestId).Should().ContainSingle().Which.TextContent.Should().Be("done: answer-1");

        cut.Render(parameters => parameters.Add(component => component.Generating, true));
        AppendCompletedTurn(messages, "answer-2");
        cut.Render(parameters => parameters.Add(component => component.Generating, false));

        var note = cut.FindAll(NoteTestId).Should().ContainSingle(
            "the new completion replaces the previous note instead of accumulating next to it").Which;
        note.TextContent.Should().Be("done: answer-2");
        note.Closest("[role='log']").Should().NotBeNull("the replacement stays inside the transcript live region");
    }

    /// <summary>
    /// Opening the next generation window clears the stale note of the previous turn: while the new answer
    /// is still being produced there is no completed answer to announce, so the transcript carries none —
    /// the same bookkeeping <see cref="TmAiConversation.StreamAsync"/> already does at stream start.
    /// </summary>
    [Fact]
    public void HostDrivenWindow_OpeningTheNextTurn_ClearsTheStaleNote()
    {
        var messages = TmAiConversationTests.Conversation(1);
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, messages));

        cut.Render(parameters => parameters.Add(component => component.Generating, true));
        AppendCompletedTurn(messages, "answer-1");
        cut.Render(parameters => parameters.Add(component => component.Generating, false));
        cut.FindAll(NoteTestId).Should().ContainSingle("the first completion announced its answer");

        cut.Render(parameters => parameters.Add(component => component.Generating, true));

        cut.FindAll(NoteTestId).Should().BeEmpty(
            "while the new turn runs there is no completed answer to announce — the previous note is stale");

        AppendCompletedTurn(messages, "answer-2");
        cut.Render(parameters => parameters.Add(component => component.Generating, false));

        var note = cut.FindAll(NoteTestId).Should().ContainSingle().Which;
        note.TextContent.Should().Be("Answer complete");
    }

    // ── The note is a claim about the LAST turn ─────────────────────────────────────────────────

    /// <summary>
    /// The completion note names the announced turn (the slot remembers its <see cref="AiMessage.Id"/>):
    /// a host that CLEARS the conversation without opening a new window must not leave the stale note
    /// behind — "Answer complete" would claim a completion the transcript no longer ends with.
    /// </summary>
    [Fact]
    public void CompletedTurn_ThenConversationCleared_RemovesTheNote()
    {
        var messages = TmAiConversationTests.Conversation(1);
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, messages));

        cut.Render(parameters => parameters.Add(component => component.Generating, true));
        AppendCompletedTurn(messages, "answer-1");
        cut.Render(parameters => parameters.Add(component => component.Generating, false));
        cut.FindAll(NoteTestId).Should().ContainSingle("the first completion announced its answer");

        messages.Clear();
        cut.Render(parameters => parameters.Add(component => component.Messages, messages));

        cut.FindAll(NoteTestId).Should().BeEmpty(
            "the announced answer is gone from the transcript — the note about it is stale");
    }

    /// <summary>
    /// The same staleness guard without a window: the host appends a fresh user turn (the reader asked
    /// the next question) and has not opened the next generation yet — the announced answer is no longer
    /// the newest turn, so the note about it must go. A mutant without the <c>OnParametersSet</c>
    /// id-check keeps the note and is RED here.
    /// </summary>
    [Fact]
    public void CompletedTurn_ThenNewUserTurnAppended_RemovesTheStaleNote()
    {
        var messages = TmAiConversationTests.Conversation(1);
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, messages));

        cut.Render(parameters => parameters.Add(component => component.Generating, true));
        AppendCompletedTurn(messages, "answer-1");
        cut.Render(parameters => parameters.Add(component => component.Generating, false));
        cut.FindAll(NoteTestId).Should().ContainSingle("the first completion announced its answer");

        messages.Add(new AiMessage(AiMessageRole.User, "the next question") { Status = AiMessageStatus.Completed });
        cut.Render(parameters => parameters.Add(component => component.Messages, messages));

        cut.FindAll(NoteTestId).Should().BeEmpty(
            "the announced answer stopped being the last turn — the note is stale until the new answer completes");
    }

    /// <summary>
    /// A STOPPED host-driven turn announces nothing — and leaves nothing: opening its window already
    /// cleared the previous turn's stale note, and a stopped turn never writes a new one, so the
    /// transcript ends the window with NO completion note at all.
    /// </summary>
    [Fact]
    public void HostDrivenWindow_StoppedTurn_LeavesNoNote()
    {
        var messages = TmAiConversationTests.Conversation(1);
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, messages));

        cut.Render(parameters => parameters.Add(component => component.Generating, true));
        AppendCompletedTurn(messages, "answer-1");
        cut.Render(parameters => parameters.Add(component => component.Generating, false));
        cut.FindAll(NoteTestId).Should().ContainSingle("the first completion announced its answer");

        cut.Render(parameters => parameters.Add(component => component.Generating, true));
        messages.Add(new AiMessage(AiMessageRole.User, "question for answer-2") { Status = AiMessageStatus.Completed });
        messages.Add(new AiMessage { Id = "answer-2", Role = AiMessageRole.Assistant, Status = AiMessageStatus.Stopped });
        cut.Render(parameters => parameters.Add(component => component.Generating, false));

        cut.FindAll(AllAnnouncementsTestId).Should().BeEmpty(
            "a stopped turn is not a completion — the stale note of the previous turn is gone and nothing replaces it");
    }

    // ── The two drivers share the one slot ──────────────────────────────────────────────────────

    [Fact]
    public async Task StreamedCompletion_AfterAHostDrivenOne_KeepsExactlyOneNote()
    {
        var clock = new FakeTimeProvider(Start);
        Services.AddSingleton<TimeProvider>(clock);
        SetupInterop();

        var messages = TmAiConversationTests.Conversation(1);
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, messages));

        cut.Render(parameters => parameters.Add(component => component.Generating, true));
        AppendCompletedTurn(messages, "answer-1");
        cut.Render(parameters => parameters.Add(component => component.Generating, false));
        cut.FindAll(NoteTestId).Should().ContainSingle();

        var streamed = new AiMessage { Id = "answer-2", Role = AiMessageRole.Assistant, Status = AiMessageStatus.Pending };
        messages.Add(streamed);
        await cut.InvokeAsync(() => cut.Instance.StreamAsync(
            streamed,
            FinishedStream(new AiTextDelta { Text = "done" }, new AiCompletedDelta())));

        streamed.Status.Should().Be(AiMessageStatus.Completed);
        cut.FindAll(NoteTestId).Should().ContainSingle(
            "the streamed completion replaces the host-driven note — the slot is shared by both drivers");

        // A re-set of the parameters runs OnParametersSet: the note stores the announced turn's id, and
        // the streamed message IS messages[^1], so the staleness guard must leave the note in place.
        cut.Render(parameters => parameters.Add(component => component.Messages, messages));
        cut.FindAll(NoteTestId).Should().ContainSingle(
            "the streamed answer is still the last turn — the id guard keeps its note");
    }

    /// <summary>
    /// A stream that FAILS announces nothing — and the stale note of the previous turn is gone: the stream
    /// cleared it at start, exactly like it always cleared the announcement list. A note left behind would
    /// claim a completion the transcript no longer ends with.
    /// </summary>
    [Fact]
    public async Task FailedStream_ClearsTheStaleNoteWithoutAddingANewOne()
    {
        var clock = new FakeTimeProvider(Start);
        Services.AddSingleton<TimeProvider>(clock);
        SetupInterop();

        var messages = TmAiConversationTests.Conversation(1);
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, messages));

        cut.Render(parameters => parameters.Add(component => component.Generating, true));
        AppendCompletedTurn(messages, "answer-1");
        cut.Render(parameters => parameters.Add(component => component.Generating, false));
        cut.FindAll(NoteTestId).Should().ContainSingle();

        var streamed = new AiMessage { Id = "answer-2", Role = AiMessageRole.Assistant, Status = AiMessageStatus.Pending };
        messages.Add(streamed);
        var act = () => cut.InvokeAsync(() => cut.Instance.StreamAsync(streamed, FailingStream()));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("transport exploded");
        streamed.Status.Should().Be(AiMessageStatus.Failed);
        cut.FindAll(NoteTestId).Should().BeEmpty(
            "a failed stream announces nothing, and the stale note of the previous turn was cleared at stream start");
    }

    // ── The host's own announcements are not collateral ─────────────────────────────────────────

    /// <summary>
    /// The completion-note replacement never touches the host channel: a note spoken INSIDE the open
    /// window is still there after the completed edge, rendered before the completion note. Opening the
    /// NEXT window is a different edge — that one clears the previous window's host note (the window
    /// re-owns the host channel, the same bookkeeping <see cref="TmAiConversation.StreamAsync"/> does at
    /// stream start; the full host-channel contract lives in
    /// <see cref="TmAiConversationHostAnnouncementTests"/>).
    /// </summary>
    [Fact]
    public async Task HostAnnouncements_SurviveTheCompletionNoteReplacement()
    {
        var messages = TmAiConversationTests.Conversation(1);
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, messages));

        cut.Render(parameters => parameters.Add(component => component.Generating, true));
        await cut.InvokeAsync(() => cut.Instance.AnnounceAsync("Host note"));
        AppendCompletedTurn(messages, "answer-1");
        cut.Render(parameters => parameters.Add(component => component.Generating, false));

        cut.FindAll(AllAnnouncementsTestId).Select(node => node.TextContent).Should().Equal("Host note", "Answer complete");

        cut.Render(parameters => parameters.Add(component => component.Generating, true));
        await cut.InvokeAsync(() => cut.Instance.AnnounceAsync("Host note 2"));
        AppendCompletedTurn(messages, "answer-2");
        cut.Render(parameters => parameters.Add(component => component.Generating, false));

        cut.FindAll(AllAnnouncementsTestId).Select(node => node.TextContent).Should().Equal(
            new[] { "Host note 2", "Answer complete" },
            "the current window's host note stays untouched by the completion replacement — only the window edge clears the previous one");
    }

    /// <summary>
    /// The replacement is a NEW keyed node even when the text repeats (the completion note is keyed by the
    /// announcement sequence): a live region ignores an unchanged text node, so re-using the node would
    /// silently swallow the second completion.
    /// </summary>
    [Fact]
    public void HostDrivenCompletion_SameTextTwice_StillAnnouncesTheRepeat()
    {
        var messages = TmAiConversationTests.Conversation(1);
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, messages));

        cut.Render(parameters => parameters.Add(component => component.Generating, true));
        AppendCompletedTurn(messages, "answer-1");
        cut.Render(parameters => parameters.Add(component => component.Generating, false));
        var first = cut.Find(NoteTestId);

        cut.Render(parameters => parameters.Add(component => component.Generating, true));
        AppendCompletedTurn(messages, "answer-2");
        cut.Render(parameters => parameters.Add(component => component.Generating, false));
        var second = cut.Find(NoteTestId);

        second.TextContent.Should().Be(first.TextContent, "both turns completed with the same default wording");
        second.Should().NotBeSameAs(first,
            "the note is a NEW keyed node — an unchanged text node in a live region is not re-announced");
    }
}
