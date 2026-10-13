using Microsoft.JSInterop;

namespace Tempo.Blazor.Localization;

/// <summary>
/// The scoped default <see cref="ITmUserTimeZone"/>. On WebAssembly the runtime's local zone is the
/// browser's zone (requires <c>InvariantTimezone</c> to stay false, the default), so the zone is
/// resolved immediately. On Blazor Server (and InteractiveAuto's Server circuits) the zone is UTC
/// until <see cref="EnsureResolvedAsync"/> reads <c>Intl.DateTimeFormat().resolvedOptions().timeZone</c>
/// once per circuit — after the first interactive render, when JS interop is available; an unknown
/// IANA id falls back to UTC.
/// </summary>
internal sealed class TmUserTimeZone : ITmUserTimeZone
{
    internal const string ModulePath = "./_content/Tempo.Blazor/js/tm-user-zone.js";

    private readonly IJSRuntime _js;
    private readonly bool _isBrowser;
    private bool _probed;
    private bool _hostOverride;
    private TimeZoneInfo _zone;

    public TmUserTimeZone(IJSRuntime js)
        : this(js, OperatingSystem.IsBrowser())
    {
    }

    internal TmUserTimeZone(IJSRuntime js, bool isBrowser)
    {
        ArgumentNullException.ThrowIfNull(js);
        _js = js;
        _isBrowser = isBrowser;
        if (isBrowser)
        {
            _zone = TimeZoneInfo.Local;
            IsResolved = true;
        }
        else
        {
            _zone = TimeZoneInfo.Utc;
        }
    }

    /// <inheritdoc />
    public TimeZoneInfo Zone => _zone;

    /// <inheritdoc />
    public bool IsResolved { get; private set; }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public void SetZone(TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        _hostOverride = true;
        Apply(zone);
    }

    /// <summary>
    /// Reads the browser zone once per circuit. Call from <c>OnAfterRenderAsync(firstRender: true)</c>;
    /// before the first interactive render (prerender) JS interop is unavailable and the zone stays
    /// UTC/unresolved. A host override from <see cref="SetZone"/> skips the probe entirely.
    /// Never throws: the zone is best-effort and must not fault a circuit.
    /// </summary>
    internal async Task EnsureResolvedAsync()
    {
        if (_isBrowser || _probed || _hostOverride)
        {
            return;
        }

        _probed = true;

        string? iana = null;
        IJSObjectReference? module = null;
        try
        {
            module = await _js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            if (module is not null)
            {
                iana = await module.InvokeAsync<string?>("resolve");
            }
        }
        catch (InvalidOperationException)
        {
            // Prerender (or a circuit that went away): JS interop is unavailable, stay UTC/unresolved.
            return;
        }
        catch (JSException)
        {
            // The module failed in the browser: a zone is best-effort, never a render error.
            return;
        }
        catch (JSDisconnectedException)
        {
            // The circuit disconnected mid-probe: stay UTC/unresolved.
            return;
        }
        catch (OperationCanceledException)
        {
            // The circuit is shutting down or the navigation was aborted: stay UTC/unresolved.
            return;
        }
        finally
        {
            if (module is not null)
            {
                try
                {
                    await module.DisposeAsync();
                }
                catch (JSDisconnectedException)
                {
                    // The circuit is gone; the module reference dies with it.
                }
            }
        }

        if (iana is null)
        {
            return;
        }

        if (_hostOverride)
        {
            // SetZone landed while the probe was in flight: the host override wins.
            return;
        }

        // IANA ids resolve through ICU on .NET 8+ (Windows included); an unknown id falls back to UTC.
        Apply(TimeZoneInfo.TryFindSystemTimeZoneById(iana, out var zone) ? zone : TimeZoneInfo.Utc);
    }

    private void Apply(TimeZoneInfo zone)
    {
        if (IsResolved && zone.Equals(_zone))
        {
            return;
        }

        _zone = zone;
        IsResolved = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
