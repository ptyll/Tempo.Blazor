using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.Activity;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Activity;

/// <summary>
/// CommentSubmitHandler / CommentEditHandler contract: a failed save (false) must keep the
/// editor open with the text, while true closes and clears exactly like the legacy callbacks.
/// Split out of TmActivityCommentsTests so neither file breaches the 1200-line source ceiling.
/// </summary>
public class TmActivityCommentsSubmitHandlersTests : LocalizationTestBase
{
    private static List<TmCommentEntry> TwoComments() =>
    [
        Comment("c1", "Alice", "<p>First comment</p>", true, true, DateTimeOffset.Now.AddHours(-2)),
        Comment("c2", "Bob", "<p>Second comment</p>", false, false, DateTimeOffset.Now.AddHours(-1)),
    ];

    [Fact]
    public async Task SubmitHandler_ReturnsFalse_KeepsEditorAndContent()
    {
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.CommentSubmitHandler, _ => Task.FromResult(false)));

        cut.Find(".tm-comments-add-btn").Click();
        await cut.InvokeAsync(() => cut.Instance.SetEditorContentForTestAsync("<p>New comment</p>"));
        cut.Find(".tm-comment-submit-btn").Click();

        // Not saved: the editor stays open and the typed text survives.
        cut.FindAll(".tm-comment-editor").Should().NotBeEmpty();
        cut.FindComponent<TmRichEditorSimple>().Instance.Value.Should().Be("<p>New comment</p>");
        cut.Instance.HasUnsavedDraft.Should().BeTrue();
    }

    [Fact]
    public async Task SubmitHandler_ReturnsFalse_KeepsInternalToggle()
    {
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.ShowInternalToggle, true)
            .Add(c => c.CommentSubmitHandler, _ => Task.FromResult(false)));

        cut.Find(".tm-comments-add-btn").Click();
        cut.Find(".tm-comment-internal-toggle input").Change(true);
        await cut.InvokeAsync(() => cut.Instance.SetEditorContentForTestAsync("<p>Internal note</p>"));
        cut.Find(".tm-comment-submit-btn").Click();

        cut.Find(".tm-comment-internal-toggle input").HasAttribute("checked").Should().BeTrue();
        cut.Find(".tm-comment-editor").ClassList.Should().Contain("tm-comment-editor--internal");
    }

    [Fact]
    public async Task SubmitHandler_ReturnsFalse_DraftStaysDirty_NoDraftNotification()
    {
        var states = new List<TmCommentDraftState>();
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.CommentSubmitHandler, _ => Task.FromResult(false))
            .Add(c => c.OnDraftChanged, EventCallback.Factory.Create<TmCommentDraftState>(this, states.Add)));

        cut.Find(".tm-comments-add-btn").Click();
        await cut.InvokeAsync(() => cut.Instance.SetEditorContentForTestAsync("<p>Draft text</p>"));
        var countBeforeSubmit = states.Count;
        cut.Find(".tm-comment-submit-btn").Click();

        // The dirty state is reported as-is; a failed save is not a draft transition.
        states.Count.Should().Be(countBeforeSubmit);
        states.Last().Should().Be(new TmCommentDraftState(true, null, true, false));
        cut.Instance.HasUnsavedDraft.Should().BeTrue();
    }

    [Fact]
    public async Task SubmitHandler_ReturnsFalse_AnnouncesNotSaved()
    {
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.CommentSubmitHandler, _ => Task.FromResult(false)));

        cut.Find(".tm-comments-add-btn").Click();
        await cut.InvokeAsync(() => cut.Instance.SetEditorContentForTestAsync("<p>New comment</p>"));
        cut.Find(".tm-comment-submit-btn").Click();

        cut.Find(".tm-comment-editor [role='status'][aria-live='polite']").TextContent
            .Should().Contain("Comment was not saved");
    }

    [Fact]
    public async Task SubmitHandler_ReturnsTrue_ClosesAndClears_ResetsToggle()
    {
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.ShowInternalToggle, true)
            .Add(c => c.CommentSubmitHandler, _ => Task.FromResult(true)));

        cut.Find(".tm-comments-add-btn").Click();
        cut.Find(".tm-comment-internal-toggle input").Change(true);
        await cut.InvokeAsync(() => cut.Instance.SetEditorContentForTestAsync("<p>New comment</p>"));
        cut.Find(".tm-comment-submit-btn").Click();

        cut.FindAll(".tm-comment-editor").Should().BeEmpty();
        cut.Instance.HasUnsavedDraft.Should().BeFalse();

        cut.Find(".tm-comments-add-btn").Click();
        cut.Find(".tm-comment-internal-toggle input").HasAttribute("checked").Should().BeFalse();
    }

    [Fact]
    public async Task SubmitHandler_Set_ReplacesBothCallbacks()
    {
        var handlerCalls = 0;
        TmCommentSubmission? received = null;
        var callbackCalls = 0;
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.ShowInternalToggle, true)
            .Add(c => c.CommentSubmitHandler, submission =>
            {
                handlerCalls++;
                received = submission;
                return Task.FromResult(true);
            })
            .Add(c => c.OnCommentSubmitted, EventCallback.Factory.Create<TmCommentSubmission>(this, _ => callbackCalls++))
            .Add(c => c.OnCommentAdded, EventCallback.Factory.Create<string>(this, _ => callbackCalls++)));

        cut.Find(".tm-comments-add-btn").Click();
        cut.Find(".tm-comment-internal-toggle input").Change(true);
        await cut.InvokeAsync(() => cut.Instance.SetEditorContentForTestAsync("<p>Via handler</p>"));
        cut.Find(".tm-comment-submit-btn").Click();

        handlerCalls.Should().Be(1);
        received.Should().NotBeNull();
        received!.Html.Should().Be("<p>Via handler</p>");
        received.IsInternal.Should().BeTrue();
        callbackCalls.Should().Be(0);
    }

    [Fact]
    public async Task SubmitHandler_DisabledWhileRunning_DoubleClickSingleCall()
    {
        var calls = 0;
        var release = new TaskCompletionSource<bool>();
        IRenderedComponent<TmActivityComments> cut = null!;
        cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.CommentSubmitHandler, _ =>
            {
                calls++;
                cut.Render();
                cut.Find(".tm-comment-submit-btn").GetAttribute("aria-disabled").Should().Be("true");
                return release.Task;
            }));

        cut.Find(".tm-comments-add-btn").Click();
        await cut.InvokeAsync(() => cut.Instance.SetEditorContentForTestAsync("<p>slow</p>"));
        var first = cut.InvokeAsync(() => cut.Find(".tm-comment-submit-btn").Click());
        cut.Find(".tm-comment-submit-btn").Click();
        release.SetResult(true);
        await first;
        cut.Render();

        calls.Should().Be(1);
        cut.FindAll(".tm-comment-editor").Should().BeEmpty();
    }

    [Fact]
    public async Task SubmitHandler_Throws_KeepsEditor_SubmittingCleared()
    {
        var calls = 0;
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.CommentSubmitHandler, _ =>
            {
                calls++;
                return Task.FromException<bool>(new InvalidOperationException("save failed"));
            }));

        cut.Find(".tm-comments-add-btn").Click();
        await cut.InvokeAsync(() => cut.Instance.SetEditorContentForTestAsync("<p>New comment</p>"));

        // The handler exception propagates (the handler must convert failure to false),
        // but the editor stays open and _isSubmitting is cleared in finally. The submit
        // button carries aria-disabled (not the native disabled attribute) so focus is
        // kept — after the flag clears the attribute is gone again.
        var act = () => cut.Find(".tm-comment-submit-btn").Click();
        act.Should().Throw<InvalidOperationException>();

        cut.FindAll(".tm-comment-editor").Should().NotBeEmpty();
        cut.Find(".tm-comment-submit-btn").HasAttribute("aria-disabled").Should().BeFalse();
        cut.Instance.HasUnsavedDraft.Should().BeTrue();

        // A retry is possible: the guard no longer blocks the second submit.
        var retry = () => cut.Find(".tm-comment-submit-btn").Click();
        retry.Should().Throw<InvalidOperationException>();
        calls.Should().Be(2);
    }

    [Fact]
    public async Task SubmitHandler_Throws_LiveStatusNoLongerPosting()
    {
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.CommentSubmitHandler, _ => Task.FromException<bool>(new InvalidOperationException("save failed"))));

        cut.Find(".tm-comments-add-btn").Click();
        await cut.InvokeAsync(() => cut.Instance.SetEditorContentForTestAsync("<p>New comment</p>"));

        var act = () => cut.Find(".tm-comment-submit-btn").Click();
        act.Should().Throw<InvalidOperationException>();

        // The finally reset must reach the DOM: the polite status may not keep announcing
        // the in-flight "Posting comment." after the save blew up.
        cut.Render();
        cut.Find(".tm-comment-editor [role='status'][aria-live='polite']").TextContent
            .Should().NotContain("Posting comment.");
    }

    [Fact]
    public async Task SubmitHandler_CompletesAfterCancel_DoesNotClearNewDraft()
    {
        var release = new TaskCompletionSource<bool>();
        IRenderedComponent<TmActivityComments> cut = null!;
        cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.CommentSubmitHandler, _ => release.Task));

        cut.Find(".tm-comments-add-btn").Click();
        await cut.InvokeAsync(() => cut.Instance.SetEditorContentForTestAsync("<p>slow</p>"));
        var first = cut.InvokeAsync(() => cut.Find(".tm-comment-submit-btn").Click());

        // While the save is in flight the user cancels and starts a NEW draft.
        cut.Find(".tm-comment-cancel-btn").Click();
        cut.Find(".tm-comments-add-btn").Click();
        await cut.InvokeAsync(() => cut.Instance.SetEditorContentForTestAsync("<p>second draft</p>"));

        release.SetResult(true);
        await first;

        // The late success of the first submit must not close the composer again or wipe
        // the draft the user is typing now.
        cut.FindAll(".tm-comment-editor").Should().ContainSingle();
        cut.FindComponent<TmRichEditorSimple>().Instance.Value.Should().Be("<p>second draft</p>");
        cut.Instance.HasUnsavedDraft.Should().BeTrue();
    }

    [Fact]
    public async Task NoHandler_ExistingOrder_Unchanged()
    {
        var order = new List<string>();
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.OnCommentSubmitted, EventCallback.Factory.Create<TmCommentSubmission>(this, _ =>
            {
                order.Add("submitted");
                return Task.CompletedTask;
            }))
            .Add(c => c.OnCommentAdded, EventCallback.Factory.Create<string>(this, _ =>
            {
                order.Add("added");
                return Task.CompletedTask;
            })));

        cut.Find(".tm-comments-add-btn").Click();
        await cut.InvokeAsync(() => cut.Instance.SetEditorContentForTestAsync("<p>Plain callback</p>"));
        cut.Find(".tm-comment-submit-btn").Click();

        order.Should().Equal("submitted", "added");
        cut.FindAll(".tm-comment-editor").Should().BeEmpty();
    }

    [Fact]
    public async Task EditHandler_ReturnsFalse_KeepsInlineEditorAndContent()
    {
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.CommentEditHandler, _ => Task.FromResult(false)));

        cut.FindAll(".tm-comment-item")[0].QuerySelector(".tm-comment-edit-btn")!.Click();
        await cut.InvokeAsync(() => cut.Instance.SetEditorContentForTestAsync("<p>First comment edited</p>"));
        cut.Find(".tm-comment-submit-btn").Click();

        cut.FindAll(".tm-comment-editor").Should().ContainSingle();
        cut.FindComponent<TmRichEditorSimple>().Instance.Value.Should().Be("<p>First comment edited</p>");
    }

    [Fact]
    public async Task EditHandler_ReturnsFalse_AnnouncesNotSaved()
    {
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.CommentEditHandler, _ => Task.FromResult(false)));

        cut.FindAll(".tm-comment-item")[0].QuerySelector(".tm-comment-edit-btn")!.Click();
        await cut.InvokeAsync(() => cut.Instance.SetEditorContentForTestAsync("<p>First comment edited</p>"));
        cut.Find(".tm-comment-submit-btn").Click();

        // The inline editor has its own always-rendered polite live region for the
        // not-saved announcement (the composer one is not mounted while editing).
        cut.Find(".tm-comment-editor [role='status'][aria-live='polite']").TextContent
            .Should().Contain("Comment was not saved");
    }

    [Fact]
    public async Task EditHandler_Throws_KeepsInlineEditor_SubmittingCleared()
    {
        var calls = 0;
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.CommentEditHandler, _ =>
            {
                calls++;
                return Task.FromException<bool>(new InvalidOperationException("save failed"));
            }));

        cut.FindAll(".tm-comment-item")[0].QuerySelector(".tm-comment-edit-btn")!.Click();
        await cut.InvokeAsync(() => cut.Instance.SetEditorContentForTestAsync("<p>First comment edited</p>"));

        var act = () => cut.Find(".tm-comment-submit-btn").Click();
        act.Should().Throw<InvalidOperationException>();

        cut.FindAll(".tm-comment-editor").Should().ContainSingle();
        cut.Find(".tm-comment-submit-btn").HasAttribute("aria-disabled").Should().BeFalse();
        cut.Instance.HasUnsavedDraft.Should().BeTrue();

        // A retry is possible: the guard no longer blocks the second save.
        var retry = () => cut.Find(".tm-comment-submit-btn").Click();
        retry.Should().Throw<InvalidOperationException>();
        calls.Should().Be(2);
    }

    [Fact]
    public async Task EditHandler_CompletesAfterSwitchingTarget_KeepsNewEdit()
    {
        var release = new TaskCompletionSource<bool>();
        IRenderedComponent<TmActivityComments> cut = null!;
        cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, new List<TmCommentEntry>
            {
                Comment("c1", "Alice", "<p>First comment</p>", true, true, DateTimeOffset.Now.AddHours(-2)),
                Comment("c2", "Bob", "<p>Second comment</p>", true, false, DateTimeOffset.Now.AddHours(-1))
            })
            .Add(c => c.CommentEditHandler, _ => release.Task));

        cut.FindAll(".tm-comment-item")[0].QuerySelector(".tm-comment-edit-btn")!.Click();
        await cut.InvokeAsync(() => cut.Instance.SetEditorContentForTestAsync("<p>c1 edited</p>"));
        var first = cut.InvokeAsync(() => cut.Find(".tm-comment-submit-btn").Click());

        // While the c1 save is in flight the user switches to editing c2.
        cut.FindAll(".tm-comment-item")[1].QuerySelector(".tm-comment-edit-btn")!.Click();
        await cut.InvokeAsync(() => cut.Instance.SetEditorContentForTestAsync("<p>c2 edited</p>"));

        release.SetResult(true);
        await first;

        // The late success of the c1 submit must not close the c2 editor or wipe its content.
        cut.FindAll(".tm-comment-editor").Should().ContainSingle();
        cut.FindComponent<TmRichEditorSimple>().Instance.Value.Should().Be("<p>c2 edited</p>");
        cut.Instance.HasUnsavedDraft.Should().BeTrue();
    }

    [Fact]
    public async Task EditHandler_ReturnsTrue_ClosesEditor()
    {
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.CommentEditHandler, _ => Task.FromResult(true)));

        cut.FindAll(".tm-comment-item")[0].QuerySelector(".tm-comment-edit-btn")!.Click();
        await cut.InvokeAsync(() => cut.Instance.SetEditorContentForTestAsync("<p>First comment edited</p>"));
        cut.Find(".tm-comment-submit-btn").Click();

        cut.FindAll(".tm-comment-editor").Should().BeEmpty();
        cut.Instance.HasUnsavedDraft.Should().BeFalse();
    }

    [Fact]
    public async Task EditHandler_Set_ReplacesCallback()
    {
        var handlerCalls = 0;
        var callbackCalls = 0;
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.CommentEditHandler, _ =>
            {
                handlerCalls++;
                return Task.FromResult(true);
            })
            .Add(c => c.OnCommentEdited, EventCallback.Factory.Create<(string Id, string Html)>(this, _ => callbackCalls++)));

        cut.FindAll(".tm-comment-item")[0].QuerySelector(".tm-comment-edit-btn")!.Click();
        await cut.InvokeAsync(() => cut.Instance.SetEditorContentForTestAsync("<p>First comment edited</p>"));
        cut.Find(".tm-comment-submit-btn").Click();

        handlerCalls.Should().Be(1);
        callbackCalls.Should().Be(0);
        cut.FindAll(".tm-comment-editor").Should().BeEmpty();
    }

    [Fact]
    public async Task EditHandler_ReceivesCommentIdAndHtml()
    {
        TmCommentEdit? received = null;
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.CommentEditHandler, edit =>
            {
                received = edit;
                return Task.FromResult(true);
            }));

        cut.FindAll(".tm-comment-item")[0].QuerySelector(".tm-comment-edit-btn")!.Click();
        await cut.InvokeAsync(() => cut.Instance.SetEditorContentForTestAsync("<p>First comment edited</p>"));
        cut.Find(".tm-comment-submit-btn").Click();

        received.Should().Be(new TmCommentEdit("c1", "<p>First comment edited</p>"));
    }

    [Fact]
    public async Task EditHandler_ReturnsFalse_DraftReportsEditDirty()
    {
        var states = new List<TmCommentDraftState>();
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.CommentEditHandler, _ => Task.FromResult(false))
            .Add(c => c.OnDraftChanged, EventCallback.Factory.Create<TmCommentDraftState>(this, states.Add)));

        cut.FindAll(".tm-comment-item")[0].QuerySelector(".tm-comment-edit-btn")!.Click();
        await cut.InvokeAsync(() => cut.Instance.SetEditorContentForTestAsync("<p>First comment edited</p>"));
        var countBeforeSubmit = states.Count;
        cut.Find(".tm-comment-submit-btn").Click();

        states.Count.Should().Be(countBeforeSubmit);
        states.Last().Should().Be(new TmCommentDraftState(true, "c1", true, false));
        cut.Instance.HasUnsavedDraft.Should().BeTrue();
    }

    [Fact]
    public async Task NoEditHandler_Unchanged()
    {
        (string Id, string Html)? received = null;
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.OnCommentEdited, EventCallback.Factory.Create<(string Id, string Html)>(this, edit =>
            {
                received = edit;
                return Task.CompletedTask;
            })));

        cut.FindAll(".tm-comment-item")[0].QuerySelector(".tm-comment-edit-btn")!.Click();
        await cut.InvokeAsync(() => cut.Instance.SetEditorContentForTestAsync("<p>First comment edited</p>"));
        cut.Find(".tm-comment-submit-btn").Click();

        received.Should().Be(("c1", "<p>First comment edited</p>"));
        cut.FindAll(".tm-comment-editor").Should().BeEmpty();
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
