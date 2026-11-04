using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// The .NET half of the package's <c>tm-agent-run-tree.js</c> module, which owns the one thing a
/// <c>TmAgentRunTree</c> cannot express in Blazor: cancelling the browser's scroll default for the
/// navigation keys its rows handle (←/→/↑/↓, Home/End, Space).
/// </summary>
/// <remarks>
/// <para>
/// The module is imported through <see cref="IJSObjectReference"/> (no <c>&lt;script&gt;</c> tag, no eval)
/// and every call is guarded: where JavaScript is unavailable (bUnit, prerender, a disconnected circuit) the
/// component keeps its keyboard behaviour and only the scroll default stays — never a throw into the render
/// loop.
/// </para>
/// <para>
/// The guard is attached to the GRID rather than to each row, so a filter, a collapse or a live tick that
/// replaces the row set never has to move a listener.
/// </para>
/// </remarks>
internal sealed class AgentRunTreeInterop : IAsyncDisposable
{
    private const string ModulePath = "./_content/Tempo.Blazor.AI/js/tm-agent-run-tree.js";
    private const string RowSelector = ".tm-ai-agent-run-tree__row";

    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;
    private ElementReference? _grid;
    private bool _disposed;

    /// <summary>Creates the interop over the runtime the component was rendered with.</summary>
    /// <param name="js">The runtime to import the module from.</param>
    public AgentRunTreeInterop(IJSRuntime js) => _js = js;

    /// <summary>
    /// Attaches the scroll-key guard to the grid. Idempotent per element: a render that keeps the same grid
    /// does not re-enter JavaScript, while a grid the empty state removed and re-created does get a guard.
    /// </summary>
    /// <param name="grid">The element carrying <c>role="treegrid"</c>.</param>
    public async Task AttachAsync(ElementReference grid)
    {
        if (_disposed)
        {
            return;
        }

        _grid = grid;

        try
        {
            _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            if (_module is not null)
            {
                await _module.InvokeVoidAsync("attachScrollKeyGuard", grid, RowSelector);
            }
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            // No JavaScript (bUnit, prerender, a dropped circuit): the keys still work, only the scroll
            // default stays.
            _module = null;
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
        var module = _module;
        _module = null;

        if (module is null)
        {
            return;
        }

        try
        {
            if (_grid is { } grid)
            {
                await module.InvokeVoidAsync("detachScrollKeyGuard", grid);
            }
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            // The element or the circuit is already gone: there is nothing left to detach.
        }
        finally
        {
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
}
