using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.Notifications;
using Tempo.Blazor.Services;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Notifications;

/// <summary>
/// F6 follow-up (c51ddf81 #4): the notification toast container is inert-exempt status UI — its
/// close button can hold focus while a modal sheet/dialog is open. Removing a toast removes that
/// button, so the container asks the shared focus trap to recover the lost focus
/// (restoreTrapFocusIfLost) once per render in which an id leaves the visible set, and never on
/// a push. Mirrors the TmToastContainer behaviour pinned in Feedback/TmToastTests.
/// </summary>
public class TmNotificationToastContainerTests : LocalizationTestBase
{
    private readonly InMemoryNotificationStore _store = new();

    public TmNotificationToastContainerTests()
    {
        Services.AddSingleton<ITmNotificationService>(_store);
        Services.AddSingleton<NavigationManager>(new FakeNavManager());
    }

    [Fact]
    public void Container_PushingAToast_DoesNotAskTheTrapToRecoverFocus()
    {
        var trap = JSInterop.SetupModule("./_content/Tempo.Blazor/js/tm-focus-trap.js");
        trap.Setup<bool>("restoreTrapFocusIfLost", _ => true).SetResult(true);

        var cut = Render<TmNotificationToastContainer>();

        _store.PublishAsync(MakeNotification("alice", "Hello")).Wait();
        cut.WaitForAssertion(() => cut.FindAll(".tm-notification-toast").Should().HaveCount(1));

        trap.Invocations.Where(i => i.Identifier == "restoreTrapFocusIfLost").Should().BeEmpty(
            "a push never moves focus and must not ask the trap for anything");
    }

    [Fact]
    public void Container_RemovingAToast_AsksTheTrapToRecoverLostFocus()
    {
        var trap = JSInterop.SetupModule("./_content/Tempo.Blazor/js/tm-focus-trap.js");
        trap.Setup<bool>("restoreTrapFocusIfLost", _ => true).SetResult(true);

        var cut = Render<TmNotificationToastContainer>();

        _store.PublishAsync(MakeNotification("alice", "Hello")).Wait();
        cut.WaitForAssertion(() => cut.FindAll(".tm-notification-toast").Should().HaveCount(1));

        cut.Find(".tm-notification-toast__close").Click();
        cut.WaitForAssertion(() => cut.FindAll(".tm-notification-toast").Should().BeEmpty());

        trap.Invocations.Where(i => i.Identifier == "restoreTrapFocusIfLost").Should().HaveCount(1,
            "the removed toast took its focused close button along — the trap gets one chance to recover");
    }

    [Fact]
    public void Container_CloseButton_HasAnAccessibleName()
    {
        // Review fix: the close button is icon-only — without an accessible name a screen reader
        // announces an unnamed button. It uses the same localized key as the plain toast container.
        var cut = Render<TmNotificationToastContainer>();

        _store.PublishAsync(MakeNotification("alice", "Hello")).Wait();
        cut.WaitForAssertion(() => cut.FindAll(".tm-notification-toast").Should().HaveCount(1));

        cut.Find(".tm-notification-toast__close").GetAttribute("aria-label").Should().Be("Dismiss");
    }

    [Fact]
    public void Container_QueueRefillAtCap_AsksTheTrapToRecoverLostFocus()
    {
        // Review fix: the ask keyed on the visible COUNT misses the queue refill — at the cap of 3
        // a close removes one toast and IMMEDIATELY dequeues the next, so the count never drops
        // (3 -> 3) while the closed card (possibly holding focus) left the DOM. The visible id set
        // drops an entry: exactly one ask.
        var trap = JSInterop.SetupModule("./_content/Tempo.Blazor/js/tm-focus-trap.js");
        trap.Setup<bool>("restoreTrapFocusIfLost", _ => true).SetResult(true);

        var cut = Render<TmNotificationToastContainer>();

        for (var i = 1; i <= 4; i++)
        {
            var expected = Math.Min(i, 3);
            _store.PublishAsync(MakeNotification($"user{i}", $"Hello {i}")).Wait();
            cut.WaitForAssertion(() => cut.FindAll(".tm-notification-toast").Should().HaveCount(expected));
        }

        trap.Invocations.Where(i => i.Identifier == "restoreTrapFocusIfLost").Should().BeEmpty(
            "the fourth publish only queues — the visible set is unchanged");

        cut.Find(".tm-notification-toast__close").Click();
        cut.WaitForAssertion(() => cut.FindAll(".tm-notification-toast").Should().HaveCount(3,
            "closing one toast at the cap refills from the queue — the count stays 3"));

        trap.Invocations.Where(i => i.Identifier == "restoreTrapFocusIfLost").Should().HaveCount(1,
            "the closed card left the visible set although the count never dropped");
    }

    private static TmNotification MakeNotification(string sender, string message) => new()
    {
        Type = TmNotificationTypes.Mention,
        RecipientUserId = "demo",
        Actor = new TmUserRef { Id = sender, DisplayName = sender },
        Title = message,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private sealed class FakeNavManager : NavigationManager
    {
        public FakeNavManager()
        {
            Initialize("https://localhost/", "https://localhost/");
        }

        protected override void NavigateToCore(string uri, bool forceLoad)
        {
            Uri = ToAbsoluteUri(uri).ToString();
        }
    }
}
