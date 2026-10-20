using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;

namespace Tempo.Blazor.Helpers;

/// <summary>
/// Internal. A fixed, hidden box the size of the viewport, used by an overlay that has no viewport
/// scope. It is not a component a host renders, so it carries no Tm prefix and no public surface.
/// The import/guard boilerplate is delegated to <see cref="LazyJsModule"/> (cluster-B 8.8).
/// </summary>
internal sealed class ViewportProbe : ComponentBase, IAsyncDisposable
{
    private const string ModulePath = "./_content/Tempo.Blazor/js/layout-observer.js";

    private readonly string _id = $"tm-viewport-{Guid.NewGuid():n}";
    private LazyJsModule? _lazy;
    private ElementReference _root;
    private DotNetObjectReference<ViewportProbe>? _dotNetRef;
    private bool _disposed;

    [Inject] private IJSRuntime JS { get; set; } = default!;

    /// <summary>Raised for every measurement, including the first. The mode is the viewport's.</summary>
    [Parameter] public EventCallback<TmLayoutMode> OnMeasured { get; set; }

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "tm-viewport-probe");
        builder.AddAttribute(2, "style", "position:fixed;inset:0;visibility:hidden;pointer-events:none");
        builder.AddElementReferenceCapture(3, element => _root = element);
        builder.CloseElement();
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || _disposed) return;
        _dotNetRef = DotNetObjectReference.Create(this);
        // An import that outlived the owner is disconnected, then released — the pinned
        // release path a late import must still run. The helper swallows the guarded failure
        // classes itself.
        _lazy ??= new LazyJsModule(JS, ModulePath, async module => await module.InvokeVoidAsync("disconnect", _id));
        // Null when JS is unavailable — or when the owner was disposed while the import
        // awaited (the helper released the module in that case).
        if (await _lazy.GetAsync() is null)
        {
            if (_disposed) ReleaseDotNetRef();
            return;
        }
        await _lazy.TryInvokeVoidAsync("observe", _root, _dotNetRef, _id, new
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
        // disconnect runs only when the module was actually imported — never a fresh import
        // from a dispose path. The helper swallows the guarded failure classes itself.
        if (_lazy is { IsImported: true }) await _lazy.TryInvokeVoidAsync("disconnect", _id);
        if (_lazy is not null) await _lazy.DisposeAsync();
        ReleaseDotNetRef();
    }

    private void ReleaseDotNetRef()
    {
        _dotNetRef?.Dispose();
        _dotNetRef = null;
    }
}
