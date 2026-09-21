using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Demo.Api.Data;

/// <summary>
/// Demo-only atomic page store. Legacy demo blocks are converted once at the HTTP boundary; all
/// subsequent table authoring uses complete canonical snapshots and optimistic tokens.
/// </summary>
public sealed class DemoNotionAggregateStore(
    MockNotionDataStore pageStore,
    MockNotionBlockStore blockStore)
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, NotionPageSnapshot> _snapshots = [];
    private readonly HashSet<Guid> _forceConflictOnNextSave = [];
    private long _version;

    public async Task<NotionAggregateLoadResult> LoadPageAsync(Guid pageId)
    {
        NotionPageSnapshot snapshot;
        lock (_gate)
        {
            if (_snapshots.TryGetValue(pageId, out var existing))
            {
                return new NotionAggregateLoadResult
                {
                    Found = true,
                    Snapshot = Clone(existing)
                };
            }
        }

        INotionPage page;
        try
        {
            page = await pageStore.GetPageAsync(pageId.ToString());
        }
        catch (KeyNotFoundException)
        {
            return new NotionAggregateLoadResult { Found = false };
        }

        snapshot = CreateSnapshot(page, blockStore.GetAllPageBlocks(pageId));
        lock (_gate)
        {
            if (!_snapshots.TryGetValue(pageId, out var existing))
            {
                snapshot.ConcurrencyToken = NextToken();
                snapshot.Digest = ComputeDigest(snapshot);
                _snapshots[pageId] = Clone(snapshot);
            }
            else
            {
                snapshot = existing;
            }
            return new NotionAggregateLoadResult
            {
                Found = true,
                Snapshot = Clone(snapshot)
            };
        }
    }

    public async Task<NotionAggregateLoadResult> LoadBlockAsync(Guid blockId)
    {
        Guid? knownPageId;
        lock (_gate)
        {
            knownPageId = _snapshots.Values
                .FirstOrDefault(snapshot => snapshot.Blocks.Any(block => block.Id == blockId))
                ?.Page.Id;
        }
        if (knownPageId is null)
        {
            var block = blockStore.GetBlock(blockId);
            knownPageId = block?.PageId;
        }
        if (knownPageId is null)
        {
            return new NotionAggregateLoadResult { Found = false };
        }

        var load = await LoadPageAsync(knownPageId.Value);
        load.MatchedBlockId = load.Snapshot?.Blocks.Any(block => block.Id == blockId) == true
            ? blockId
            : null;
        load.Found = load.MatchedBlockId is not null;
        return load;
    }

    public NotionAggregateSaveResult Save(NotionAggregateSaveRequest request)
    {
        var issues = NotionAggregateValidator.Validate(request.Pages.Select(page => page.Snapshot));
        if (issues.Any(issue => issue.Severity == NotionIssueSeverity.Error))
        {
            return new NotionAggregateSaveResult { Issues = issues };
        }

        lock (_gate)
        {
            var forcedConflicts = request.Pages
                .Where(page => _forceConflictOnNextSave.Remove(page.Snapshot.Page.Id))
                .Select(page =>
                {
                    _snapshots.TryGetValue(page.Snapshot.Page.Id, out var current);
                    return new NotionPageConflict
                    {
                        PageId = page.Snapshot.Page.Id,
                        ExpectedConcurrencyToken = page.BaseConcurrencyToken,
                        CurrentConcurrencyToken = current?.ConcurrencyToken,
                        CurrentDigest = current?.Digest
                    };
                })
                .ToList();
            if (forcedConflicts.Count > 0)
            {
                return new NotionAggregateSaveResult
                {
                    Conflict = true,
                    Conflicts = forcedConflicts
                };
            }

            var conflicts = request.Pages
                .Where(page =>
                    !_snapshots.TryGetValue(page.Snapshot.Page.Id, out var current) ||
                    !string.Equals(
                        current.ConcurrencyToken,
                        page.BaseConcurrencyToken,
                        StringComparison.Ordinal))
                .Select(page =>
                {
                    _snapshots.TryGetValue(page.Snapshot.Page.Id, out var current);
                    return new NotionPageConflict
                    {
                        PageId = page.Snapshot.Page.Id,
                        ExpectedConcurrencyToken = page.BaseConcurrencyToken,
                        CurrentConcurrencyToken = current?.ConcurrencyToken,
                        CurrentDigest = current?.Digest
                    };
                })
                .OrderBy(conflict => conflict.PageId)
                .ToList();
            if (conflicts.Count > 0)
            {
                return new NotionAggregateSaveResult
                {
                    Conflict = true,
                    Conflicts = conflicts
                };
            }

            var saved = new List<NotionSavedPage>(request.Pages.Count);
            foreach (var page in request.Pages)
            {
                var snapshot = Clone(page.Snapshot);
                snapshot.ConcurrencyToken = NextToken();
                snapshot.Digest = ComputeDigest(snapshot);
                blockStore.ApplyAggregateSnapshot(snapshot);
                _snapshots[snapshot.Page.Id] = snapshot;
                saved.Add(new NotionSavedPage
                {
                    PageId = snapshot.Page.Id,
                    ConcurrencyToken = snapshot.ConcurrencyToken,
                    Digest = snapshot.Digest,
                    SchemaVersion = snapshot.SchemaVersion
                });
            }

            return new NotionAggregateSaveResult
            {
                Success = true,
                Pages = saved
            };
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _snapshots.Clear();
            _forceConflictOnNextSave.Clear();
            _version = 0;
        }
    }

    /// <summary>
    /// Drops the cached snapshot for a page so the next load rebuilds it from the
    /// block store. Required when a write path mutates <see cref="MockNotionBlockStore"/>
    /// directly (e.g., task completion), bypassing <see cref="Save"/> — otherwise the
    /// stale cached snapshot keeps serving the pre-mutation state.
    /// </summary>
    public void InvalidatePageSnapshot(Guid pageId)
    {
        lock (_gate)
        {
            _snapshots.Remove(pageId);
        }
    }

    public bool AdvanceConcurrencyToken(Guid pageId)
    {
        lock (_gate)
        {
            if (!_snapshots.TryGetValue(pageId, out var snapshot))
            {
                return false;
            }
            snapshot.ConcurrencyToken = NextToken();
            snapshot.Digest = ComputeDigest(snapshot);
            _forceConflictOnNextSave.Add(pageId);
            return true;
        }
    }

    private NotionPageSnapshot CreateSnapshot(
        INotionPage page,
        IReadOnlyList<IPageBlock> blocks)
        => new()
        {
            Page = new NotionPageState
            {
                Id = page.Id,
                ParentPageId = page.ParentId,
                Title = page.Title,
                Description = page.Description,
                SpaceId = page.SpaceId,
                Labels = page.Labels,
                IconEmoji = page.IconEmoji,
                IconImageUrl = page.IconImageUrl,
                CoverImageUrl = page.CoverImageUrl,
                CoverImagePositionY = page.CoverImagePositionY,
                IsFullWidth = page.IsFullWidth,
                IsSmallText = page.IsSmallText,
                IsLocked = page.IsLocked,
                CreatedAt = page.CreatedAt,
                CreatedByUserId = page.CreatedByUserId,
                LastEditedAt = page.LastEditedAt,
                LastEditedByUserId = page.LastEditedByUserId,
                IsDeleted = page.IsDeleted,
                DeletedAt = page.DeletedAt,
                IsFavorite = page.IsFavorite
            },
            Blocks = NormalizeBlockOrder(blocks)
        };

    private static IReadOnlyList<NotionBlockSnapshot> NormalizeBlockOrder(
        IReadOnlyList<IPageBlock> blocks)
    {
        var snapshots = blocks.Select(ToSnapshot).ToList();
        foreach (var siblings in snapshots.GroupBy(block => block.ParentBlockId))
        {
            var index = 0;
            foreach (var block in siblings.OrderBy(block => block.Order).ThenBy(block => block.Id))
            {
                block.Order = index++;
            }
        }
        return snapshots
            .OrderBy(block => block.ParentBlockId)
            .ThenBy(block => block.Order)
            .ThenBy(block => block.Id)
            .ToList();
    }

    private static NotionBlockSnapshot ToSnapshot(IPageBlock block)
        => new()
        {
            Id = block.Id,
            PageId = block.PageId,
            ParentBlockId = block.ParentBlockId,
            Type = block.Type,
            Order = block.Order,
            CreatedAt = block.CreatedAt,
            LastEditedAt = block.LastEditedAt,
            Content = block.Type switch
            {
                BlockType.Table => JsonSerializer.SerializeToElement(
                    ToCanonicalTable((ITableBlockContent)block.Content),
                    NotionAggregateJson.Options),
                BlockType.TableRow => JsonSerializer.SerializeToElement(
                    ToCanonicalRow((ITableRowBlockContent)block.Content),
                    NotionAggregateJson.Options),
                _ => JsonSerializer.SerializeToElement(
                    block.Content,
                    block.Content.GetType(),
                    NotionAggregateJson.Options)
            }
        };

    private static NotionAuthoringTable ToCanonicalTable(ITableBlockContent table)
        => new()
        {
            ColumnCount = table.ColumnCount,
            HasHeaderRow = table.HasHeaderRow,
            HasHeaderColumn = table.HasHeaderColumn,
            ColumnAlignments = table.ColumnAlignments.Select(alignment => alignment switch
            {
                Tempo.Blazor.DocumentEditor.Models.TableColumnAlignment.Center =>
                    NotionTableHorizontalAlignment.Center,
                Tempo.Blazor.DocumentEditor.Models.TableColumnAlignment.Right =>
                    NotionTableHorizontalAlignment.Right,
                _ => NotionTableHorizontalAlignment.Left
            }).ToList(),
            ColumnWidths = table.ColumnWidths
        };

    private static NotionAuthoringTableRow ToCanonicalRow(ITableRowBlockContent row)
        => new()
        {
            Cells = row.RichCells
                .Where(cell => !cell.IsMergeHidden)
                .Select(ToCanonicalCell)
                .ToList()
        };

    private static NotionAuthoringTableCell ToCanonicalCell(NotionTableCell cell)
        => new()
        {
            Html = NotionHtmlSanitizer.SanitizeHtmlFragment(cell.Html),
            Inlines = cell.Inlines.Select(inline => new NotionRichTextInline
            {
                Text = inline.Text,
                Href = NotionHtmlSanitizer.IsSafeHref(inline.Href) ? inline.Href : null,
                Bold = inline.Bold,
                Italic = inline.Italic,
                Underline = inline.Underline,
                Strikethrough = inline.Strikethrough,
                Code = inline.Code,
                TextColor = SafeColor(inline.TextColor),
                BackgroundColor = SafeColor(inline.BackgroundColor)
            }).ToList(),
            BackgroundColor = SafeColor(cell.BackgroundColor),
            TextColor = SafeColor(cell.TextColor),
            HorizontalAlignment = cell.HorizontalAlignment,
            VerticalAlignment = cell.VerticalAlignment,
            RowSpan = Math.Max(1, cell.RowSpan),
            ColumnSpan = Math.Max(1, cell.ColSpan),
            Width = cell.Width is > 0 && double.IsFinite(cell.Width.Value) ? cell.Width : null,
            Borders = new NotionTableCellBorders
            {
                Top = SafeBorder(cell.Borders.Top),
                Right = SafeBorder(cell.Borders.Right),
                Bottom = SafeBorder(cell.Borders.Bottom),
                Left = SafeBorder(cell.Borders.Left)
            }
        };

    private static string? SafeColor(string? color)
        => NotionCssNormalizer.TryNormalizeColor(color, out var normalized)
            ? normalized
            : null;

    private static NotionTableBorder? SafeBorder(NotionTableBorder? border)
        => border is not null &&
           border.Width > 0 &&
           double.IsFinite(border.Width) &&
           SafeColor(border.Color) is { } color
            ? new NotionTableBorder
            {
                Style = border.Style,
                Color = color,
                Width = border.Width
            }
            : null;

    private string NextToken() => $"demo-aggregate-{++_version}";

    private static string ComputeDigest(NotionPageSnapshot snapshot)
    {
        var clone = Clone(snapshot);
        clone.ConcurrencyToken = string.Empty;
        clone.Digest = string.Empty;
        var bytes = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(clone, NotionAggregateJson.Options));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static NotionPageSnapshot Clone(NotionPageSnapshot snapshot)
        => JsonSerializer.Deserialize<NotionPageSnapshot>(
            JsonSerializer.Serialize(snapshot, NotionAggregateJson.Options),
            NotionAggregateJson.Options)
           ?? throw new InvalidDataException("Could not clone the demo Notion aggregate.");
}
