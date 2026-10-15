using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.Activity;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Activity;

public class TmActivityCommentsTests : LocalizationTestBase
{
    private static List<TmCommentEntry> TwoComments() =>
    [
        Comment("c1", "Alice", "<p>First comment</p>", true, true, DateTimeOffset.Now.AddHours(-2)),
        Comment("c2", "Bob", "<p>Second comment</p>", false, false, DateTimeOffset.Now.AddHours(-1)),
    ];

    [Fact]
    public void Comments_RendersExistingComments()
    {
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments()));

        cut.FindAll(".tm-comment-item").Count.Should().Be(2);
    }

    [Fact]
    public void Comments_Empty_RendersEmptyState()
    {
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, Array.Empty<TmCommentEntry>()));

        cut.FindAll(".tm-comment-item").Should().BeEmpty();
        cut.FindAll(".tm-empty-state, .tm-comments-empty").Should().NotBeEmpty();
    }

    [Fact]
    public void Comments_ShowEditor_WhenAddCommentClicked()
    {
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments()));

        cut.Find(".tm-comments-add-btn").Click();

        cut.FindAll(".tm-comment-editor").Should().NotBeEmpty();
    }

    [Fact]
    public void Comments_HideEditor_WhenCancelClicked()
    {
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments()));

        cut.Find(".tm-comments-add-btn").Click();
        cut.Find(".tm-comment-cancel-btn").Click();

        cut.FindAll(".tm-comment-editor").Should().BeEmpty();
    }

    [Fact]
    public async Task Comments_SubmitComment_CallsOnCommentAdded()
    {
        string? received = null;
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.OnCommentAdded,
                EventCallback.Factory.Create<string>(this, html => received = html)));

        cut.Find(".tm-comments-add-btn").Click();
        // Set the editor content and submit
        await cut.InvokeAsync(() => cut.Instance.SetEditorContentForTest("<p>New comment</p>"));
        cut.Find(".tm-comment-submit-btn").Click();

        received.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Comments_EditButton_ShowsWhenCanEdit()
    {
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments()));

        // Alice's comment (CanEdit=true) must have an edit button
        var items = cut.FindAll(".tm-comment-item");
        items[0].QuerySelectorAll(".tm-comment-edit-btn").Length.Should().Be(1);
    }

    [Fact]
    public void Comments_DeleteButton_ShowsWhenCanDelete()
    {
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments()));

        // Alice's comment (CanDelete=true) must have a delete button; Bob's must not
        var items = cut.FindAll(".tm-comment-item");
        items[0].QuerySelectorAll(".tm-comment-delete-btn").Length.Should().Be(1);
        items[1].QuerySelectorAll(".tm-comment-delete-btn").Length.Should().Be(0);
    }

    [Fact]
    public async Task Comments_Delete_CallsOnCommentDeleted()
    {
        string? deletedId = null;
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.OnCommentDeleted,
                EventCallback.Factory.Create<string>(this, id => deletedId = id)));

        cut.FindAll(".tm-comment-delete-btn").First().Click();
        await cut.InvokeAsync(() => { });

        deletedId.Should().Be("c1");
    }

    [Fact]
    public async Task Comments_RefreshAsync_ReloadsComments()
    {
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments()));

        cut.FindAll(".tm-comment-item").Count.Should().Be(2);
        await cut.InvokeAsync(() => cut.Instance.RefreshAsync());
        cut.FindAll(".tm-comment-item").Count.Should().Be(2);
    }

    [Fact]
    public void Comments_CommentDate_UsesRelativeTime()
    {
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments()));

        cut.FindAll(".tm-comment-time").Should().NotBeEmpty();
        cut.FindAll(".tm-comment-time")[0].TextContent.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Comments_OnDraftChanged_FiresWhenEditorOpens()
    {
        var states = new List<TmCommentDraftState>();
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.OnDraftChanged,
                EventCallback.Factory.Create<TmCommentDraftState>(this, state => states.Add(state))));

        cut.Find(".tm-comments-add-btn").Click();

        states.Should().HaveCount(1);
        states[0].IsEditorOpen.Should().BeTrue();
        states[0].EditingCommentId.Should().BeNull();
        states[0].HasUnsavedContent.Should().BeFalse();
    }

    [Fact]
    public void Comments_OnDraftChanged_FiresWhenEditorCancelled()
    {
        var states = new List<TmCommentDraftState>();
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.OnDraftChanged,
                EventCallback.Factory.Create<TmCommentDraftState>(this, state => states.Add(state))));

        cut.Find(".tm-comments-add-btn").Click();
        cut.Find(".tm-comment-cancel-btn").Click();

        states.Should().HaveCount(2);
        states[1].IsEditorOpen.Should().BeFalse();
        states[1].EditingCommentId.Should().BeNull();
        states[1].HasUnsavedContent.Should().BeFalse();
    }

    [Fact]
    public void Comments_OnDraftChanged_NotSet_DoesNotThrow()
    {
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments()));

        // Non-breaking guarantee: without OnDraftChanged the component behaves exactly as before.
        var add = () => cut.Find(".tm-comments-add-btn").Click();
        var cancel = () => cut.Find(".tm-comment-cancel-btn").Click();

        add.Should().NotThrow();
        cancel.Should().NotThrow();
        cut.FindAll(".tm-comment-editor").Should().BeEmpty();
    }

    [Fact]
    public void Comments_OnDraftChanged_FiresWhenEditStarts()
    {
        var states = new List<TmCommentDraftState>();
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.OnDraftChanged,
                EventCallback.Factory.Create<TmCommentDraftState>(this, state => states.Add(state))));

        cut.FindAll(".tm-comment-edit-btn").First().Click();

        states.Should().HaveCount(1);
        states[0].IsEditorOpen.Should().BeTrue();
        states[0].EditingCommentId.Should().Be("c1");
        // Opening an edit without changing anything is NOT an unsaved draft yet.
        states[0].HasUnsavedContent.Should().BeFalse();
    }

    [Fact]
    public void Comments_OnDraftChanged_FiresWhenEditCancelled()
    {
        var states = new List<TmCommentDraftState>();
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.OnDraftChanged,
                EventCallback.Factory.Create<TmCommentDraftState>(this, state => states.Add(state))));

        cut.FindAll(".tm-comment-edit-btn").First().Click();
        cut.Find(".tm-comment-cancel-btn").Click();

        states.Should().HaveCount(2);
        states[1].IsEditorOpen.Should().BeFalse();
        states[1].EditingCommentId.Should().BeNull();
        states[1].HasUnsavedContent.Should().BeFalse();
    }

    [Fact]
    public void Comments_StartEdit_ClosesNewCommentEditor()
    {
        var states = new List<TmCommentDraftState>();
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.OnDraftChanged,
                EventCallback.Factory.Create<TmCommentDraftState>(this, state => states.Add(state))));

        cut.Find(".tm-comments-add-btn").Click();
        cut.FindAll(".tm-comment-edit-btn").First().Click();
        cut.Find(".tm-comment-cancel-btn").Click();

        cut.FindAll(".tm-comment-editor").Should().BeEmpty();
        cut.FindAll(".tm-comments-add-btn").Should().NotBeEmpty();
        states.Should().NotBeEmpty();
        states[^1].Should().Be(new TmCommentDraftState(false, null, false));
    }

    [Fact]
    public async Task Comments_OnDraftChanged_ResetsAfterSubmittingNewComment()
    {
        var states = new List<TmCommentDraftState>();
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.OnDraftChanged,
                EventCallback.Factory.Create<TmCommentDraftState>(this, state => states.Add(state))));

        cut.Find(".tm-comments-add-btn").Click();
        await cut.InvokeAsync(() => cut.Instance.SetEditorContentForTest("<p>Draft text</p>"));
        cut.Find(".tm-comment-submit-btn").Click();

        // The full sequence must be exactly open → closed: the last state must carry the closed
        // editor, otherwise the consumer keeps a stale dirty flag after a successful submit.
        states.Should().Equal(
            new TmCommentDraftState(true, null, false),
            new TmCommentDraftState(false, null, false));
    }

    [Fact]
    public async Task Comments_OnDraftChanged_ResetsAfterSubmittingEdit()
    {
        var states = new List<TmCommentDraftState>();
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.OnDraftChanged,
                EventCallback.Factory.Create<TmCommentDraftState>(this, state => states.Add(state))));

        cut.FindAll(".tm-comment-edit-btn").First().Click();
        await cut.InvokeAsync(() => cut.Instance.SetEditorContentForTest("<p>First comment edited</p>"));
        cut.Find(".tm-comment-submit-btn").Click();

        // The full sequence must be exactly open("c1") → closed.
        states.Should().Equal(
            new TmCommentDraftState(true, "c1", false),
            new TmCommentDraftState(false, null, false));
    }

    [Fact]
    public async Task Comments_OnDraftChanged_EmitsNoDuplicateStates()
    {
        var states = new List<TmCommentDraftState>();
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.OnDraftChanged,
                EventCallback.Factory.Create<TmCommentDraftState>(this, state => states.Add(state))));

        // New-comment flow: open → type → submit.
        cut.Find(".tm-comments-add-btn").Click();
        await cut.InvokeAsync(() => cut.Instance.SetEditorContentForTest("<p>Draft text</p>"));
        cut.Find(".tm-comment-submit-btn").Click();

        // Edit flow: start → type → submit.
        cut.FindAll(".tm-comment-edit-btn").First().Click();
        await cut.InvokeAsync(() => cut.Instance.SetEditorContentForTest("<p>First comment edited</p>"));
        cut.Find(".tm-comment-submit-btn").Click();

        states.Should().NotBeEmpty();
        for (var i = 1; i < states.Count; i++)
        {
            states[i].Should().NotBe(states[i - 1],
                "OnDraftChanged fires only when the draft state value actually changes");
        }
    }

    private static TmCommentEntry Comment(
        string id,
        string authorName,
        string html,
        bool canEdit,
        bool canDelete,
        DateTimeOffset createdAt)
        => new()
        {
            Id = id,
            ThreadId = id,
            Author = new TmUserRef { DisplayName = authorName },
            CreatedAt = createdAt,
            Body = html,
            BodyFormat = TmCommentBodyFormat.Html,
            CanEdit = canEdit,
            CanDelete = canDelete
        };
}
