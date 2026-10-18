using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Stencils;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Diagram;

/// <summary>
/// J4: the anchored context menu renders culture-INVARIANT coordinates (a cs/sk/fr culture used
/// to produce invalid CSS like <c>left:123,5px</c>) and carries the same viewport point in
/// <c>data-screen-x/y</c>, which the JS placement re-reads on every open.
/// </summary>
public class TmDiagramEditorContextMenuTests : LocalizationTestBase
{
    public TmDiagramEditorContextMenuTests()
    {
        Services.GetRequiredService<DiagramStencilRegistry>()
            .RegisterProvider(new BuiltInDiagramStencilProvider());
    }

    [Fact]
    public async Task ContextMenu_RendersInvariantCoordinates_UnderCzechCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("cs");

            using var cut = Render<TmDiagramEditor>(p => p.Add(e => e.ReadOnly, false));
            var canvas = cut.FindComponent<TmDiagramCanvas>();
            await cut.InvokeAsync(async () => await canvas.Instance.JsOnNodeContextMenu("n1", 123.5, 45.25, "mouse"));

            var menu = cut.Find(".tm-diagram-editor__context-menu");
            menu.GetAttribute("data-screen-x").Should().Be("123.5", "the JS placement reads this attribute back");
            menu.GetAttribute("data-screen-y").Should().Be("45.25");
            menu.GetAttribute("data-anchor").Should().BeNull("a mouse menu keeps the cursor-anchored placement");
            menu.GetAttribute("style").Should().Contain("left:123.5px",
                "a 'cs' culture must never render the decimal comma into the inline style");
            menu.GetAttribute("style").Should().Contain("top:45.25px");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public async Task ContextMenu_TouchOrigin_RendersTouchAnchor_AndDisablesZoomAtLimits()
    {
        using var cut = Render<TmDiagramEditor>(p => p.Add(e => e.ReadOnly, false));
        var canvas = cut.FindComponent<TmDiagramCanvas>();
        await cut.InvokeAsync(async () => await canvas.Instance.JsOnNodeContextMenu("n1", 100, 200, "touch"));

        var menu = cut.Find(".tm-diagram-editor__context-menu");
        menu.GetAttribute("data-anchor").Should().Be("touch",
            "the JS parks a touch-anchored menu above the finger instead of under it");

        // Zoom buttons disable at the shared limits (DiagramZoomLimits) — the status-bar group.
        await cut.InvokeAsync(() => canvas.Instance.OnZoomChanged(DiagramZoomLimits.MaxScale));
        cut.FindAll(".tm-diagram-editor__zoom-group button")
            .Should().Contain(b => b.HasAttribute("disabled"),
                "zoom-in disables at MaxScale");

        await cut.InvokeAsync(() => canvas.Instance.OnZoomChanged(DiagramZoomLimits.MinScale));
        cut.FindAll(".tm-diagram-editor__zoom-group button")
            .Should().Contain(b => b.HasAttribute("disabled"),
                "zoom-out disables at MinScale");
    }
}
