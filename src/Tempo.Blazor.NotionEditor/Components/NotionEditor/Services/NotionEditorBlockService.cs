using System.Text.Json;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.Services;

/// <summary>
/// Interactive block operations backed by complete aggregate replacements.
/// </summary>
public interface INotionEditorBlockService
{
    /// <summary>Loads page-level blocks.</summary>
    Task<IEnumerable<IPageBlock>> GetBlocksAsync(string pageId);

    /// <summary>Loads direct child blocks.</summary>
    Task<IEnumerable<IPageBlock>> GetChildBlocksAsync(string parentBlockId);

    /// <summary>Creates one block.</summary>
    Task<IPageBlock> CreateBlockAsync(string pageId, IPageBlock block, string? afterBlockId);

    /// <summary>Creates multiple blocks.</summary>
    Task<IEnumerable<IPageBlock>> CreateBlocksAsync(
        string pageId,
        IEnumerable<IPageBlock> blocks,
        string? afterBlockId);

    /// <summary>
    /// Restores blocks with stable identifiers. Implementations without native restore support
    /// fall back to creating the supplied batch.
    /// </summary>
    Task RestoreBlocksAsync(IEnumerable<IPageBlock> blocks)
    {
        var first = blocks.FirstOrDefault();
        return first is null
            ? Task.CompletedTask
            : CreateBlocksAsync(first.PageId.ToString(), blocks, null);
    }

    /// <summary>Updates one block.</summary>
    Task UpdateBlockAsync(IPageBlock block);

    /// <summary>
    /// Updates one block and deletes another block subtree as one logical mutation.
    /// </summary>
    async Task UpdateBlockAndDeleteAsync(IPageBlock block, string deletedBlockId)
    {
        await UpdateBlockAsync(block);
        await DeleteBlockAsync(deletedBlockId);
    }

    /// <summary>Deletes one block subtree.</summary>
    Task DeleteBlockAsync(string blockId);

    /// <summary>Reorders sibling blocks.</summary>
    Task ReorderBlocksAsync(string pageId, IEnumerable<string> orderedBlockIds);

    /// <summary>Moves a block subtree.</summary>
    Task MoveBlockAsync(MoveNotionBlockRequest request);

    /// <summary>Moves a block subtree to another page.</summary>
    Task MoveBlockToPageAsync(string blockId, string targetPageId, string? afterBlockId);

    /// <summary>Duplicates a block subtree.</summary>
    Task<IPageBlock> DuplicateBlockAsync(string blockId);

    /// <summary>Converts a block type.</summary>
    Task<IPageBlock> ConvertBlockTypeAsync(string blockId, BlockType newType);

    /// <summary>
    /// Converts a block type using current editor HTML. Implementations that do not need the live
    /// value fall back to the regular conversion overload.
    /// </summary>
    Task<IPageBlock> ConvertBlockTypeAsync(
        string blockId,
        BlockType newType,
        string? currentHtml)
        => ConvertBlockTypeAsync(blockId, newType);

    /// <summary>Returns a stable block link.</summary>
    Task<string> GetBlockLinkAsync(string blockId);
}

/// <summary>
/// Applies interactive block mutations to complete Notion page snapshots and persists each logical
/// change through exactly one <see cref="INotionAggregateProvider.SaveAsync"/> call.
/// </summary>
public sealed class NotionEditorBlockService : INotionEditorBlockService
{
    private readonly INotionAggregateProvider _provider;
    private readonly NotionEditorAggregateSession? _session;
    private readonly Dictionary<(Guid BlockId, BlockType Type), IBlockContent> _conversionMemory = [];

    /// <summary>Creates an aggregate-backed block service for one editor.</summary>
    public NotionEditorBlockService(
        INotionAggregateProvider provider,
        NotionEditorAggregateSession? session = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _session = session;
    }

    /// <summary>Loads the page-level blocks of a complete page aggregate.</summary>
    public async Task<IEnumerable<IPageBlock>> GetBlocksAsync(string pageId)
    {
        var snapshot = await LoadPageAsync(ParseGuid(pageId, nameof(pageId)));
        return ToViewBlocks(snapshot, parentBlockId: null);
    }

    /// <summary>Loads direct children from the complete aggregate that owns the parent block.</summary>
    public async Task<IEnumerable<IPageBlock>> GetChildBlocksAsync(string parentBlockId)
    {
        var parentId = ParseGuid(parentBlockId, nameof(parentBlockId));
        var snapshot = await LoadBlockOwnerAsync(parentId);
        return ToViewBlocks(snapshot, parentId);
    }

