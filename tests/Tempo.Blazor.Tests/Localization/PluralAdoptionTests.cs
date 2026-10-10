using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Tests.Localization;

/// <summary>
/// B16: counts in the document-review summary and the modeling issue panel use the grammatical
/// form of the count ("1 čekající změna", "2 čekající změny", "5 čekajících změn") instead of the
/// neutral "Čekající změny: N" stop-gap.
/// </summary>
public class PluralAdoptionTests : LocalizationTestBase
{
    [Theory]
    [InlineData(1, 1, "1 čekající změna, 1 otevřený komentář")]
    [InlineData(2, 3, "2 čekající změny, 3 otevřené komentáře")]
    [InlineData(5, 7, "5 čekajících změn, 7 otevřených komentářů")]
    public void Czech_review_summary_agrees_with_both_counts(int revisions, int comments, string expected)
    {
        UseCzechLocalization();

        var cut = Render<TmDocumentReviewSummary>(p => p
            .Add(c => c.PendingRevisionCount, revisions)
            .Add(c => c.OpenCommentCount, comments));

        cut.Find(".tm-document-review-summary__text span").TextContent.Should().Be(expected);
    }

    [Theory]
    [InlineData(1, "1 čekající změna")]
    [InlineData(2, "2 čekající změny")]
    [InlineData(9, "9 čekajících změn")]
    public void Czech_review_summary_buttons_agree_with_their_count(int revisions, string expected)
    {
        UseCzechLocalization();

        var cut = Render<TmDocumentReviewSummary>(p => p
            .Add(c => c.PendingRevisionCount, revisions)
            .Add(c => c.OpenCommentCount, 1));

        cut.Find("[data-testid='document-review-summary-revisions']").TextContent.Trim().Should().Be(expected);
    }

    [Theory]
    [InlineData(1, "1 pending change, 1 open comment")]
    [InlineData(3, "3 pending changes, 2 open comments")]
    public void English_review_summary_is_singular_and_plural(int revisions, string expected)
    {
        var cut = Render<TmDocumentReviewSummary>(p => p
            .Add(c => c.PendingRevisionCount, revisions)
            .Add(c => c.OpenCommentCount, revisions == 1 ? 1 : 2));

        cut.Find(".tm-document-review-summary__text span").TextContent.Should().Be(expected);
    }

    [Theory]
    [InlineData(1, "1 nález")]
    [InlineData(4, "4 nálezy")]
    [InlineData(6, "6 nálezů")]
    public void Czech_modeling_issue_header_agrees_with_the_count(int count, string expected)
    {
        UseCzechLocalization();
        var issues = Enumerable.Range(1, count)
            .Select(i => new ModelingIssueDto { Id = $"i{i}", Category = "Validation", Severity = ModelingIssueSeverity.Warning, Message = $"m{i}" })
            .ToList();

        var cut = Render<TmModelingIssuePanel>(p => p.Add(c => c.Issues, issues));

        cut.Find(".tm-modeling-issue-panel__header span").TextContent.Should().Be(expected);
    }
}