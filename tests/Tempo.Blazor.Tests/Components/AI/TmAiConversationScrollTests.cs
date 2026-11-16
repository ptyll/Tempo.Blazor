using Bunit;
using FluentAssertions;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// bUnit contract of the transcript's SCROLL tracking: the component imports
/// <c>_content/Tempo.Blazor.AI/js/ai-conversation.js</c> and asks it whether the reader is at the bottom
/// before it follows new content. A conversation that scrolls itself while the reader is reading back is
/// worse than one that never scrolls, so a detached reader gets a "New messages" affordance instead — and
/// gets scrolled only when they press it.
/// </summary>
/// <remarks>
/// The module is mocked (<see cref="BunitJSModuleInterop"/>), so the assertions are about the CALLS the
/// component makes and the state it derives from the answer — the geometry itself is the module's job.
/// </remarks>
public sealed class TmAiConversationScrollTests : LocalizationTestBase
{
    private const string NewMessages = "[data-testid='ai-conversation-new-messages']";

    private BunitJSModuleInterop SetupInterop(bool nearBottom)
    {
        var module = JSInterop.SetupModule(TmAiConversationTests.InteropModulePath);
        module.SetupVoid("attach", _ => true).SetVoidResult();
        module.SetupVoid("detach", _ => true).SetVoidResult();
        module.Setup<bool>("isNearBottom", _ => true).SetResult(nearBottom);
        module.SetupVoid("scrollToBottom", _ => true).SetVoidResult();
        module.Setup<bool>("scrollToMessage", _ => true).SetResult(true);
        return module;
    }

    private static List<AiMessage> Conversation(int count)
        => TmAiConversationTests.Conversation(count);

