using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Tempo.Blazor.Helpers;

namespace Tempo.Blazor.Services;

/// <summary>
/// Pins host elements above every Tempo top-layer surface — <c>TmModal</c>, <c>TmDialog</c>, a
/// modal <c>TmDrawer</c>, sheets, <c>TmOverlayPanel</c> popovers, the command palette — including
/// surfaces opened AFTER the element appeared. The C# front door of the one pinned-on-top registry
/// the toast containers use (decision <c>TOP-LAYER-PIN</c>, docs/top-layer-pin.md).
/// </summary>
/// <remarks>
/// <para>
/// Registered scoped by <c>AddTempoBlazor()</c> (one per circuit / WebAssembly app). Call
/// <see cref="PinAsync"/> from <c>OnAfterRenderAsync</c> — an element that has not rendered yet
/// cannot be pinned — and dispose the returned <see cref="TopLayerPin"/> when the element goes away.
/// </para>
/// <para>
/// VISIBILITY IS THE SIGNAL: keep hiding and showing the element the way you already do (a class,
/// inline <c>display</c>, the <c>hidden</c> attribute); the module promotes it while it is visible
/// and hands it back as a plain element while it is hidden. Do NOT put <c>popover</c> on it — the
/// module owns that attribute. While a native modal <c>&lt;dialog&gt;</c> is open nothing is
/// promoted above it; the pin lands when the dialog closes. Pinned elements are exempt from a
/// Tempo modal trap's inert background, so render them as layout-level siblings (outside the
/// <c>@Body</c> wrapper). Add the <c>tm-pin-on-top</c> class to reset the browser's popover
/// defaults (a bottom bar stays a bar).
/// </para>
/// <para>
/// Every interop is guarded: during prerender, without JS, without the Popover API, after the
/// circuit is gone or after disposal the calls are no-ops and the handle reports
/// <see cref="TopLayerPin.IsActive"/> <c>false</c>.
/// </para>
/// </remarks>
public sealed class TopLayerPinService : IAsyncDisposable, IDisposable
{
    private const string ModulePath = "./_content/Tempo.Blazor/js/tm-pin-on-top.js";

    private readonly LazyJsModule? _module;
    private bool _disposed;

    /// <summary>Creates the service. No interop happens here, so it is safe during prerender.</summary>
    /// <param name="js">
    /// The JS runtime of the current circuit or app. Null in a host without Blazor (an API, a
    /// worker), where every pin is inactive.
    /// </param>
    public TopLayerPinService(IJSRuntime? js)
    {
        _module = js is null ? null : new LazyJsModule(js, ModulePath);
    }

    /// <summary>
    /// Pins <paramref name="element"/> on top. Disposing the returned handle unpins it. Call from
    /// <c>OnAfterRenderAsync</c>; an element reference that has not rendered yet yields an inactive
    /// handle.
    /// </summary>
    /// <param name="element">The element to keep above every Tempo top-layer surface.</param>
    /// <param name="options">Order between pins (<see cref="TopLayerPinOptions.Priority"/>); null pins at priority 0.</param>
    /// <returns>
    /// The pin. <see cref="TopLayerPin.IsActive"/> is <c>false</c> when the pin could not be
    /// registered (no JS, prerender, an unrendered element, a disposed service).
    /// </returns>
    public async Task<TopLayerPin> PinAsync(ElementReference element, TopLayerPinOptions? options = null)
    {
        options ??= new TopLayerPinOptions();
        if (_disposed || _module is null || string.IsNullOrEmpty(element.Id))
            return new TopLayerPin(this, element, options.Priority, active: false);

        var ok = await _module.TryInvokeVoidAsync("pin", element, options);
        // When the scope went away while the call was in flight, the handle still reports inactive:
        // TopLayerPin.IsActive reads the owner's disposal, and UnpinAsync is a no-op on a disposed
        // service (the module is released, and the page that held the element went with it).
        return new TopLayerPin(this, element, options.Priority, active: ok);
    }

    internal bool IsDisposed => _disposed;

    internal async Task<bool> RePinAsync(ElementReference element, int priority)
    {
        if (_disposed || _module is null) return false;
        return await _module.TryInvokeVoidAsync("pin", element, new TopLayerPinOptions { Priority = priority });
    }

    internal async Task UnpinAsync(ElementReference element)
    {
        if (_disposed || _module is null) return;
        await _module.TryInvokeVoidAsync("unpin", element);
    }

    /// <summary>Releases the JS module. Pins created by this service become inactive.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (_module is not null)
            await _module.DisposeAsync();
    }

    /// <summary>
    /// Synchronous dispose for a container that disposes its scope synchronously. A scoped service
    /// that implements only <see cref="IAsyncDisposable"/> makes such a <c>Dispose()</c> throw. The
    /// module release is started here without awaiting it, and every failure of it is swallowed.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_module is not null)
            _ = ReleaseQuietlyAsync(_module);
    }

    private static async Task ReleaseQuietlyAsync(LazyJsModule module)
    {
        try { await module.DisposeAsync(); }
        catch (Exception) { /* a torn-down circuit: nothing left to release */ }
    }
}

/// <summary>
/// One pin created by <see cref="TopLayerPinService.PinAsync"/>. Dispose it to unpin; the dispose
/// is idempotent and never throws (also during prerender or after the circuit is gone).
/// </summary>
public sealed class TopLayerPin : IAsyncDisposable
{
    private readonly TopLayerPinService _owner;
    private readonly ElementReference _element;
    private bool _active;
    private bool _disposed;

    internal TopLayerPin(TopLayerPinService owner, ElementReference element, int priority, bool active)
    {
        _owner = owner;
        _element = element;
        Priority = priority;
        _active = active;
    }

    /// <summary>
    /// <c>true</c> while the element is registered in the pinned-on-top registry. <c>false</c> when JS
    /// or the module was unavailable, the element had not rendered, the service was disposed, or after
    /// <see cref="DisposeAsync"/>. (Without the Popover API the module registers nothing, but the
    /// call itself succeeds, so the handle stays active and its calls stay no-ops.)
    /// </summary>
    public bool IsActive => _active && !_disposed && !_owner.IsDisposed;

    /// <summary>The priority the pin was last registered with. Higher paints higher; 0 is the toast band.</summary>
    public int Priority { get; private set; }

    /// <summary>
    /// Changes the order of this pin relative to the other pins (higher paints higher). A no-op
    /// once the pin is inactive.
    /// </summary>
    /// <param name="priority">The new priority.</param>
    public async Task SetPriorityAsync(int priority)
    {
        if (!IsActive || priority == Priority) return;
        Priority = priority;
        _active = await _owner.RePinAsync(_element, priority);
    }

    /// <summary>Unpins the element (idempotent, exception-safe).</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        var wasActive = IsActive;
        _disposed = true;
        _active = false;
        if (wasActive)
            await _owner.UnpinAsync(_element);
    }
}

/// <summary>Options of a pin created by <see cref="TopLayerPinService.PinAsync"/>.</summary>
public sealed record TopLayerPinOptions
{
    /// <summary>
    /// Order between pins: a higher value paints higher; equal values keep registration order.
    /// Defaults to 0, the band of the toast containers. The docs recommend 100 for a framework error
    /// bar, so it lands above toasts.
    /// </summary>
    public int Priority { get; init; }
}
