using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Timeline;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Tests.Localization;

/// <summary>
/// B19: the private relative-time copies are gone — TmTimeline asks <see cref="ITmRelativeTimeFormatter"/>.
/// </summary>
public class RelativeTimeAdoptionTests : LocalizationTestBase
{
    private sealed class StubFormatter : ITmRelativeTimeFormatter
    {
        public List<DateTimeOffset> Seen { get; } = [];

        public string Format(DateTimeOffset value, DateTimeOffset? now = null, TimeZoneInfo? timeZone = null)
        {
            Seen.Add(value);
            return "STUB-TIME";
        }
    }

    private sealed record Entry(
        string Id, string EntryType, string AuthorName, string? AuthorAvatarUrl, DateTimeOffset CreatedAt,
        string? HtmlContent, string? PlainContent, bool IsInternal,
        IReadOnlyDictionary<string, string>? Metadata = null) : ITimelineEntry;

    private static Entry At(DateTimeOffset when) =>
        new("e1", "comment", "Alice", null, when, null, "Text", false);

    [Fact]
    public void TmTimeline_renders_the_timestamp_through_the_registered_formatter()
    {
        var stub = new StubFormatter();
        Services.AddSingleton<ITmRelativeTimeFormatter>(stub);
        var created = DateTimeOffset.UtcNow.AddHours(-30);

        var cut = Render<TmTimeline>(p => p.Add(c => c.Entries, new[] { At(created) }));

        cut.Find(".tm-timeline-timestamp").TextContent.Should().Be("STUB-TIME");
        stub.Seen.Should().Contain(created);
    }

    [Fact]
    public void TmTimeline_without_a_registered_formatter_still_renders_localized_relative_time()
    {
        var cut = Render<TmTimeline>(p => p.Add(c => c.Entries, new[] { At(DateTimeOffset.UtcNow.AddMinutes(-5)) }));

        cut.Find(".tm-timeline-timestamp").TextContent.Should().Be("5 min ago");
    }

    [Fact]
    public void TmTimeline_czech_says_prave_ted_and_yesterday_in_czech()
    {
        UseCzechLocalization();

        var now = Render<TmTimeline>(p => p.Add(c => c.Entries, new[] { At(DateTimeOffset.UtcNow.AddSeconds(-5)) }));
        now.Find(".tm-timeline-timestamp").TextContent.Should().Be("Právě teď");

        var old = Render<TmTimeline>(p => p.Add(c => c.Entries, new[] { At(DateTimeOffset.UtcNow.AddDays(-30)) }));
        old.Find(".tm-timeline-timestamp").TextContent.Should().MatchRegex(@"^\d{1,2}\. \d{1,2}\. \d{4}$");
    }

    [Fact]
    public void TmTimeline_uses_a_TimeProvider_when_one_is_registered()
    {
        var fake = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero));
        Services.AddSingleton<TimeProvider>(fake);
        Services.AddSingleton<ITmRelativeTimeFormatter>(sp => new TmRelativeTimeFormatter(sp.GetRequiredService<ITmLocalizer>(), fake));

        var cut = Render<TmTimeline>(p => p.Add(c => c.Entries, new[] { At(fake.GetUtcNow().AddMinutes(-42)) }));

        cut.Find(".tm-timeline-timestamp").TextContent.Should().Be("42 min ago");
    }
}