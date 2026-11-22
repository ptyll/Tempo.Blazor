using System.Runtime.CompilerServices;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// bUnit contract of the transcript's RENDER WINDOW: a conversation with hundreds of turns keeps the DOM at
/// the size of the window, follows the tail while the reader is at the bottom, and lets the reader pull the
/// earlier turns in one step at a time.
/// </summary>
/// <remarks>
/// The window is a plain slice of <see cref="TmAiConversation.Messages"/> — deliberately not
/// <c>&lt;Virtualize&gt;</c>, which would unmount rows the reader is looking at and break the message
/// anchors and focus. The default is <see langword="null"/>: no window, every turn rendered, byte-identical
/// to the pre-window behaviour.
/// </remarks>
public sealed class TmAiConversationWindowTests : LocalizationTestBase
{
    private const string LoadOlder = "[data-testid='ai-conversation-load-older']";
    private const string NewMessages = "[data-testid='ai-conversation-new-messages']";
    private const string FocusInvocation = "Blazor._internal.domWrapper.focus";

    private static List<AiMessage> Conversation(int count) => TmAiConversationTests.Conversation(count);

    private static string[] RenderedIds(IRenderedComponent<TmAiConversation> cut)
        => cut.FindAll(".tm-ai-conversation__message")
            .Select(row => row.GetAttribute("data-message-id")!)
            .ToArray();

    private IRenderedComponent<TmAiConversation> RenderWindowed(
        List<AiMessage> messages,
        int? size,
        int step = 10)
        => Render<TmAiConversation>(parameters => parameters
            .Add(component => component.Messages, messages)
            .Add(component => component.RenderWindowSize, size)
            .Add(component => component.RenderWindowStep, step));

    // ── Default: no window ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void WithoutRenderWindowSize_EveryTurnIsRendered()
    {
        var cut = RenderWindowed(Conversation(120), size: null);

        RenderedIds(cut).Should().HaveCount(120);
        cut.FindAll(LoadOlder).Should().BeEmpty();
    }

    // ── Tail window ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FirstRender_ShowsTheNewestTurns_AndOffersToLoadTheOlderOnes()
    {
        var cut = RenderWindowed(Conversation(100), size: 20);

        var ids = RenderedIds(cut);
        ids.Should().HaveCount(20);
        ids[0].Should().Be("m-80");
        ids[^1].Should().Be("m-99");
        cut.FindAll(LoadOlder).Should().ContainSingle().Which.TextContent.Should().Be("Load older messages");
    }

    [Fact]
    public void LoadOlder_GrowsTheWindowByTheStep()
    {
        var cut = RenderWindowed(Conversation(100), size: 20, step: 10);

        cut.Find(LoadOlder).Click();

        var ids = RenderedIds(cut);
        ids.Should().HaveCount(30);
        ids[0].Should().Be("m-70");
    }

    [Fact]
    public void LoadOlder_AtTheTopOfTheList_RemovesTheButton()
    {
        var cut = RenderWindowed(Conversation(15), size: 10, step: 50);

        cut.Find(LoadOlder).Click();

        RenderedIds(cut).Should().HaveCount(15);
        cut.FindAll(LoadOlder).Should().BeEmpty("everything is rendered, so there is nothing older to load");
    }

    /// <summary>A parameter-only re-render (the host's own state changed) must not snap the window back.</summary>
    [Fact]
    public void ExpandedWindow_SurvivesAParameterOnlyRerender()
    {
        var messages = Conversation(100);
        var cut = RenderWindowed(messages, size: 20, step: 10);
        cut.Find(LoadOlder).Click();

        cut.Render(parameters => parameters
            .Add(component => component.Messages, messages)
            .Add(component => component.RenderWindowSize, 20)
            .Add(component => component.RenderWindowStep, 10));

        RenderedIds(cut)[0].Should().Be("m-70", "the reader's expansion is a choice, not a derived default");
    }

