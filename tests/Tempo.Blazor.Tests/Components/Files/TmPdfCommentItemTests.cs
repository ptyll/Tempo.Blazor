using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Files;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Files;

/// <summary>
/// bUnit tests for the shared <see cref="TmPdfCommentItem"/> (PdfViewer redesign phase 4):
/// TmAvatar with initials or image, a "⋯" TmDropdown menu with Edit and Delete instead of an
/// inline link, and inline editing (PD3) with Save/Cancel, Ctrl+Enter and Escape.
/// </summary>
public class TmPdfCommentItemTests : LocalizationTestBase
{
    private const string Base = "pdf-annotation";

    private static DocumentComment Comment(
        string id = "c1",
        string body = "original body",
        string authorId = "author-1",
        string authorName = "Alena Nováková",
        string? avatarUrl = null,
        bool canEdit = false,
        bool canDelete = true,
        DateTimeOffset? editedAt = null)
        => new()
        {
            Id = id,
            AuthorId = authorId,
            AuthorName = authorName,
            AuthorAvatarUrl = avatarUrl,
            Body = body,
            CanEdit = canEdit,
            CanDelete = canDelete,
            EditedAt = editedAt
        };

    private IRenderedComponent<TmPdfCommentItem> Render(DocumentComment comment, string? currentUserId = "user-2", Action<Bunit.ComponentParameterCollectionBuilder<TmPdfCommentItem>>? configure = null)
    {
        return Render<TmPdfCommentItem>(p =>
        {
            p.Add(x => x.ThreadId, "t1");
            p.Add(x => x.Comment, comment);
            p.Add(x => x.CurrentUserId, currentUserId);
            p.Add(x => x.TestIdBase, Base);
            configure?.Invoke(p);
        });
    }

    [Fact]
    public void RendersInitialsAvatar_WhenNoUrl()
    {
        var cut = Render(Comment(avatarUrl: null));

        var avatar = cut.Find(".tm-pdf-comment-item .tm-avatar-fallback");
        avatar.TextContent.Should().Be("AN", "initials come from the author name (first + last initial)");
        cut.FindAll(".tm-pdf-comment-item img").Should().BeEmpty();
    }

    [Fact]
    public void RendersImage_WhenAuthorAvatarUrl()
    {
        var cut = Render(Comment(avatarUrl: "https://example.com/alena.png"));

        var img = cut.Find(".tm-pdf-comment-item img");
        img.Attributes["src"]!.Value.Should().Be("https://example.com/alena.png");
        cut.FindAll(".tm-pdf-comment-item .tm-avatar-fallback").Should().BeEmpty();
    }

    [Fact]
    public void MoreMenu_Delete_RaisesDeleteRequest()
    {
        DocumentCommentDeleteRequest? deleted = null;
        var cut = Render(Comment(), configure: p => p
            .Add(x => x.OnDeleteRequested, EventCallback.Factory.Create<DocumentCommentDeleteRequest>(this, r => deleted = r)));

        OpenMenu(cut);
        cut.Find($"[data-testid='{Base}-delete']").Click();

        cut.WaitForAssertion(() => deleted.Should().NotBeNull("selecting the menu item raises OnDeleteRequested"));
        deleted!.ThreadId.Should().Be("t1");
        deleted.CommentId.Should().Be("c1");
    }

    [Fact]
    public void MoreMenu_EditHidden_WhenCannotEdit()
    {
        // Neither CanEdit flag nor author match → the Edit item is hidden, Delete stays.
        var cut = Render(Comment(canEdit: false, authorId: "author-1"), currentUserId: "user-2");
        OpenMenu(cut);
        cut.FindAll($"[data-testid='{Base}-edit']").Should().BeEmpty();
        cut.Find($"[data-testid='{Base}-delete']").Should().NotBeNull();

        // The author may edit their own comment (DocumentCommentPermissionHelper.CanEdit).
        var own = Render(Comment(canEdit: false, authorId: "author-1"), currentUserId: "author-1");
        OpenMenu(own);
        own.Find($"[data-testid='{Base}-edit']").Should().NotBeNull();
    }

