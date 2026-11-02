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
/// bUnit contract of the STREAMING half of <see cref="TmAiConversation"/>: the host hands the component an
/// <see cref="IAsyncEnumerable{T}"/> of <see cref="AiStreamDelta"/>s, the component folds it into the message
/// through <c>AiMessageStreamApplier</c>, throttles the renders (a Server circuit must not re-render per
/// token), lets the reader stop it, and announces the finished answer exactly once.
/// </summary>
/// <remarks>
/// The clock is the injected <see cref="TimeProvider"/> (a <see cref="FakeTimeProvider"/> here, the system
/// clock in production — the package never registers one), so the throttle is asserted deterministically
/// instead of by sleeping.
/// </remarks>
public sealed class TmAiConversationStreamingTests : LocalizationTestBase
{
    private const string StopTestId = "[data-testid='ai-conversation-stop']";

    private static readonly DateTimeOffset Start = new(2026, 10, 31, 8, 0, 0, TimeSpan.Zero);

    private FakeTimeProvider UseFakeClock()
    {
        var clock = new FakeTimeProvider(Start);
        Services.AddSingleton<TimeProvider>(clock);
        return clock;
    }

    /// <summary>
    /// Tracks the transcript without following it (the reader is already at the bottom), so the render counts
    /// below measure the STREAM and nothing else.
    /// </summary>
    private BunitJSModuleInterop UseInterop()
    {
        var module = JSInterop.SetupModule(TmAiConversationTests.InteropModulePath);
        module.SetupVoid("attach", _ => true).SetVoidResult();
        module.SetupVoid("detach", _ => true).SetVoidResult();
        module.Setup<bool>("isNearBottom", _ => true).SetResult(true);
        module.SetupVoid("scrollToBottom", _ => true).SetVoidResult();
        return module;
    }

