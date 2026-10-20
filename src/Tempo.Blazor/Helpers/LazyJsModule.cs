using Microsoft.JSInterop;

namespace Tempo.Blazor.Helpers;

/// <summary>
/// One lazily imported ES module per owner: a single in-flight import (concurrent and repeated
/// <see cref="GetAsync"/> calls share it), disposal-safe hand-out — an import that outlived its
/// owner is released by the <see cref="GetAsync"/> call that awaited it — and every interop
/// failure class (<see cref="JSException"/>, <see cref="JSDisconnectedException"/>,
/// <see cref="TaskCanceledException"/>/<see cref="OperationCanceledException"/>,
/// <see cref="ObjectDisposedException"/>, <see cref="InvalidOperationException"/>) swallowed.
/// Safe to construct during prerender: nothing touches the <see cref="IJSRuntime"/> before the
/// first call.
/// </summary>
/// <remarks>
/// The shared import/guard boilerplate of the internal interop wrappers (cluster-B 8.8): a wrapper
/// keeps its public surface and replaces its <c>_module</c>/<c>_disposed</c>/try-catch with one
/// <see cref="LazyJsModule"/> field. A null or failed import is never cached — the next call
/// retries, so a module handler registered after a first unavailable-JS attempt is still honoured.
/// Unexpected (non-guarded) failures are deliberately not degraded away: they surface to the caller.
/// </remarks>
internal sealed class LazyJsModule : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private readonly string _modulePath;
    // The owner's last will: runs once with the module when an import outlived the owner, BEFORE
    // the module is released — the deactivate/detach/disconnect a racing dispose could not issue
    // itself (it no longer holds the module).
    private readonly Func<IJSObjectReference, Task>? _onLateImportRelease;

    // One import per owner, shared by every caller: concurrent Gets await the same task instead of
    // racing two imports of the same module. Cleared on a null or faulted result so the next call
    // can retry, and never handed out again after a release.
    private Task<IJSObjectReference?>? _importTask;
    private IJSObjectReference? _module;
    private bool _disposed;
    // Concurrent Gets share one import; when that import outlives the owner, exactly ONE awaiter
    // runs the last will and releases the module — the others see this flag and just yield null.
    private bool _lateReleased;

    /// <summary>Creates the helper. No interop happens here — safe during prerender.</summary>
    /// <param name="onLateImportRelease">
    /// Optional. Runs once with the module when an import outlived the owner, before the module is
    /// released; the wrapper's one cleanup invoke (deactivate/detach/stop/disconnect) a racing
    /// dispose could not issue through the helper. Guarded failures are swallowed.
    /// </param>
    public LazyJsModule(IJSRuntime js, string modulePath, Func<IJSObjectReference, Task>? onLateImportRelease = null)
    {
        _js = js;
        _modulePath = modulePath;
        _onLateImportRelease = onLateImportRelease;
    }

    /// <summary>True once <see cref="DisposeAsync"/> ran; <see cref="GetAsync"/> yields null and the invokes report failure afterwards.</summary>
    public bool IsDisposed => _disposed;

    /// <summary>
    /// True once the module import has completed. Lets a cleanup-only path (detach on close,
    /// dispose) invoke without triggering the import for a module that was never used.
    /// </summary>
    public bool IsImported => _module is not null;

    /// <summary>
    /// The module, or null when JS is unavailable, the import failed, or the owner was disposed
    /// (an import that outlived disposal is released here).
    /// </summary>
    public ValueTask<IJSObjectReference?> GetAsync()
    {
        if (_disposed) return new ValueTask<IJSObjectReference?>((IJSObjectReference?)null);
        if (_module is not null) return new ValueTask<IJSObjectReference?>(_module);
        _importTask ??= ImportAsync();
        return new ValueTask<IJSObjectReference?>(AwaitAsync(_importTask));
    }

    /// <summary>Invokes a void export. False when the call could not be made or failed (the exception is swallowed).</summary>
    public async ValueTask<bool> TryInvokeVoidAsync(string identifier, params object?[]? args)
    {
        var module = await GetAsync();
        if (module is null) return false;
        try
        {
            await module.InvokeVoidAsync(identifier, args);
            return true;
        }
        catch (Exception ex) when (IsBenign(ex))
        {
            return false;
        }
    }

    /// <summary>Invokes a value-returning export. <c>Ok</c> is false when the call could not be made or failed (the exception is swallowed).</summary>
    public async ValueTask<(bool Ok, T? Value)> TryInvokeAsync<T>(string identifier, params object?[]? args)
    {
        var module = await GetAsync();
        if (module is null) return (false, default);
        try
        {
            return (true, await module.InvokeAsync<T>(identifier, args));
        }
        catch (Exception ex) when (IsBenign(ex))
        {
            return (false, default);
        }
    }

    /// <summary>Releases the imported module. Idempotent; an import still in flight keeps its awaiter, which releases the module when it completes.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _importTask = null;
        var module = _module;
        _module = null;
        if (module is null) return;
        try
        {
            await module.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
        catch (ObjectDisposedException) { }
        catch (OperationCanceledException) { }
        catch (InvalidOperationException) { }
    }

    private async Task<IJSObjectReference?> ImportAsync()
    {
        try
        {
            return await _js.InvokeAsync<IJSObjectReference>("import", _modulePath);
        }
        catch (Exception ex) when (IsBenign(ex))
        {
            return null;
        }
    }

    private async Task<IJSObjectReference?> AwaitAsync(Task<IJSObjectReference?> importTask)
    {
        IJSObjectReference? module;
        try
        {
            module = await importTask;   // ImportAsync converts guarded failures to null
        }
        catch
        {
            // A faulted import is never cached: the next call retries instead of inheriting the fault.
            if (ReferenceEquals(_importTask, importTask)) _importTask = null;
            throw;
        }
        if (_disposed)
        {
            // The import outlived the owner: run the owner's last cleanup invoke, then release —
            // once, no matter how many Gets were awaiting the shared import.
            if (_lateReleased || module is null) return null;
            _lateReleased = true;
            if (_onLateImportRelease is not null)
            {
                try { await _onLateImportRelease(module); }
                catch (Exception ex) when (IsBenign(ex)) { }
            }
            await ReleaseAsync(module);
            return null;
        }
        if (module is null)
        {
            // JS unavailable (prerender, bUnit's substituted runtime, a torn-down circuit) —
            // don't cache; the next call retries and honours a handler registered later.
            if (ReferenceEquals(_importTask, importTask)) _importTask = null;
            return null;
        }
        _module = module;
        return module;
    }

    private async Task ReleaseAsync(IJSObjectReference? module)
    {
        if (module is null) return;
        if (ReferenceEquals(_module, module)) _module = null;
        try
        {
            await module.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
        catch (ObjectDisposedException) { }
        catch (OperationCanceledException) { }
        catch (InvalidOperationException) { }
    }

    private static bool IsBenign(Exception ex) => ex is JSException or JSDisconnectedException
        or OperationCanceledException or ObjectDisposedException or InvalidOperationException;
}
