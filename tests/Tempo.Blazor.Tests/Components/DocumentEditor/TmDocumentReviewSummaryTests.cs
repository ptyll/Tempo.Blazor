using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

public class TmDocumentReviewSummaryTests : LocalizationTestBase
{
    [Fact]
    public void Summary_RendersPendingChangesAndComments()
    {
        var cut = Render<TmDocumentReviewSummary>(parameters => parameters
            .Add(p => p.PendingRevisionCount, 3)
            .Add(p => p.OpenCommentCount, 2));

        cut.Find("[data-testid='document-review-summary']").TextContent.Should().Contain("3 pending changes, 2 open comments");
    }

    [Theory]
    [InlineData(1, "1 čekající změna")]
    [InlineData(2, "2 čekající změny")]
    [InlineData(5, "5 čekajících změn")]
    public void Czech_ReviewSummary_UsesGrammaticalPluralForms(int pending, string expectedRevisions)
    {
        // Phase 1 / G11 (a): the Czech wording must be grammatical for every count. The summary already renders the
        // count through the plural families (Loc.Plural), never "1 čekajících změn" or a fixed genitive plural.
        UseCzechLocalization();

        var cut = Render<TmDocumentReviewSummary>(parameters => parameters
            .Add(p => p.PendingRevisionCount, pending)
            .Add(p => p.OpenCommentCount, 1));

        var text = cut.Find(".tm-document-review-summary__text span").TextContent;
        text.Should().StartWith(expectedRevisions);
        text.Should().NotContain("1 čekajících").And.NotContain("1 komentářů");
        cut.Find("[data-testid='document-review-summary-revisions']").TextContent.Trim().Should().Be(expectedRevisions);
        cut.Find("[data-testid='document-review-summary-comments']").TextContent.Trim().Should().Be("1 komentář");
    }

    [Fact]
    public void Summary_InvokesPanelActions()
    {
        var openedComments = false;
        var openedRevisions = false;
        var cut = Render<TmDocumentReviewSummary>(parameters => parameters
            .Add(p => p.PendingRevisionCount, 1)
            .Add(p => p.OpenCommentCount, 1)
            .Add(p => p.OpenComments, () => openedComments = true)
            .Add(p => p.OpenRevisions, () => openedRevisions = true));

        cut.Find("[data-testid='document-review-summary-revisions']").Click();
        cut.Find("[data-testid='document-review-summary-comments']").Click();

        openedRevisions.Should().BeTrue();
        openedComments.Should().BeTrue();
    }
}
