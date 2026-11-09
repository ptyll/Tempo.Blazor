using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Text.Json;
using Tempo.Blazor.Components.NotionEditor.Blocks;
using Tempo.Blazor.Components.NotionEditor.Blocks.Text;
using Tempo.Blazor.Components.NotionEditor.Page;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.Configuration;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>
/// Phase 5 (N2): the aggregate-conflict banner stays inline with its test id and button order,
/// but exactly ONE action is the primary — "Reload server version" (the server copy wins per the
/// reference design); "Reapply my changes" stays secondary.
/// </summary>
public sealed class TmNotionPageConflictTests : LocalizationTestBase
{
    private static readonly Guid PageId = Guid.Parse("55555555-5555-5555-5555-555555555556");

    [Fact]
    public async Task AggregateConflictBanner_RendersExactlyOnePrimaryButton_ReloadStaysFirst()
    {
        var anchor = new PageBlock
        {
            Id = Guid.NewGuid(), PageId = PageId, Type = BlockType.Paragraph, Order = 0,
            Content = new TextBlockContent { Html = "anchor" }
        };

        var aggregateProvider = Substitute.For<INotionAggregateProvider>();
        var snapshot = new NotionPageSnapshot
        {
            Page = new NotionPageState { Id = PageId, Title = "Page" },
            ConcurrencyToken = "token-1",
            Blocks =
            [
                new NotionBlockSnapshot
                {
                    Id = anchor.Id,
                    PageId = PageId,
                    Type = anchor.Type,
                    Order = anchor.Order,
                    Content = JsonSerializer.SerializeToElement(
                        anchor.Content, anchor.Content.GetType(), NotionAggregateJson.Options)
                }
            ]
        };
        aggregateProvider.LoadPageAsync(PageId, Arg.Any<CancellationToken>())
            .Returns(new NotionAggregateLoadResult { Found = true, Snapshot = snapshot });
        aggregateProvider.SaveAsync(Arg.Any<NotionAggregateSaveRequest>(), Arg.Any<CancellationToken>())
            .Returns(new NotionAggregateSaveResult { Conflict = true, Issues = [] });
        var aggregateSession = new NotionEditorAggregateSession(aggregateProvider);
        (await aggregateSession.LoadAsync(PageId)).Success.Should().BeTrue();

        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddTempoBlazorNotionEditor();
        var provider = Substitute.For<INotionEditorBlockService>();
        provider.GetBlocksAsync(PageId.ToString()).Returns([anchor]);
        var context = new NotionEditorContext
        {
            BlockService = provider,
            AggregateSession = aggregateSession
        };
        var page = new NotionPage { Id = PageId, Title = "Page" };
        var cut = Render<TmNotionPage>(parameters => parameters
            .AddCascadingValue(context)
            .Add(p => p.Page, page));

        var pasted = new List<IPageBlock>
        {
            new PageBlock { Id = Guid.NewGuid(), PageId = PageId, Type = BlockType.Paragraph, Order = 0, Content = new TextBlockContent { Html = "pasted" } }
        };
        var list = cut.FindComponent<TmNotionBlockList>();
        await cut.InvokeAsync(() => list.Instance.OnInsertTemplateBlocksAfter
            .InvokeAsync((anchor.Id.ToString(), (IReadOnlyList<IPageBlock>)pasted)));

        var banner = cut.Find("[data-testid='notion-page-conflict']");
        var buttons = banner.QuerySelectorAll("button");
        buttons.Should().HaveCount(2, "the banner keeps exactly the two documented actions");

        // Exactly one primary action, and it is the server reload — order unchanged.
        banner.QuerySelectorAll(".tm-notion-load-error__retry--primary").Should().ContainSingle();
        buttons[0].ClassList.Should().Contain("tm-notion-load-error__retry--primary");
        buttons[0].TextContent.Trim().Should().Be("Reload server version");
        buttons[1].ClassList.Should().NotContain("tm-notion-load-error__retry--primary");
        buttons[1].TextContent.Trim().Should().Be("Reapply my changes");

        // F-03: the actions sit in their own second-row wrapper, not squeezed beside the message.
        banner.QuerySelector(".tm-notion-page__aggregate-actions").Should().NotBeNull(
            "the two actions render inside the aggregate-actions row");
        banner.QuerySelectorAll(".tm-notion-page__aggregate-actions button").Should().HaveCount(2);
    }

    [Fact]
    public async Task TypedSaveConflict_RendersPageConflictBanner()
    {
        var anchor = new PageBlock
        {
            Id = Guid.NewGuid(), PageId = PageId, Type = BlockType.Paragraph, Order = 0,
            Content = new TextBlockContent { Html = "typed" }
        };

        var aggregateProvider = Substitute.For<INotionAggregateProvider>();
        var snapshot = new NotionPageSnapshot
        {
            Page = new NotionPageState { Id = PageId, Title = "Page" },
            ConcurrencyToken = "token-1",
            Blocks =
            [
                new NotionBlockSnapshot
                {
                    Id = anchor.Id,
                    PageId = PageId,
                    Type = anchor.Type,
                    Order = anchor.Order,
                    Content = JsonSerializer.SerializeToElement(
                        anchor.Content, anchor.Content.GetType(), NotionAggregateJson.Options)
                }
            ]
        };
        aggregateProvider.LoadPageAsync(PageId, Arg.Any<CancellationToken>())
            .Returns(new NotionAggregateLoadResult { Found = true, Snapshot = snapshot });
        aggregateProvider.SaveAsync(Arg.Any<NotionAggregateSaveRequest>(), Arg.Any<CancellationToken>())
            .Returns(new NotionAggregateSaveResult { Conflict = true, Issues = [] });
        var aggregateSession = new NotionEditorAggregateSession(aggregateProvider);
        (await aggregateSession.LoadAsync(PageId)).Success.Should().BeTrue();
        (await aggregateSession.ApplyAsync(page => page)).Conflict.Should().BeTrue();

        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddTempoBlazorNotionEditor();
        var provider = Substitute.For<INotionEditorBlockService>();
        provider.GetBlocksAsync(PageId.ToString()).Returns([anchor]);
        provider.UpdateBlockAsync(Arg.Any<IPageBlock>())
            .Returns(_ => Task.FromException(new InvalidOperationException("Notion aggregate concurrency conflict.")));
        var context = new NotionEditorContext
        {
            BlockService = provider,
            AggregateSession = aggregateSession
        };
        var page = new NotionPage { Id = PageId, Title = "Page" };
        var cut = Render<TmNotionPage>(parameters => parameters
            .AddCascadingValue(context)
            .Add(p => p.Page, page));

        var text = cut.FindComponent<TmNotionTextBlock>();
        await cut.InvokeAsync(() => text.Instance.OnContentSaved.InvokeAsync("typed elsewhere"));

        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='notion-page-conflict']").Should().NotBeEmpty());
    }
}
