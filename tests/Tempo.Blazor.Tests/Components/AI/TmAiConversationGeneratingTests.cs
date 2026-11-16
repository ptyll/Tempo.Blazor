using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// bUnit contract of the HOST-DRIVEN generation window (<see cref="TmAiConversation.Generating"/>):
/// a host whose answer does not travel through <see cref="TmAiConversation.StreamAsync"/> (its own HTTP
/// call, its own agent loop) sets <c>Generating</c> true for the request and false when the answer
/// arrived. The component must treat that window exactly like a stream — busy state, Stop affordance,
/// and the one completion announcement on the true→false edge — so the host needs none of the busy
/// bookkeeping it used to do itself (the EmusCz <c>setBusy</c>/<c>SyncBusyAsync</c> workaround).
/// </summary>
/// <remarks>
/// A generation that DOES run through <see cref="TmAiConversation.StreamAsync"/> already announces its
/// completion; the edge must then stay silent, or a host that binds both (composer state + stream) hears
/// "Answer complete" twice.
/// </remarks>
public sealed class TmAiConversationGeneratingTests : LocalizationTestBase
{
    private const string StopTestId = "[data-testid='ai-conversation-stop']";
    private const string AnnouncementTestId = "[data-testid='ai-conversation-announcement']";

    private static readonly DateTimeOffset Start = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

    private BunitJSModuleInterop SetupInterop()
    {
        var module = JSInterop.SetupModule(TmAiConversationTests.InteropModulePath);
        module.SetupVoid("attach", _ => true).SetVoidResult();
        module.SetupVoid("detach", _ => true).SetVoidResult();
        module.Setup<bool>("isNearBottom", _ => true).SetResult(true);
        module.SetupVoid("scrollToBottom", _ => true).SetVoidResult();
        return module;
    }

    /// <summary>A finished answer turn, the shape the transcript has when a host-driven request ended well.</summary>
    private static List<AiMessage> CompletedConversation()
        => TmAiConversationTests.Conversation(1);

    // ── The generating window ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Generating_True_RunsTheGeneratingState()
    {
        var module = SetupInterop();
        var cut = Render<TmAiConversation>(parameters => parameters
            .Add(component => component.Messages, CompletedConversation())
            .Add(component => component.Generating, true)
            .Add(component => component.OnStop, () => { }));

        cut.Instance.IsGenerating.Should().BeTrue();
        cut.Find(".tm-ai-conversation").GetAttribute("data-generating").Should().Be("true");
        cut.Find("[role='log']").GetAttribute("aria-busy").Should().Be("true");
        cut.FindAll(StopTestId).Should().ContainSingle("a host-driven generation is a running generation");
        module.Invocations.Should().NotBeEmpty("the module is still the scroll bookkeeping of the window");
    }

    /// <summary>
    /// A pill the reader can press but that cannot DO anything is worse than a missing control: without a
    /// bound <see cref="TmAiConversation.OnStop"/> a host-driven window has no stop path (there is no
    /// stream token to cancel), so the pill must not render at all.
    /// </summary>
    [Fact]
    public void Generating_True_WithoutAnOnStopHandler_RendersNoStopPill()
    {
        SetupInterop();
        var cut = Render<TmAiConversation>(parameters => parameters
            .Add(component => component.Messages, CompletedConversation())
            .Add(component => component.Generating, true));

        cut.FindAll(StopTestId).Should().BeEmpty("a dead Stop pill must not render");
        cut.FindAll(".tm-ai-conversation__overlay-bar").Should().BeEmpty();
        cut.Find(".tm-ai-conversation").GetAttribute("data-generating").Should().Be("true",
            "the busy state itself does not depend on the stop affordance");
    }

