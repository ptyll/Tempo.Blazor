using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Components.Overlay;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.AI;


/// <summary>
/// bUnit contract of the KEYBOARD FOCUS around the message's two popovers (UX review round on
/// Fáze 3). Two failures it closes: a <c>role="dialog"</c> popover the reader's focus never enters is
/// unreachable with the keyboard (WCAG 2.4.3 / 2.1.1), and a popover closed PROGRAMMATICALLY has no
/// dismissal for overlay.js to restore focus from, so focus used to die on <c>&lt;body&gt;</c>.
/// </summary>
/// <remarks>
/// The focus target is identified by the element reference id bUnit stamps on the rendered node. It is
/// read BEFORE the interaction: a render that follows the popover opening clears the serialized
/// attribute (the reference itself is unchanged), which is why <c>ShouldBeElementReferenceTo</c> cannot
/// be used on a freshly re-found element here.
/// </remarks>
public sealed class TmAiMessageFocusTests : LocalizationTestBase
{
    private const string FocusInvocation = "Blazor._internal.domWrapper.focus";

    internal static AiMessage Assistant(string markdown = "An answer.")
    {
        var message = new AiMessage
        {
            Id = "assistant-1",
            Role = AiMessageRole.Assistant,
            Status = AiMessageStatus.Completed,
        };
        message.Parts.Add(new AiTextPart(markdown));
        return message;
    }

    /// <summary>A turn whose answer cites <c>s1</c>, so a citation chip and its source exist.</summary>
    internal static AiMessage Cited()
    {
        var message = Assistant("Answer [1](cite:s1).");
        message.Parts.Add(new AiSourcesPart([TmAiMessageTests.Source("s1")]));
        return message;
    }

    private BunitJSModuleInterop SetupInterop()
    {
        var module = JSInterop.SetupModule(TmAiMessageTests.InteropModulePath);
        module.Setup<bool>("isSpeechSupported", _ => true).SetResult(false);
        module.Setup<bool>("copyText", _ => true).SetResult(true);
        module.Setup<bool>("speak", _ => true).SetResult(false);
        module.SetupVoid("stopSpeaking", _ => true).SetVoidResult();
        module.SetupVoid("focusCitation", _ => true).SetVoidResult();
        return module;
    }

    /// <summary>The element reference id the renderer stamped on <paramref name="element"/>.</summary>
    private static string? ReferenceId(IElement element) => element.GetAttribute("blazor:elementreference");

    private List<JSRuntimeInvocation> FocusCalls()
        => JSInterop.Invocations.Where(invocation => invocation.Identifier == FocusInvocation).ToList();

    /// <summary>Asserts the LAST focus move targeted the element that carried <paramref name="expectedReferenceId"/>.</summary>
    private void ShouldHaveFocusedLast(string? expectedReferenceId)
    {
        expectedReferenceId.Should().NotBeNullOrEmpty("the fixture reads the target's reference id before the interaction");
        var focusCalls = FocusCalls();
        focusCalls.Should().NotBeEmpty("the interaction must move the focus");
        var target = focusCalls[^1].Arguments[0].Should().BeOfType<ElementReference>().Subject;
        target.Id.Should().Be(expectedReferenceId);
    }

    /// <summary>The overlay panel instance that carries <paramref name="cssClass"/>.</summary>
    private static IRenderedComponent<TmOverlayPanel> Panel(IRenderedComponent<TmAiMessage> cut, string cssClass)
        => cut.FindComponents<TmOverlayPanel>().Single(component => component.Instance.Class == cssClass);

    private static Action<ComponentParameterCollectionBuilder<TmAiMessage>> WithFeedback(AiMessage message)
        => parameters => parameters
            .Add(component => component.Message, message)
            .Add(component => component.OnFeedback, (AiFeedback _) => { });

    // ── Source preview ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SourcePreview_MovesFocusToItsCloseButton_OnOpen()
    {
        SetupInterop();
        var cut = Render<TmAiMessage>(parameters => parameters.Add(component => component.Message, Cited()));

        await cut.InvokeAsync(() => cut.FindComponent<TmMarkdownView>().Instance.HandleCitationClickAsync("s1"));

        cut.Find(".tm-ai-message__citation-preview").Should().NotBeNull();
        var focus = JSInterop.VerifyFocusAsyncInvoke();
        focus.Arguments[0].Should().BeOfType<ElementReference>(
            "the dialog's Close button takes the focus — the panel's content is read-only text");
    }

    [Fact]
    public async Task SourcePreview_CloseButton_ReturnsFocusToTheCitationChip()
    {
        var module = SetupInterop();
        var cut = Render<TmAiMessage>(parameters => parameters.Add(component => component.Message, Cited()));
        var bodyReference = ReferenceId(cut.Find(".tm-ai-message__body"));
        await cut.InvokeAsync(() => cut.FindComponent<TmMarkdownView>().Instance.HandleCitationClickAsync("s1"));

        cut.Find(".tm-ai-message__preview-close").Click();

        var restore = module.Invocations.Should().ContainSingle(item => item.Identifier == "focusCitation").Subject;
        restore.Arguments[0].Should().BeOfType<ElementReference>().Subject.Id.Should().Be(bodyReference,
            "the chip is resolved inside the message body — Blazor holds no reference to markdown-generated markup");
        restore.Arguments[1].Should().Be("s1");
    }

