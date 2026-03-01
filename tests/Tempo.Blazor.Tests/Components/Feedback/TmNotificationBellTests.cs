using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Feedback;

file sealed record TestNotification(
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

/// <summary>TDD tests for TmNotificationBell.</summary>
public class TmNotificationBellTests : LocalizationTestBase
{
    private static List<INotificationItem> MakeItems(int unread, int read) =>
    [
        .. Enumerable.Range(0, unread).Select(i => (INotificationItem)new TestNotification($"u{i}", $"Unread {i}", false)),
        .. Enumerable.Range(0, read).Select(i => (INotificationItem)new TestNotification($"r{i}", $"Read {i}", true)),
    ];

    [Fact]
    public void TmNotificationBell_Has_Bell_Button()
    {
        var cut = RenderComponent<TmNotificationBell>(p => p
            .Add(c => c.Items, MakeItems(0, 0)));

        cut.Find(".tm-notification-bell-button").Should().NotBeNull();
    }

    [Fact]
    public void TmNotificationBell_No_Badge_When_No_Unread()
    {
        var cut = RenderComponent<TmNotificationBell>(p => p
            .Add(c => c.Items, MakeItems(0, 2)));

        cut.FindAll(".tm-notification-badge").Should().BeEmpty();
    }

    [Fact]
    public void TmNotificationBell_Shows_Badge_With_Unread_Count()
    {
        var cut = RenderComponent<TmNotificationBell>(p => p
            .Add(c => c.Items, MakeItems(3, 1)));

        cut.Find(".tm-notification-badge").TextContent.Should().Contain("3");
    }

    [Fact]
    public void TmNotificationBell_Dropdown_Hidden_By_Default()
    {
        var cut = RenderComponent<TmNotificationBell>(p => p
            .Add(c => c.Items, MakeItems(1, 0)));

        cut.FindAll(".tm-notification-dropdown").Should().BeEmpty();
    }

    [Fact]
    public void TmNotificationBell_Click_Opens_Dropdown()
    {
        var cut = RenderComponent<TmNotificationBell>(p => p
            .Add(c => c.Items, MakeItems(1, 1)));

        cut.Find(".tm-notification-bell-button").Click();

        cut.FindAll(".tm-notification-dropdown").Should().NotBeEmpty();
    }

    [Fact]
    public void TmNotificationBell_Dropdown_Shows_Items()
    {
        var cut = RenderComponent<TmNotificationBell>(p => p
            .Add(c => c.Items, MakeItems(2, 1)));

        cut.Find(".tm-notification-bell-button").Click();

        cut.FindAll(".tm-notification-item").Count.Should().Be(3);
    }

    [Fact]
    public void TmNotificationBell_MarkAllRead_Button_Shown_When_Unread_Exist()
    {
        var cut = RenderComponent<TmNotificationBell>(p => p
            .Add(c => c.Items, MakeItems(2, 0)));

        cut.Find(".tm-notification-bell-button").Click();

        cut.FindAll(".tm-notification-mark-all-read").Should().NotBeEmpty();
    }

    [Fact]
    public void TmNotificationBell_OnMarkAllRead_Fires()
    {
        var fired = false;
        var cut = RenderComponent<TmNotificationBell>(p => p
            .Add(c => c.Items, MakeItems(2, 0))
            .Add(c => c.OnMarkAllRead, EventCallback.Factory.Create(this, () => fired = true)));

        cut.Find(".tm-notification-bell-button").Click();
        cut.Find(".tm-notification-mark-all-read").Click();

        fired.Should().BeTrue();
    }
}