    /// <summary>A host with its own stop control hides the pill the same way — <c>ShowStop</c> still wins.</summary>
    [Fact]
    public void Generating_True_WithShowStopFalse_RendersNoStopPill()
    {
        SetupInterop();
        var cut = Render<TmAiConversation>(parameters => parameters
            .Add(component => component.Messages, CompletedConversation())
            .Add(component => component.Generating, true)
            .Add(component => component.OnStop, () => { })
            .Add(component => component.ShowStop, false));

        cut.FindAll(StopTestId).Should().BeEmpty();
    }

    [Fact]
    public void Generating_False_LeavesTheIdleState()
    {
        var cut = Render<TmAiConversation>(parameters => parameters
            .Add(component => component.Messages, CompletedConversation())
            .Add(component => component.Generating, false));

        cut.Instance.IsGenerating.Should().BeFalse();
        cut.Find(".tm-ai-conversation").GetAttribute("data-generating").Should().Be("false");
        cut.Find("[role='log']").GetAttribute("aria-busy").Should().BeNull();
        cut.FindAll(StopTestId).Should().BeEmpty();
    }

    // ── The completion announcement on the true→false edge ──────────────────────────────────────

    [Fact]
    public void GeneratingEdge_TrueToFalse_WithACompletedLastTurn_AnnouncesTheAnswer()
    {
        var cut = Render<TmAiConversation>(parameters => parameters
            .Add(component => component.Messages, CompletedConversation())
            .Add(component => component.Generating, true));

        cut.Render(parameters => parameters.Add(component => component.Generating, false));

        var announcements = cut.FindAll(AnnouncementTestId);
        announcements.Should().ContainSingle("the finished answer is announced exactly like a streamed one");
        announcements[0].TextContent.Should().Be("Answer complete");
        announcements[0].Closest("[role='log']").Should().NotBeNull("the note lives inside the live region");
    }

    [Fact]
    public void GeneratingEdge_TrueToFalse_WithoutACompletedLastTurn_AnnouncesNothing()
    {
        var pending = new AiMessage { Id = "answer-1", Role = AiMessageRole.Assistant, Status = AiMessageStatus.Pending };
        var cut = Render<TmAiConversation>(parameters => parameters
            .Add(component => component.Messages, new List<AiMessage> { pending })
            .Add(component => component.Generating, true));

        cut.Render(parameters => parameters.Add(component => component.Generating, false));

        cut.FindAll(AnnouncementTestId).Should().BeEmpty(
            "an edge without a completed answer is not a completion — the turn may still be missing");
    }

    /// <summary>
    /// A host that binds <c>Generating</c> AND streams through <see cref="TmAiConversation.StreamAsync"/>
    /// hears the completion once: the stream announces it, so the edge that follows (the host resetting its
    /// own busy flag) stays silent.
    /// </summary>
    [Fact]
    public async Task GeneratingEdge_AfterAStreamedCompletion_DoesNotAnnounceTwice()
    {
        var clock = new FakeTimeProvider(Start);
        Services.AddSingleton<TimeProvider>(clock);
        SetupInterop();

        var message = new AiMessage { Id = "answer-1", Role = AiMessageRole.Assistant, Status = AiMessageStatus.Pending };
        var messages = new List<AiMessage> { message };
        var cut = Render<TmAiConversation>(parameters => parameters
            .Add(component => component.Messages, messages)
            .Add(component => component.Generating, true));

        await cut.InvokeAsync(() => cut.Instance.StreamAsync(
            message,
            FinishedStream(new AiTextDelta { Text = "done" }, new AiCompletedDelta())));

        cut.FindAll(AnnouncementTestId).Should().ContainSingle("the stream announced the completion");

        cut.Render(parameters => parameters.Add(component => component.Generating, false));

        cut.FindAll(AnnouncementTestId).Should().ContainSingle("the edge must not repeat the stream's announcement");
    }

    [Fact]
    public void GeneratingEdge_RepeatedIdleToggling_DoesNotAnnounceWithoutAWindow()
    {
        var cut = Render<TmAiConversation>(parameters => parameters
            .Add(component => component.Messages, CompletedConversation()));

        cut.Render(parameters => parameters.Add(component => component.Generating, false));

        cut.FindAll(AnnouncementTestId).Should().BeEmpty("a render that never generated is not an edge");
    }

