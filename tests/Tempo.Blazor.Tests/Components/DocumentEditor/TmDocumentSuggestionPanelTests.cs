using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

/// <summary>Redesign plan 146c7e1b, phase 6 (DE-2.4): the suggestion panel renders the SAME card as the revision panel.</summary>
public sealed class TmDocumentSuggestionPanelTests : LocalizationTestBase
{
    private static DocumentSuggestion Suggestion(string id, DocumentSuggestionType type, string? original = null, string? suggested = null) => new()
    {
        Id = id,
        Type = type,
        Author = new DocumentRevisionAuthor { Id = "reviewer-1", DisplayName = "Jan Prochazka" },
        OriginalText = original,
        SuggestedText = suggested,
        Status = DocumentSuggestionStatus.Pending
    };

    [Fact]
    public void Suggestion_RendersTheSharedReviewCard()
    {
        var cut = Render<TmDocumentSuggestionPanel>(parameters => parameters
            .Add(p => p.Suggestions, [Suggestion("s1", DocumentSuggestionType.InsertText, suggested: "new words")]));

        var item = cut.Find("[data-testid='document-suggestion-item']");
        item.ClassList.Should().Contain("tm-document-review-card").And.Contain("tm-document-suggestion-panel__item");
        item.GetAttribute("data-suggestion-id").Should().Be("s1");
        item.QuerySelector(".tm-avatar-fallback")!.TextContent.Should().Be("JP");
        item.QuerySelector(".tm-document-review-card__type")!.TextContent.Should().Be("Insert");
        item.QuerySelector("ins")!.TextContent.Should().Be("new words");
        cut.Find("[data-testid='document-suggestion-preview']").Should().NotBeNull();
    }

    [Fact]
    public void DeleteSuggestion_ShowsTheOriginalTextInADel()
    {
        var cut = Render<TmDocumentSuggestionPanel>(parameters => parameters
            .Add(p => p.Suggestions, [Suggestion("s1", DocumentSuggestionType.DeleteText, original: "old words")]));

        cut.Find("[data-testid='document-suggestion-item'] del").TextContent.Should().Be("old words");
    }

    [Fact]
    public void ReplaceSuggestion_ShowsTheSuggestedText_AsAnExcerpt()
    {
        var cut = Render<TmDocumentSuggestionPanel>(parameters => parameters
            .Add(p => p.Suggestions, [Suggestion("s1", DocumentSuggestionType.ReplaceText, original: "old", suggested: "new")]));

        cut.Find("[data-testid='document-suggestion-preview']").TextContent.Trim().Should().Be("new");
    }

    [Fact]
    public void AcceptReject_RaiseTheSuggestion_AndAreDisabledWhileBusy()
    {
        DocumentSuggestion? accepted = null;
        DocumentSuggestion? rejected = null;
        var cut = Render<TmDocumentSuggestionPanel>(parameters => parameters
            .Add(p => p.Suggestions, [Suggestion("s1", DocumentSuggestionType.InsertText, suggested: "x")])
            .Add(p => p.OnAcceptSuggestion, suggestion => accepted = suggestion)
            .Add(p => p.OnRejectSuggestion, suggestion => rejected = suggestion));

        cut.Find("[data-testid='document-suggestion-accept']").Click();
        cut.Find("[data-testid='document-suggestion-reject']").Click();
        accepted!.Id.Should().Be("s1");
        rejected!.Id.Should().Be("s1");

        var busy = Render<TmDocumentSuggestionPanel>(parameters => parameters
            .Add(p => p.Suggestions, [Suggestion("s1", DocumentSuggestionType.InsertText, suggested: "x")])
            .Add(p => p.IsBusy, true));
        busy.Find("[data-testid='document-suggestion-accept']").HasAttribute("disabled").Should().BeTrue();
        busy.Find("[data-testid='document-suggestion-reject']").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void SelectingTheCard_RaisesOnSelectSuggestion_AndAcceptDoesNotAlsoSelect()
    {
        var selected = 0;
        var cut = Render<TmDocumentSuggestionPanel>(parameters => parameters
            .Add(p => p.Suggestions, [Suggestion("s1", DocumentSuggestionType.InsertText, suggested: "x")])
            .Add(p => p.OnSelectSuggestion, _ => selected++));

        cut.Find("[data-testid='document-suggestion-accept']").Click();
        selected.Should().Be(0, "the action buttons do not bubble into a selection");

        cut.Find("[data-testid='document-suggestion-item'] .tm-document-review-card__summary").Click();
        selected.Should().Be(1);
    }

    [Fact]
    public void EmptyAndStatus_RenderTheirLines()
    {
        var empty = Render<TmDocumentSuggestionPanel>();
        empty.Find("[data-testid='document-suggestion-empty']").Should().NotBeNull();

        var withStatus = Render<TmDocumentSuggestionPanel>(parameters => parameters.Add(p => p.StatusMessage, "Saved"));
        withStatus.Find("[data-testid='document-suggestion-status']").TextContent.Should().Be("Saved");
    }
}