    [Fact]
    public void MoreMenu_Hidden_WhenNoPermissions()
    {
        var cut = Render(Comment(canDelete: false), currentUserId: "user-2");
        cut.FindAll(".tm-pdf-comment-item__menu").Should().BeEmpty(
            "a comment the current user cannot edit nor delete renders no action menu");
    }

    [Fact]
    public void Edit_SaveRaisesEditRequest_EscCancels()
    {
        var requests = new List<DocumentCommentEditRequest>();
        var cut = Render(Comment(canEdit: true), configure: p => p
            .Add(x => x.OnEditRequested, EventCallback.Factory.Create<DocumentCommentEditRequest>(this, r => requests.Add(r))));

        OpenMenu(cut);
        cut.WaitForElement($"[data-testid='{Base}-edit']").Click();
        var input = cut.WaitForElement($"[data-testid='{Base}-edit-input']");
        input.Input("updated body");
        cut.Find($"[data-testid='{Base}-edit-save']").Click();

        requests.Should().HaveCount(1);
        requests[0].ThreadId.Should().Be("t1");
        requests[0].CommentId.Should().Be("c1");
        requests[0].Body.Should().Be("updated body");
        cut.FindAll($"[data-testid='{Base}-edit-input']").Should().BeEmpty("saving leaves edit mode");

        // Ctrl+Enter in the textarea saves as well.
        OpenMenu(cut);
        cut.WaitForElement($"[data-testid='{Base}-edit']").Click();
        cut.WaitForElement($"[data-testid='{Base}-edit-input']").Input("ctrl-enter body");
        cut.Find($"[data-testid='{Base}-edit-input']").KeyDown(new KeyboardEventArgs { Key = "Enter", CtrlKey = true });
        requests.Should().HaveCount(2);
        requests[1].Body.Should().Be("ctrl-enter body");

        // Escape cancels the edit without raising anything.
        OpenMenu(cut);
        cut.WaitForElement($"[data-testid='{Base}-edit']").Click();
        cut.WaitForElement($"[data-testid='{Base}-edit-input']").KeyDown("Escape");
        cut.FindAll($"[data-testid='{Base}-edit-input']").Should().BeEmpty("Escape leaves edit mode");
        requests.Should().HaveCount(2, "cancelling must not raise an edit request");
    }

    [Fact]
    public void EditedComment_ShowsEditedMarker()
    {
        var cut = Render(Comment(editedAt: DateTimeOffset.UtcNow));
        cut.Find(".tm-pdf-comment-item__edited").TextContent.Should().Contain("Edited");
    }

