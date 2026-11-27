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
    [Fact]
    public void Summary_IsASingleStrip_WithALeadingIcon_AndIconPlusTextButtons()
    {
        var cut = Render<TmDocumentReviewSummary>(parameters => parameters
            .Add(p => p.PendingRevisionCount, 3)
            .Add(p => p.OpenCommentCount, 2));

        var strip = cut.Find("[data-testid='document-review-summary']");
        strip.TagName.Should().Be("SECTION");
        strip.GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace();
        strip.QuerySelector(".tm-document-review-summary__icon .tm-icon").Should().NotBeNull();
        strip.QuerySelector(".tm-document-review-summary__text strong")!.TextContent.Should().Be("Needs review");
        strip.Children.Select(child => child.ClassName).Should().Equal(
            "tm-document-review-summary__icon", "tm-document-review-summary__text", "tm-document-review-summary__actions");

        foreach (var testId in new[] { "document-review-summary-revisions", "document-review-summary-comments" })
        {
            var button = cut.Find($"[data-testid='{testId}']");
            button.QuerySelector(".tm-icon").Should().NotBeNull("the button carries an icon");
            button.TextContent.Trim().Should().NotBeEmpty("and its text");
            button.ClassList.Should().Contain("tm-btn");
        }
    }

    [Theory]
    [InlineData(1, "1 pending change")]
    [InlineData(2, "2 pending changes")]
    public void English_RevisionCount_UsesTheOneAndOtherForms(int pending, string expected)
    {
        var cut = Render<TmDocumentReviewSummary>(parameters => parameters
            .Add(p => p.PendingRevisionCount, pending)
            .Add(p => p.OpenCommentCount, 0));

        cut.Find("[data-testid='document-review-summary-revisions']").TextContent.Trim().Should().Be(expected);
    }

    [Theory]
    [InlineData(1, "1 otevřený komentář")]
    [InlineData(3, "3 otevřené komentáře")]
    [InlineData(5, "5 otevřených komentářů")]
    public void Czech_OpenComments_UseTheGrammaticalFamilyInTheSentence(int open, string expected)
    {
        UseCzechLocalization();

        var cut = Render<TmDocumentReviewSummary>(parameters => parameters
            .Add(p => p.PendingRevisionCount, 1)
            .Add(p => p.OpenCommentCount, open));

        cut.Find(".tm-document-review-summary__text span").TextContent.Should().EndWith(expected);
    }

    [Fact]
    public void Summary_RendersNothing_WhenThereIsNothingToReview()
    {
        var cut = Render<TmDocumentReviewSummary>();

        cut.FindAll("[data-testid='document-review-summary']").Should().BeEmpty();
    }
}
