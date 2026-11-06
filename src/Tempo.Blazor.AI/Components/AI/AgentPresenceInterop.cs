using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// The .NET half of the package's <c>tm-agent-presence.js</c> module: the one thing a <c>TmAgentPresenceOverlay</c>
/// cannot express in Blazor — where an element actually sits, measured from the real DOM.
/// </summary>
/// <remarks>
/// <para>
/// The module is imported through <see cref="IJSObjectReference"/> (no <c>&lt;script&gt;</c> tag, no eval) and it writes
/// the measured offsets straight into two CSS custom properties, so a cursor that moves never re-renders the component
/// (or the host's content) — on a Server circuit that would be a message per keystroke of a collaborator.
/// </para>
/// <para>
/// Every call is guarded: where JavaScript is unavailable (bUnit, prerender, a disconnected circuit) the layer still
/// renders its markers at the host's own fractions and the roster still names everyone present; only the MEASURED
/// position of an anchored marker is missing, never a throw into the render loop.
/// </para>
/// </remarks>
internal sealed class AgentPresenceInterop : IAsyncDisposable
{
    private const string ModulePath = "./_content/Tempo.Blazor.AI/js/tm-agent-presence.js";

    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;
    private IJSObjectReference? _handle;
    private bool _disposed;

    /// <summary>Creates the interop over the runtime the component was rendered with.</summary>
    /// <param name="js">The runtime to import the module from.</param>
    public AgentPresenceInterop(IJSRuntime js) => _js = js;

    /// <summary>Whether the module is attached to a rendered layer (so a re-render can ask for a refresh).</summary>
    public bool IsAttached => _handle is not null;

    /// <summary>
    /// Attaches the measurement to a rendered overlay. Idempotent per render tree: while a handle is alive the call is a
    /// no-op, so the component never re-imports on every paint.
    /// </summary>
    /// <param name="root">The overlay's root element (the markers live inside it).</param>
    /// <param name="content">The element whose anchors are measured, and the origin every marker is placed against.</param>
    /// <returns>A task that completes when the measurement is attached (or the attempt has been swallowed).</returns>
    public async Task AttachAsync(ElementReference root, ElementReference content)
    {
        if (_disposed || _handle is not null)
        {
            return;
        }

        try
        {
            _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            if (_module is null)
            {
                return;
            }

            _handle = await _module.InvokeAsync<IJSObjectReference?>("attachPresence", root, content);
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            // No JavaScript: the anchored markers stay hidden (their real position is unknown) and every other cue —
            // the host's own fractions, the labels and the roster — still renders.
            _module = null;
            _handle = null;
        }
    }

    /// <summary>
    /// Asks the module to re-measure after a render that may have moved an anchor. The module throttles the work, so a
    /// fast sequence of paints costs one measurement.
    /// </summary>
    /// <returns>A task that completes when the module accepted the request.</returns>
    public async Task RefreshAsync()
    {
        var handle = _handle;
        if (_disposed || handle is null)
        {
            return;
        }

        try
        {
            await handle.InvokeVoidAsync("refresh");
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            // The layer or the circuit is gone: there is nothing left to re-measure.
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
        await DetachAsync();

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

    private static bool IsInteropFailure(Exception exception)
        => exception is JSException or JSDisconnectedException or InvalidOperationException
            or NotSupportedException or ObjectDisposedException or ArgumentException;

    private async Task DetachAsync()
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
}
