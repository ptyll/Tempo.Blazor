using System.Runtime.CompilerServices;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// bUnit contract of the KEYBOARD FOCUS around the conversation's disappearing controls (WCAG 2.4.3): the
/// transcript is a keyboard-scrollable region, and every control the reader's activation REMOVES hands the
/// focus back to it instead of dropping the reader onto <c>&lt;body&gt;</c> — the "Load older messages"
/// button (it is gone as soon as the whole history is rendered), the "New messages" pill (it is dismissed by
/// its own activation) and the "Stop generating" pill (it disappears when the generation ends).
/// </summary>
/// <remarks>
/// The focus target is the transcript's element reference; bUnit stamps it on the rendered node as
/// <c>blazor:elementreference</c> before the interaction, so the assertion matches the node the component
/// owns rather than any focusable element. A host that stops a stream PROGRAMMATICALLY keeps the focus where
/// it is — the component never steals it, because nothing the reader had focused disappeared.
/// </remarks>
public sealed class TmAiConversationFocusTests : LocalizationTestBase
{
    private const string FocusInvocation = "Blazor._internal.domWrapper.focus";

    private static List<AiMessage> Conversation(int count) => TmAiConversationTests.Conversation(count);

    private static AiMessage Pending() => new()
    {
        Id = "answer-1",
        Role = AiMessageRole.Assistant,
        Status = AiMessageStatus.Pending,
    };

    /// <summary>A stream that parks after its first delta until its token is cancelled.</summary>
    private static async IAsyncEnumerable<AiStreamDelta> Parked(
        TaskCompletionSource parked,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new AiTextDelta { Text = "partial" };
        parked.TrySetResult();
        await new TaskCompletionSource().Task.WaitAsync(cancellationToken);
    }

    private IRenderedComponent<TmAiConversation> RenderWithoutScrollTracking(
        Action<ComponentParameterCollectionBuilder<TmAiConversation>>? configure = null)
        => Render<TmAiConversation>(parameters =>
        {
            // The host owns the scrolling surface: no module is imported, so the only interop left is the
            // focus move this fixture is about.
            parameters.Add(component => component.AutoScroll, false);
            configure?.Invoke(parameters);
        });

    private static string? TranscriptReferenceId(IRenderedComponent<TmAiConversation> cut)
        => cut.Find("[role='log']").GetAttribute("blazor:elementreference");

    private void ShouldHaveFocusedTheTranscriptLast(IRenderedComponent<TmAiConversation> cut, string? referenceId)
    {
        referenceId.Should().NotBeNullOrEmpty("the fixture reads the transcript's reference id before the interaction");

        var focusCalls = JSInterop.Invocations
            .Where(invocation => invocation.Identifier == FocusInvocation)
            .ToList();
        focusCalls.Should().NotBeEmpty("the removed control must hand the focus back");
        focusCalls[^1].Arguments[0].Should().BeOfType<ElementReference>()
            .Subject.Id.Should().Be(referenceId, "the transcript is the reader's next stop");
    }

    // ── Load older messages ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void LoadOlder_AtTheTopOfTheHistory_HandsTheFocusToTheTranscript()
    {
        var cut = RenderWithoutScrollTracking(parameters => parameters
            .Add(component => component.Messages, Conversation(15))
            .Add(component => component.RenderWindowSize, 10)
            .Add(component => component.RenderWindowStep, 50));
        var transcript = TranscriptReferenceId(cut);

        cut.Find("[data-testid='ai-conversation-load-older']").Click();

        cut.FindAll("[data-testid='ai-conversation-load-older']").Should().BeEmpty("the whole history is rendered");
        ShouldHaveFocusedTheTranscriptLast(cut, transcript);
    }

    /// <summary>The button stays in the DOM while older turns remain, so its focus is never taken away.</summary>
    [Fact]
    public void LoadOlder_WithTurnsStillOlder_KeepsTheFocusOnTheButton()
    {
        var cut = RenderWithoutScrollTracking(parameters => parameters
            .Add(component => component.Messages, Conversation(100))
            .Add(component => component.RenderWindowSize, 20)
            .Add(component => component.RenderWindowStep, 10));

        cut.Find("[data-testid='ai-conversation-load-older']").Click();

        JSInterop.Invocations.Should().NotContain(invocation => invocation.Identifier == FocusInvocation,
            "a control that is still rendered keeps the focus the reader put on it");
    }

    // ── The floating pills ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StopPill_HandsTheFocusToTheTranscript()
    {
        var message = Pending();
        var cut = RenderWithoutScrollTracking(parameters => parameters.Add(component => component.Messages, new List<AiMessage> { message }));
        var transcript = TranscriptReferenceId(cut);

        var parked = new TaskCompletionSource();
        var streaming = cut.InvokeAsync(() => cut.Instance.StreamAsync(message, Parked(parked)));
        await parked.Task;

        cut.Find("[data-testid='ai-conversation-stop']").Click();
        await streaming;

        cut.FindAll("[data-testid='ai-conversation-stop']").Should().BeEmpty();
        ShouldHaveFocusedTheTranscriptLast(cut, transcript);
    }

    /// <summary>A host that stops the stream itself keeps the reader's focus: nothing disappeared under it.</summary>
    [Fact]
    public async Task StopCalledByTheHost_DoesNotMoveTheFocus()
    {
        var message = Pending();
        var cut = RenderWithoutScrollTracking(parameters => parameters.Add(component => component.Messages, new List<AiMessage> { message }));

        var parked = new TaskCompletionSource();
        var streaming = cut.InvokeAsync(() => cut.Instance.StreamAsync(message, Parked(parked)));
        await parked.Task;

        await cut.InvokeAsync(() => cut.Instance.StopAsync());
        await streaming;

        JSInterop.Invocations.Should().NotContain(invocation => invocation.Identifier == FocusInvocation);
    }

    [Fact]
    public void NewMessagesPill_HandsTheFocusToTheTranscript()
    {
        var module = JSInterop.SetupModule(TmAiConversationTests.InteropModulePath);
        module.SetupVoid("attach", _ => true).SetVoidResult();
        module.SetupVoid("detach", _ => true).SetVoidResult();
        module.Setup<bool>("isNearBottom", _ => true).SetResult(false);
        module.SetupVoid("scrollToBottom", _ => true).SetVoidResult();

        var messages = Conversation(2);
        var cut = Render<TmAiConversation>(parameters => parameters.Add(component => component.Messages, messages));
        // Read before the interaction: a render that follows clears the serialized attribute, while the
        // element reference the component holds stays the same.
        var transcript = TranscriptReferenceId(cut);

        messages.Add(TmAiConversationTests.Message("m-2"));
        cut.Render(parameters => parameters.Add(component => component.Messages, messages));

        cut.Find("[data-testid='ai-conversation-new-messages']").Click();

        cut.FindAll("[data-testid='ai-conversation-new-messages']").Should().BeEmpty("the pill dismissed itself");
        ShouldHaveFocusedTheTranscriptLast(cut, transcript);
    }

    // ── The transcript itself ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// A scroll container that cannot be tabbed into locks a keyboard-only reader out of every earlier turn
    /// (WCAG 2.1.1): the transcript is a labelled, focusable region with a visible focus ring.
    /// </summary>
    [Fact]
    public void Transcript_IsAFocusableLabelledRegion()
    {
        var cut = RenderWithoutScrollTracking(parameters => parameters.Add(component => component.Messages, Conversation(2)));

        var log = cut.Find("[role='log']");
        log.GetAttribute("tabindex").Should().Be("0");
        log.GetAttribute("aria-label").Should().Be("Conversation");
    }
}
