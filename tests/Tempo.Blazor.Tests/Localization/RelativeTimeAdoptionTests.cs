using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.Activity;
using Tempo.Blazor.Components.Notifications;
using Tempo.Blazor.Components.Timeline;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Services;

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
    public void TmTimeline_renders_the_stamp_in_a_time_element_with_the_iso_instant()
    {
        var created = new DateTimeOffset(2026, 10, 11, 14, 20, 0, TimeSpan.Zero);
        var cut = Render<TmTimeline>(p => p.Add(c => c.Entries, new[] { At(created) }));

        var time = cut.Find("time.tm-timeline-timestamp");
        time.GetAttribute("datetime").Should().Be(created.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        time.GetAttribute("title").Should().NotBeNullOrEmpty("the title is the absolute time in the formatter's zone");
    }

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

    [Theory]
    [InlineData("fi-FI")]
    [InlineData("fa-IR")]
    public void TimeStamp_datetime_is_invariant_iso_under_a_culture_that_rewrites_numbers(string cultureName)
    {
        // m2: DateTime.ToString without a culture uses the ambient one. fi-FI writes 14.20.00 and
        // fa-IR a non-Gregorian year, both invalid in <time datetime>.
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;

            var created = new DateTimeOffset(2026, 6, 14, 14, 20, 0, TimeSpan.Zero);
            const string expected = "2026-06-14T14:20:00Z";

            Render<TmTimeline>(p => p.Add(c => c.Entries, new[] { At(created) }))
                .Find("time.tm-timeline-timestamp").GetAttribute("datetime").Should().Be(expected);

            Render<TmActivityTimeline>(p => p.Add(c => c.Entries, new ITimelineEntry[]
            {
                new ActivityEntry("e1", "comment", "Alice", null, created, null, "Text"),
            })).Find("time.tm-timeline-time").GetAttribute("datetime").Should().Be(expected);

            Render<TmActivityComments>(p => p.Add(c => c.Comments, new[]
            {
                new TmCommentEntry
                {
                    Id = "c1", ThreadId = "c1", Author = new TmUserRef { DisplayName = "Alice" },
                    CreatedAt = created, Body = "<p>Text</p>", BodyFormat = TmCommentBodyFormat.Html,
                },
            })).Find("time.tm-comment-time").GetAttribute("datetime").Should().Be(expected);

            BellDatetime(created).Should().Be(expected);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    private static string? BellDatetime(DateTimeOffset created)
    {
        // Its own context: bUnit rejects registrations after the first render.
        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<ITmLocalizer>(new MockTmLocalizer(new Dictionary<string, string>
        {
            ["Tm_Time_JustNow"] = "Just now",
        }));
        var store = new InMemoryNotificationStore();
        ctx.Services.AddSingleton<ITmNotificationService>(store);
        ctx.Services.AddSingleton<Microsoft.AspNetCore.Components.NavigationManager>(new TestNav());
        store.PublishAsync(new TmNotification
        {
            Type = TmNotificationTypes.Mention,
            RecipientUserId = "demo",
            Actor = new TmUserRef { Id = "alice", DisplayName = "Alice" },
            Title = "Test",
            CreatedAt = created,
        }).Wait();

        var bell = ctx.Render<TmNotificationBell>();
        bell.Find("button").Click();
        return bell.Find("time.tm-notification-bell__time").GetAttribute("datetime");
    }

    private sealed record ActivityEntry(
        string Id, string EntryType, string AuthorName, string? AuthorAvatarUrl, DateTimeOffset CreatedAt,
        string? HtmlContent, string? PlainContent, bool IsInternal = false,
        IReadOnlyDictionary<string, string>? Metadata = null) : ITimelineEntry;

    private sealed class TestNav : Microsoft.AspNetCore.Components.NavigationManager
    {
        public TestNav() => Initialize("https://localhost/", "https://localhost/");

        protected override void NavigateToCore(string uri, bool forceLoad) { }
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