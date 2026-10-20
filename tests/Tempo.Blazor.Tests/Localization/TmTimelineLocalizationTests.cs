using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.Components.Timeline;
using Tempo.Blazor.Interfaces;

namespace Tempo.Blazor.Tests.Localization;

/// <summary>
/// RED tests – TmTimeline must use ITmLocalizer for "Internal" badge and FormatTime strings.
/// The relative-time stamps render on a FIXED clock (FakeTimeProvider): the hours format is
/// calendar-day gated, so a live <c>DateTimeOffset.Now</c> flaked when the run crossed midnight
/// (2 h ago became "yesterday at …" instead of "před 2 h").
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

        var cut = Render<TmTimeline>(p => p
            .Add(c => c.Entries, new[] { MakeEntry(isInternal: true) })
            .Add(c => c.ShowInternal, true));

        cut.Find(".tm-timeline-internal-badge").TextContent
            .Should().Be("Interní");
    }

    [Fact]
    public void TmTimeline_InternalBadge_English_ShowsEnglishText()
    {
        var cut = Render<TmTimeline>(p => p
            .Add(c => c.Entries, new[] { MakeEntry(isInternal: true) })
            .Add(c => c.ShowInternal, true));

        cut.Find(".tm-timeline-internal-badge").TextContent
            .Should().Be("Internal");
    }

    [Fact]
    public void TmTimeline_Time_JustNow_UsesLocalizer()
    {
        UseCzechLocalization();

        var cut = Render<TmTimeline>(p => p
            .Add(c => c.Entries, new[] { MakeEntry(timestamp: DateTimeOffset.Now.AddSeconds(-10)) })
            .Add(c => c.ShowInternal, true));

        cut.Find(".tm-timeline-timestamp").TextContent
            .Should().Be("Právě teď");
    }

    [Fact]
    public void TmTimeline_Time_MinutesAgo_UsesLocalizer()
    {
        UseCzechLocalization();

        var cut = Render<TmTimeline>(p => p
            .Add(c => c.Entries, new[] { MakeEntry(timestamp: DateTimeOffset.Now.AddMinutes(-5)) })
            .Add(c => c.ShowInternal, true));

        cut.Find(".tm-timeline-timestamp").TextContent
            .Should().Contain("min");
    }

    [Fact]
    public void TmTimeline_Time_HoursAgo_UsesLocalizer_OnAFixedClock()
    {
        // The hours format is gated on the CALENDAR day of the timestamp, so a live clock made
        // this test flake when a run crossed midnight (2 h ago read as the yesterday format).
        // The clock is a FakeTimeProvider pinned to a fixed UTC noon and the entry is derived
        // from it — same day, hours path, always. m5 (review): the local zone is set through the
        // provider's own API instead of an override subclass.
        UseCzechLocalization();
        var clock = new FakeTimeProvider(new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero));
        clock.SetLocalTimeZone(TimeZoneInfo.Utc);
        Services.AddSingleton<TimeProvider>(clock);

        var cut = Render<TmTimeline>(p => p
            .Add(c => c.Entries, new[] { MakeEntry(timestamp: clock.GetUtcNow().AddHours(-2)) })
            .Add(c => c.ShowInternal, true));

        cut.Find(".tm-timeline-timestamp").TextContent
            .Should().Be("před 2 h");
    }

    [Fact]
    public void TmTimeline_Time_HoursAgoAcrossMidnight_FormatsAsYesterday_OnAFixedClock()
    {
        // The midnight case itself, pinned: 2 h before 00:30 is the neighbouring calendar day,
        // so the stamp is the yesterday format — deterministically, never "před 2 h". The local
        // zone is pinned through SetLocalTimeZone, not the machine's (m5).
        UseCzechLocalization();
        var clock = new FakeTimeProvider(new DateTimeOffset(2024, 6, 15, 0, 30, 0, TimeSpan.Zero));
        clock.SetLocalTimeZone(TimeZoneInfo.Utc);
        Services.AddSingleton<TimeProvider>(clock);

        var cut = Render<TmTimeline>(p => p
            .Add(c => c.Entries, new[] { MakeEntry(timestamp: clock.GetUtcNow().AddHours(-2)) })
            .Add(c => c.ShowInternal, true));

        cut.Find(".tm-timeline-timestamp").TextContent
            .Should().Be("včera v 22:30");
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
