using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Timeline;
using Tempo.Blazor.Configuration;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Tests.Localization;

/// <summary>
/// F7 review J1: <see cref="ITmRelativeTimeFormatter"/> must be scoped. Registered as a singleton it
/// captures the first <see cref="ITmLocalizer"/>, so a scoped localizer either throws under
/// <c>ValidateScopes</c> or every circuit shares the first language. A scoped <see cref="TimeProvider"/>
/// must be resolved per scope too, so a host can give each circuit the browser's time zone.
/// </summary>
public class RelativeTimeRegistrationTests
{
    /// <summary>
    /// Scoped stand-in for a per-circuit localizer. The stamp is set by the test before the
    /// formatter is resolved, so two scopes can speak two languages from one registration.
    /// </summary>
    private sealed class ScopedCultureLocalizer : ITmLocalizer
    {
        public string Stamp { get; set; } = "unset";

        public CultureInfo Culture { get; set; } = CultureInfo.GetCultureInfo("en");

        public string this[string key] => key switch
        {
            "Tm_Time_JustNow" => Stamp,
            "Tm_Time_YesterdayAt" => "yesterday at {0}",
            "Tm_Time_TomorrowAt" => "tomorrow at {0}",
            "Tm_Time_HoursAgo" => "{0} h ago",
            "Tm_Time_InHours" => "in {0} h",
            "Tm_Time_TimeFormat" => "h:mm tt",
            _ => key,
        };

        public string this[string key, params object[] arguments]
        {
            get
            {
                var template = this[key];
                return arguments is { Length: > 0 } ? string.Format(Culture, template, arguments) : template;
            }
        }
    }

    private sealed class ZoneClock : TimeProvider
    {
        public ZoneClock(TimeZoneInfo zone) => LocalTimeZone = zone;

        public override TimeZoneInfo LocalTimeZone { get; }
    }

    /// <summary>Scoped bag: the test fills the clock after the scope exists, before the formatter resolves it.</summary>
    private sealed class ZoneClockHolder
    {
        public TimeProvider Clock { get; set; } = TimeProvider.System;
    }

    private sealed record Entry(
        string Id, string EntryType, string AuthorName, string? AuthorAvatarUrl, DateTimeOffset CreatedAt,
        string? HtmlContent, string? PlainContent, bool IsInternal,
        IReadOnlyDictionary<string, string>? Metadata = null) : ITimelineEntry;

    private static Entry At(DateTimeOffset when) =>
        new("e1", "comment", "Alice", null, when, null, "Text", false);

    private static ServiceProvider BuildValidated(Action<ServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddTempoBlazor();
        configure(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    [Fact]
    public void TwoScopes_WithDifferentCultures_EachRenderTheirOwnLanguage()
    {
        using var provider = BuildValidated(services =>
            services.AddScoped<ITmLocalizer, ScopedCultureLocalizer>());

        using var english = provider.CreateScope();
        using var czech = provider.CreateScope();
        var enLoc = (ScopedCultureLocalizer)english.ServiceProvider.GetRequiredService<ITmLocalizer>();
        var csLoc = (ScopedCultureLocalizer)czech.ServiceProvider.GetRequiredService<ITmLocalizer>();
        enLoc.Stamp = "EN-STAMP";
        enLoc.Culture = CultureInfo.GetCultureInfo("en");
        csLoc.Stamp = "CS-STAMP";
        csLoc.Culture = CultureInfo.GetCultureInfo("cs");

        var enText = english.ServiceProvider.GetRequiredService<ITmRelativeTimeFormatter>()
            .Format(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var csText = czech.ServiceProvider.GetRequiredService<ITmRelativeTimeFormatter>()
            .Format(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        enText.Should().Be("EN-STAMP");
        csText.Should().Be("CS-STAMP", "a scoped localizer must not be captured once by a singleton formatter");
        english.ServiceProvider.GetRequiredService<ITmRelativeTimeFormatter>()
            .Should().NotBeSameAs(czech.ServiceProvider.GetRequiredService<ITmRelativeTimeFormatter>(),
                "the formatter is resolved per scope, with that scope's localizer and clock");
    }

    [Fact]
    public void ScopedTimeProvider_ChangesYesterdayAndTheDayBoundary()
    {
        // 2026-06-15 03:30 UTC is still 14 June in New York (EDT, UTC-4) and already 15 June in Prague (CEST, UTC+2).
        var instant = new DateTimeOffset(2026, 6, 15, 3, 30, 0, TimeSpan.Zero);
        var eightHoursEarlier = instant.AddHours(-8); // 2026-06-14 19:30 UTC

        using var provider = BuildValidated(services =>
        {
            services.AddSingleton<ITmLocalizer>(new ScopedCultureLocalizer
            {
                Stamp = "now",
                Culture = CultureInfo.GetCultureInfo("en-US"),
            });
            services.AddScoped<ZoneClockHolder>();
            services.AddScoped<TimeProvider>(sp => sp.GetRequiredService<ZoneClockHolder>().Clock);
        });

        using var prague = provider.CreateScope();
        using var newYork = provider.CreateScope();
        prague.ServiceProvider.GetRequiredService<ZoneClockHolder>().Clock =
            new ZoneClock(TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague"));
        newYork.ServiceProvider.GetRequiredService<ZoneClockHolder>().Clock =
            new ZoneClock(TimeZoneInfo.FindSystemTimeZoneById("America/New_York"));

        var pragueText = prague.ServiceProvider.GetRequiredService<ITmRelativeTimeFormatter>()
            .Format(eightHoursEarlier, instant);
        var newYorkText = newYork.ServiceProvider.GetRequiredService<ITmRelativeTimeFormatter>()
            .Format(eightHoursEarlier, instant);

        pragueText.Should().StartWith("yesterday at",
            "Prague has already crossed midnight, so the same instant is the previous calendar day");
        newYorkText.Should().Contain("h ago",
            "New York is still on 14 June, so eight hours earlier is the same calendar day");
        pragueText.Should().NotBe(newYorkText);
    }

    [Fact]
    public void TmTimeline_RendersTheLanguageOfTheScopeTheFormatterWasResolvedIn()
    {
        using var english = Render("EN-STAMP");
        using var czech = Render("CS-STAMP");

        english.Find(".tm-timeline-timestamp").TextContent.Should().Be("EN-STAMP");
        czech.Find(".tm-timeline-timestamp").TextContent.Should().Be("CS-STAMP");
    }

    private static IRenderedComponent<TmTimeline> Render(string stamp)
    {
        var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddTempoBlazor();
        ctx.Services.AddScoped<ITmLocalizer>(_ => new ScopedCultureLocalizer { Stamp = stamp });
        return ctx.Render<TmTimeline>(p => p.Add(c => c.Entries, new[] { At(DateTimeOffset.UtcNow) }));
    }
}
