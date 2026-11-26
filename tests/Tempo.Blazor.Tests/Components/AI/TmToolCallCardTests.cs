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
/// bUnit contract of <see cref="TmToolCallCard"/> (plan 7f21f26b, Fáze 4): the status icon plus the
/// localized status text, the live elapsed timer (driven by the injected <see cref="TimeProvider"/>, so
/// a fake clock decides when it ticks), the disclosure that carries <c>aria-expanded</c> /
/// <c>aria-controls</c> and pretty-prints both JSON payloads, the redacted call that shows a localized
/// notice instead of data, Copy through the package's ES module, Retry only for a failed call, and
/// exactly ONE live region for the whole card.
/// </summary>
public sealed class TmToolCallCardTests : LocalizationTestBase
{
    internal const string InteropModulePath = "./_content/Tempo.Blazor.AI/js/ai-message.js";

    private static readonly DateTimeOffset Start = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A call in <paramref name="status"/>. Everything but a queued call gets a start; a
    /// <paramref name="durationSeconds"/> additionally records the completion, which is the only way a
    /// card can show a final duration.
    /// </summary>
    internal static AiToolCall Call(
        AiToolCallStatus status = AiToolCallStatus.Succeeded,
        string? arguments = null,
        string? result = null,
        double? durationSeconds = null,
        string name = "search",
        string? displayName = "Search")
    {
        var call = new AiToolCall
        {
            Id = "call-1",
            Name = name,
            DisplayName = displayName,
            Status = status,
            ArgumentsJson = arguments,
            ResultJson = result,
        };

        if (status != AiToolCallStatus.Queued)
        {
            call.StartedAt = Start;
        }

        if (durationSeconds is { } duration)
        {
            call.CompletedAt = Start.AddSeconds(duration);
        }

        return call;
    }

    private FakeTimeProvider UseFakeClock(DateTimeOffset? now = null)
    {
        var time = now is { } value ? new FakeTimeProvider(value) : new FakeTimeProvider(Start);
        Services.AddSingleton<TimeProvider>(time);
        return time;
    }

    /// <summary>Drains renderer-dispatched work queued by a timer callback.</summary>
    private static Task DrainAsync<TComponent>(IRenderedComponent<TComponent> cut)
        where TComponent : IComponent
        => cut.InvokeAsync(() => { });

    private BunitJSModuleInterop SetupInterop(bool copySucceeds = true)
    {
        var module = JSInterop.SetupModule(InteropModulePath);
        module.Setup<bool>("copyText", _ => true).SetResult(copySucceeds);
        return module;
    }

    private static string LiveRegion(IRenderedComponent<TmToolCallCard> cut)
        => cut.Find("[role='status']").TextContent.Trim();

    [Theory]
    [InlineData(AiToolCallStatus.Queued, "queued", "Queued")]
    [InlineData(AiToolCallStatus.Running, "running", "Running")]
    [InlineData(AiToolCallStatus.Succeeded, "succeeded", "Done")]
    [InlineData(AiToolCallStatus.Failed, "failed", "Failed")]
    [InlineData(AiToolCallStatus.Cancelled, "cancelled", "Cancelled")]
    public void EveryStatus_CarriesItsOwnModifierAnIconAndTheLocalizedText(
        AiToolCallStatus status,
        string modifier,
        string expectedLabel)
    {
        UseFakeClock();
        var cut = Render<TmToolCallCard>(parameters => parameters.Add(card => card.ToolCall, Call(status)));

        var root = cut.Find(".tm-tool-call");
        root.ClassList.Should().Contain($"tm-tool-call--{modifier}");
        root.GetAttribute("data-status").Should().Be(modifier);
        root.GetAttribute("data-testid").Should().Be("tool-call");

        var icon = cut.Find(".tm-tool-call__status-icon");
        icon.GetAttribute("aria-hidden").Should().Be("true", "the icon decorates the text that follows it");
        icon.QuerySelector("svg, .tm-icon-unknown").Should().NotBeNull("every status has its own glyph");

        cut.Find(".tm-tool-call__status").TextContent.Trim().Should().Be(expectedLabel);
    }

