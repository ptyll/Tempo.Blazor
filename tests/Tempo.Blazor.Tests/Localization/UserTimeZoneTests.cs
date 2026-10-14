using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.Timeline;
using Tempo.Blazor.Configuration;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Services;

namespace Tempo.Blazor.Tests.Localization;

/// <summary>
/// Fáze 18 (8d6b99e3): <see cref="ITmUserTimeZone"/> resolves the browser zone once per circuit and
/// <see cref="TmUserTimeProvider"/> gives the formatter a private clock over it. Since Fáze 25b the
/// default formatter already reads the zone (the former <c>AddTempoUserTimeZone()</c> opt-in is an
/// obsolete no-op), so these tests pin the plumbing itself. RED tests per cluster-D.md §11 (4).
/// </summary>
public class UserTimeZoneTests : LocalizationTestBase
{
    private sealed record Entry(
        string Id, string EntryType, string AuthorName, string? AuthorAvatarUrl, DateTimeOffset CreatedAt,
        string? HtmlContent, string? PlainContent, bool IsInternal,
        IReadOnlyDictionary<string, string>? Metadata = null) : ITimelineEntry;

    private static Entry At(DateTimeOffset when) =>
        new("e1", "comment", "Alice", null, when, null, "Text", false);

    /// <summary>Stand-in for prerender: JS interop throws <see cref="InvalidOperationException"/>.</summary>
    private sealed class UnavailableJSRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => throw new InvalidOperationException("JavaScript interop calls cannot be issued at this time.");

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => throw new InvalidOperationException("JavaScript interop calls cannot be issued at this time.");
    }

    /// <summary>Throws <paramref name="error"/> from every interop call (a circuit that went away mid-probe).</summary>
    private sealed class ThrowingJSRuntime(Exception error) : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => throw error;

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => throw error;
    }

    /// <summary>Import succeeds; the module's <c>resolve</c> stays pending until the test completes it.</summary>
    private sealed class PendingZoneJSRuntime : IJSRuntime
    {
        public TaskCompletionSource<string?> Resolve { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => new((TValue)(object)new PendingZoneModule(this));

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => new((TValue)(object)new PendingZoneModule(this));
    }

    private sealed class PendingZoneModule(PendingZoneJSRuntime runtime) : IJSObjectReference
    {
        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => (TValue)(object)(await runtime.Resolve.Task)!;

        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => (TValue)(object)(await runtime.Resolve.Task)!;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FixedFrequencyProvider(long frequency) : TimeProvider
    {
        public override long TimestampFrequency => frequency;
    }

    private BunitJSModuleInterop SetupZoneModule(string? zoneId)
    {
        var module = JSInterop.SetupModule("./_content/Tempo.Blazor/js/tm-user-zone.js");
        module.Setup<string?>("resolve", _ => true).SetResult(zoneId);
        return module;
    }

    [Fact]
    public void AddTempoBlazor_RegistersTheUserZone_ButNeverA_TimeProvider()
    {
        // The ValidateScopes trap: Kestrel and cookie auth are singletons that consume TimeProvider,
        // so a (scoped) TimeProvider registration breaks the host. The user zone must live on the
        // formatter's private clock instead.
        var services = new ServiceCollection();
        services.AddTempoBlazor();

        services.Should().Contain(d => d.ServiceType == typeof(ITmUserTimeZone),
            "AddTempoBlazor always registers ITmUserTimeZone (TryAddScoped)");
        services.Should().NotContain(d => d.ServiceType == typeof(TimeProvider),
            "AddTempoBlazor must never register a TimeProvider");

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        provider.GetService<TimeProvider>().Should().BeNull();
    }

    [Fact]
    public void AddTempoUserTimeZone_NullServices_Throws()
    {
        var act = () => ServiceCollectionExtensions.AddTempoUserTimeZone(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddTempoUserTimeZone_IsMarkedObsolete()
    {
        // Fáze 25b: the default flipped — the opt-in call is an obsolete no-op alias kept for
        // source compatibility. The method must stay public, callable and marked [Obsolete].
        var method = typeof(ServiceCollectionExtensions).GetMethod(
            nameof(ServiceCollectionExtensions.AddTempoUserTimeZone),
            new[] { typeof(IServiceCollection) });
        method.Should().NotBeNull("the alias must stay part of the public API");
        method!.GetCustomAttributes(typeof(ObsoleteAttribute), inherit: false)
            .Should().HaveCount(1, "the alias is marked [Obsolete]");
    }

    [Fact]
    public void AddTempoBlazor_Formatter_WithoutJsInterop_FallsBackToThePlainFormatter()
    {
        // A pure backend host (no IJSRuntime registered at all) must still resolve the formatter —
        // it keeps judging days in the clock's zone, exactly like before the flip. A non-opted host
        // must never fault.
        var services = new ServiceCollection();
        services.AddTempoBlazor();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        var formatter = scope.ServiceProvider.GetRequiredService<ITmRelativeTimeFormatter>();
        var instant = new DateTimeOffset(2026, 6, 15, 3, 30, 0, TimeSpan.Zero);
        formatter.Invoking(f => f.Format(instant, instant)).Should().NotThrow();
    }

    [Fact]
    public void AddTempoBlazor_WithoutJsRuntime_RegistersAResolvedMachineLocalZone()
    {
        // Fáze 25b fix: the ITmUserTimeZone registration must not require IJSRuntime — a pure
        // backend scope (or any host that never registered interop) still resolves the service,
        // and without a browser to ask, the machine-local zone IS the right zone, resolved.
        var services = new ServiceCollection();
        services.AddTempoBlazor();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        var zone = scope.ServiceProvider.GetRequiredService<ITmUserTimeZone>();
        zone.Zone.Should().Be(TimeZoneInfo.Local, "no IJSRuntime → nothing to probe → the machine zone");
        zone.IsResolved.Should().BeTrue();
    }

    [Fact]
    public void AddTempoBlazor_RespectsAHostRegisteredUserZone()
    {
        // TryAddScoped: a host that registers its own ITmUserTimeZone (a profile-driven zone)
        // keeps it — the library must not clobber it with the default.
        var services = new ServiceCollection();
        services.AddTempoBlazor();
        services.AddScoped<ITmUserTimeZone>(_ => new StubTmUserTimeZone(
            TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague")));

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ITmUserTimeZone>().Zone.Id.Should().Be(
            TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague").Id);
    }

    [Fact]
    public async Task TmUserTimeZone_WithoutJsRuntime_IsLocalResolved_AndNeverProbes()
    {
        var zone = new TmUserTimeZone(null!, isBrowser: false);
        var raised = 0;
        zone.Changed += (_, _) => raised++;

        await zone.EnsureResolvedAsync();

        zone.Zone.Should().Be(TimeZoneInfo.Local);
        zone.IsResolved.Should().BeTrue();
        raised.Should().Be(0);
    }

    [Fact]
    public void AddTempoBlazor_Formatter_JudgesDaysInTheUserZone_ByDefault()
    {
        // Fáze 25b: the default flipped — AddTempoBlazor() itself registers the formatter over
        // TmUserTimeProvider. No AddTempoUserTimeZone() call is needed any more.
        Services.AddTempoBlazor();
        var fake = new FakeTimeProvider(new DateTimeOffset(2026, 6, 14, 22, 30, 0, TimeSpan.Zero));
        Services.AddSingleton<TimeProvider>(fake);

        var formatter = Services.GetRequiredService<ITmRelativeTimeFormatter>();

        // 22:30 and 20:00 UTC are the same UTC day, but Prague (UTC+2) has already crossed midnight.
        var earlier = new DateTimeOffset(2026, 6, 14, 20, 0, 0, TimeSpan.Zero);
        formatter.Format(earlier).Should().Contain("h ago",
            "before the zone arrives the clock reads UTC, so both instants are still 14 June");

        Services.GetRequiredService<ITmUserTimeZone>().SetZone(TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague"));
        formatter.Format(earlier).Should().StartWith("yesterday at",
            "the DEFAULT formatter is the one over TmUserTimeProvider, so it reads the zone that arrived late");

        Services.GetRequiredService<ITmUserTimeZone>().SetZone(TimeZoneInfo.Utc);
        formatter.Format(earlier).Should().Contain("h ago",
            "the same formatter instance reads the zone on every format, never capturing it");
    }

    [Fact]
    public void WebAssemblyPath_IsResolvedImmediately_WithTheLocalZone()
    {
        var zone = new TmUserTimeZone(JSInterop.JSRuntime, isBrowser: true);

        zone.IsResolved.Should().BeTrue("the WASM runtime zone already is the browser zone");
        zone.Zone.Should().Be(TimeZoneInfo.Local);
    }

    [Fact]
    public void ServerPath_BeforeTheProbe_IsUtcAndUnresolved()
    {
        var zone = new TmUserTimeZone(JSInterop.JSRuntime, isBrowser: false);

        zone.IsResolved.Should().BeFalse();
        zone.Zone.Should().Be(TimeZoneInfo.Utc);
    }

    [Fact]
    public async Task Probe_WithAKnownId_ResolvesTheZone_AndRaisesChangedOnce()
    {
        SetupZoneModule("America/New_York");
        var zone = new TmUserTimeZone(JSInterop.JSRuntime, isBrowser: false);
        var raised = 0;
        zone.Changed += (_, _) => raised++;

        await zone.EnsureResolvedAsync();

        zone.IsResolved.Should().BeTrue();
        zone.Zone.Id.Should().Be(TimeZoneInfo.FindSystemTimeZoneById("America/New_York").Id);
        raised.Should().Be(1, "the browser value arrived exactly once");

        await zone.EnsureResolvedAsync();
        raised.Should().Be(1, "the probe runs once per circuit");
        zone.Zone.Id.Should().Be(TimeZoneInfo.FindSystemTimeZoneById("America/New_York").Id);
    }

    [Fact]
    public async Task Probe_WithAnUnknownId_FallsBackToUtc()
    {
        SetupZoneModule("Not/AZone");
        var zone = new TmUserTimeZone(JSInterop.JSRuntime, isBrowser: false);

        await zone.EnsureResolvedAsync();

        zone.IsResolved.Should().BeTrue("the browser reported; an unmapped id falls back to UTC");
        zone.Zone.Should().Be(TimeZoneInfo.Utc);
    }

    [Fact]
    public async Task Probe_WhenJsInteropIsUnavailable_KeepsTheZoneUnresolved()
    {
        // Prerender: JS interop throws InvalidOperationException. The zone must stay UTC/unresolved
        // (OnAfterRenderAsync does not run during prerender, so this is defence in depth).
        var zone = new TmUserTimeZone(new UnavailableJSRuntime(), isBrowser: false);
        var raised = 0;
        zone.Changed += (_, _) => raised++;

        await zone.EnsureResolvedAsync();

        zone.IsResolved.Should().BeFalse();
        zone.Zone.Should().Be(TimeZoneInfo.Utc);
        raised.Should().Be(0);
    }

    [Fact]
    public async Task Probe_WhenTheCircuitDisconnects_ReturnsQuietly()
    {
        // The circuit went away mid-probe: JSDisconnectedException from the import AND from the
        // module call. The zone is best-effort — it must never fault the circuit.
        var importLevel = new TmUserTimeZone(
            new ThrowingJSRuntime(new JSDisconnectedException("JavaScript interop calls cannot be issued at this time.")),
            isBrowser: false);
        var raised = 0;
        importLevel.Changed += (_, _) => raised++;

        await importLevel.EnsureResolvedAsync();

        importLevel.IsResolved.Should().BeFalse();
        importLevel.Zone.Should().Be(TimeZoneInfo.Utc);
        raised.Should().Be(0);

        var module = JSInterop.SetupModule("./_content/Tempo.Blazor/js/tm-user-zone.js");
        module.Setup<string?>("resolve", _ => true)
            .SetException(new JSDisconnectedException("JavaScript interop calls cannot be issued at this time."));
        var resolveLevel = new TmUserTimeZone(JSInterop.JSRuntime, isBrowser: false);

        await resolveLevel.EnsureResolvedAsync();

        resolveLevel.IsResolved.Should().BeFalse();
        resolveLevel.Zone.Should().Be(TimeZoneInfo.Utc);
    }

    [Fact]
    public async Task Probe_WhenCancelled_ReturnsQuietly()
    {
        var zone = new TmUserTimeZone(new ThrowingJSRuntime(new OperationCanceledException()), isBrowser: false);
        var raised = 0;
        zone.Changed += (_, _) => raised++;

        await zone.EnsureResolvedAsync();

        zone.IsResolved.Should().BeFalse();
        zone.Zone.Should().Be(TimeZoneInfo.Utc);
        raised.Should().Be(0);
    }

    private sealed class SwitchableJSRuntime : IJSRuntime
    {
        public IJSRuntime Current { get; set; } = null!;

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => Current.InvokeAsync<TValue>(identifier, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => Current.InvokeAsync<TValue>(identifier, cancellationToken, args);
    }

    [Fact]
    public async Task Probe_AfterAFailedImport_CanBeRetried()
    {
        // Fáze 25b: the one-shot flag flips only after a SUCCESSFUL import — a probe attempted
        // while interop was unavailable (prerender-like) must not burn it; a later call resolves.
        var switcher = new SwitchableJSRuntime { Current = new UnavailableJSRuntime() };
        var zone = new TmUserTimeZone(switcher, isBrowser: false);

        await zone.EnsureResolvedAsync();
        zone.IsResolved.Should().BeFalse("the import never landed; the one-shot must not burn");

        SetupZoneModule("America/New_York");
        switcher.Current = JSInterop.JSRuntime;
        await zone.EnsureResolvedAsync();

        zone.Zone.Id.Should().Be(TimeZoneInfo.FindSystemTimeZoneById("America/New_York").Id,
            "the retried probe resolved the browser zone");
    }

    [Fact]
    public async Task SetZone_WinsOverAProbeThatCompletesLater()
    {
        // Race: the probe is in flight when the host calls SetZone; the late browser value must not
        // overwrite the override.
        var js = new PendingZoneJSRuntime();
        var zone = new TmUserTimeZone(js, isBrowser: false);
        var raised = 0;
        zone.Changed += (_, _) => raised++;

        var probe = zone.EnsureResolvedAsync();
        var prague = TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");
        zone.SetZone(prague);

        js.Resolve.TrySetResult("America/New_York");
        await probe;

        zone.Zone.Id.Should().Be(prague.Id,
            "a host override that landed while the probe was in flight must win over the late browser value");
        raised.Should().Be(1, "the overridden probe must not raise Changed a second time");
    }

    [Fact]
    public async Task SetZone_WinsOverALaterBrowserValue()
    {
        SetupZoneModule("America/New_York");
        var zone = new TmUserTimeZone(JSInterop.JSRuntime, isBrowser: false);
        var raised = 0;
        zone.Changed += (_, _) => raised++;

        var prague = TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");
        zone.SetZone(prague);

        zone.IsResolved.Should().BeTrue("a host override counts as resolved");
        zone.Zone.Should().Be(prague);
        raised.Should().Be(1);

        await zone.EnsureResolvedAsync();

        zone.Zone.Should().Be(prague, "the host override wins over the browser value");
        raised.Should().Be(1, "the skipped probe must not raise Changed again");
        JSInterop.Invocations.Should().NotContain(i => i.Identifier == "import",
            "the probe must not even run once a host override exists");
    }

    [Fact]
    public void SetZone_RaisesChangedOnlyWhenTheZoneChanges_AndRejectsNull()
    {
        var zone = new TmUserTimeZone(JSInterop.JSRuntime, isBrowser: false);
        var raised = 0;
        zone.Changed += (_, _) => raised++;

        zone.Invoking(z => z.SetZone(null!)).Should().Throw<ArgumentNullException>();

        var utc = TimeZoneInfo.Utc;
        zone.SetZone(utc);
        raised.Should().Be(1, "SetZone on the unresolved UTC default still announces the resolved zone");

        zone.SetZone(utc);
        raised.Should().Be(1, "setting the same zone again is a no-op");

        zone.SetZone(TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague"));
        raised.Should().Be(2);
    }

    [Fact]
    public void TmUserTimeProvider_ReadsTheZoneLazily_AndDelegatesTheClock()
    {
        var zone = new TmUserTimeZone(JSInterop.JSRuntime, isBrowser: false);
        var fake = new FakeTimeProvider(new DateTimeOffset(2026, 6, 14, 22, 30, 0, TimeSpan.Zero));
        var clock = new TmUserTimeProvider(zone, fake);

        clock.LocalTimeZone.Should().Be(TimeZoneInfo.Utc, "read lazily: the zone arrives only later");
        clock.GetUtcNow().Should().Be(fake.GetUtcNow(), "the time itself comes from the inner clock");
        clock.GetTimestamp().Should().Be(fake.GetTimestamp());

        var prague = TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");
        zone.SetZone(prague);
        clock.LocalTimeZone.Should().Be(prague,
            "the same clock instance honours a zone that arrives after the first read");
    }

    [Fact]
    public void TmUserTimeProvider_DelegatesTimestampFrequency()
    {
        var zone = new TmUserTimeZone(JSInterop.JSRuntime, isBrowser: false);
        var inner = new FixedFrequencyProvider(12_345_678);
        var clock = new TmUserTimeProvider(zone, inner);

        clock.TimestampFrequency.Should().Be(12_345_678,
            "elapsed-time math divides by the inner clock's frequency, so it must be forwarded");
    }

    [Fact]
    public void AddTempoUserTimeZone_Formatter_JudgesDaysInTheUserZone_Lazily()
    {
        Services.AddTempoBlazor();
        Services.AddTempoUserTimeZone();
        var fake = new FakeTimeProvider(new DateTimeOffset(2026, 6, 14, 22, 30, 0, TimeSpan.Zero));
        Services.AddSingleton<TimeProvider>(fake);

        var formatter = Services.GetRequiredService<ITmRelativeTimeFormatter>();

        // 22:30 and 20:00 UTC are the same UTC day, but Prague (UTC+2) has already crossed midnight.
        var earlier = new DateTimeOffset(2026, 6, 14, 20, 0, 0, TimeSpan.Zero);
        formatter.Format(earlier).Should().Contain("h ago",
            "before the zone arrives the clock reads UTC, so both instants are still 14 June");

        Services.GetRequiredService<ITmUserTimeZone>().SetZone(TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague"));
        formatter.Format(earlier).Should().StartWith("yesterday at",
            "the formatter is the one over TmUserTimeProvider, so it reads the zone that arrived late");

        Services.GetRequiredService<ITmUserTimeZone>().SetZone(TimeZoneInfo.Utc);
        formatter.Format(earlier).Should().Contain("h ago",
            "the same formatter instance reads the zone on every format, never capturing it");
    }

    [Fact]
    public void TmTimeline_WithAddTempoUserTimeZone_Rerenders_WhenTheZoneArrives()
    {
        Services.AddTempoBlazor();
        Services.AddTempoUserTimeZone();
        var fake = new FakeTimeProvider(new DateTimeOffset(2026, 6, 14, 22, 30, 0, TimeSpan.Zero));
        Services.AddSingleton<TimeProvider>(fake);

        var cut = Render<TmTimeline>(p => p.Add(c => c.Entries, new[] { At(fake.GetUtcNow().AddHours(-2).AddMinutes(-30)) }));
        cut.Find(".tm-timeline-timestamp").TextContent.Should().Contain("h ago");

        cut.Services.GetRequiredService<ITmUserTimeZone>().SetZone(TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague"));

        cut.WaitForAssertion(
            () => cut.Find(".tm-timeline-timestamp").TextContent.Should().StartWith("yesterday at"),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Observer_StopsRefreshing_AfterDispose()
    {
        Services.AddTempoBlazor();
        var zone = Services.GetRequiredService<ITmUserTimeZone>();
        var refreshed = 0;
        var observer = TmUserZoneObserver.Subscribe(Services, () => { refreshed++; return Task.CompletedTask; });
        observer.Should().NotBeNull();

        zone.SetZone(TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague"));
        refreshed.Should().Be(1);

        observer!.Dispose();
        zone.SetZone(TimeZoneInfo.Utc);
        refreshed.Should().Be(1, "a disposed observer must not refresh the component");
    }
}
