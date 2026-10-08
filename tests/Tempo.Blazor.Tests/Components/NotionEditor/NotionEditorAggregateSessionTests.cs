using System.Text.Json;
using FluentAssertions;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

public sealed class NotionEditorAggregateSessionTests
{
    [Fact]
    public async Task MultiBlockMutation_UndoAndRedo_EachUseOneValidatedSave()
    {
        var provider = new RecordingAggregateProvider(Snapshot("token-1", "A", "B"));
        var session = new NotionEditorAggregateSession(provider);
        await session.LoadAsync(provider.PageId);

        var changed = await session.ApplyAsync(snapshot =>
            WithRowHtml(snapshot, "Merged", "Changed"));
        changed.Success.Should().BeTrue();
        provider.SaveRequests.Should().ContainSingle();
        provider.SaveRequests[0].Pages.Should().ContainSingle();
        provider.SaveRequests[0].Pages[0].BaseConcurrencyToken.Should().Be("token-1");
        RowHtml(session.CurrentSnapshot!, 0).Should().Be("Merged");
        RowHtml(session.CurrentSnapshot!, 1).Should().Be("Changed");

        var undone = await session.ApplyAsync(snapshot =>
            WithRowHtml(snapshot, "A", "B"));
        undone.Success.Should().BeTrue();
        provider.SaveRequests.Should().HaveCount(2);

        var redone = await session.ApplyAsync(snapshot =>
            WithRowHtml(snapshot, "Merged", "Changed"));
        redone.Success.Should().BeTrue();
        provider.SaveRequests.Should().HaveCount(3);
    }

    [Fact]
    public async Task Conflict_KeepsLocalCandidate_AndReapplyUsesFreshRemoteToken()
    {
        var provider = new RecordingAggregateProvider(Snapshot("token-1", "A", "B"))
        {
            ConflictNextSave = true
        };
        var session = new NotionEditorAggregateSession(provider);
        await session.LoadAsync(provider.PageId);

        var conflicted = await session.ApplyAsync(snapshot =>
            WithRowHtml(snapshot, "Local", "B"));

        conflicted.Conflict.Should().BeTrue();
        session.HasPendingConflict.Should().BeTrue();
        RowHtml(session.CurrentSnapshot!, 0).Should().Be("Local");

        provider.Remote = Snapshot("token-remote", "Remote", "B");
        var reapplied = await session.ReapplyAsync();

        reapplied.Success.Should().BeTrue();
        session.HasPendingConflict.Should().BeFalse();
        provider.SaveRequests.Should().HaveCount(2);
        provider.SaveRequests[1].Pages[0].BaseConcurrencyToken.Should().Be("token-remote");
        RowHtml(session.CurrentSnapshot!, 0).Should().Be("Local");
    }