    [Fact]
    public void QueuedCallWithoutAStart_ShowsNoDuration()
    {
        UseFakeClock();
        var cut = Render<TmToolCallCard>(parameters => parameters
            .Add(card => card.ToolCall, Call(AiToolCallStatus.Queued)));

        cut.FindAll(".tm-tool-call__duration").Should().BeEmpty(
            "a call that has not started has no elapsed time to report");
    }

    [Fact]
    public async Task RunningCall_TicksOnTheInjectedClock()
    {
        var time = UseFakeClock();
        var cut = Render<TmToolCallCard>(parameters => parameters
            .Add(card => card.ToolCall, Call(AiToolCallStatus.Running)));

        cut.Find(".tm-tool-call__duration").TextContent.Trim().Should().Be("0.0 s");

        time.Advance(TimeSpan.FromSeconds(3));
        await DrainAsync(cut);

        cut.Find(".tm-tool-call__duration").TextContent.Trim().Should().Be("3.0 s",
            "a running call shows how long it has been running");
    }

    [Fact]
    public async Task CompletedCall_StopsTheTimerAndKeepsTheRecordedDuration()
    {
        var time = UseFakeClock();
        var cut = Render<TmToolCallCard>(parameters => parameters
            .Add(card => card.ToolCall, Call(AiToolCallStatus.Running)));

        time.Advance(TimeSpan.FromSeconds(2));
        await DrainAsync(cut);
        cut.Find(".tm-tool-call__duration").TextContent.Trim().Should().Be("2.0 s");

        // The host reports the completion on the SAME call instance (that is how the stream applier does it);
        // the card switches to the recorded duration and disarms its timer.
        var call = cut.Instance.ToolCall!;
        await cut.InvokeAsync(() =>
        {
            call.CompletedAt = call.StartedAt!.Value.AddSeconds(4.2);
            call.Status = AiToolCallStatus.Succeeded;
        });
        cut.Render(parameters => parameters.Add(card => card.ToolCall, call));

        cut.Find(".tm-tool-call__duration").TextContent.Trim().Should().Be("4.2 s");
        cut.Find(".tm-tool-call").GetAttribute("data-status").Should().Be("succeeded");

        time.Advance(TimeSpan.FromSeconds(10));
        await DrainAsync(cut);

        cut.Find(".tm-tool-call__duration").TextContent.Trim().Should().Be("4.2 s",
            "a finished call never keeps ticking — its duration is a fact, not an estimate");
    }

    [Fact]
    public void Toggle_IsADisclosure_WithAriaExpandedAndAriaControls()
    {
        UseFakeClock();
        var cut = Render<TmToolCallCard>(parameters => parameters
            .Add(card => card.ToolCall, Call(arguments: """{"query":"refunds"}""")));

        var toggle = cut.Find(".tm-tool-call__toggle");
        toggle.GetAttribute("aria-expanded").Should().Be("false");
        toggle.GetAttribute("aria-controls").Should().BeNull(
            "a collapsed disclosure renders no region, so the id would point at nothing");
        cut.FindAll(".tm-tool-call__details").Should().BeEmpty("a collapsed disclosure renders no region");

        toggle.Click();

        cut.Find(".tm-tool-call__toggle").GetAttribute("aria-expanded").Should().Be("true");
        var detailsId = cut.Find(".tm-tool-call__toggle").GetAttribute("aria-controls");
        detailsId.Should().NotBeNullOrWhiteSpace("an open disclosure names the region it owns");
        cut.Find($"[id='{detailsId}']").ClassList.Should().Contain("tm-tool-call__details");

        cut.Find(".tm-tool-call__toggle").Click();
        cut.Find(".tm-tool-call__toggle").GetAttribute("aria-expanded").Should().Be("false");
        cut.Find(".tm-tool-call__toggle").GetAttribute("aria-controls").Should().BeNull();
        cut.FindAll(".tm-tool-call__details").Should().BeEmpty();
    }

