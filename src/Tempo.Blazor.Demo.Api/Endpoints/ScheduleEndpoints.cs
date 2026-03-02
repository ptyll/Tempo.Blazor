using Tempo.Blazor.Demo.Api.Data;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Demo.Api.Endpoints;

public static class ScheduleEndpoints
{
    public static void MapScheduleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/schedule").WithTags("Schedule");

        // GET /api/schedule/events?start=...&end=...
        group.MapGet("/events", (DateTime start, DateTime end, MockScheduleStore store) =>
        {
            var events = store.GetEvents(start, end);
            return Results.Ok(events);
        });

        // GET /api/schedule/resources
        group.MapGet("/resources", (MockScheduleStore store) =>
        {
            return Results.Ok(store.GetResources());
        });

        // GET /api/schedule/events/{id}
        group.MapGet("/events/{id}", (string id, MockScheduleStore store) =>
        {
            var evt = store.GetEvent(id);
            return evt is not null ? Results.Ok(evt) : Results.NotFound();
        });

        // POST /api/schedule/events
        group.MapPost("/events", (CreateEventRequest request, MockScheduleStore store) =>
        {
            var evt = store.CreateEvent(request.Title, request.Start, request.End, request.Color, request.ResourceId);
            return Results.Created($"/api/schedule/events/{evt.Id}", evt);
        });

        // PUT /api/schedule/events/{id}
        group.MapPut("/events/{id}", (string id, UpdateEventRequest request, MockScheduleStore store) =>
        {
            var success = store.UpdateEvent(id, request.Start, request.End);
            return success ? Results.NoContent() : Results.NotFound();
        });

        // DELETE /api/schedule/events/{id}
        group.MapDelete("/events/{id}", (string id, MockScheduleStore store) =>
        {
            var success = store.DeleteEvent(id);
            return success ? Results.NoContent() : Results.NotFound();
        });
    }
}

public record CreateEventRequest(
    string Title,
    DateTime Start,
    DateTime End,
    string Color,
    string? ResourceId = null
);

public record UpdateEventRequest(DateTime Start, DateTime End);
