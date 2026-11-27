using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

public class TmDocumentCommentRailTests : LocalizationTestBase
{
    [Fact]
    public void Rail_FiltersOpenResolvedMineAndAll()
    {
        var comments = new[]
        {
            Comment("open-mine", DocumentCommentStatus.Open, "me"),
            Comment("resolved-mine", DocumentCommentStatus.Resolved, "me"),
            Comment("open-other", DocumentCommentStatus.Open, "other")
        };

        Render(DocumentCommentFilter.Open, comments).FindAll("[data-testid='document-comment-thread']").Should().HaveCount(2);
        Render(DocumentCommentFilter.Resolved, comments).FindAll("[data-testid='document-comment-thread']").Should().HaveCount(1);
        Render(DocumentCommentFilter.Mine, comments).FindAll("[data-testid='document-comment-thread']").Should().HaveCount(2);
        Render(DocumentCommentFilter.All, comments).FindAll("[data-testid='document-comment-thread']").Should().HaveCount(3);
    }

    [Fact]
    public void Rail_RaisesFilterAndSortChanges()
    {
        DocumentCommentFilter? filter = null;
        DocumentCommentSortMode? sort = null;
        var cut = Render<TmDocumentCommentRail>(parameters => parameters
            .Add(p => p.Comments, new[] { Comment("open", DocumentCommentStatus.Open, "me") })
            .Add(p => p.CurrentAuthorId, "me")
            .Add(p => p.FilterChanged, value => filter = value)
            .Add(p => p.SortModeChanged, value => sort = value));

        cut.Find("[data-testid='document-comment-filter']").Change(DocumentCommentFilter.Mine.ToString());
        cut.Find("[data-testid='document-comment-sort']").Change(DocumentCommentSortMode.Time.ToString());

        filter.Should().Be(DocumentCommentFilter.Mine);
        sort.Should().Be(DocumentCommentSortMode.Time);
    }

    [Theory]
    [InlineData(1, "1 of 1 comment")]
    [InlineData(3, "3 of 3 comments")]
    public void Counter_UsesThePluralFamilyOfTheTotal_English(int total, string expected)
    {
        var comments = Enumerable.Range(1, total).Select(i => Comment($"c{i}", DocumentCommentStatus.Open, "me")).ToList();

        Render(DocumentCommentFilter.All, comments).Find(".tm-document-panel__meta").TextContent.Should().Be(expected);
    }

    [Theory]
    [InlineData(1, "1 z 1 komentáře")]
    [InlineData(3, "3 z 3 komentářů")]
    [InlineData(5, "5 z 5 komentářů")]
    public void Counter_Czech_DeclinesTheTotalAfterZ(int total, string expected)
    {
        UseCzechLocalization();
        var comments = Enumerable.Range(1, total).Select(i => Comment($"c{i}", DocumentCommentStatus.Open, "me")).ToList();

        Render(DocumentCommentFilter.All, comments).Find(".tm-document-panel__meta").TextContent.Should().Be(expected);
    }

    private IRenderedComponent<TmDocumentCommentRail> Render(DocumentCommentFilter filter, IReadOnlyList<DocumentComment> comments)
        => Render<TmDocumentCommentRail>(parameters => parameters
            .Add(p => p.Comments, comments)
            .Add(p => p.CurrentAuthorId, "me")
            .Add(p => p.Filter, filter));

    private static DocumentComment Comment(string id, DocumentCommentStatus status, string authorId)
        => new()
        {
            Id = id,
            Status = status,
            Anchor = new DocumentCommentAnchor { BlockId = id },
            Entries =
            [
                new DocumentCommentEntry
                {
                    Author = new DocumentEditorAuthor { Id = authorId, DisplayName = authorId },
                    Text = id,
                    CreatedAt = DateTimeOffset.Parse("2026-05-17T08:00:00Z")
                }
            ]
        };
}