    [Fact]
    public void DefaultExpanded_OpensTheDetailsOnFirstRender()
    {
        UseFakeClock();
        var cut = Render<TmToolCallCard>(parameters => parameters
            .Add(card => card.ToolCall, Call(arguments: """{"a":1}"""))
            .Add(card => card.DefaultExpanded, true));

        cut.Find(".tm-tool-call__toggle").GetAttribute("aria-expanded").Should().Be("true");
        cut.Find(".tm-tool-call__details").Should().NotBeNull();
    }

    [Fact]
    public void ArgumentsAndResult_ArePrettyPrintedJson()
    {
        UseFakeClock();
        var cut = Render<TmToolCallCard>(parameters => parameters
            .Add(card => card.ToolCall, Call(
                arguments: """{"query":"refunds","limit":5}""",
                result: """[1,2]"""))
            .Add(card => card.DefaultExpanded, true));

        var arguments = cut.Find(".tm-tool-call__arguments code").TextContent;
        arguments.Should().Contain("\n", "pretty-printed JSON is multi-line");
        arguments.Should().Contain("  \"query\": \"refunds\"",
            "the writer indents with two spaces and keeps the property name intact");

        var result = cut.Find(".tm-tool-call__result code").TextContent;
        result.Should().Contain("\n").And.Contain("  1").And.Contain("  2");
    }

    [Fact]
    public void InvalidJson_IsShownAsPlainText_InsteadOfBeingDropped()
    {
        UseFakeClock();
        var cut = Render<TmToolCallCard>(parameters => parameters
            .Add(card => card.ToolCall, Call(arguments: "not-json { at all", result: "42"))
            .Add(card => card.DefaultExpanded, true));

        cut.Find(".tm-tool-call__arguments code").TextContent.Should().Be("not-json { at all");
        cut.Find(".tm-tool-call__result code").TextContent.Should().Be("42",
            "a bare JSON scalar is valid JSON and stays readable");
    }

    [Fact]
    public void RedactedCall_ShowsTheLocalizedNotice_InsteadOfAnyData()
    {
        UseFakeClock();
        var call = Call(arguments: """{"secret":"token"}""", result: """{"ok":true}""");
        call.IsRedacted = true;

        var cut = Render<TmToolCallCard>(parameters => parameters
            .Add(card => card.ToolCall, call)
            .Add(card => card.DefaultExpanded, true));

        cut.Find(".tm-tool-call__redacted").TextContent.Trim().Should().Be("Details hidden");
        cut.Find(".tm-tool-call__redacted").QuerySelector("svg")
            .Should().NotBeNull("the notice is marked as a withheld payload, not as ordinary prose");
        cut.FindAll(".tm-tool-call__details").Should().BeEmpty();
        cut.FindAll(".tm-tool-call__toggle").Should().BeEmpty("there is nothing to disclose");
        cut.FindAll("code").Should().BeEmpty();
        cut.FindAll(".tm-tool-call__copy").Should().BeEmpty(
            "a redacted call must not offer a path that takes the hidden payload out of the page");
        cut.Markup.Should().NotContain("token");
    }

    [Fact]
    public void Copy_WritesTheDetailsThroughTheInteropModule_AndAnnouncesIt()
    {
        UseFakeClock();
        var module = SetupInterop();
        var cut = Render<TmToolCallCard>(parameters => parameters
            .Add(card => card.ToolCall, Call(arguments: """{"query":"refunds"}""", result: """{"hits":2}""")));

        cut.Find(".tm-tool-call__copy").Click();

        var invocation = module.VerifyInvoke("copyText");
        var text = invocation.Arguments[0] as string;
        text.Should().NotBeNull();
        text.Should().Contain("\"query\": \"refunds\"").And.Contain("\"hits\": 2");

        LiveRegion(cut).Should().Be("Copied");
        cut.Find(".tm-tool-call__copy").GetAttribute("data-copied").Should().Be("true");
    }

    [Fact]
    public void Copy_DoesNotClaimSuccess_WhenTheClipboardWriteIsRefused()
    {
        UseFakeClock();
        SetupInterop(copySucceeds: false);
        var cut = Render<TmToolCallCard>(parameters => parameters
            .Add(card => card.ToolCall, Call(arguments: """{"a":1}""")));

        cut.Find(".tm-tool-call__copy").Click();

        LiveRegion(cut).Should().BeEmpty();
        cut.Find(".tm-tool-call__copy").GetAttribute("data-copied").Should().BeNull();
    }

