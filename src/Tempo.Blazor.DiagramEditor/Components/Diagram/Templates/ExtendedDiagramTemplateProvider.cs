using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Serialization;

namespace Tempo.Blazor.Components.Diagram.Templates;

/// <summary>Provides Tempo-original templates for extended stencil libraries.</summary>
public sealed class ExtendedDiagramTemplateProvider : IDiagramTemplateProvider
{
    private Lazy<Task<IEnumerable<DiagramTemplateCategory>>> _categories =
        new(LoadCategoriesAsync, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <inheritdoc />
    public int Priority => 10;

    /// <inheritdoc />
    public Task<IEnumerable<DiagramTemplateCategory>> GetTemplateCategoriesAsync()
    {
        var lazy = Volatile.Read(ref _categories);
        try
        {
            return lazy.Value;
        }
        catch
        {
            // A faulted Lazy stays faulted forever: swap in a fresh one (only if the cached
            // value is still the one that faulted) so the next call retries the build.
            Interlocked.CompareExchange(ref _categories,
                new Lazy<Task<IEnumerable<DiagramTemplateCategory>>>(LoadCategoriesAsync, LazyThreadSafetyMode.ExecutionAndPublication),
                lazy);
            throw;
        }
    }

    private static Task<IEnumerable<DiagramTemplateCategory>> LoadCategoriesAsync()
    {
        // Documents are built (and thumbnails generated) once, on first use, thread-safely.
        IEnumerable<DiagramTemplateCategory> categories =
        [
            new()
            {
                Name = "UML",
                Templates =
                [
                    Template(
                        "uml25-class-baseline",
                        "UML 2.5 Class Baseline",
                        "UML",
                        ["uml", "uml25", "class", "software"],
                        BuildUml25ClassDocument())
                ]
            },
            new()
            {
                Name = "BPMN",
                Templates =
                [
                    Template(
                        "bpmn2-process-baseline",
                        "BPMN 2 Process Baseline",
                        "BPMN",
                        ["bpmn", "bpmn2", "process", "workflow"],
                        BuildBpmn2ProcessDocument())
                ]
            },
            new()
            {
                Name = "ArchiMate",
                Templates =
                [
                    Template(
                        "archimate3-layered-baseline",
                        "ArchiMate 3 Layered Baseline",
                        "ArchiMate",
                        ["archimate", "archimate3", "enterprise-architecture", "layers"],
                        BuildArchimate3LayeredDocument())
                ]
            },
            new()
            {
                Name = "C4",
                Templates =
                [
                    Template(
                        "c4-container-baseline",
                        "C4 Container Baseline",
                        "C4",
                        ["c4", "container", "architecture"],
                        BuildC4ContainerDocument())
                ]
            }
        ];

        return Task.FromResult(categories);
    }

    private static DiagramTemplate Template(string id, string name, string category, string[] tags, string documentJson)
        => new()
        {
            Id = id,
            Name = name,
            Category = category,
            Tags = tags,
            DocumentJson = documentJson,
            ThumbnailUrl = DiagramTemplateThumbnails.FromDocumentJson(documentJson)
        };

    private static string BuildUml25ClassDocument()
    {
        var document = NewDocument("UML 2.5 Class Baseline");
        var page = document.ActivePage;

        var customer = Node("uml25.class", 190, 160, 210, 170, new()
        {
            ["name"] = "Customer",
            ["attributes"] = new[] { "- id: Guid", "- name: string", "- email: string" },
            ["methods"] = new[] { "+ Save(): void", "+ Load(): void" }
        });
        var order = Node("uml25.class", 550, 160, 210, 170, new()
        {
            ["name"] = "Order",
            ["attributes"] = new[] { "- id: Guid", "- total: decimal" },
            ["methods"] = new[] { "+ Place(): void", "+ Cancel(): void" }
        });
        var repository = Node("uml25.interface", 910, 160, 210, 170, new()
        {
            ["stereotype"] = "<<interface>>",
            ["name"] = "IOrderRepository",
            ["attributes"] = Array.Empty<string>(),
            ["methods"] = new[] { "+ Save(order): void", "+ Find(id): Order" }
        });

        page.Nodes.AddRange([customer, order, repository]);
        page.Edges.Add(Edge(customer, order, "1..*", "association", "straight", "none", "classic"));
        page.Edges.Add(Edge(order, repository, "uses", "realization", "orthogonal", "none", "block", endArrowFill: false, strokeDashPattern: "dashed"));

        return DiagramSerializer.Serialize(document);
    }

    private static string BuildBpmn2ProcessDocument()
    {
        var document = NewDocument("BPMN 2 Process Baseline", 1050, 620);
        var page = document.ActivePage;

        var start = Node("bpmn2.event.start", 90, 270, 62, 62, new() { ["label"] = "" });
        var task = Node("bpmn2.task.user", 230, 235, 220, 108, new() { ["label"] = "Review request" });
        var gateway = Node("bpmn2.gateway.exclusive", 530, 245, 96, 96, new() { ["label"] = "" });
        var approve = Node("bpmn2.task.service", 715, 145, 220, 108, new() { ["label"] = "Approve" });
        var reject = Node("bpmn2.task.manual", 715, 365, 220, 108, new() { ["label"] = "Reject" });
        var end = Node("bpmn2.event.end", 970, 270, 66, 66, new() { ["label"] = "" });

        page.Nodes.AddRange([start, task, gateway, approve, reject, end]);
        page.Edges.Add(Edge(start, task, null, "bpmn-sequence-flow", "straight", "none", "block", endArrowFill: true));
        page.Edges.Add(Edge(task, gateway, null, "bpmn-sequence-flow", "straight", "none", "block", endArrowFill: true));
        page.Edges.Add(Edge(gateway, approve, "yes", "bpmn-sequence-flow", "orthogonal", "none", "block", endArrowFill: true));
        page.Edges.Add(Edge(gateway, reject, "no", "bpmn-sequence-flow", "orthogonal", "none", "block", endArrowFill: true));
        page.Edges.Add(Edge(approve, end, null, "bpmn-sequence-flow", "orthogonal", "none", "block", endArrowFill: true));
        page.Edges.Add(Edge(reject, end, null, "bpmn-sequence-flow", "orthogonal", "none", "block", endArrowFill: true));

        return DiagramSerializer.Serialize(document);
    }

    private static string BuildArchimate3LayeredDocument()
    {
        var document = NewDocument("ArchiMate 3 Layered Baseline");
        var page = document.ActivePage;

        var actor = Node("archimate3.business.actor", 200, 130, 160, 84, new() { ["label"] = "Customer" });
        var process = Node("archimate3.business.process", 470, 130, 170, 84, new() { ["label"] = "Order Journey" });
        var app = Node("archimate3.application.component", 470, 320, 190, 84, new() { ["label"] = "Order App" });
        var service = Node("archimate3.application.service", 760, 320, 180, 84, new() { ["label"] = "Checkout Service" });
        var node = Node("archimate3.technology.node", 470, 510, 180, 84, new() { ["label"] = "Runtime Node" });

        page.Nodes.AddRange([actor, process, app, service, node]);
        page.Edges.Add(Edge(actor, process, null, "archimate-assignment", "straight", "none", "block", endArrowFill: true));
        page.Edges.Add(Edge(process, app, null, "archimate-serving", "orthogonal", "none", "open", endArrowFill: false));
        page.Edges.Add(Edge(app, service, null, "archimate-realization", "straight", "none", "open", endArrowFill: false, strokeDashPattern: "dashed"));
        page.Edges.Add(Edge(node, app, null, "archimate-serving", "orthogonal", "none", "open", endArrowFill: false));

        return DiagramSerializer.Serialize(document);
    }

    private static string BuildC4ContainerDocument()
    {
        var document = NewDocument("C4 Container Baseline");
        var page = document.ActivePage;

        var person = Node("c4.person", 180, 160, 132, 92, new() { ["label"] = "User" });
        var system = Node("c4.software-system", 410, 130, 190, 110, new() { ["label"] = "Online Platform" });
        var web = Node("c4.container", 720, 100, 166, 92, new() { ["label"] = "Web App" });
        var api = Node("c4.container", 720, 270, 166, 92, new() { ["label"] = "API Service" });
        var database = Node("c4.database", 1010, 270, 144, 96, new() { ["label"] = "Database" });

        page.Nodes.AddRange([person, system, web, api, database]);
        page.Edges.Add(Edge(person, system, "uses", "c4-relationship", "orthogonal", "none", "block", endArrowFill: true));
        page.Edges.Add(Edge(system, web, "contains", "c4-relationship", "orthogonal", "none", "block", endArrowFill: true));
        page.Edges.Add(Edge(web, api, "calls", "c4-relationship", "orthogonal", "none", "block", endArrowFill: true));
        page.Edges.Add(Edge(api, database, "reads/writes", "c4-relationship", "orthogonal", "none", "block", endArrowFill: true));

        return DiagramSerializer.Serialize(document);
    }

    private static DiagramDocument NewDocument(string title, double width = 1400, double height = 900)
    {
        var document = new DiagramDocument
        {
            Title = title,
            Width = width,
            Height = height
        };
        document.EnsurePages();
        return document;
    }

    private static DiagramNode Node(string stencilId, double x, double y, double width, double height, Dictionary<string, object> data)
    {
        var node = new DiagramNode
        {
            StencilId = stencilId,
            X = x,
            Y = y,
            W = width,
            H = height,
            Data = data
        };
        node.Ports.AddRange(CardinalPorts());
        return node;
    }

    private static DiagramEdge Edge(
        DiagramNode source,
        DiagramNode target,
        string? label,
        string connectorType,
        string routing,
        string startArrow,
        string endArrow,
        bool? startArrowFill = null,
        bool? endArrowFill = null,
        string? strokeDashPattern = null)
        => new()
        {
            SourceNodeId = source.Id,
            TargetNodeId = target.Id,
            SourcePortId = source.Ports.First(port => port.Name == "right").Id,
            TargetPortId = target.Ports.First(port => port.Name == "left").Id,
            Label = label,
            Routing = routing,
            ConnectorType = connectorType,
            StartArrow = startArrow,
            EndArrow = endArrow,
            StartArrowFill = startArrowFill,
            EndArrowFill = endArrowFill,
            Style = new() { StrokeDashPattern = strokeDashPattern }
        };

    private static List<DiagramPort> CardinalPorts()
        =>
        [
            new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
            new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
            new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
            new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
        ];
}
