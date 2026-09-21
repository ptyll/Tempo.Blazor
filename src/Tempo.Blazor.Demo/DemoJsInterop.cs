using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Services;

namespace Tempo.Blazor.Demo.Services;

/// <summary>
/// JS-interop helper exposed to E2E tests so they can inject demo notifications
/// directly without relying on mention orchestration (which targets other users).
/// </summary>
public static class DemoJsInterop
{
    private static ITmNotificationService? _notificationService;
    private static InMemoryNotificationStore? _store;

    public static void Initialize(ITmNotificationService service)
    {
        _notificationService = service;
        _store = service as InMemoryNotificationStore;
    }

    [JSInvokable("NotifyDemoAsync")]
    public static async Task NotifyDemoAsync(string message, string deepLink)
    {
        if (_notificationService is null)
            throw new InvalidOperationException("DemoJsInterop not initialized");

        await _notificationService.PublishAsync(new TmNotification
        {
            Type = TmNotificationTypes.Mention,
            RecipientUserId = "demo",
            Actor = new TmUserRef { Id = "system", DisplayName = "System" },
            Title = message,
            ActionUrl = deepLink
        });
    }

    [JSInvokable("ClearDemoNotificationsAsync")]
    public static async Task ClearDemoNotificationsAsync()
    {
        // The demo hosts register an HTTP-backed DemoNotionNotificationService (not the
        // in-memory store), so "_store?.ClearAll()" was a silent no-op and stale
        // notifications leaked across tests. Dispatch on the concrete service type.
        switch (_notificationService)
        {
            case InMemoryNotificationStore mem:
                mem.ClearAll();
                break;
            case DemoNotionNotificationService http:
                await http.ClearAsync();
                break;
        }
    }
}
