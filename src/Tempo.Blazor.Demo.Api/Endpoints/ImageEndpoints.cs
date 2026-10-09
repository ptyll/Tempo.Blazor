using Tempo.Blazor.Demo.Api.Data;

namespace Tempo.Blazor.Demo.Api.Endpoints;

public static class ImageEndpoints
{
    public static void MapImageEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/images").WithTags("Images");

        group.MapGet("/{entityId}", (string entityId, MockImageStore store) =>
            Results.Ok(store.Images));

        group.MapPost("/{imageId}/ticket", (string imageId, MockImageStore store) =>
        {
            var image = store.Images.FirstOrDefault(i => i.Id == imageId);
            if (image is null) return Results.NotFound();

            var ticket = store.CreateTicket(imageId);
            return Results.Ok(new { ticketUrl = $"/api/images/stream/{ticket}" });
        });

        // Serves the image content inline (no redirect): the demo images are local SVG assets
        // linked into the API output from Tempo.Blazor.Demo.SharedUI/wwwroot/gallery, so the
        // lightbox works without internet access. AppContext.BaseDirectory (not ContentRootPath)
        // is where the linked content lands under `dotnet run`.
        group.MapGet("/stream/{ticket}", (string ticket, MockImageStore store) =>
        {
            var imageId = store.ResolveTicket(ticket);
            if (imageId is null) return Results.NotFound();

            var image = store.Images.FirstOrDefault(i => i.Id == imageId);
            if (image is null) return Results.NotFound();

            var path = Path.Combine(AppContext.BaseDirectory, "gallery", $"photo-{image.Id}.svg");
            if (!File.Exists(path)) return Results.NotFound();

            return Results.File(path, contentType: "image/svg+xml");
        });

        group.MapDelete("/{imageId}", (string imageId, MockImageStore store) =>
            store.DeleteImage(imageId) ? Results.NoContent() : Results.NotFound());
    }
}