    // ── Appends ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AppendedTurn_SlidesTheWindowWithTheTail()
    {
        var messages = Conversation(100);
        var cut = RenderWindowed(messages, size: 20);

        messages.Add(TmAiConversationTests.Message("m-100"));
        cut.Render(parameters => parameters
            .Add(component => component.Messages, messages)
            .Add(component => component.RenderWindowSize, 20));

        var ids = RenderedIds(cut);
        ids.Should().HaveCount(20);
        ids[0].Should().Be("m-81");
        ids[^1].Should().Be("m-100");
    }

    /// <summary>
    /// A reader who pulled the older turns in keeps them when a new turn arrives — the window grows at the
    /// bottom instead of unmounting the rows above the viewport.
    /// </summary>
    [Fact]
    public void AppendedTurn_WhileExpanded_KeepsTheLoadedTurns()
    {
        var messages = Conversation(100);
        var cut = RenderWindowed(messages, size: 20, step: 10);
        cut.Find(LoadOlder).Click();

        messages.Add(TmAiConversationTests.Message("m-100"));
        cut.Render(parameters => parameters
            .Add(component => component.Messages, messages)
            .Add(component => component.RenderWindowSize, 20)
            .Add(component => component.RenderWindowStep, 10));

        var ids = RenderedIds(cut);
        ids[0].Should().Be("m-70");
        ids[^1].Should().Be("m-100");
        ids.Should().HaveCount(31);
    }

    /// <summary>Replacing the list (loading another conversation) resets the window to the new tail.</summary>
    [Fact]
    public void ReplacedList_ResetsTheWindowToTheTail()
    {
        var messages = Conversation(100);
        var cut = RenderWindowed(messages, size: 20, step: 10);
        cut.Find(LoadOlder).Click();

        var replacement = Enumerable.Range(0, 60)
            .Select(index => TmAiConversationTests.Message($"other-{index}"))
            .ToList();

        cut.Render(parameters => parameters
            .Add(component => component.Messages, replacement)
            .Add(component => component.RenderWindowSize, 20)
            .Add(component => component.RenderWindowStep, 10));

        var ids = RenderedIds(cut);
        ids.Should().HaveCount(20);
        ids[0].Should().Be("other-40");
        ids[^1].Should().Be("other-59");
    }

    [Fact]
    public void ShrunkList_ClampsTheWindow()
    {
        var messages = Conversation(100);
        var cut = RenderWindowed(messages, size: 20);
        cut.Find(LoadOlder).Click();

        messages.RemoveRange(50, 50);
        cut.Render(parameters => parameters
            .Add(component => component.Messages, messages)
            .Add(component => component.RenderWindowSize, 20));

        RenderedIds(cut).Should().HaveCount(20);
        RenderedIds(cut)[^1].Should().Be("m-49");
    }

    /// <summary>A window smaller than the minimum is a footgun, not a feature.</summary>
    [Fact]
    public void RenderWindowSize_IsClampedToTheMinimum()
    {
        var cut = RenderWindowed(Conversation(100), size: 3);

        RenderedIds(cut).Should().HaveCount(10, "the window floor keeps a usable transcript");
    }

    /// <summary>The step is a real step: a zero or negative value would make the button a no-op.</summary>
    [Fact]
    public void RenderWindowStep_IsClampedToTheMinimum()
    {
        var cut = RenderWindowed(Conversation(100), size: 20, step: 0);

        cut.Find(LoadOlder).Click();

        RenderedIds(cut).Should().HaveCount(30);
    }

    /// <summary>Loading older turns must not be mistaken for new content: the viewport stays where it is.</summary>
    [Fact]
    public void LoadOlder_DoesNotScrollToTheBottom()
    {
        var module = JSInterop.SetupModule(TmAiConversationTests.InteropModulePath);
        module.SetupVoid("attach", _ => true).SetVoidResult();
        module.SetupVoid("detach", _ => true).SetVoidResult();
        module.Setup<bool>("isNearBottom", _ => true).SetResult(true);
        module.SetupVoid("scrollToBottom", _ => true).SetVoidResult();
        var cut = RenderWindowed(Conversation(100), size: 20, step: 10);
        var before = TmAiConversationTests.Count(module, "scrollToBottom");

        cut.Find(LoadOlder).Click();

        TmAiConversationTests.Count(module, "scrollToBottom").Should().Be(before);
    }

