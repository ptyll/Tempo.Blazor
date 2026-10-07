using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;

namespace Tempo.Blazor.Components.Layout;

/// <summary>
/// Internal. A fixed, hidden box the size of the viewport, used by an overlay that has no viewport
/// scope. It is not a component a host renders, so it is not part of the public surface.
/// </summary>
public sealed partial class TmViewportProbe : ComponentBase, IAsyncDisposable
{
    private const string ModulePath = "./_content/Tempo.Blazor/js/layout-observer.js";

    private readonly string _id = $"tm-viewport-{Guid.NewGuid():n}";
    private ElementReference _root;
    private IJSObjectReference? _module;
    private DotNetObjectReference<TmViewportProbe>? _dotNetRef;
    private bool _disposed;

    [Inject] private IJSRuntime JS { get; set; } = default!;

    /// <summary>Raised for every measurement, including the first. The mode is the viewport's.</summary>
    [Parameter] public EventCallback<TmLayoutMode> OnMeasured { get; set; }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || _disposed) return;
        _dotNetRef = DotNetObjectReference.Create(this);
        _module = await JS.InvokeAsync<IJSObjectReference>("import", ModulePath);
        if (_disposed)
        {
            await ReleaseAsync();
            return;
        }
        if (_module is null) return;
        await _module.InvokeVoidAsync("observe", _root, _dotNetRef, _id, new
        {
            breakpoints = new { sm = TmBreakpoints.Sm, md = TmBreakpoints.Md, lg = TmBreakpoints.Lg },
        });
    }

    /// <summary>Called by layout-observer.js. Reports every measurement, including a repeat.</summary>
    [JSInvokable]
    public Task OnLayoutModeChanged(string mode)
    {
        var measured = mode switch
        {
            "mobile" => TmLayoutMode.Mobile,
            "tablet" => TmLayoutMode.Tablet,
            "desktop" => TmLayoutMode.Desktop,
            _ => TmLayoutMode.Desktop
        };
        return InvokeAsync(() => OnMeasured.InvokeAsync(measured));
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await ReleaseAsync();
    }

    private async Task ReleaseAsync()
    {
        try { if (_module is not null) await _module.InvokeVoidAsync("disconnect", _id); }
        catch (JSDisconnectedException) { }
        catch (ObjectDisposedException) { }
        try { if (_module is not null) await _module.DisposeAsync(); }
        catch (JSDisconnectedException) { }
        _dotNetRef?.Dispose();
    }
}
