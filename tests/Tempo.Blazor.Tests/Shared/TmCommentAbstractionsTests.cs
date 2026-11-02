using FluentAssertions;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.DocumentEditor.Services;

namespace Tempo.Blazor.Tests.Shared;

public class TmCommentAbstractionsTests
{
    [Fact]
    public void TmCommentAnchor_TextRange_ValidatesOffsets()
    {
        var anchor = TmCommentAnchor.TextRange("block-1", 2, 8, "commented");

        anchor.Kind.Should().Be(TmCommentAnchorKind.TextRange);
        anchor.IsValid().Should().BeTrue();
    }

    [Fact]
    public void TmCommentEntry_Defaults_IsInternalFalse_BadgesEmpty()
    {
        var entry = new TmCommentEntry();

        entry.IsInternal.Should().BeFalse();
        entry.Badges.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void TmCommentBadge_IsValid_RequiresText()
    {
        new TmCommentBadge().IsValid.Should().BeFalse();
        new TmCommentBadge { Text = "   " }.IsValid.Should().BeFalse();
        new TmCommentBadge { Text = "Internal" }.IsValid.Should().BeTrue();
    }

    [Fact]
    public void TmCommentBadgeTone_HasExpectedMembers()
    {
        Enum.GetNames<TmCommentBadgeTone>().Should().Equal(
            "Neutral", "Info", "Success", "Warning", "Danger", "Accent");
        ((int)TmCommentBadgeTone.Neutral).Should().Be(0);
        ((int)TmCommentBadgeTone.Accent).Should().Be(5);
    }

    [Fact]
    public void TmCommentThread_IsValid_RequiresEntityAndId()
    {
        var thread = new TmCommentThread
        {
            Id = "comment-1",
            EntityRef = TmEntityRef.Create("work-item", "task-1"),
            Anchor = TmCommentAnchor.Block("description"),
            Entries =
            [
                new TmCommentEntry
                {
                    Id = "entry-1",
                    ThreadId = "comment-1",
                    Author = new TmUserRef { Id = "alice", DisplayName = "Alice" },
                    Body = "Looks good."
                }
            ]
        };

        thread.IsValid.Should().BeTrue();
        thread.Entries.Single().IsValid.Should().BeTrue();
    }

    [Fact]
    public void DocumentCommentBridge_RoundTripsDocumentEditorComment()
    {
        var comment = new DocumentComment
        {
            Id = "comment-1",
            Anchor = new DocumentCommentAnchor
            {
                Type = DocumentCommentAnchorType.TextRange,
                BlockId = "paragraph-1",
                StartInlineIndex = 0,
                StartOffset = 4,
                EndInlineIndex = 0,
                EndOffset = 12
            },
            Visibility = DocumentCommentVisibility.External,
            Entries =
            [
                new DocumentCommentEntry
                {
                    Id = "entry-1",
                    Author = new DocumentEditorAuthor { Id = "alice", DisplayName = "Alice" },
                    IsExternalAuthor = true,
                    Text = "Please review this range.",
                    CreatedAt = new DateTimeOffset(2026, 6, 21, 10, 0, 0, TimeSpan.Zero)
                }
            ]
        };

        var shared = DocumentCommentBridge.ToTmCommentThread(comment, "doc-1");
        var restored = DocumentCommentBridge.ToDocumentComment(shared);

        shared.EntityRef.EntityType.Should().Be(DocumentCommentBridge.EntityType);
        shared.Anchor!.Kind.Should().Be(TmCommentAnchorKind.TextRange);
        shared.Entries.Single().BodyFormat.Should().Be(TmCommentBodyFormat.PlainText);
        restored.Anchor.Type.Should().Be(DocumentCommentAnchorType.TextRange);
        restored.Visibility.Should().Be(DocumentCommentVisibility.External);
        restored.Entries.Single().IsExternalAuthor.Should().BeTrue();
    }

    [Fact]
    public async Task InMemoryDocumentEditorProvider_ExposesSharedCommentProvider()
    {
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedEmptyDocument("doc-1");
        ITmCommentProvider commentProvider = provider;

        var thread = await commentProvider.CreateThreadAsync(new TmCommentThread
        {
            Id = "thread-1",
            EntityRef = DocumentCommentBridge.Entity("doc-1"),
            Anchor = TmCommentAnchor.Block("block-1"),
            Entries =
            [
                new TmCommentEntry
                {
                    Id = "entry-1",
                    Author = new TmUserRef { Id = "alice", DisplayName = "Alice" },
                    Body = "Initial comment"
                }
            ]
        });

        await commentProvider.ReplyAsync(thread.Id, new TmCommentEntry
        {
            Id = "entry-2",
            Author = new TmUserRef { Id = "bob", DisplayName = "Bob" },
            Body = "Reply"
        });

        var comments = await commentProvider.GetForEntityAsync(DocumentCommentBridge.Entity("doc-1"));
        comments.Should().ContainSingle();
        comments.Single().Entries.Should().HaveCount(2);
    }
}