    [Fact]
    public async Task InvalidCandidate_IsRejectedBeforeProviderSave()
    {
        var provider = new RecordingAggregateProvider(Snapshot("token-1", "A", "B"));
        var session = new NotionEditorAggregateSession(provider);
        await session.LoadAsync(provider.PageId);

        var result = await session.ApplyAsync(snapshot =>
        {
            snapshot.Blocks[0].Content = JsonSerializer.SerializeToElement("not-an-object");
            return snapshot;
        });

        result.Success.Should().BeFalse();
        result.Issues.Should().Contain(issue => issue.Code == "block_content_object_required");
        provider.SaveRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task OverlappingApplyAsync_QueuesSecondMutationBehindInFlightSave()
    {
        var provider = new RecordingAggregateProvider(Snapshot("token-1", "A", "B"))
        {
            SaveDelay = TimeSpan.FromMilliseconds(50),
            EnforceBaseTokens = true
        };
        var session = new NotionEditorAggregateSession(provider);
        await session.LoadAsync(provider.PageId);

        var first = session.ApplyAsync(snapshot => WithRowHtml(snapshot, "One", "B"));
        var second = session.ApplyAsync(snapshot => WithRowHtml(snapshot, "Two", "B"));

        var results = await Task.WhenAll(first, second);

        results.Should().OnlyContain(result => result.Success && !result.Conflict);
        provider.SaveRequests.Should().HaveCount(2);
        provider.SaveRequests[1].Pages[0].BaseConcurrencyToken.Should().Be("token-2");
        RowHtml(session.CurrentSnapshot!, 0).Should().Be("Two");
    }

    [Fact]
    public async Task StructuredPaste_InsertsAllBlocksWithOneAggregateSave()
    {
        var provider = new RecordingAggregateProvider(Snapshot("token-1", "A", "B"));
        var session = new NotionEditorAggregateSession(provider);
        await session.LoadAsync(provider.PageId);
        var rootTableId = session.CurrentSnapshot!.Blocks
            .Single(block => block.Type == BlockType.Table)
            .Id;
        IReadOnlyList<IPageBlock> pasted =
        [
            new PageBlock
            {
                Id = Guid.NewGuid(),
                PageId = provider.PageId,
                Type = BlockType.Heading1,
                Order = 1,
                Content = new HeadingBlockContent { Level = 1, Html = "Pasted title" }
            },
            new PageBlock
            {
                Id = Guid.NewGuid(),
                PageId = provider.PageId,
                Type = BlockType.Paragraph,
                Order = 2,
                Content = new TextBlockContent { Html = "Pasted body" }
            }
        ];

        var result = await session.ApplyAsync(snapshot =>
            NotionCanonicalBlockBridge.InsertBlocks(snapshot, pasted, rootTableId));

        result.Success.Should().BeTrue();
        provider.SaveRequests.Should().ContainSingle();
        session.CurrentSnapshot!.Blocks
            .Where(block => block.ParentBlockId is null)
            .OrderBy(block => block.Order)
            .Select(block => block.Type)
            .Should().Equal(BlockType.Table, BlockType.Heading1, BlockType.Paragraph);
    }

    private static NotionPageSnapshot Snapshot(string token, string first, string second)
    {
        var pageId = Guid.Parse("61000000-0000-0000-0000-000000000001");
        var tableId = Guid.Parse("61000000-0000-0000-0000-000000000010");
        return new NotionPageSnapshot
        {
            Page = new NotionPageState { Id = pageId, Title = "Atomic editor" },
            ConcurrencyToken = token,
            Digest = $"digest:{token}",
            Blocks =
            [
                Block(
                    tableId,
                    pageId,
                    null,
                    BlockType.Table,
                    0,
                    new NotionAuthoringTable { ColumnCount = 1 }),
                Block(
                    Guid.Parse("61000000-0000-0000-0000-000000000011"),
                    pageId,
                    tableId,
                    BlockType.TableRow,
                    0,
                    new NotionAuthoringTableRow
                    {
                        Cells = [new NotionAuthoringTableCell { Html = first }]
                    }),
                Block(
                    Guid.Parse("61000000-0000-0000-0000-000000000012"),
                    pageId,
                    tableId,
                    BlockType.TableRow,
                    1,
                    new NotionAuthoringTableRow
                    {
                        Cells = [new NotionAuthoringTableCell { Html = second }]
                    })
            ]
        };
    }

    private static NotionPageSnapshot WithRowHtml(
        NotionPageSnapshot snapshot,
        string first,
        string second)
    {
        var rows = snapshot.Blocks
            .Where(block => block.Type == BlockType.TableRow)
            .OrderBy(block => block.Order)
            .ToList();
        rows[0].Content = JsonSerializer.SerializeToElement(
            new NotionAuthoringTableRow
            {
                Cells = [new NotionAuthoringTableCell { Html = first }]
            },
            NotionAggregateJson.Options);
        rows[1].Content = JsonSerializer.SerializeToElement(
            new NotionAuthoringTableRow
            {
                Cells = [new NotionAuthoringTableCell { Html = second }]
            },
            NotionAggregateJson.Options);
        return snapshot;
    }

    private static string RowHtml(NotionPageSnapshot snapshot, int row)
        => snapshot.Blocks
            .Where(block => block.Type == BlockType.TableRow)
            .OrderBy(block => block.Order)
            .ElementAt(row)
            .Content.Deserialize<NotionAuthoringTableRow>(NotionAggregateJson.Options)!
            .Cells[0]
            .Html;

    private static NotionBlockSnapshot Block<T>(
        Guid id,
        Guid pageId,
        Guid? parentId,
        BlockType type,
        int order,
        T content)
        => new()
        {
            Id = id,
            PageId = pageId,
            ParentBlockId = parentId,
            Type = type,
            Order = order,
            Content = JsonSerializer.SerializeToElement(content, NotionAggregateJson.Options)
        };

    private sealed class RecordingAggregateProvider : INotionAggregateProvider
    {
        public RecordingAggregateProvider(NotionPageSnapshot initial)
        {
            Remote = initial;
            PageId = initial.Page.Id;
        }

        public Guid PageId { get; }
        public NotionPageSnapshot Remote { get; set; }
        public bool ConflictNextSave { get; set; }
        public bool EnforceBaseTokens { get; set; }
        public TimeSpan SaveDelay { get; set; }
        public List<NotionAggregateSaveRequest> SaveRequests { get; } = [];

        public Task<NotionAggregateLoadResult> LoadPageAsync(
            Guid pageId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new NotionAggregateLoadResult
            {
                Found = pageId == PageId,
                Snapshot = Clone(Remote)
            });

        public Task<NotionAggregateLoadResult> LoadBlockAsync(
            Guid blockId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new NotionAggregateLoadResult { Found = false });

        public async Task<NotionAggregateSaveResult> SaveAsync(
            NotionAggregateSaveRequest request,
            CancellationToken cancellationToken = default)
        {
            if (SaveDelay > TimeSpan.Zero)
            {
                await Task.Delay(SaveDelay, cancellationToken);
            }

            SaveRequests.Add(request);
            if (EnforceBaseTokens &&
                !string.Equals(
                    Remote.ConcurrencyToken,
                    request.Pages[0].BaseConcurrencyToken,
                    StringComparison.Ordinal))
            {
                return new NotionAggregateSaveResult
                {
                    Conflict = true,
                    Conflicts =
                    [
                        new NotionPageConflict
                        {
                            PageId = PageId,
                            ExpectedConcurrencyToken = request.Pages[0].BaseConcurrencyToken,
                            CurrentConcurrencyToken = Remote.ConcurrencyToken
                        }
                    ]
                };
            }

            if (ConflictNextSave)
            {
                ConflictNextSave = false;
                return new NotionAggregateSaveResult
                {
                    Conflict = true,
                    Conflicts =
                    [
                        new NotionPageConflict
                        {
                            PageId = PageId,
                            ExpectedConcurrencyToken = request.Pages[0].BaseConcurrencyToken,
                            CurrentConcurrencyToken = "token-remote"
                        }
                    ]
                };
            }

            Remote = Clone(request.Pages[0].Snapshot);
            Remote.ConcurrencyToken = $"token-{SaveRequests.Count + 1}";
            Remote.Digest = $"digest:{Remote.ConcurrencyToken}";
            return new NotionAggregateSaveResult
            {
                Success = true,
                Pages =
                [
                    new NotionSavedPage
                    {
                        PageId = PageId,
                        ConcurrencyToken = Remote.ConcurrencyToken,
                        Digest = Remote.Digest
                    }
                ]
            };
        }

        private static NotionPageSnapshot Clone(NotionPageSnapshot snapshot)
            => JsonSerializer.Deserialize<NotionPageSnapshot>(
                JsonSerializer.Serialize(snapshot, NotionAggregateJson.Options),
                NotionAggregateJson.Options)!;
    }
}
