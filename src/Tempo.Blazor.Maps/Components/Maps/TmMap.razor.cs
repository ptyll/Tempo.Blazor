using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Interfaces;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components;

namespace Tempo.Blazor.Components.Maps;

/// <summary>
/// Interactive Leaflet map with OpenStreetMap tiles. Leaflet assets are bundled with the
/// Tempo.Blazor.Maps package (no CDN dependency) and loaded lazily by the ES module.
/// Marker batches are rendered through a Canvas renderer so thousands of points stay fast;
/// data sets (markers/clusters) are swapped atomically so the map never double-renders.
/// </summary>
public partial class TmMap : TmComponentBase, IAsyncDisposable
{
    private const string ModulePath = "./_content/Tempo.Blazor.Maps/js/map.js";

    private readonly string _mapElementId = $"tm-map-{Guid.NewGuid():N}";
    private IJSObjectReference? _module;
    private DotNetObjectReference<TmMap>? _dotNetRef;
    private bool _initialized;
    private bool _disposed;
    private IReadOnlyList<MapMarker>? _renderedMarkers;
    private IReadOnlyList<MapClusterPoint>? _renderedClusters;
    private bool _renderedClientClustering;
    private CancellationTokenSource? _debounceCts;
    private CancellationTokenSource? _loadCts;
    private int _pendingDataRequests;

    /// <summary>
    /// How many scheduled debounce→load chains are still alive — armed debounces AND in-flight
    /// provider calls. Test-visible state: the "no further request may arrive" assertions wait on
    /// this reaching zero, which is the only point where nothing can still call the provider —
    /// instead of sleeping a fixed wall-clock margin past the debounce window.
    /// </summary>
    internal int PendingDataRequests => Volatile.Read(ref _pendingDataRequests);
    private int _loadSequence;
    private MapViewport? _lastViewport;

    [Inject] private IJSRuntime JS { get; set; } = default!;

    /// <summary>Latitude of the initial map center in decimal degrees. Defaults to the Czech Republic centroid.</summary>
    [Parameter] public double CenterLatitude { get; set; } = 49.8175;

    /// <summary>Longitude of the initial map center in decimal degrees. Defaults to the Czech Republic centroid.</summary>
    [Parameter] public double CenterLongitude { get; set; } = 15.473;

    /// <summary>Initial Leaflet zoom level. Fractional values are supported. Defaults to 7.5 (whole Czech Republic).</summary>
    [Parameter] public double Zoom { get; set; } = 7.5;

    /// <summary>CSS height of the map container (any CSS length). Defaults to 400px.</summary>
    [Parameter] public string Height { get; set; } = "400px";

    /// <summary>Additional CSS classes applied to the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>
    /// Markers rendered on the map via the Canvas renderer. Markers with a duplicate non-null
    /// <see cref="MapMarker.Id"/> are de-duplicated (the first occurrence wins). Replacing the
    /// list atomically re-renders all markers.
    /// </summary>
    [Parameter] public IReadOnlyList<MapMarker>? Markers { get; set; }

    /// <summary>
    /// Server-side aggregated cluster points rendered as count bubbles. A cluster with
    /// Count = 1 renders as a plain marker. Replacing the list atomically re-renders all clusters.
    /// </summary>
    [Parameter] public IReadOnlyList<MapClusterPoint>? ClusterPoints { get; set; }

    /// <summary>
    /// When true, <see cref="Markers"/> are grouped client-side with leaflet.markercluster
    /// (spiderfy on max zoom, zoom-to-bounds on cluster click).
    /// </summary>
    [Parameter] public bool UseClientClustering { get; set; }

    /// <summary>
    /// Optional data source: when set, the component requests fresh points after the viewport
    /// settles (debounced by <see cref="DataRequestDebounceMs"/>), cancelling the superseded
    /// request. The component stays fully usable without a provider (imperative mode).
    /// </summary>
    [Parameter] public IMapDataProvider? DataProvider { get; set; }

