using Tempo.Blazor.Demo.Shared;

namespace Tempo.Blazor.Demo.Api.Data;

public class MockActivityStore
{
    private static readonly string[] Authors = ["Alice Nováková", "Bob Svoboda", "Carol Dvořáková", "System"];
    private static readonly string[] EntryTypes = ["status_change", "comment", "assignment", "internal_note"];
    private static readonly string[] StatusChanges =
        ["Changed status to <b>In Progress</b>", "Moved to <b>Review</b>", "Resolved as <b>Done</b>", "Reopened"];
    private static readonly string[] Assignments =
        ["Assigned to <b>Alice</b>", "Reassigned to <b>Bob</b>", "Auto-assigned by workflow"];

    public Dictionary<string, List<TimelineEntryDto>> Timelines { get; } = new();
    public Dictionary<string, List<CommentDto>> Comments { get; } = new();

    public MockActivityStore()
    {
        foreach (var entity in new[] { "task-1", "task-2", "task-3" })
        {
            Timelines[entity] = GenerateTimeline(entity);
            Comments[entity] = GenerateComments(entity);
        }
    }

    private static List<TimelineEntryDto> GenerateTimeline(string entityId)
    {
        var hash = entityId.GetHashCode();
        var entries = new List<TimelineEntryDto>();

        for (var i = 0; i < 8; i++)
        {
            var idx = Math.Abs(hash + i);
            var type = EntryTypes[idx % EntryTypes.Length];
            var author = Authors[idx % Authors.Length];
            var isInternal = type == "internal_note";
            string? html = type switch
            {
                "status_change" => StatusChanges[idx % StatusChanges.Length],
                "assignment" => Assignments[idx % Assignments.Length],
                "internal_note" => "Auto-processed by background job.",
                _ => null
            };
            string? plain = type == "comment" ? $"Comment #{i + 1} on {entityId} by {author}." : null;

            entries.Add(new TimelineEntryDto(
                Id: $"{entityId}-tl-{i + 1}",
                EntryType: type,
                AuthorName: author,
                AuthorAvatarUrl: null,
                CreatedAt: DateTimeOffset.Now.AddHours(-(i * 4 + idx % 3)),
                HtmlContent: html,
                PlainContent: plain,
                IsInternal: isInternal));
        }

        return entries.OrderByDescending(e => e.CreatedAt).ToList();
    }

    private static List<CommentDto> GenerateComments(string entityId)
    {
        var hash = Math.Abs(entityId.GetHashCode());
        var comments = new List<CommentDto>();

        for (var i = 0; i < 5; i++)
        {
            var author = Authors[(hash + i) % Authors.Length];
            comments.Add(new CommentDto(
                Id: $"{entityId}-c-{i + 1}",
                AuthorName: author,
                AuthorAvatarUrl: null,
                CreatedAt: DateTimeOffset.Now.AddHours(-(i * 6 + hash % 2)),
                UpdatedAt: i == 1 ? DateTimeOffset.Now.AddHours(-1) : null,
                HtmlContent: $"<p>This is comment <b>#{i + 1}</b> on <em>{entityId}</em> by {author}.</p>",
                CanEdit: i < 2,
                CanDelete: i < 3));
        }

        return comments.OrderByDescending(c => c.CreatedAt).ToList();
    }
}
