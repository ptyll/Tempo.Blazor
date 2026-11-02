using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.AI;


/// <summary>
/// bUnit contract of the RENDERING half of <see cref="TmAiMessage"/>: which turn the bubble belongs to,
/// the avatar slot, the ordered part rendering, the citation preview, the source cards, the follow-up
/// chips and the three non-happy lifecycle states. The action bar has its own file
/// (<c>TmAiMessageActionsTests</c>) because it carries the roving-tabindex contract.
/// </summary>
public sealed class TmAiMessageTests : LocalizationTestBase
{
    internal const string InteropModulePath = "./_content/Tempo.Blazor.AI/js/ai-message.js";

    internal static AiSource Source(string id, string? url = null) => new()
    {
        Id = id,
        Title = $"Source {id}",
        Url = url ?? $"https://example.test/{id}",
        Domain = "example.test",
        Snippet = $"Snippet of {id}.",
    };

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

    internal BunitJSModuleInterop SetupInterop(bool speechSupported = false, bool copySucceeds = true)
    {
        var module = JSInterop.SetupModule(InteropModulePath);
        module.Setup<bool>("isSpeechSupported", _ => true).SetResult(speechSupported);
        module.Setup<bool>("copyText", _ => true).SetResult(copySucceeds);
        module.Setup<bool>("speak", _ => true).SetResult(speechSupported);
        module.SetupVoid("stopSpeaking", _ => true).SetVoidResult();
        return module;
    }

    [Fact]
    public void AssistantMessage_RendersTheAssistantModifier()
    {
        var cut = Render<TmAiMessage>(parameters => parameters.Add(message => message.Message, Assistant()));

        var root = cut.Find(".tm-ai-message");
        root.ClassList.Should().Contain("tm-ai-message--assistant");
        root.GetAttribute("data-role").Should().Be("assistant");
        root.GetAttribute("data-testid").Should().Be("ai-message");
    }

    [Fact]
    public void UserMessage_RendersTheUserModifier_SoTheBubbleAlignsToTheOtherSide()
    {
        var message = new AiMessage(AiMessageRole.User, "What is the refund window?")
        {
            Status = AiMessageStatus.Completed,
        };

        var cut = Render<TmAiMessage>(parameters => parameters.Add(component => component.Message, message));

        cut.Find(".tm-ai-message").ClassList.Should().Contain("tm-ai-message--user");
        cut.Find(".tm-ai-message").GetAttribute("data-role").Should().Be("user");
    }

    [Theory]
    [InlineData(AiMessageRole.User, "user")]
    [InlineData(AiMessageRole.Assistant, "assistant")]
    [InlineData(AiMessageRole.System, "system")]
    [InlineData(AiMessageRole.Tool, "tool")]
    public void EveryRole_MapsToItsOwnModifier(AiMessageRole role, string expected)
    {
        var message = new AiMessage { Role = role, Status = AiMessageStatus.Completed };
        message.Parts.Add(new AiTextPart("text"));

        var cut = Render<TmAiMessage>(parameters => parameters.Add(component => component.Message, message));

        cut.Find(".tm-ai-message").ClassList.Should().Contain($"tm-ai-message--{expected}");
    }

    [Fact]
    public void DefaultAvatar_IsDecorative_AndTheHostTemplateReplacesIt()
    {
        var cut = Render<TmAiMessage>(parameters => parameters.Add(message => message.Message, Assistant()));

        var avatar = cut.Find(".tm-ai-message__avatar");
        avatar.GetAttribute("aria-hidden").Should().Be("true");
        avatar.QuerySelector("svg").Should().NotBeNull("the default avatar is the role glyph");

        var templated = Render<TmAiMessage>(parameters => parameters
            .Add(message => message.Message, Assistant())
            .Add(message => message.AvatarTemplate, (RenderFragment)(builder => builder.AddMarkupContent(0, "<span class=\"host-avatar\">AI</span>"))));

        templated.Find(".tm-ai-message__avatar .host-avatar").Should().NotBeNull();
        templated.Find(".tm-ai-message__avatar").GetAttribute("aria-hidden").Should().Be("true");
    }

