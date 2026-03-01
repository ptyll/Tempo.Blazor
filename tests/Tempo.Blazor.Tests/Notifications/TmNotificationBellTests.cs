using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Notifications;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Notifications;

public class TmNotificationBellTests : LocalizationTestBase
{
    private static TestNotification Unread(string id = "1", string title = "Test") =>
        new(id, title, false, NotificationSeverity.Info);

    private static TestNotification Read(string id = "2", string title = "Read") =>
        new(id, title, true, NotificationSeverity.Info);

    private record TestNotification(
        string Id,
        string Title,
        bool IsRead,
        NotificationSeverity Severity,
        string? Body             = null,
        string? IconName         = null,
        string? ActionUrl        = null) : INotificationItem
    {
        public DateTimeOffset CreatedAt { get; } = DateTimeOffset.Now.AddMinutes(-5);
    }

    [Fact]
    public void Bell_ShowsBadge_WhenUnreadNotifications()
    {
        var cut = RenderComponent<TmNotificationBell>(p => p
            .Add(c => c.Notifications, new[] { Unread() }));

        cut.FindAll(".tm-notification-badge").Should().HaveCount(1);
    }

    [Fact]
    public void Bell_HidesBadge_WhenAllRead()
    {
        var cut = RenderComponent<TmNotificationBell>(p => p
            .Add(c => c.Notifications, new[] { Read() }));

        cut.FindAll(".tm-notification-badge").Should().BeEmpty();
    }

    [Fact]
    public void Bell_Click_OpensPanel()
    {
        var cut = RenderComponent<TmNotificationBell>(p => p
            .Add(c => c.Notifications, new[] { Unread() }));

        cut.Find(".tm-notification-bell-button").Click();

        cut.FindAll(".tm-notification-dropdown").Should().HaveCount(1);
    }

    [Fact]
    public void Bell_MarkAsRead_CallsCallback()
    {
        string? markedId = null;
        var cut = RenderComponent<TmNotificationBell>(p => p
            .Add(c => c.Notifications,  new[] { Unread("42") })
            .Add(c => c.OnMarkAsRead,   (string id) => markedId = id));

        cut.Find(".tm-notification-bell-button").Click();
        cut.Find(".tm-notification-mark-read").Click();

        markedId.Should().Be("42");
    }

    [Fact]
    public void Bell_MarkAllRead_CallsCallback()
    {
        var called = false;
        var cut = RenderComponent<TmNotificationBell>(p => p
            .Add(c => c.Notifications,  new[] { Unread() })
            .Add(c => c.OnMarkAllRead,  () => called = true));

        cut.Find(".tm-notification-bell-button").Click();
        cut.Find(".tm-notification-mark-all-read").Click();

        called.Should().BeTrue();
    }

    [Fact]
    public void Bell_NotificationItem_RendersTitle()
    {
        var cut = RenderComponent<TmNotificationBell>(p => p
            .Add(c => c.Notifications, new[] { Unread(title: "Deploy complete") }));

        cut.Find(".tm-notification-bell-button").Click();

        cut.Find(".tm-notification-item-title").TextContent.Should().Contain("Deploy complete");
    }

    [Fact]
    public void Bell_NotificationItem_SeverityIcon_Rendered()
    {
        var warning = new TestNotification("1", "Disk almost full", false, NotificationSeverity.Warning);
        var cut = RenderComponent<TmNotificationBell>(p => p
            .Add(c => c.Notifications, new[] { warning }));

        cut.Find(".tm-notification-bell-button").Click();

        cut.FindAll(".tm-notification-severity").Should().NotBeEmpty();
    }

    [Fact]
    public void Bell_Empty_RendersEmptyState()
    {
        var cut = RenderComponent<TmNotificationBell>(p => p
            .Add(c => c.Notifications, Array.Empty<INotificationItem>()));

        cut.Find(".tm-notification-bell-button").Click();

        cut.Find(".tm-notification-empty").Should().NotBeNull();
    }

    [Fact]
    public void Bell_MaxVisible_LimitsItems()
    {
        var items = Enumerable.Range(1, 10)
            .Select(i => Unread(i.ToString(), $"Notif {i}"))
            .ToArray();

        var cut = RenderComponent<TmNotificationBell>(p => p
            .Add(c => c.Notifications, items)
            .Add(c => c.MaxVisible,    3));

        cut.Find(".tm-notification-bell-button").Click();

        cut.FindAll(".tm-notification-item").Should().HaveCount(3);
    }

    [Fact]
    public void Bell_RelativeTime_UsesLocalizer()
    {
        var item = new TestNotification("1", "Test", false, NotificationSeverity.Info)
            { };
        var cut = RenderComponent<TmNotificationBell>(p => p
            .Add(c => c.Notifications, new[] { item }));

        cut.Find(".tm-notification-bell-button").Click();

        cut.Find(".tm-notification-item-time").TextContent.Should().NotBeEmpty();
    }
}
