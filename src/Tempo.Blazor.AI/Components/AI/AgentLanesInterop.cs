using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// The .NET half of the package's <c>tm-agent-lanes.js</c> module, which owns the one thing a <c>TmAgentLanes</c>
/// cannot express in Blazor: the browser's ctrl+wheel / pinch gesture that produces a zoom scale, and the scrollport
/// measurement "fit to width" needs.
/// </summary>
/// <remarks>
/// <para>
/// The module is imported through <see cref="IJSObjectReference"/> (no <c>&lt;script&gt;</c> tag, no eval) and it does
/// NOT re-implement the gesture: it imports the core <c>pointer-gesture.js</c> helper across the static-asset boundary,
/// so a lane surface and the PDF viewer pinch the same way (see docs/touch-interaction.md).
/// </para>
/// <para>
/// Every call is guarded: where JavaScript is unavailable (bUnit, prerender, a disconnected circuit) the component
/// keeps its zoom BUTTONS — WCAG 2.5.7's non-drag alternative — and only the gesture is missing, never a throw into
/// the render loop.
/// </para>
/// </remarks>
internal sealed class AgentLanesInterop : IAsyncDisposable
{
    private const string ModulePath = "./_content/Tempo.Blazor.AI/js/tm-agent-lanes.js";

    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;
    private IJSObjectReference? _handle;
    private ElementReference? _viewport;
    private bool _disposed;

    /// <summary>Creates the interop over the runtime the component was rendered with.</summary>
    /// <param name="js">The runtime to import the module from.</param>
    public AgentLanesInterop(IJSRuntime js) => _js = js;

    /// <summary>
    /// Attaches the zoom gesture to the timeline scrollport. Idempotent per element: a render that keeps the same
    /// scrollport does not re-enter JavaScript, while a scrollport the timeline view re-created does get a gesture.
    /// </summary>
    /// <param name="viewport">The horizontal scrollport of the timeline.</param>
    /// <param name="self">The component's .NET reference the module reports the gesture to.</param>
    /// <returns>A task that completes when the gesture is attached (or the attempt has been swallowed).</returns>
    public async Task AttachAsync(ElementReference viewport, DotNetObjectReference<TmAgentLanes>? self)
    {
        if (_disposed || self is null)
        {
            return;
        }

        _viewport = viewport;

        try
        {
            _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            if (_module is null)
            {
                return;
            }

            await DetachGestureAsync();
            _handle = await _module.InvokeAsync<IJSObjectReference?>("attachLanesZoom", viewport, self);
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            // No JavaScript: the buttons still zoom, so only the gesture is missing.
            _module = null;
            _handle = null;
        }
    }

    /// <summary>
    /// Measures the scrollport so "fit to width" can derive a scale from the room the reader actually has.
    /// </summary>
    /// <param name="viewport">The horizontal scrollport of the timeline.</param>
    /// <returns>The measured width in pixels, or 0 where nothing can be measured (the caller keeps its zoom then).</returns>
    public async Task<double> MeasureWidthAsync(ElementReference viewport)
    {
        if (_disposed)
        {
            return 0;
        }

        try
        {
            _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            return _module is null ? 0 : await _module.InvokeAsync<double>("measureWidth", viewport);
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            return 0;
        }
    }

    /// <summary>The element the gesture is currently attached to, so the component can skip a redundant attach.</summary>
    public ElementReference? Viewport => _viewport;

    private static bool IsInteropFailure(Exception exception)
        => exception is JSException or JSDisconnectedException or InvalidOperationException
            or NotSupportedException or ObjectDisposedException or ArgumentException;

    private async Task DetachGestureAsync()
    {
        var handle = _handle;
        _handle = null;
        if (handle is null)
        {
            return;
        }

        try
        {
            await handle.InvokeVoidAsync("detach");
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            // The element or the circuit is already gone: there is nothing left to detach.
        }
        finally
        {
            try
            {
                await handle.DisposeAsync();
            }
            catch (Exception exception) when (IsInteropFailure(exception))
            {
                // A disposed runtime owns the handle.
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await DetachGestureAsync();

        var module = _module;
        _module = null;
        if (module is null)
        {
            return;
        }

        try
        {
            await module.DisposeAsync();
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            // A disposed runtime owns the module; releasing it again would throw.
        }
    }
}