    /// <summary>Debounce interval in milliseconds for provider requests after pan/zoom. Defaults to 300.</summary>
    [Parameter] public int DataRequestDebounceMs { get; set; } = 300;

    /// <summary>Tile layer URL template. Defaults to the public OpenStreetMap tile server.</summary>
    [Parameter] public string TileUrlTemplate { get; set; } = "https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png";

    /// <summary>Maximum zoom level offered by the tile layer. Defaults to 19.</summary>
    [Parameter] public int MaxZoom { get; set; } = 19;

    /// <summary>Raised when a marker is clicked. The payload carries the marker id and coordinates.</summary>
    [Parameter] public EventCallback<MapMarker> OnMarkerClick { get; set; }

    /// <summary>
    /// Raised when a cluster is clicked: for server-side clusters the payload carries the cluster
    /// key and count; for client-side (markercluster) groups the key is null.
    /// </summary>
    [Parameter] public EventCallback<MapClusterPoint> OnClusterClick { get; set; }

    /// <summary>Raised after the zoom level changes. The payload carries the new center and zoom.</summary>
    [Parameter] public EventCallback<MapViewport> OnZoomChanged { get; set; }

    /// <summary>Raised when the map background is clicked. The payload carries the clicked coordinates and current zoom.</summary>
    [Parameter] public EventCallback<MapViewport> OnMapClick { get; set; }