    // ── Pinned to the bottom ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void FirstRender_AttachesTheModule_AndScrollsToTheLatestTurn()
    {
        var module = SetupInterop(nearBottom: true);

        Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, Conversation(3)));

        TmAiConversationTests.Count(module, "attach").Should().Be(1);
        TmAiConversationTests.Count(module, "scrollToBottom").Should().Be(1);
    }

    [Fact]
    public void NewTurn_WhileTheReaderIsAtTheBottom_IsFollowed()
    {
        var module = SetupInterop(nearBottom: true);
        var messages = Conversation(2);
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, messages));
        var before = TmAiConversationTests.Count(module, "scrollToBottom");

        messages.Add(TmAiConversationTests.Message("m-2"));
        cut.Render(parameters => parameters.Add(component => component.Messages, messages));

        TmAiConversationTests.Count(module, "scrollToBottom").Should().Be(before + 1);
        cut.FindAll(NewMessages).Should().BeEmpty();
    }

    [Fact]
    public void TurningAPageOfTheSameList_DoesNotProbeTheGeometryAgain()
    {
        var module = SetupInterop(nearBottom: true);
        var messages = Conversation(2);
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, messages));
        var before = TmAiConversationTests.Count(module, "isNearBottom");

        cut.Render(parameters => parameters.Add(component => component.Messages, messages));

        TmAiConversationTests.Count(module, "isNearBottom").Should().Be(before,
            "a render that did not change the transcript must not touch the browser");
    }

    // ── Detached from the bottom ────────────────────────────────────────────────────────────────

    [Fact]
    public void NewTurn_WhileTheReaderIsScrolledUp_DoesNotScroll_AndOffersTheJumpButton()
    {
        var module = SetupInterop(nearBottom: false);
        var messages = Conversation(2);
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, messages));
        var before = TmAiConversationTests.Count(module, "scrollToBottom");

        messages.Add(TmAiConversationTests.Message("m-2"));
        cut.Render(parameters => parameters.Add(component => component.Messages, messages));

        TmAiConversationTests.Count(module, "scrollToBottom").Should().Be(before,
            "moving the reader's viewport while they read back is an interruption");
        cut.FindAll(NewMessages).Should().ContainSingle().Which.TextContent.Should().Be("New messages");
    }

    [Fact]
    public void JumpToTheLatestTurn_ScrollsAndDismissesTheAffordance()
    {
        var module = SetupInterop(nearBottom: false);
        var messages = Conversation(2);
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, messages));

        messages.Add(TmAiConversationTests.Message("m-2"));
        cut.Render(parameters => parameters.Add(component => component.Messages, messages));
        var before = TmAiConversationTests.Count(module, "scrollToBottom");

        cut.Find(NewMessages).Click();

        TmAiConversationTests.Count(module, "scrollToBottom").Should().Be(before + 1);
        cut.FindAll(NewMessages).Should().BeEmpty();
    }

    /// <summary>
    /// The module pushes scroll changes back, so a reader who scrolls to the bottom themselves clears the
    /// affordance without another render being requested from above.
    /// </summary>
    [Fact]
    public async Task ScrollReport_AtTheBottom_DismissesTheAffordance()
    {
        SetupInterop(nearBottom: false);
        var messages = Conversation(2);
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, messages));

        messages.Add(TmAiConversationTests.Message("m-2"));
        cut.Render(parameters => parameters.Add(component => component.Messages, messages));
        cut.FindAll(NewMessages).Should().ContainSingle();

        await cut.InvokeAsync(() => cut.Instance.OnScrollStateAsync(true));

        cut.FindAll(NewMessages).Should().BeEmpty();
    }

    /// <summary>A scroll report away from the bottom marks the reader detached, so the next turn is not followed.</summary>
    [Fact]
    public async Task ScrollReport_AwayFromTheBottom_StopsFollowingNewTurns()
    {
        var module = SetupInterop(nearBottom: true);
        var messages = Conversation(2);
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, messages));

        await cut.InvokeAsync(() => cut.Instance.OnScrollStateAsync(false));
        var before = TmAiConversationTests.Count(module, "scrollToBottom");

        messages.Add(TmAiConversationTests.Message("m-2"));
        cut.Render(parameters => parameters.Add(component => component.Messages, messages));

        TmAiConversationTests.Count(module, "scrollToBottom").Should().Be(before);
        cut.FindAll(NewMessages).Should().ContainSingle();
    }

    /// <summary>
    /// Once the module has reported a position, that report is the state of record: a new turn is followed
    /// without a geometry probe. Probing on every render is a Blazor round-trip per parent render — the
    /// Server cost the render throttle exists to reduce.
    /// </summary>
    [Fact]
    public async Task AfterAReport_NewTurnsDoNotProbeTheGeometryAgain()
    {
        var module = SetupInterop(nearBottom: true);
        var messages = Conversation(2);
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, messages));

        await cut.InvokeAsync(() => cut.Instance.OnScrollStateAsync(true));
        var probes = TmAiConversationTests.Count(module, "isNearBottom");
        var scrolls = TmAiConversationTests.Count(module, "scrollToBottom");

        messages.Add(TmAiConversationTests.Message("m-2"));
        cut.Render(parameters => parameters.Add(component => component.Messages, messages));

        TmAiConversationTests.Count(module, "isNearBottom").Should().Be(probes);
        TmAiConversationTests.Count(module, "scrollToBottom").Should().Be(scrolls + 1,
            "the reported position still follows the new turn");
    }

    /// <summary>
    /// Content can grow while the reader is detached (an image, an expanded reasoning panel, a streamed
    /// token): the resize report marks it as new while a generation is running. A resize that is not a
    /// streamed answer is not "new messages".
    /// </summary>
    [Fact]
    public async Task ContentResize_WhileGenerating_OffersTheJumpButton()
    {
        SetupInterop(nearBottom: false);
        var message = new AiMessage { Id = "answer-1", Role = AiMessageRole.Assistant, Status = AiMessageStatus.Streaming };
        message.Parts.Add(new AiTextPart("streaming"));
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, new List<AiMessage> { message }));

        await cut.InvokeAsync(() => cut.Instance.OnContentResizedAsync(false));

        cut.FindAll(NewMessages).Should().BeEmpty("the component only treats a resize as new content while it is generating");

        var parked = new TaskCompletionSource();
        var streaming = cut.InvokeAsync(() => cut.Instance.StreamAsync(message, Parking(parked)));
        await parked.Task;

        await cut.InvokeAsync(() => cut.Instance.OnContentResizedAsync(false));

        cut.FindAll(NewMessages).Should().ContainSingle();

        await cut.InvokeAsync(() => cut.Instance.StopAsync());
        await streaming;
    }

    /// <summary>A resize report while the reader IS at the bottom keeps the transcript pinned.</summary>
    [Fact]
    public async Task ContentResize_AtTheBottom_KeepsTheTranscriptPinned()
    {
        var module = SetupInterop(nearBottom: true);
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, Conversation(2)));
        var before = TmAiConversationTests.Count(module, "scrollToBottom");

        await cut.InvokeAsync(() => cut.Instance.OnContentResizedAsync(true));

        TmAiConversationTests.Count(module, "scrollToBottom").Should().Be(before + 1);
        cut.FindAll(NewMessages).Should().BeEmpty();
    }

    [Fact]
    public void AutoScrollDisabled_ImportsNoModule()
    {
        var module = SetupInterop(nearBottom: true);

        var cut = Render<TmAiConversation>(parameters => parameters
            .Add(component => component.Messages, Conversation(2))
            .Add(component => component.AutoScroll, false));

        module.Invocations.Should().BeEmpty("a host that owns the scrolling surface gets no scroll tracking at all");
        var messages = Conversation(3);
        cut.Render(parameters => parameters
            .Add(component => component.Messages, messages)
            .Add(component => component.AutoScroll, false));
        module.Invocations.Should().BeEmpty();
        cut.FindAll(NewMessages).Should().BeEmpty();
    }

    [Fact]
    public async Task Dispose_DetachesTheModule()
    {
        var module = SetupInterop(nearBottom: true);
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, Conversation(1)));

        await cut.InvokeAsync(() => cut.Instance.DisposeAsync().AsTask());

        TmAiConversationTests.Count(module, "detach").Should().Be(1);
    }

    // ── Host-initiated scrolling ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// A host that reacts to its own state (its "jump to latest" toolbar item, its own busy handling) asks
    /// the component instead of poking the DOM: the public <see cref="TmAiConversation.ScrollToBottomAsync"/>
    /// runs the same guarded, reduced-motion-aware follow the built-in pill uses.
    /// </summary>
    [Fact]
    public async Task ScrollToBottomAsync_PublicMethod_FollowsTheTranscript()
    {
        var module = SetupInterop(nearBottom: true);
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, Conversation(2)));
        var before = TmAiConversationTests.Count(module, "scrollToBottom");

        await cut.InvokeAsync(() => cut.Instance.ScrollToBottomAsync());

        TmAiConversationTests.Count(module, "scrollToBottom").Should().Be(before + 1);
    }

    /// <summary>A caller without the module (AutoScroll off, static rendering) gets a quiet no-op.</summary>
    [Fact]
    public async Task ScrollToBottomAsync_WithoutTheModule_IsANoOp()
    {
        var module = SetupInterop(nearBottom: true);
        var cut = Render<TmAiConversation>(parameters => parameters
            .Add(component => component.Messages, Conversation(2))
            .Add(component => component.AutoScroll, false));
        var before = TmAiConversationTests.Count(module, "scrollToBottom");

        await cut.InvokeAsync(() => cut.Instance.ScrollToBottomAsync());

        TmAiConversationTests.Count(module, "scrollToBottom").Should().Be(before);
    }

    /// <summary>
    /// The public <see cref="TmAiConversation.ScrollToBottomAsync"/> is the same jump the "New messages"
    /// pill performs, not just another scroll probe: it dismisses the pill AND re-arms the follow, so the
    /// next appended turn is followed again instead of pill-ed. (The pre-fix version called the bare
    /// scrollToBottom interop, which left the pill on screen and the follow detached.)
    /// </summary>
    [Fact]
    public async Task ScrollToBottomAsync_DismissesThePill_AndReArmsTheFollow()
    {
        var module = SetupInterop(nearBottom: false);
        var messages = Conversation(2);
        var cut = Render<TmAiConversation>(parameters => parameters
            .Add(component => component.Messages, messages)
            .Add(component => component.Generating, true));

        await cut.InvokeAsync(() => cut.Instance.OnScrollStateAsync(false));
        messages.Add(TmAiConversationTests.Message("m-2"));
        cut.Render(parameters => parameters.Add(component => component.Messages, messages));
        cut.FindAll(NewMessages).Should().ContainSingle("the reader is detached while the generation runs");
        var before = TmAiConversationTests.Count(module, "scrollToBottom");

        await cut.InvokeAsync(() => cut.Instance.ScrollToBottomAsync());

        cut.FindAll(NewMessages).Should().BeEmpty("the public jump dismisses the pill like the built-in one");
        TmAiConversationTests.Count(module, "scrollToBottom").Should().Be(before + 1);

        messages.Add(TmAiConversationTests.Message("m-3"));
        cut.Render(parameters => parameters.Add(component => component.Messages, messages));

        TmAiConversationTests.Count(module, "scrollToBottom").Should().Be(before + 2,
            "the follow is re-armed: the next turn is followed again");
        cut.FindAll(NewMessages).Should().BeEmpty();
    }

    /// <summary>
    /// A host can name a turn (its "go to the answer" link, an error anchor): the public
    /// <see cref="TmAiConversation.ScrollToMessageAsync"/> asks the module to bring that row into view
    /// through the same programmatic-scroll contract (the module owns the geometry and the smooth/reduced
    /// motion decision) and answers whether the named turn actually moved.
    /// </summary>
    [Fact]
    public async Task ScrollToMessageAsync_AsksTheModuleToBringTheTurnIntoView_AndAnswersTrue()
    {
        var module = SetupInterop(nearBottom: true);
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, Conversation(3)));

        var moved = await cut.InvokeAsync(() => cut.Instance.ScrollToMessageAsync("m-1"));

        moved.Should().BeTrue("the module reported a matching turn");
        var calls = module.Invocations.Where(invocation => invocation.Identifier == "scrollToMessage").ToList();
        calls.Should().ContainSingle();
        calls[0].Arguments.Should().HaveCount(3);
        calls[0].Arguments[1].Should().Be("m-1");
    }

    /// <summary>The module answers false for a turn it cannot find — the host gets the honest false.</summary>
    [Fact]
    public async Task ScrollToMessageAsync_UnknownTurn_AnswersFalse()
    {
        var module = SetupInterop(nearBottom: true);
        module.Setup<bool>("scrollToMessage", _ => true).SetResult(false);
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, Conversation(2)));

        var moved = await cut.InvokeAsync(() => cut.Instance.ScrollToMessageAsync("m-missing"));

        moved.Should().BeFalse("an id the transcript cannot resolve moved nothing");
        TmAiConversationTests.Count(module, "scrollToMessage").Should().Be(1, "the usable id still reached the browser");
    }

    /// <summary>An interop failure (a gone circuit, a module that threw) is a quiet false.</summary>
    [Fact]
    public async Task ScrollToMessageAsync_InteropFailure_AnswersFalse()
    {
        var module = SetupInterop(nearBottom: true);
        module.Setup<bool>("scrollToMessage", _ => true)
            .SetException(new Microsoft.JSInterop.JSException("circuit gone"));
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, Conversation(2)));

        var moved = await cut.InvokeAsync(() => cut.Instance.ScrollToMessageAsync("m-1"));

        moved.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ScrollToMessageAsync_WithoutAUsableId_IsANoOpThatAnswersFalse(string? messageId)
    {
        var module = SetupInterop(nearBottom: true);
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, Conversation(2)));

        var moved = await cut.InvokeAsync(() => cut.Instance.ScrollToMessageAsync(messageId!));

        moved.Should().BeFalse();
        module.Invocations.Where(invocation => invocation.Identifier == "scrollToMessage")
            .Should().BeEmpty("an unusable id must not reach the browser");
    }

    [Fact]
    public async Task ScrollToMessageAsync_WithoutTheModule_IsANoOpThatAnswersFalse()
    {
        var module = SetupInterop(nearBottom: true);
        var cut = Render<TmAiConversation>(parameters => parameters
            .Add(component => component.Messages, Conversation(2))
            .Add(component => component.AutoScroll, false));

        var moved = await cut.InvokeAsync(() => cut.Instance.ScrollToMessageAsync("m-1"));

        moved.Should().BeFalse();
        module.Invocations.Where(invocation => invocation.Identifier == "scrollToMessage").Should().BeEmpty();
    }

    /// <summary>
    /// Scrolling to a turn that is NOT the newest detaches the follow: the next appended turn is news for
    /// the reader who was just taken somewhere else, so it is not followed and the "New messages" pill
    /// appears instead.
    /// </summary>
    [Fact]
    public async Task ScrollToMessageAsync_TargetIsNotTheLastTurn_DetachesAndOffersThePill()
    {
        var module = SetupInterop(nearBottom: true);
        var messages = Conversation(3);
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, messages));

        var moved = await cut.InvokeAsync(() => cut.Instance.ScrollToMessageAsync("m-1"));
        moved.Should().BeTrue();

        messages.Add(TmAiConversationTests.Message("m-3"));
        cut.Render(parameters => parameters.Add(component => component.Messages, messages));

        TmAiConversationTests.Count(module, "scrollToBottom").Should().Be(1,
            "the only scroll so far is the first-render follow — the turn appended after a targeted scroll is NOT followed");
        cut.FindAll(NewMessages).Should().ContainSingle();
    }

    /// <summary>Scrolling to the NEWEST turn keeps the follow semantics intact.</summary>
    [Fact]
    public async Task ScrollToMessageAsync_TargetIsTheLastTurn_KeepsFollowing()
    {
        var module = SetupInterop(nearBottom: true);
        var messages = Conversation(3);
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, messages));

        var moved = await cut.InvokeAsync(() => cut.Instance.ScrollToMessageAsync("m-2"));
        moved.Should().BeTrue();

        messages.Add(TmAiConversationTests.Message("m-3"));
        cut.Render(parameters => parameters.Add(component => component.Messages, messages));

        TmAiConversationTests.Count(module, "scrollToBottom").Should().Be(2, "the follow survives a jump to the newest turn");
        cut.FindAll(NewMessages).Should().BeEmpty();
    }

    private static async IAsyncEnumerable<AiStreamDelta> Parking(
        TaskCompletionSource parked,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new AiTextDelta { Text = "streamed" };
        parked.TrySetResult();
        await new TaskCompletionSource().Task.WaitAsync(cancellationToken);
    }
}