    /// <summary>Creates one block and atomically replaces its owning page snapshot.</summary>
    public async Task<IPageBlock> CreateBlockAsync(
        string pageId,
        IPageBlock block,
        string? afterBlockId)
    {
        var created = (await CreateBlocksAsync(pageId, [block], afterBlockId))
            .Single();
        return created;
    }

    /// <summary>Creates a block batch, remaps intra-batch parents, and saves it once.</summary>
    public async Task<IEnumerable<IPageBlock>> CreateBlocksAsync(
        string pageId,
        IEnumerable<IPageBlock> blocks,
        string? afterBlockId)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        var pageGuid = ParseGuid(pageId, nameof(pageId));
        var source = blocks.ToList();
        if (source.Count == 0)
        {
            return [];
        }

        var idMap = source
            .Where(block => block.Id != Guid.Empty)
            .ToDictionary(block => block.Id, _ => Guid.NewGuid());
        var now = DateTime.UtcNow;
        var created = source.Select(block =>
        {
            var id = block.Id != Guid.Empty
                ? idMap[block.Id]
                : Guid.NewGuid();
            var parentId = block.ParentBlockId is { } parent &&
                           idMap.TryGetValue(parent, out var remapped)
                ? remapped
                : block.ParentBlockId;
            return new PageBlock
            {
                Id = id,
                PageId = pageGuid,
                ParentBlockId = parentId,
                Type = block.Type,
                Order = block.Order,
                Content = CloneContent(block.Content),
                CreatedAt = now,
                LastEditedAt = now
            };
        }).Cast<IPageBlock>().ToList();
        var afterId = TryParseGuid(afterBlockId);

