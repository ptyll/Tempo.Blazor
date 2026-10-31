using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Tempo.Blazor.Components.Signing;

/// <summary>
/// The Signing editors' shared JS interop with the neutral <c>signing-editor.js</c> module (Signing
/// redesign phase 7 review): the post-render focus hand-off after a remove/move
/// (<see cref="TmRecipientRoleEditor"/>, <see cref="TmConditionBuilder"/>) and the cursor-aware
/// insertion into the formula textarea (<see cref="TmFormulaBuilder"/>). The module is imported on
/// demand (dynamic <c>import()</c>, like <c>signing-runner.js</c>) and every call is best-effort: a
/// view whose JS circuit is gone (bUnit, static rendering, a disposed circuit) keeps the failure
/// local — the focus move reports <c>false</c> so the caller applies its fallback, an insertion
/// returns <c>null</c> so the builder appends the fragment. The wrapper itself is
/// <see cref="IAsyncDisposable"/> and a failed import never retries per call (the failure is
/// latched).
/// </summary>
internal sealed class SigningEditorInterop : IAsyncDisposable
{
    private const string ModulePath = "./_content/Tempo.Blazor.Signing/js/signing-editor.js";

    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;
    private bool _importFailed;

    public SigningEditorInterop(IJSRuntime js)
    {
        _js = js;
    }

    /// <summary>
    /// Moves the focus to the first element matching <paramref name="selector"/> (phase 7 review:
    /// an editor whose remove/move action rebuilds the rows must move the focus somewhere useful
    /// instead of dropping it to <c>&lt;body&gt;</c>; best-effort — a missing module or element
    /// reports <c>false</c> and the caller keeps its fallback).
    /// </summary>
    /// <returns>Whether the focus landed on the element; <c>false</c> without a live module.</returns>
    public async Task<bool> FocusSelectorAsync(string selector)
    {
        if (_importFailed || !await EnsureModuleAsync().ConfigureAwait(false))
        {
            return false;
        }

        try
        {
            return await _module!.InvokeAsync<bool>("focusElement", selector).ConfigureAwait(false);
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
    /// Inserts <paramref name="text"/> into <paramref name="textarea"/> at its cursor; the new
    /// value is returned, or <c>null</c> when no JS runtime answers or the module could not resolve
    /// a textarea (append fallback).
    /// </summary>
    public async Task<string?> InsertAtCursorAsync(ElementReference textarea, string text)
    {
        if (_importFailed || !await EnsureModuleAsync().ConfigureAwait(false))
        {
            return null;
        }

        try
        {
            return await _module!.InvokeAsync<string?>("insertAtCursor", textarea, text).ConfigureAwait(false);
        }
        catch (JSDisconnectedException)
        {
            return null;
        }
        catch (JSException)
        {
            return null;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Imports the module once; <c>false</c> when the import fails or races a disposed runtime
    /// (the failure is recorded, so a dead circuit never retries per call).
    /// </summary>
    private async Task<bool> EnsureModuleAsync()
    {
        if (_module is not null)
        {
            return true;
        }

        try
        {
            _module = await _js.InvokeAsync<IJSObjectReference>("import", ModulePath).ConfigureAwait(false);
            return _module is not null;
        }
        catch (Exception ex) when (ex is JSDisconnectedException or JSException or ObjectDisposedException or TaskCanceledException or InvalidOperationException)
        {
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
