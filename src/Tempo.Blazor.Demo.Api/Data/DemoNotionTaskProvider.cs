using System.Net;
using System.Text.RegularExpressions;
using Tempo.Blazor.Abstractions.WorkItems;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Demo.Api.Data;

/// <summary>
/// Aggregates Notion todo blocks into unified <see cref="TmWorkItem"/>s so the Notion editor,
/// Gantt and other components can share the same task source.
/// </summary>
public sealed partial class DemoNotionTaskProvider : TmWorkItemProviderBase
{
    private readonly MockNotionDataStore _pageStore;
    private readonly MockNotionBlockStore _blockStore;
    private readonly DemoNotionAggregateStore _aggregateStore;

    public DemoNotionTaskProvider(MockNotionDataStore pageStore, MockNotionBlockStore blockStore, DemoNotionAggregateStore aggregateStore)
    {
        _pageStore = pageStore;
        _blockStore = blockStore;
        _aggregateStore = aggregateStore;
    }

    public override string SourceKey => "notion";

    public override string DisplayName => "Notion tasks";

    public override TmWorkItemCapabilities Capabilities =>
        TmWorkItemCapabilities.Read | TmWorkItemCapabilities.Update;

    public override Task<Tempo.Blazor.Models.PagedResult<TmWorkItem>> SearchAsync(TmWorkItemQuery query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var pages = _pageStore.GetAllPages()
            .Where(page => !page.IsDeleted)
            .ToDictionary(page => page.Id.ToString(), page => page.Title, StringComparer.OrdinalIgnoreCase);

        var tasks = _blockStore.GetAllBlocksSnapshot()
            .Where(block => block.Type == BlockType.TodoItem)
            .Where(block => block.Content is ITodoBlockContent)
            .Where(block => pages.ContainsKey(block.PageId.ToString()))
            .Select(block => MapTask(block, (ITodoBlockContent)block.Content, pages))
            .Where(task => query.Ids.Count == 0 || query.Ids.Contains(task.Id))
            .Where(task => query.IncludeCompleted || !task.IsCompleted)
            .Where(task => string.IsNullOrWhiteSpace(query.AssigneeId)
                || task.Assignees.Any(a => string.Equals(a.Id, query.AssigneeId, StringComparison.OrdinalIgnoreCase)))
            .Where(task => string.IsNullOrWhiteSpace(query.OriginPageId)
                || string.Equals(task.OriginPageId, query.OriginPageId, StringComparison.OrdinalIgnoreCase))
            .Where(task => query.DueAfter is null || (task.DueDate is not null && task.DueDate.Value.Date >= query.DueAfter.Value.Date))
            .Where(task => query.DueBefore is null || (task.DueDate is not null && task.DueDate.Value.Date <= query.DueBefore.Value.Date))
            .Where(task => string.IsNullOrWhiteSpace(query.FreeText)
                || task.Title.Contains(query.FreeText, StringComparison.OrdinalIgnoreCase))
            .OrderBy(task => task.IsCompleted)
            .ThenBy(task => task.DueDate ?? DateTime.MaxValue)
            .ThenBy(task => task.OriginPageTitle, StringComparer.OrdinalIgnoreCase)
            .ThenBy(task => task.CreatedAt)
            .ToList();

        var skip = Math.Max(0, query.Skip);
        var take = query.Take <= 0 ? 50 : query.Take;
        var page = skip / take + 1;

        return Task.FromResult(new Tempo.Blazor.Models.PagedResult<TmWorkItem>
        {
            Items = tasks.Skip(skip).Take(take).ToList(),
            TotalCount = tasks.Count,
            Page = page,
            PageSize = take
        });
    }

    public override async Task SetCompletedAsync(string id, bool completed, CancellationToken cancellationToken = default)
    {
        await _blockStore.SetTodoCompletedAsync(id, completed, cancellationToken);

        // Completion writes mutate the block store directly, so drop the cached
        // aggregate snapshot for the owning page — otherwise the next page load
        // would serve the stale pre-toggle state and the todo renders unchecked.
        if (Guid.TryParse(id, out var blockId) && _blockStore.GetBlock(blockId) is { } block)
        {
            _aggregateStore.InvalidatePageSnapshot(block.PageId);
        }
    }

    private static TmWorkItem MapTask(PageBlock block, ITodoBlockContent todo, IReadOnlyDictionary<string, string> pageTitles)
    {
        var pageId = block.PageId.ToString();
        var assignees = new List<TmWorkItemAssignee>();
        if (!string.IsNullOrWhiteSpace(todo.AssigneeId))
        {
            assignees.Add(new TmWorkItemAssignee
            {
                Id = todo.AssigneeId,
                Name = todo.AssigneeDisplayName ?? todo.AssigneeId
            });
        }

        return new TmWorkItem
        {
            Id = block.Id.ToString(),
            SourceKey = "notion",
            Title = ToPlainText(todo.Html),
            OriginPageId = pageId,
            OriginPageTitle = pageTitles.TryGetValue(pageId, out var title) ? title : pageId,
            OriginBlockId = block.Id.ToString(),
            Assignees = assignees,
            DueDate = todo.DueDate,
            IsCompleted = todo.IsChecked,
            Status = todo.IsChecked ? TmWorkItemStatus.Done : TmWorkItemStatus.Open,
            CreatedAt = block.CreatedAt
        };
    }

    private static string ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        var withoutTags = HtmlTagRegex().Replace(html, " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);
        return WhitespaceRegex().Replace(decoded, " ").Trim();
    }

    [GeneratedRegex("<[^>]+>", RegexOptions.Compiled)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex("\\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();
}
