using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.NotionEditor;
using Tempo.Blazor.Components.NotionEditor.Page;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Helpers;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;
using Tempo.Blazor.Services;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>Phase 3 (N4): the public page shows the page name and hosts a chrome-less editor.</summary>
public sealed class TmNotionPublicPageTests : LocalizationTestBase
{
    private static readonly Guid PageId = TmNotionEditorShellTests.PageId;

    public TmNotionPublicPageTests()
    {
        Services.AddSingleton<ITmNotificationService>(new InMemoryNotificationStore());
        Services.AddSingleton<CommentNotificationOrchestrator>();
        Services.AddSingleton<NavigationManager>(new PublicNavigationManager());
    }

    [Fact]
    public void Header_ShowsPageName_AfterOnPageChanged()
    {
        var provider = new TmNotionEditorShellTests.Provider();
        var share = new PublicShare();
        var cut = Render<TmNotionPublicPage>(p => p
            .Add(c => c.Token, "public-token")
            .Add(c => c.PublicShareProvider, share)
            .Add(c => c.DataProvider, provider)
            .Add(c => c.AggregateProvider, new NotionAggregateTestAdapter(provider, provider)));

        cut.WaitForAssertion(() => cut.Find(".tm-npp__header h1").TextContent.Should().Contain("Shell page"));
        cut.FindAll(".tm-notification-bell, .tm-notion-topbar__shortcuts, .tm-notion-reading-toggle").Should().BeEmpty();
    }

    [Fact]
    public void ChromeFull_Default_StillRendersTheBell()
    {
        var provider = new TmNotionEditorShellTests.Provider();
        var cut = Render<TmNotionEditor>(p => p
            .Add(c => c.DataProvider, provider)
            .Add(c => c.AggregateProvider, new NotionAggregateTestAdapter(provider, provider))
            .Add(c => c.InitialPageId, PageId.ToString("D")));

        cut.WaitForAssertion(() => cut.Find(".tm-notion-page").Should().NotBeNull());
        cut.Find(".tm-notification-bell").Should().NotBeNull();
    }

    private sealed class PublicNavigationManager : NavigationManager
    {
        public PublicNavigationManager() => Initialize("https://localhost/", "https://localhost/p/public-token");
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }

    private sealed class PublicShare : INotionPublicShareProvider
    {
        public Task<PublicShareDto> CreateShareAsync(Guid pageId, PublicShareOptions options, CancellationToken cancellationToken = default)
            => Task.FromResult(Share());
        public Task RevokeAsync(Guid pageId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<PublicShareDto?> GetShareAsync(Guid pageId, CancellationToken cancellationToken = default)
            => Task.FromResult<PublicShareDto?>(Share());
        public Task<PublicShareDto?> ResolveByTokenAsync(string token, CancellationToken cancellationToken = default)
            => Task.FromResult<PublicShareDto?>(token == "public-token" ? Share() : null);

        private static PublicShareDto Share() => new()
        {
            PageId = PageId, Token = "public-token", IsEnabled = true
        };
    }

}