    // ── Jump / expand outside the window (Fáze 7a) ──────────────────────────────────────────────

    private BunitJSModuleInterop SetupWindowInterop()
    {
        var module = JSInterop.SetupModule(TmAiConversationTests.InteropModulePath);
        module.SetupVoid("attach", _ => true).SetVoidResult();
        module.SetupVoid("detach", _ => true).SetVoidResult();
        module.Setup<bool>("isNearBottom", _ => true).SetResult(true);
        module.SetupVoid("scrollToBottom", _ => true).SetVoidResult();
        module.Setup<bool>("scrollToMessage", _ => true).SetResult(true);
        return module;
    }

    /// <summary>
    /// A named turn that is not in the rendered tail must be pulled into the DOM before the module is
    /// asked to scroll to it: otherwise the browser cannot find the row.
    /// </summary>
    [Fact]
    public async Task ScrollToMessageAsync_OutsideWindow_ExpandsAndAnswersTrue()
    {
        var module = SetupWindowInterop();
        var cut = RenderWindowed(Conversation(40), size: 10, step: 10);
        RenderedIds(cut).Should().Equal(Enumerable.Range(30, 10).Select(index => $"m-{index}"));

        var moved = await cut.InvokeAsync(() => cut.Instance.ScrollToMessageAsync("m-3"));

        moved.Should().BeTrue("the named turn is in the host list, so the jump must succeed after the window grows");
        cut.Find("[data-message-id='m-3']").Should().NotBeNull();
        RenderedIds(cut)[0].Should().Be("m-0", "index 3 steps back from the tail onto the Load older grid, so the window starts at 0");
        cut.FindAll(LoadOlder).Should().BeEmpty();
        TmAiConversationTests.Count(module, "scrollToMessage").Should().Be(1,
            "the module is asked once, after the expanded row is in the DOM");
    }

    [Fact]
    public async Task ScrollToMessageAsync_UnknownId_FalseAndWindowUnchanged()
    {
        var module = SetupWindowInterop();
        module.Setup<bool>("scrollToMessage", _ => true).SetResult(false);
        var cut = RenderWindowed(Conversation(40), size: 10, step: 10);

        var moved = await cut.InvokeAsync(() => cut.Instance.ScrollToMessageAsync("m-missing"));

        moved.Should().BeFalse();
        RenderedIds(cut).Should().HaveCount(10);
        RenderedIds(cut)[0].Should().Be("m-30");
        cut.FindAll(LoadOlder).Should().ContainSingle("an unknown id must not grow the window");
        TmAiConversationTests.Count(module, "scrollToMessage").Should().Be(1,
            "the usable unknown id still reaches the browser — same contract as ScrollToMessageAsync_UnknownTurn_AnswersFalse");
    }

    [Fact]
    public async Task ScrollToMessageAsync_LastTurn_DoesNotExpand()
    {
        SetupWindowInterop();
        var cut = RenderWindowed(Conversation(40), size: 10, step: 10);

        var moved = await cut.InvokeAsync(() => cut.Instance.ScrollToMessageAsync("m-39"));

        moved.Should().BeTrue();
        RenderedIds(cut).Should().HaveCount(10, "the newest turn is already in the tail window");
        RenderedIds(cut)[0].Should().Be("m-30");
        cut.FindAll(LoadOlder).Should().ContainSingle();
    }

    [Fact]
    public async Task ScrollToMessageAsync_NoRenderWindow_Unchanged()
    {
        SetupWindowInterop();
        var cut = RenderWindowed(Conversation(40), size: null);

        var moved = await cut.InvokeAsync(() => cut.Instance.ScrollToMessageAsync("m-3"));

        moved.Should().BeTrue();
        RenderedIds(cut).Should().HaveCount(40, "without a window every turn is already in the DOM");
        cut.FindAll(LoadOlder).Should().BeEmpty();
    }

