using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using NSubstitute;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.Activity;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Activity;

/// <summary>
/// TB8: TmActivityLog already owns the comment callbacks, so the 2.10 comment surface is forwarded.
/// Defaults stay off — a host that sets none of the new parameters renders the pre-2.10 comments tab.
/// </summary>
public class TmActivityLogCommentPassthroughTests : LocalizationTestBase
{
    private static TmCommentEntry Comment() => new()
    {
        Id = "c1",
        ThreadId = "t1",
        Author = new TmUserRef { DisplayName = "Ada" },
        Body = "<p>Hi</p>",
        BodyFormat = TmCommentBodyFormat.Html,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private IRenderedComponent<TmActivityLog> OpenComments(
        Action<ComponentParameterCollectionBuilder<TmActivityLog>> configure)
        => Render<TmActivityLog>((ComponentParameterCollectionBuilder<TmActivityLog> p) =>
        {
            p.Add(c => c.ShowTimeline, false);
            p.Add(c => c.Comments, (IReadOnlyList<TmCommentEntry>)[Comment()]);
            configure(p);
        });

    [Fact]
    public void ActivityLog_CommentPassthrough_DefaultsOff()
    {
        var cut = OpenComments(_ => { });
        var comments = cut.FindComponent<TmActivityComments>();

        comments.Instance.SupportsMentions.Should().BeFalse();
        comments.Instance.MentionProvider.Should().BeNull();
        comments.Instance.ShowInternalToggle.Should().BeFalse();
        comments.Instance.CommentActions.Should().BeNull();
        comments.Instance.OnCommentSubmitted.HasDelegate.Should().BeFalse();
        cut.FindAll(".tm-comment-internal-toggle").Should().BeEmpty();
        cut.FindAll(".tm-comment-actions__custom").Should().BeEmpty();
    }

    [Fact]
    public void ActivityLog_Forwards_SupportsMentions_And_MentionProvider()
    {
        var provider = Substitute.For<ITmPeopleProvider>();
        var cut = OpenComments(p => p
            .Add(c => c.SupportsMentions, true)
            .Add(c => c.MentionProvider, provider));

        var comments = cut.FindComponent<TmActivityComments>();
        comments.Instance.SupportsMentions.Should().BeTrue();
        comments.Instance.MentionProvider.Should().BeSameAs(provider);
    }

    [Fact]
    public void ActivityLog_Forwards_ShowInternalToggle()
    {
        var cut = OpenComments(p => p.Add(c => c.ShowInternalToggle, true));

        cut.Find(".tm-comments-add-btn").Click();

        cut.FindComponent<TmActivityComments>().Instance.ShowInternalToggle.Should().BeTrue();
        cut.FindAll(".tm-comment-internal-toggle").Should().NotBeEmpty();
    }

    [Fact]
    public void ActivityLog_Forwards_CommentActions()
    {
        RenderFragment<TmCommentEntry> actions = _ => b => b.AddMarkupContent(0, "<button type=\"button\" class=\"host-visibility\">Toggle</button>");
        var cut = OpenComments(p => p.Add(c => c.CommentActions, actions));

        cut.Find(".tm-comment-actions__custom .host-visibility").Should().NotBeNull();
    }

    [Fact]
    public async Task ActivityLog_Forwards_OnCommentSubmitted_Before_OnCommentAdded()
    {
        var order = new List<string>();
        var cut = OpenComments(p => p
            .Add(c => c.ShowInternalToggle, true)
            .Add(c => c.OnCommentSubmitted, EventCallback.Factory.Create<TmCommentSubmission>(this, s =>
            {
                order.Add($"submitted:{s.IsInternal}");
            }))
            .Add(c => c.OnCommentAdded, EventCallback.Factory.Create<string>(this, _ => order.Add("added"))));

        cut.Find(".tm-comments-add-btn").Click();
        cut.Find(".tm-comment-internal-toggle input").Change(true);
        await cut.InvokeAsync(() => cut.FindComponent<TmActivityComments>().Instance.SetEditorContentForTestAsync("<p>Secret</p>"));
        cut.Find(".tm-comment-submit-btn").Click();

        order.Should().Equal("submitted:True", "added");
    }
}