    [Fact]
    public async Task SourcePreview_EscapeDismissal_ReturnsFocusToTheCitationChip()
    {
        var module = SetupInterop();
        var cut = Render<TmAiMessage>(parameters => parameters.Add(component => component.Message, Cited()));
        await cut.InvokeAsync(() => cut.FindComponent<TmMarkdownView>().Instance.HandleCitationClickAsync("s1"));

        // overlay.js delivers Escape through the panel's JSInvokable dismissal path.
        var panel = Panel(cut, "tm-ai-message__preview-panel");
        await cut.InvokeAsync(() => panel.Instance.NotifyDismissedAsync("escape"));

        cut.FindAll(".tm-ai-message__citation-preview").Should().BeEmpty();
        module.Invocations.Should().ContainSingle(item => item.Identifier == "focusCitation");
    }

    [Fact]
    public async Task SourcePreview_OutsideClick_DoesNotPullFocusBack()
    {
        var module = SetupInterop();
        var cut = Render<TmAiMessage>(parameters => parameters.Add(component => component.Message, Cited()));
        await cut.InvokeAsync(() => cut.FindComponent<TmMarkdownView>().Instance.HandleCitationClickAsync("s1"));

        var panel = Panel(cut, "tm-ai-message__preview-panel");
        await cut.InvokeAsync(() => panel.Instance.NotifyDismissedAsync("outside"));

        // The reader clicked somewhere else: pulling focus back would undo that click.
        module.Invocations.Should().NotContain(item => item.Identifier == "focusCitation");
    }

    // ── Feedback reason dialog ─────────────────────────────────────────────────────────────

    [Fact]
    public void FeedbackPopover_IsAModalDialog_ThatTrapsFocus()
    {
        SetupInterop();
        var cut = Render<TmAiMessage>(WithFeedback(Assistant()));

        cut.Find("[data-tm-ai-action=\"feedback-down\"]").Click();

        var panel = cut.Find(".tm-ai-message__feedback-panel");
        panel.GetAttribute("role").Should().Be("dialog");
        cut.WaitForAssertion(() => panel.GetAttribute("aria-modal").Should().Be("true",
            "the reason form is a real dialog: Tab must not walk out of it into the page"));
    }

    [Fact]
    public void FeedbackPopover_MovesFocusIntoTheReasonInput()
    {
        SetupInterop();
        var cut = Render<TmAiMessage>(WithFeedback(Assistant()));

        cut.Find("[data-tm-ai-action=\"feedback-down\"]").Click();

        var focus = JSInterop.VerifyFocusAsyncInvoke();
        focus.Arguments[0].Should().BeOfType<ElementReference>(
            "the reason form opens on its textarea, the one thing it asks for");
    }

    [Fact]
    public void FeedbackCancel_ReturnsFocusToTheThumbsDown_AndMovesTheRovingTabStop()
    {
        SetupInterop();
        var cut = Render<TmAiMessage>(WithFeedback(Assistant()));
        var downReference = ReferenceId(cut.Find("[data-tm-ai-action=\"feedback-down\"]"));
        cut.Find("[data-tm-ai-action=\"feedback-down\"]").Click();

        cut.Find(".tm-ai-message__feedback-cancel").Click();

        ShouldHaveFocusedLast(downReference);
        cut.Find("[data-tm-ai-action=\"feedback-down\"]").GetAttribute("tabindex").Should().Be("0",
            "the tab stop has to follow the focus the close just took");
        cut.Find("[data-tm-ai-action=\"copy\"]").GetAttribute("tabindex").Should().Be("-1");
    }

    [Fact]
    public void FeedbackSubmit_ReturnsFocusToTheThumbsDown_AndMovesTheRovingTabStop()
    {
        SetupInterop();
        var cut = Render<TmAiMessage>(WithFeedback(Assistant()));
        var downReference = ReferenceId(cut.Find("[data-tm-ai-action=\"feedback-down\"]"));
        cut.Find("[data-tm-ai-action=\"feedback-down\"]").Click();

        cut.Find(".tm-ai-message__feedback-input").Change("Wrong section.");
        cut.Find(".tm-ai-message__feedback-submit").Click();

        ShouldHaveFocusedLast(downReference);
        cut.Find("[data-tm-ai-action=\"feedback-down\"]").GetAttribute("tabindex").Should().Be("0");
    }

    [Fact]
    public async Task FeedbackEscapeDismissal_LeavesTheRovingTabStopOnTheThumbsDown()
    {
        SetupInterop();
        var cut = Render<TmAiMessage>(WithFeedback(Assistant()));
        cut.Find("[data-tm-ai-action=\"feedback-down\"]").Click();

        // The focus trap restores the opener on deactivation; the roving tab stop must follow.
        var panel = Panel(cut, "tm-ai-message__feedback-panel");
        await cut.InvokeAsync(() => panel.Instance.NotifyDismissedAsync("escape"));

        cut.FindAll(".tm-ai-message__feedback").Should().BeEmpty();
        cut.Find("[data-tm-ai-action=\"feedback-down\"]").GetAttribute("tabindex").Should().Be("0");
        cut.Find("[data-tm-ai-action=\"copy\"]").GetAttribute("tabindex").Should().Be("-1");
    }
}
