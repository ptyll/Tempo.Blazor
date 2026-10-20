using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Layout;

namespace Tempo.Blazor.Tests.Layout;

/// <summary>
/// Test host for <see cref="TmSidePanel"/>: bUnit cannot re-render a rendered component with new
/// cascading values, so the cascade-change tests drive the container/viewport contexts through
/// parameters of this wrapper.
/// </summary>
internal sealed class TmSidePanelCascadeHost : ComponentBase
{
    [Parameter] public TmLayoutContext? ContainerContext { get; set; }
    [Parameter] public TmLayoutContext? ViewportContext { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<CascadingValue<TmLayoutContext>>(0);
        builder.AddAttribute(1, "Value", ContainerContext);
        builder.AddAttribute(2, "IsFixed", false);
        builder.AddAttribute(3, "ChildContent", (RenderFragment)(inner => RenderPanel(inner)));
        builder.CloseComponent();
    }

    private void RenderPanel(RenderTreeBuilder builder)
    {
        if (ViewportContext is null)
        {
            builder.OpenComponent<TmSidePanel>(10);
            builder.AddAttribute(11, "Open", true);
            builder.AddAttribute(12, "ChildContent", ChildContent);
            builder.CloseComponent();
            return;
        }

        builder.OpenComponent<CascadingValue<TmLayoutContext>>(20);
        builder.AddAttribute(21, "Value", ViewportContext);
        builder.AddAttribute(22, "Name", TmLayoutScopes.Viewport);
        builder.AddAttribute(23, "IsFixed", false);
        builder.AddAttribute(24, "ChildContent", (RenderFragment)(inner => RenderPanelWithScope(inner)));
        builder.CloseComponent();
    }

    private void RenderPanelWithScope(RenderTreeBuilder builder)
    {
        builder.OpenComponent<TmSidePanel>(30);
        builder.AddAttribute(31, "Open", true);
        builder.AddAttribute(32, "ChildContent", ChildContent);
        builder.CloseComponent();
    }
}