    // ── Stop during a host-driven window ────────────────────────────────────────────────────────

    /// <summary>
    /// The built-in Stop pill renders while the host drives <c>Generating</c>; pressing it (or a host
    /// calling <see cref="TmAiConversation.StopAsync"/>) must reach the host through <c>OnStop</c> — there
    /// is no stream token to cancel, so the event is the only way to ask the host to stop its own work.
    /// A second call while the window is still open is a no-op, so a defensive handler cannot loop.
    /// </summary>
    [Fact]
    public async Task StopAsync_DuringHostDrivenGeneration_RaisesOnStopOnce()
    {
        var stopped = 0;
        var cut = Render<TmAiConversation>(parameters => parameters
            .Add(component => component.Messages, CompletedConversation())
            .Add(component => component.Generating, true)
            .Add(component => component.OnStop, () => stopped++));

        await cut.InvokeAsync(() => cut.Instance.StopAsync());

        stopped.Should().Be(1, "the host-driven window forwards the stop as the notification it is");

        await cut.InvokeAsync(() => cut.Instance.StopAsync());

        stopped.Should().Be(1, "a second call in the same window is a no-op — it cannot loop");
    }

    /// <summary>
    /// Pressing the built-in Stop pill raises <see cref="TmAiConversation.OnStop"/> once and consumes the
    /// window's guard, so the pill disappears immediately — and since the reader's own activation removed
    /// the focused control, the focus falls back to the transcript (WCAG 2.4.3) instead of <c>&lt;body&gt;</c>.
    /// </summary>
    [Fact]
    public void StopPill_DuringHostDrivenGeneration_RaisesOnStopOnce_Disappears_AndFallsBackToTheLog()
    {
        SetupInterop();
        var stopped = 0;
        var cut = Render<TmAiConversation>(parameters => parameters
            .Add(component => component.Messages, CompletedConversation())
            .Add(component => component.Generating, true)
            .Add(component => component.OnStop, () => stopped++));

        cut.Find(StopTestId).Click();

        stopped.Should().Be(1, "the host-driven window forwards the stop as the notification it is");
        cut.FindAll(StopTestId).Should().BeEmpty("the window's one stop is spent — the pill must not stay clickable-but-dead");
        cut.FindAll(".tm-ai-conversation__overlay-bar").Should().BeEmpty();
        JSInterop.VerifyFocusAsyncInvoke().Arguments[0].Should().BeOfType<ElementReference>(
            "the pill removed itself, so the focus moves to the transcript instead of <body>");
    }

    [Fact]
    public async Task StopAsync_AfterTheHostDrivenWindow_IsANoOpAgain()
    {
        var stopped = 0;
        var cut = Render<TmAiConversation>(parameters => parameters
            .Add(component => component.Messages, CompletedConversation())
            .Add(component => component.OnStop, () => stopped++));

        cut.Render(parameters => parameters.Add(component => component.Generating, true));
        await cut.InvokeAsync(() => cut.Instance.StopAsync());
        cut.Render(parameters => parameters.Add(component => component.Generating, false));

        await cut.InvokeAsync(() => cut.Instance.StopAsync());

        stopped.Should().Be(1, "a stop when nothing runs (the window closed) does nothing");
    }

    /// <summary>A NEW window is stoppable again: the once-per-window guard resets when the host re-arms.</summary>
    [Fact]
    public async Task StopAsync_ANewHostDrivenWindow_IsStoppableAgain()
    {
        var stopped = 0;
        var cut = Render<TmAiConversation>(parameters => parameters
            .Add(component => component.Messages, CompletedConversation())
            .Add(component => component.Generating, true)
            .Add(component => component.OnStop, () => stopped++));

        await cut.InvokeAsync(() => cut.Instance.StopAsync());
        cut.Render(parameters => parameters.Add(component => component.Generating, false));
        cut.Render(parameters => parameters.Add(component => component.Generating, true));
        await cut.InvokeAsync(() => cut.Instance.StopAsync());

        stopped.Should().Be(2, "each window gets exactly one stop");
    }

