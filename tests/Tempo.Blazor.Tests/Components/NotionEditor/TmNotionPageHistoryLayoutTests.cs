using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.Components.NotionEditor.UI;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>N9: history layout — Escape, the mobile drill-in, and the footer gates.</summary>
public sealed class TmNotionPageHistoryLayoutTests : LocalizationTestBase
{
    [Fact]
    public async Task Escape_ClosesThePanel()
    {
        var closed = 0;
        var host = RenderHistory(onClosed: () => closed++);

        host.WaitForAssertion(() => host.FindAll(".tm-nph__version-item").Should().HaveCount(2));
        await host.Find(".tm-modal-close").ClickAsync(new MouseEventArgs());

        closed.Should().Be(1, "Escape takes the modal's OnClose path exactly once");
    }

    [Fact]
    public async Task Mobile_ListThenPreviewThenBack()
    {
        var host = RenderHistory(TmLayoutMode.Mobile);

        host.WaitForAssertion(() => host.FindAll(".tm-nph__version-item").Should().HaveCount(2));
        host.Find(".tm-nph").GetAttribute("data-mobile-preview").Should().Be("false");

        await host.FindAll(".tm-nph__version-item")[1].ClickAsync(new MouseEventArgs());

        host.WaitForAssertion(() =>
        {
            host.Find(".tm-nph").GetAttribute("data-mobile-preview").Should().Be("true");
            host.Find("[data-testid='notion-history-back']").Should().NotBeNull();
        });

        await host.Find("[data-testid='notion-history-back']").ClickAsync(new MouseEventArgs());

        host.WaitForAssertion(() =>
            host.Find(".tm-nph").GetAttribute("data-mobile-preview").Should().Be("false"));
    }

    [Fact]
    public async Task CompareSelected_StaysDisabledUntilTwoArePicked()
    {
        var host = RenderHistory();
        host.WaitForAssertion(() => host.FindAll(".tm-nph__version-item").Should().HaveCount(2));

        host.Find("[data-testid='notion-history-compare-selected']").HasAttribute("disabled").Should().BeTrue();

        await host.FindAll("[data-testid='notion-history-pick']")[1]
            .ChangeAsync(new ChangeEventArgs { Value = true });
        host.Find("[data-testid='notion-history-compare-selected']").HasAttribute("disabled").Should().BeTrue();

        await host.FindAll("[data-testid='notion-history-pick']")[0]
            .ChangeAsync(new ChangeEventArgs { Value = true });
        host.Find("[data-testid='notion-history-compare-selected']").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public async Task Restore_IsDisabledOnTheCurrentVersion()
    {
        var host = RenderHistory();
        host.WaitForAssertion(() => host.FindAll(".tm-nph__version-item").Should().HaveCount(2));

        await host.FindAll(".tm-nph__version-item")[0].ClickAsync(new MouseEventArgs());
        host.Find("[data-testid='notion-history-restore']").HasAttribute("disabled").Should().BeTrue();

        await host.FindAll(".tm-nph__version-item")[1].ClickAsync(new MouseEventArgs());
        host.Find("[data-testid='notion-history-restore']").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public async Task Mobile_HasNoSideBySideToggle()
    {
        var host = RenderHistory(TmLayoutMode.Mobile);
        host.WaitForAssertion(() => host.FindAll(".tm-nph__version-item").Should().HaveCount(2));

        await host.FindAll("[data-testid='notion-history-pick']")[0].ChangeAsync(new ChangeEventArgs { Value = true });
        await host.FindAll("[data-testid='notion-history-pick']")[1].ChangeAsync(new ChangeEventArgs { Value = true });
        await host.Find("[data-testid='notion-history-compare-selected']").ClickAsync(new MouseEventArgs());

        host.WaitForAssertion(() => host.Find("[data-testid='notion-diff-viewer']").Should().NotBeNull());
        host.FindAll("[data-testid='notion-diff-mode-side-by-side']").Should().BeEmpty();
    }

    private IRenderedComponent<CascadingValue<NotionEditorContext>> RenderHistory(
        TmLayoutMode layout = TmLayoutMode.Desktop, Action? onClosed = null)
    {
        var context = new NotionEditorContext { HistoryProvider = new TwoVersionHistory() };
        return Render<CascadingValue<NotionEditorContext>>(parameters => parameters
            .Add(component => component.Value, context)
            .AddChildContent<CascadingValue<TmLayoutContext>>(layoutCascade => layoutCascade
                .Add(cascade => cascade.Name, TmLayoutScopes.Viewport)
                .Add(cascade => cascade.Value, new TmLayoutContext(TmLayoutMode.Auto, layout))
                .Add(cascade => cascade.IsFixed, true)
                .AddChildContent<TmNotionPageHistory>(child =>
                {
                    child.Add(component => component.Visible, true);
                    child.Add(component => component.PageId, TwoVersionHistory.PageId.ToString("D"));
                    if (onClosed is not null)
                        child.Add(component => component.OnClosed, EventCallback.Factory.Create(this, onClosed));
                })));
    }

    private sealed class TwoVersionHistory : INotionVersionProvider
    {
        public static readonly Guid PageId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        private readonly List<NotionPageVersionDto> _versions =
        [
            Version("cf230000-0000-0000-0000-000000000201", 0),
            Version("cf230000-0000-0000-0000-000000000200", 2),
        ];

        public Task<PagedResult<IPageVersion>> GetVersionsAsync(string pageId, int page, int pageSize)
            => Task.FromResult(new PagedResult<IPageVersion>
            {
                Items = _versions.Cast<IPageVersion>().ToList(),
                TotalCount = _versions.Count,
                Page = page,
                PageSize = pageSize,
            });

        public Task<IPageVersion> GetVersionAsync(string pageId, string versionId)
            => Task.FromResult<IPageVersion>(_versions.Single(version => version.Id.ToString("D") == versionId));

        public Task RestoreVersionAsync(string pageId, string versionId) => Task.CompletedTask;

        public Task<IReadOnlyList<BlockDiff>> GetDiffAsync(string pageId, string versionIdA, string versionIdB)
            => Task.FromResult<IReadOnlyList<BlockDiff>>([]);

        public Task<IEnumerable<BlockDiff>> CompareVersionsAsync(string versionId1, string versionId2)
            => Task.FromResult(Enumerable.Empty<BlockDiff>());

        private static NotionPageVersionDto Version(string id, int hoursAgo) => new()
        {
            Id = Guid.Parse(id),
            PageId = PageId,
            EditedAt = new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc).AddHours(-hoursAgo),
            EditedByUserId = "tester",
            EditedByDisplayName = "Test User",
            ChangeDescription = hoursAgo == 0 ? "Current" : "Earlier",
            BlocksSnapshot = [],
        };
    }
}
