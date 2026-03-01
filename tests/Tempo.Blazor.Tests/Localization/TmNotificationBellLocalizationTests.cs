using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Interfaces;

namespace Tempo.Blazor.Tests.Localization;

/// <summary>
/// RED tests – TmNotificationBell must use ITmLocalizer for aria-label, title,
/// "Mark all as read" and "No notifications" strings.
/// </summary>
public class TmNotificationBellLocalizationTests : LocalizationTestBase
{
    private static List<INotificationItem> UnreadItems(int count) =>
        Enumerable.Range(0, count)
            .Select(i => (INotificationItem)new TestNotif($"u{i}", $"Notif {i}", false))
            .ToList();

    [Fact]
    public void TmNotificationBell_AriaLabel_UsesLocalizer()
    {
        UseCzechLocalization();

        var cut = RenderComponent<TmNotificationBell>(p => p
            .Add(c => c.Items, UnreadItems(0)));

        cut.Find(".tm-notification-bell-button").GetAttribute("aria-label")
            .Should().Be("Oznámení");
    }

    [Fact]
    public void TmNotificationBell_AriaLabel_English_ShowsEnglishText()
    {
        var cut = RenderComponent<TmNotificationBell>(p => p
            .Add(c => c.Items, UnreadItems(0)));

        cut.Find(".tm-notification-bell-button").GetAttribute("aria-label")
            .Should().Be("Notifications");
    }

    [Fact]
    public void TmNotificationBell_Title_UsesLocalizer()
    {
        UseCzechLocalization();

        var cut = RenderComponent<TmNotificationBell>(p => p
            .Add(c => c.Items, UnreadItems(1)));

        cut.Find(".tm-notification-bell-button").Click();

        cut.Find(".tm-notification-title").TextContent
            .Should().Be("Oznámení");
    }

    [Fact]
    public void TmNotificationBell_MarkAllRead_UsesLocalizer()
    {
        UseCzechLocalization();

        var cut = RenderComponent<TmNotificationBell>(p => p
            .Add(c => c.Items, UnreadItems(2)));

        cut.Find(".tm-notification-bell-button").Click();

        cut.Find(".tm-notification-mark-all-read").TextContent.Trim()
            .Should().Be("Označit vše jako přečtené");
    }

    [Fact]
    public void TmNotificationBell_NoNotifications_UsesLocalizer()
    {
        UseCzechLocalization();

        var cut = RenderComponent<TmNotificationBell>(p => p
            .Add(c => c.Items, UnreadItems(0)));

        cut.Find(".tm-notification-bell-button").Click();

        cut.Find(".tm-notification-empty").TextContent
            .Should().Be("Žádná oznámení");
    }

    [Fact]
    public void TmNotificationBell_NoNotifications_English_ShowsEnglishText()
    {
        var cut = RenderComponent<TmNotificationBell>(p => p
            .Add(c => c.Items, UnreadItems(0)));

        cut.Find(".tm-notification-bell-button").Click();

        cut.Find(".tm-notification-empty").TextContent
            .Should().Be("No notifications");
    }

    private record TestNotif(
        string Id,
        string Title,
        bool IsRead) : INotificationItem
    {
        public string?               Body      => null;
        public DateTimeOffset        CreatedAt => DateTimeOffset.Now;
        public string?               IconName  => null;
        public NotificationSeverity  Severity  => NotificationSeverity.Info;
        public string?               ActionUrl => null;
    }
}
