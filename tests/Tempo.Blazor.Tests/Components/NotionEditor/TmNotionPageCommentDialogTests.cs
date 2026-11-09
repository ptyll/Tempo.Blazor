using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.NotionEditor;
using Tempo.Blazor.Components.NotionEditor.Page;
using Tempo.Blazor.Components.NotionEditor.UI;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.Configuration;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>
/// Phase 5 (P4): <c>PageCommentsPresentation=Dialog</c> — the page comment panel renders inside
/// a core <c>TmModal</c> (Embedded body) instead of inline; the header badge and the Comments
/// page action open it. Default (Inline) keeps the panel on the page.
/// </summary>
public sealed class TmNotionPageCommentDialogTests : LocalizationTestBase
{
    private static readonly Guid PageId = Guid.Parse("55555555-5555-5555-5555-555555555557");

    [Fact]
    public async Task DialogPresentation_PanelNotInline_BadgeOpensModalWithEmbeddedPanel()
    {
        var provider = new SeededCommentProvider();
        var cut = RenderPage(provider, NotionPageCommentsPresentation.Dialog);

        // Inline section absent; the header badge carries the count instead.
        cut.FindAll(".tm-notion-page__comments").Should().BeEmpty();
        var badge = cut.Find(".tm-notion-header-comment-badge");
        badge.TextContent.Trim().Should().Be("1");

        await badge.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        var overlay = cut.Find(".tm-modal-overlay");
        overlay.QuerySelector(".tm-npcp").Should().NotBeNull("the modal hosts the Embedded panel");
        cut.FindComponent<TmNotionPageCommentPanel>().Instance.Mode
            .Should().Be(TmNotionPageCommentPanelMode.Embedded);
    }

    [Fact]
    public async Task DialogClose_WithUnsavedDraft_AsksThenDiscards()
    {
        var provider = new SeededCommentProvider();
        var cut = RenderPage(provider, NotionPageCommentsPresentation.Dialog);
        await cut.Find(".tm-notion-header-comment-badge").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        var panel = cut.FindComponent<TmNotionPageCommentPanel>();
        cut.Find(".tm-npcp__new-comment .tm-npcp__reply-input").Input("half-written");
        panel.Instance.HasUnsavedDraft.Should().BeTrue();

        await cut.Find(".tm-modal-close").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        cut.FindAll(".tm-dialog").Should().ContainSingle("a draft asks before the dialog closes");
        cut.Find(".tm-modal-overlay .tm-npcp").Should().NotBeNull("cancel keeps the comments dialog");

        await cut.Find(".tm-dialog-btn-ok").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        cut.WaitForAssertion(() => cut.FindAll(".tm-modal-overlay").Should().BeEmpty());
    }

    [Fact]
    public async Task DialogClose_WithoutDraft_ClosesImmediately()
    {
        var provider = new SeededCommentProvider();
        var cut = RenderPage(provider, NotionPageCommentsPresentation.Dialog);
        await cut.Find(".tm-notion-header-comment-badge").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        await cut.Find(".tm-modal-close").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        cut.WaitForAssertion(() =>
        {
            cut.FindAll(".tm-dialog").Should().BeEmpty();
            cut.FindAll(".tm-modal-overlay").Should().BeEmpty();
        });
    }

    [Fact]
    public void InlinePresentation_PanelRendersInline_ByDefault()
    {
        var provider = new SeededCommentProvider();
        var cut = RenderPage(provider, NotionPageCommentsPresentation.Inline);

        cut.Find(".tm-notion-page__comments").Should().NotBeNull();
        cut.Find(".tm-npcp").Should().NotBeNull();
        cut.FindAll(".tm-modal-overlay").Should().BeEmpty();
    }

    private IRenderedComponent<TmNotionPage> RenderPage(SeededCommentProvider comments, NotionPageCommentsPresentation presentation)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddTempoBlazorNotionEditor();

        var blockService = Substitute.For<INotionEditorBlockService>();
        blockService.GetBlocksAsync(PageId.ToString()).Returns(
            [new PageBlock
            {
                Id = Guid.NewGuid(), PageId = PageId, Type = BlockType.Paragraph, Order = 0,
                Content = new TextBlockContent { Html = "body" }
            }]);

        var context = new NotionEditorContext
        {
            BlockService = blockService,
            CommentProvider = comments,
            PageCommentsPresentation = presentation
        };

        var page = new NotionPage { Id = PageId, Title = "Dialog page" };
        return Render<TmNotionPage>(parameters => parameters
            .AddCascadingValue(context)
            .Add(p => p.Page, page));
    }

    private sealed class SeededCommentProvider : ITmCommentProvider
    {
        public TmCommentProviderCapabilities Capabilities => TmCommentProviderCapabilities.Read;

        public Task<IReadOnlyList<TmCommentThread>> GetForEntityAsync(
            TmEntityRef entityRef, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<TmCommentThread> result =
                entityRef.EntityType == "notion-page" && entityRef.EntityId == PageId.ToString("D")
                ?
                [
                    new TmCommentThread
                    {
                        Id = "t1",
                        EntityRef = entityRef,
                        Anchor = TmCommentAnchor.None(),
                        Status = TmCommentThreadStatus.Open,
                        Entries =
                        [
                            new TmCommentEntry
                            {
                                Id = "e1",
                                ThreadId = "t1",
                                Author = new TmUserRef { Id = "u1", DisplayName = "User One" },
                                Body = "Unresolved page comment",
                                BodyFormat = TmCommentBodyFormat.Html,
                                CreatedAt = DateTimeOffset.UtcNow
                            }
                        ]
                    }
                ]
                : [];
            return Task.FromResult(result);
        }

        public Task<TmCommentThread> CreateThreadAsync(TmCommentThread thread, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<TmCommentEntry> ReplyAsync(string threadId, TmCommentEntry entry, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<TmCommentEntry> UpdateEntryAsync(string threadId, string entryId, TmCommentEntry entry, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteThreadAsync(string threadId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteEntryAsync(string threadId, string entryId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<TmCommentThread> ResolveAsync(string threadId, TmUserRef? resolvedBy = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<TmCommentThread> ReopenAsync(string threadId, TmUserRef? reopenedBy = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
