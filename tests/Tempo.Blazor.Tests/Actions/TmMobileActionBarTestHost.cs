using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Actions;

namespace Tempo.Blazor.Tests.Actions;

/// <summary>
/// Test-only host for <see cref="TmMobileActionBar"/>: exercises the public
/// <c>CloseMoreAsync</c> API through a real dispatched DOM event, the way a host component
/// reaches it from its own event handler (e.g. the confirmed-delete flow). Calling the method
/// directly via <c>IRenderedComponent.InvokeAsync</c> re-enters a busy dispatcher in sheet mode,
/// which production never does.
/// </summary>
public sealed class TmMobileActionBarTestHost : ComponentBase
{
    private TmMobileActionBar? _bar;

    [Parameter] public IReadOnlyList<TmActionItem> Items { get; set; } = [];

    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Mobile;

    public Task CloseMoreAsync() => _bar?.CloseMoreAsync() ?? Task.CompletedTask;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "button");
        builder.AddAttribute(1, "type", "button");
        builder.AddAttribute(2, "id", "host-close-more");
        builder.AddAttribute(3, "onclick", EventCallback.Factory.Create(this, CloseMoreAsync));
        builder.AddContent(4, "close more");
        builder.CloseElement();

        builder.OpenComponent<TmMobileActionBar>(5);
        builder.AddAttribute(6, nameof(TmMobileActionBar.Items), Items);
        builder.AddAttribute(7, nameof(TmMobileActionBar.LayoutMode), LayoutMode);
        builder.AddComponentReferenceCapture(8, component => _bar = (TmMobileActionBar)component);
        builder.CloseComponent();
    }
}
