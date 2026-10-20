using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Tempo.Blazor.Helpers;

/// <summary>
/// Promotes a modal overlay root to the browser top layer and only THEN reports the focus scope
/// active, so the trap's initial-focus move can never run against a popover root that is still
/// <c>display:none</c> — the UA rule <c>[popover]:not(:popover-open) { display: none }</c> beats
/// every stylesheet the root's own <c>display</c> used to hide behind, so a consumer class or a
/// future CSS edit turns the "early activation" race into lost focus on <c>&lt;body&gt;</c>
/// (F3 follow-up c51ddf81 #1). This is the <c>TmGanttImportDialog</c> / <c>TmOverlayPanel
/// TrapFocus</c> pattern, shared: promote first, re-check the open flag after the await
/// (c51ddf81 #2), and only then flip <see cref="ScopeActive"/>.
/// </summary>
/// <remarks>
/// Bind the scope's <c>Active</c> to <c>_gate?.ScopeActive == true</c> and call
/// <see cref="AfterRenderAsync"/> from <c>OnAfterRenderAsync</c> on every render. A promote that
/// completes after its owner closed (or was re-opened) is dropped by the generation check, so a
/// stale activation can never ride into the next open.
/// </remarks>
internal sealed class PromotedScopeGate : IAsyncDisposable
{
    private readonly TopLayerInterop _topLayer;
    private bool _promoted;
    private bool _disposed;
    private int _generation;

    public PromotedScopeGate(IJSRuntime js) => _topLayer = new TopLayerInterop(js);

    /// <summary>Bind <c>TmFocusScope.Active</c> to this.</summary>
    public bool ScopeActive { get; private set; }

    /// <summary>
    /// Call from <c>OnAfterRenderAsync</c> on EVERY render. Returns true when
    /// <see cref="ScopeActive"/> just turned true; the caller must then call
    /// <c>StateHasChanged()</c> so the scope re-renders active.
    /// </summary>
    /// <param name="isOpen">The component's open flag as it was for this render.</param>
    /// <param name="stillOpen">
    /// Re-reads the open flag after the promote await — the guard that drops a promote that
    /// finished after its owner closed (c51ddf81 #2).
    /// </param>
    /// <param name="root">
    /// The <c>popover="manual"</c> root; its id is still unset while the element has not rendered.
    /// </param>
    public async Task<bool> AfterRenderAsync(bool isOpen, Func<bool> stillOpen, ElementReference? root)
    {
        if (_disposed) return false;
        if (!isOpen)
        {
            if (_promoted || ScopeActive)
            {
                _promoted = false;
                ScopeActive = false;
                _generation++;
            }

            return false;
        }

        if (_promoted || root is null || root.Value.Id is null) return false;
        _promoted = true;
        var generation = ++_generation;
        await _topLayer.PromoteAsync(root.Value); // guarded: a no-op where JS/Popover API is missing
        if (_disposed) return false;
        if (generation != _generation || !stillOpen()) return false; // closed during the await
        ScopeActive = true;
        return true;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return _topLayer.DisposeAsync();
    }
}
