using Microsoft.JSInterop;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>
/// Lazily imports and owns the CSP-safe <c>modeling-editor.js</c> ES module shared by the
/// Modeling components (imported via <c>IJSObjectReference</c> — no <c>&lt;script&gt;</c> tag
/// needed in the host). Every interop is progressive enhancement: a failed import is not cached
/// (the next call retries) and the safe invoke swallows the teardown exceptions a prerender,
/// bUnit or a torn-down circuit produces, so the component keeps working without the module.
/// NEVER fall back to <c>eval</c>: hosts may ship a CSP without <c>unsafe-eval</c> (guarded by
/// <c>ModelingNoEvalInteropTests</c>).
/// </summary>
internal sealed class ModelingInteropModule : IAsyncDisposable
{
    /// <summary>Module path of the Modeling package's CSP-safe ES module.</summary>
    internal const string InteropModulePath = "./_content/Tempo.Blazor.Modeling/js/modeling-editor.js";

    private readonly IJSRuntime _jsRuntime;
    private Task<IJSObjectReference>? _moduleTask;
    private bool _disposed;

    /// <summary>Test seam (InternalsVisibleTo): whether <see cref="DisposeAsync"/> already ran.</summary>
    internal bool IsDisposed => _disposed;

    public ModelingInteropModule(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    /// <summary>
    /// Invokes a void export of the module. A failed import is not cached — the next call
    /// retries — and the invocation swallows the exceptions that mean "there is nothing left
    /// to enhance"; it never falls back to <c>eval</c>. No import is attempted after
    /// <see cref="DisposeAsync"/>.
    /// </summary>
    public async Task InvokeVoidSafeAsync(string identifier, params object?[] args)
    {
        if (_disposed)
        {
            return;
        }

        IJSObjectReference? module;
        try
        {
            module = await ImportSafeAsync();
        }
        catch (JSException)
        {
            // A failed import is progressive-enhancement noise — the component keeps working.
            return;
        }
        catch (JSDisconnectedException)
        {
            // The circuit is gone; there is nothing left to enhance.
            return;
        }
        catch (OperationCanceledException)
        {
            // The runtime cancelled the in-flight import.
            return;
        }
        catch (ObjectDisposedException)
        {
            // The component or the runtime is being torn down.
            return;
        }

        if (module is null)
        {
            return;
        }

        try
        {
            await module.InvokeVoidAsync(identifier, args);
        }
        catch (JSException)
        {
            // Progressive enhancement only — the component itself must keep working.
        }
        catch (JSDisconnectedException)
        {
            // The circuit is gone; there is nothing left to enhance.
        }
        catch (OperationCanceledException)
        {
            // The runtime cancelled the in-flight call.
        }
        catch (ObjectDisposedException)
        {
            // The component or the runtime is being torn down.
        }
    }

    /// <summary>
    /// Invokes a VALUE-returning export with the same progressive-enhancement guarantees as
    /// <see cref="InvokeVoidSafeAsync"/>: every failure path (no module, prerender, torn-down
    /// circuit, disposed runtime) yields <c>default</c> instead of throwing, so a boolean probe
    /// simply reports "no" and the component keeps working without the module.
    /// </summary>
    public async Task<T> InvokeSafeAsync<T>(string identifier, params object?[] args)
    {
        if (_disposed)
        {
            return default!;
        }

        IJSObjectReference? module;
        try
        {
            module = await ImportSafeAsync();
        }
        catch (JSException)
        {
            // A failed import is progressive-enhancement noise — the component keeps working.
            return default!;
        }
        catch (JSDisconnectedException)
        {
            // The circuit is gone; there is nothing left to enhance.
            return default!;
        }
        catch (OperationCanceledException)
        {
            // The runtime cancelled the in-flight import.
            return default!;
        }
        catch (ObjectDisposedException)
        {
            // The component or the runtime is being torn down.
            return default!;
        }

        if (module is null)
        {
            return default!;
        }

        try
        {
            return await module.InvokeAsync<T>(identifier, args);
        }
        catch (JSException)
        {
            // Progressive enhancement only — the component itself must keep working.
            return default!;
        }
        catch (JSDisconnectedException)
        {
            // The circuit is gone; there is nothing left to enhance.
            return default!;
        }
        catch (OperationCanceledException)
        {
            // The runtime cancelled the in-flight call.
            return default!;
        }
        catch (ObjectDisposedException)
        {
            // The component or the runtime is being torn down.
            return default!;
        }
    }

    private async Task<IJSObjectReference?> ImportSafeAsync()
    {
        if (_disposed)
        {
            return null;
        }

        _moduleTask ??= _jsRuntime.InvokeAsync<IJSObjectReference>("import", InteropModulePath).AsTask();
        try
        {
            return await _moduleTask;
        }
        catch (Exception)
        {
            // Do not cache a failed import — the next call retries (e.g. a module handler
            // registered by a test after the first render, or a module that 404'd once).
            _moduleTask = null;
            throw;
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
        var import = _moduleTask;
        _moduleTask = null;
        if (import is null)
        {
            return;
        }

        try
        {
            // Await the in-flight import before disposing: releasing an IJSObjectReference
            // whose import is still running would race the runtime's own bookkeeping.
            var module = await import;
            await module.DisposeAsync();
        }
        catch (Exception)
        {
            // The import never completed (prerender, torn-down circuit, disposed runtime) —
            // there is nothing left to release, and dispose must not throw into the host.
        }
    }
}