        var saved = await MutatePageAsync(pageGuid, snapshot =>
        {
            InsertBlocks(snapshot, created, afterId, preserveOrder: false);
            return snapshot;
        });
        return created.Select(block =>
            (IPageBlock)NotionCanonicalBlockBridge.ToViewBlock(
                saved,
                saved.Blocks.Single(candidate => candidate.Id == block.Id)))
            .ToList();
    }

    /// <summary>Restores blocks with their original identifiers and parent links in one save.</summary>
    public async Task RestoreBlocksAsync(IEnumerable<IPageBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        var restored = blocks.ToList();
        if (restored.Count == 0)
        {
            return;
        }

        var pages = restored.GroupBy(block => block.PageId).ToList();
        if (pages.Count != 1)
        {
            throw new InvalidOperationException(
                "One restore operation must target exactly one page aggregate.");
        }

        await MutatePageAsync(pages[0].Key, snapshot =>
        {
            InsertBlocks(snapshot, restored, afterBlockId: null, preserveOrder: true);
            return snapshot;
        });
    }

    /// <summary>Replaces one block in its complete owning page snapshot.</summary>
    public async Task UpdateBlockAsync(IPageBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        await MutateBlockOwnerAsync(block.Id, snapshot =>
        {
            ReplaceBlock(snapshot, block);
            return snapshot;
        });
    }

    /// <summary>Updates one block and deletes another subtree in one aggregate save.</summary>
    public async Task UpdateBlockAndDeleteAsync(IPageBlock block, string deletedBlockId)
    {
        ArgumentNullException.ThrowIfNull(block);
        var deletedId = ParseGuid(deletedBlockId, nameof(deletedBlockId));
        if (block.Id == deletedId)
        {
            throw new ArgumentException(
                "The updated and deleted block identifiers must be different.",
                nameof(deletedBlockId));
        }

        await MutateBlockOwnerAsync(block.Id, snapshot =>
        {
            ReplaceBlock(snapshot, block);
            if (!snapshot.Blocks.Any(candidate => candidate.Id == deletedId))
            {
                throw new KeyNotFoundException($"Block '{deletedId}' was not found.");
            }
            var removed = DescendantIds(snapshot, deletedId);
            removed.Add(deletedId);
            snapshot.Blocks = snapshot.Blocks
                .Where(candidate => !removed.Contains(candidate.Id))
                .ToList();
            NormalizeOrders(snapshot);
            return snapshot;
        });
    }

    /// <summary>Deletes a block and its complete descendant subtree in one save.</summary>
    public async Task DeleteBlockAsync(string blockId)
    {
        var id = ParseGuid(blockId, nameof(blockId));
        await MutateBlockOwnerAsync(id, snapshot =>
        {
            var removed = DescendantIds(snapshot, id);
            removed.Add(id);
            snapshot.Blocks = snapshot.Blocks
                .Where(block => !removed.Contains(block.Id))
                .ToList();
            NormalizeOrders(snapshot);
            return snapshot;
        });
    }

    /// <summary>Persists the supplied sibling order as one aggregate replacement.</summary>
    public async Task ReorderBlocksAsync(
        string pageId,
        IEnumerable<string> orderedBlockIds)
    {
        ArgumentNullException.ThrowIfNull(orderedBlockIds);
        var pageGuid = ParseGuid(pageId, nameof(pageId));
        var ids = orderedBlockIds.Select(id => ParseGuid(id, nameof(orderedBlockIds))).ToList();
        await MutatePageAsync(pageGuid, snapshot =>
        {
            for (var index = 0; index < ids.Count; index++)
            {
                var block = snapshot.Blocks.SingleOrDefault(candidate => candidate.Id == ids[index]);
                if (block is not null)
                {
                    block.Order = index;
                    block.LastEditedAt = DateTime.UtcNow;
                }
            }
            return snapshot;
        });
    }

    /// <summary>Moves a block subtree within or across page aggregates.</summary>
    public async Task MoveBlockAsync(MoveNotionBlockRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var blockId = ParseGuid(request.BlockId, nameof(request.BlockId));
        var targetPageId = ParseGuid(request.TargetPageId, nameof(request.TargetPageId));
        var source = await LoadBlockOwnerAsync(blockId);
        if (source.Page.Id == targetPageId)
        {
            await MutateLoadedPageAsync(source, snapshot =>
            {
                MoveWithinSnapshots(
                    snapshot,
                    snapshot,
                    blockId,
                    TryParseGuid(request.TargetParentBlockId),
                    request.TargetIndex);
                return snapshot;
            });
            return;
        }

        var target = await LoadPageAsync(targetPageId);
        var sourceBaseline = Clone(source);
        var targetBaseline = Clone(target);
        MoveWithinSnapshots(
            source,
            target,
            blockId,
            TryParseGuid(request.TargetParentBlockId),
            request.TargetIndex);
        await SavePagesAsync(
            [source, target],
            [sourceBaseline, targetBaseline]);
    }

    /// <summary>Moves a block subtree to the root of another page aggregate.</summary>
    public async Task MoveBlockToPageAsync(
        string blockId,
        string targetPageId,
        string? afterBlockId)
    {
        var targetId = ParseGuid(targetPageId, nameof(targetPageId));
        var target = await LoadPageAsync(targetId);
        var roots = target.Blocks
            .Where(block => block.ParentBlockId is null)
            .OrderBy(block => block.Order)
            .ToList();
        var afterId = TryParseGuid(afterBlockId);
        var index = afterId is { } anchor
            ? roots.FindIndex(block => block.Id == anchor) + 1
            : roots.Count;
        if (index <= 0 && afterId is not null)
        {
            index = roots.Count;
        }

        await MoveBlockAsync(new MoveNotionBlockRequest(
            blockId,
            targetPageId,
            SourceParentBlockId: null,
            TargetParentBlockId: null,
            index));
    }

    /// <summary>Duplicates a block subtree with fresh identifiers in one save.</summary>
    public async Task<IPageBlock> DuplicateBlockAsync(string blockId)
    {
        var id = ParseGuid(blockId, nameof(blockId));
        PageBlock? duplicateRoot = null;
        var saved = await MutateBlockOwnerAsync(id, snapshot =>
        {
            var source = snapshot.Blocks.Single(block => block.Id == id);
            var subtreeIds = DescendantIds(snapshot, id);
            var subtree = snapshot.Blocks
                .Where(block => block.Id == id || subtreeIds.Contains(block.Id))
                .OrderBy(block => block.Id == id ? 0 : 1)
                .ThenBy(block => block.Order)
                .ToList();
            var idMap = subtree.ToDictionary(block => block.Id, _ => Guid.NewGuid());
            var copies = subtree.Select(block => new NotionBlockSnapshot
            {
                Id = idMap[block.Id],
                PageId = block.PageId,
                ParentBlockId = block.Id == id
                    ? block.ParentBlockId
                    : idMap[block.ParentBlockId!.Value],
                Type = block.Type,
                Order = block.Order,
                CreatedAt = DateTime.UtcNow,
                LastEditedAt = DateTime.UtcNow,
                Content = block.Content.Clone()
            }).ToList();
            var root = copies.Single(block => block.Id == idMap[id]);
            var siblings = snapshot.Blocks
                .Where(block => block.ParentBlockId == source.ParentBlockId)
                .OrderBy(block => block.Order)
                .ToList();
            var sourceIndex = siblings.FindIndex(block => block.Id == source.Id);
            root.Order = sourceIndex + 1;
            snapshot.Blocks = snapshot.Blocks.Concat(copies).ToList();
            NormalizeOrders(
                snapshot,
                source.ParentBlockId,
                preferredOrder: siblings
                    .Take(sourceIndex + 1)
                    .Select(block => block.Id)
                    .Append(root.Id)
                    .Concat(siblings.Skip(sourceIndex + 1).Select(block => block.Id))
                    .ToList());
            duplicateRoot = NotionCanonicalBlockBridge.ToViewBlock(snapshot, root);
            return snapshot;
        });
        return duplicateRoot is null
            ? throw new InvalidDataException("The duplicate operation returned no root block.")
            : NotionCanonicalBlockBridge.ToViewBlock(
                saved,
                saved.Blocks.Single(block => block.Id == duplicateRoot.Id));
    }

    /// <summary>Converts a block while preserving its current text content.</summary>
    public Task<IPageBlock> ConvertBlockTypeAsync(string blockId, BlockType newType)
        => ConvertBlockTypeAsync(blockId, newType, currentHtml: null);

    /// <summary>Converts a block using the caller's current editor HTML when supplied.</summary>
    public async Task<IPageBlock> ConvertBlockTypeAsync(
        string blockId,
        BlockType newType,
        string? currentHtml)
    {
        var id = ParseGuid(blockId, nameof(blockId));
        PageBlock? converted = null;
        var saved = await MutateBlockOwnerAsync(id, snapshot =>
        {
            var index = FindBlockIndex(snapshot, id);
            var source = NotionCanonicalBlockBridge.ToViewBlock(
                snapshot,
                snapshot.Blocks[index]);
            _conversionMemory[(id, source.Type)] = CloneContent(source.Content);
            var content = CreateConvertedContent(newType, source.Content, currentHtml);
            if (_conversionMemory.TryGetValue((id, newType), out var remembered))
            {
                RestoreTypedFields(content, remembered);
            }
            converted = new PageBlock
            {
                Id = source.Id,
                PageId = source.PageId,
                ParentBlockId = source.ParentBlockId,
                Type = newType,
                Order = source.Order,
                Content = content,
                CreatedAt = source.CreatedAt,
                LastEditedAt = DateTime.UtcNow
            };
            var mutable = snapshot.Blocks.ToList();
            mutable[index] = NotionCanonicalBlockBridge.ToSnapshot(converted);
            snapshot.Blocks = mutable;
            CascadeChildren(snapshot, source, newType);
            if (newType == BlockType.Table)
            {
                // A freshly converted table must start with its two seed rows (the source text in
                // the first cell) or the rendered <tbody> is empty — see SlashMenu_ParagraphToTable.
                snapshot.Blocks = snapshot.Blocks
                    .Concat(CreateTableRowSnapshots(converted, TextOf(source.Content, currentHtml)))
                    .ToList();
            }
            return snapshot;
        });
        return NotionCanonicalBlockBridge.ToViewBlock(
            saved,
            saved.Blocks.Single(block => block.Id == converted!.Id));
    }

    /// <summary>Returns the stable demo link shape for a block identifier.</summary>
    public Task<string> GetBlockLinkAsync(string blockId)
    {
        _ = ParseGuid(blockId, nameof(blockId));
        return Task.FromResult($"https://notion.demo/block/{blockId}");
    }

    private async Task<NotionPageSnapshot> LoadPageAsync(Guid pageId)
    {
        if (_session?.CurrentSnapshot is { } current &&
            current.Page.Id == pageId)
        {
            return Clone(current);
        }

        var load = await _provider.LoadPageAsync(pageId);
        return RequireLoaded(load, pageId);
    }

    private async Task<NotionPageSnapshot> LoadBlockOwnerAsync(Guid blockId)
    {
        if (_session?.CurrentSnapshot is { } current &&
            current.Blocks.Any(block => block.Id == blockId))
        {
            return Clone(current);
        }

        var load = await _provider.LoadBlockAsync(blockId);
        return RequireLoaded(load, blockId);
    }

    private async Task<NotionPageSnapshot> MutateBlockOwnerAsync(
        Guid blockId,
        Func<NotionPageSnapshot, NotionPageSnapshot> mutation)
    {
        var owner = await LoadBlockOwnerAsync(blockId);
        return await MutateLoadedPageAsync(owner, mutation);
    }

    private async Task<NotionPageSnapshot> MutatePageAsync(
        Guid pageId,
        Func<NotionPageSnapshot, NotionPageSnapshot> mutation)
    {
        var baseline = await LoadPageAsync(pageId);
        return await MutateLoadedPageAsync(baseline, mutation);
    }

    private async Task<NotionPageSnapshot> MutateLoadedPageAsync(
        NotionPageSnapshot baseline,
        Func<NotionPageSnapshot, NotionPageSnapshot> mutation)
    {
        if (_session?.CurrentSnapshot is { } current &&
            current.Page.Id == baseline.Page.Id)
        {
            var result = await _session.ApplyAsync(mutation);
            return RequireSaved(result);
        }

        var candidate = mutation(Clone(baseline));
        return await SavePagesAsync([candidate], [baseline]);
    }

    private async Task<NotionPageSnapshot> SavePagesAsync(
        IReadOnlyList<NotionPageSnapshot> candidates,
        IReadOnlyList<NotionPageSnapshot>? baselines = null)
    {
        baselines ??= candidates.Select(Clone).ToList();
        var baselineByPage = baselines.ToDictionary(snapshot => snapshot.Page.Id);
        var issues = NotionAggregateValidator.Validate(candidates);
        if (issues.Any(issue => issue.Severity == NotionIssueSeverity.Error))
        {
            throw PersistenceFailure(issues);
        }

        var save = await _provider.SaveAsync(new NotionAggregateSaveRequest
        {
            Pages = candidates.Select(candidate => new NotionPageSave
            {
                Snapshot = candidate,
                BaseConcurrencyToken = baselineByPage[candidate.Page.Id].ConcurrencyToken
            }).ToList()
        });
        if (!save.Success)
        {
            throw PersistenceFailure(save.Issues, save.Conflict);
        }

        foreach (var candidate in candidates)
        {
            var metadata = save.Pages.SingleOrDefault(page => page.PageId == candidate.Page.Id)
                ?? throw new InvalidDataException(
                    $"Save metadata for page '{candidate.Page.Id}' is missing.");
            candidate.ConcurrencyToken = metadata.ConcurrencyToken;
            candidate.Digest = metadata.Digest;
        }

        if (_session?.CurrentSnapshot is { } current &&
            candidates.Any(candidate => candidate.Page.Id == current.Page.Id))
        {
            var reload = await _session.LoadAsync(current.Page.Id);
            _ = RequireSaved(reload);
        }
        return candidates[0];
    }

    private static void InsertBlocks(
        NotionPageSnapshot snapshot,
        IReadOnlyList<IPageBlock> inserted,
        Guid? afterBlockId,
        bool preserveOrder)
    {
        var insertedSnapshots = inserted
            .Select(NotionCanonicalBlockBridge.ToSnapshot)
            .ToList();
        var insertedIds = insertedSnapshots.Select(block => block.Id).ToHashSet();
        snapshot.Blocks = snapshot.Blocks
            .Where(block => !insertedIds.Contains(block.Id))
            .Concat(insertedSnapshots)
            .ToList();

        foreach (var group in insertedSnapshots.GroupBy(block => block.ParentBlockId))
        {
            if (preserveOrder)
            {
                NormalizeOrders(snapshot, group.Key);
                continue;
            }

            var existing = snapshot.Blocks
                .Where(block =>
                    block.ParentBlockId == group.Key &&
                    !insertedIds.Contains(block.Id))
                .OrderBy(block => block.Order)
                .ThenBy(block => block.Id)
                .ToList();
            var groupBlocks = group.OrderBy(block => block.Order).ToList();
            var insertionIndex = afterBlockId is { } anchor
                ? existing.FindIndex(block => block.Id == anchor) + 1
                : existing.Count;
            if (insertionIndex <= 0 && afterBlockId is not null)
            {
                insertionIndex = existing.Count;
            }
            existing.InsertRange(insertionIndex, groupBlocks);
            NormalizeOrders(
                snapshot,
                group.Key,
                existing.Select(block => block.Id).ToList());
        }
    }

    private static void MoveWithinSnapshots(
        NotionPageSnapshot source,
        NotionPageSnapshot target,
        Guid blockId,
        Guid? targetParentId,
        int targetIndex)
    {
        var root = source.Blocks.SingleOrDefault(block => block.Id == blockId)
            ?? throw new KeyNotFoundException($"Block '{blockId}' was not found.");
        var movingIds = DescendantIds(source, blockId);
        movingIds.Add(blockId);
        if (targetParentId == blockId || movingIds.Contains(targetParentId ?? Guid.Empty))
        {
            throw new InvalidOperationException(
                "A block cannot be moved into itself or one of its descendants.");
        }
        if (targetParentId is { } parent &&
            !target.Blocks.Any(block => block.Id == parent))
        {
            throw new KeyNotFoundException($"Target parent block '{parent}' was not found.");
        }

        var sourceParent = root.ParentBlockId;
        var moving = source.Blocks.Where(block => movingIds.Contains(block.Id)).ToList();
        source.Blocks = source.Blocks.Where(block => !movingIds.Contains(block.Id)).ToList();
        if (!ReferenceEquals(source, target))
        {
            target.Blocks = target.Blocks.Where(block => !movingIds.Contains(block.Id)).ToList();
        }
        foreach (var block in moving)
        {
            block.PageId = target.Page.Id;
            block.LastEditedAt = DateTime.UtcNow;
        }
        root.ParentBlockId = targetParentId;
        target.Blocks = target.Blocks.Concat(moving).ToList();

        NormalizeOrders(source, sourceParent);
        var siblings = target.Blocks
            .Where(block => block.ParentBlockId == targetParentId && block.Id != blockId)
            .OrderBy(block => block.Order)
            .ThenBy(block => block.Id)
            .Select(block => block.Id)
            .ToList();
        siblings.Insert(Math.Clamp(targetIndex, 0, siblings.Count), blockId);
        NormalizeOrders(target, targetParentId, siblings);
    }

    private static HashSet<Guid> DescendantIds(NotionPageSnapshot snapshot, Guid rootId)
    {
        var descendants = new HashSet<Guid>();
        var frontier = new Queue<Guid>();
        frontier.Enqueue(rootId);
        while (frontier.Count > 0)
        {
            var parent = frontier.Dequeue();
            foreach (var child in snapshot.Blocks.Where(block => block.ParentBlockId == parent))
            {
                if (descendants.Add(child.Id))
                {
                    frontier.Enqueue(child.Id);
                }
            }
        }
        return descendants;
    }

    private static void NormalizeOrders(NotionPageSnapshot snapshot)
    {
        foreach (var parent in snapshot.Blocks.Select(block => block.ParentBlockId).Distinct())
        {
            NormalizeOrders(snapshot, parent);
        }
    }

    private static void NormalizeOrders(
        NotionPageSnapshot snapshot,
        Guid? parentId,
        IReadOnlyList<Guid>? preferredOrder = null)
    {
        var orderLookup = preferredOrder?
            .Select((id, index) => (id, index))
            .ToDictionary(item => item.id, item => item.index);
        var siblings = snapshot.Blocks
            .Where(block => block.ParentBlockId == parentId)
            .OrderBy(block =>
                orderLookup is not null &&
                orderLookup.TryGetValue(block.Id, out var preferred)
                    ? preferred
                    : int.MaxValue)
            .ThenBy(block => block.Order)
            .ThenBy(block => block.Id)
            .ToList();
        for (var index = 0; index < siblings.Count; index++)
        {
            siblings[index].Order = index;
        }
    }

    private static void CascadeChildren(
        NotionPageSnapshot snapshot,
        IPageBlock source,
        BlockType newType)
    {
        if (source.Type == newType)
        {
            return;
        }
        var children = snapshot.Blocks
            .Where(block => block.ParentBlockId == source.Id)
            .OrderBy(block => block.Order)
            .ToList();
        if (source.Type == BlockType.Table)
        {
            var rowIds = children
                .Where(block => block.Type == BlockType.TableRow)
                .Select(block => block.Id)
                .ToHashSet();
            snapshot.Blocks = snapshot.Blocks
                .Where(block => !rowIds.Contains(block.Id))
                .ToList();
            children = children.Where(block => block.Type != BlockType.TableRow).ToList();
        }
        if (CanHoldChildren(newType))
        {
            return;
        }
        foreach (var child in children)
        {
            child.ParentBlockId = source.ParentBlockId;
        }
        NormalizeOrders(snapshot, source.ParentBlockId);
    }

    /// <summary>
    /// Builds the two seed rows every freshly converted table needs. The source text goes into the
    /// first cell of the first row so converting a paragraph to a table is a promotion, not a reset.
    /// Mirrors <c>MockNotionBlockStore.EnsureTableRows</c> for the aggregate-backed path.
    /// </summary>
    private static IEnumerable<NotionBlockSnapshot> CreateTableRowSnapshots(
        IPageBlock table,
        string headerText)
    {
        var columnCount = table.Content is ITableBlockContent { ColumnCount: > 0 } content
            ? content.ColumnCount
            : 3;

        for (var rowIndex = 0; rowIndex < 2; rowIndex++)
        {
            var cells = Enumerable
                .Range(0, columnCount)
                .Select(_ => new NotionTableCell { Html = string.Empty })
                .ToList();
            if (rowIndex == 0 && !string.IsNullOrEmpty(headerText))
            {
                cells[0] = new NotionTableCell { Html = headerText };
            }

            yield return NotionCanonicalBlockBridge.ToSnapshot(new PageBlock
            {
                Id            = Guid.NewGuid(),
                PageId        = table.PageId,
                ParentBlockId = table.Id,
                Type          = BlockType.TableRow,
                Order         = rowIndex,
                Content       = new TableRowBlockContent { RichCells = cells },
                CreatedAt     = DateTime.UtcNow,
                LastEditedAt  = DateTime.UtcNow
            });
        }
    }

    private static string TextOf(IBlockContent source, string? currentHtml = null) => currentHtml ?? source switch
    {
        ITextBlockContent text => text.Html,
        ICodeBlockContent code => code.Code,
        _ => string.Empty
    };

    private static bool CanHoldChildren(BlockType type) => type
        is BlockType.Toggle
        or BlockType.Callout
        or BlockType.Table
        or BlockType.ColumnList
        or BlockType.Column
        or BlockType.SyncedBlockOrigin
        or BlockType.SyncedBlockRef
        or BlockType.Quote;

    private static IBlockContent CreateConvertedContent(
        BlockType type,
        IBlockContent source,
        string? currentHtml)
    {
        var html = currentHtml ?? source switch
        {
            ITextBlockContent text => text.Html,
            ICodeBlockContent code => code.Code,
            _ => string.Empty
        };
        var background = source is ITextBlockContent styled ? styled.BackgroundColor : null;
        var color = source is ITextBlockContent colored ? colored.TextColor : null;
        var alignment = source is ITextBlockContent aligned
            ? aligned.Alignment
            : TextAlignment.Left;
        return type switch
        {
            BlockType.Heading1 => new HeadingBlockContent
                { Level = 1, Html = html, BackgroundColor = background, TextColor = color, Alignment = alignment },
            BlockType.Heading2 => new HeadingBlockContent
                { Level = 2, Html = html, BackgroundColor = background, TextColor = color, Alignment = alignment },
            BlockType.Heading3 => new HeadingBlockContent
                { Level = 3, Html = html, BackgroundColor = background, TextColor = color, Alignment = alignment },
            BlockType.Paragraph or BlockType.Quote => new TextBlockContent
                { Html = html, BackgroundColor = background, TextColor = color, Alignment = alignment },
            BlockType.BulletList or BlockType.NumberedList => new ListBlockContent
                { Html = html, BackgroundColor = background, TextColor = color, Alignment = alignment },
            BlockType.TodoItem => new TodoBlockContent
                { Html = html, BackgroundColor = background, TextColor = color, Alignment = alignment },
            BlockType.Toggle => new ToggleBlockContent
                { Html = html, BackgroundColor = background, TextColor = color, Alignment = alignment },
            BlockType.Callout => new CalloutBlockContent
                { Html = html, BackgroundColor = background, TextColor = color, Alignment = alignment, IconEmoji = "💡" },
            BlockType.Code => new CodeBlockContent { Code = html },
            _ => CreateDefaultContent(type)
        };
    }

    private static IBlockContent CreateDefaultContent(BlockType type) => type switch
    {
        BlockType.Divider => new DividerBlockContent(),
        BlockType.Equation => new EquationBlockContent(),
        BlockType.Table => new TableBlockContent { ColumnCount = 3 },
        BlockType.Image => new ImageBlockContent(),
        BlockType.Video => new VideoBlockContent(),
        BlockType.Audio => new AudioBlockContent(),
        BlockType.File => new FileBlockContent(),
        BlockType.Pdf => new PdfBlockContent(),
        BlockType.Bookmark => new BookmarkBlockContent(),
        BlockType.Embed => new EmbedBlockContent(),
        BlockType.ChildPage => new ChildPageBlockContent(),
        BlockType.LinkedPage => new LinkedPageBlockContent(),
        BlockType.Breadcrumb => new BreadcrumbBlockContent(),
        BlockType.SyncedBlockOrigin => new SyncedBlockOriginContent(),
        BlockType.SyncedBlockRef => new SyncedBlockRefContent(),
        BlockType.InlineDatabase => new InlineDatabaseBlockContent(),
        BlockType.LinkedDatabase => new LinkedDatabaseBlockContent(),
        BlockType.ColumnList => new ColumnListBlockContent { ColumnCount = 2 },
        BlockType.Column => new ColumnBlockContent(),
        BlockType.TemplateButton => new TemplateButtonBlockContent(),
        BlockType.TableOfContents => new TableOfContentsBlockContent(),
        BlockType.Diagram => new DiagramBlockContent(),
        BlockType.Wireframe => new WireframeBlockContent(),
        BlockType.Spreadsheet => new SpreadsheetBlockContent(),
        BlockType.WorkItem => new WorkItemBlockContent(),
        BlockType.ContentByLabel => new ContentByLabelBlockContent(),
        BlockType.IncludePage => new IncludePageBlockContent(),
        BlockType.ChildrenDisplay => new ChildrenDisplayBlockContent(),
        BlockType.Excerpt => new ExcerptBlockContent(),
        BlockType.ExcerptInclude => new ExcerptIncludeBlockContent(),
        BlockType.PageProperties => new PagePropertiesBlockContent(),
        BlockType.PagePropertiesReport => new PagePropertiesReportBlockContent(),
        _ => new TextBlockContent()
    };

    private static void RestoreTypedFields(IBlockContent target, IBlockContent remembered)
    {
        switch (target)
        {
            case TodoBlockContent todo when remembered is ITodoBlockContent previous:
                todo.IsChecked = previous.IsChecked;
                todo.AssigneeId = previous.AssigneeId;
                todo.AssigneeDisplayName = previous.AssigneeDisplayName;
                todo.DueDate = previous.DueDate;
                break;
            case CalloutBlockContent callout when remembered is ICalloutBlockContent previous:
                callout.IconEmoji = previous.IconEmoji ?? "💡";
                callout.IconImageUrl = previous.IconImageUrl;
                callout.Variant = previous.Variant;
                break;
            case CodeBlockContent code when remembered is ICodeBlockContent previous:
                code.Language = previous.Language;
                code.ShowLineNumbers = previous.ShowLineNumbers;
                code.WrapLines = previous.WrapLines;
                code.Caption = previous.Caption;
                break;
            case ToggleBlockContent toggle when remembered is IToggleBlockContent previous:
                toggle.IsOpen = previous.IsOpen;
                break;
        }
    }

    private static IReadOnlyList<IPageBlock> ToViewBlocks(
        NotionPageSnapshot snapshot,
        Guid? parentBlockId)
        => snapshot.Blocks
            .Where(block => block.ParentBlockId == parentBlockId)
            .OrderBy(block => block.Order)
            .ThenBy(block => block.Id)
            .Select(block =>
                (IPageBlock)NotionCanonicalBlockBridge.ToViewBlock(snapshot, block))
            .ToList();

    private static NotionPageSnapshot RequireLoaded(
        NotionAggregateLoadResult load,
        Guid requestedId)
        => load.Found && load.Snapshot is not null
            ? Clone(load.Snapshot)
            : throw new KeyNotFoundException(
                $"Notion aggregate '{requestedId}' was not found.");

    private static NotionPageSnapshot RequireSaved(NotionEditorAggregateSaveResult result)
        => result.Success && result.Snapshot is not null
            ? Clone(result.Snapshot)
            : throw PersistenceFailure(result.Issues, result.Conflict);

    private static InvalidOperationException PersistenceFailure(
        IReadOnlyList<NotionAggregateIssue> issues,
        bool conflict = false)
        => new(string.Join(
            Environment.NewLine,
            issues.Select(issue => $"{issue.Code}: {issue.Message}").Prepend(
                conflict ? "Notion aggregate concurrency conflict." : "Notion aggregate save failed.")));

    private static int FindBlockIndex(NotionPageSnapshot snapshot, Guid blockId)
    {
        var index = snapshot.Blocks.ToList().FindIndex(block => block.Id == blockId);
        return index >= 0
            ? index
            : throw new KeyNotFoundException($"Block '{blockId}' was not found.");
    }

    private static void ReplaceBlock(NotionPageSnapshot snapshot, IPageBlock block)
    {
        var index = FindBlockIndex(snapshot, block.Id);
        var existing = snapshot.Blocks[index];
        var replacement = NotionCanonicalBlockBridge.ToSnapshot(block);
        replacement.PageId = existing.PageId;
        replacement.ParentBlockId = existing.ParentBlockId;
        replacement.Order = existing.Order;
        replacement.CreatedAt = existing.CreatedAt;
        replacement.LastEditedAt = DateTime.UtcNow;
        var mutable = snapshot.Blocks.ToList();
        mutable[index] = replacement;
        snapshot.Blocks = mutable;
    }

    private static Guid ParseGuid(string value, string parameterName)
        => Guid.TryParse(value, out var parsed)
            ? parsed
            : throw new ArgumentException(
                $"'{value}' is not a valid Notion identifier.",
                parameterName);

    private static Guid? TryParseGuid(string? value)
        => Guid.TryParse(value, out var parsed) ? parsed : null;

    private static IBlockContent CloneContent(IBlockContent content)
        => JsonSerializer.Deserialize<IBlockContent>(
               JsonSerializer.Serialize<IBlockContent>(
                   content,
                   NotionAggregateJson.Options),
               NotionAggregateJson.Options)
           ?? throw new InvalidDataException("Could not clone Notion block content.");

    private static NotionPageSnapshot Clone(NotionPageSnapshot snapshot)
        => JsonSerializer.Deserialize<NotionPageSnapshot>(
               JsonSerializer.Serialize(snapshot, NotionAggregateJson.Options),
               NotionAggregateJson.Options)
           ?? throw new InvalidDataException("Could not clone the Notion page snapshot.");
}