    [Fact]
    public void Retry_IsOfferedOnlyForAFailedCall_ThatHasAHandler()
    {
        UseFakeClock();
        var calls = 0;

        var running = Render<TmToolCallCard>(parameters => parameters
            .Add(card => card.ToolCall, Call(AiToolCallStatus.Running))
            .Add(card => card.OnRetry, () => calls++));
        running.FindAll(".tm-tool-call__retry").Should().BeEmpty("a running call has nothing to retry");

        var failedWithoutHandler = Render<TmToolCallCard>(parameters => parameters
            .Add(card => card.ToolCall, Call(AiToolCallStatus.Failed)));
        failedWithoutHandler.FindAll(".tm-tool-call__retry").Should().BeEmpty(
            "a button that does nothing is worse than no button");

        var failed = Render<TmToolCallCard>(parameters => parameters
            .Add(card => card.ToolCall, Call(AiToolCallStatus.Failed))
            .Add(card => card.OnRetry, () => calls++));
        var retry = failed.Find(".tm-tool-call__retry");
        retry.TextContent.Should().Contain("Retry");
        retry.Click();
        calls.Should().Be(1);
    }

    /// <summary>
    /// A host re-running a call is asynchronous: a second activation lands while the first retry is still
    /// in flight and would fire <c>OnRetry</c> twice. The button therefore marks itself
    /// <c>aria-disabled="true"</c> (and the handler refuses re-entry) until the host's task completes.
    /// It deliberately does NOT use the native <c>disabled</c> attribute: a natively disabled button
    /// leaves the tab order and the browser moves focus to <c>body</c>, so a reader who activated the
    /// retry from the keyboard would lose their place the moment the re-run starts (WCAG 2.4.3 — the
    /// same choice the autonomy dial's step buttons and the scheduled-runs day step make, decision
    /// <c>AI-PACKAGE-DISABLED-IDIOM</c>). The attribute is omitted again (not left as <c>"false"</c>)
    /// once the host finishes, matching the card's other state attributes (<c>data-copied</c>): an
    /// absent attribute carries no state for CSS or AT to trip over. The start of the re-run is
    /// announced through the card's single live region; a "retrying" note the host never replaced with
    /// a status announcement is cleared when the task ends, so it cannot linger as a stale claim.
    /// </summary>
    [Fact]
    public async Task Retry_InFlight_StaysFocusableWithAriaDisabled_AndRefusesReentry()
    {
        UseFakeClock();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        var cut = Render<TmToolCallCard>(parameters => parameters
            .Add(card => card.ToolCall, Call(AiToolCallStatus.Failed))
            .Add(card => card.OnRetry, () => { calls++; return release.Task; }));

        cut.Find(".tm-tool-call__retry").HasAttribute("aria-disabled").Should().BeFalse(
            "an idle retry carries no disabled state");
        cut.Find(".tm-tool-call__retry").HasAttribute("disabled").Should().BeFalse(
            "a failed call with a handler offers an enabled Retry");

        cut.Find(".tm-tool-call__retry").Click();

        var inFlight = cut.Find(".tm-tool-call__retry");
        inFlight.GetAttribute("aria-disabled").Should().Be("true",
            "the in-flight retry announces itself disabled while the host's task runs");
        inFlight.HasAttribute("disabled").Should().BeFalse(
            "native disabled would drop the focus the reader just placed on the button (WCAG 2.4.3)");
        LiveRegion(cut).Should().Be("Retrying Search…",
            "the start of the re-run is announced through the card's single live region");

        cut.Find(".tm-tool-call__retry").Click();
        calls.Should().Be(1, "one reader action means one retry — the handler refuses re-entry");

        release.SetResult();
        await cut.InvokeAsync(() => { });

        var settled = cut.Find(".tm-tool-call__retry");
        settled.HasAttribute("aria-disabled").Should().BeFalse(
            "the host finished; the state attribute is omitted again rather than left as a stale \"false\"");
        settled.HasAttribute("disabled").Should().BeFalse();
        LiveRegion(cut).Should().BeEmpty(
            "the host never replaced the retrying note with a status announcement, so the card clears " +
            "it when the task ends — a stale \"Retrying…\" must not linger in the region");
    }

