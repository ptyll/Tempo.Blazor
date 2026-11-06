using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.NotionEditor.UI;
using Tempo.Blazor.Components.Overlay;
using Tempo.Blazor.NotionEditor.Helpers;
using Tempo.Blazor.Services;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>
/// Phase 5 (P7): the Notion notification center — the notification body renders as a second
/// line, the filter exposes <c>aria-pressed</c>, the "view all" footer exists only with an
/// <c>OnViewAll</c> handler, and the overlay panel resolves its mobile presentation per the
/// cascaded layout (sheet on a mobile layout).
/// </summary>
public sealed class TmNotionNotificationCenterPhase5Tests : LocalizationTestBase
{
    private readonly InMemoryNotificationStore _notifications = new();

    public TmNotionNotificationCenterPhase5Tests()
    {
        Services.AddSingleton<ITmNotificationService>(_notifications);
        Services.AddSingleton<NavigationManager>(new FakeNavManager());
        UseCustomLocalization(new Dictionary<string, string>
        {
            ["Notion_Notifications_Title"] = "Notifications",
            ["Notion_Notifications_MarkAllRead"] = "Mark all as read",
            ["Notion_Notifications_Empty"] = "No notifications.",
            ["Notion_Notifications_JustNow"] = "just now",
            ["Notion_Notifications_MinutesAgo"] = "{0}m ago",
            ["Notion_Notifications_HoursAgo"] = "{0}h ago",
            ["Notion_Notifications_DaysAgo"] = "{0}d ago",
            ["TmNotificationBell_ShowAll"] = "Show all",
            ["TmNotificationBell_ShowOnlyUnread"] = "Show unread only",
            ["TmNotificationBell_NewCount_One"] = "{0} new notification",
            ["TmNotificationBell_NewCount_Other"] = "{0} new notifications",
            ["TmNotionNotifications_ViewAll"] = "View all"
        });
    }

    [Fact]
    public async Task Item_RendersBodyAsSecondLine()
    {
        await SeedAsync(new TmNotification
        {
            Type = TmNotificationTypes.Mention,
            RecipientUserId = "alice",
            Actor = new TmUserRef { Id = "bob", DisplayName = "Bob" },
            Title = "Bob mentioned you",
            Body = "Please review the Product section.",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        });

        var cut = RenderCenter();
        cut.Find("[data-testid='notion-notification-toggle']").Click();

        cut.WaitForAssertion(() =>
        {
            var body = cut.Find(".tm-notion-notifications__item .tm-notion-notifications__body");
            body.TextContent.Should().Contain("Please review the Product section.");
        });
    }

    [Fact]
    public void FilterButton_ExposesAriaPressed()
    {
        var cut = RenderCenter();
        cut.Find("[data-testid='notion-notification-toggle']").Click();

        var filter = cut.Find("[data-testid='notion-notification-filter']");
        filter.GetAttribute("aria-pressed").Should().Be("false");
        filter.Click();
        cut.Find("[data-testid='notion-notification-filter']").GetAttribute("aria-pressed").Should().Be("true");
    }

    [Fact]
    public void WithoutOnViewAll_PanelHasNoFooter()
    {
        var cut = RenderCenter();
        cut.Find("[data-testid='notion-notification-toggle']").Click();

        cut.FindAll(".tm-notion-notifications__footer").Should().BeEmpty();
        cut.FindAll("[data-testid='notion-notification-view-all']").Should().BeEmpty();
    }

    [Fact]
    public async Task WithOnViewAll_FooterInvokesCallbackAndCloses()
    {
        await SeedAsync(new TmNotification
        {
            Type = TmNotificationTypes.PageEdited,
            RecipientUserId = "alice",
            Title = "Page updated",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var viewed = false;
        var cut = RenderCenter(onViewAll: () => viewed = true);
        cut.Find("[data-testid='notion-notification-toggle']").Click();

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='notion-notification-view-all']").Should().NotBeNull());
        cut.Find("[data-testid='notion-notification-view-all']").Click();

        viewed.Should().BeTrue();
        cut.FindAll("[data-testid='notion-notification-panel']").Should().BeEmpty();
    }

    [Fact]
    public void OverlayPanel_ResolvesMobilePresentationAutomatically()
    {
        var cut = RenderCenter();
        cut.Find("[data-testid='notion-notification-toggle']").Click();

        cut.FindComponent<TmOverlayPanel>().Instance.MobilePresentation
            .Should().Be(PanelPresentation.Auto);
    }

    private IRenderedComponent<TmNotionNotificationCenter> RenderCenter(Action? onViewAll = null)
        => Render<TmNotionNotificationCenter>(builder =>
        {
            builder.Add(p => p.CurrentUserId, "alice");
            builder.Add(p => p.PollInterval, TimeSpan.Zero);
            if (onViewAll is not null)
                builder.Add(p => p.OnViewAll, EventCallback.Factory.Create(this, onViewAll));
        });

    private Task SeedAsync(TmNotification notification) => _notifications.PublishAsync(notification);

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
