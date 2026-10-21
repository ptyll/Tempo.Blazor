using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Actions;
using Tempo.Blazor.Components.Buttons;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Components.Navigation;
using Tempo.Blazor.Components.Toolbar;
using Tempo.Blazor.EmailTemplates.Abstractions.Editing;
using Tempo.Blazor.EmailTemplates.Abstractions.Import;
using Tempo.Blazor.EmailTemplates.Abstractions.Layout;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Registry;
using Tempo.Blazor.EmailTemplates.Abstractions.Serialization;
using Tempo.Blazor.EmailTemplates.Abstractions.Templating;
using Tempo.Blazor.EmailTemplates.Services;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.EmailTemplates.Components;

// JS interop: the per-element Ctrl/Cmd+S guard (the browser's own "save page" default).
public partial class TmEmailTemplateEditor
{
    private const string SaveShortcutModulePath = "./_content/Tempo.Blazor.EmailTemplates/tm-email-save-shortcut.js";

    // ── JS interop: cancel the browser's own Ctrl/⌘+S -----------------------------------------

    private async Task AttachSaveShortcutAsync()
    {
        try
        {
            _shortcutModule ??= await JS.InvokeAsync<IJSObjectReference>("import", SaveShortcutModulePath);
            if (_disposed)
            {
                // Disposed while the module import was in flight: the root element is gone, so attaching now
                // would guard an element nothing can ever detach (the per-element WeakMap lives in the JS
                // module, and its listener would outlive the component).
                DetachSaveShortcut();
                return;
            }

            if (_shortcutModule is not null)
            {
                await _shortcutModule.InvokeVoidAsync("attachSaveShortcut", _rootRef);
            }
        }
        catch (JSException)
        {
            // Interop is unavailable (prerendering, tests): the Blazor handler still saves; only the
            // browser's "save page" default is left untouched.
        }
        catch (InvalidOperationException)
        {
            // No JS runtime during prerendering.
        }
        catch (JSDisconnectedException)
        {
            // The circuit is gone; nothing to guard.
        }
        catch (TaskCanceledException)
        {
            // The render was cancelled (navigation/disposal mid-import).
        }
    }

    private void DetachSaveShortcut()
    {
        var module = _shortcutModule;
        _shortcutModule = null;
        if (module is null) return;
        _ = DetachSaveShortcutCoreAsync(module, _rootRef);
    }

    /// <summary>
    /// Releases the chord guard for exactly this element (the module keys its listeners per element, so two
    /// editors on one page cannot detach each other) and disposes the module handle. Never throws: disposal
    /// runs after the circuit may already be gone.
    /// </summary>
    private static async Task DetachSaveShortcutCoreAsync(IJSObjectReference module, ElementReference element)
    {
        try
        {
            await module.InvokeVoidAsync("detachSaveShortcut", element);
            await module.DisposeAsync();
        }
        catch (JSException)
        {
        }
        catch (JSDisconnectedException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (TaskCanceledException)
        {
        }
    }
}