    /// <summary>
    /// A retry the reader activated from the keyboard is FOCUSED when the re-run starts. While the host's
    /// task runs the button stays in the tab order (aria-disabled, fáze 4d), but a successful re-run turns
    /// the call succeeded and the Retry renders only for a failed call — the focused button leaves the DOM
    /// and the browser would drop the focus to <c>body</c>. The card hands it to the disclosure toggle
    /// instead (WCAG 2.4.3): the failed call auto-expanded its details, so the toggle is the control the
    /// reader most plausibly wants next. Mutant guard: removing the focus hand-off from
    /// <c>OnAfterRenderAsync</c> leaves no <c>domWrapper.focus</c> invocation and turns this red.
    /// </summary>
    [Fact]
    public async Task Retry_Completion_HandsTheFocusToTheDisclosure_WhenTheButtonDisappears()
    {
        UseFakeClock();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var call = Call(AiToolCallStatus.Failed, arguments: """{"a":1}""");

        var cut = Render<TmToolCallCard>(parameters => parameters
            .Add(card => card.ToolCall, call)
            .Add(card => card.OnRetry, () => release.Task));

        var toggleId = cut.Find(".tm-tool-call__toggle").GetAttribute("blazor:elementReference");
        toggleId.Should().NotBeNullOrEmpty("the disclosure toggle is the focus-hand-off target");

        cut.Find(".tm-tool-call__retry").Focus();
        cut.Find(".tm-tool-call__retry").Click();
        LiveRegion(cut).Should().Be("Retrying Search…", "the start of the re-run is announced");

        // The host's re-run succeeds on the SAME call instance: the Retry disappears with the failure.
        call.Status = AiToolCallStatus.Succeeded;
        call.Error = null;
        call.ResultJson = """{"ok":true}""";
        release.SetResult();
        await cut.InvokeAsync(() => { });
        cut.Render(parameters => parameters.Add(card => card.ToolCall, call));

        cut.FindAll(".tm-tool-call__retry").Should().BeEmpty("a succeeded call offers no retry");
        LiveRegion(cut).Should().Be("Search: Done",
            "the status transition takes the live region over from the retrying note");
        var focus = JSInterop.Invocations.Last(invocation => invocation.Identifier == "Blazor._internal.domWrapper.focus");
        ((ElementReference)focus.Arguments[0]).Id.Should().Be(toggleId,
            "the focused Retry disappeared with the failure — the card hands the focus to the disclosure toggle");
    }

    /// <summary>
    /// The focus hand-off after a completed retry walks the card's controls in order — disclosure toggle,
    /// Copy button, and finally the CARD ITSELF. A failed call with no payloads and no error text offers
    /// neither the disclosure nor Copy, so the root (which carries <c>tabindex="-1"</c> exactly for this:
    /// programmatically focusable, never a tab stop) takes the focus instead of <c>body</c>.
    /// </summary>
    [Fact]
    public async Task Retry_Completion_WithoutAnyOtherControl_HandsTheFocusToTheCard()
    {
        UseFakeClock();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var call = Call(AiToolCallStatus.Failed);

        var cut = Render<TmToolCallCard>(parameters => parameters
            .Add(card => card.ToolCall, call)
            .Add(card => card.OnRetry, () => release.Task));

        var rootId = cut.Find(".tm-tool-call").GetAttribute("blazor:elementReference");
        rootId.Should().NotBeNullOrEmpty("the card root is the focus fallback (tabindex=-1)");

        cut.Find(".tm-tool-call__retry").Focus();
        cut.Find(".tm-tool-call__retry").Click();

        call.Status = AiToolCallStatus.Succeeded;
        release.SetResult();
        await cut.InvokeAsync(() => { });
        cut.Render(parameters => parameters.Add(card => card.ToolCall, call));

        cut.FindAll(".tm-tool-call__toggle").Should().BeEmpty("no payloads and no error — nothing to expand");
        cut.FindAll(".tm-tool-call__copy").Should().BeEmpty("nothing to copy either");
        var root = cut.Find(".tm-tool-call");
        root.GetAttribute("tabindex").Should().Be("-1",
            "the root is the programmatic focus fallback — never a tab stop");
        var focus = JSInterop.Invocations.Last(invocation => invocation.Identifier == "Blazor._internal.domWrapper.focus");
        ((ElementReference)focus.Arguments[0]).Id.Should().Be(rootId,
            "no toggle and no copy — the card itself takes the focus instead of body");
    }

