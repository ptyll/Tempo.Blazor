using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Tempo.Blazor.Helpers;

/// <summary>
/// Per-instance wrapper around the shared <c>tm-focus-trap</c> ES module. Lazily imports the
/// module on first activation (safe to construct during prerender — no interop happens until
/// <see cref="ActivateAsync{T}"/> is called from <c>OnAfterRenderAsync</c>), traps Tab focus
/// inside an overlay, restores focus to the trigger on close, and optionally routes a
/// document-level Escape key back to the owning component.
/// </summary>
/// <remarks>
/// Used by TmModal, TmDialog and TmDrawer so all three share one focus-trap implementation.
/// All interop is guarded: when JS is unavailable (bUnit / prerender / disconnected circuit)
/// activation degrades to a best-effort <see cref="ElementReference.FocusAsync()"/> and never
/// throws into the render loop.
/// </remarks>
internal sealed class FocusTrap : IAsyncDisposable
{
    private const string ModulePath = "./_content/Tempo.Blazor/js/tm-focus-trap.js";

    private readonly IJSRuntime _js;
    private readonly string _id = Guid.NewGuid().ToString("N");
    private IJSObjectReference? _module;
    private bool _active;

    public FocusTrap(IJSRuntime js) => _js = js;

    /// <summary>
    /// Activates the trap on <paramref name="element"/>. When <paramref name="closeOnEscape"/> is
    /// true and <paramref name="escapeHandler"/> is supplied, a document-level Escape listener
    /// invokes the component's <c>[JSInvokable] HandleFocusTrapEscapeAsync</c>.
    /// </summary>
    public async Task ActivateAsync<T>(
        ElementReference element,
        DotNetObjectReference<T>? escapeHandler = null,
        bool closeOnEscape = false,
        ElementReference? restoreTarget = null,
        bool modal = true) where T : class
    {
        try
        {
            _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            if (_module is null)
            {
                // JS unavailable (bUnit loose interop / prerender) — best-effort focus.
                await FallbackFocusAsync(element);
                return;
            }
            await _module.InvokeVoidAsync("activate", element, _id, escapeHandler, closeOnEscape, restoreTarget, modal);
            _active = true;
        }
        catch (JSException) { await FallbackFocusAsync(element); }
        catch (InvalidOperationException) { await FallbackFocusAsync(element); }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
        catch (NullReferenceException) { await FallbackFocusAsync(element); }
    }

    /// <summary>Deactivates the trap and restores focus to the previously-focused element.</summary>
    public async Task DeactivateAsync()
    {
        if (!_active) return;
        _active = false;
        if (_module is null) return;
        try
        {
            await _module.InvokeVoidAsync("deactivate", _id);
        }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
        catch (InvalidOperationException) { }
    }

    private static async Task FallbackFocusAsync(ElementReference element)
    {
        try { await element.FocusAsync(); } catch { /* JS unavailable — best effort */ }
    }

    public async ValueTask DisposeAsync()
    {
        // The scope that owns this trap is disposed both by its host and by the renderer.
        if (_disposed) return;
        _disposed = true;

        await DeactivateAsync();
        if (_module is null) return;
        try
        {
            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
    }

    private bool _disposed;
}
