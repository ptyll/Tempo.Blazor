using System.Text.Json;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;
using Tempo.Blazor.NotionEditor.Testing;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

public sealed class NotionEditorBlockServiceTests
{
    [Fact]
    public async Task CreateAndDelete_CommitOneCompleteSnapshotPerMutation()
    {
        var pageId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var first = Block(pageId, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), null, 0, "first");
        var provider = new FakeNotionAggregateProvider([Snapshot(pageId, first)]);
        var service = new NotionEditorBlockService(provider);

        var created = await service.CreateBlockAsync(
            pageId.ToString("D"),
            new PageBlock
            {
                Id = Guid.NewGuid(),
                PageId = pageId,
                Type = BlockType.Paragraph,
                Content = new TextBlockContent { Html = "second" }
            },
            first.Id.ToString("D"));

        provider.SaveCallCount.Should().Be(1);
        var afterCreate = provider.GetSnapshot(pageId);
        afterCreate.Blocks.OrderBy(block => block.Order)
            .Select(block => block.Id)
            .Should().Equal(first.Id, created.Id);

        await service.DeleteBlockAsync(created.Id.ToString("D"));

        provider.SaveCallCount.Should().Be(2);
        provider.GetSnapshot(pageId).Blocks.Should().ContainSingle()
            .Which.Id.Should().Be(first.Id);
    }

    [Fact]
    public async Task DeleteContainer_RemovesItsWholeSubtreeInOneSave()
    {
        var pageId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var parent = Block(pageId, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), null, 0, "parent");
        var child = Block(pageId, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), parent.Id, 0, "child");
        var grandchild = Block(pageId, Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), child.Id, 0, "grandchild");
        var provider = new FakeNotionAggregateProvider([Snapshot(pageId, parent, child, grandchild)]);
        var service = new NotionEditorBlockService(provider);

        await service.DeleteBlockAsync(parent.Id.ToString("D"));

        provider.LoadCallCount.Should().Be(1);
        provider.SaveCallCount.Should().Be(1);
        provider.GetSnapshot(pageId).Blocks.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateAndDelete_MergesBlocksInOneSave()
    {
        var pageId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var first = Block(pageId, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), null, 0, "first");
        var second = Block(pageId, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), null, 1, "second");
        var provider = new FakeNotionAggregateProvider([Snapshot(pageId, first, second)]);
        var service = new NotionEditorBlockService(provider);
        var merged = new PageBlock
        {
            Id = first.Id,
            PageId = pageId,
            Type = BlockType.Paragraph,
            Content = new TextBlockContent { Html = "first second" }
        };

        await service.UpdateBlockAndDeleteAsync(merged, second.Id.ToString("D"));

        provider.SaveCallCount.Should().Be(1);
        var saved = provider.GetSnapshot(pageId).Blocks.Should().ContainSingle().Which;
        saved.Id.Should().Be(first.Id);
        saved.Content.Deserialize<TextBlockContent>(NotionAggregateJson.Options)!
            .Html.Should().Be("first second");
    }

    [Fact]
    public async Task MoveAcrossPages_CommitsBothCompleteSnapshotsAtomically()
    {
        var sourcePageId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var targetPageId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var parent = Block(sourcePageId, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), null, 0, "parent");
        var child = Block(sourcePageId, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), parent.Id, 0, "child");
        var target = Block(targetPageId, Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), null, 0, "target");
        var provider = new FakeNotionAggregateProvider(
        [
            Snapshot(sourcePageId, parent, child),
            Snapshot(targetPageId, target)
        ]);
        var service = new NotionEditorBlockService(provider);

        await service.MoveBlockToPageAsync(
            parent.Id.ToString("D"),
            targetPageId.ToString("D"),
            target.Id.ToString("D"));

        provider.SaveCallCount.Should().Be(1);
        provider.LastSaveRequest!.Pages.Should().HaveCount(2);
        provider.GetSnapshot(sourcePageId).Blocks.Should().BeEmpty();
        var moved = provider.GetSnapshot(targetPageId).Blocks
            .Where(block => block.Id == parent.Id || block.Id == child.Id)
            .ToList();
        moved.Should().HaveCount(2);
        moved.Should().OnlyContain(block => block.PageId == targetPageId);
        moved.Single(block => block.Id == child.Id).ParentBlockId.Should().Be(parent.Id);
    }

    [Fact]
    public async Task ActiveEditorSession_UsesLoadedSnapshotAndOneSave()
    {
        var pageId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var first = Block(pageId, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), null, 0, "first");
        var provider = new FakeNotionAggregateProvider([Snapshot(pageId, first)]);
        var session = new NotionEditorAggregateSession(provider);
        (await session.LoadAsync(pageId)).Success.Should().BeTrue();
        var service = new NotionEditorBlockService(provider, session);

        var created = await service.CreateBlockAsync(
            pageId.ToString("D"),
            new PageBlock
            {
                Id = Guid.NewGuid(),
                PageId = pageId,
                Type = BlockType.Paragraph,
                Content = new TextBlockContent { Html = "second" }
            },
            first.Id.ToString("D"));

        provider.SaveCallCount.Should().Be(1);
        provider.LoadCallCount.Should().Be(1);
        session.CurrentSnapshot!.Blocks.Should().Contain(block => block.Id == created.Id);
    }

    [Fact]
    public async Task ActiveEditorSession_ConflictRetainsCompleteLocalCandidate()
    {
        var pageId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var blockId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var provider = new FakeNotionAggregateProvider(
            [Snapshot(pageId, Block(pageId, blockId, null, 0, "baseline"))]);
        var session = new NotionEditorAggregateSession(provider);
        (await session.LoadAsync(pageId)).Success.Should().BeTrue();
        var service = new NotionEditorBlockService(provider, session);
        var remote = provider.GetSnapshot(pageId);
        remote.ConcurrencyToken = "remote-token";
        remote.Blocks.Single().Content = JsonSerializer.SerializeToElement(
            new TextBlockContent { Html = "remote" },
            NotionAggregateJson.Options);
        provider.Seed(remote);

        var update = new PageBlock
        {
            Id = blockId,
            PageId = pageId,
            Type = BlockType.Paragraph,
            Content = new TextBlockContent { Html = "local" }
        };
        var action = () => service.UpdateBlockAsync(update);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*concurrency conflict*");
        provider.SaveCallCount.Should().Be(1);
        session.HasPendingConflict.Should().BeTrue();
        session.CurrentSnapshot!.Blocks.Single().Content
            .Deserialize<TextBlockContent>(NotionAggregateJson.Options)!
            .Html.Should().Be("local");
        provider.GetSnapshot(pageId).Blocks.Single().Content
            .Deserialize<TextBlockContent>(NotionAggregateJson.Options)!
            .Html.Should().Be("remote");
    }

    [Fact]
    public async Task ConvertParagraphToTable_CreatesTwoRowsWithSourceTextInFirstCell()
    {
        var pageId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var paragraph = Block(pageId, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), null, 0, "header cell");
        var provider = new FakeNotionAggregateProvider([Snapshot(pageId, paragraph)]);
        var service = new NotionEditorBlockService(provider);

        var converted = await service.ConvertBlockTypeAsync(paragraph.Id.ToString("D"), BlockType.Table);

        converted.Type.Should().Be(BlockType.Table);
        var rows = provider.GetSnapshot(pageId).Blocks
            .Where(block => block.ParentBlockId == converted.Id && block.Type == BlockType.TableRow)
            .OrderBy(block => block.Order)
            .ToList();
        rows.Should().HaveCount(2, "a converted table starts with exactly two rows");

        // The paragraph text must land in the first cell of the first row, not be dropped.
        var firstRow = NotionCanonicalBlockBridge.ToViewBlock(provider.GetSnapshot(pageId), rows[0]);
        var firstRowCells = ((ITableRowBlockContent)firstRow.Content).RichCells;
        firstRowCells.Should().NotBeEmpty();
        firstRowCells[0].Html.Should().Contain("header cell");
    }

    private static NotionPageSnapshot Snapshot(
        Guid pageId,
        params NotionBlockSnapshot[] blocks)
        => new()
        {
            Page = new NotionPageState
            {
                Id = pageId,
                Title = $"Page {pageId:N}"
            },
            ConcurrencyToken = $"token-{pageId:N}",
            Digest = $"sha256:{pageId:N}",
            Blocks = blocks
        };

    private static NotionBlockSnapshot Block(
        Guid pageId,
        Guid id,
        Guid? parentId,
        int order,
        string html)
        => new()
        {
            Id = id,
            PageId = pageId,
            ParentBlockId = parentId,
            Type = BlockType.Paragraph,
            Order = order,
            CreatedAt = DateTime.UtcNow,
            LastEditedAt = DateTime.UtcNow,
            Content = JsonSerializer.SerializeToElement(
                new TextBlockContent { Html = html },
                NotionAggregateJson.Options)
        };
}
