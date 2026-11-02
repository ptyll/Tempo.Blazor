using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using NSubstitute;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Abstractions.WorkItems;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>
/// TB8: the task panel already raises <c>OnCommentAdded</c> for task comments, so the 2.10
/// comment surface is forwarded. The panel posts the entry once; the composer flag captured by
/// <c>OnCommentSubmitted</c> lands on <see cref="TmCommentEntry.IsInternal"/>.
/// </summary>
public class TmGanttTaskPanelCommentPassthroughTests : LocalizationTestBase
{
    private static TmWorkItem TaskWithComment() => new()
    {
        Id = "t1",
        Title = "Task",
        Start = new DateTime(2024, 1, 1),
        End = new DateTime(2024, 1, 5),
        Comments =
        [
            new TmCommentEntry
            {
                Id = "c1",
                ThreadId = "t1",
                Author = new TmUserRef { DisplayName = "Ada" },
                Body = "<p>Hi</p>",
                BodyFormat = TmCommentBodyFormat.Html,
                CreatedAt = DateTimeOffset.UtcNow
            }
        ]
    };

    [Fact]
    public void TaskPanel_CommentPassthrough_DefaultsOff()
    {
        var cut = Render<TmGanttTaskPanel>(p => p.Add(x => x.Task, TaskWithComment()));
        var comments = cut.FindComponent<Tempo.Blazor.Components.Activity.TmActivityComments>();

        comments.Instance.SupportsMentions.Should().BeFalse();
        comments.Instance.MentionProvider.Should().BeNull();
        comments.Instance.ShowInternalToggle.Should().BeFalse();
        comments.Instance.CommentActions.Should().BeNull();
        cut.FindAll(".tm-comment-actions__custom").Should().BeEmpty();
    }

    [Fact]
    public void TaskPanel_Forwards_SupportsMentions_And_MentionProvider()
    {
        var provider = Substitute.For<ITmPeopleProvider>();
        var cut = Render<TmGanttTaskPanel>(p => p
            .Add(x => x.Task, TaskWithComment())
            .Add(x => x.SupportsMentions, true)
            .Add(x => x.MentionProvider, provider));

        var comments = cut.FindComponent<Tempo.Blazor.Components.Activity.TmActivityComments>();
        comments.Instance.SupportsMentions.Should().BeTrue();
        comments.Instance.MentionProvider.Should().BeSameAs(provider);
    }

    [Fact]
    public void TaskPanel_Forwards_ShowInternalToggle()
    {
        var cut = Render<TmGanttTaskPanel>(p => p
            .Add(x => x.Task, TaskWithComment())
            .Add(x => x.ShowInternalToggle, true));

        cut.Find(".tm-comments-add-btn").Click();
        cut.FindAll(".tm-comment-internal-toggle").Should().NotBeEmpty();
    }

    [Fact]
    public void TaskPanel_Forwards_CommentActions()
    {
        RenderFragment<TmCommentEntry> actions = _ => b => b.AddMarkupContent(0, "<button type=\"button\" class=\"host-visibility\">Toggle</button>");
        var cut = Render<TmGanttTaskPanel>(p => p
            .Add(x => x.Task, TaskWithComment())
            .Add(x => x.CommentActions, actions));

        cut.Find(".tm-comment-actions__custom .host-visibility").Should().NotBeNull();
    }

    [Fact]
    public async Task TaskPanel_PostsOnce_WithComposerInternalFlag()
    {
        var received = new List<TmCommentEntry>();
        var cut = Render<TmGanttTaskPanel>(p => p
            .Add(x => x.Task, TaskWithComment())
            .Add(x => x.ShowInternalToggle, true)
            .Add(x => x.OnCommentAdded, EventCallback.Factory.Create<TmCommentEntry>(this, received.Add)));

        cut.Find(".tm-comments-add-btn").Click();
        cut.Find(".tm-comment-internal-toggle input").Change(true);
        var comments = cut.FindComponent<Tempo.Blazor.Components.Activity.TmActivityComments>();
        await cut.InvokeAsync(() => comments.Instance.SetEditorContentForTestAsync("<p>Secret</p>"));
        cut.Find(".tm-comment-submit-btn").Click();

        received.Should().ContainSingle();
        received[0].IsInternal.Should().BeTrue();
        received[0].Body.Should().Contain("Secret");
    }

    [Fact]
    public async Task TaskPanel_PublicPost_StaysPublic()
    {
        var received = new List<TmCommentEntry>();
        var cut = Render<TmGanttTaskPanel>(p => p
            .Add(x => x.Task, TaskWithComment())
            .Add(x => x.OnCommentAdded, EventCallback.Factory.Create<TmCommentEntry>(this, received.Add)));

        cut.Find(".tm-comments-add-btn").Click();
        var comments = cut.FindComponent<Tempo.Blazor.Components.Activity.TmActivityComments>();
        await cut.InvokeAsync(() => comments.Instance.SetEditorContentForTestAsync("<p>Public</p>"));
        cut.Find(".tm-comment-submit-btn").Click();

        received.Should().ContainSingle();
        received[0].IsInternal.Should().BeFalse();
    }
}
