using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Models;
using Xunit;

namespace Tempo.Blazor.Tests.Diagram;

/// <summary>
/// Review U1: the BPMN stencil SVG (task type marker, event ring, gateway marker) must be in the
/// rendered DOM — the native primitive used to hide it entirely.
/// </summary>
public class BpmnMarkerDomTests : DiagramTestBase
{
    public BpmnMarkerDomTests()
    {
        // The shared fixture registers the built-in stencils only; the BPMN markers live in the
        // BPMN provider (the last registration wins).
        Services.AddSingleton(_ =>
        {
            var registry = new Tempo.Blazor.Components.Diagram.Stencils.DiagramStencilRegistry();
            registry.RegisterProvider(new Tempo.Blazor.Components.Diagram.Stencils.BuiltInDiagramStencilProvider());
            registry.RegisterProvider(new Tempo.Blazor.Components.Diagram.Stencils.Bpmn2DiagramStencilProvider());
            return registry;
        });
    }

    [Fact]
    public void The_bpmn_markers_are_rendered_inside_the_native_node_bodies()
    {
        var document = new DiagramDocument
        {
            Title = "Markers",
            Width = 1000,
            Height = 1000,
            Nodes =
            [
                new DiagramNode { Id = "task", StencilId = "bpmn2.task.user", X = 40, Y = 40, W = 150, H = 92 },
                new DiagramNode { Id = "gateway", StencilId = "bpmn2.gateway.exclusive", X = 240, Y = 40, W = 84, H = 84 },
                new DiagramNode { Id = "event", StencilId = "bpmn2.event.start", X = 400, Y = 40, W = 64, H = 64 }
            ]
        };

        var cut = Render<TmDiagramCanvas>(parameters => parameters.Add(p => p.Document, document));

        var task = cut.Find("[data-node-id='task']");
        task.QuerySelector(".tm-bpmn-task-marker-user").Should().NotBeNull("the user task type icon is the BPMN convention");
        task.QuerySelector(".tm-diagram-node__svg-bg").Should().NotBeNull();
        cut.Find("[data-node-id='gateway']").QuerySelector(".tm-bpmn-gateway-marker-exclusive").Should().NotBeNull();
        cut.Find("[data-node-id='event']").QuerySelector(".tm-diagram-node__svg-bg circle").Should().NotBeNull();
    }
}
