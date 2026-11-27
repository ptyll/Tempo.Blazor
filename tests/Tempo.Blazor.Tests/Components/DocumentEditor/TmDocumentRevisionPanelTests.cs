using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.Localization;
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

    // ─── Redesign 146c7e1b, phase 6 (DE-2.4) ──────────────────────────────────

    private IRenderedComponent<TmDocumentRevisionPanel> RenderPanel(
        IEnumerable<DocumentRevision> revisions,
        Action<ComponentParameterCollectionBuilder<TmDocumentRevisionPanel>>? configure = null)
        => Render<TmDocumentRevisionPanel>(parameters =>
        {
            parameters.Add(p => p.Revisions, revisions.ToList()).Add(p => p.CanReview, true);
            configure?.Invoke(parameters);
        });

    [Fact]
    public void PreviewResolver_IsCalledPerRevision_AndItsTextRendered()
    {
        var asked = new List<string>();
        var cut = RenderPanel(
            [CreateRevision("revision-1", DocumentRevisionAction.Pending), CreateRevision("revision-2", DocumentRevisionAction.Pending, type: DocumentRevisionType.Deletion)],
            parameters => parameters.Add(p => p.PreviewResolver, revision =>
            {
                asked.Add(revision.Id);
                return new DocumentRevisionPreview { Text = $"text of {revision.Id}" };
            }));

        asked.Should().Contain(["revision-1", "revision-2"]);
        var previews = cut.FindAll(".tm-document-revision-panel__preview").Select(preview => preview.TextContent.Trim()).ToList();
        previews.Should().BeEquivalentTo(["text of revision-1", "text of revision-2"]);
    }

    [Fact]
    public void PreviewResolver_ReturningNull_FallsBackToThePayload()
    {
        var cut = RenderPanel(
            [CreateRevision("revision-1", DocumentRevisionAction.Pending)],
            parameters => parameters.Add(p => p.PreviewResolver, _ => null));

        cut.Find(".tm-document-revision-panel__preview").TextContent.Trim().Should().Be("Inserted");
    }

    [Fact]
    public void Insertion_RendersItsTextInAnIns_AndDeletionInADel()
    {
        var inserted = CreateRevision("revision-1", DocumentRevisionAction.Pending);
        inserted.PayloadJson = "added words";
        var deleted = CreateRevision("revision-2", DocumentRevisionAction.Pending, type: DocumentRevisionType.Deletion);
        deleted.PayloadJson = "removed words";

        var cut = RenderPanel([inserted, deleted]);

        cut.Find("[data-revision-id='revision-1'] ins").TextContent.Should().Be("added words");
        cut.FindAll("[data-revision-id='revision-1'] del").Should().BeEmpty();
        cut.Find("[data-revision-id='revision-2'] del").TextContent.Should().Be("removed words");
        cut.FindAll("[data-revision-id='revision-2'] ins").Should().BeEmpty();
    }

    [Theory]
    [InlineData("{\"markType\":\"Bold\",\"newActive\":true}", "Bold: on")]
    [InlineData("{\"markType\":\"italic\",\"newActive\":false}", "Italic: off")]
    [InlineData("{\"MarkType\":0,\"NewActive\":true}", "Bold: on")]
    [InlineData("{\"markType\":\"SmallCaps\",\"newActive\":true}", "Text formatting")]
    public void Formatting_ShowsAHumanDescription_FromTheFormattingPayload(string payload, string expected)
    {
        var revision = CreateRevision("revision-format", DocumentRevisionAction.Pending, type: DocumentRevisionType.Formatting);
        revision.PayloadJson = payload;

        var cut = RenderPanel([revision]);

        cut.Find(".tm-document-revision-panel__preview").TextContent.Trim().Should().Be(expected);
        cut.Markup.Should().NotContain("markType").And.NotContain("MarkType");
    }

    [Fact]
    public void Formatting_KeepsTheResolversExcerpt_AndAddsTheDescription()
    {
        var revision = CreateRevision("revision-format", DocumentRevisionAction.Pending, type: DocumentRevisionType.Formatting);
        revision.PayloadJson = "{\"markType\":\"Bold\",\"newActive\":true}";

        var cut = RenderPanel([revision], parameters => parameters
            .Add(p => p.PreviewResolver, _ => new DocumentRevisionPreview { Text = "Quarterly figures" }));

        var preview = cut.Find(".tm-document-revision-panel__preview");
        preview.QuerySelector(".tm-document-review-card__excerpt")!.TextContent.Should().Be("Quarterly figures");
        preview.QuerySelector(".tm-document-review-card__description")!.TextContent.Should().Be("Bold: on");
    }

    [Fact]
    public void Time_GoesThroughTheSharedRelativeTimeFormatter()
    {
        var formatter = Substitute.For<ITmRelativeTimeFormatter>();
        formatter.Format(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset?>(), Arg.Any<TimeZoneInfo?>()).Returns("2 hours ago");
        formatter.Absolute(Arg.Any<DateTimeOffset>(), Arg.Any<TimeZoneInfo?>()).Returns("12 Apr 2025 10:24");
        Services.AddSingleton(formatter);

        var cut = RenderPanel([CreateRevision("revision-1", DocumentRevisionAction.Pending)]);

        var time = cut.Find(".tm-document-review-card__time");
        time.TextContent.Should().Be("2 hours ago");
        time.GetAttribute("title").Should().Be("12 Apr 2025 10:24");
        time.GetAttribute("datetime").Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Avatar_ShowsInitials_WithoutAnAvatarUrl_AndThePictureWithOne()
    {
        var without = CreateRevision("revision-1", DocumentRevisionAction.Pending);
        without.Author = new DocumentRevisionAuthor { Id = "a1", DisplayName = "Jan Prochazka" };
        var with = CreateRevision("revision-2", DocumentRevisionAction.Pending);
        with.Author = new DocumentRevisionAuthor { Id = "a2", DisplayName = "Martina Kralova", AvatarUrl = "https://example.test/mk.png" };

        var cut = RenderPanel([without, with]);

        cut.Find("[data-revision-id='revision-1'] .tm-avatar-fallback").TextContent.Should().Be("JP");
        cut.Find("[data-revision-id='revision-2'] img.tm-avatar-image").GetAttribute("src").Should().Be("https://example.test/mk.png");
    }

    [Fact]
    public void Card_CarriesTheTypeAsWord_Icon_AndAccentClass()
    {
        var cut = RenderPanel(
        [
            CreateRevision("revision-ins", DocumentRevisionAction.Pending, type: DocumentRevisionType.Insertion),
            CreateRevision("revision-del", DocumentRevisionAction.Pending, type: DocumentRevisionType.Deletion),
            CreateRevision("revision-fmt", DocumentRevisionAction.Pending, type: DocumentRevisionType.Formatting)
        ]);

        var insertion = cut.Find("[data-revision-id='revision-ins']");
        var deletion = cut.Find("[data-revision-id='revision-del']");
        var formatting = cut.Find("[data-revision-id='revision-fmt']");

        insertion.ClassList.Should().Contain("tm-document-review-card--insertion");
        deletion.ClassList.Should().Contain("tm-document-review-card--deletion");
        formatting.ClassList.Should().Contain("tm-document-review-card--formatting");
        insertion.QuerySelector(".tm-document-review-card__type")!.TextContent.Should().Be("Inserted text");
        deletion.QuerySelector(".tm-document-review-card__type")!.TextContent.Should().Be("Deleted text");
        formatting.QuerySelector(".tm-document-review-card__type")!.TextContent.Should().Be("Formatting change");

        var iconMarkup = new[] { insertion, deletion, formatting }
            .Select(card => card.QuerySelector(".tm-document-review-card__type-icon svg")!.InnerHtml)
            .ToList();
        iconMarkup.Should().OnlyHaveUniqueItems("every type has its own icon");
    }

    [Fact]
    public void AcceptAll_IsThePrimaryBatchAction_AndRejectAllSecondary()
    {
        var cut = RenderPanel([CreateRevision("revision-1", DocumentRevisionAction.Pending)]);

        cut.Find("[data-testid='document-revision-accept-all']").ClassList.Should().Contain("tm-btn-primary");
        cut.Find("[data-testid='document-revision-reject-all']").ClassList.Should().Contain("tm-btn-secondary").And.NotContain("tm-btn-primary");
    }

    [Fact]
    public void ItemActions_StillRaiseTheRevision()
    {
        DocumentRevision? accepted = null;
        DocumentRevision? rejected = null;
        DocumentRevision? selected = null;
        var cut = RenderPanel([CreateRevision("revision-1", DocumentRevisionAction.Pending)], parameters => parameters
            .Add(p => p.OnAcceptRevision, revision => accepted = revision)
            .Add(p => p.OnRejectRevision, revision => rejected = revision)
            .Add(p => p.OnSelectRevision, revision => selected = revision));

        cut.Find("[data-testid='document-revision-accept']").Click();
        cut.Find("[data-testid='document-revision-reject']").Click();
        cut.Find(".tm-document-review-card__summary").Click();

        accepted!.Id.Should().Be("revision-1");
        rejected!.Id.Should().Be("revision-1");
        selected!.Id.Should().Be("revision-1");
    }

    [Fact]
    public void SelectedRevision_IsMarked()
    {
        var cut = RenderPanel([CreateRevision("revision-1", DocumentRevisionAction.Pending), CreateRevision("revision-2", DocumentRevisionAction.Pending)],
            parameters => parameters.Add(p => p.SelectedRevisionId, "revision-2"));

        cut.Find("[data-revision-id='revision-2']").GetAttribute("aria-current").Should().Be("true");
        cut.Find("[data-revision-id='revision-2']").ClassList.Should().Contain("tm-document-revision-panel__item--selected");
        cut.Find("[data-revision-id='revision-1']").GetAttribute("aria-current").Should().Be("false");
    }

    [Fact]
    public void TwoHundredRevisions_RenderInAWindow_WithinTheBudget_AndShowMoreReachesTheRest()
    {
        var revisions = Enumerable.Range(1, 200)
            .Select(i => CreateRevision($"revision-{i:000}", DocumentRevisionAction.Pending, authorId: $"author-{i % 7}"))
            .ToList();

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var cut = RenderPanel(revisions);
        stopwatch.Stop();

        cut.FindAll("[data-testid='document-revision-item']").Should().HaveCount(40, "only the first window is in the DOM");
        cut.Find(".tm-document-revision-panel__list").GetAttribute("data-total-count").Should().Be("200");
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(3000, "200 pending changes must not cost 200 cards of render time");

        var more = cut.Find("[data-testid='document-revision-show-more']");
        more.TextContent.Trim().Should().Be("Show 40 more");
        more.Click();
        cut.FindAll("[data-testid='document-revision-item']").Should().HaveCount(80);

        for (var i = 0; i < 3; i++)
        {
            cut.Find("[data-testid='document-revision-show-more']").Click();
        }

        cut.FindAll("[data-testid='document-revision-item']").Should().HaveCount(200);
        cut.FindAll("[data-testid='document-revision-show-more']").Should().BeEmpty();
    }

    [Fact]
    public void ASelectedRevisionBeyondTheFirstWindow_GrowsTheWindowSoItIsRendered()
    {
        // The list is newest first and CreateRevision stamps "now", so the LAST created revision is the first card: revision-110 of 200 is the 91st card.
        var revisions = Enumerable.Range(1, 200)
            .Select(i => CreateRevision($"revision-{i:000}", DocumentRevisionAction.Pending))
            .ToList();
        var cut = RenderPanel(revisions, parameters => parameters.Add(p => p.SelectedRevisionId, "revision-110"));

        cut.FindAll("[data-revision-id='revision-110']").Should().ContainSingle("the selected revision is never hidden behind 'Show more'");
        cut.Find("[data-revision-id='revision-110']").GetAttribute("aria-current").Should().Be("true");
        cut.FindAll("[data-testid='document-revision-item']").Should().HaveCount(120, "the window grew by whole pages (3 x 40) until it covered the selection");
        cut.FindAll("[data-testid='document-revision-show-more']").Should().ContainSingle();

        // A later selection of a card further down grows the window again; one inside the window changes nothing.
        cut.Render(parameters => parameters.Add(p => p.SelectedRevisionId, "revision-020"));
        cut.FindAll("[data-revision-id='revision-020']").Should().ContainSingle();
        cut.FindAll("[data-testid='document-revision-item']").Should().HaveCount(200);
        cut.Render(parameters => parameters.Add(p => p.SelectedRevisionId, "revision-199"));
        cut.FindAll("[data-testid='document-revision-item']").Should().HaveCount(200, "a selection inside the window never shrinks it");
    }

    [Fact]
    public void ReviewCard_TheAvatarSitsBesideTheSummaryButton_NotInsideIt()
    {
        var cut = RenderPanel([CreateRevision("revision-1", DocumentRevisionAction.Pending)]);

        var summary = cut.Find(".tm-document-review-card__summary");
        summary.QuerySelector(".tm-avatar").Should().BeNull("a <div> is not phrasing content inside a <button>");
        cut.Find(".tm-document-review-card__head").QuerySelector(":scope > .tm-avatar").Should().NotBeNull();
    }

    [Fact]
    public void FilteringAndPaging_ComputeTheListsOncePerParameterSet()
    {
        // The pending / filtered lists are fields refreshed in OnParametersSet and on a filter change, not properties re-sorted per access.
        var revisions = Enumerable.Range(1, 120)
            .Select(i => CreateRevision($"revision-{i:000}", DocumentRevisionAction.Pending, authorId: i % 2 == 0 ? "author-even" : "author-odd"))
            .ToList();
        var cut = RenderPanel(revisions);

        cut.Find("[data-testid='document-revision-filter-author']").Change("author-odd");
        cut.Find(".tm-document-revision-panel__list").GetAttribute("data-total-count").Should().Be("60");
        cut.Find("[data-testid='document-revision-filter-author']").Change(string.Empty);
        cut.Find(".tm-document-revision-panel__list").GetAttribute("data-total-count").Should().Be("120");
        cut.Render(parameters => parameters.Add(p => p.Revisions, revisions.Take(10).ToList()));
        cut.Find(".tm-document-revision-panel__list").GetAttribute("data-total-count").Should().Be("10", "new parameters refresh the cached lists");
    }

    [Fact]
    public void BatchActions_StillCarryTheCurrentFilter_AcrossWindows()
    {
        DocumentRevisionFilter? filter = null;
        var revisions = Enumerable.Range(1, 60)
            .Select(i => CreateRevision($"revision-{i:000}", DocumentRevisionAction.Pending, authorId: i % 2 == 0 ? "author-even" : "author-odd"))
            .ToList();
        var cut = RenderPanel(revisions, parameters => parameters.Add(p => p.OnRejectAllRevisions, value => filter = value));

        cut.Find("[data-testid='document-revision-filter-author']").Change("author-even");
        cut.FindAll("[data-testid='document-revision-item']").Should().HaveCount(30);
        cut.Find("[data-testid='document-revision-reject-all']").Click();

        filter!.AuthorId.Should().Be("author-even");
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
