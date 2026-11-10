using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// The .NET half of the ONE thing <see cref="TmScheduledRuns"/> cannot express in Blazor: taking the Alt+arrow chord
/// away from the browser (it is the Back/Forward history shortcut) and delivering it to the component even though the
/// focused element sits inside a menu trigger whose content fences Blazor's key dispatch.
/// </summary>
/// <remarks>
/// <para>
/// The package module <c>_content/Tempo.Blazor.AI/js/tm-scheduled-runs.js</c> owns the conditional part and nothing else:
/// it wraps the SHARED core helper <c>attachKeyboardMove</c> (from <c>_content/Tempo.Blazor/js/pointer-drag.js</c> — never
/// a second keyboard implementation), which cancels exactly the Alt+arrow chord on the row that declares it owns the keys
/// (<c>[data-tm-run-move-keys]</c>), calls <c>preventDefault</c> and never <c>stopPropagation</c>. The module then resolves
/// the focused row's <c>data-run-id</c> and reports ONE <c>OnRunMoveStep(runId, key)</c>; the component owns the move, the
/// refusal and the single announcement.
/// </para>
/// <para>
/// Every call is guarded: where JavaScript is unavailable (bUnit, prerender, a disconnected circuit) the chord is
/// unavailable; the row's move menu (which lists every window) remains the path.
/// </para>
/// </remarks>
internal sealed class ScheduledRunsInterop : IAsyncDisposable
{
    private const string ModulePath = "./_content/Tempo.Blazor.AI/js/tm-scheduled-runs.js";

    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;
    private IJSObjectReference? _handle;
    private ElementReference? _root;
    private bool _disposed;

    /// <summary>Creates the interop over the runtime the surface was rendered with.</summary>
    /// <param name="js">The runtime to import the helper from.</param>
    public ScheduledRunsInterop(IJSRuntime js) => _js = js;

    /// <summary>
    /// Arms the Alt+arrow guard on the surface root. Idempotent per element: a render that keeps the same root does not
    /// enter JavaScript again, while a root the surface re-created does get its own guard.
    /// </summary>
    /// <param name="root">The surface's own element (the chord is resolved from the row inside it).</param>
    /// <param name="self">The component's .NET reference the module reports a step to.</param>
    /// <returns>A task that completes when the guard is armed (or the attempt has been swallowed).</returns>
    public async Task AttachAsync(ElementReference root, DotNetObjectReference<TmScheduledRuns>? self)
    {
        if (_disposed || root.Id is null || self is null)
        {
            return;
        }

        if (_root is not null && string.Equals(_root.Value.Id, root.Id, StringComparison.Ordinal))
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

            await DetachAsync();
            _handle = await _module.InvokeAsync<IJSObjectReference?>("attachMoveKeys", root, self);
            _root = root;
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            // Without JavaScript the chord is unavailable; the row's move menu (which lists every window) remains
            // the path.
            _handle = null;
            _root = null;
        }
    }

    private async Task DetachAsync()
    {
        var handle = _handle;
        _handle = null;
        _root = null;
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

        try
        {
            await handle.DisposeAsync();
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            // A disposed runtime owns the reference; releasing it again would throw.
        }
    }

    private static bool IsInteropFailure(Exception exception)
        => exception is JSException or JSDisconnectedException or InvalidOperationException
            or NotSupportedException or ObjectDisposedException or ArgumentException;

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
}
