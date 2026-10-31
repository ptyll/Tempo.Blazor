using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.AI;


/// <summary>
/// bUnit contract of the ACTION BAR of <see cref="TmAiMessage"/>: copy (plain text, not markdown),
/// regenerate, the version switcher, thumbs up/down with the optional reason popover, read-aloud behind
/// a feature detect, the host action template and the <c>role="toolbar"</c> + roving-tabindex keyboard
/// model. Clipboard and speech go through the package's ES module, so everything here is asserted on the
/// JS interop calls and on the rendered state.
/// </summary>
public sealed class TmAiMessageActionsTests : LocalizationTestBase
{
    private static AiMessage Completed(string markdown = "An answer.")
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

    private BunitJSModuleInterop SetupInterop(bool speechSupported = false, bool copySucceeds = true)
    {
        var module = JSInterop.SetupModule(TmAiMessageTests.InteropModulePath);
        module.Setup<bool>("isSpeechSupported", _ => true).SetResult(speechSupported);
        module.Setup<bool>("copyText", _ => true).SetResult(copySucceeds);
        module.Setup<bool>("speak", _ => true).SetResult(speechSupported);
        module.SetupVoid("stopSpeaking", _ => true).SetVoidResult();
        return module;
    }

    /// <summary>
    /// Asserts the LAST focus move went to <paramref name="expected"/>. Several key presses in one fact
    /// produce several focus calls, so the single-invocation helper cannot be used here.
    /// </summary>
    private void ShouldHaveFocusedLast(IElement expected)
    {
        var focusCalls = JSInterop.Invocations
            .Where(invocation => invocation.Identifier == "Blazor._internal.domWrapper.focus")
            .ToList();
        focusCalls.Should().NotBeEmpty("the keyboard move must move the focus");
        focusCalls[^1].Arguments[0].ShouldBeElementReferenceTo(expected);
    }

    /// <summary>A turn with every optional action bound, so the toolbar has a full roving set.</summary>
    private static Action<ComponentParameterCollectionBuilder<TmAiMessage>> FullActions(AiMessage message)
        => parameters => parameters
            .Add(component => component.Message, message)
            .Add(component => component.OnRegenerate, () => { })
            .Add(component => component.OnFeedback, (AiFeedback _) => { });

    // ── Toolbar contract ────────────────────────────────────────────────────────────────────

    [Fact]
    public void ActionBar_IsAToolbar_WithTheLocalizedName()
    {
        var cut = Render<TmAiMessage>(parameters => parameters.Add(message => message.Message, Completed()));

        var toolbar = cut.Find(".tm-ai-message__actions");
        toolbar.GetAttribute("role").Should().Be("toolbar");
        toolbar.GetAttribute("aria-label").Should().Be("Message actions");
        toolbar.GetAttribute("aria-orientation").Should().Be("horizontal");
    }