    [Fact]
    public async Task ExpandRenderWindowToAsync_ExpandsWithoutScroll()
    {
        var module = SetupWindowInterop();
        var cut = RenderWindowed(Conversation(40), size: 10, step: 10);

        var found = await cut.InvokeAsync(() => cut.Instance.ExpandRenderWindowToAsync("m-3"));

        found.Should().BeTrue();
        cut.Find("[data-message-id='m-3']").Should().NotBeNull();
        RenderedIds(cut)[0].Should().Be("m-0");
        TmAiConversationTests.Count(module, "scrollToMessage").Should().Be(0,
            "ExpandRenderWindowToAsync only grows the window; the host scrolls itself if it wants to");

        var missing = await cut.InvokeAsync(() => cut.Instance.ExpandRenderWindowToAsync("m-missing"));
        missing.Should().BeFalse();
        RenderedIds(cut)[0].Should().Be("m-0", "a miss after a successful expand must not snap the window back");
    }

    [Fact]
    public async Task Expand_ToIndexZero_MovesFocusToTranscript()
    {
        var cut = Render<TmAiConversation>(parameters => parameters
            .Add(component => component.AutoScroll, false)
            .Add(component => component.Messages, Conversation(15))
            .Add(component => component.RenderWindowSize, 10)
            .Add(component => component.RenderWindowStep, 50));
        var transcript = cut.Find("[role='log']").GetAttribute("blazor:elementreference");

        var found = await cut.InvokeAsync(() => cut.Instance.ExpandRenderWindowToAsync("m-0"));

        found.Should().BeTrue();
        cut.FindAll(LoadOlder).Should().BeEmpty("the whole history is rendered, so the control that had a tab stop is gone");
        var focusCalls = JSInterop.Invocations
            .Where(invocation => invocation.Identifier == FocusInvocation)
            .ToList();
        focusCalls.Should().NotBeEmpty("the removed Load older control must hand the focus back");
        focusCalls[^1].Arguments[0].Should().BeOfType<ElementReference>()
            .Subject.Id.Should().Be(transcript, "the transcript is the reader's next stop");
    }

    [Fact]
    public async Task Expand_ThenAppendedTurn_KeepsFirstRow()
    {
        SetupWindowInterop();
        var messages = Conversation(40);
        var cut = RenderWindowed(messages, size: 10, step: 10);

        (await cut.InvokeAsync(() => cut.Instance.ExpandRenderWindowToAsync("m-3"))).Should().BeTrue();

        messages.Add(TmAiConversationTests.Message("m-40"));
        cut.Render(parameters => parameters
            .Add(component => component.Messages, messages)
            .Add(component => component.RenderWindowSize, 10)
            .Add(component => component.RenderWindowStep, 10));

        RenderedIds(cut)[0].Should().Be("m-0", "an expanded window keeps its first row when a turn is appended");
        RenderedIds(cut)[^1].Should().Be("m-40");
    }

    [Fact]
    public async Task Expand_DuringStreamAsync_KeepsExpansion()
    {
        SetupWindowInterop();
        var messages = Conversation(40);
        var streamingTurn = messages[^1];
        streamingTurn.Status = AiMessageStatus.Pending;
        var cut = RenderWindowed(messages, size: 10, step: 10);

        var parked = new TaskCompletionSource();
        var streaming = cut.InvokeAsync(() => cut.Instance.StreamAsync(streamingTurn, Parked(parked)));
        await parked.Task;

        (await cut.InvokeAsync(() => cut.Instance.ExpandRenderWindowToAsync("m-3"))).Should().BeTrue();
        RenderedIds(cut)[0].Should().Be("m-0");

        await cut.InvokeAsync(() => cut.Instance.StopAsync());
        await streaming;

        RenderedIds(cut)[0].Should().Be("m-0", "throttled stream renders must not snap an expanded window back to the tail");
        cut.Find("[data-message-id='m-3']").Should().NotBeNull();
    }

    [Fact]
    public async Task ScrollToMessageAsync_OutsideWindow_DetachesFollow()
    {
        var module = SetupWindowInterop();
        var messages = Conversation(40);
        var cut = RenderWindowed(messages, size: 10, step: 10);

        (await cut.InvokeAsync(() => cut.Instance.ScrollToMessageAsync("m-3"))).Should().BeTrue();
        cut.Find("[data-message-id='m-3']").Should().NotBeNull();

        messages.Add(TmAiConversationTests.Message("m-40"));
        cut.Render(parameters => parameters
            .Add(component => component.Messages, messages)
            .Add(component => component.RenderWindowSize, 10)
            .Add(component => component.RenderWindowStep, 10));

        TmAiConversationTests.Count(module, "scrollToBottom").Should().Be(1,
            "the only follow is the first-render jump — a jump away from the newest turn detaches");
        cut.FindAll(NewMessages).Should().ContainSingle();
    }