    /// <summary>
    /// A reader who TABS AWAY from the in-flight retry keeps their new place: the focus hand-off fires
    /// only while the focus is still on the Retry when it disappears, never as a focus steal from the
    /// control the reader moved to.
    /// </summary>
    [Fact]
    public async Task Retry_Completion_DoesNotStealAFocusTheReaderMovedAway()
    {
        UseFakeClock();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var call = Call(AiToolCallStatus.Failed, arguments: """{"a":1}""");

        var cut = Render<TmToolCallCard>(parameters => parameters
            .Add(card => card.ToolCall, call)
            .Add(card => card.OnRetry, () => release.Task));

        cut.Find(".tm-tool-call__retry").Focus();
        cut.Find(".tm-tool-call__retry").Click();
        cut.Find(".tm-tool-call__retry").Blur();

        call.Status = AiToolCallStatus.Succeeded;
        release.SetResult();
        await cut.InvokeAsync(() => { });
        cut.Render(parameters => parameters.Add(card => card.ToolCall, call));

        JSInterop.Invocations.Should().NotContain(
            invocation => invocation.Identifier == "Blazor._internal.domWrapper.focus",
            "the reader moved the focus away before the re-run completed — the card must not grab it back");
    }

    /// <summary>
    /// Chrome fires a blur when the focused button is REMOVED by the successful re-run — and that blur
    /// arrives after the status already flipped (<see cref="TmToolCallCard"/> can retry no more). It is
    /// the browser's reaction to the removal, not the reader moving on, so it must NOT cancel the focus
    /// hand-off (found by the fáze-4d WASM E2E: the removal-blur reset the tracking and the focus fell
    /// to body). A blur while the retry is still rendered (the reader tabs away mid flight) DOES cancel
    /// it — see <see cref="Retry_Completion_DoesNotStealAFocusTheReaderMovedAway"/>.
    /// </summary>
    [Fact]
    public async Task Retry_Completion_TheRemovalBlur_DoesNotCancelTheHandOff()
    {
        UseFakeClock();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var call = Call(AiToolCallStatus.Failed, arguments: """{"a":1}""");

        var cut = Render<TmToolCallCard>(parameters => parameters
            .Add(card => card.ToolCall, call)
            .Add(card => card.OnRetry, () => release.Task));

        var toggleId = cut.Find(".tm-tool-call__toggle").GetAttribute("blazor:elementReference");
        cut.Find(".tm-tool-call__retry").Focus();
        cut.Find(".tm-tool-call__retry").Click();

        // The host's re-run flips the status FIRST; the removal-blur arrives while the retry is no
        // longer retryable — exactly the browser's order when the button leaves the DOM.
        call.Status = AiToolCallStatus.Succeeded;
        call.Error = null;
        call.ResultJson = """{"ok":true}""";
        cut.Find(".tm-tool-call__retry").Blur();

        release.SetResult();
        await cut.InvokeAsync(() => { });
        cut.Render(parameters => parameters.Add(card => card.ToolCall, call));

        var focus = JSInterop.Invocations.Last(invocation => invocation.Identifier == "Blazor._internal.domWrapper.focus");
        ((ElementReference)focus.Arguments[0]).Id.Should().Be(toggleId,
            "the removal-blur is not the reader leaving — the hand-off to the disclosure toggle still fires");
    }

