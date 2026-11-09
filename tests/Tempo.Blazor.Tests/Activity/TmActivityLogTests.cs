using Bunit;
using FluentAssertions;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.Activity;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Activity;

// Uses the same file-scoped record types declared in the other test files via
// separate namespaces; we define local ones here.
file record LogEntry(
    string Id,
    string EntryType,
    string AuthorName,
    string? AuthorAvatarUrl,
    DateTimeOffset CreatedAt,
    string? HtmlContent,
    string? PlainContent,
    bool IsInternal = false,
    IReadOnlyDictionary<string, string>? Metadata = null) : ITimelineEntry;

public class TmActivityLogTests : LocalizationTestBase
{
    private static IReadOnlyList<ITimelineEntry> SampleEntries() =>
    [
        new LogEntry("e1", "comment", "Alice", null, DateTimeOffset.Now.AddHours(-1), null, "Hello"),
    ];

    private static IReadOnlyList<TmCommentEntry> SampleComments() =>
    [
        new TmCommentEntry
        {
            Id = "c1",
            ThreadId = "c1",
            Author = new TmUserRef { DisplayName = "Bob" },
            CreatedAt = DateTimeOffset.Now.AddMinutes(-30),
            Body = "<p>Hi</p>",
            BodyFormat = TmCommentBodyFormat.Html
        },
    ];

    [Fact]
    public void ActivityLog_RendersTimelineTab()
    {
        var cut = Render<TmActivityLog>(p => p
            .Add(c => c.TimelineEntries, SampleEntries()));

        cut.FindAll(".tm-activity-tab").Should().Contain(t => t.TextContent.Contains("Timeline"));
    }

    [Fact]
    public void ActivityLog_RendersCommentsTab()
    {
        var cut = Render<TmActivityLog>(p => p
            .Add(c => c.Comments, SampleComments()));

        cut.FindAll(".tm-activity-tab").Should().Contain(t => t.TextContent.Contains("Comments") || t.TextContent.Contains("Komentáře"));
    }

    [Fact]
    public void ActivityLog_RendersAttachmentsTab()
    {
        var cut = Render<TmActivityLog>();

        cut.FindAll(".tm-activity-tab").Should().Contain(t =>
            t.TextContent.Contains("Attachments") || t.TextContent.Contains("Přílohy"));
    }

    [Fact]
    public void ActivityLog_DefaultTab_IsTimeline()
    {
        var cut = Render<TmActivityLog>(p => p
            .Add(c => c.TimelineEntries, SampleEntries()));

        // Timeline content visible by default
        cut.FindAll(".tm-activity-content .tm-timeline").Should().NotBeEmpty();
    }

    [Fact]
    public async Task ActivityLog_RefreshAsync_RefreshesAllTabs()
    {
        var cut = Render<TmActivityLog>(p => p
            .Add(c => c.TimelineEntries, SampleEntries())
            .Add(c => c.Comments, SampleComments()));

        // Should not throw
        await cut.InvokeAsync(() => cut.Instance.RefreshAsync());

        cut.FindAll(".tm-activity-tab").Count.Should().Be(3);
    }

    [Fact]
    public void ActivityLog_HideTab_HidesCorrectTab()
    {
        var cut = Render<TmActivityLog>(p => p
            .Add(c => c.ShowComments, false));

        var tabs = cut.FindAll(".tm-activity-tab");
        tabs.Should().NotContain(t =>
            t.TextContent.Contains("Comments") || t.TextContent.Contains("Komentáře"));
    }

    [Fact]
    public void TmActivityLog_ForwardsCommentHandlers()
    {
        Func<TmCommentSubmission, Task<bool>> submitHandler = _ => Task.FromResult(true);
        Func<TmCommentEdit, Task<bool>> editHandler = _ => Task.FromResult(true);

        var cut = Render<TmActivityLog>(p => p
            .Add(c => c.TimelineEntries, SampleEntries())
            .Add(c => c.Comments, SampleComments())
            .Add(c => c.CommentSubmitHandler, submitHandler)
            .Add(c => c.CommentEditHandler, editHandler));

        // Comments tab content mounts on demand — switch to it.
        cut.Find("#tm-activity-tab-comments").Click();

        var comments = cut.FindComponent<TmActivityComments>();
        comments.Instance.CommentSubmitHandler.Should().BeSameAs(submitHandler);
        comments.Instance.CommentEditHandler.Should().BeSameAs(editHandler);
    }

    [Fact]
    public async Task TmActivityLog_CommentSubmitHandlerFalse_KeepsDraft()
    {
        var cut = Render<TmActivityLog>(p => p
            .Add(c => c.TimelineEntries, SampleEntries())
            .Add(c => c.Comments, SampleComments())
            .Add(c => c.CommentSubmitHandler, _ => Task.FromResult(false)));

        cut.Find("#tm-activity-tab-comments").Click();
        var comments = cut.FindComponent<TmActivityComments>();

        comments.Find(".tm-comments-add-btn").Click();
        await cut.InvokeAsync(() => comments.Instance.SetEditorContentForTestAsync("<p>draft text</p>"));
        comments.Find(".tm-comment-submit-btn").Click();

        // The failed save keeps the composer open with the draft and no comment is appended.
        comments.FindAll(".tm-comment-editor").Should().NotBeEmpty();
        comments.Instance.HasUnsavedDraft.Should().BeTrue();
        cut.FindComponent<TmRichEditorSimple>().Instance.Value
            .Should().Contain("draft text");
        comments.Instance.Comments.Should().HaveCount(1);
    }
}
