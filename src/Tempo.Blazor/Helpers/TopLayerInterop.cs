using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Tempo.Blazor.Helpers;

/// <summary>
/// The shared promote helper for every modal viewport-anchored overlay root (F3 review round 2,
/// U1): a modal <c>TmDrawer</c> at any position, the <c>TmModal</c>/<c>TmDialog</c> overlay root
/// and the <c>TmToastContainer</c>. Each root carries <c>popover="manual"</c> and is promoted to
/// the browser top layer after its opening render, so the top-layer order equals the open order
/// and no promoted surface can cover one opened later from inside it. The DOM stays in place —
/// the trap, inert and Escape are unchanged. Wraps the shared <c>tm-top-layer</c> module, imported
/// lazily on first use (safe to construct during prerender).
/// </summary>
/// <remarks>
/// Every interop is guarded: where JS is unavailable the surface renders exactly as before,
/// fixed-positioned in its z-index band. Roots that unmount on close (drawer, modal, dialog) only
/// promote; roots that stay in the DOM (the toast container) raise on each push and demote when
/// empty. The helpers live in the <c>tm-top-layer</c> module since F6; <c>tm-sheet.js</c>
/// re-exports them for hosts that still import them from there.
/// </remarks>
internal sealed class TopLayerInterop : IAsyncDisposable
{
    private const string ModulePath = "./_content/Tempo.Blazor/js/tm-top-layer.js";

    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;
    private bool _disposed;

    public TopLayerInterop(IJSRuntime js) => _js = js;

    /// <summary>
    /// Promotes the root to the browser top layer. A no-op when it is already promoted, the
    /// Popover API is missing or JS is unavailable.
    /// </summary>
    public Task PromoteAsync(ElementReference root) => InvokeAsync("promote", root);

    /// <summary>
    /// Re-raises the root above everything promoted after it (hidePopover + showPopover in one
    /// synchronous step). Used by the toast container on each push.
    /// </summary>
    public Task RaiseAsync(ElementReference root) => InvokeAsync("raise", root);

    /// <summary>Demotes the root (hidePopover). No-op when it is not currently promoted.</summary>
    public Task DemoteAsync(ElementReference root) => InvokeAsync("demote", root);

    /// <summary>
    /// Registers the root as pinned on top: <c>promote</c>/<c>raise</c> re-raise every registered
    /// pinned root that is still popover-open, so a toast container holding toasts stays above
    /// every modal surface promoted after it (F3 review round 3, R3-M1).
    /// </summary>
    public Task PinAsync(ElementReference root) => InvokeAsync("pinRoot", root);

    /// <summary>Removes the root from the pinned registry (demote on empty, dispose).</summary>
    public Task UnpinAsync(ElementReference root) => InvokeAsync("unpinRoot", root);

    private async Task InvokeAsync(string function, ElementReference root)
    {
        if (_disposed) return;
        try
        {
            var module = _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            if (_disposed || module is null) return;
            await module.InvokeVoidAsync(function, root);
        }
        catch (JSException) { }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        var module = _module;
        _module = null;
        if (module is null) return;
        try
        {
            await module.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
        catch (ObjectDisposedException) { }
    }
}