    /// <summary>An answer stream that yields on a fresh dispatcher turn per delta, like a real transport.</summary>
    private static async IAsyncEnumerable<AiStreamDelta> Stream(
        AiStreamDelta[] deltas,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var delta in deltas)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return delta;
        }
    }

    /// <summary>
    /// A stream that parks after its first delta until the caller's token is cancelled — the shape of a
    /// model call the reader interrupts.
    /// </summary>
    private static async IAsyncEnumerable<AiStreamDelta> Parked(
        TaskCompletionSource parked,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new AiTextDelta { Text = "partial" };
        parked.TrySetResult();
        await new TaskCompletionSource().Task.WaitAsync(cancellationToken);
        yield return new AiTextDelta { Text = "late" };
    }

    private static async IAsyncEnumerable<AiStreamDelta> Failing(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new AiTextDelta { Text = "partial" };
        await Task.Yield();
        throw new InvalidOperationException("transport exploded");
    }

    private static List<AiMessage> Answers(AiMessage message) => new() { message };

    private static AiMessage Pending(string id = "answer-1") => new()
    {
        Id = id,
        Role = AiMessageRole.Assistant,
        Status = AiMessageStatus.Pending,
    };

    // ── Folding deltas ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StreamAsync_FoldsEveryDeltaIntoTheMessage()
    {
        var message = Pending();
        var messages = Answers(message);
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, messages));

        await cut.InvokeAsync(() => cut.Instance.StreamAsync(message, Stream(
        [
            new AiTextDelta { Text = "Hel" },
            new AiTextDelta { Text = "lo!" },
            new AiCompletedDelta(),
        ])));

        message.Text.Should().Be("Hello!");
        message.Status.Should().Be(AiMessageStatus.Completed);
        cut.Find(".tm-ai-conversation__message .tm-ai-message").TextContent.Should().Contain("Hello!");
        cut.Instance.IsGenerating.Should().BeFalse();
    }

    /// <summary>
    /// A transport that simply ends is a finished answer: a host must not have to append a trailing
    /// <see cref="AiCompletedDelta"/> to stop the caret.
    /// </summary>
    [Fact]
    public async Task StreamAsync_EndOfStream_CompletesTheAnswer()
    {
        var message = Pending();
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, Answers(message)));

        await cut.InvokeAsync(() => cut.Instance.StreamAsync(message, Stream([new AiTextDelta { Text = "done" }])));

        message.Status.Should().Be(AiMessageStatus.Completed);
        message.Text.Should().Be("done");
    }

    /// <summary>
    /// The completion is announced ONCE through the transcript's live region, and a later re-render of the
    /// same conversation does not repeat it (a status that re-announces on every parent render is noise).
    /// </summary>
    [Fact]
    public async Task StreamAsync_AnnouncesTheCompletedAnswerOnce()
    {
        var message = Pending();
        var messages = Answers(message);
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, messages));

        await cut.InvokeAsync(() => cut.Instance.StreamAsync(message, Stream(
        [
            new AiTextDelta { Text = "answer" },
            new AiCompletedDelta(),
        ])));

        var announcements = cut.FindAll("[data-testid='ai-conversation-announcement']");
        announcements.Should().ContainSingle();
        announcements[0].TextContent.Should().Be("Answer complete");
        announcements[0].GetAttribute("role").Should().BeNull("the transcript itself is the single live region");
        announcements[0].Closest("[role='log']").Should().NotBeNull("the note has to live inside the live region");

        cut.Render(parameters => parameters.Add(component => component.Messages, messages));

        cut.FindAll("[data-testid='ai-conversation-announcement']").Should().ContainSingle("a re-render is not a new completion");
    }

    /// <summary>A stopped answer is NOT announced as complete, and the conversation says nothing extra.</summary>
    [Fact]
    public async Task Stop_DoesNotAnnounceACompletion()
    {
        var message = Pending();
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, Answers(message)));

        var parked = new TaskCompletionSource();
        var streaming = cut.InvokeAsync(() => cut.Instance.StreamAsync(message, Parked(parked)));
        await parked.Task;

        await cut.InvokeAsync(() => cut.Instance.StopAsync());
        await streaming;

        cut.FindAll("[data-testid='ai-conversation-announcement']").Should().BeEmpty();
    }

    // ── Throttling ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// One render per elapsed <see cref="TmAiConversation.RenderThrottle"/> interval, plus a render when the
    /// stream starts (the Stop affordance) and one when it ends — NOT one per token. With a fake clock
    /// advanced 10 ms per delta and a 50 ms throttle, ten deltas cost four renders.
    /// </summary>
    [Fact]
    public async Task StreamAsync_ThrottlesRendersToOnePerInterval()
    {
        var clock = UseFakeClock();
        UseInterop();
        var message = Pending();
        var cut = Render<TmAiConversationTests.RenderCountingConversation>(parameters => parameters
            .Add(component => component.Messages, Answers(message))
            .Add(component => component.RenderThrottle, TimeSpan.FromMilliseconds(50)));

        var deltas = Enumerable.Range(0, 10)
            .Select(_ => (AiStreamDelta)new AiTextDelta { Text = "x" })
            .ToArray();

        var before = cut.Instance.OwnRenderCount;
        await cut.InvokeAsync(() => cut.Instance.StreamAsync(message, Timed(clock, deltas, TimeSpan.FromMilliseconds(10))));
        var renders = cut.Instance.OwnRenderCount - before;

        renders.Should().Be(4,
            "two 50 ms boundaries elapsed (at 50 ms and 100 ms) plus the start and the final render");
        renders.Should().BeLessThan(deltas.Length, "one render per token is the render storm this throttle exists to prevent");
        message.Text.Should().Be("xxxxxxxxxx");
    }

    /// <summary>Deltas arriving faster than the throttle cost the start and the final render only.</summary>
    [Fact]
    public async Task StreamAsync_DeltasFasterThanTheThrottle_DoNotRenderPerDelta()
    {
        var clock = UseFakeClock();
        UseInterop();
        var message = Pending();
        var cut = Render<TmAiConversationTests.RenderCountingConversation>(parameters => parameters
            .Add(component => component.Messages, Answers(message))
            .Add(component => component.RenderThrottle, TimeSpan.FromMilliseconds(200)));

        var deltas = Enumerable.Range(0, 50).Select(_ => (AiStreamDelta)new AiTextDelta { Text = "x" }).ToArray();

        var before = cut.Instance.OwnRenderCount;
        await cut.InvokeAsync(() => cut.Instance.StreamAsync(message, Timed(clock, deltas, TimeSpan.Zero)));
        var renders = cut.Instance.OwnRenderCount - before;

        renders.Should().Be(2, "a stream that never crosses a throttle boundary renders once when it starts and once when it ends");
        message.Text.Length.Should().Be(50);
    }

    /// <summary>A zero throttle means "every applied delta" — the escape hatch for a host that wants it.</summary>
    [Fact]
    public async Task StreamAsync_ZeroThrottle_RendersPerAppliedDelta()
    {
        UseFakeClock();
        UseInterop();
        var message = Pending();
        var cut = Render<TmAiConversationTests.RenderCountingConversation>(parameters => parameters
            .Add(component => component.Messages, Answers(message))
            .Add(component => component.RenderThrottle, TimeSpan.Zero));

        var before = cut.Instance.OwnRenderCount;
        await cut.InvokeAsync(() => cut.Instance.StreamAsync(message, Stream(
        [
            new AiTextDelta { Text = "a" },
            new AiTextDelta { Text = "b" },
            new AiTextDelta { Text = "c" },
        ])));

        (cut.Instance.OwnRenderCount - before).Should().Be(5, "start + three deltas + final");
    }

    private static async IAsyncEnumerable<AiStreamDelta> Timed(
        FakeTimeProvider clock,
        AiStreamDelta[] deltas,
        TimeSpan step,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var delta in deltas)
        {
            cancellationToken.ThrowIfCancellationRequested();
            clock.Advance(step);
            await Task.Yield();
            yield return delta;
        }
    }

    // ── Stop ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Stop_CancelsTheStream_KeepsThePartialAnswer_AndRaisesOnStop()
    {
        var message = Pending();
        var stopped = 0;
        var cut = Render<TmAiConversation>(parameters => parameters
            .Add(component => component.Messages, Answers(message))
            .Add(component => component.OnStop, () => stopped++));

        var parked = new TaskCompletionSource();
        var streaming = cut.InvokeAsync(() => cut.Instance.StreamAsync(message, Parked(parked)));
        await parked.Task;

        cut.Instance.IsGenerating.Should().BeTrue();
        cut.FindAll(StopTestId).Should().ContainSingle("a running generation must be stoppable");
        cut.Find("[role='log']").GetAttribute("aria-busy").Should().Be("true");

        await cut.InvokeAsync(() => cut.Instance.StopAsync());
        await streaming;

        message.Status.Should().Be(AiMessageStatus.Stopped);
        message.Text.Should().Be("partial", "a stopped answer keeps what already arrived");
        stopped.Should().Be(1);
        cut.Instance.IsGenerating.Should().BeFalse();
        cut.FindAll(StopTestId).Should().BeEmpty();
        cut.Find("[role='log']").GetAttribute("aria-busy").Should().BeNull();
    }

    /// <summary>
    /// The host's own cancellation token stops the stream the same way, but the reader did not press
    /// anything, so <see cref="TmAiConversation.OnStop"/> stays silent (a host must not receive its own
    /// cancellation echo).
    /// </summary>
    [Fact]
    public async Task HostCancellation_StopsTheAnswerWithoutRaisingOnStop()
    {
        var message = Pending();
        var stopped = 0;
        var cut = Render<TmAiConversation>(parameters => parameters
            .Add(component => component.Messages, Answers(message))
            .Add(component => component.OnStop, () => stopped++));

        using var cancellation = new CancellationTokenSource();
        var parked = new TaskCompletionSource();
        var streaming = cut.InvokeAsync(() => cut.Instance.StreamAsync(message, Parked(parked), cancellation.Token));
        await parked.Task;

        cancellation.Cancel();
        await streaming;

        message.Status.Should().Be(AiMessageStatus.Stopped);
        stopped.Should().Be(0);
        cut.Instance.IsGenerating.Should().BeFalse();
    }

    [Fact]
    public async Task Stop_WithoutARunningStream_IsANoOp()
    {
        var message = Pending();
        var stopped = 0;
        var cut = Render<TmAiConversation>(parameters => parameters
            .Add(component => component.Messages, Answers(message))
            .Add(component => component.OnStop, () => stopped++));

        await cut.InvokeAsync(() => cut.Instance.StopAsync());

        stopped.Should().Be(0);
        message.Status.Should().Be(AiMessageStatus.Pending);
    }

    /// <summary>A second generation cannot start on top of a running one — the first stream would be orphaned.</summary>
    [Fact]
    public async Task StreamAsync_WhileGenerating_Throws()
    {
        var message = Pending();
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, Answers(message)));

        var parked = new TaskCompletionSource();
        var streaming = cut.InvokeAsync(() => cut.Instance.StreamAsync(message, Parked(parked)));
        await parked.Task;

        try
        {
            var second = cut.InvokeAsync(() => cut.Instance.StreamAsync(Pending("answer-2"), Stream([new AiTextDelta { Text = "x" }])));
            await FluentActions.Awaiting(() => second).Should().ThrowAsync<InvalidOperationException>();
        }
        finally
        {
            await cut.InvokeAsync(() => cut.Instance.StopAsync());
            await streaming;
        }
    }

    [Fact]
    public async Task Stream_ThatThrows_FailsTheAnswerAndSurfacesTheException()
    {
        var message = Pending();
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, Answers(message)));

        var streaming = cut.InvokeAsync(() => cut.Instance.StreamAsync(message, Failing()));

        await FluentActions.Awaiting(() => streaming).Should().ThrowAsync<InvalidOperationException>();

        message.Status.Should().Be(AiMessageStatus.Failed);
        message.Error.Should().Be("transport exploded");
        cut.Instance.IsGenerating.Should().BeFalse();
        cut.FindAll(StopTestId).Should().BeEmpty();
    }

    /// <summary>
    /// A late token must not resurrect a stopped answer: the loop stops applying deltas the moment it is
    /// cancelled, even if the transport keeps pushing.
    /// </summary>
    [Fact]
    public async Task Stop_IgnoresTokensThatArriveAfterTheCancellation()
    {
        var message = Pending();
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, Answers(message)));

        using var cancellation = new CancellationTokenSource();
        var parked = new TaskCompletionSource();
        var streaming = cut.InvokeAsync(() => cut.Instance.StreamAsync(message, Parked(parked), cancellation.Token));
        await parked.Task;

        cancellation.Cancel();
        await streaming;

        message.Text.Should().Be("partial");
        message.Status.Should().Be(AiMessageStatus.Stopped);
    }

    // ── Pre-render and terminal states ──────────────────────────────────────────────────────────

    [Fact]
    public async Task StreamAsync_DisarmsTheGeneratingStateWhenItEnds()
    {
        var message = Pending();
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, Answers(message)));

        cut.Instance.IsGenerating.Should().BeFalse();

        await cut.InvokeAsync(() => cut.Instance.StreamAsync(message, Stream([new AiCompletedDelta()])));

        cut.Instance.IsGenerating.Should().BeFalse();
        cut.Find(".tm-ai-conversation").GetAttribute("data-generating").Should().Be("false");
    }
}