    /// <summary>
    /// Two jumps that overlap before the next paint must both finish: replacing the pending waiter
    /// would leave the first <see cref="TaskCompletionSource{TResult}"/> incomplete forever.
    /// </summary>
    [Fact]
    public async Task ExpandRenderWindowToAsync_OverlappingJumps_BothComplete()
    {
        SetupWindowInterop();
        var cut = RenderWindowed(Conversation(40), size: 10, step: 10);

        var first = cut.Instance.ExpandRenderWindowToAsync("m-3");
        var second = cut.Instance.ExpandRenderWindowToAsync("m-15");

        var completed = Task.WhenAll(first, second).Wait(TimeSpan.FromSeconds(5));
        completed.Should().BeTrue("a second expand must share the pending paint, not orphan the first waiter");
        (await first).Should().BeTrue();
        (await second).Should().BeTrue();
        cut.Find("[data-message-id='m-3']").Should().NotBeNull();
        cut.Find("[data-message-id='m-15']").Should().NotBeNull();
    }

    /// <summary>
    /// The jump must step back from the current tail origin, not from index 0: otherwise a list whose
    /// length is not a multiple of <c>RenderWindowStep</c> lands later "Load older" clicks on a
    /// different grid than the button itself uses.
    /// </summary>
    [Fact]
    public async Task ExpandRenderWindowToAsync_NonMultipleLength_AlignsToLoadOlderGrid()
    {
        SetupWindowInterop();
        var cut = RenderWindowed(Conversation(37), size: 10, step: 10);
        RenderedIds(cut)[0].Should().Be("m-27", "37 − 10 is not a multiple of the step");

        (await cut.InvokeAsync(() => cut.Instance.ExpandRenderWindowToAsync("m-22"))).Should().BeTrue();

        RenderedIds(cut)[0].Should().Be("m-17",
            "index 22 is one step back from start 27, matching Load older's grid");

        cut.Find(LoadOlder).Click();
        RenderedIds(cut)[0].Should().Be("m-7");
    }

    /// <summary>
    /// A host that expands during <see cref="ComponentBase.OnInitializedAsync"/> must not wait for
    /// <c>OnAfterRenderAsync</c>: that paint cannot run until initialization finishes, so a TCS
    /// waiter would hang (static SSR, prerender, and a call before the first interactive paint).
    /// </summary>
    [Fact]
    public void ExpandRenderWindowToAsync_FromOnInitialized_Completes()
    {
        SetupWindowInterop();

        var cut = Render<ExpandOnInitializedConversation>(parameters => parameters
            .Add(component => component.Messages, Conversation(40))
            .Add(component => component.RenderWindowSize, 10)
            .Add(component => component.RenderWindowStep, 10)
            .Add(component => component.AutoScroll, false));

        cut.Instance.ExpandCompleted.Should().BeTrue();
        cut.Find("[data-message-id='m-3']").Should().NotBeNull();
    }

    /// <summary>
    /// Hosts the expand from <see cref="ComponentBase.OnInitializedAsync"/> and bounds the wait so a
    /// hang fails the test instead of stalling the runner.
    /// </summary>
    private sealed class ExpandOnInitializedConversation : TmAiConversation
    {
        internal bool ExpandCompleted { get; private set; }

        protected override async Task OnInitializedAsync()
        {
            ExpandCompleted = await ExpandRenderWindowToAsync("m-3").WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    /// <summary>A stream that parks after its first delta until its token is cancelled.</summary>
    private static async IAsyncEnumerable<AiStreamDelta> Parked(
        TaskCompletionSource parked,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new AiTextDelta { Text = "partial" };
        parked.TrySetResult();
        await new TaskCompletionSource().Task.WaitAsync(cancellationToken);
    }
}
