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
    private readonly FakeNavManager _nav = new();

    public TmNotificationBellTests()
    {
        Services.AddSingleton<ITmNotificationService>(_store);
        Services.AddSingleton<NavigationManager>(_nav);
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

    // ── 2.9.1: entries are native buttons — a div @onclick was a keyboard trap ─────────────────

    [Fact]
    public void Bell_Item_IsANativeButton_WithoutEmulationResidue()
    {
        _store.PublishAsync(MakeNotification("alice", "Test")).Wait();
        var cut = Render<TmNotificationBell>();
        cut.Find(".tm-notification-bell__button").Click();

        var item = cut.Find(".tm-notification-bell__item");
        item.TagName.Should().Be("BUTTON",
            "a div with @onclick is a keyboard trap — the entry must be a native button (WCAG 2.1.1)");
        item.GetAttribute("type").Should().Be("button");
        item.GetAttribute("role").Should().BeNull("role=button on a <button> is emulation residue");
        item.GetAttribute("tabindex").Should().BeNull("tabindex=0 on a <button> is emulation residue");
        item.QuerySelector("button").Should().BeNull("the entry holds no nested interactive element — a button inside a button is invalid HTML");
        item.QuerySelector("a").Should().BeNull("the entry holds no nested link — a link inside a button is invalid HTML");
    }

    [Fact]
    public void Bell_UnreadItem_AnnouncesTheStateToScreenReaders()
    {
        // m6 (review): the unread dot is aria-hidden decoration, so two entries — one read, one
        // not — were indistinguishable to a screen reader. The unread entry carries a localized
        // sr-only text alternative; the read entry carries none.
        _store.PublishAsync(MakeNotification("alice", "Unread one")).Wait();
        var read = MakeNotification("bob", "Already read");
        read.ReadAt = DateTimeOffset.UtcNow; // IsRead is computed from ReadAt
        _store.PublishAsync(read).Wait();
        UseCzechLocalization();
        var cut = Render<TmNotificationBell>();
        cut.Find(".tm-notification-bell__button").Click();

        var items = cut.FindAll(".tm-notification-bell__item");
        items.Should().HaveCount(2);
        var unread = items.Single(i => i.ClassList.Contains("tm-notification-bell__item--unread"));
        var marker = unread.QuerySelector(".tm-sr-only");
        marker.Should().NotBeNull("the unread entry needs a text alternative for its dot");
        marker!.TextContent.Trim().Should().Be("Nepřečtené oznámení",
            "the state text comes from ITmLocalizer, like every user-visible string");

        items.Single(i => !i.ClassList.Contains("tm-notification-bell__item--unread"))
            .QuerySelector(".tm-sr-only")
            .Should().BeNull("a read entry announces no unread state");
    }

    [Fact]
    public void Bell_Item_NativeActivation_MarksReadAndNavigates_ExactlyOnce()
    {
        var notification = MakeNotification("alice", "Deploy complete");
        notification.ActionUrl = "/inbox/1";
        _store.PublishAsync(notification).Wait();
        var cut = Render<TmNotificationBell>();
        cut.Find(".tm-notification-bell__button").Click();
        var item = cut.Find(".tm-notification-bell__item");

        // A native <button> must not carry emulated key handlers
        // (docs/keyboard-activation-convention.md): the missing-handler exceptions ARE the proof
        // that Enter/Space cannot double-fire — the browser delivers the click itself.
        var enterDown = () => item.KeyDown("Enter");
        enterDown.Should().Throw<MissingEventHandlerException>();
        var spaceDown = () => item.KeyDown(" ");
        spaceDown.Should().Throw<MissingEventHandlerException>();
        var spaceUp = () => item.KeyUp(" ");
        spaceUp.Should().Throw<MissingEventHandlerException>();

        item.Click();
        _store.GetUnreadCountAsync("demo").Wait();
        _store.GetUnreadCountAsync("demo").Result.Should().Be(0, "the click marks the entry read");
        _nav.Uri.Should().Contain("/inbox/1", "the entry's action navigates");
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
