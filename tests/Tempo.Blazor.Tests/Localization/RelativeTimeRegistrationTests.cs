using System.Globalization;
using System.Reflection;
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
    public void TmRelativeTime_Resolve_IsPublic()
    {
        // 9b (Q10=A): split packages (NotionEditor today) must resolve the shared fallback without
        // InternalsVisibleTo or copying it — the one resolver every component uses is public.
        typeof(TmRelativeTime).IsPublic.Should().BeTrue("packages cannot see an internal helper");
        var resolve = typeof(TmRelativeTime).GetMethod("Resolve", BindingFlags.Public | BindingFlags.Static);
        resolve.Should().NotBeNull("the shared resolver must be callable from any package");
        resolve!.ReturnType.Should().Be(typeof(ITmRelativeTimeFormatter));
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

    /// <summary>Stand-in for a singleton host service (Kestrel pool, cookie auth) that consumes TimeProvider.</summary>
    private sealed class SingletonClockConsumer(TimeProvider clock)
    {
        public TimeProvider Clock { get; } = clock;
    }

    [Fact]
    public void DocumentedRegistrationSnippet_Compiles()
    {
        // The snippet in docs/localization-plurals.md is compiled as part of this project
        // (DocumentedUserZoneRegistration). A snippet that does not compile fails the build.
        var doc = File.ReadAllText(Path.Combine(Theme.ThemeCss.RepositoryRoot().FullName, "docs", "localization-plurals.md"));
        var sample = File.ReadAllText(Path.Combine(
            Theme.ThemeCss.RepositoryRoot().FullName,
            "tests", "Tempo.Blazor.Tests", "Localization", "DocumentedUserZoneRegistration.cs"));
        var marker = doc.IndexOf("doc-sample:user-zone", StringComparison.Ordinal);
        marker.Should().BeGreaterThan(0);
        var snippet = doc[(marker + "doc-sample:user-zone".Length)..(doc.IndexOf(")));", marker, StringComparison.Ordinal) + 4)];
        var body = sample.IndexOf("public static void Register", StringComparison.Ordinal);
        var compiledStart = sample.IndexOf("doc-sample:user-zone", body, StringComparison.Ordinal);
        var compiled = sample[(compiledStart + "doc-sample:user-zone".Length)..(sample.IndexOf(")));", compiledStart, StringComparison.Ordinal) + 4)];
        Normalize(snippet).Should().Be(Normalize(compiled),
            "the compiled sample is the documented snippet, not a paraphrase of it");

        // The registration lines are only half the documented snippet: the lazy zone read lives in the
        // UserTimeProvider class body, so that body is compared too. "must appear once" is what keeps a
        // commented paraphrase of the class out of the compiled sample.
        const string classAnchor = "sealed class UserTimeProvider";
        Normalize(ClassBlock(doc, classAnchor)).Should().Be(Normalize(ClassBlock(sample, classAnchor)),
            "the documented UserTimeProvider (the lazy zone read the K1 fix depends on) is the compiled one");
    }

    private static string Normalize(string text)
        => string.Join('\n', text.Replace("\r", "", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("//", StringComparison.Ordinal)));

    /// <summary>From the anchor to the line that closes the class's brace (depth back to 0).</summary>
    private static string ClassBlock(string text, string anchor)
    {
        var start = text.IndexOf(anchor, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"'{anchor}' must exist exactly where the doc shows it");
        text.IndexOf(anchor, start + anchor.Length, StringComparison.Ordinal).Should().Be(-1, $"'{anchor}' must appear once");
        var depth = 0;
        var seenBrace = false;
        for (var i = start; i < text.Length; i++)
        {
            if (text[i] == '{')
            {
                depth++;
                seenBrace = true;
            }
            else if (text[i] == '}' && --depth == 0 && seenBrace)
            {
                return text[start..(i + 1)];
            }
        }

        throw new InvalidOperationException($"unterminated class block after '{anchor}'");
    }

    [Fact]
    public void DocumentedRegistration_ResolvesBesideASingletonTimeProviderConsumer_AndHonoursALateZone()
    {
        // K1: AddScoped<TimeProvider> replaces the app-wide clock, so a singleton consumer throws
        // under ValidateScopes. And a clock that captures the zone at first resolution stays UTC,
        // because JS fills the cache only after the first render.
        using var provider = BuildValidated(services =>
        {
            services.AddSingleton<ITmLocalizer>(new ScopedCultureLocalizer
            {
                Stamp = "now",
                Culture = CultureInfo.GetCultureInfo("en-US"),
            });
            services.AddSingleton<TimeProvider>(TimeProvider.System);
            services.AddSingleton<SingletonClockConsumer>();
            // Registered before the snippet so the RED snippet, which expects the host to own the cache,
            // can resolve it. The fixed snippet registers its own; the last registration wins for both.
            services.AddScoped<DocumentedUserZoneRegistration.CircuitZoneCache>();
            DocumentedUserZoneRegistration.Register(services);
        });

        var resolveHost = () => provider.GetRequiredService<SingletonClockConsumer>();
        resolveHost.Should().NotThrow(
            "the documented registration must not replace the app-wide TimeProvider: a singleton host service consumes it");
        resolveHost().Clock.Should().BeSameAs(TimeProvider.System);

        using var scope = provider.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<DocumentedUserZoneRegistration.CircuitZoneCache>();
        var formatter = scope.ServiceProvider.GetRequiredService<ITmRelativeTimeFormatter>();

        // 22:30 and 20:00 UTC are the same UTC day, but Prague (UTC+2) has already crossed midnight.
        var instant = new DateTimeOffset(2026, 6, 14, 22, 30, 0, TimeSpan.Zero);
        var earlier = new DateTimeOffset(2026, 6, 14, 20, 0, 0, TimeSpan.Zero);

        formatter.Format(earlier, instant).Should().Contain("h ago",
            "before JS supplies the zone the clock reads UTC, so both instants are still 14 June");

        cache.Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");
        formatter.Format(earlier, instant).Should().StartWith("yesterday at",
            "the zone set after the first resolution is read lazily, so the same formatter instance honours it");
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