    [Fact]
    public void ShowAvatarFalse_RendersNoAvatarSlot()
    {
        var cut = Render<TmAiMessage>(parameters => parameters
            .Add(message => message.Message, Assistant())
            .Add(message => message.ShowAvatar, false));

        cut.FindAll(".tm-ai-message__avatar").Should().BeEmpty();
    }

    [Fact]
    public void Body_RendersThroughTheSafeMarkdownView()
    {
        var cut = Render<TmAiMessage>(parameters => parameters
            .Add(message => message.Message, Assistant("## Heading\n\n- one\n- two\n\n<script>alert(1)</script>")));

        var body = cut.Find(".tm-ai-message__markdown");
        body.ClassList.Should().Contain("tm-markdown-view");
        body.QuerySelector("h2")!.TextContent.Should().Be("Heading");
        body.QuerySelectorAll("li").Select(item => item.TextContent).Should().Equal("one", "two");
        cut.FindAll("script").Should().BeEmpty("model output is untrusted and never becomes markup");
    }

    [Fact]
    public void Parts_RenderInArrivalOrder()
    {
        var message = Assistant("first");
        message.Parts.Add(new AiSourcesPart([Source("a")]));
        message.Parts.Add(new AiTextPart("second"));

        var cut = Render<TmAiMessage>(parameters => parameters.Add(component => component.Message, message));

        cut.FindAll(".tm-ai-message__body > *")
            .Select(element => element.ClassList.Contains("tm-ai-message__sources") ? "sources" : "text")
            .Should().Equal("text", "sources", "text");
    }

    [Fact]
    public void ReasonAndToolCallParts_AreSummarizedInsteadOfDropped()
    {
        var message = Assistant("answer");
        message.Parts.Insert(0, new AiReasoningPart(["Checked the policy."]));
        message.Parts.Add(new AiToolCallPart(new AiToolCall
        {
            Id = "call-1",
            Name = "search",
            DisplayName = "Search",
            Status = AiToolCallStatus.Succeeded,
        }));

        var cut = Render<TmAiMessage>(parameters => parameters.Add(component => component.Message, message));

        // The reasoning part is the package's own panel now, so its steps live behind the disclosure.
        cut.Find(".tm-ai-reasoning").TextContent.Should().Contain("Thinking");
        cut.Find(".tm-ai-reasoning__toggle").Click();
        cut.Find(".tm-ai-reasoning").TextContent.Should().Contain("Checked the policy.");

        cut.Find(".tm-tool-call").TextContent.Should().Contain("Search");
        cut.Find(".tm-tool-call").GetAttribute("data-status").Should().Be("succeeded");
    }

    [Fact]
    public void ConsecutiveToolCallParts_AreGroupedIntoOneCollapsibleGroup()
    {
        var message = Assistant("answer");
        for (var index = 0; index < 3; index++)
        {
            message.Parts.Add(new AiToolCallPart(new AiToolCall
            {
                Id = $"call-{index}",
                Name = "search",
                DisplayName = "Search",
                Status = AiToolCallStatus.Succeeded,
                StartedAt = new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero),
                CompletedAt = new DateTimeOffset(2026, 10, 31, 12, 0, 1, TimeSpan.Zero),
            }));
        }

        var cut = Render<TmAiMessage>(parameters => parameters.Add(component => component.Message, message));

        cut.FindAll(".tm-tool-call-group").Should().ContainSingle(
            "three consecutive calls belong to one summary instead of three stacked rows");
        cut.FindAll(".tm-tool-call").Should().BeEmpty("the group starts collapsed");

        cut.Find(".tm-tool-call-group__toggle").Click();

