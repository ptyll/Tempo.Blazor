using Microsoft.JSInterop;

namespace Tempo.Blazor.Components.Signing;

/// <summary>
/// The TmSigningFormRunner's JS interop with <c>signing-runner.js</c> (Signing redesign phase 5):
/// the coarse-pointer probe that resolves the <see cref="TmSigningDefaultSignatureMode.Auto"/>
/// signature default, the active-field auto-scroll and the field-list focus move. The module is
/// imported on demand (dynamic <c>import()</c>, like <c>pdf-template-designer.js</c> — no
/// <c>&lt;script&gt;</c> tag in the host) and every call is best-effort: a view whose JS circuit
/// is gone (bUnit, static rendering, a disposed circuit) keeps the failure local and falls back
/// to the fine-pointer / no-scroll behaviour. The wrapper itself is <see cref="IAsyncDisposable"/>:
/// the runner's <c>DisposeAsync</c> releases the module instance, and a failed import never
/// retries per call (the failure is latched).
/// </summary>
internal sealed class SigningRunnerInterop : IAsyncDisposable
{
    private const string ModulePath = "./_content/Tempo.Blazor.Signing/js/signing-runner.js";

    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;
    private bool _importFailed;

    public SigningRunnerInterop(IJSRuntime js)
    {
        _js = js;
    }

    /// <summary>Whether the module is available (imported at least once).</summary>
    private bool HasModule => _module is not null;

    /// <summary>
    /// Whether the primary input is a coarse pointer (a finger). <c>false</c> when the module
    /// cannot load or answer — the fine-pointer default keeps today's Typed presentation.
    /// </summary>
    public async Task<bool> IsCoarsePointerAsync()
    {
        if (_importFailed || !await EnsureModuleAsync().ConfigureAwait(false))
        {
            return false;
        }

        try
        {
            return await _module!.InvokeAsync<bool>("isCoarsePointer").ConfigureAwait(false);
        }
        catch (JSDisconnectedException)
        {
            return false;
        }
        catch (JSException)
        {
            return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Scrolls the document overlay of <paramref name="fieldUuid"/> into view above the mobile
    /// sheet (best-effort; a missing module or field is a no-op).
    /// </summary>
    public async Task ScrollToFieldAsync(string fieldUuid)
    {
        if (_importFailed || !await EnsureModuleAsync().ConfigureAwait(false))
        {
            return;
        }

        try
        {
            await _module!.InvokeVoidAsync("scrollToField", fieldUuid).ConfigureAwait(false);
        }
        catch (JSDisconnectedException)
        {
        }
        catch (JSException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (TaskCanceledException)
        {
        }
    }

    /// <summary>
    /// Scrolls the field list's row for <paramref name="fieldUuid"/> into view and focuses it
    /// (the More menu's "Field list" entry; best-effort — a missing module or row is a no-op).
    /// </summary>
    public async Task FocusFieldButtonAsync(string fieldUuid)
    {
        if (_importFailed || !await EnsureModuleAsync().ConfigureAwait(false))
        {
            return;
        }

        try
        {
            await _module!.InvokeVoidAsync("focusFieldButton", fieldUuid).ConfigureAwait(false);
        }
        catch (JSDisconnectedException)
        {
        }
        catch (JSException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (TaskCanceledException)
        {
        }
    }

    /// <summary>
    /// Imports the module once; <c>false</c> when the import fails or races a disposed runtime
    /// (the failure is recorded, so a dead circuit never retries per call).
    /// </summary>
    private async Task<bool> EnsureModuleAsync()
    {
        if (HasModule)
        {
            return true;
        }

        try
        {
            _module = await _js.InvokeAsync<IJSObjectReference>("import", ModulePath).ConfigureAwait(false);
            return _module is not null;
        }
        catch (JSDisconnectedException)
        {
            _importFailed = true;
            return false;
        }
        catch (JSException)
        {
            _importFailed = true;
            return false;
        }
        catch (ObjectDisposedException)
        {
            _importFailed = true;
            return false;
        }
        catch (TaskCanceledException)
        {
            _importFailed = true;
            return false;
        }
        catch (InvalidOperationException)
        {
            // No live JS runtime behind the circuit (disposed view / static render).
            _importFailed = true;
            return false;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_module is null)
        {
            return;
        }

        try
        {
            await _module.DisposeAsync().ConfigureAwait(false);
        }
        catch (JSDisconnectedException)
        {
            // The circuit died before the module could be released — nothing to clean up.
        }
        catch (JSException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            _module = null;
        }
    }
}
