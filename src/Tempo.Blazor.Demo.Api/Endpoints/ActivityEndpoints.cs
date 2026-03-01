using Tempo.Blazor.Demo.Api.Data;
using Tempo.Blazor.Demo.Shared;

namespace Tempo.Blazor.Demo.Api.Endpoints;

public static class ActivityEndpoints
{
    public static void MapActivityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/activity").WithTags("Activity");

        group.MapGet("/{entityId}/timeline", (string entityId, MockActivityStore store) =>
        {
            if (!store.Timelines.TryGetValue(entityId, out var entries))
                return Results.Ok(Array.Empty<TimelineEntryDto>());

            return Results.Ok(entries.OrderByDescending(e => e.CreatedAt).ToList());
        });

        group.MapGet("/{entityId}/comments", (string entityId, MockActivityStore store) =>
        {
            if (!store.Comments.TryGetValue(entityId, out var comments))
                return Results.Ok(Array.Empty<CommentDto>());

            return Results.Ok(comments.OrderByDescending(c => c.CreatedAt).ToList());
        });

        group.MapPost("/{entityId}/comments", (string entityId, AddCommentRequest body, MockActivityStore store) =>
        {
            var comments = store.Comments.GetValueOrDefault(entityId);
            if (comments is null)
            {
                comments = [];
                store.Comments[entityId] = comments;
            }

            var comment = new CommentDto(
                Id: Guid.NewGuid().ToString("N")[..8],
                AuthorName: "Demo User",
                AuthorAvatarUrl: null,
                CreatedAt: DateTimeOffset.Now,
                UpdatedAt: null,
                HtmlContent: body.HtmlContent,
                CanEdit: true,
                CanDelete: true);

            comments.Insert(0, comment);

            var timeline = store.Timelines.GetValueOrDefault(entityId);
            if (timeline is null)
            {
                timeline = [];
                store.Timelines[entityId] = timeline;
            }
            timeline.Insert(0, new TimelineEntryDto(
                Id: Guid.NewGuid().ToString("N")[..8],
                EntryType: "comment",
                AuthorName: "Demo User",
                AuthorAvatarUrl: null,
                CreatedAt: DateTimeOffset.Now,
                HtmlContent: null,
                PlainContent: "Added a comment."));

            return Results.Created($"/api/activity/{entityId}/comments/{comment.Id}", comment);
        });

        group.MapPut("/{entityId}/comments/{commentId}", (string entityId, string commentId,
            UpdateCommentRequest body, MockActivityStore store) =>
        {
            if (!store.Comments.TryGetValue(entityId, out var comments))
                return Results.NotFound();

            var idx = comments.FindIndex(c => c.Id == commentId);
            if (idx < 0) return Results.NotFound();

            var updated = comments[idx] with
            {
                HtmlContent = body.HtmlContent,
                UpdatedAt = DateTimeOffset.Now
            };
            comments[idx] = updated;
            return Results.Ok(updated);
        });

        group.MapDelete("/{entityId}/comments/{commentId}", (string entityId, string commentId,
            MockActivityStore store) =>
        {
            if (!store.Comments.TryGetValue(entityId, out var comments))
                return Results.NotFound();

            var comment = comments.FirstOrDefault(c => c.Id == commentId);
            if (comment is null) return Results.NotFound();

            comments.Remove(comment);
            return Results.NoContent();
        });
    }
}

public record AddCommentRequest(string HtmlContent);
public record UpdateCommentRequest(string HtmlContent);
