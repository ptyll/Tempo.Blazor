using System.Text.Json;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.NotionEditor.UI;
using Tempo.Blazor.Components.Overlay;
using Tempo.Blazor.NotionEditor.Helpers;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;
using Tempo.Blazor.Services;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

public sealed class TmNotionWatchNotificationsTests : LocalizationTestBase
{
    private readonly InMemoryNotificationStore _notifications = new();
    private readonly TestWatchProvider _watchProvider = new();

    public TmNotionWatchNotificationsTests()
    {
        Services.AddSingleton<ITmNotificationService>(_notifications);
        Services.AddSingleton<NavigationManager>(new FakeNavManager());
    }

    [Fact]
    public void TmNotificationTypes_ExposePageTypeKeys()
    {
        TmNotificationTypes.Mention.Should().Be("mention");
        TmNotificationTypes.Reply.Should().Be("reply");
        TmNotificationTypes.Reaction.Should().Be("reaction");
        TmNotificationTypes.ThreadResolved.Should().Be("thread-resolved");
        TmNotificationTypes.NewThread.Should().Be("new-thread");
        TmNotificationTypes.PageEdited.Should().Be("page-edited");
        TmNotificationTypes.PageCommentAdded.Should().Be("page-comment-added");
        TmNotificationTypes.TaskAssigned.Should().Be("task-assigned");
        TmNotificationTypes.PageShared.Should().Be("page-shared");

        var json = JsonSerializer.Serialize(TmNotificationTypes.PageEdited);
        JsonSerializer.Deserialize<string>(json).Should().Be(TmNotificationTypes.PageEdited);
    }

    [Fact]
    public async Task PageNotificationOrchestrator_NotifiesWatchers_AndSkipsActor()
    {
        await _watchProvider.WatchAsync("page-1", "alice", includeChildren: false);
        await _watchProvider.WatchAsync("page-1", "bob", includeChildren: false);
        var orchestrator = new PageNotificationOrchestrator(_notifications);

        await orchestrator.OnPageEditedAsync(_watchProvider, "page-1", "Launch Plan", "alice", "Alice");

        (await GetNotificationsAsync("alice")).Should().BeEmpty();
        var bob = await GetNotificationsAsync("bob");
        bob.Should().ContainSingle();
        bob[0].Type.Should().Be(TmNotificationTypes.PageEdited);
        bob[0].ActionUrl.Should().Contain("page-1");
    }

    [Fact]
    public void WatchButton_TogglesWatch_AndIncludeChildren()
    {
        var cut = Render<TmNotionWatchButton>(parameters => parameters
            .Add(p => p.Provider, _watchProvider)
            .Add(p => p.PageId, "page-1")
            .Add(p => p.UserId, "alice"));

        cut.Find("[data-testid='notion-watch-toggle']").Click();
        _watchProvider.IsWatchingAsync("page-1", "alice").Result.Should().BeTrue();

        cut.Find("[data-testid='notion-watch-include-children']").Change(true);
        _watchProvider.GetWatchersAsync("page-1").Result.Single().IncludeChildren.Should().BeTrue();

        cut.Find("[data-testid='notion-watch-toggle']").Click();
        _watchProvider.IsWatchingAsync("page-1", "alice").Result.Should().BeFalse();
    }

    [Fact]
    public void NotificationCenter_ShowsBadge_MarksAllRead_AndNavigates()
    {
        _notifications.PublishAsync(new TmNotification
        {
            Type = TmNotificationTypes.PageEdited,
            RecipientUserId = "alice",
            Actor = new TmUserRef { Id = "bob", DisplayName = "Bob" },
            Title = "Bob edited Launch Plan",
            ActionUrl = "/notion-editor?page=page-1"
        }).Wait();

        var nav = (FakeNavManager)Services.GetRequiredService<NavigationManager>();
        var cut = Render<TmNotionNotificationCenter>(parameters => parameters
            .Add(p => p.CurrentUserId, "alice")
            .Add(p => p.PollInterval, TimeSpan.Zero));

        cut.Find("[data-testid='notion-notification-badge']").TextContent.Should().Be("1");
        cut.Find("[data-testid='notion-notification-toggle']").Click();
        cut.Find("[data-testid='notion-notification-item']").TextContent.Should().Contain("Bob edited Launch Plan");
        cut.Find("[data-testid='notion-notification-item']").Click();

        nav.Uri.Should().Contain("/notion-editor?page=page-1");
        _notifications.GetUnreadCountAsync("alice").Result.Should().Be(0);
    }