    /// <summary>
    /// Inside a group every card repeats the same three buttons, so the accessible NAME has to carry the
    /// tool name — a screen reader's list of buttons would otherwise read "Copy tool call, Copy tool call,
    /// …" and the reader could not tell which call a button belongs to (WCAG 2.4.6).
    /// </summary>
    [Fact]
    public void TheActionNames_CarryTheToolName()
    {
        UseFakeClock();
        var call = Call(AiToolCallStatus.Failed, arguments: """{"a":1}""", name: "search", displayName: "Web search");

        var cut = Render<TmToolCallCard>(parameters => parameters
            .Add(card => card.ToolCall, call)
            .Add(card => card.OnRetry, () => { }));

        cut.Find(".tm-tool-call__copy").GetAttribute("aria-label").Should().Be("Copy details of Web search");
        cut.Find(".tm-tool-call__retry").GetAttribute("aria-label").Should().Be("Retry Web search");
        cut.Find(".tm-tool-call__toggle").GetAttribute("aria-label").Should().Be("Details of Web search");
        cut.Find(".tm-tool-call__retry").TextContent.Trim().Should().Be("Retry",
            "the visible label stays the short verb");
    }

    /// <summary>
    /// A streaming call fills its payload late: the formatted block has to reach the already-open disclosure
    /// without a re-mount. The card caches the formatting on the payload strings, so this also guards the
    /// cache's invalidation.
    /// </summary>
    [Fact]
    public async Task APayloadThatArrivesAfterTheFirstRender_ReachesTheOpenDisclosure()
    {
        UseFakeClock();
        var call = Call(AiToolCallStatus.Running);
        var cut = Render<TmToolCallCard>(parameters => parameters
            .Add(card => card.ToolCall, call)
            .Add(card => card.DefaultExpanded, true));

        cut.FindAll(".tm-tool-call__arguments").Should().BeEmpty("the call has no arguments yet");

        await cut.InvokeAsync(() => call.ArgumentsJson = """{"q":"late"}""");
        cut.Render(parameters => parameters.Add(card => card.ToolCall, call));

        cut.Find(".tm-tool-call__arguments code").TextContent.Should().Contain("\"q\": \"late\"");
        cut.Find(".tm-tool-call__toggle").GetAttribute("aria-expanded").Should().Be("true",
            "a payload arriving is not a reason to collapse the row");

        await cut.InvokeAsync(() => call.ArgumentsJson = """{"q":"later"}""");
        cut.Render(parameters => parameters.Add(card => card.ToolCall, call));

        cut.Find(".tm-tool-call__arguments code").TextContent.Should().Contain("\"q\": \"later\"",
            "the cache keys on the payload, so a changed payload is re-formatted");
    }

    [Fact]
    public void FailedCall_ShowsTheErrorMessage()
    {
        UseFakeClock();
        var call = Call(AiToolCallStatus.Failed);
        call.Error = "The index is unreachable.";

        var cut = Render<TmToolCallCard>(parameters => parameters
            .Add(card => card.ToolCall, call)
            .Add(card => card.DefaultExpanded, true));

        cut.Find(".tm-tool-call__error").TextContent.Should().Contain("The index is unreachable.");
        cut.FindAll("[role='status']").Count.Should().Be(1,
            "the failure is announced by the card's single live region, not by a second alert region");
    }

    [Fact]
    public void FailedCall_ExposesErrorCodeOnTheRoot()
    {
        UseFakeClock();
        var call = Call(AiToolCallStatus.Failed);
        call.Error = "The index is unreachable.";
        call.ErrorCode = "index.unreachable";

        var cut = Render<TmToolCallCard>(parameters => parameters
            .Add(card => card.ToolCall, call)
            .Add(card => card.DefaultExpanded, true));

        cut.Find(".tm-tool-call").GetAttribute("data-error-code").Should().Be("index.unreachable");
    }

    [Fact]
    public void SucceededCall_DoesNotExposeErrorCodeEvenWhenSet()
    {
        UseFakeClock();
        var call = Call(AiToolCallStatus.Succeeded);
        call.ErrorCode = "should-not-leak";

        var cut = Render<TmToolCallCard>(parameters => parameters.Add(card => card.ToolCall, call));

        cut.Find(".tm-tool-call").GetAttribute("data-error-code").Should().BeNull(
            "data-error-code is only for Failed; a leftover code on a succeeded call must not leak");
    }

