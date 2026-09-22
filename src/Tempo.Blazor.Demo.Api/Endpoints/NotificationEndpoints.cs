using Microsoft.Extensions.Options;
using Tempo.Blazor.Abstractions.Interfaces;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Demo.Api.Services;
using Tempo.Blazor.WebPush;

namespace Tempo.Blazor.Demo.Api.Endpoints;

/// <summary>Web Push subscription, digest-trigger, and push-test endpoints for the notifications demo.</summary>
public static class NotificationEndpoints
{
    /// <summary>Maps notification-related endpoints.</summary>
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notifications").WithTags("Notifications");

        // GET the VAPID public key the browser needs to subscribe.
        group.MapGet("/push/vapid-public-key", (IOptions<WebPushOptions> options) =>
            Results.Ok(new { publicKey = options.Value.PublicKey }))
            .WithName("GetVapidPublicKey");

        // POST a browser push subscription.
        group.MapPost("/push/subscribe", async (IPushSubscriptionStore store, PushSubscriptionDto dto, CancellationToken ct) =>
        {
            var sub = new TmPushSubscription
            {
                UserId = dto.UserId,
                Endpoint = dto.Endpoint,
                P256dh = dto.P256dh,
                Auth = dto.Auth,
                ExpirationTime = dto.ExpirationTime
            };
            if (!sub.IsValid) return Results.BadRequest(new { error = "Incomplete subscription." });
            await store.SaveAsync(sub, ct);
            return Results.Ok(new { subscribed = true });
        }).WithName("SubscribeWebPush");

        // DELETE a subscription by endpoint.
        group.MapDelete("/push/subscribe", async (IPushSubscriptionStore store, string endpoint, CancellationToken ct) =>
        {
            await store.RemoveAsync(endpoint, ct);
            return Results.Ok(new { unsubscribed = true });
        }).WithName("UnsubscribeWebPush");

        // POST clear all stored subscriptions — E2E test isolation. The in-memory store
        // outlives browser contexts, so subscriptions from earlier suite runs would
        // otherwise keep inflating the attempted/succeeded counters of /push/test.
        group.MapPost("/push/reset", (IPushSubscriptionStore store) =>
        {
            if (store is Tempo.Blazor.Services.InMemoryPushSubscriptionStore mem)
            {
                mem.Clear();
            }
            return Results.Ok(new { reset = true });
        }).WithName("ResetWebPushSubscriptions");

        // POST send a test push to a user's subscriptions (verifies the server send path).
        group.MapPost("/push/test", async (IPushSubscriptionStore store, IWebPushSender sender, string userId, CancellationToken ct) =>
        {
            var subs = await store.GetForUserAsync(userId, ct);
            var payload = new TmWebPushPayload { Title = "Test push", Body = "Hello from Tempo.", Url = "/notifications" };
            var attempted = 0;
            var succeeded = 0;
            var expired = 0;
            foreach (var sub in subs)
            {
                attempted++;
                var result = await sender.SendAsync(sub, payload, ct);
                if (result.Success) succeeded++;
                if (result.IsExpired) { expired++; await store.RemoveAsync(sub.Endpoint, ct); }
            }
            return Results.Ok(new { attempted, succeeded, expired });
        }).WithName("TestWebPush");

        // POST publish a notification (server broadcasts it in real time).
        group.MapPost("/publish", async (ITmNotificationService service, TmNotification notification, CancellationToken ct) =>
        {
            var saved = await service.PublishAsync(notification, ct);
            return Results.Ok(saved);
        }).WithName("PublishNotification");

        // POST trigger a digest run now (returns how many digests were sent).
        group.MapPost("/digest/run", async (TmNotificationDigestService digest, CancellationToken ct) =>
        {
            var sent = await digest.RunNowAsync(ct);
            return Results.Ok(new { sent = sent.Count, recipients = sent.Select(d => d.RecipientUserId).ToArray() });
        }).WithName("RunNotificationDigest");

        return app;
    }

    /// <summary>Incoming browser PushSubscription payload.</summary>
    public sealed record PushSubscriptionDto(string UserId, string Endpoint, string P256dh, string Auth, DateTimeOffset? ExpirationTime);
}