    // ── Stream during the window ────────────────────────────────────────────────────────────────

    /// <summary>
    /// A host may drive <c>Generating</c> itself and STILL hand the deltas to <see cref="TmAiConversation.StreamAsync"/>
    /// (the composer busy flag and the stream are two views of the same request). The stream owns the
    /// completion announcement; the busy state stays on through both halves.
    /// </summary>
    [Fact]
    public async Task StreamAsync_DuringTheHostDrivenWindow_KeepsTheBusyStateAndCompletes()
    {
        var clock = new FakeTimeProvider(Start);
        Services.AddSingleton<TimeProvider>(clock);
        SetupInterop();

        var message = new AiMessage { Id = "answer-1", Role = AiMessageRole.Assistant, Status = AiMessageStatus.Pending };
        var cut = Render<TmAiConversation>(parameters => parameters
            .Add(component => component.Messages, new List<AiMessage> { message })
            .Add(component => component.Generating, true));

        await cut.InvokeAsync(() => cut.Instance.StreamAsync(
            message,
            FinishedStream(new AiTextDelta { Text = "done" }, new AiCompletedDelta())));

        message.Status.Should().Be(AiMessageStatus.Completed);
        cut.Instance.IsGenerating.Should().BeTrue("the host's window is still open until it resets Generating");
        cut.Find(".tm-ai-conversation").GetAttribute("data-generating").Should().Be("true");
    }

    /// <summary>
    /// A stop that interrupts a STREAM inside a host-driven window is the window's ONE stop: the stream
    /// cancellation raises <see cref="TmAiConversation.OnStop"/> and consumes the window's guard, so the
    /// host (whose <c>Generating</c> flag is still up) cannot be asked a second time — and the pill that
    /// now has no action left disappears instead of staying clickable-but-dead.
    /// </summary>
    [Fact]
    public async Task StopAsync_AStreamInsideTheGeneratingWindow_RaisesOnStopOncePerWindow()
    {
        SetupInterop();
        var stopped = 0;
        var message = new AiMessage { Id = "answer-1", Role = AiMessageRole.Assistant, Status = AiMessageStatus.Pending };
        var cut = Render<TmAiConversation>(parameters => parameters
            .Add(component => component.Messages, new List<AiMessage> { message })
            .Add(component => component.Generating, true)
            .Add(component => component.OnStop, () => stopped++));

        var parked = new TaskCompletionSource();
        var streaming = cut.InvokeAsync(() => cut.Instance.StreamAsync(message, Parked(parked)));
        await parked.Task;
        cut.FindAll(StopTestId).Should().ContainSingle();

        await cut.InvokeAsync(() => cut.Instance.StopAsync());
        await streaming;

        stopped.Should().Be(1, "the stream's stop is the window's stop");
        message.Status.Should().Be(AiMessageStatus.Stopped);
        cut.Instance.IsGenerating.Should().BeTrue("the host has not reset its Generating flag yet");
        cut.FindAll(StopTestId).Should().BeEmpty("the window's one stop was already delivered");

        await cut.InvokeAsync(() => cut.Instance.StopAsync());

        stopped.Should().Be(1, "OnStop runs once per generating window — never twice");
    }

    private static async IAsyncEnumerable<AiStreamDelta> Parked(
        TaskCompletionSource parked,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new AiTextDelta { Text = "partial" };
        parked.TrySetResult();
        await new TaskCompletionSource().Task.WaitAsync(cancellationToken);
    }

    private static async IAsyncEnumerable<AiStreamDelta> FinishedStream(
        AiStreamDelta first,
        AiStreamDelta second,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return first;
        await Task.Yield();
        yield return second;
    }
}
