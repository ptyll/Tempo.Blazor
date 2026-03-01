using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Activity;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Activity;

// Uses the same file-scoped record types declared in the other test files via
// separate namespaces; we define local ones here.
file record LogEntry(
    string Id,
    string EntryType,
    string AuthorName,
    string? AuthorAvatarUrl,
    DateTimeOffset CreatedAt,
    string? HtmlContent,
    string? PlainContent,
    bool IsInternal = false,
    IReadOnlyDictionary<string, string>? Metadata = null) : ITimelineEntry;

file record LogComment(
    string Id,
    string AuthorName,
    string? AuthorAvatarUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    string HtmlContent,
    bool CanEdit,
    bool CanDelete) : ICommentEntry;

public class TmActivityLogTests : LocalizationTestBase
{
    private static IReadOnlyList<ITimelineEntry> SampleEntries() =>
    [
        new LogEntry("e1", "comment", "Alice", null, DateTimeOffset.Now.AddHours(-1), null, "Hello"),
    ];

    private static IReadOnlyList<ICommentEntry> SampleComments() =>
    [
        new LogComment("c1", "Bob", null, DateTimeOffset.Now.AddMinutes(-30), null, "<p>Hi</p>", false, false),
    ];

    [Fact]
    public void ActivityLog_RendersTimelineTab()
    {
        var cut = RenderComponent<TmActivityLog>(p => p
            .Add(c => c.TimelineEntries, SampleEntries()));

        cut.FindAll(".tm-activity-tab").Should().Contain(t => t.TextContent.Contains("Timeline"));
    }

    [Fact]
    public void ActivityLog_RendersCommentsTab()
    {
        var cut = RenderComponent<TmActivityLog>(p => p
            .Add(c => c.Comments, SampleComments()));

        cut.FindAll(".tm-activity-tab").Should().Contain(t => t.TextContent.Contains("Comments") || t.TextContent.Contains("Komentáře"));
    }

    [Fact]
    public void ActivityLog_RendersAttachmentsTab()
    {
        var cut = RenderComponent<TmActivityLog>();

        cut.FindAll(".tm-activity-tab").Should().Contain(t =>
            t.TextContent.Contains("Attachments") || t.TextContent.Contains("Přílohy"));
    }

    [Fact]
    public void ActivityLog_DefaultTab_IsTimeline()
    {
        var cut = RenderComponent<TmActivityLog>(p => p
            .Add(c => c.TimelineEntries, SampleEntries()));

        // Timeline content visible by default
        cut.FindAll(".tm-activity-content .tm-timeline").Should().NotBeEmpty();
    }

    [Fact]
    public async Task ActivityLog_RefreshAsync_RefreshesAllTabs()
    {
        var cut = RenderComponent<TmActivityLog>(p => p
            .Add(c => c.TimelineEntries, SampleEntries())
            .Add(c => c.Comments, SampleComments()));

        // Should not throw
        await cut.InvokeAsync(() => cut.Instance.RefreshAsync());

        cut.FindAll(".tm-activity-tab").Count.Should().Be(3);
    }

    [Fact]
    public void ActivityLog_HideTab_HidesCorrectTab()
    {
        var cut = RenderComponent<TmActivityLog>(p => p
            .Add(c => c.ShowComments, false));

        var tabs = cut.FindAll(".tm-activity-tab");
        tabs.Should().NotContain(t =>
            t.TextContent.Contains("Comments") || t.TextContent.Contains("Komentáře"));
    }
}