        cut.FindAll(".tm-tool-call").Should().HaveCount(3);
    }

    [Fact]
    public void ACallBrokenUpByText_IsNotGroupedAcrossTheText()
    {
        var message = Assistant("first");
        message.Parts.Add(new AiToolCallPart(new AiToolCall
        {
            Id = "call-1",
            Name = "search",
            DisplayName = "Search",
            Status = AiToolCallStatus.Succeeded,
        }));
        message.Parts.Add(new AiTextPart("second"));
        message.Parts.Add(new AiToolCallPart(new AiToolCall
        {
            Id = "call-2",
            Name = "fetch",
            DisplayName = "Fetch",
            Status = AiToolCallStatus.Succeeded,
        }));

        var cut = Render<TmAiMessage>(parameters => parameters.Add(component => component.Message, message));

        cut.FindAll(".tm-tool-call-group").Should().BeEmpty(
            "a single call renders as its own card; grouping it would claim a run that does not exist");
        cut.FindAll(".tm-tool-call").Should().HaveCount(2);
    }

    [Fact]
    public void ToolCallRetry_IsRaisedWithTheCallThatFailed()
    {
        var failed = new AiToolCall
        {
            Id = "call-1",
            Name = "search",
            DisplayName = "Search",
            Status = AiToolCallStatus.Failed,
        };
        var message = Assistant("answer");
        message.Parts.Add(new AiToolCallPart(failed));
        AiToolCall? retried = null;

        var cut = Render<TmAiMessage>(parameters => parameters
            .Add(component => component.Message, message)
            .Add(component => component.OnToolCallRetry, (AiToolCall call) => retried = call));

        cut.Find(".tm-tool-call__retry").Click();

        retried.Should().BeSameAs(failed);
    }

    [Fact]
    public void AReasoningPartOfAStreamingTurn_KeepsItsLiveTimer()
    {
        var message = new AiMessage
        {
            Id = "assistant-1",
            Role = AiMessageRole.Assistant,
            Status = AiMessageStatus.Streaming,
        };
        message.Parts.Add(new AiReasoningPart(["Looking at the policy."]));

        var cut = Render<TmAiMessage>(parameters => parameters.Add(component => component.Message, message));

        cut.Find(".tm-ai-reasoning__summary").ClassList.Should().Contain("tm-ai-shimmer",
            "the reasoning is still arriving, so the panel keeps its live state");
        cut.Find(".tm-ai-reasoning__summary").TextContent.Should().Contain("Thinking for");
    }

    [Fact]
    public async Task CitationChip_RaisesOnCitationClick_AndOpensTheSourcePreview()
    {
        var message = Assistant("Answer [1](cite:s1).");
        message.Parts.Add(new AiSourcesPart([Source("s1")]));
        string? cited = null;

        var cut = Render<TmAiMessage>(parameters => parameters
            .Add(component => component.Message, message)
            .Add(component => component.OnCitationClick, (string id) => cited = id));

        cut.FindAll(".tm-ai-message__citation-preview").Should().BeEmpty("the preview opens on demand");

        var view = cut.FindComponent<TmMarkdownView>();
        await cut.InvokeAsync(() => view.Instance.HandleCitationClickAsync("s1"));

        cited.Should().Be("s1");
        var preview = cut.Find(".tm-ai-message__citation-preview");
        preview.QuerySelector(".tm-ai-message__preview-title")!.TextContent.Should().Be("Source s1");
        preview.QuerySelector(".tm-ai-message__preview-domain")!.TextContent.Should().Be("example.test");
        preview.QuerySelector(".tm-ai-message__preview-snippet")!.TextContent.Should().Be("Snippet of s1.");
    }

    [Fact]
    public async Task SourcePreview_LinkIsHardenedAndARefusedSchemeNeverBecomesAnHref()
    {
        var message = Assistant("Answer [1](cite:bad).");
        message.Parts.Add(new AiSourcesPart([Source("bad", "javascript:alert(1)")]));

        var cut = Render<TmAiMessage>(parameters => parameters.Add(component => component.Message, message));
        await cut.InvokeAsync(() => cut.FindComponent<TmMarkdownView>().Instance.HandleCitationClickAsync("bad"));

        var preview = cut.Find(".tm-ai-message__citation-preview");
        preview.QuerySelector(".tm-ai-message__preview-link").Should().BeNull(
            "a refused scheme is rendered as plain text, never as a link");
        cut.Markup.Should().NotContainEquivalentOf("javascript:");
    }

    [Fact]
    public async Task SourcePreview_AllowedLinkOpensInANewHardenedTab()
    {
        var message = Assistant("Answer [1](cite:s1).");
        message.Parts.Add(new AiSourcesPart([Source("s1")]));

        var cut = Render<TmAiMessage>(parameters => parameters.Add(component => component.Message, message));
        await cut.InvokeAsync(() => cut.FindComponent<TmMarkdownView>().Instance.HandleCitationClickAsync("s1"));

        var link = cut.Find(".tm-ai-message__citation-preview .tm-ai-message__preview-link");
        link.GetAttribute("href").Should().Be("https://example.test/s1");
        link.GetAttribute("target").Should().Be("_blank");
        link.GetAttribute("rel").Should().Be("noopener noreferrer nofollow");
        link.GetAttribute("aria-label").Should().Be("Open source");
    }

    [Fact]
    public async Task SourcePreview_CanBeClosed_AndThenRendersNothing()
    {
        var message = Assistant("Answer [1](cite:s1).");
        message.Parts.Add(new AiSourcesPart([Source("s1")]));

        var cut = Render<TmAiMessage>(parameters => parameters.Add(component => component.Message, message));
        await cut.InvokeAsync(() => cut.FindComponent<TmMarkdownView>().Instance.HandleCitationClickAsync("s1"));

        cut.Find(".tm-ai-message__preview-close").Click();

        cut.FindAll(".tm-ai-message__citation-preview").Should().BeEmpty();
    }

    [Fact]
    public void CitationForAnUnknownSource_OpensNoPreview()
    {
        var message = Assistant("Answer [9](cite:missing).");

        var cut = Render<TmAiMessage>(parameters => parameters.Add(component => component.Message, message));

        cut.FindAll(".tm-ai-message__citation-preview").Should().BeEmpty();
    }

    [Fact]
    public void SourceCards_RenderTitleDomainSnippetAndIndex()
    {
        var message = Assistant("Answer [1](cite:a) [2](cite:b).");
        message.Parts.Add(new AiSourcesPart([Source("a"), Source("b")]));

        var cut = Render<TmAiMessage>(parameters => parameters.Add(component => component.Message, message));

        var sources = cut.Find(".tm-ai-message__sources");
        sources.GetAttribute("aria-label").Should().Be("Sources");
        var cards = sources.QuerySelectorAll(".tm-ai-message__source").ToList();
        cards.Should().HaveCount(2);
        cards[0].QuerySelector(".tm-ai-message__source-index")!.TextContent.Should().Be("1");
        cards[0].QuerySelector(".tm-ai-message__source-title")!.TextContent.Should().Be("Source a");
        cards[0].QuerySelector(".tm-ai-message__source-domain")!.TextContent.Should().Be("example.test");
        cards[0].QuerySelector(".tm-ai-message__source-snippet")!.TextContent.Should().Be("Snippet of a.");
        cards[1].QuerySelector(".tm-ai-message__source-index")!.TextContent.Should().Be("2");
    }

    [Fact]
    public void ShowSourcesFalse_HidesTheSourceCards()
    {
        var message = Assistant("Answer [1](cite:a).");
        message.Parts.Add(new AiSourcesPart([Source("a")]));

        var cut = Render<TmAiMessage>(parameters => parameters
            .Add(component => component.Message, message)
            .Add(component => component.ShowSources, false));

        cut.FindAll(".tm-ai-message__sources").Should().BeEmpty();
    }

    [Fact]
    public void FollowUpChips_RaiseOnFollowUp_WithTheSuggestion()
    {
        var message = Assistant();
        message.FollowUps.Add("Why?");
        message.FollowUps.Add("Show the source.");
        string? picked = null;

        var cut = Render<TmAiMessage>(parameters => parameters
            .Add(component => component.Message, message)
            .Add(component => component.OnFollowUp, (string text) => picked = text));

        var chips = cut.FindAll(".tm-ai-message__follow-up");
        chips.Select(chip => chip.TextContent).Should().Equal("Why?", "Show the source.");

        chips[1].Click();
        picked.Should().Be("Show the source.");
    }

    [Fact]
    public void ShowFollowUpsFalse_HidesTheChips()
    {
        var message = Assistant();
        message.FollowUps.Add("Why?");

        var cut = Render<TmAiMessage>(parameters => parameters
            .Add(component => component.Message, message)
            .Add(component => component.ShowFollowUps, false));

        cut.FindAll(".tm-ai-message__follow-ups").Should().BeEmpty();
    }

    [Fact]
    public void StreamingMessage_MarksTheBodyBusy_AndStreamsWithTheCursor()
    {
        var message = Assistant("Partial answ");
        message.Status = AiMessageStatus.Streaming;

        var cut = Render<TmAiMessage>(parameters => parameters.Add(component => component.Message, message));

        cut.Find(".tm-ai-message").GetAttribute("data-status").Should().Be("streaming");
        cut.Find(".tm-ai-message__body").GetAttribute("aria-busy").Should().Be("true");
        cut.Find(".tm-ai-message__markdown").ClassList.Should().Contain("tm-ai-streaming");
    }

    [Fact]
    public void EmptyStreamingMessage_StillShowsTheStreamingCursor()
    {
        var message = new AiMessage { Id = "a", Role = AiMessageRole.Assistant, Status = AiMessageStatus.Streaming };

        var cut = Render<TmAiMessage>(parameters => parameters.Add(component => component.Message, message));

        var pending = cut.Find(".tm-ai-message__pending");
        pending.ClassList.Should().Contain("tm-ai-streaming");
        pending.TextContent.Should().Contain("Generating an answer");
    }

    [Fact]
    public void CompletedMessage_DoesNotClaimToBeBusyNorStream()
    {
        var cut = Render<TmAiMessage>(parameters => parameters.Add(message => message.Message, Assistant()));

        cut.Find(".tm-ai-message__body").GetAttribute("aria-busy").Should().BeNull();
        cut.Find(".tm-ai-message__markdown").ClassList.Should().NotContain("tm-ai-streaming");
    }

    [Fact]
    public void StoppedMessage_ShowsTheLocalizedLabel()
    {
        var message = Assistant("Half an answ");
        message.Status = AiMessageStatus.Stopped;

        var cut = Render<TmAiMessage>(parameters => parameters.Add(component => component.Message, message));

        cut.Find(".tm-ai-message").GetAttribute("data-status").Should().Be("stopped");
        cut.Find(".tm-ai-message__state").TextContent.Should().Be("Generation stopped");
        cut.FindAll(".tm-ai-message__retry").Should().BeEmpty();
    }

    [Fact]
    public void StreamingToStopped_AnnouncesTheStopInTheLiveRegion()
    {
        var message = Assistant("Half an answ");
        message.Status = AiMessageStatus.Streaming;

        var cut = Render<TmAiMessage>(parameters => parameters.Add(component => component.Message, message));
        cut.Find("[data-tm-ai-live]").TextContent.Should().BeEmpty("nothing has been announced yet");

        message.Status = AiMessageStatus.Stopped;
        cut.Render(parameters => parameters.Add(component => component.Message, message));

        // A reader waiting on a streaming answer is not touching the page: the visible label alone
        // is never read out. One live region per message carries the change.
        cut.Find("[data-tm-ai-live]").TextContent.Should().Be("Generation stopped");
    }

    [Fact]
    public void AlreadyStoppedMessage_DoesNotReplayTheAnnouncementOnFirstRender()
    {
        var message = Assistant("Half an answ");
        message.Status = AiMessageStatus.Stopped;

        var cut = Render<TmAiMessage>(parameters => parameters.Add(component => component.Message, message));

        cut.Find(".tm-ai-message__state").TextContent.Should().Be("Generation stopped");
        cut.Find("[data-tm-ai-live]").TextContent.Should().BeEmpty(
            "the reader never witnessed this stop, so there is nothing to announce");
    }

    [Fact]
    public void FailedMessage_KeepsTheActionBarNextToTheErrorBlock()
    {
        var message = Assistant(string.Empty);
        message.Status = AiMessageStatus.Failed;
        message.Error = "The model timed out.";

        var cut = Render<TmAiMessage>(parameters => parameters
            .Add(component => component.Message, message)
            .Add(component => component.OnRetry, () => { }));

        cut.Find(".tm-ai-message__error").Should().NotBeNull();
        cut.Find("[data-tm-ai-action=\"copy\"]").Should().NotBeNull(
            "a failed turn keeps its action bar next to the error block: a partial answer is still worth copying");
    }

    [Fact]
    public void FailedMessage_ShowsTheError_AndRetryRaisesOnRetry()
    {
        var message = Assistant(string.Empty);
        message.Status = AiMessageStatus.Failed;
        message.Error = "The model timed out.";
        var retried = false;

        var cut = Render<TmAiMessage>(parameters => parameters
            .Add(component => component.Message, message)
            .Add(component => component.OnRetry, () => retried = true));

        cut.Find(".tm-ai-message").GetAttribute("data-status").Should().Be("failed");
        var error = cut.Find(".tm-ai-message__error");
        error.TextContent.Should().Contain("The model timed out.");
        error.GetAttribute("role").Should().Be("alert");
        error.QuerySelectorAll("svg").Should().OnlyContain(icon => icon.GetAttribute("aria-hidden") == "true",
            "the alert and retry glyphs are decorative — the alert text and the Retry label carry the meaning");

        cut.Find(".tm-ai-message__retry").Click();
        retried.Should().BeTrue();
    }

    [Fact]
    public void FailedMessage_WithoutAnErrorText_StillRendersTheLocalizedFallback()
    {
        var message = new AiMessage { Id = "a", Role = AiMessageRole.Assistant, Status = AiMessageStatus.Failed };

        var cut = Render<TmAiMessage>(parameters => parameters
            .Add(component => component.Message, message)
            .Add(component => component.OnRetry, () => { }));

        cut.Find(".tm-ai-message__error").TextContent.Should().Contain("The answer could not be generated");
    }

    [Fact]
    public void HostClassAndAttributes_LandOnTheRoot()
    {
        var cut = Render<TmAiMessage>(parameters => parameters
            .Add(message => message.Message, Assistant())
            .Add(message => message.Class, "answer-row")
            .Add(message => message.DataTestId, "first-answer")
            .AddUnmatched("data-sticky", "true"));

        var root = cut.Find(".tm-ai-message");
        root.ClassList.Should().Contain("answer-row");
        root.GetAttribute("data-testid").Should().Be("first-answer");
        root.GetAttribute("data-sticky").Should().Be("true");
    }

    [Fact]
    public void NoMessage_RendersAnEmptyShellInsteadOfThrowing()
    {
        var cut = Render<TmAiMessage>(parameters => parameters.Add(message => message.Message, null));

        cut.Find(".tm-ai-message").Should().NotBeNull();
        cut.Find(".tm-ai-message__body").InnerHtml.Trim().Should().BeEmpty();
    }
}
