using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Resources;

namespace Tempo.Blazor.Tests.Localization;

/// <summary>
/// <see cref="ITmRelativeTimeFormatter"/> over the REAL embedded Czech / English / French strings:
/// "právě teď", "před 5 min", "včera v 16:20", an absolute date once it is a week away, the
/// future ("za 3 h", "zítra v 9:00", "za 5 dní") and the time zone that decides what "yesterday" is.
/// Deterministic: every case passes its own <c>now</c>, and the clock-driven cases use a
/// <see cref="FakeTimeProvider"/>.
/// </summary>
public class RelativeTimeFormatterTests
{
    // Fixed-offset zones: identical on Windows and Linux (no tz database lookup), and the reference dates
    // sit inside Prague summer time, so a fixed +02:00 is exactly what the real zone would say.
    private static readonly TimeSpan PragueOffset = TimeSpan.FromHours(2);
    private static readonly TimeZoneInfo Prague = TimeZoneInfo.CreateCustomTimeZone("Prague-test", PragueOffset, "Prague", "Prague");
    private static readonly TimeZoneInfo Tokyo = TimeZoneInfo.CreateCustomTimeZone("Tokyo-test", TimeSpan.FromHours(9), "Tokyo", "Tokyo");

    // 2026-10-12 is a Monday; Prague is on summer time (UTC+2) until 25 October.
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 14, 0, 0, PragueOffset);   // 12:00Z

    private static ITmRelativeTimeFormatter Formatter(string culture, TimeProvider? clock = null)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        return new TmRelativeTimeFormatter(new DefaultTmLocalizer(new JsonStringLocalizer<TmResources>()), clock);
    }

    private static string Cs(DateTimeOffset value, DateTimeOffset? now = null, TimeZoneInfo? zone = null)
        => Formatter("cs").Format(value, now ?? Now, zone ?? Prague);

    private static string En(DateTimeOffset value, DateTimeOffset? now = null, TimeZoneInfo? zone = null)
        => Formatter("en").Format(value, now ?? Now, zone ?? Prague);

    private static string Fr(DateTimeOffset value, DateTimeOffset? now = null, TimeZoneInfo? zone = null)
        => Formatter("fr").Format(value, now ?? Now, zone ?? Prague);

    private static DateTimeOffset Local(int day, int hour, int minute = 0, int month = 10)
        => new(2026, month, day, hour, minute, 0, PragueOffset);

    // ── just now ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Under_a_minute_is_just_now_in_every_language()
    {
        Cs(Now.AddSeconds(-10)).Should().Be("Právě teď");
        En(Now.AddSeconds(-59)).Should().Be("Just now");
        Fr(Now).Should().Be("À l'instant");
    }

    [Fact]
    public void A_moment_in_the_near_future_is_also_just_now_so_clock_skew_never_says_in_0_min()
    {
        Cs(Now.AddSeconds(30)).Should().Be("Právě teď");
        En(Now.AddSeconds(59)).Should().Be("Just now");
    }

    // ── minutes and hours ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Future_minutes_and_hours_round_up_so_4m59s_is_in_5_min()
    {
        // Truncation said "in 4 min" for 4m59s, which reads as already elapsed. A future distance rounds up.
        En(Now.AddMinutes(4).AddSeconds(59)).Should().Be("in 5 min");
        En(Now.AddHours(2).AddMinutes(1)).Should().Be("in 3 h");
        En(Now.AddMinutes(-4).AddSeconds(-59)).Should().Be("4 min ago", "the past keeps truncating");
    }

    [Fact]
    public void Minutes_ago()
    {
        Cs(Now.AddMinutes(-5)).Should().Be("před 5 min");
        En(Now.AddMinutes(-5)).Should().Be("5 min ago");
        Fr(Now.AddMinutes(-5)).Should().Be("il y a 5 min");
        Cs(Now.AddMinutes(-1)).Should().Be("před 1 min");
        Cs(Now.AddMinutes(-59)).Should().Be("před 59 min");
    }

    [Fact]
    public void Hours_ago_on_the_same_calendar_day()
    {
        Cs(Now.AddMinutes(-60)).Should().Be("před 1 h");
        Cs(Local(12, 10, 30)).Should().Be("před 3 h");
        En(Local(12, 10, 30)).Should().Be("3 h ago");
        Fr(Local(12, 1, 0)).Should().Be("il y a 13 h");
    }

    // ── yesterday ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Yesterday_names_the_clock_time()
    {
        Cs(Local(11, 16, 20)).Should().Be("včera v 16:20");
        // en has no Tm_Time_TimeFormat: the clock falls back to the culture's "t", so en-GB is not forced to 12h.
        var clock = Local(11, 16, 20).ToString("t", CultureInfo.GetCultureInfo("en"));
        En(Local(11, 16, 20)).Should().Be($"yesterday at {clock}");
        Fr(Local(11, 16, 20)).Should().Be("hier à 16:20");
    }

    [Fact]
    public void Yesterday_is_a_calendar_day_not_24_hours()
    {
        // 23 hours before now, but on the previous calendar day: "yesterday", not "23 h ago".
        Cs(Local(11, 15, 0)).Should().Be("včera v 15:00");
        // 14.5 hours before now, still the previous calendar day.
        Cs(Local(11, 23, 30)).Should().Be("včera v 23:30");
        // 13 hours before now, the SAME calendar day.
        Cs(Local(12, 1, 0)).Should().Be("před 13 h");
    }

    // ── days and older dates ────────────────────────────────────────────────────────────────

    [Fact]
    public void Two_to_six_days_ago_count_the_days_in_the_right_grammatical_form()
    {
        Cs(Local(10, 9)).Should().Be("před 2 dny");
        Cs(Local(8, 9)).Should().Be("před 4 dny");
        Cs(Local(6, 9)).Should().Be("před 6 dny");
        En(Local(10, 9)).Should().Be("2 days ago");
        Fr(Local(10, 9)).Should().Be("il y a 2 jours");
    }

    [Fact]
    public void A_week_or_more_is_the_absolute_date_in_the_culture_format()
    {
        Cs(Local(5, 9)).Should().Be("5. 10. 2026");
        En(Local(5, 9)).Should().Be("Oct 5, 2026");
        Fr(Local(5, 9)).Should().StartWith("5 oct").And.EndWith("2026");
        Cs(new DateTimeOffset(2025, 1, 3, 8, 0, 0, TimeSpan.FromHours(1))).Should().Be("3. 1. 2025");
    }

    // ── future ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Future_minutes_and_hours()
    {
        Cs(Now.AddMinutes(5)).Should().Be("za 5 min");
        En(Now.AddMinutes(5)).Should().Be("in 5 min");
        Fr(Now.AddMinutes(5)).Should().Be("dans 5 min");
        Cs(Local(12, 17, 0)).Should().Be("za 3 h");
        En(Local(12, 17, 0)).Should().Be("in 3 h");
    }

    [Fact]
    public void Tomorrow_names_the_clock_time()
    {
        Cs(Local(13, 9, 0)).Should().Be("zítra v 9:00");
        En(Local(13, 9, 0)).Should().Be($"tomorrow at {Local(13, 9, 0).ToString("t", CultureInfo.GetCultureInfo("en"))}");
        Fr(Local(13, 9, 0)).Should().Be("demain à 09:00");
    }

    [Fact]
    public void Future_days_use_the_plural_form_of_the_language()
    {
        Cs(Local(14, 9)).Should().Be("za 2 dny");
        Cs(Local(15, 9)).Should().Be("za 3 dny");
        Cs(Local(17, 9)).Should().Be("za 5 dní");
        En(Local(17, 9)).Should().Be("in 5 days");
        Fr(Local(17, 9)).Should().Be("dans 5 jours");
    }

    [Fact]
    public void A_future_date_a_week_or_more_away_is_the_absolute_date()
    {
        Cs(Local(19, 9)).Should().Be("19. 10. 2026");
        En(Local(19, 9)).Should().Be("Oct 19, 2026");
    }

    // ── time zone ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_time_zone_decides_what_yesterday_means_and_which_clock_time_is_shown()
    {
        var now = new DateTimeOffset(2026, 10, 12, 22, 30, 0, TimeSpan.Zero);
        var value = new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);    // 10.5 hours earlier

        // In UTC both are on 12 October: hours ago. In Tokyo (UTC+9) now is 13 Oct 07:30 and the
        // value is 12 Oct 21:00: yesterday at 21:00.
        Cs(value, now, TimeZoneInfo.Utc).Should().Be("před 10 h");
        Cs(value, now, Tokyo).Should().Be("včera v 21:00");
    }

    [Fact]
    public void The_offset_the_value_carries_does_not_matter_only_the_instant()
    {
        var sameInstantTokyo = Local(11, 16, 20).ToOffset(TimeSpan.FromHours(9));
        Cs(sameInstantTokyo).Should().Be("včera v 16:20");
    }

    // ── the clock ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Without_an_explicit_now_the_injected_TimeProvider_is_the_clock_and_its_zone_the_default()
    {
        var clock = new FakeTimeProvider(Now);
        clock.SetLocalTimeZone(Prague);
        var formatter = Formatter("cs", clock);

        formatter.Format(Now.AddMinutes(-7)).Should().Be("před 7 min");
        formatter.Format(Local(11, 16, 20)).Should().Be("včera v 16:20");

        clock.Advance(TimeSpan.FromHours(2));
        formatter.Format(Now.AddMinutes(-7)).Should().Be("před 2 h", "two hours later the same instant is hours old");
    }

    [Fact]
    public void Without_a_TimeProvider_the_system_clock_is_used()
    {
        var formatter = Formatter("en");
        formatter.Format(DateTimeOffset.UtcNow.AddSeconds(-5)).Should().Be("Just now");
    }

    [Fact]
    public void Absolute_uses_the_formatter_clock_zone_and_the_localizer_culture_not_the_servers()
    {
        // K2: the default interface member formats the title with TimeZoneInfo.Local and the ambient
        // culture. A Tokyo circuit on a CEST server then says "yesterday at 23:20" while the tooltip
        // shows 16:20. The zones and cultures are fixed here, so the assertion does not depend on the
        // machine's own zone.
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 15, 14, 20, 0, TimeSpan.Zero));
        clock.SetLocalTimeZone(Tokyo);
        var ambient = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentCulture = ambient;
        CultureInfo.CurrentUICulture = ambient;
        var circuit = CultureInfo.GetCultureInfo("cs-CZ");

        var localizer = new MockTmLocalizer(new Dictionary<string, string>
        {
            ["Tm_Time_YesterdayAt"] = "yesterday at {0}",
            ["Tm_Time_TimeFormat"] = "HH:mm",
        }, circuit);
        var formatter = new TmRelativeTimeFormatter(localizer, clock);
        var value = new DateTimeOffset(2026, 6, 14, 14, 20, 0, TimeSpan.Zero); // Tokyo 23:20, CEST 16:20
        var serverZone = TimeZoneInfo.CreateCustomTimeZone("Server-cest", TimeSpan.FromHours(2), "Server", "Server");

        formatter.Format(value).Should().Be("yesterday at 23:20");
        // Through the interface: without the override this is the default member.
        var title = ((ITmRelativeTimeFormatter)formatter).Absolute(value);

        title.Should().Be("14.06.2026 23:20", "the tooltip uses the clock's zone and the localizer's culture");
        title.Should().NotBe(TimeZoneInfo.ConvertTime(value, serverZone).ToString("g", ambient),
            "16:20 in the ambient en-US culture is what the server's zone would have printed");
    }

    [Fact]
    public void The_localizer_culture_not_the_ambient_one_formats_the_clock_time()
    {
        // A Czech localizer served while the thread culture is English: still a 24-hour clock time.
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
        var loc = new MockTmLocalizer(new Dictionary<string, string>
        {
            ["Tm_Time_YesterdayAt"] = "včera v {0}",
            ["Tm_Time_TimeFormat"] = "H:mm",
        }, CultureInfo.GetCultureInfo("cs"));

        new TmRelativeTimeFormatter(loc).Format(Local(11, 16, 20), Now, Prague).Should().Be("včera v 16:20");
    }

    [Fact]
    public void A_host_localizer_that_lacks_the_time_keys_never_throws()
    {
        var loc = new MockTmLocalizer(new Dictionary<string, string>());
        var formatter = new TmRelativeTimeFormatter(loc);

        var act = () => formatter.Format(Now.AddMinutes(-5), Now, Prague);
        act.Should().NotThrow();
    }
}