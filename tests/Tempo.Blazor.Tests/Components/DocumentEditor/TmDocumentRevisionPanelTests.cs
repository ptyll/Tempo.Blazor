using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

public class TmDocumentRevisionPanelTests : LocalizationTestBase
{
    [Fact]
    public void Panel_RendersOnlyPendingRevisionCount()
    {
        var revisions = new[]
        {
            CreateRevision("revision-1", DocumentRevisionAction.Pending),
            CreateRevision("revision-2", DocumentRevisionAction.Accepted),
            CreateRevision("revision-3", DocumentRevisionAction.Pending)
        };

        var cut = Render<TmDocumentRevisionPanel>(parameters => parameters
            .Add(p => p.Revisions, revisions)
            .Add(p => p.CanReview, true));

        cut.FindAll("[data-testid='document-revision-item']").Should().HaveCount(2);
    }

    [Fact]
    public void Panel_DisablesAcceptRejectButtonsWhileReviewActionIsBusy()
    {
        var cut = Render<TmDocumentRevisionPanel>(parameters => parameters
            .Add(p => p.Revisions, new[] { CreateRevision("revision-1", DocumentRevisionAction.Pending) })
            .Add(p => p.CanReview, true)
            .Add(p => p.IsBusy, true));

        cut.Find("[data-testid='document-revision-accept']").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-testid='document-revision-reject']").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-testid='document-revision-accept']").GetAttribute("aria-disabled").Should().Be("true");
        cut.Find("[data-testid='document-revision-reject']").GetAttribute("aria-disabled").Should().Be("true");
        cut.Find("[data-testid='document-revision-accept']").ClassList
            .Should()
            .Contain("tm-document-revision-panel__action--accept");
        cut.Find("[data-testid='document-revision-reject']").ClassList
            .Should()
            .Contain("tm-document-revision-panel__action--reject");
    }

    [Fact]
    public void Panel_ReviewActionsUseSemanticHierarchyAndIcons()
    {
        var cut = Render<TmDocumentRevisionPanel>(parameters => parameters
            .Add(p => p.Revisions, new[] { CreateRevision("revision-1", DocumentRevisionAction.Pending) })
            .Add(p => p.CanReview, true));

        var acceptAll = cut.Find("[data-testid='document-revision-accept-all']");
        acceptAll.ClassList.Should().Contain("tm-document-revision-panel__batch-action--accept");
        acceptAll.GetAttribute("data-review-action").Should().Be("accept-all");
        acceptAll.GetAttribute("aria-disabled").Should().Be("false");
        acceptAll.QuerySelector(".tm-icon").Should().NotBeNull();

        var rejectAll = cut.Find("[data-testid='document-revision-reject-all']");
        rejectAll.ClassList.Should().Contain("tm-document-revision-panel__batch-action--reject");
        rejectAll.GetAttribute("data-review-action").Should().Be("reject-all");
        rejectAll.GetAttribute("aria-disabled").Should().Be("false");
        rejectAll.QuerySelector(".tm-icon").Should().NotBeNull();

        var accept = cut.Find("[data-testid='document-revision-accept']");
        accept.ClassList.Should().Contain("tm-document-revision-panel__action--accept");
        accept.GetAttribute("data-review-action").Should().Be("accept");
        accept.GetAttribute("aria-disabled").Should().Be("false");
        accept.QuerySelector(".tm-icon").Should().NotBeNull();

        var reject = cut.Find("[data-testid='document-revision-reject']");
        reject.ClassList.Should().Contain("tm-document-revision-panel__action--reject");
        reject.GetAttribute("data-review-action").Should().Be("reject");
        reject.GetAttribute("aria-disabled").Should().Be("false");
        reject.QuerySelector(".tm-icon").Should().NotBeNull();
    }

    [Fact]
    public void Panel_DisablesBatchReviewButtonsWhenUserCannotReview()
    {
        var cut = Render<TmDocumentRevisionPanel>(parameters => parameters
            .Add(p => p.Revisions, new[] { CreateRevision("revision-1", DocumentRevisionAction.Pending) })
            .Add(p => p.CanReview, false));

        cut.Find("[data-testid='document-revision-accept-all']").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-testid='document-revision-reject-all']").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-testid='document-revision-accept-all']").GetAttribute("aria-disabled").Should().Be("true");
        cut.Find("[data-testid='document-revision-reject-all']").GetAttribute("aria-disabled").Should().Be("true");
    }

    [Fact]
    public void Panel_FiltersPendingRevisionsByAuthorAndType()
    {
        var cut = Render<TmDocumentRevisionPanel>(parameters => parameters
            .Add(p => p.Revisions, new[]
            {
                CreateRevision("revision-1", DocumentRevisionAction.Pending, "author-1", DocumentRevisionType.Insertion),
                CreateRevision("revision-2", DocumentRevisionAction.Pending, "author-2", DocumentRevisionType.Deletion),
                CreateRevision("revision-3", DocumentRevisionAction.Pending, "author-1", DocumentRevisionType.Deletion)
            })
            .Add(p => p.CanReview, true));

        cut.Find("[data-testid='document-revision-filter-author']").Change("author-1");
        cut.Find("[data-testid='document-revision-filter-type']").Change(DocumentRevisionType.Deletion.ToString());

        cut.FindAll("[data-testid='document-revision-item']").Should().HaveCount(1);
        cut.Markup.Should().Contain("revision-3");
    }

    [Fact]
    public void Panel_RaisesAcceptAllWithCurrentFilter()
    {
        DocumentRevisionFilter? filter = null;
        var cut = Render<TmDocumentRevisionPanel>(parameters => parameters
            .Add(p => p.Revisions, new[]
            {
                CreateRevision("revision-1", DocumentRevisionAction.Pending, "author-1", DocumentRevisionType.Insertion),
                CreateRevision("revision-2", DocumentRevisionAction.Pending, "author-2", DocumentRevisionType.Deletion)
            })
            .Add(p => p.CanReview, true)
            .Add(p => p.OnAcceptAllRevisions, value => filter = value));

        cut.Find("[data-testid='document-revision-filter-author']").Change("author-2");
        cut.Find("[data-testid='document-revision-accept-all']").Click();

        filter.Should().NotBeNull();
        filter!.AuthorId.Should().Be("author-2");
    }

    [Theory]
    [InlineData(DocumentRevisionType.Formatting, "{\"markType\":\"Bold\",\"newActive\":true}")]
    [InlineData(DocumentRevisionType.Table, "{\"rows\":2}")]
    [InlineData(DocumentRevisionType.Structure, "splitBlock")]
    [InlineData(DocumentRevisionType.Structure, "InsertBlock")]
    [InlineData(DocumentRevisionType.Move, "[\"block-1\",\"block-2\"]")]
    [InlineData(DocumentRevisionType.Insertion, "{\"text\":\"Inserted\"}")]
    public void Preview_NeverRendersRawPayloadJson(DocumentRevisionType type, string payload)
    {
        var revision = CreateRevision("revision-structured", DocumentRevisionAction.Pending, type: type);
        revision.PayloadJson = payload;

        var cut = Render<TmDocumentRevisionPanel>(parameters => parameters
            .Add(p => p.Revisions, new[] { revision })
            .Add(p => p.CanReview, true));

        var preview = cut.Find(".tm-document-revision-panel__preview").TextContent;
        preview.Should().Be("Details available in the document", "opaque payloads show a localized line, never the payload (or a repeat of the type label)");
        cut.Markup.Should().NotContain("markType").And.NotContain("splitBlock").And.NotContain("InsertBlock");
    }

    [Theory]
    [InlineData(DocumentRevisionType.Insertion, "{x}")]
    [InlineData(DocumentRevisionType.Insertion, "42")]
    [InlineData(DocumentRevisionType.Deletion, "removed words")]
    public void Preview_ShowsPlainTextOfInsertionAndDeletion(DocumentRevisionType type, string text)
    {
        var revision = CreateRevision("revision-text", DocumentRevisionAction.Pending, type: type);
        revision.PayloadJson = text;

        var cut = Render<TmDocumentRevisionPanel>(parameters => parameters
            .Add(p => p.Revisions, new[] { revision })
            .Add(p => p.CanReview, true));

        cut.Find(".tm-document-revision-panel__preview").TextContent.Should().Be(text);
    }

    [Fact]
    public void Preview_KeepsPlainTextPayloadOfTextRevisions()
    {
        var cut = Render<TmDocumentRevisionPanel>(parameters => parameters
            .Add(p => p.Revisions, new[] { CreateRevision("revision-text", DocumentRevisionAction.Pending) })
            .Add(p => p.CanReview, true));

        cut.Find(".tm-document-revision-panel__preview").TextContent.Should().Be("Inserted");
    }

    [Fact]
    public void Preview_WithoutPayload_ShowsUnavailableText()
    {
        var revision = CreateRevision("revision-empty", DocumentRevisionAction.Pending);
        revision.PayloadJson = null;

        var cut = Render<TmDocumentRevisionPanel>(parameters => parameters
            .Add(p => p.Revisions, new[] { revision })
            .Add(p => p.CanReview, true));

        cut.Find(".tm-document-revision-panel__preview").TextContent.Should().Be("No preview available");
    }

    private static DocumentRevision CreateRevision(
        string id,
        DocumentRevisionAction action,
        string authorId = "reviewer-1",
        DocumentRevisionType type = DocumentRevisionType.Insertion)
        => new()
        {
            Id = id,
            Type = type,
            Action = action,
            Author = new DocumentRevisionAuthor { Id = authorId, DisplayName = authorId },
            Range = new DocumentRevisionRange { BlockId = "block-1" },
            PayloadJson = "Inserted"
        };
}
