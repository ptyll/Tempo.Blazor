using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.NotionEditor.Blocks;
using Tempo.Blazor.Components.NotionEditor.Sidebar;
using Tempo.Blazor.Components.NotionEditor.UI;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Localization;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;
using Tempo.Blazor.NotionEditor.Services;
using Tempo.Blazor.Services;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>
/// Phase 9: the surfaces that used to format relative time themselves now render through the
/// shared <see cref="ITmRelativeTimeFormatter"/>.
/// </summary>
public sealed class TmNotionDbPhase9RelativeTimeTests : LocalizationTestBase
{
    private readonly StubFormatter _formatter = new();
    private readonly InMemoryNotificationStore _notifications = new();

    public TmNotionDbPhase9RelativeTimeTests()
    {
        Services.AddSingleton<ITmRelativeTimeFormatter>(_formatter);
        Services.AddSingleton<ITmNotificationService>(_notifications);
        Services.AddSingleton<NavigationManager>(new FakeNavManager());
    }

    [Fact]
    public async Task NotificationCenter_UsesTheSharedFormatter()
    {
        var created = DateTimeOffset.UtcNow.AddMinutes(-5);
        await _notifications.PublishAsync(new TmNotification
        {
            Type = TmNotificationTypes.Mention,
            RecipientUserId = "alice",
            Title = "Mention",
            CreatedAt = created,
        });

        var cut = Render<TmNotionNotificationCenter>(p => p
            .Add(x => x.CurrentUserId, "alice")
            .Add(x => x.PollInterval, TimeSpan.Zero));
        cut.Find("[data-testid='notion-notification-toggle']").Click();

        cut.WaitForAssertion(() => cut.Find("time").TextContent.Trim().Should().Be("STUB-TIME"));
        _formatter.Seen.Should().Contain(created);
    }

    [Fact]
    public void SidebarRecent_UsesTheSharedFormatter()
    {
        var edited = DateTime.UtcNow.AddMinutes(-5);
        var page = new NotionPage { Id = Guid.NewGuid(), Title = "Alpha", LastEditedAt = edited };

        var cut = Render<TmNotionSidebarRecent>(p => p.Add(x => x.Pages, new[] { page }));
        cut.Find(".tm-nsr-header").Click();

        cut.Find("time.tm-nsr-item__time").TextContent.Trim().Should().Be("STUB-TIME");
        _formatter.Seen.Should().Contain(x => x.UtcDateTime == edited);
    }

    [Fact]
    public void SidebarTrash_UsesTheSharedFormatter()
    {
        var deleted = DateTime.UtcNow.AddMinutes(-5);
        var provider = new TrashProvider(deleted);
        var cut = Render<CascadingValue<NotionEditorContext>>(p => p
            .Add(x => x.Value, new NotionEditorContext { DataProvider = provider })
            .AddChildContent<TmNotionSidebarTrash>());

        cut.WaitForAssertion(() => cut.Find("time").TextContent.Trim().Should().Be("STUB-TIME"));
        _formatter.Seen.Should().Contain(x => x.UtcDateTime == deleted);
    }

    [Fact]
    public void TmNotionBlock_UsesTheSharedFormatter()
    {
        var when = DateTime.UtcNow.AddMinutes(-5);
        var block = new PageBlock
        {
            Id = Guid.NewGuid(),
            Type = BlockType.Paragraph,
            Content = new TextBlockContent { Html = "Hello" }
        };
        var cut = Render<TmNotionBlock>(p => p
            .Add(x => x.Block, block)
            .Add(x => x.BlockUnresolvedCount, 1)
            .Add(x => x.BlockLastAuthorName, "Ada")
            .Add(x => x.BlockLastEntryTime, when));

        cut.Find(".tm-notion-block__thread-wrap").TriggerEvent("onmouseenter", new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        cut.WaitForAssertion(() => cut.Find("time").TextContent.Trim().Should().Be("STUB-TIME"), timeout: TimeSpan.FromSeconds(2));
        _formatter.Seen.Should().Contain(x => x.UtcDateTime == when);
    }

    private sealed class StubFormatter : ITmRelativeTimeFormatter
    {
        public List<DateTimeOffset> Seen { get; } = [];

        public string Format(DateTimeOffset value, DateTimeOffset? now = null, TimeZoneInfo? timeZone = null)
        {
            Seen.Add(value);
            return "STUB-TIME";
        }

        public string Absolute(DateTimeOffset value, TimeZoneInfo? timeZone = null) => "ABSOLUTE";
    }

    private sealed class FakeNavManager : NavigationManager
    {
        public FakeNavManager() => Initialize("https://localhost/", "https://localhost/");
    }

    private sealed class TrashProvider(DateTime deletedAt) : INotionDataProvider
    {
        public Task<IEnumerable<INotionPage>> GetTrashAsync()
            => Task.FromResult<IEnumerable<INotionPage>>(
                [new NotionPage { Id = Guid.NewGuid(), Title = "Gone", IsDeleted = true, DeletedAt = deletedAt }]);

        public Task<INotionPage> GetPageAsync(string pageId) => throw new NotSupportedException();
        public Task<IEnumerable<INotionPage>> GetChildPagesAsync(string? parentId) => Task.FromResult(Enumerable.Empty<INotionPage>());
        public Task<IEnumerable<INotionPage>> GetFavoritesAsync() => Task.FromResult(Enumerable.Empty<INotionPage>());
        public Task<IEnumerable<INotionPage>> GetRecentPagesAsync(int count) => Task.FromResult(Enumerable.Empty<INotionPage>());
        public Task<IReadOnlyList<INotionPage>> GetPagesByLabelAsync(string label, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<INotionPage>>([]);
        public Task<IReadOnlyList<string>> GetAllLabelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>([]);
        public Task SetPageLabelsAsync(Guid pageId, IReadOnlyList<string> labels, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task<INotionPage> CreatePageAsync(string? parentId, string title) => throw new NotSupportedException();
        public Task UpdatePageAsync(INotionPage page) => Task.CompletedTask;
        public Task DeletePageAsync(string pageId) => Task.CompletedTask;
        public Task RestorePageAsync(string pageId) => Task.CompletedTask;
        public Task PermanentlyDeletePageAsync(string pageId) => Task.CompletedTask;
        public Task ToggleFavoriteAsync(string pageId, bool isFavorite) => Task.CompletedTask;
        public Task MovePageAsync(string pageId, string? newParentId) => Task.CompletedTask;
        public Task<INotionPage> DuplicatePageAsync(string pageId) => throw new NotSupportedException();
    }
}
