using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// The .NET half of the package's <c>tm-autonomy-dial.js</c> module, which owns the one thing
/// <see cref="TmAutonomyDial"/> cannot express in Blazor: telling the browser NOT to scroll the page when the reader
/// presses one of the keys that belong to the slider.
/// </summary>
/// <remarks>
/// <para>
/// Blazor's <c>@onkeydown:preventDefault</c> is unconditional per element, so it would swallow Tab, Escape and every other
/// key the surface must keep; the module therefore owns the conditional part and calls <c>preventDefault</c> for the four
/// arrows and Home/End only. It never stops propagation, so the component's own keyboard model keeps receiving the same
/// event.
/// </para>
/// <para>
/// The module is imported through <see cref="IJSObjectReference"/> (no <c>&lt;script&gt;</c> tag, no eval). Every call is
/// guarded: where JavaScript is unavailable (bUnit, prerender, a disconnected circuit) the dial keeps every key it
/// handles — only the page may scroll along with it — so a failed interop never surfaces as a throw in the render loop.
/// </para>
/// </remarks>
internal sealed class AutonomyDialInterop : IAsyncDisposable
{
    private const string ModulePath = "./_content/Tempo.Blazor.AI/js/tm-autonomy-dial.js";

    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;
    private ElementReference? _slider;
    private bool _disposed;

    /// <summary>Creates the interop over the runtime the component was rendered with.</summary>
    /// <param name="js">The runtime to import the module from.</param>
    public AutonomyDialInterop(IJSRuntime js) => _js = js;

    /// <summary>
    /// Arms the scroll guard on the slider. Idempotent per element: a render that keeps the same slider does not enter
    /// JavaScript again, while a slider the surface re-created does get its own guard.
    /// </summary>
    /// <param name="slider">The rendered <c>role="slider"</c> element.</param>
    /// <returns>A task that completes when the guard is armed (or the attempt has been swallowed).</returns>
    public async Task AttachAsync(ElementReference slider)
    {
        if (_disposed || slider.Id is null)
        {
            return;
        }

        if (_slider is not null && string.Equals(_slider.Value.Id, slider.Id, StringComparison.Ordinal))
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
            await _module.InvokeVoidAsync("attachDialKeys", slider);
            _slider = slider;
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            // No JavaScript: every key still moves the dial, so only the scroll guard is missing.
            _module = null;
            _slider = null;
        }
    }

    private static bool IsInteropFailure(Exception exception)
        => exception is JSException or JSDisconnectedException or InvalidOperationException
            or NotSupportedException or ObjectDisposedException or ArgumentException;

    private async Task DetachAsync()
    {
        var slider = _slider;
        _slider = null;
        var module = _module;
        if (slider is null || module is null)
        {
            return;
        }

        try
        {
            await module.InvokeVoidAsync("detachDialKeys", slider.Value);
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            // The element or the circuit is already gone: there is nothing left to detach.
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
}
