using System.Reflection;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using NSubstitute;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.Activity;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;

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
    public async Task Comments_SubmitNewComment_EffectivelyEmptyMarkup_DoesNotCallOnCommentAdded()
    {
        string? received = null;
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.OnCommentAdded,
                EventCallback.Factory.Create<string>(this, html => received = html)));

        cut.Find(".tm-comments-add-btn").Click();
        // "<div><br></div>" is the markup a browser leaves behind when the editor is emptied;
        // the submit guard must treat it like the draft flag does — nothing to submit.
        await cut.InvokeAsync(() => cut.Instance.SetEditorContentForTestAsync("<div><br></div>"));
        cut.Find(".tm-comment-submit-btn").Click();

        // The submit is a no-op: OnCommentAdded never fires and the editor stays open so the
        // user can keep (re)typing.
        received.Should().BeNull();
        cut.FindAll(".tm-comment-editor").Should().NotBeEmpty();
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
        await cut.InvokeAsync(() => cut.Instance.SetEditorContentForTestAsync("<p>Draft text</p>"));
        cut.Find(".tm-comment-submit-btn").Click();

        // The typed content must surface as the unsaved state (true, null, true) before the
        // submit resets to the closed editor.
        states.Should().Equal(
            new TmCommentDraftState(true, null, false),
            new TmCommentDraftState(true, null, true),
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
        await cut.InvokeAsync(() => cut.Instance.SetEditorContentForTestAsync("<p>First comment edited</p>"));
        cut.Find(".tm-comment-submit-btn").Click();

        // The edited content differs from the original body, so the unsaved state
        // (true, "c1", true) must surface before the submit resets to the closed editor.
        states.Should().Equal(
            new TmCommentDraftState(true, "c1", false),
            new TmCommentDraftState(true, "c1", true),
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

    [Fact]
    public async Task Comments_OnDraftChanged_ReportsUnsavedContentWhenTypingNewComment()
    {
        var states = new List<TmCommentDraftState>();
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.OnDraftChanged,
                EventCallback.Factory.Create<TmCommentDraftState>(this, state => states.Add(state))));

        cut.Find(".tm-comments-add-btn").Click();
        await cut.InvokeAsync(() => cut.Instance.SetEditorContentForTestAsync("<p>x</p>"));

        // Typing into the new-comment editor flips HasUnsavedContent exactly once.
        states.Should().Equal(
            new TmCommentDraftState(true, null, false),
            new TmCommentDraftState(true, null, true));
    }

    [Fact]
    public async Task Comments_OnDraftChanged_EmptyMarkupArtifacts_AreNotUnsavedContent()
    {
        var states = new List<TmCommentDraftState>();
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.OnDraftChanged,
                EventCallback.Factory.Create<TmCommentDraftState>(this, state => states.Add(state))));

        cut.Find(".tm-comments-add-btn").Click();
        // "<div><br></div>" is the markup a browser leaves behind when the editor is emptied;
        // it renders the placeholder and must NOT surface as an unsaved draft, so the
        // editor-open state remains the only notification.
        await cut.InvokeAsync(() => cut.Instance.SetEditorContentForTestAsync("<div><br></div>"));

        states.Should().Equal(new TmCommentDraftState(true, null, false));
    }

    [Fact]
    public async Task Comments_OnDraftChanged_EditWithoutChangeReportsNoUnsavedContent()
    {
        var states = new List<TmCommentDraftState>();
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.OnDraftChanged,
                EventCallback.Factory.Create<TmCommentDraftState>(this, state => states.Add(state))));

        cut.FindAll(".tm-comment-edit-btn").First().Click();
        // No content change: the editor is pre-filled with the original body, which must NOT
        // count as an unsaved draft. The edit-open state must be the ONLY notification.
        await cut.InvokeAsync(() => cut.Instance.RefreshAsync());

        states.Should().Equal(new TmCommentDraftState(true, "c1", false));
    }

    [Fact]
    public async Task Comments_OnDraftChanged_EditWithChangeReportsUnsavedContent()
    {
        var states = new List<TmCommentDraftState>();
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.OnDraftChanged,
                EventCallback.Factory.Create<TmCommentDraftState>(this, state => states.Add(state))));

        cut.FindAll(".tm-comment-edit-btn").First().Click();
        await cut.InvokeAsync(() => cut.Instance.SetEditorContentForTestAsync("<p>First comment changed</p>"));

        // Changing the body of the edited comment flips HasUnsavedContent while the editor
        // stays open on "c1".
        states.Should().Equal(
            new TmCommentDraftState(true, "c1", false),
            new TmCommentDraftState(true, "c1", true));
    }

    [Fact]
    public async Task Comments_OnDraftChanged_EditRevertedToOriginalReportsNoUnsavedContent()
    {
        var states = new List<TmCommentDraftState>();
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.OnDraftChanged,
                EventCallback.Factory.Create<TmCommentDraftState>(this, state => states.Add(state))));

        cut.FindAll(".tm-comment-edit-btn").First().Click();
        await cut.InvokeAsync(() => cut.Instance.SetEditorContentForTestAsync("<p>First comment changed</p>"));
        await cut.InvokeAsync(() => cut.Instance.SetEditorContentForTestAsync("<p>First comment</p>"));

        // Reverting the edit back to the ORIGINAL body must drop HasUnsavedContent again —
        // the comparison is against the original body, not a "changed once" latch.
        states.Should().Equal(
            new TmCommentDraftState(true, "c1", false),
            new TmCommentDraftState(true, "c1", true),
            new TmCommentDraftState(true, "c1", false));
    }

    [Fact]
    public async Task Comments_OnDraftChanged_RepeatedTypingDoesNotRefire()
    {
        var states = new List<TmCommentDraftState>();
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.OnDraftChanged,
                EventCallback.Factory.Create<TmCommentDraftState>(this, state => states.Add(state))));

        cut.Find(".tm-comments-add-btn").Click();
        await cut.InvokeAsync(() => cut.Instance.SetEditorContentForTestAsync("<p>a</p>"));
        await cut.InvokeAsync(() => cut.Instance.SetEditorContentForTestAsync("<p>bb</p>"));
        await cut.InvokeAsync(() => cut.Instance.SetEditorContentForTestAsync("<p>ccc</p>"));

        // Dedup is mandatory: after the first transition to HasUnsavedContent == true the
        // state value is unchanged, so no further notification may arrive.
        states.Should().Equal(
            new TmCommentDraftState(true, null, false),
            new TmCommentDraftState(true, null, true));
    }

    [Fact]
    public async Task Comments_OnDraftChanged_ValueChangedPath_NewCommentEditorNotifies()
    {
        var states = new List<TmCommentDraftState>();
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.OnDraftChanged,
                EventCallback.Factory.Create<TmCommentDraftState>(this, state => states.Add(state))));

        cut.Find(".tm-comments-add-btn").Click();
        // Drive the REAL user-typing path (ValueChanged → bind → @bind-Value:after) instead of
        // the SetEditorContentForTest seam, so the markup wiring itself is under test. Exactly
        // one editor instance is rendered while the new-comment editor is open.
        var editor = cut.FindComponents<TmRichEditorSimple>().Single();
        await cut.InvokeAsync(() => editor.Instance.ValueChanged.InvokeAsync("<p>x</p>"));

        states.Should().Equal(
            new TmCommentDraftState(true, null, false),
            new TmCommentDraftState(true, null, true));
    }

    [Fact]
    public async Task Comments_OnDraftChanged_ValueChangedPath_EditEditorNotifies()
    {
        var states = new List<TmCommentDraftState>();
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.OnDraftChanged,
                EventCallback.Factory.Create<TmCommentDraftState>(this, state => states.Add(state))));

        cut.FindAll(".tm-comment-edit-btn").First().Click();
        // The SAME real-typing path on the SECOND editor instance: the edit editor is separate
        // markup with its own @bind-Value:after wiring and must notify on its own.
        var editor = cut.FindComponents<TmRichEditorSimple>().Single();
        await cut.InvokeAsync(() => editor.Instance.ValueChanged.InvokeAsync("<p>First comment changed</p>"));

        states.Should().Equal(
            new TmCommentDraftState(true, "c1", false),
            new TmCommentDraftState(true, "c1", true));
    }

    [Fact]
    public async Task Comments_HasUnsavedDraft_ReflectsEditorState()
    {
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments()));

        // Fresh render: no editor, no draft.
        cut.Instance.HasUnsavedDraft.Should().BeFalse();

        // Opening an EMPTY editor is not an unsaved draft.
        cut.Find(".tm-comments-add-btn").Click();
        cut.Instance.HasUnsavedDraft.Should().BeFalse();

        // Typed content makes the draft unsaved.
        await cut.InvokeAsync(() => cut.Instance.SetEditorContentForTestAsync("<p>Draft text</p>"));
        cut.Instance.HasUnsavedDraft.Should().BeTrue();

        // Cancelling clears the draft again.
        cut.Find(".tm-comment-cancel-btn").Click();
        cut.Instance.HasUnsavedDraft.Should().BeFalse();
    }

    [Fact]
    public async Task Comments_DiscardDraftAsync_ClosesEditorAndClearsContent()
    {
        var states = new List<TmCommentDraftState>();
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.OnDraftChanged,
                EventCallback.Factory.Create<TmCommentDraftState>(this, state => states.Add(state))));

        cut.Find(".tm-comments-add-btn").Click();
        await cut.InvokeAsync(() => cut.Instance.SetEditorContentForTestAsync("<p>Draft text</p>"));
        await cut.InvokeAsync(() => cut.Instance.DiscardDraftAsync());

        // The editor is gone from the DOM, no unsaved draft remains, and the parent received
        // the closed-editor state (open → unsaved → closed).
        cut.FindAll(".tm-comment-editor").Should().BeEmpty();
        cut.Instance.HasUnsavedDraft.Should().BeFalse();
        states.Should().Equal(
            new TmCommentDraftState(true, null, false),
            new TmCommentDraftState(true, null, true),
            new TmCommentDraftState(false, null, false));
    }

    [Fact]
    public void Comments_AddButton_HiddenDuringEdit()
    {
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments()));

        cut.FindAll(".tm-comments-add-btn").Should().NotBeEmpty();

        cut.FindAll(".tm-comment-edit-btn").First().Click();

        // While an inline edit is open the Add button must be hidden: both editors share one
        // content slot, so clicking it would wipe the edit draft without opening any editor.
        cut.FindAll(".tm-comments-add-btn").Should().BeEmpty();
    }

    [Fact]
    public async Task Comments_ShowEditor_DuringEdit_EndsEditAndOpensNewEditor()
    {
        var states = new List<TmCommentDraftState>();
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.OnDraftChanged,
                EventCallback.Factory.Create<TmCommentDraftState>(this, state => states.Add(state))));

        cut.FindAll(".tm-comment-edit-btn").First().Click();
        await cut.InvokeAsync(() => cut.Instance.SetEditorContentForTestAsync("<p>First comment changed</p>"));

        // The Add button is hidden while the edit is open (Comments_AddButton_HiddenDuringEdit),
        // so no UI path reaches ShowEditor during an edit; it is invoked directly as defense-in-depth — the guard must still END
        // the active edit (symmetry with StartEdit closing the new-comment editor) before
        // opening the new-comment editor.
        var showEditor = typeof(TmActivityComments).GetMethod(
            "ShowEditor", BindingFlags.NonPublic | BindingFlags.Instance);
        showEditor.Should().NotBeNull();
        await cut.InvokeAsync(() => (Task)showEditor!.Invoke(cut.Instance, null)!);
        // Blazor re-renders automatically after a real @onclick handler; a direct call skips
        // that, so the test plays the framework's part before asserting the markup.
        cut.Render();

        // The inline edit closed (c1 renders as read mode again) and the new-comment editor
        // opened in its place.
        cut.FindAll(".tm-comment-editor").Should().ContainSingle();
        cut.FindAll(".tm-comment-content").Should().HaveCount(2);
        cut.FindAll(".tm-comment-edit-btn").Should().NotBeEmpty();
        cut.Instance.HasUnsavedDraft.Should().BeFalse();
        // Exactly ONE deduped notification bridges edit → new-comment editor.
        states.Should().Equal(
            new TmCommentDraftState(true, "c1", false),
            new TmCommentDraftState(true, "c1", true),
            new TmCommentDraftState(true, null, false));
    }

    [Fact]
    public async Task Comments_DiscardDraftAsync_DuringEdit_ReturnsToReadMode()
    {
        var states = new List<TmCommentDraftState>();
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.OnDraftChanged,
                EventCallback.Factory.Create<TmCommentDraftState>(this, state => states.Add(state))));

        cut.FindAll(".tm-comment-edit-btn").First().Click();
        await cut.InvokeAsync(() => cut.Instance.SetEditorContentForTestAsync("<p>First comment changed</p>"));
        await cut.InvokeAsync(() => cut.Instance.DiscardDraftAsync());

        // The inline editor closed, the ORIGINAL comment renders as text again, and the parent
        // received the closed-editor state (edit open → unsaved → closed).
        cut.FindAll(".tm-comment-editor").Should().BeEmpty();
        cut.FindAll(".tm-comment-content").Should().HaveCount(2);
        var contents = cut.FindAll(".tm-comment-content");
        contents[0].InnerHtml.Should().Contain("<p>First comment</p>");
        contents[0].TextContent.Should().NotContain("First comment changed");
        cut.Instance.HasUnsavedDraft.Should().BeFalse();
        states.Should().Equal(
            new TmCommentDraftState(true, "c1", false),
            new TmCommentDraftState(true, "c1", true),
            new TmCommentDraftState(false, null, false));
    }

    [Fact]
    public async Task Comments_DiscardDraftAsync_EditedCommentRemovedFromList_ResetsState()
    {
        var states = new List<TmCommentDraftState>();
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.OnDraftChanged,
                EventCallback.Factory.Create<TmCommentDraftState>(this, state => states.Add(state))));

        cut.FindAll(".tm-comment-edit-btn").First().Click();

        // A parent refresh drops the comment being edited; the component still holds the
        // editing state (a plain re-render does not notify).
        cut.Render(p => p
            .Add(c => c.Comments, TwoComments().Where(entry => entry.Id != "c1").ToList()));
        cut.FindAll(".tm-comment-item").Should().ContainSingle();

        await cut.InvokeAsync(() => cut.Instance.DiscardDraftAsync());

        // The recovery path resets the whole draft state and the parent receives the
        // closed-editor state exactly once.
        states.Should().Equal(
            new TmCommentDraftState(true, "c1", false),
            new TmCommentDraftState(false, null, false));
        cut.Instance.HasUnsavedDraft.Should().BeFalse();
        cut.FindAll(".tm-comment-editor").Should().BeEmpty();
    }

    [Fact]
    public async Task Comments_DiscardDraftAsync_WhenNothingOpen_IsNoOp()
    {
        var states = new List<TmCommentDraftState>();
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.OnDraftChanged,
                EventCallback.Factory.Create<TmCommentDraftState>(this, state => states.Add(state))));

        await cut.InvokeAsync(() => cut.Instance.DiscardDraftAsync());

        // Nothing was open, so nothing may change and NO notification may fire (dedup).
        cut.FindAll(".tm-comment-item").Count.Should().Be(2);
        cut.Instance.HasUnsavedDraft.Should().BeFalse();
        states.Should().BeEmpty();
    }

    [Fact]
    public void Comments_PublicEntry_HasNoInternalMarker_AndDataInternalFalse()
    {
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments()));

        var item = cut.Find(".tm-comment-item");
        item.GetAttribute("data-comment-id").Should().Be("c1");
        item.GetAttribute("data-internal").Should().Be("false");
        item.ClassList.Should().NotContain("tm-comment-item--internal");
        cut.FindAll(".tm-comment-internal-label").Should().BeEmpty();
        cut.FindAll(".tm-comment-badge").Should().BeEmpty();
    }

    [Fact]
    public void Comments_InternalEntry_RendersLockLabel_AndKeepsTheBody()
    {
        var comments = TwoComments();
        comments[0].IsInternal = true;

        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, comments));

        var item = cut.Find("[data-comment-id='c1']");
        item.GetAttribute("data-internal").Should().Be("true");
        item.ClassList.Should().Contain("tm-comment-item--internal");
        item.TextContent.Should().Contain("Internal");
        item.TextContent.Should().Contain("First comment");
        var label = item.QuerySelector(".tm-comment-internal-label")!;
        label.QuerySelector(".tm-icon").Should().NotBeNull();
        label.InnerHtml.Should().Contain("M7 11V7a5 5 0 0 1 10 0v4", "the internal cue is IconNames.Lock, not EyeOff");
        label.InnerHtml.Should().NotContain("M17.94 17.94", "EyeOff is not the internal cue");
        label.HasAttribute("aria-label").Should().BeFalse("the visible label is the accessible name");
        label.QuerySelector(".tm-sr-only")!.TextContent.Should().Be(": Visible only to internal users");
        cut.Find("[data-comment-id='c2']").GetAttribute("data-internal").Should().Be("false");
    }

    [Fact]
    public void Comments_NullBadges_RendersTheEntry_WithoutBadges()
    {
        var comments = TwoComments();
        comments[0].Badges = null!;

        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, comments));

        cut.Find("[data-comment-id='c1']").TextContent.Should().Contain("First comment");
        cut.FindAll(".tm-comment-badge").Should().BeEmpty();
    }

    [Fact]
    public void Comments_Badges_RenderInMeta_WithToneAttribute_AndSkipInvalid()
    {
        var comments = TwoComments();
        comments[1].Badges =
        [
            new TmCommentBadge { Text = "Blocked", Tone = TmCommentBadgeTone.Danger, Icon = "alert-circle", Title = "Waiting on vendor" },
            new TmCommentBadge { Text = "   ", Tone = TmCommentBadgeTone.Info },
            new TmCommentBadge { Text = "Done", Tone = TmCommentBadgeTone.Success },
            new TmCommentBadge { Text = "Watch", Tone = TmCommentBadgeTone.Warning },
            new TmCommentBadge { Text = "FYI", Tone = TmCommentBadgeTone.Info },
            new TmCommentBadge { Text = "Pinned", Tone = TmCommentBadgeTone.Accent },
            new TmCommentBadge { Text = "Note", Tone = TmCommentBadgeTone.Neutral }
        ];

        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, comments));

        var meta = cut.Find("[data-comment-id='c2'] .tm-comment-meta");
        var badges = meta.QuerySelectorAll(".tm-comment-badge");
        badges.Should().HaveCount(6);
        badges[0].GetAttribute("data-badge-tone").Should().Be("danger");
        badges[0].ClassList.Should().Contain("tm-badge-danger");
        badges[0].GetAttribute("title").Should().Be("Waiting on vendor");
        badges[0].HasAttribute("aria-label").Should().BeFalse();
        badges[0].QuerySelector(".tm-sr-only")!.TextContent.Should().Be(": Waiting on vendor");
        badges[0].TextContent.Should().Contain("Blocked");
        badges[1].GetAttribute("data-badge-tone").Should().Be("success");
        badges[1].ClassList.Should().Contain("tm-badge-success");
        badges[2].GetAttribute("data-badge-tone").Should().Be("warning");
        badges[2].ClassList.Should().Contain("tm-badge-warning");
        badges[3].GetAttribute("data-badge-tone").Should().Be("info");
        badges[3].ClassList.Should().Contain("tm-badge-info");
        badges[4].GetAttribute("data-badge-tone").Should().Be("accent");
        badges[4].ClassList.Should().Contain("tm-badge-primary");
        badges[5].GetAttribute("data-badge-tone").Should().Be("neutral");
        badges[5].ClassList.Should().Contain("tm-badge-default");
        cut.FindAll("[data-comment-id='c2'] [data-badge-tone]").Should().HaveCount(6);
    }

    [Fact]
    public void Comments_InternalAndEdited_ShowsBothCues()
    {
        var comments = TwoComments();
        comments[0].IsInternal = true;
        comments[0].EditedAt = DateTimeOffset.Now.AddMinutes(-5);
        comments[0].Badges = [new TmCommentBadge { Text = "Admin", Tone = TmCommentBadgeTone.Warning }];

        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, comments));

        var meta = cut.Find("[data-comment-id='c1'] .tm-comment-meta");
        meta.QuerySelector(".tm-comment-edited").Should().NotBeNull();
        meta.QuerySelector(".tm-comment-internal-label")!.TextContent.Should().Contain("Internal");
        meta.QuerySelector(".tm-comment-internal-label")!.GetAttribute("title")
            .Should().Be("Visible only to internal users");
        meta.QuerySelector("[data-badge-tone='warning']").Should().NotBeNull();
    }

    [Fact]
    public void Comments_InternalLabel_IsLocalizedInCzech()
    {
        UseCzechLocalization();
        var comments = TwoComments();
        comments[0].IsInternal = true;

        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, comments));

        var label = cut.Find(".tm-comment-internal-label");
        label.TextContent.Should().Contain("Interní");
        label.GetAttribute("title").Should().Be("Viditelné jen interním uživatelům");
        label.QuerySelector(".tm-sr-only")!.TextContent.Should().Be(": Viditelné jen interním uživatelům");
    }

    [Fact]
    public void ActivityCommentsCss_BadgeWidthAndInternalInset_ComeFromTokens()
    {
        var root = ThemeCss.RepositoryRoot().FullName;
        var css = File.ReadAllText(Path.Combine(root, "src", "Tempo.Blazor", "wwwroot", "css", "components", "_activity-comments.css"));
        var tokens = File.ReadAllText(Path.Combine(root, "src", "Tempo.Blazor", "wwwroot", "css", "tokens.css"));

        css.Should().Contain("max-width: var(--tm-comment-badge-max-width)");
        css.Should().NotContain("16rem");
        css.Should().Contain("margin-inline-start: calc(-1 * var(--tm-comment-internal-inset))");
        css.Should().Contain("IconNames.Lock");
        tokens.Should().Contain("--tm-comment-badge-max-width:");
        tokens.Should().Contain("--tm-comment-internal-inset:");
        tokens.Should().MatchRegex(@"--tm-comment-internal-inset:\s*calc\(var\(--tm-space-2\) \+ var\(--tm-space-1\)\)");
        tokens.Should().NotContain("--tm-comment-internal-fg");
    }

    [Fact]
    public void CommentActions_RenderedPerCommentWithEntryContext()
    {
        var seen = new List<string>();
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.CommentActions, entry => builder =>
            {
                seen.Add(entry.Id);
                builder.OpenElement(0, "button");
                builder.AddAttribute(1, "type", "button");
                builder.AddAttribute(2, "class", "tm-comment-custom-probe");
                builder.AddAttribute(3, "data-entry-id", entry.Id);
                builder.AddContent(4, entry.Author.DisplayName);
                builder.CloseElement();
            }));

        var items = cut.FindAll(".tm-comment-item");
        items.Should().HaveCount(2);
        seen.Should().Equal("c1", "c2");
        foreach (var item in items)
        {
            var actions = item.QuerySelector(".tm-comment-actions")!;
            var custom = actions.QuerySelector(".tm-comment-actions__custom")!;
            // Custom slot follows the built-in edit/delete controls, never precedes them.
            actions.LastElementChild.Should().BeSameAs(custom);
            var probe = custom.QuerySelector(".tm-comment-custom-probe")!;
            probe.GetAttribute("data-entry-id").Should().Be(item.GetAttribute("data-comment-id"));
        }

        items[0].QuerySelector(".tm-comment-edit-btn").Should().NotBeNull();
        items[1].QuerySelector(".tm-comment-edit-btn").Should().BeNull();
    }

    [Fact]
    public void CommentActions_ContainerRenderedWithoutEditDelete()
    {
        var comments = TwoComments();
        comments[1].CanEdit = false;
        comments[1].CanDelete = false;

        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, comments)
            .Add(c => c.CommentActions, entry => builder =>
            {
                builder.OpenElement(0, "button");
                builder.AddAttribute(1, "type", "button");
                builder.AddAttribute(2, "class", "tm-comment-custom-probe");
                builder.AddContent(3, entry.Id);
                builder.CloseElement();
            }));

        var bob = cut.FindAll(".tm-comment-item")[1];
        bob.QuerySelector(".tm-comment-edit-btn").Should().BeNull();
        bob.QuerySelector(".tm-comment-delete-btn").Should().BeNull();
        bob.QuerySelector(".tm-comment-actions .tm-comment-actions__custom .tm-comment-custom-probe")
            .Should().NotBeNull();
    }

    [Fact]
    public void CommentActions_HiddenWhileEditingThatComment()
    {
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.CommentActions, entry => builder =>
            {
                builder.OpenElement(0, "button");
                builder.AddAttribute(1, "type", "button");
                builder.AddAttribute(2, "class", "tm-comment-custom-probe");
                builder.AddAttribute(3, "data-entry-id", entry.Id);
                builder.CloseElement();
            }));

        cut.FindAll(".tm-comment-item")[0].QuerySelector(".tm-comment-edit-btn")!.Click();

        var editing = cut.Find("[data-comment-id='c1']");
        editing.QuerySelector(".tm-comment-actions").Should().BeNull();
        editing.QuerySelector(".tm-comment-actions__custom").Should().BeNull();
        editing.QuerySelector(".tm-comment-editor").Should().NotBeNull();

        var other = cut.Find("[data-comment-id='c2']");
        other.QuerySelector(".tm-comment-actions__custom .tm-comment-custom-probe")
            .GetAttribute("data-entry-id").Should().Be("c2");
    }

    [Fact]
    public void NoActions_NoEmptyContainer()
    {
        var comments = TwoComments();
        comments[1].CanEdit = false;
        comments[1].CanDelete = false;

        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, comments));

        cut.FindAll(".tm-comment-item")[0].QuerySelector(".tm-comment-actions").Should().NotBeNull();
        cut.FindAll(".tm-comment-item")[1].QuerySelector(".tm-comment-actions").Should().BeNull();
        cut.FindAll(".tm-comment-actions__custom").Should().BeEmpty();
    }

    [Fact]
    public void MentionProvider_PassedToComposerEditor()
    {
        var provider = Substitute.For<ITmPeopleProvider>();
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.SupportsMentions, true)
            .Add(c => c.MentionProvider, provider));

        cut.Find(".tm-comments-add-btn").Click();

        var editor = cut.FindComponent<TmRichEditorSimple>();
        editor.Instance.SupportsMentions.Should().BeTrue();
        editor.Instance.MentionProvider.Should().BeSameAs(provider);
    }

    [Fact]
    public void MentionProvider_PassedToInlineEditEditor()
    {
        var provider = Substitute.For<ITmPeopleProvider>();
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.SupportsMentions, true)
            .Add(c => c.MentionProvider, provider));

        cut.FindAll(".tm-comment-item")[0].QuerySelector(".tm-comment-edit-btn")!.Click();

        var editor = cut.FindComponent<TmRichEditorSimple>();
        editor.Instance.SupportsMentions.Should().BeTrue();
        editor.Instance.MentionProvider.Should().BeSameAs(provider);
    }

    [Fact]
    public void SupportsMentionsWithoutProvider_NoAutocomplete()
    {
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.SupportsMentions, true));

        cut.Find(".tm-comments-add-btn").Click();

        var editor = cut.FindComponent<TmRichEditorSimple>();
        editor.Instance.SupportsMentions.Should().BeFalse("autocomplete stays off without a provider; no exception");
        editor.Instance.MentionProvider.Should().BeNull();
        cut.FindAll(".tm-rte-mention-dropdown").Should().BeEmpty();
    }

    [Fact]
    public void InternalToggle_HiddenByDefault()
    {
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments()));

        cut.Find(".tm-comments-add-btn").Click();

        cut.FindAll(".tm-comment-internal-toggle").Should().BeEmpty();
        cut.Find(".tm-comment-editor").ClassList.Should().NotContain("tm-comment-editor--internal");
    }

    [Fact]
    public async Task InternalToggle_Submit_InvokesOnCommentSubmittedWithFlag_AndOnCommentAdded()
    {
        var order = new List<string>();
        string? added = null;
        TmCommentSubmission? submitted = null;
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.ShowInternalToggle, true)
            .Add(c => c.OnCommentAdded, EventCallback.Factory.Create<string>(this, html =>
            {
                order.Add("added");
                added = html;
            }))
            .Add(c => c.OnCommentSubmitted, EventCallback.Factory.Create<TmCommentSubmission>(this, submission =>
            {
                order.Add("submitted");
                submitted = submission;
            })));

        cut.Find(".tm-comments-add-btn").Click();
        cut.Find(".tm-comment-internal-toggle input").Change(true);
        await cut.InvokeAsync(() => cut.Instance.SetEditorContentForTestAsync("<p>Internal note</p>"));
        cut.Find(".tm-comment-submit-btn").Click();

        // Document order: OnCommentSubmitted, then OnCommentAdded, with the same HTML.
        order.Should().Equal("submitted", "added");
        added.Should().Be("<p>Internal note</p>");
        submitted.Should().NotBeNull();
        submitted!.Html.Should().Be(added);
        submitted.IsInternal.Should().BeTrue();
    }

    [Fact]
    public async Task InternalToggle_ResetAfterSubmit()
    {
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.ShowInternalToggle, true)
            .Add(c => c.OnCommentAdded, EventCallback.Factory.Create<string>(this, _ => { })));

        cut.Find(".tm-comments-add-btn").Click();
        cut.Find(".tm-comment-internal-toggle input").Change(true);
        await cut.InvokeAsync(() => cut.Instance.SetEditorContentForTestAsync("<p>once</p>"));
        cut.Find(".tm-comment-submit-btn").Click();

        cut.Find(".tm-comments-add-btn").Click();
        cut.Find(".tm-comment-internal-toggle input").HasAttribute("checked").Should().BeFalse();
        cut.Find(".tm-comment-editor").ClassList.Should().NotContain("tm-comment-editor--internal");
    }

    [Fact]
    public async Task InternalToggle_DisabledWhileSubmitting()
    {
        IRenderedComponent<TmActivityComments> cut = null!;
        cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.ShowInternalToggle, true)
            .Add(c => c.OnCommentAdded, EventCallback.Factory.Create<string>(this, _ =>
            {
                cut.Render();
                cut.Find(".tm-comment-internal-toggle input").HasAttribute("disabled").Should().BeTrue();
                cut.Find(".tm-comment-submit-btn").HasAttribute("disabled").Should().BeTrue();
                return Task.CompletedTask;
            })));

        cut.Find(".tm-comments-add-btn").Click();
        cut.Find(".tm-comment-internal-toggle input").Change(true);
        await cut.InvokeAsync(() => cut.Instance.SetEditorContentForTestAsync("<p>slow</p>"));
        cut.Find(".tm-comment-submit-btn").Click();
    }

    [Fact]
    public void InternalToggle_NotShownInInlineEdit()
    {
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.ShowInternalToggle, true));

        cut.FindAll(".tm-comment-item")[0].QuerySelector(".tm-comment-edit-btn")!.Click();

        cut.FindAll(".tm-comment-editor").Should().ContainSingle();
        cut.FindAll(".tm-comment-internal-toggle").Should().BeEmpty();
    }

    [Fact]
    public void Composer_InternalMode_HasModifierClass()
    {
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.ShowInternalToggle, true));

        cut.Find(".tm-comments-add-btn").Click();
        cut.Find(".tm-comment-editor").ClassList.Should().NotContain("tm-comment-editor--internal");

        cut.Find(".tm-comment-internal-toggle input").Change(true);

        cut.Find(".tm-comment-editor").ClassList.Should().Contain("tm-comment-editor--internal");
    }

    [Fact]
    public void InternalToggle_UsesLocalizedLabel_AndNativeSwitch()
    {
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.ShowInternalToggle, true));

        cut.Find(".tm-comments-add-btn").Click();

        var input = cut.Find(".tm-comment-internal-toggle input");
        input.GetAttribute("type").Should().Be("checkbox");
        input.GetAttribute("role").Should().Be("switch");
        cut.Find(".tm-comment-internal-toggle").TextContent.Should().Contain("Internal");
        cut.Markup.Should().Contain("Only internal users will see this comment.");
    }

    [Fact]
    public void InternalToggle_HintId_IsPerInstance_AndDescribesTheSwitch()
    {
        var first = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.ShowInternalToggle, true));
        var second = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.ShowInternalToggle, true));

        first.Find(".tm-comments-add-btn").Click();
        second.Find(".tm-comments-add-btn").Click();

        var hintA = first.Find(".tm-comment-internal-toggle-hint");
        var hintB = second.Find(".tm-comment-internal-toggle-hint");
        hintA.Id.Should().NotBe("tm-comment-internal-toggle-hint");
        hintA.Id.Should().NotBe(hintB.Id);
        first.Find(".tm-comment-internal-toggle input").GetAttribute("aria-describedby").Should().Be(hintA.Id);
    }

    [Fact]
    public async Task InlineEdit_Save_SetsSubmitting_SoASecondSaveIsIgnored()
    {
        var saves = 0;
        var release = new TaskCompletionSource();
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.OnCommentEdited, EventCallback.Factory.Create<(string Id, string Html)>(this, async _ =>
            {
                saves++;
                await release.Task;
            })));

        cut.FindAll(".tm-comment-item")[0].QuerySelector(".tm-comment-edit-btn")!.Click();
        var first = cut.InvokeAsync(() => cut.Find(".tm-comment-submit-btn").Click());
        cut.Find(".tm-comment-submit-btn").Click();
        release.SetResult();
        await first;

        saves.Should().Be(1);
    }

    [Fact]
    public async Task InternalToggle_DiscardDraft_ResetsSwitch()
    {
        var states = new List<TmCommentDraftState>();
        var cut = Render<TmActivityComments>(p => p
            .Add(c => c.Comments, TwoComments())
            .Add(c => c.ShowInternalToggle, true)
            .Add(c => c.OnDraftChanged, EventCallback.Factory.Create<TmCommentDraftState>(this, states.Add)));

        cut.Find(".tm-comments-add-btn").Click();
        cut.Find(".tm-comment-internal-toggle input").Change(true);

        states.Should().Contain(new TmCommentDraftState(true, null, false, true));

        await cut.InvokeAsync(() => cut.Instance.DiscardDraftAsync());

        states.Last().Should().Be(new TmCommentDraftState(false, null, false, false));
    }

    [Fact]
    public void CommentActions_CustomSlot_DoesNotEmulateActivation()
    {
        var source = File.ReadAllText(Path.Combine(
            ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor", "Components", "Activity", "TmActivityComments.razor"));
        var slot = source.IndexOf("tm-comment-actions__custom", StringComparison.Ordinal);
        slot.Should().BeGreaterThan(0);
        var window = source.Substring(slot, Math.Min(400, source.Length - slot));
        window.Should().NotContain("onkeydown");
        window.Should().NotContain("tabindex");
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