    [Fact]
    public void ErrorTemplate_ReplacesTheDefaultErrorText_AndKeepsTheErrorLabel()
    {
        UseFakeClock();
        var call = Call(AiToolCallStatus.Failed);
        call.Error = "The index is unreachable.";
        call.ErrorCode = "index.unreachable";

        var cut = Render<TmToolCallCard>(parameters => parameters
            .Add(card => card.ToolCall, call)
            .Add(card => card.DefaultExpanded, true)
            .Add(card => card.ErrorTemplate, (AiToolCall value) => builder =>
            {
                builder.OpenElement(0, "span");
                builder.AddAttribute(1, "class", "host-error");
                builder.AddAttribute(2, "data-testid", "host-error");
                builder.AddContent(3, $"{value.ErrorCode}: {value.Error}");
                builder.CloseElement();
            }));

        cut.Find(".tm-tool-call__error-label").TextContent.Trim().Should().Be("Error",
            "Tempo still owns the Error caption; the template only replaces the text");
        cut.Find("[data-testid='host-error']").TextContent.Should().Be("index.unreachable: The index is unreachable.");
        cut.FindAll(".tm-tool-call__error-text").Should().BeEmpty(
            "ErrorTemplate replaces the default error text, not sits next to it");
    }

    [Fact]
    public void WithoutErrorTemplate_DefaultErrorMarkupIsUnchanged()
    {
        UseFakeClock();
        var call = Call(AiToolCallStatus.Failed);
        call.Error = "The index is unreachable.";

        var cut = Render<TmToolCallCard>(parameters => parameters
            .Add(card => card.ToolCall, call)
            .Add(card => card.DefaultExpanded, true));

        var error = cut.Find(".tm-tool-call__error");
        error.Children.Should().HaveCount(2, "label + default text; a template must not appear by default");
        cut.Find(".tm-tool-call__error-text").TextContent.Should().Be("The index is unreachable.");
        cut.Find(".tm-tool-call").GetAttribute("data-error-code").Should().BeNull(
            "no code on the model means no data-error-code attribute");
    }

    [Fact]
    public async Task AStatusChange_IsAnnouncedThroughTheCardsSingleLiveRegion()
    {
        UseFakeClock();
        var cut = Render<TmToolCallCard>(parameters => parameters
            .Add(card => card.ToolCall, Call(AiToolCallStatus.Running)));

        cut.FindAll("[role='status']").Count.Should().Be(1, "one card owns exactly one live region");
        LiveRegion(cut).Should().BeEmpty("the first render is already on screen; it is not a change");

        // The stream applier mutates the SAME call instance; a new instance would be a new row.
        var call = cut.Instance.ToolCall!;
        await cut.InvokeAsync(() =>
        {
            call.CompletedAt = call.StartedAt!.Value.AddSeconds(2);
            call.Status = AiToolCallStatus.Succeeded;
        });
        cut.Render(parameters => parameters.Add(card => card.ToolCall, call));

        LiveRegion(cut).Should().Be("Search: Done");
        cut.Find(".tm-tool-call__duration").TextContent.Trim().Should().Be("2.0 s");
        cut.Find(".tm-tool-call").GetAttribute("data-status").Should().Be("succeeded");
    }

    [Fact]
    public void NullToolCall_RendersNothing()
    {
        UseFakeClock();
        var cut = Render<TmToolCallCard>(parameters => parameters.Add(card => card.ToolCall, null));

        cut.FindAll(".tm-tool-call").Should().BeEmpty();
    }

    [Fact]
    public void TheStatusTextAndTheCopyName_AreLocalized()
    {
        UseCzechLocalization();
        UseFakeClock();

        var cut = Render<TmToolCallCard>(parameters => parameters
            .Add(card => card.ToolCall, Call(AiToolCallStatus.Running, arguments: """{"a":1}""")));

        cut.Find(".tm-tool-call__status").TextContent.Trim().Should().Be("Probíhá");
        cut.Find(".tm-tool-call__copy").GetAttribute("aria-label").Should().Be("Kopírovat podrobnosti nástroje Search");
        cut.Find(".tm-tool-call__toggle").GetAttribute("aria-label").Should().Be("Podrobnosti nástroje Search");
    }
}
