using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Timeline;
using Tempo.Blazor.Interfaces;

namespace Tempo.Blazor.Tests.Localization;

/// <summary>
/// RED tests – TmTimeline must use ITmLocalizer for "Internal" badge and FormatTime strings.
/// </summary>
public class TmTimelineLocalizationTests : LocalizationTestBase
{
    private static ITimelineEntry MakeEntry(
        bool isInternal = false,
        DateTimeOffset? timestamp = null) =>
        new TestEntry(
            Id: "e1",
            EntryType: "comment",
            AuthorName: "Alice",
            AuthorAvatarUrl: null,
            CreatedAt: timestamp ?? DateTimeOffset.Now.AddSeconds(-30),
            HtmlContent: null,
            PlainContent: "Test content",
            IsInternal: isInternal);

    [Fact]
    public void TmTimeline_InternalBadge_UsesLocalizer()
    {
        UseCzechLocalization();

        var cut = RenderComponent<TmTimeline>(p => p
            .Add(c => c.Entries, new[] { MakeEntry(isInternal: true) })
            .Add(c => c.ShowInternal, true));

        cut.Find(".tm-timeline-internal-badge").TextContent
            .Should().Be("Interní");
    }

    [Fact]
    public void TmTimeline_InternalBadge_English_ShowsEnglishText()
    {
        var cut = RenderComponent<TmTimeline>(p => p
            .Add(c => c.Entries, new[] { MakeEntry(isInternal: true) })
            .Add(c => c.ShowInternal, true));

        cut.Find(".tm-timeline-internal-badge").TextContent
            .Should().Be("Internal");
    }

    [Fact]
    public void TmTimeline_JustNow_UsesLocalizer()
    {
        UseCzechLocalization();

        var cut = RenderComponent<TmTimeline>(p => p
            .Add(c => c.Entries, new[] { MakeEntry(timestamp: DateTimeOffset.Now.AddSeconds(-10)) })
            .Add(c => c.ShowInternal, true));

        cut.Find(".tm-timeline-timestamp").TextContent
            .Should().Be("Právě teď");
    }

    [Fact]
    public void TmTimeline_MinutesAgo_UsesLocalizer()
    {
        UseCzechLocalization();

        var cut = RenderComponent<TmTimeline>(p => p
            .Add(c => c.Entries, new[] { MakeEntry(timestamp: DateTimeOffset.Now.AddMinutes(-5)) })
            .Add(c => c.ShowInternal, true));

        cut.Find(".tm-timeline-timestamp").TextContent
            .Should().Contain("min");
    }

    [Fact]
    public void TmTimeline_HoursAgo_UsesLocalizer()
    {
        UseCzechLocalization();

        var cut = RenderComponent<TmTimeline>(p => p
            .Add(c => c.Entries, new[] { MakeEntry(timestamp: DateTimeOffset.Now.AddHours(-2)) })
            .Add(c => c.ShowInternal, true));

        cut.Find(".tm-timeline-timestamp").TextContent
            .Should().Contain("h");
    }

    private record TestEntry(
        string Id,
        string EntryType,
        string AuthorName,
        string? AuthorAvatarUrl,
        DateTimeOffset CreatedAt,
        string? HtmlContent,
        string? PlainContent,
        bool IsInternal,
        IReadOnlyDictionary<string, string>? Metadata = null) : ITimelineEntry;
}
