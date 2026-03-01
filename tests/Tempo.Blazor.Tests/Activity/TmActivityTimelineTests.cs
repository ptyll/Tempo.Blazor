using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Activity;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Activity;

file record TimelineEntry(
    string Id,
    string EntryType,
    string AuthorName,
    string? AuthorAvatarUrl,
    DateTimeOffset CreatedAt,
    string? HtmlContent,
    string? PlainContent,
    bool IsInternal = false,
    IReadOnlyDictionary<string, string>? Metadata = null) : ITimelineEntry;

public class TmActivityTimelineTests : LocalizationTestBase
{
    private static List<ITimelineEntry> TwoEntries() =>
    [
        new TimelineEntry("e1", "comment",       "Alice", null, DateTimeOffset.Now.AddHours(-2), null, "First comment"),
        new TimelineEntry("e2", "status_change", "Bob",   null, DateTimeOffset.Now.AddHours(-1), null, "Status changed"),
    ];

    [Fact]
    public void Timeline_RendersEntries_InReverseChronologicalOrder()
    {
        var cut = RenderComponent<TmActivityTimeline>(p => p
            .Add(c => c.Entries, TwoEntries()));

        var items = cut.FindAll(".tm-timeline-item");
        items.Count.Should().Be(2);
        // Newer (Bob, -1 h) must come first
        items[0].TextContent.Should().Contain("Bob");
        items[1].TextContent.Should().Contain("Alice");
    }

    [Fact]
    public void Timeline_EntryType_Comment_RendersCommentClass()
    {
        var entries = new[] { new TimelineEntry("e1", "comment", "Alice", null, DateTimeOffset.Now.AddMinutes(-5), null, "Hello") };
        var cut = RenderComponent<TmActivityTimeline>(p => p.Add(c => c.Entries, entries));

        cut.Find(".tm-timeline-comment").Should().NotBeNull();
    }

    [Fact]
    public void Timeline_EntryType_StatusChange_RendersStatusClass()
    {
        var entries = new[] { new TimelineEntry("e1", "status_change", "Alice", null, DateTimeOffset.Now.AddMinutes(-5), null, "Active") };
        var cut = RenderComponent<TmActivityTimeline>(p => p.Add(c => c.Entries, entries));

        cut.Find(".tm-timeline-status_change").Should().NotBeNull();
    }

    [Fact]
    public void Timeline_HtmlContent_RendersAsMarkupString()
    {
        var entries = new[] { new TimelineEntry("e1", "comment", "Alice", null, DateTimeOffset.Now.AddMinutes(-1), "<strong>bold text</strong>", null) };
        var cut = RenderComponent<TmActivityTimeline>(p => p.Add(c => c.Entries, entries));

        cut.Find("strong").TextContent.Should().Be("bold text");
    }

    [Fact]
    public void Timeline_Empty_RendersEmptyState()
    {
        var cut = RenderComponent<TmActivityTimeline>(p => p
            .Add(c => c.Entries, Array.Empty<ITimelineEntry>()));

        cut.FindAll(".tm-timeline-item").Should().BeEmpty();
        cut.FindAll(".tm-empty-state, .tm-timeline-empty").Should().NotBeEmpty();
    }

    [Fact]
    public void Timeline_RelativeTime_UsesLocalizer()
    {
        var entries = new[] { new TimelineEntry("e1", "comment", "Alice", null, DateTimeOffset.Now.AddSeconds(-10), null, "Hi") };
        var cut = RenderComponent<TmActivityTimeline>(p => p.Add(c => c.Entries, entries));

        // FormatRelativeTime < 1 min → "Just now"
        cut.Find(".tm-timeline-time").TextContent.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Timeline_RefreshAsync_ReloadsEntries()
    {
        var entries = TwoEntries();
        var cut = RenderComponent<TmActivityTimeline>(p => p.Add(c => c.Entries, entries));

        cut.FindAll(".tm-timeline-item").Count.Should().Be(2);

        await cut.InvokeAsync(() => cut.Instance.RefreshAsync());

        cut.FindAll(".tm-timeline-item").Count.Should().Be(2);
    }

    [Fact]
    public void Timeline_AuthorAvatar_RendersWhenProvided()
    {
        var entries = new[] { new TimelineEntry("e1", "comment", "Alice", "https://example.com/avatar.png", DateTimeOffset.Now.AddMinutes(-1), null, "Hi") };
        var cut = RenderComponent<TmActivityTimeline>(p => p.Add(c => c.Entries, entries));

        cut.Find(".tm-timeline-avatar img").Should().NotBeNull();
    }

    [Fact]
    public void Timeline_AuthorAvatar_RendersInitialsWhenMissing()
    {
        var entries = new[] { new TimelineEntry("e1", "comment", "Alice Smith", null, DateTimeOffset.Now.AddMinutes(-1), null, "Hi") };
        var cut = RenderComponent<TmActivityTimeline>(p => p.Add(c => c.Entries, entries));

        // TmAvatar without Src renders initials span
        cut.Find(".tm-timeline-avatar .tm-avatar").Should().NotBeNull();
    }
}
