using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.Notifications;
using Tempo.Blazor.Services;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Notifications;

public class TmNotificationBellTests : LocalizationTestBase
{
    private readonly InMemoryNotificationStore _store = new();

    public TmNotificationBellTests()
    {
        Services.AddSingleton<ITmNotificationService>(_store);
        Services.AddSingleton<NavigationManager>(new FakeNavManager());
    }

    [Fact]
    public void Bell_FormatsTheStampThroughTheSharedFormatter_NotAPrivateEnglishCopy()
    {
        // J9: the bell carried its own "5m ago". Czech must come from ITmRelativeTimeFormatter.
        UseCzechLocalization();
        var notification = MakeNotification("alice", "Test");
        notification.CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        _store.PublishAsync(notification).Wait();

        var cut = Render<TmNotificationBell>();
        cut.Find("button").Click();

        cut.Markup.Should().Contain("před 5 min");
        cut.Markup.Should().NotContain("5m ago");
    }

    [Fact]
    public void Bell_ShowsBadge_WhenUnreadNotifications()
    {
        _store.PublishAsync(MakeNotification("alice", "Test")).Wait();
        var cut = Render<TmNotificationBell>();
        cut.FindAll(".tm-notification-bell__badge").Should().HaveCount(1);
    }

    [Fact]
    public void Bell_HidesBadge_WhenNoUnread()
    {
        var cut = Render<TmNotificationBell>();
        cut.FindAll(".tm-notification-bell__badge").Should().BeEmpty();
    }

    [Fact]
    public void Bell_Click_OpensPanel()
    {
        _store.PublishAsync(MakeNotification("alice", "Test")).Wait();
        var cut = Render<TmNotificationBell>();
        cut.Find(".tm-notification-bell__button").Click();
        cut.FindAll(".tm-notification-bell__dropdown").Should().HaveCount(1);
    }

    [Fact]
    public void Bell_MarkAllRead_ClearsBadge()
    {
        _store.PublishAsync(MakeNotification("alice", "Test")).Wait();
        var cut = Render<TmNotificationBell>();
        cut.Find(".tm-notification-bell__button").Click();
        cut.Find(".tm-notification-bell__mark-all").Click();
        cut.FindAll(".tm-notification-bell__badge").Should().BeEmpty();
    }

    [Fact]
    public void Bell_Notification_RendersMessage()
    {
        _store.PublishAsync(MakeNotification("alice", "Deploy complete")).Wait();
        var cut = Render<TmNotificationBell>();
        cut.Find(".tm-notification-bell__button").Click();
        cut.Find(".tm-notification-bell__message").TextContent.Should().Contain("Deploy complete");
    }

    [Fact]
    public void Bell_Empty_RendersEmptyState()
    {
        var cut = Render<TmNotificationBell>();
        cut.Find(".tm-notification-bell__button").Click();
        cut.Find(".tm-notification-bell__empty").Should().NotBeNull();
    }

    [Fact]
    public void Bell_Filter_ShowsOnlyUnread()
    {
        _store.PublishAsync(MakeNotification("alice", "Unread 1")).Wait();
        _store.MarkAllAsReadAsync("demo").Wait();
        _store.PublishAsync(MakeNotification("bob", "Unread 2")).Wait();
        var cut = Render<TmNotificationBell>(p => p.Add(c => c.CurrentUserId, "demo"));
        cut.Find(".tm-notification-bell__button").Click();
        cut.Find(".tm-notification-bell__filter").Click(); // switch to only unread
        cut.FindAll(".tm-notification-bell__item").Should().HaveCount(1);
        cut.Find(".tm-notification-bell__message").TextContent.Should().Contain("Unread 2");
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
