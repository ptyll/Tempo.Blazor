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

        group.MapGet("/stream/{ticket}", (string ticket, MockImageStore store) =>
        {
            var imageId = store.ResolveTicket(ticket);
            if (imageId is null) return Results.NotFound();

            var image = store.Images.FirstOrDefault(i => i.Id == imageId);
            if (image is null) return Results.NotFound();

            return Results.Redirect(image.Url ?? $"https://picsum.photos/seed/{imageId}/1200/900");
        });

        group.MapDelete("/{imageId}", (string imageId, MockImageStore store) =>
            store.DeleteImage(imageId) ? Results.NoContent() : Results.NotFound());
    }
}
