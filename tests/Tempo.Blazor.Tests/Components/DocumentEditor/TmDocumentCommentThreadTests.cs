using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.Localization;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

public class TmDocumentCommentThreadTests : LocalizationTestBase
{
    [Fact]
    public void EntryTime_GoesThroughTheSharedRelativeTimeFormatter()
    {
        // Redesign 146c7e1b, phase 6 (DE-2.5): no private ToString("g") any more.
        var formatter = Substitute.For<ITmRelativeTimeFormatter>();
        formatter.Format(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset?>(), Arg.Any<TimeZoneInfo?>()).Returns("3 min ago");
        formatter.Absolute(Arg.Any<DateTimeOffset>(), Arg.Any<TimeZoneInfo?>()).Returns("4 Mar 2026 10:30");
        Services.AddSingleton(formatter);

        var cut = Render<TmDocumentCommentThread>(parameters => parameters
            .Add(p => p.Comment, Comment(DocumentCommentStatus.Open)));

        var time = cut.Find(".tm-document-comment-entry__time");
        time.TextContent.Trim().Should().Be("3 min ago");
        time.GetAttribute("title").Should().Be("4 Mar 2026 10:30");
        time.GetAttribute("datetime").Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Thread_CollapsesResolvedCommentByDefault()
    {
        var cut = Render<TmDocumentCommentThread>(parameters => parameters
            .Add(p => p.Comment, Comment(DocumentCommentStatus.Resolved)));

        cut.Find("[data-testid='document-comment-thread-collapsed']").TextContent.Should().Contain("Resolved text");
        cut.FindAll("[data-testid='document-comment-entry']").Should().BeEmpty();
    }

    [Fact]
    public void Thread_ExpandsResolvedComment()
    {
        var cut = Render<TmDocumentCommentThread>(parameters => parameters
            .Add(p => p.Comment, Comment(DocumentCommentStatus.Resolved)));

        cut.Find("[data-testid='document-comment-expand']").Click();

        cut.Find("[data-testid='document-comment-thread-expanded']").TextContent.Should().Contain("Resolved text");
        cut.FindAll("[data-testid='document-comment-entry']").Should().HaveCount(1);
    }

    [Fact]
    public void Thread_ReopensResolvedCommentFromCollapsedState()
    {
        string? reopened = null;
        var comment = Comment(DocumentCommentStatus.Resolved);
        var cut = Render<TmDocumentCommentThread>(parameters => parameters
            .Add(p => p.Comment, comment)
            .Add(p => p.OnReopen, id => reopened = id));

        cut.Find("[data-testid='document-comment-reopen']").Click();

        reopened.Should().Be(comment.Id);
    }

    private static DocumentComment Comment(DocumentCommentStatus status)
        => new()
        {
            Id = "comment-1",
            Status = status,
            Anchor = new DocumentCommentAnchor { BlockId = "block-1" },
            Entries =
            [
                new DocumentCommentEntry
                {
                    Author = new DocumentEditorAuthor { Id = "author-1", DisplayName = "Author" },
                    Text = "Resolved text",
                    CreatedAt = DateTimeOffset.Parse("2026-05-17T08:00:00Z")
                }
            ]
        };
}
