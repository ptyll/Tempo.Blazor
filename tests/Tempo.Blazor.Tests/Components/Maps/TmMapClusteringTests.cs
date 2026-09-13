using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Interfaces;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Maps;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Maps;

public class TmMapClusteringTests : LocalizationTestBase
{
    private const string ModulePath = "./_content/Tempo.Blazor.Maps/js/map.js";

    private BunitJSModuleInterop SetupMapModule()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.Mode = JSRuntimeMode.Loose;
        return module;
    }

    private static MapDataPayload PayloadOf(JSRuntimeInvocation invocation)
        => (MapDataPayload)invocation.Arguments[1]!;

    /// <summary>
    /// Polls until <paramref name="condition"/> holds. Background provider loads do not trigger
    /// renders, so bUnit's render-driven WaitForAssertion never re-evaluates — plain polling does.
    /// </summary>
    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition() && Environment.TickCount64 < deadline)
        {
            await Task.Delay(20);
        }

        condition().Should().BeTrue("the awaited condition should hold within the timeout");
    }

    /// <summary>Fake provider with controllable results and call recording.</summary>
    private sealed class FakeMapDataProvider : IMapDataProvider
    {
        private readonly Func<MapViewport, CancellationToken, Task<MapDataResult>> _handler;

        public FakeMapDataProvider(Func<MapViewport, CancellationToken, Task<MapDataResult>>? handler = null)
            => _handler = handler ?? ((_, _) => Task.FromResult(MapDataResult.Empty));

        public List<(MapViewport Viewport, CancellationToken Token)> Calls { get; } = [];

        public Task<MapDataResult> GetPointsAsync(MapViewport viewport, CancellationToken cancellationToken)
        {
            Calls.Add((viewport, cancellationToken));
            return _handler(viewport, cancellationToken);
        }
    }

    // ── CLU-1: ClusterPoints parametr → setData s cluster payloadem ────────

    [Fact]
    public void ClusterPoints_ArePassedToSetData()
    {
        var module = SetupMapModule();
        var clusters = new List<MapClusterPoint>
        {
            new("cell-1", 50.0, 14.0, 42, "avg 25 000"),
            new("cell-2", 49.2, 16.6, 7, null),
        };

        var cut = Render<TmMap>(parameters => parameters
            .Add(p => p.ClusterPoints, clusters));

        var invocation = module.Invocations.Single(i => i.Identifier == "setData");
        var payload = PayloadOf(invocation);
        payload.Clusters.Should().HaveCount(2);
        payload.Clusters[0].Key.Should().Be("cell-1");
        payload.Clusters[0].Count.Should().Be(42);
        payload.Markers.Should().BeEmpty();
    }

    // ── CLU-2: cluster CountText je formátovaný dle CultureInfo ────────────

    [Fact]
    public void ClusterCountText_IsCultureFormatted()
    {
        var module = SetupMapModule();
        var clusters = new List<MapClusterPoint> { new("k", 50.0, 14.0, 12345, null) };

        var cut = Render<TmMap>(parameters => parameters
            .Add(p => p.ClusterPoints, clusters));

        var payload = PayloadOf(module.Invocations.Single(i => i.Identifier == "setData"));
        var expected = 12345.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);
        payload.Clusters[0].CountText.Should().Be(expected);
    }

    // ── CLU-3: UseClientClustering → setData s clientClustering=true ───────

    [Fact]
    public void UseClientClustering_IsPassedToSetData()
    {
        var module = SetupMapModule();
        var markers = new List<MapMarker> { new("m1", 50.0, 14.0, null) };

        var cut = Render<TmMap>(parameters => parameters
            .Add(p => p.Markers, markers)
            .Add(p => p.UseClientClustering, true));

        var payload = PayloadOf(module.Invocations.Single(i => i.Identifier == "setData"));
        payload.ClientClustering.Should().BeTrue();
        payload.Markers.Should().HaveCount(1);
    }

    // ── CLU-4: výměna markers→clusters je JEDNO setData (atomická) ─────────

    [Fact]
    public void SwitchingMarkersToClusters_IsSingleAtomicSetData()
    {
        var module = SetupMapModule();
        var cut = Render<TmMap>(parameters => parameters
            .Add(p => p.Markers, new List<MapMarker> { new("m1", 50.0, 14.0, null) }));

        module.Invocations.Count(i => i.Identifier == "setData").Should().Be(1);

        cut.Render(parameters => parameters
            .Add(p => p.Markers, (IReadOnlyList<MapMarker>?)null)
            .Add(p => p.ClusterPoints, new List<MapClusterPoint> { new("c", 49.0, 15.0, 10, null) }));

        // Exactly one more setData — never a separate clear + add (no double rendering).
        module.Invocations.Count(i => i.Identifier == "setData").Should().Be(2);
        module.Invocations.Should().NotContain(i => i.Identifier == "clearMarkers");
        var last = PayloadOf(module.Invocations.Last(i => i.Identifier == "setData"));
        last.Clusters.Should().HaveCount(1);
        last.Markers.Should().BeEmpty();
    }

    // ── CLU-5: HandleClusterClick vyvolá OnClusterClick s klíčem ───────────

    [Fact]
    public async Task HandleClusterClick_RaisesOnClusterClickWithKey()
    {
        SetupMapModule();
        MapClusterPoint? received = null;
        var cut = Render<TmMap>(parameters => parameters
            .Add(p => p.OnClusterClick, EventCallback.Factory.Create<MapClusterPoint>(
                this, c => received = c)));

        await cut.InvokeAsync(() => cut.Instance.HandleClusterClick("cell-7", 49.9, 15.1, 23));

        received.Should().NotBeNull();
        received!.Key.Should().Be("cell-7");
        received.Latitude.Should().Be(49.9);
        received.Longitude.Should().Be(15.1);
        received.Count.Should().Be(23);
    }

    // ── CLU-6: imperativní AddClusterGroupAsync + AddMarkersToClusterAsync ─

    [Fact]
    public async Task ImperativeClusterGroup_InvokesJsFunctions()
    {
        var module = SetupMapModule();
        module.Setup<string>("addClusterGroup", _ => true).SetResult("cluster-1");
        var cut = Render<TmMap>();

        var clusterId = await cut.Instance.AddClusterGroupAsync();
        clusterId.Should().Be("cluster-1");

        await cut.Instance.AddMarkersToClusterAsync(clusterId!, new List<MapMarker> { new("m1", 50.0, 14.0, "P") });

        module.Invocations.Should().Contain(i => i.Identifier == "addMarkersToCluster");
    }

    // ── CLU-7: DataProvider se zavolá po initu s výchozím viewportem ───────

    [Fact]
    public void DataProvider_IsCalledAfterInit_WithInitialViewport()
    {
        var module = SetupMapModule();
        var provider = new FakeMapDataProvider((v, _) => Task.FromResult(new MapDataResult(
            Markers: [new MapMarker("m1", v.Latitude, v.Longitude, null)])));

        var cut = Render<TmMap>(parameters => parameters
            .Add(p => p.CenterLatitude, 49.8)
            .Add(p => p.CenterLongitude, 15.5)
            .Add(p => p.Zoom, 8.0)
            .Add(p => p.DataProvider, provider));

        cut.WaitForAssertion(() =>
        {
            provider.Calls.Should().HaveCount(1);
            provider.Calls[0].Viewport.Latitude.Should().Be(49.8);
            provider.Calls[0].Viewport.Longitude.Should().Be(15.5);
            provider.Calls[0].Viewport.Zoom.Should().Be(8.0);
            module.Invocations.Should().Contain(i => i.Identifier == "setData");
        }, TimeSpan.FromSeconds(5));
    }

    // ── CLU-8: debounce — rychlé změny viewportu → jediný dotaz ────────────

    [Fact]
    public async Task ViewportChanges_AreDebounced()
    {
        SetupMapModule();
        var provider = new FakeMapDataProvider();
        var cut = Render<TmMap>(parameters => parameters
            .Add(p => p.DataProvider, provider)
            .Add(p => p.DataRequestDebounceMs, 100));

        await WaitUntilAsync(() => provider.Calls.Count == 1);

        // Three rapid viewport changes within the debounce window…
        await cut.Instance.HandleViewportChanged(9.0, 49.0, 15.0);
        await cut.Instance.HandleViewportChanged(9.5, 49.1, 15.1);
        await cut.Instance.HandleViewportChanged(10.0, 49.2, 15.2);

        // …coalesce into exactly one provider request (the latest viewport).
        await WaitUntilAsync(() => provider.Calls.Count == 2);
        provider.Calls[1].Viewport.Zoom.Should().Be(10.0);

        // The barrier is "nothing can still call the provider": every scheduled debounce→load
        // chain — cancelled or completed — has settled. A chain that escaped supersession is still
        // pending here, and it delivers a third call before this wait ends.
        await WaitUntilAsync(() => cut.Instance.PendingDataRequests == 0);
        provider.Calls.Should().HaveCount(2);
    }

    // ── CLU-9: race — novější dotaz zruší CancellationToken předchozího ────

    [Fact]
    public async Task NewerRequest_CancelsPreviousToken_AndOnlyLatestResultApplies()
    {
        var module = SetupMapModule();
        var firstStarted = new TaskCompletionSource();
        var firstRelease = new TaskCompletionSource<MapDataResult>();
        var callIndex = 0;

        var provider = new FakeMapDataProvider((v, ct) =>
        {
            var index = Interlocked.Increment(ref callIndex);
            if (index == 2)
            {
                // Initial load already happened (index 1); this is the slow request.
                firstStarted.TrySetResult();
                return firstRelease.Task;
            }

            return Task.FromResult(new MapDataResult(
                Markers: [new MapMarker($"call-{index}", v.Latitude, v.Longitude, null)]));
        });

        var cut = Render<TmMap>(parameters => parameters
            .Add(p => p.DataProvider, provider)
            .Add(p => p.DataRequestDebounceMs, 1));

        await WaitUntilAsync(() => provider.Calls.Count == 1);

        // Slow request #2 hangs inside the provider.
        await cut.Instance.HandleViewportChanged(9.0, 49.0, 15.0);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Request #3 supersedes it — token #2 must be cancelled.
        await cut.Instance.HandleViewportChanged(10.0, 49.2, 15.2);
        await WaitUntilAsync(() => provider.Calls.Count == 3 && provider.Calls[1].Token.IsCancellationRequested);

        // Late result of the superseded request must be discarded.
        firstRelease.SetResult(new MapDataResult(Markers: [new MapMarker("stale", 0, 0, null)]));

        // The barrier is "the stale continuation has finished": request #2's chain stays pending
        // until the released provider task's continuation has run — applied or discarded. Waiting
        // on that is the only point where the payload below is final; a fixed delay could return
        // before the continuation ever ran.
        await WaitUntilAsync(() => cut.Instance.PendingDataRequests == 0);

        var lastPayload = PayloadOf(module.Invocations.Last(i => i.Identifier == "setData"));
        lastPayload.Markers.Should().NotContain(m => m.Id == "stale");
    }

    // ── CLU-10: výjimka providera → OnDataError, mapa dál použitelná ───────

    [Fact]
    public async Task ProviderException_RaisesOnDataError_AndMapStaysUsable()
    {
        var module = SetupMapModule();
        var shouldFail = true;
        Exception? received = null;
        var provider = new FakeMapDataProvider((v, _) => shouldFail
            ? Task.FromException<MapDataResult>(new InvalidOperationException("backend down"))
            : Task.FromResult(new MapDataResult(Markers: [new MapMarker("ok", v.Latitude, v.Longitude, null)])));

        var cut = Render<TmMap>(parameters => parameters
            .Add(p => p.DataProvider, provider)
            .Add(p => p.DataRequestDebounceMs, 1)
            .Add(p => p.OnDataError, EventCallback.Factory.Create<Exception>(this, e => received = e)));

        await WaitUntilAsync(() => received is InvalidOperationException);

        // Map recovers: next refresh succeeds and renders data.
        shouldFail = false;
        await cut.Instance.RefreshDataAsync();
        await WaitUntilAsync(() =>
            module.Invocations.Any(i => i.Identifier == "setData")
            && PayloadOf(module.Invocations.Last(i => i.Identifier == "setData")).Markers.Any(m => m.Id == "ok"));
    }

    // ── CLU-11: RefreshDataAsync bez providera je no-op ────────────────────

    [Fact]
    public async Task RefreshDataAsync_WithoutProvider_DoesNotThrow()
    {
        SetupMapModule();
        var cut = Render<TmMap>();

        var act = async () => await cut.Instance.RefreshDataAsync();

        await act.Should().NotThrowAsync();
    }

    // ── CLU-12: 50k bodů projde jedním setData voláním ─────────────────────

    [Fact]
    public async Task FiftyThousandMarkers_GoThroughSingleSetData()
    {
        var module = SetupMapModule();
        var markers = Enumerable.Range(0, 50_000)
            .Select(i => new MapMarker($"m{i}", 48.5 + (i % 200) * 0.01, 12.0 + (i / 200) * 0.02, null))
            .ToList();
        var cut = Render<TmMap>();

        await cut.Instance.SetMarkersAsync(markers);

        var payload = PayloadOf(module.Invocations.Single(i => i.Identifier == "setData"));
        payload.Markers.Should().HaveCount(50_000);
    }

    // ── CLU-13: prázdný výsledek providera → setData s prázdnými poli ──────

    [Fact]
    public async Task EmptyProviderResult_AppliesEmptySetData()
    {
        var module = SetupMapModule();
        var provider = new FakeMapDataProvider();

        var cut = Render<TmMap>(parameters => parameters
            .Add(p => p.DataProvider, provider));

        await WaitUntilAsync(() => module.Invocations.Any(i => i.Identifier == "setData"));
        var payload = PayloadOf(module.Invocations.Last(i => i.Identifier == "setData"));
        payload.Markers.Should().BeEmpty();
        payload.Clusters.Should().BeEmpty();
    }

    // ── CLU-14: cluster Count=1 payload projde (plain marker řeší JS) ──────

    [Fact]
    public void ClusterWithCountOne_IsPassedThrough()
    {
        var module = SetupMapModule();
        var clusters = new List<MapClusterPoint> { new("single", 50.0, 14.0, 1, "one") };

        var cut = Render<TmMap>(parameters => parameters
            .Add(p => p.ClusterPoints, clusters));

        var payload = PayloadOf(module.Invocations.Single(i => i.Identifier == "setData"));
        payload.Clusters.Should().ContainSingle(c => c.Count == 1 && c.Key == "single");
    }

    // ── CLU-15: cluster payload nese lokalizovaný aria-label ───────────────

    [Fact]
    public void ClusterPayload_CarriesLocalizedAriaLabel()
    {
        var module = SetupMapModule();
        var clusters = new List<MapClusterPoint> { new("k", 50.0, 14.0, 5, null) };

        var cut = Render<TmMap>(parameters => parameters
            .Add(p => p.ClusterPoints, clusters));

        var payload = PayloadOf(module.Invocations.Single(i => i.Identifier == "setData"));
        payload.Clusters[0].AriaLabel.Should().NotBeNullOrEmpty();
        payload.Clusters[0].AriaLabel.Should().Contain("5");
    }
}