    [Fact]
    public void ActionBar_HasExactlyOneTabStop_AndArrowKeysMoveIt()
    {
        var cut = Render<TmAiMessage>(FullActions(Completed()));

        var buttons = cut.FindAll(".tm-ai-message__actions .tm-ai-message__action");
        buttons.Should().HaveCount(4);
        buttons[0].GetAttribute("tabindex").Should().Be("0");
        buttons.Skip(1).Should().OnlyContain(button => button.GetAttribute("tabindex") == "-1");

        cut.Find(".tm-ai-message__actions").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });

        ShouldHaveFocusedLast(buttons[1]);

        buttons = cut.FindAll(".tm-ai-message__actions .tm-ai-message__action");
        buttons[1].GetAttribute("tabindex").Should().Be("0", "the roving tab stop follows the focus");
        buttons[0].GetAttribute("tabindex").Should().Be("-1");
    }

    [Fact]
    public void ArrowLeftWraps_AndHomeEndJump()
    {
        var cut = Render<TmAiMessage>(FullActions(Completed()));
        var toolbar = cut.Find(".tm-ai-message__actions");
        var buttons = cut.FindAll(".tm-ai-message__actions .tm-ai-message__action");

        toolbar.KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });
        ShouldHaveFocusedLast(buttons[^1]);

        toolbar.KeyDown(new KeyboardEventArgs { Key = "Home" });
        ShouldHaveFocusedLast(buttons[0]);

        toolbar.KeyDown(new KeyboardEventArgs { Key = "End" });
        ShouldHaveFocusedLast(buttons[^1]);
    }

    [Fact]
    public void OtherKeys_DoNotMoveFocus()
    {
        var cut = Render<TmAiMessage>(parameters => parameters.Add(message => message.Message, Completed()));

        cut.Find(".tm-ai-message__actions").KeyDown(new KeyboardEventArgs { Key = "a" });

        JSInterop.Invocations.Should().NotContain(invocation => invocation.Identifier.Contains("domWrapper"));
    }

    [Fact]
    public void ShowActionsFalse_RendersNoActionBar()
    {
        var cut = Render<TmAiMessage>(parameters => parameters
            .Add(message => message.Message, Completed())
            .Add(message => message.ShowActions, false));

        cut.FindAll(".tm-ai-message__actions").Should().BeEmpty();
    }

    [Fact]
    public void StreamingMessage_OffersNoActions()
    {
        var message = Completed("Partial");
        message.Status = AiMessageStatus.Streaming;

        var cut = Render<TmAiMessage>(parameters => parameters.Add(component => component.Message, message));

        cut.FindAll(".tm-ai-message__actions").Should().BeEmpty(
            "there is nothing to copy, regenerate or rate while the answer is still arriving");
    }

    [Fact]
    public void ActionsTemplate_ReplacesTheBuiltInBar()
    {
        var cut = Render<TmAiMessage>(parameters => parameters
            .Add(message => message.Message, Completed())
            .Add(message => message.ActionsTemplate,
                (RenderFragment)(builder => builder.AddMarkupContent(0, "<button class=\"host-action\">Host</button>"))));

        cut.FindAll(".tm-ai-message__actions").Should().BeEmpty();
        cut.FindAll("[role=\"toolbar\"]").Should().BeEmpty("a host template owns its own keyboard model");
        cut.Find(".tm-ai-message__host-actions .host-action").Should().NotBeNull();
    }

    // ── Copy ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Copy_SendsThePlainText_WithoutMarkdownSyntax()
    {
        var module = SetupInterop();
        var cut = Render<TmAiMessage>(parameters => parameters
            .Add(message => message.Message, Completed("# Title\n\nSome **bold** [1](cite:a).")));

        cut.Find("[data-tm-ai-action=\"copy\"]").Click();

        var invocation = module.Invocations.Should().ContainSingle(item => item.Identifier == "copyText").Subject;
        invocation.Arguments[0].Should().Be("Title\n\nSome bold 1.");
    }

    [Fact]
    public void Copy_AnnouncesTheCopiedStateInASiblingLiveRegion()
    {
        SetupInterop();
        var cut = Render<TmAiMessage>(parameters => parameters
            .Add(message => message.Message, Completed()));

        cut.Find("[data-tm-ai-action=\"copy\"]").Click();

        cut.Find("[data-tm-ai-live]").TextContent.Should().Be("Copied");
        cut.Find("[data-tm-ai-action=\"copy\"]").GetAttribute("data-copied").Should().Be("true");
        cut.Find("[data-tm-ai-action=\"copy\"]").GetAttribute("aria-label").Should().Be("Copy message",
            "the confirmation travels through the live region — renaming the control for two seconds " +
            "both duplicates the announcement and loses the button's name");
    }

    [Fact]
    public void Copy_RefusedByTheClipboard_LeavesTheStateUnchanged()
    {
        SetupInterop(copySucceeds: false);
        var cut = Render<TmAiMessage>(parameters => parameters
            .Add(message => message.Message, Completed()));

        cut.Find("[data-tm-ai-action=\"copy\"]").Click();

        cut.Find("[data-tm-ai-live]").TextContent.Should().BeEmpty();
        cut.Find("[data-tm-ai-action=\"copy\"]").HasAttribute("data-copied").Should().BeFalse();
    }

    [Fact]
    public void CopyForAUserMessage_CopiesTheUserText()
    {
        var module = SetupInterop();
        var message = new AiMessage(AiMessageRole.User, "What is the refund window?")
        {
            Status = AiMessageStatus.Completed,
        };

        var cut = Render<TmAiMessage>(parameters => parameters.Add(component => component.Message, message));
        cut.Find("[data-tm-ai-action=\"copy\"]").Click();

        module.Invocations.Should().ContainSingle(item => item.Identifier == "copyText")
            .Which.Arguments[0].Should().Be("What is the refund window?");
    }

    // ── Regenerate ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Regenerate_IsOfferedOnlyWhenTheHostListens_AndRaisesOnRegenerate()
    {
        var withoutHandler = Render<TmAiMessage>(parameters => parameters.Add(message => message.Message, Completed()));
        withoutHandler.FindAll("[data-tm-ai-action=\"regenerate\"]").Should().BeEmpty();

        var regenerated = 0;
        var cut = Render<TmAiMessage>(parameters => parameters
            .Add(message => message.Message, Completed())
            .Add(message => message.OnRegenerate, () => regenerated++));

        cut.Find("[data-tm-ai-action=\"regenerate\"]").Click();
        regenerated.Should().Be(1);
    }

    // ── Versions ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SingleVersion_RendersNoSwitcher()
    {
        var cut = Render<TmAiMessage>(parameters => parameters.Add(message => message.Message, Completed()));

        cut.FindAll(".tm-ai-message__versions").Should().BeEmpty();
    }

    [Fact]
    public void VersionSwitcher_ShowsThePositionOutOfTheTotal()
    {
        var message = Completed("third");
        message.Versions.Add(new AiMessageVersion([new AiTextPart("first")]));
        message.Versions.Add(new AiMessageVersion([new AiTextPart("second")]));
        message.ActiveVersionIndex = 1;

        var cut = Render<TmAiMessage>(parameters => parameters.Add(component => component.Message, message));

        var group = cut.Find(".tm-ai-message__versions");
        group.GetAttribute("role").Should().Be("group");
        group.GetAttribute("aria-label").Should().Be("Answer versions");
        cut.Find(".tm-ai-message__version-label").TextContent.Should().Be("2/3");
        cut.Find(".tm-ai-message__version-label").GetAttribute("aria-label").Should().Be("Version 2 of 3");
        cut.Find(".tm-ai-message__body").TextContent.Should().Contain("second");
    }

    [Fact]
    public void VersionSwitcher_PrevAndNext_RaiseTheTargetIndex_AndTheEndsDisable()
    {
        var message = Completed("third");
        message.Versions.Add(new AiMessageVersion([new AiTextPart("first")]));
        message.Versions.Add(new AiMessageVersion([new AiTextPart("second")]));
        var raised = new List<int>();

        var cut = Render<TmAiMessage>(parameters => parameters
            .Add(component => component.Message, message)
            .Add(component => component.ActiveVersionIndexChanged, (int index) => raised.Add(index)));

        cut.Find("[data-tm-ai-action=\"version-next\"]").HasAttribute("disabled").Should().BeTrue(
            "a regenerated message opens on its newest version");
        cut.Find("[data-tm-ai-action=\"version-prev\"]").Click();
        cut.Find("[data-tm-ai-action=\"version-prev\"]").Click();

        raised.Should().Equal(1, 0);
        cut.Find("[data-tm-ai-action=\"version-prev\"]").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void VersionSwitcher_AtTheNewestVersion_DisablesNext()
    {
        var message = Completed("live");
        message.Versions.Add(new AiMessageVersion([new AiTextPart("old")]));
        message.ActiveVersionIndex = 1;

        var cut = Render<TmAiMessage>(parameters => parameters.Add(component => component.Message, message));

        cut.Find("[data-tm-ai-action=\"version-next\"]").HasAttribute("disabled").Should().BeTrue();
        cut.Find(".tm-ai-message__version-label").TextContent.Should().Be("2/2");
    }

    [Fact]
    public void VersionSwitcher_WithoutAHandler_StillSwitchesTheShownVersionLocally()
    {
        var message = Completed("live");
        message.Versions.Add(new AiMessageVersion([new AiTextPart("old")]));

        var cut = Render<TmAiMessage>(parameters => parameters.Add(component => component.Message, message));
        cut.Find("[data-tm-ai-action=\"version-next\"]").Click();

        cut.Find(".tm-ai-message__version-label").TextContent.Should().Be("2/2");
        cut.Find(".tm-ai-message__body").TextContent.Should().Contain("live");
    }

    [Fact]
    public void VersionSwitcher_HostWritingTheIndex_WinsOverTheReadersLastStep()
    {
        var message = Completed("live");
        message.Versions.Add(new AiMessageVersion([new AiTextPart("first")]));
        message.Versions.Add(new AiMessageVersion([new AiTextPart("second")]));

        var cut = Render<TmAiMessage>(parameters => parameters
            .Add(component => component.Message, message)
            .Add(component => component.ActiveVersionIndexChanged, (int _) => { }));

        cut.Find("[data-tm-ai-action=\"version-prev\"]").Click();
        cut.Find(".tm-ai-message__version-label").TextContent.Should().Be("2/3");

        cut.Render(parameters => parameters
            .Add(component => component.Message, message)
            .Add(component => component.ActiveVersionIndexChanged, (int _) => { }));
        cut.Find(".tm-ai-message__version-label").TextContent.Should().Be("2/3");

        message.ActiveVersionIndex = 0;
        cut.Render(parameters => parameters
            .Add(component => component.Message, message)
            .Add(component => component.ActiveVersionIndexChanged, (int _) => { }));
        cut.Find(".tm-ai-message__version-label").TextContent.Should().Be("1/3");
        cut.Find(".tm-ai-message__body").TextContent.Should().Contain("first");
    }

    // ── Feedback ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ThumbsUp_RaisesPositiveFeedback_AndStaysPressed()
    {
        AiFeedback? feedback = null;
        var cut = Render<TmAiMessage>(parameters => parameters
            .Add(message => message.Message, Completed())
            .Add(message => message.OnFeedback, (AiFeedback value) => feedback = value));

        var up = cut.Find("[data-tm-ai-action=\"feedback-up\"]");
        up.GetAttribute("aria-pressed").Should().Be("false");

        up.Click();

        feedback!.Rating.Should().Be(AiFeedbackRating.Positive);
        cut.Find("[data-tm-ai-action=\"feedback-up\"]").GetAttribute("aria-pressed").Should().Be("true");
        cut.Find("[data-tm-ai-action=\"feedback-down\"]").GetAttribute("aria-pressed").Should().Be("false");
    }

    [Fact]
    public void ThumbsDown_RaisesNegativeFeedback_AndOpensTheOptionalReason()
    {
        AiFeedback? feedback = null;
        var cut = Render<TmAiMessage>(parameters => parameters
            .Add(message => message.Message, Completed())
            .Add(message => message.OnFeedback, (AiFeedback value) => feedback = value));

        cut.Find("[data-tm-ai-action=\"feedback-down\"]").Click();

        feedback!.Rating.Should().Be(AiFeedbackRating.Negative);
        feedback.Reason.Should().BeNull();
        cut.FindAll(".tm-ai-message__feedback").Should().ContainSingle();
        cut.Find(".tm-ai-message__feedback-label").TextContent.Should().Be("What was wrong with this answer? (optional)");
        var panel = cut.Find(".tm-ai-message__feedback-panel");
        panel.GetAttribute("role").Should().Be("dialog");
        panel.GetAttribute("aria-label").Should().Be("What went wrong?");
    }

    [Fact]
    public void NegativeFeedback_ReasonIsOptional_ButReachesOnFeedbackWhenGiven()
    {
        var raised = new List<AiFeedback>();
        var cut = Render<TmAiMessage>(parameters => parameters
            .Add(message => message.Message, Completed())
            .Add(message => message.OnFeedback, (AiFeedback value) => raised.Add(value)));

        cut.Find("[data-tm-ai-action=\"feedback-down\"]").Click();
        cut.Find(".tm-ai-message__feedback-input").Change("The second paragraph is wrong.");
        cut.Find(".tm-ai-message__feedback-submit").Click();

        raised.Should().HaveCount(2);
        raised[0].Reason.Should().BeNull();
        raised[1].Rating.Should().Be(AiFeedbackRating.Negative);
        raised[1].Reason.Should().Be("The second paragraph is wrong.");
        cut.FindAll(".tm-ai-message__feedback").Should().BeEmpty("submitting closes the popover");
    }

    [Fact]
    public void NegativeFeedback_CancelKeepsTheRatingAndClosesThePopover()
    {
        var raised = new List<AiFeedback>();
        var cut = Render<TmAiMessage>(parameters => parameters
            .Add(message => message.Message, Completed())
            .Add(message => message.OnFeedback, (AiFeedback value) => raised.Add(value)));

        cut.Find("[data-tm-ai-action=\"feedback-down\"]").Click();
        cut.Find(".tm-ai-message__feedback-cancel").Click();

        raised.Should().HaveCount(1);
        cut.FindAll(".tm-ai-message__feedback").Should().BeEmpty();
        cut.Find("[data-tm-ai-action=\"feedback-down\"]").GetAttribute("aria-pressed").Should().Be("true");
    }

    [Fact]
    public void FeedbackOnTheModel_DrivesThePressedState()
    {
        var message = Completed();
        message.Feedback = new AiFeedback { Rating = AiFeedbackRating.Negative, Reason = "Wrong." };

        var cut = Render<TmAiMessage>(parameters => parameters
            .Add(component => component.Message, message)
            .Add(component => component.OnFeedback, (AiFeedback _) => { }));

        cut.Find("[data-tm-ai-action=\"feedback-down\"]").GetAttribute("aria-pressed").Should().Be("true");
        cut.Find("[data-tm-ai-action=\"feedback-up\"]").GetAttribute("aria-pressed").Should().Be("false");
    }

    [Fact]
    public void Feedback_WithoutAHandler_IsNotOffered()
    {
        var cut = Render<TmAiMessage>(parameters => parameters.Add(message => message.Message, Completed()));

        cut.FindAll("[data-tm-ai-action=\"feedback-up\"]").Should().BeEmpty();
        cut.FindAll("[data-tm-ai-action=\"feedback-down\"]").Should().BeEmpty();
    }

    // ── Read aloud ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ReadAloud_IsNotOfferedUnlessTheHostOptsIn()
    {
        SetupInterop(speechSupported: true);
        var cut = Render<TmAiMessage>(parameters => parameters.Add(message => message.Message, Completed()));

        cut.FindAll("[data-tm-ai-action=\"read-aloud\"]").Should().BeEmpty();
    }

    [Fact]
    public void ReadAloud_IsHiddenWhenTheBrowserHasNoSpeechSynthesis()
    {
        SetupInterop(speechSupported: false);
        var cut = Render<TmAiMessage>(parameters => parameters
            .Add(message => message.Message, Completed())
            .Add(message => message.ReadAloud, true));

        cut.FindAll("[data-tm-ai-action=\"read-aloud\"]").Should().BeEmpty();
    }

    [Fact]
    public void ReadAloud_SpeaksThePlainText_AndTogglesToStop()
    {
        var module = SetupInterop(speechSupported: true);
        var cut = Render<TmAiMessage>(parameters => parameters
            .Add(message => message.Message, Completed("Read **me**."))
            .Add(message => message.ReadAloud, true));

        cut.WaitForAssertion(() => cut.Find("[data-tm-ai-action=\"read-aloud\"]").GetAttribute("aria-pressed").Should().Be("false"));

        cut.Find("[data-tm-ai-action=\"read-aloud\"]").Click();

        var speak = module.Invocations.Should().ContainSingle(item => item.Identifier == "speak").Subject;
        speak.Arguments[0].Should().Be("Read me.");
        cut.Find("[data-tm-ai-action=\"read-aloud\"]").GetAttribute("aria-pressed").Should().Be("true");
        cut.Find("[data-tm-ai-action=\"read-aloud\"]").GetAttribute("aria-label").Should().Be("Stop reading");

        cut.Find("[data-tm-ai-action=\"read-aloud\"]").Click();

        module.Invocations.Should().Contain(item => item.Identifier == "stopSpeaking");
        cut.Find("[data-tm-ai-action=\"read-aloud\"]").GetAttribute("aria-pressed").Should().Be("false");
    }

    [Fact]
    public void ReadAloud_ControlCarriesAStableTestHook()
    {
        SetupInterop(speechSupported: true);
        var cut = Render<TmAiMessage>(parameters => parameters
            .Add(message => message.Message, Completed())
            .Add(message => message.ReadAloud, true));

        cut.WaitForAssertion(() => cut.Find("[data-tm-ai-action=\"read-aloud\"]").Should().NotBeNull());
        cut.Find("[data-tm-ai-action=\"read-aloud\"]").GetAttribute("aria-label").Should().Be("Read aloud");
    }
}
