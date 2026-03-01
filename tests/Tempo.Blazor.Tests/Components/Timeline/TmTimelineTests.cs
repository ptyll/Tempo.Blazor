using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Timeline;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Timeline;

file record TestEntry(
    string Id,
    string EntryType,
    string AuthorName,
    string? AuthorAvatarUrl,
    DateTimeOffset CreatedAt,
    string? HtmlContent,
    string? PlainContent,
    bool IsInternal,
    IReadOnlyDictionary<string, string>? Metadata = null) : ITimelineEntry;

/// <summary>TDD tests for TmTimeline.</summary>
public class TmTimelineTests : LocalizationTestBase
{
    private static List<ITimelineEntry> MakeEntries(bool includeInternal = true) =>
    [
        new TestEntry("e1", "comment",       "Alice", null, DateTimeOffset.Now.AddHours(-3), null, "Fixed the bug",    false),
        new TestEntry("e2", "status_change", "Bob",   null, DateTimeOffset.Now.AddHours(-2), null, "Changed to Active", false),
        new TestEntry("e3", "internal_note", "Carol", null, DateTimeOffset.Now.AddHours(-1), null, "Internal remark",   true),
    ];

    [Fact]
    public void TmTimeline_Renders_Timeline()
    {
        var cut = RenderComponent<TmTimeline>(p => p
            .Add(c => c.Entries, MakeEntries()));

        cut.Find(".tm-timeline").Should().NotBeNull();
    }

    [Fact]
    public void TmTimeline_Renders_All_Public_Entries()
    {
        var cut = RenderComponent<TmTimeline>(p => p
            .Add(c => c.Entries, MakeEntries())
            .Add(c => c.ShowInternal, false));

        // Only 2 non-internal entries
        cut.FindAll(".tm-timeline-entry").Count.Should().Be(2);
    }

    [Fact]
    public void TmTimeline_Internal_Hidden_By_Default()
    {
        var cut = RenderComponent<TmTimeline>(p => p
            .Add(c => c.Entries, MakeEntries()));

        cut.FindAll(".tm-timeline-entry").Count.Should().Be(2);
    }

    [Fact]
    public void TmTimeline_ShowInternal_Shows_All_Entries()
    {
        var cut = RenderComponent<TmTimeline>(p => p
            .Add(c => c.Entries, MakeEntries())
            .Add(c => c.ShowInternal, true));

        cut.FindAll(".tm-timeline-entry").Count.Should().Be(3);
    }

    [Fact]
    public void TmTimeline_Shows_Author_Name()
    {
        var cut = RenderComponent<TmTimeline>(p => p
            .Add(c => c.Entries, MakeEntries())
            .Add(c => c.ShowInternal, false));

        cut.FindAll(".tm-timeline-author")[0].TextContent
            .Should().Contain("Alice");
    }

    [Fact]
    public void TmTimeline_Shows_Entry_Content()
    {
        var cut = RenderComponent<TmTimeline>(p => p
            .Add(c => c.Entries, MakeEntries())
            .Add(c => c.ShowInternal, false));

        cut.FindAll(".tm-timeline-content")[0].TextContent
            .Should().Contain("Fixed the bug");
    }
}