    /// <summary>Raised when <see cref="DataProvider"/> throws; the map stays usable and keeps its last data.</summary>
    [Parameter] public EventCallback<Exception> OnDataError { get; set; }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || _disposed)
        {
            return;
        }

        try
        {
            _module = await JS.InvokeAsync<IJSObjectReference>("import", ModulePath);
            _dotNetRef = DotNetObjectReference.Create(this);
            var options = new MapInitOptions(TileUrlTemplate, MaxZoom, Loc["TmMap_Attribution"]);
            await _module.InvokeVoidAsync("init", _mapElementId, CenterLatitude, CenterLongitude, Zoom, options, _dotNetRef);
            _initialized = true;
            _lastViewport = new MapViewport(CenterLatitude, CenterLongitude, Zoom);

            if (Markers is { Count: > 0 } || ClusterPoints is { Count: > 0 })
            {
                await ApplyDataAsync(Markers, ClusterPoints, UseClientClustering);
            }
            else
            {
                _renderedMarkers = Markers;
                _renderedClusters = ClusterPoints;
                _renderedClientClustering = UseClientClustering;
            }

            StateHasChanged();

            if (DataProvider is not null)
            {
                await LoadDataAsync();
            }
        }
        catch (JSDisconnectedException)
        {
            // Circuit torn down while initialising — nothing to clean up yet.
        }
    }

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        if (!_initialized || _disposed || _module is null)
        {
            return;
        }

        var dataChanged = !ReferenceEquals(Markers, _renderedMarkers)
            || !ReferenceEquals(ClusterPoints, _renderedClusters)
            || UseClientClustering != _renderedClientClustering;

        if (dataChanged)
        {
            await ApplyDataAsync(Markers, ClusterPoints, UseClientClustering);
        }
    }

    /// <summary>Moves the map to the given center and zoom without animation.</summary>
    /// <param name="viewport">Target center and zoom.</param>
    public async ValueTask SetViewAsync(MapViewport viewport)
    {
        ArgumentNullException.ThrowIfNull(viewport);

        if (_disposed || _module is null)
        {
            return;
        }

        try
        {
            await _module.InvokeVoidAsync("setView", _mapElementId, viewport.Latitude, viewport.Longitude, viewport.Zoom);
        }
        catch (JSDisconnectedException)
        {
        }
    }

    /// <summary>Reads the current map center and zoom. Returns the configured defaults before initialisation.</summary>
    public async ValueTask<MapViewport> GetViewportAsync()
    {
        if (_disposed || _module is null)
        {
            return new MapViewport(CenterLatitude, CenterLongitude, Zoom);
        }

        try
        {
            return await _module.InvokeAsync<MapViewport>("getViewport", _mapElementId);
        }
        catch (JSDisconnectedException)
        {
            return new MapViewport(CenterLatitude, CenterLongitude, Zoom);
        }
    }

    /// <summary>Removes all markers, cluster groups, and data layers from the map.</summary>
    public async ValueTask ClearMarkersAsync()
    {
        if (_disposed || _module is null)
        {
            return;
        }

        try
        {
            await _module.InvokeVoidAsync("clearMarkers", _mapElementId);
        }
        catch (JSDisconnectedException)
        {
        }
    }

    /// <summary>Atomically replaces all declarative data with the supplied marker batch (Canvas rendered).</summary>
    /// <param name="markers">Markers to render. Duplicate non-null ids are de-duplicated (first wins).</param>
    public async ValueTask SetMarkersAsync(IReadOnlyList<MapMarker> markers)
    {
        ArgumentNullException.ThrowIfNull(markers);
        await ApplyDataAsync(markers, null, UseClientClustering);
    }

    /// <summary>Atomically replaces all declarative data with the supplied server-side cluster points.</summary>
    /// <param name="points">Cluster points to render. A Count = 1 cluster renders as a plain marker.</param>
    public async ValueTask SetClusterPointsAsync(IReadOnlyList<MapClusterPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        await ApplyDataAsync(null, points, UseClientClustering);
    }

    /// <summary>Adds a batch of Canvas markers on top of the current content (does not replace existing data).</summary>
    /// <param name="markers">Markers to add.</param>
    public async ValueTask AddMarkersAsync(IReadOnlyList<MapMarker> markers)
    {
        ArgumentNullException.ThrowIfNull(markers);

        if (_disposed || _module is null || markers.Count == 0)
        {
            return;
        }

        try
        {
            await _module.InvokeVoidAsync("addMarkersBatch", _mapElementId, DeduplicateById(markers));
        }
        catch (JSDisconnectedException)
        {
        }
    }

    /// <summary>Creates an empty client-side cluster group (leaflet.markercluster) and returns its id.</summary>
    /// <returns>The cluster group id, or null when the map is not initialised.</returns>
    public async ValueTask<string?> AddClusterGroupAsync()
    {
        if (_disposed || _module is null)
        {
            return null;
        }

        try
        {
            return await _module.InvokeAsync<string?>("addClusterGroup", _mapElementId);
        }
        catch (JSDisconnectedException)
        {
            return null;
        }
    }

    /// <summary>Adds a single marker into a client-side cluster group created by <see cref="AddClusterGroupAsync"/>.</summary>
    /// <param name="clusterId">Cluster group id returned by <see cref="AddClusterGroupAsync"/>.</param>
    /// <param name="marker">Marker to add.</param>
    public ValueTask AddMarkerToClusterAsync(string clusterId, MapMarker marker)
    {
        ArgumentNullException.ThrowIfNull(marker);
        return AddMarkersToClusterAsync(clusterId, [marker]);
    }

    /// <summary>Adds a batch of markers into a client-side cluster group created by <see cref="AddClusterGroupAsync"/>.</summary>
    /// <param name="clusterId">Cluster group id returned by <see cref="AddClusterGroupAsync"/>.</param>
    /// <param name="markers">Markers to add.</param>
    public async ValueTask AddMarkersToClusterAsync(string clusterId, IReadOnlyList<MapMarker> markers)
    {
        ArgumentException.ThrowIfNullOrEmpty(clusterId);
        ArgumentNullException.ThrowIfNull(markers);

        if (_disposed || _module is null || markers.Count == 0)
        {
            return;
        }

        try
        {
            await _module.InvokeVoidAsync("addMarkersToCluster", _mapElementId, clusterId, DeduplicateById(markers));
        }
        catch (JSDisconnectedException)
        {
        }
    }

    /// <summary>
    /// Recalculates the map size. Call after the map container becomes visible again,
    /// e.g. when it lives in a previously hidden tab.
    /// </summary>
    public async ValueTask InvalidateSizeAsync()
    {
        if (_disposed || _module is null)
        {
            return;
        }

        try
        {
            await _module.InvokeVoidAsync("invalidateSize", _mapElementId);
        }
        catch (JSDisconnectedException)
        {
        }
    }

    /// <summary>Requests fresh data from <see cref="DataProvider"/> immediately (bypasses the debounce). No-op without a provider.</summary>
    public async ValueTask RefreshDataAsync()
    {
        if (DataProvider is null || _disposed)
        {
            return;
        }

        await LoadDataAsync();
    }

    /// <summary>JS callback: a marker was clicked. Raises <see cref="OnMarkerClick"/>.</summary>
    /// <param name="id">Host-defined marker id, or null for markers without one.</param>
    /// <param name="latitude">Marker latitude.</param>
    /// <param name="longitude">Marker longitude.</param>
    [JSInvokable]
    public Task HandleMarkerClick(string? id, double latitude, double longitude)
        => OnMarkerClick.InvokeAsync(new MapMarker(id, latitude, longitude));

    /// <summary>JS callback: a cluster was clicked. Raises <see cref="OnClusterClick"/>.</summary>
    /// <param name="key">Server-side cluster key, or null for client-side markercluster groups.</param>
    /// <param name="latitude">Cluster latitude.</param>
    /// <param name="longitude">Cluster longitude.</param>
    /// <param name="count">Number of aggregated points in the cluster.</param>
    [JSInvokable]
    public Task HandleClusterClick(string? key, double latitude, double longitude, int count)
        => OnClusterClick.InvokeAsync(new MapClusterPoint(key, latitude, longitude, count));

    /// <summary>JS callback: the zoom level changed. Raises <see cref="OnZoomChanged"/>.</summary>
    /// <param name="zoom">New zoom level.</param>
    /// <param name="latitude">Latitude of the map center after the zoom.</param>
    /// <param name="longitude">Longitude of the map center after the zoom.</param>
    [JSInvokable]
    public Task HandleZoomChanged(double zoom, double latitude, double longitude)
        => OnZoomChanged.InvokeAsync(new MapViewport(latitude, longitude, zoom));

    /// <summary>
    /// JS callback: the viewport settled after a pan or zoom. Schedules a debounced
    /// <see cref="DataProvider"/> request that supersedes any pending one.
    /// </summary>
    /// <param name="zoom">Current zoom level.</param>
    /// <param name="latitude">Latitude of the map center.</param>
    /// <param name="longitude">Longitude of the map center.</param>
    [JSInvokable]
    public Task HandleViewportChanged(double zoom, double latitude, double longitude)
    {
        _lastViewport = new MapViewport(latitude, longitude, zoom);

        if (DataProvider is not null && !_disposed)
        {
            ScheduleDebouncedLoad();
        }

        return Task.CompletedTask;
    }

    /// <summary>JS callback: the map background was clicked. Raises <see cref="OnMapClick"/>.</summary>
    /// <param name="latitude">Clicked latitude.</param>
    /// <param name="longitude">Clicked longitude.</param>
    /// <param name="zoom">Zoom level at the time of the click.</param>
    [JSInvokable]
    public Task HandleMapClick(double latitude, double longitude, double zoom)
        => OnMapClick.InvokeAsync(new MapViewport(latitude, longitude, zoom));

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _debounceCts?.Cancel();
        _loadCts?.Cancel();

        try
        {
            if (_module is not null)
            {
                await _module.InvokeVoidAsync("dispose", _mapElementId);
                await _module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
            // Circuit already gone — the browser cleaned the map up with the page.
        }
        catch (ObjectDisposedException)
        {
            // JS runtime disposed before the component.
        }
        finally
        {
            _module = null;
            _dotNetRef?.Dispose();
            _dotNetRef = null;
        }

        GC.SuppressFinalize(this);
    }

    private void ScheduleDebouncedLoad()
    {
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        Interlocked.Increment(ref _pendingDataRequests);
        _ = ReleasePendingRequestWhenSettledAsync(DebounceThenLoadAsync(_debounceCts.Token));
    }

    /// <summary>
    /// Tracks a debounce→load chain through cancellation, provider hangs and stale-result discards,
    /// so <see cref="PendingDataRequests"/> only reaches zero when NOTHING can still call the
    /// provider — the state the "no further request" tests wait on.
    /// </summary>
    private async Task ReleasePendingRequestWhenSettledAsync(Task chain)
    {
        try
        {
            await chain;
        }
        finally
        {
            Interlocked.Decrement(ref _pendingDataRequests);
        }
    }

    private async Task DebounceThenLoadAsync(CancellationToken debounceToken)
    {
        try
        {
            await Task.Delay(Math.Max(0, DataRequestDebounceMs), debounceToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (debounceToken.IsCancellationRequested || _disposed)
        {
            return;
        }

        await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        var provider = DataProvider;
        if (provider is null || _disposed)
        {
            return;
        }

        _loadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _loadCts = cts;
        var sequence = Interlocked.Increment(ref _loadSequence);
        var viewport = _lastViewport ?? new MapViewport(CenterLatitude, CenterLongitude, Zoom);

        try
        {
            var result = await provider.GetPointsAsync(viewport, cts.Token);

            if (_disposed || cts.Token.IsCancellationRequested || sequence != Volatile.Read(ref _loadSequence))
            {
                return; // superseded by a newer request — discard the stale result
            }

            await ApplyDataAsync(result.Markers, result.Clusters, UseClientClustering, applyWhenEmpty: true);
        }
        catch (OperationCanceledException)
        {
            // Superseded request — expected.
        }
        catch (Exception ex) when (ex is not JSDisconnectedException)
        {
            if (!_disposed && sequence == Volatile.Read(ref _loadSequence))
            {
                await InvokeAsync(() => OnDataError.InvokeAsync(ex));
            }
        }
    }

    private async ValueTask ApplyDataAsync(
        IReadOnlyList<MapMarker>? markers,
        IReadOnlyList<MapClusterPoint>? clusters,
        bool clientClustering,
        bool applyWhenEmpty = true)
    {
        _renderedMarkers = markers;
        _renderedClusters = clusters;
        _renderedClientClustering = clientClustering;

        if (_disposed || _module is null)
        {
            return;
        }

        var payload = BuildPayload(markers, clusters, clientClustering);
        if (!applyWhenEmpty && payload.Markers.Count == 0 && payload.Clusters.Count == 0)
        {
            return;
        }

        try
        {
            await _module.InvokeVoidAsync("setData", _mapElementId, payload);
        }
        catch (JSDisconnectedException)
        {
            // Circuit torn down mid-update — the browser side is already gone.
        }
    }

    private MapDataPayload BuildPayload(
        IReadOnlyList<MapMarker>? markers,
        IReadOnlyList<MapClusterPoint>? clusters,
        bool clientClustering)
    {
        IReadOnlyList<MapMarker> markerPayload = markers is { Count: > 0 }
            ? DeduplicateById(markers)
            : [];

        IReadOnlyList<MapClusterPayloadItem> clusterPayload = clusters is { Count: > 0 }
            ? clusters.Select(c => new MapClusterPayloadItem(
                c.Key,
                c.Latitude,
                c.Longitude,
                c.Count,
                c.Count.ToString("N0", CultureInfo.CurrentCulture),
                Loc["TmMap_ClusterAriaLabel", c.Count],
                c.Tooltip)).ToList()
            : [];

        return new MapDataPayload(markerPayload, clusterPayload, clientClustering);
    }

    private static List<MapMarker> DeduplicateById(IReadOnlyList<MapMarker> markers)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<MapMarker>(markers.Count);
        foreach (var marker in markers)
        {
            if (marker.Id is null || seen.Add(marker.Id))
            {
                result.Add(marker);
            }
        }

        return result;
    }

    private sealed record MapInitOptions(string TileUrlTemplate, int MaxZoom, string Attribution);
}
