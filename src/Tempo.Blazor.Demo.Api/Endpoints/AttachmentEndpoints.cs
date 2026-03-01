using Tempo.Blazor.Demo.Api.Data;

namespace Tempo.Blazor.Demo.Api.Endpoints;

public static class AttachmentEndpoints
{
    public static void MapAttachmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/attachments").WithTags("Attachments");

        group.MapGet("/{entityId}", (string entityId, MockAttachmentStore store) =>
        {
            if (!store.Attachments.TryGetValue(entityId, out var attachments))
                return Results.Ok(Array.Empty<object>());

            return Results.Ok(attachments);
        });

        group.MapDelete("/item/{attachmentId}", (string attachmentId, MockAttachmentStore store) =>
        {
            foreach (var list in store.Attachments.Values)
            {
                var att = list.FirstOrDefault(a => a.Id == attachmentId);
                if (att is not null)
                {
                    list.Remove(att);
                    return Results.NoContent();
                }
            }
            return Results.NotFound();
        });

        group.MapPost("/chunk", (ChunkUploadRequest body, MockAttachmentStore store) =>
        {
            var data = Convert.FromBase64String(body.Data);
            var completedId = store.ProcessChunk(
                body.FileName, body.ContentType, body.TotalSize,
                body.ChunkIndex, body.TotalChunks, data, body.EntityId);

            if (completedId is not null)
                return Results.Ok(new { attachmentId = completedId, completed = true });

            return Results.Ok(new { completed = false });
        });

        group.MapGet("/download/{attachmentId}", (string attachmentId) =>
        {
            return Results.Ok(new { url = $"https://example.com/files/{attachmentId}" });
        });
    }
}

public record ChunkUploadRequest(
    string FileName,
    string ContentType,
    long TotalSize,
    int ChunkIndex,
    int TotalChunks,
    string Data,
    string? EntityId);
