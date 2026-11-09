using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;

namespace Tempo.Blazor.Components.NotionEditor.UI;

/// <summary>Modal overlay that lists Notion editor keyboard shortcuts.</summary>
public partial class TmNotionShortcutsPanel : TmComponentBase, IAsyncDisposable
{
    [Inject] private IJSRuntime JS { get; set; } = default!;

    /// <summary>Controls whether the shortcut panel is visible.</summary>
    [Parameter]
    public bool Visible { get; set; }

    /// <summary>Raised when <see cref="Visible"/> changes.</summary>
    [Parameter]
    public EventCallback<bool> VisibleChanged { get; set; }

    /// <summary>Shortcut groups rendered by the panel. Defaults to the built-in Notion catalog.</summary>
    [Parameter]
    public IReadOnlyList<NotionShortcutGroup> Groups { get; set; } = NotionShortcutCatalog.DefaultGroups;

    /// <summary>Forced layout. Mobile presents the panel as a sheet.</summary>
    [Parameter]
    public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    private DotNetObjectReference<TmNotionShortcutsPanel>? _dotNetRef;

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        _dotNetRef = DotNetObjectReference.Create(this);
        try
        {
            await JS.InvokeVoidAsync("tmNotionEditor.registerShortcuts", _dotNetRef);
        }
        catch
        {
            _dotNetRef.Dispose();
            _dotNetRef = null;
        }
    }

    /// <summary>Handles global shortcut keys dispatched from JavaScript.</summary>
    /// <param name="key">The pressed key.</param>
    [JSInvokable]
    public async Task OnNotionShortcutKey(string key)
    {
        if (key == "?")
            await VisibleChanged.InvokeAsync(true);
    }

    private Task CloseAsync()
        => VisibleChanged.InvokeAsync(false);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("tmNotionEditor.unregisterShortcuts");
        }
        catch { }

        _dotNetRef?.Dispose();
    }
}