    [Fact]
    public void CommentIdChange_ClosesStaleInlineEdit()
    {
        var cut = Render(Comment(id: "c1", canEdit: true));
        OpenMenu(cut);
        cut.WaitForElement($"[data-testid='{Base}-edit']").Click();
        cut.WaitForElement($"[data-testid='{Base}-edit-input']").Input("stale draft");

        // The list reuses the item for a different comment (the @key keeps instances apart,
        // but a host may swap the Comment on the same instance): the edit must not leak.
        cut.InvokeAsync(async () => await cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object> { [nameof(TmPdfCommentItem.Comment)] = Comment(id: "c2", body: "other body", canEdit: true) })
        )).GetAwaiter().GetResult();

        cut.FindAll($"[data-testid='{Base}-edit-input']").Should().BeEmpty(
            "a changed Comment.Id closes the stale inline edit");
        cut.Find(".tm-pdf-comment-item__body").TextContent.Should().Be("other body");
    }

    /// <summary>Opens the comment's "⋯" action menu.</summary>
    private static void OpenMenu(IRenderedComponent<TmPdfCommentItem> cut)
        => cut.Find(".tm-pdf-comment-item__menu .tm-dropdown-trigger").Click();

    // ── Focus (phase 8 review 12) ────────────────────────────────────────────
    // Opening the editor focuses the textarea (ElementReference.FocusAsync surfaces in bUnit's
    // Loose JSInterop as "Blazor._internal.domWrapper.focus"); Save/Cancel returns focus to the
    // "⋯" trigger through the shared JS helper.

    private const string FocusInvocation = "Blazor._internal.domWrapper.focus";
    private const string FocusDescendantInvocation = "tmPdfAnnotator.focusDescendant";

    [Fact]
    public void Edit_FocusesTheEditInput()
    {
        var cut = Render(Comment(canEdit: true));

        OpenMenu(cut);
        cut.WaitForElement($"[data-testid='{Base}-edit']").Click();

        cut.WaitForAssertion(() =>
            JSInterop.Invocations.Count(i => i.Identifier == FocusInvocation).Should().BeGreaterThanOrEqualTo(1,
                "opening the editor focuses the edit textarea"));
    }

    [Fact]
    public void EditSave_ReturnsFocusToTheMenuTrigger()
    {
        var cut = Render(Comment(canEdit: true));

        OpenMenu(cut);
        cut.WaitForElement($"[data-testid='{Base}-edit']").Click();
        cut.WaitForElement($"[data-testid='{Base}-edit-input']").Input("updated body");
        cut.Find($"[data-testid='{Base}-edit-save']").Click();

        cut.WaitForAssertion(() =>
            JSInterop.Invocations.Count(i => i.Identifier == FocusDescendantInvocation).Should().BeGreaterThanOrEqualTo(1));
        var restore = JSInterop.Invocations.Last(i => i.Identifier == FocusDescendantInvocation);
        restore.Arguments[^1].Should().Be(".tm-dropdown-trigger",
            "saving returns focus to the comment's ⋯ trigger");
    }

    [Fact]
    public void EditCancel_ReturnsFocusToTheMenuTrigger()
    {
        var cut = Render(Comment(canEdit: true));

        OpenMenu(cut);
        cut.WaitForElement($"[data-testid='{Base}-edit']").Click();
        cut.WaitForElement($"[data-testid='{Base}-edit-input']").Input("draft");
        cut.Find($"[data-testid='{Base}-edit-cancel']").Click();

        cut.WaitForAssertion(() =>
            JSInterop.Invocations.Count(i => i.Identifier == FocusDescendantInvocation).Should().BeGreaterThanOrEqualTo(1));
        var restore = JSInterop.Invocations.Last(i => i.Identifier == FocusDescendantInvocation);
        restore.Arguments[^1].Should().Be(".tm-dropdown-trigger",
            "cancelling returns focus to the comment's ⋯ trigger");
    }

    // ── FZE-9: relative time through the shared ITmRelativeTimeFormatter ──

    private sealed class StubRelativeTimeFormatter(string relative, string absolute) : Tempo.Blazor.Localization.ITmRelativeTimeFormatter
    {
        public string Format(DateTimeOffset value, DateTimeOffset? now = null, TimeZoneInfo? timeZone = null) => relative;

        public string Absolute(DateTimeOffset value, TimeZoneInfo? timeZone = null) => absolute;
    }

    [Fact]
    public void UsesRelativeTimeFormatter_WithAbsoluteTitle()
    {
        var createdAt = new DateTimeOffset(2025, 6, 1, 9, 0, 0, TimeSpan.Zero);
        Services.AddSingleton<Tempo.Blazor.Localization.ITmRelativeTimeFormatter>(
            new StubRelativeTimeFormatter("5 min ago", "1. 6. 2025 9:00"));

        var cut = Render<TmPdfCommentItem>(p =>
        {
            p.Add(x => x.ThreadId, "t1");
            p.Add(x => x.Comment, new DocumentComment
            {
                Id = "c1",
                AuthorId = "author-1",
                AuthorName = "Alena Nováková",
                Body = "original body",
                CreatedAt = createdAt
            });
            p.Add(x => x.CurrentUserId, "user-2");
            p.Add(x => x.TestIdBase, Base);
        });

        var time = cut.Find(".tm-pdf-comment-item__time");
        time.TextContent.Should().Be("5 min ago", "the stamp comes from the shared relative-time formatter");
        time.GetAttribute("title").Should().Be("1. 6. 2025 9:00", "the absolute time is the tooltip");
        time.GetAttribute("datetime").Should().Be("2025-06-01T09:00:00+00:00",
            "the machine-readable instant stays in the datetime attribute");
    }
}