    // ── N189: the panel is a TmOverlayPanel popover with full popup semantics ──

    [Fact]
    public void NotificationCenter_Toggle_ExposesPopupSemantics()
    {
        var cut = RenderNotificationCenter();

        var toggle = cut.Find("[data-testid='notion-notification-toggle']");
        toggle.GetAttribute("aria-haspopup").Should().Be("dialog");
        toggle.GetAttribute("aria-expanded").Should().Be("false");
    }

    [Fact]
    public void NotificationCenter_Open_RendersOverlayPopover_AndSyncsToggleAria()
    {
        var cut = RenderNotificationCenter();

        cut.Find("[data-testid='notion-notification-toggle']").Click();

        // TmOverlayPanel renders a manual top-layer popover — Escape/light-dismiss/focus
        // restore come from overlay.js, matching TmNotificationBell.
        cut.FindComponent<TmOverlayPanel>();
        var panel = cut.Find("[data-testid='notion-notification-panel']");
        panel.GetAttribute("role").Should().Be("dialog");
        panel.GetAttribute("popover").Should().Be("manual");

        var toggle = cut.Find("[data-testid='notion-notification-toggle']");
        toggle.GetAttribute("aria-expanded").Should().Be("true");
        toggle.GetAttribute("aria-controls").Should().Be(panel.Id);
    }

    [Theory]
    [InlineData("escape")]
    [InlineData("outside")]
    public async Task NotificationCenter_JsDismissal_Closes_AndNotifies(string reason)
    {
        bool? open = null;
        var cut = RenderNotificationCenter(v => open = v);

        cut.Find("[data-testid='notion-notification-toggle']").Click();
        open.Should().BeTrue();

        var overlay = cut.FindComponent<TmOverlayPanel>();
        await cut.InvokeAsync(() => overlay.Instance.NotifyDismissedAsync(reason));

        cut.FindAll("[data-testid='notion-notification-panel']").Should().BeEmpty();
        open.Should().BeFalse();
    }

    private IRenderedComponent<TmNotionNotificationCenter> RenderNotificationCenter(Action<bool>? openChanged = null)
        => Render<TmNotionNotificationCenter>(parameters => parameters
            .Add(p => p.CurrentUserId, "alice")
            .Add(p => p.PollInterval, TimeSpan.Zero)
            .Add(p => p.OnDropdownOpenChanged, EventCallback.Factory.Create<bool>(
                this, v => openChanged?.Invoke(v))));

    private Task<IReadOnlyList<TmNotification>> GetNotificationsAsync(string recipient)
        => _notifications.GetNotificationsAsync(new TmNotificationQuery { RecipientUserId = recipient });

    private sealed class TestWatchProvider : INotionWatchProvider
    {
        private readonly Dictionary<(string PageId, string UserId), NotionWatchSubscriptionDto> _items = new();

        public Task WatchAsync(string pageId, string userId, bool includeChildren, CancellationToken cancellationToken = default)
        {
            _items[(pageId, userId)] = new NotionWatchSubscriptionDto
            {
                PageId = pageId,
                UserId = userId,
                IncludeChildren = includeChildren
            };
            return Task.CompletedTask;
        }

        public Task UnwatchAsync(string pageId, string userId, CancellationToken cancellationToken = default)
        {
            _items.Remove((pageId, userId));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<NotionWatchSubscriptionDto>> GetWatchersAsync(string pageId, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<NotionWatchSubscriptionDto> watchers = _items.Values
                .Where(item => string.Equals(item.PageId, pageId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return Task.FromResult(watchers);
        }

        public Task<bool> IsWatchingAsync(string pageId, string userId, CancellationToken cancellationToken = default)
            => Task.FromResult(_items.ContainsKey((pageId, userId)));
    }

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
