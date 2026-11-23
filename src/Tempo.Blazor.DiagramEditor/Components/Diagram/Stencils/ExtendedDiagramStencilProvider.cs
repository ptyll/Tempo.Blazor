using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Services;

namespace Tempo.Blazor.Components.Diagram.Stencils;

/// <summary>Provides additional Tempo-original diagram stencil libraries.</summary>
public sealed class ExtendedDiagramStencilProvider : IDiagramStencilProvider
{
    private readonly Lazy<List<DiagramStencil>> _cloudArchitectureStencils = new(() => [.. CloudArchitecture()]);

    /// <inheritdoc />
    public int Priority => 10;

    /// <inheritdoc />
    public IEnumerable<DiagramStencilSet> GetStencilSets()
    {
        yield return Set("tempo-flowchart", "DiagramStencilSet_TempoFlowchart", [.. Flowchart()]);
        yield return Set("tempo-erd", "DiagramStencilSet_TempoErd", [.. Erd()]);
        yield return Set("c4", "DiagramStencilSet_C4", [.. C4()]);
        yield return Set("cloud-architecture", "DiagramStencilSet_CloudArchitecture", _cloudArchitectureStencils.Value);
        yield return Set("kubernetes", "DiagramStencilSet_Kubernetes", [.. Kubernetes()]);
    }

    private static DiagramStencilSet Set(string id, string resourceKey, List<DiagramStencil> stencils)
        => new()
        {
            Id = id,
            Name = id,
            NameResourceKey = resourceKey,
            Stencils = stencils
        };

    private static IEnumerable<DiagramStencil> Flowchart()
    {
        const string set = "tempo-flowchart";
        const string setKey = "DiagramStencilSet_TempoFlowchart";
        const string palette = "tempo-flowchart.core";
        const string paletteKey = "DiagramStencilPalette_TempoFlowchartCore";
        const string fill = "var(--tm-diagram-stencil-slate-fill)";

        yield return Node(set, setKey, palette, paletteKey, "tempo-flowchart.process", "Process", "DiagramStencil_TempoFlowchartProcess", 0, "rectangle", fill, "tm-ext-marker-flow-process", "<path d='M76 22 H96 M76 31 H90 M76 40 H96'/>", "Process");
        yield return Node(set, setKey, palette, paletteKey, "tempo-flowchart.decision", "Decision", "DiagramStencil_TempoFlowchartDecision", 1, "diamond", "var(--tm-diagram-stencil-orange-fill)", "tm-ext-marker-flow-decision", "<path d='M86 16 L98 28 L86 40 L74 28 Z'/><path d='M86 23 V29 M86 35 V36'/>", "Decision", 128, 96);
        yield return Node(set, setKey, palette, paletteKey, "tempo-flowchart.terminator", "Terminator", "DiagramStencil_TempoFlowchartTerminator", 2, "rounded", "var(--tm-diagram-stencil-cyan-fill)", "tm-ext-marker-flow-terminator", "<path d='M75 25 H97 M75 35 H97'/>", "Start / End");
        yield return Node(set, setKey, palette, paletteKey, "tempo-flowchart.data", "Data", "DiagramStencil_TempoFlowchartData", 3, "parallelogram", "var(--tm-diagram-stencil-blue-fill)", "tm-ext-marker-flow-data", "<path d='M78 17 H98 L94 42 H74 Z M80 25 H94 M78 33 H92'/>", "Data");
        yield return Node(set, setKey, palette, paletteKey, "tempo-flowchart.document", "Document", "DiagramStencil_TempoFlowchartDocument", 4, "document", "var(--tm-diagram-stencil-amber-fill)", "tm-ext-marker-flow-document", DocumentMarker(), "Document");
        yield return Node(set, setKey, palette, paletteKey, "tempo-flowchart.offpage", "Off-page Connector", "DiagramStencil_TempoFlowchartOffpage", 5, "pentagon", "var(--tm-diagram-stencil-slate-muted-fill)", "tm-ext-marker-offpage", "<path d='M76 16 H96 V32 L86 44 L76 32 Z M82 25 H92'/>", "Off-page", 118, 86);
    }

    private static IEnumerable<DiagramStencil> Erd()
    {
        const string set = "tempo-erd";
        const string setKey = "DiagramStencilSet_TempoErd";
        const string palette = "tempo-erd.core";
        const string paletteKey = "DiagramStencilPalette_TempoErdCore";

        yield return Node(set, setKey, palette, paletteKey, "tempo-erd.entity", "Entity", "DiagramStencil_TempoErdEntity", 0, "rectangle", "var(--tm-diagram-stencil-amber-100-fill)", "tm-ext-marker-erd-entity", "<path d='M74 15 H98 V42 H74 Z M74 24 H98 M80 33 H92'/>", "Entity", 160, 88, ["erd", "sql", "table", "ddl", "database"]);
        yield return Node(set, setKey, palette, paletteKey, "tempo-erd.weak-entity", "Weak Entity", "DiagramStencil_TempoErdWeakEntity", 1, "rectangle", "var(--tm-diagram-stencil-amber-200-fill)", "tm-ext-marker-erd-weak-entity", "<rect x='73' y='14' width='26' height='29' rx='2'/><rect x='77' y='18' width='18' height='21' rx='1'/>", "Weak Entity", 168, 92, ["erd", "sql", "table", "weak"]);
        yield return Node(set, setKey, palette, paletteKey, "tempo-erd.attribute", "Attribute", "DiagramStencil_TempoErdAttribute", 2, "ellipse", "var(--tm-diagram-stencil-lime-fill)", "tm-ext-marker-erd-attribute", "<ellipse cx='86' cy='28' rx='14' ry='10'/><path d='M78 28 H94'/>", "Attribute", 132, 78, ["erd", "column", "field"]);
        yield return Node(set, setKey, palette, paletteKey, "tempo-erd.key-attribute", "Key Attribute", "DiagramStencil_TempoErdKeyAttribute", 3, "ellipse", "var(--tm-diagram-stencil-lime-strong-fill)", "tm-ext-marker-erd-key", "<ellipse cx='86' cy='28' rx='14' ry='10'/><path d='M78 28 H94 M82 35 H90'/>", "Key", 132, 78, ["erd", "primary-key", "pk", "column"]);
        yield return Edge(set, setKey, palette, paletteKey, "tempo-erd.relationship", "Relationship", "DiagramStencil_TempoErdRelationship", 4, "erd-relationship", "open", false, "straight");
        yield return Edge(set, setKey, palette, paletteKey, "tempo-erd.identifying-relationship", "Identifying Relationship", "DiagramStencil_TempoErdIdentifyingRelationship", 5, "erd-identifying-relationship", "block", true, "straight", strokeDashPattern: "solid");
    }

    private static IEnumerable<DiagramStencil> C4()
    {
        const string set = "c4";
        const string setKey = "DiagramStencilSet_C4";

        yield return Node(set, setKey, "c4.software-systems", "DiagramStencilPalette_C4SoftwareSystems", "c4.person", "Person", "DiagramStencil_C4Person", 0, "rounded", "var(--tm-diagram-stencil-sky-fill)", "tm-ext-marker-person", PersonMarker(), "Person", 132, 92, ["c4", "actor", "user"]);
        yield return Node(set, setKey, "c4.software-systems", "DiagramStencilPalette_C4SoftwareSystems", "c4.software-system", "Software System", "DiagramStencil_C4SoftwareSystem", 1, "rounded", "var(--tm-diagram-stencil-blue-100-fill)", "tm-ext-marker-system", "<path d='M75 16 H97 V40 H75 Z M80 24 H92 M80 32 H88'/>", "Software System", 172, 96, ["c4", "system"]);
        yield return Node(set, setKey, "c4.containers", "DiagramStencilPalette_C4Containers", "c4.container", "Container", "DiagramStencil_C4Container", 0, "rounded", "var(--tm-diagram-stencil-green-100-fill)", "tm-ext-marker-container", "<rect x='74' y='16' width='24' height='22' rx='3'/><path d='M80 16 V38 M74 24 H98'/>", "Container", 156, 88, ["c4", "container", "service"]);
        yield return Node(set, setKey, "c4.containers", "DiagramStencilPalette_C4Containers", "c4.component", "Component", "DiagramStencil_C4Component", 1, "rectangle", "var(--tm-diagram-stencil-green-fill)", "tm-ext-marker-component-neutral", "<rect x='76' y='16' width='21' height='22' rx='2'/><path d='M72 21 H78 M72 31 H78'/>", "Component", 148, 82, ["c4", "component"]);
        yield return Node(set, setKey, "c4.containers", "DiagramStencilPalette_C4Containers", "c4.database", "Database", "DiagramStencil_C4Database", 2, "cylinder", "var(--tm-diagram-stencil-yellow-100-fill)", "tm-ext-marker-database", DatabaseMarker(), "Database", 138, 92, ["c4", "database", "data"]);
        yield return Edge(set, setKey, "c4.relationships", "DiagramStencilPalette_C4Relationships", "c4.relationship", "Relationship", "DiagramStencil_C4Relationship", 0, "c4-relationship", "block", true, "orthogonal");
    }

    private static IEnumerable<DiagramStencil> CloudArchitecture()
    {
        const string set = "cloud-architecture";
        const string setKey = "DiagramStencilSet_CloudArchitecture";

        yield return Node(set, setKey, "cloud-architecture.compute", "DiagramStencilPalette_CloudCompute", "cloud.compute", "Compute", "DiagramStencil_CloudCompute", 0, "rounded", "var(--tm-diagram-stencil-sky-fill)", "tm-ext-marker-cloud-compute", CloudMarker("<path d='M82 26 H94 M88 20 V36'/>"), "Compute", tags: ["cloud", "compute", "generic"]);
        yield return Node(set, setKey, "cloud-architecture.compute", "DiagramStencilPalette_CloudCompute", "cloud.serverless", "Serverless Function", "DiagramStencil_CloudServerless", 1, "rounded", "var(--tm-diagram-stencil-teal-fill)", "tm-ext-marker-cloud-serverless", CloudMarker("<path d='M83 18 L76 30 H86 L81 42 L98 26 H88 L93 18 Z'/>"), "Function", tags: ["cloud", "serverless", "function", "generic"]);
        yield return Node(set, setKey, "cloud-architecture.network", "DiagramStencilPalette_CloudNetwork", "cloud.load-balancer", "Load Balancer", "DiagramStencil_CloudLoadBalancer", 0, "hexagon", "var(--tm-diagram-stencil-indigo-fill)", "tm-ext-marker-cloud-lb", CloudMarker("<path d='M86 18 V40 M76 26 H86 H98 M76 34 H86 H98'/>"), "Load Balancer", tags: ["cloud", "network", "traffic", "generic"]);
        yield return Node(set, setKey, "cloud-architecture.network", "DiagramStencilPalette_CloudNetwork", "cloud.vpc", "Network Boundary", "DiagramStencil_CloudVpc", 1, "rounded", "var(--tm-diagram-stencil-indigo-50-fill)", "tm-ext-marker-cloud-network", "<rect x='73' y='14' width='26' height='28' rx='4' stroke-dasharray='4 3'/><path d='M80 24 H92 M80 32 H92'/>", "Network", tags: ["cloud", "network", "boundary", "generic"], width: 176, height: 96);
        yield return Node(set, setKey, "cloud-architecture.data", "DiagramStencilPalette_CloudData", "cloud.database", "Database", "DiagramStencil_CloudDatabase", 0, "cylinder", "var(--tm-diagram-stencil-amber-100-fill)", "tm-ext-marker-cloud-database", DatabaseMarker(), "Database", tags: ["cloud", "database", "data", "generic"]);
        yield return Node(set, setKey, "cloud-architecture.data", "DiagramStencilPalette_CloudData", "cloud.queue", "Queue", "DiagramStencil_CloudQueue", 1, "rounded", "var(--tm-diagram-stencil-pink-fill)", "tm-ext-marker-cloud-queue", "<path d='M74 20 H98 V28 H74 Z M74 32 H98 V40 H74 Z'/>", "Queue", tags: ["cloud", "queue", "messaging", "generic"]);
        yield return Node(set, setKey, "cloud-architecture.data", "DiagramStencilPalette_CloudData", "cloud.object-storage", "Object Storage", "DiagramStencil_CloudObjectStorage", 2, "cylinder", "var(--tm-diagram-stencil-yellow-100-fill)", "tm-ext-marker-cloud-storage", "<path d='M76 19 C76 14 96 14 96 19 V39 C96 44 76 44 76 39 Z M76 19 C76 24 96 24 96 19 M76 30 C76 35 96 35 96 30'/>", "Object Storage", tags: ["cloud", "storage", "object", "generic"], width: 154, height: 92);
    }

    private static IEnumerable<DiagramStencil> Kubernetes()
    {
        const string set = "kubernetes";
        const string setKey = "DiagramStencilSet_Kubernetes";

        yield return Node(set, setKey, "kubernetes.workloads", "DiagramStencilPalette_KubernetesWorkloads", "kubernetes.cluster", "Cluster", "DiagramStencil_KubernetesCluster", 0, "rounded", "var(--tm-diagram-stencil-sky-fill)", "tm-ext-marker-k8s-cluster", ClusterMarker(), "Cluster", width: 188, height: 104, tags: ["kubernetes", "cluster", "neutral"]);
        yield return Node(set, setKey, "kubernetes.workloads", "DiagramStencilPalette_KubernetesWorkloads", "kubernetes.namespace", "Namespace", "DiagramStencil_KubernetesNamespace", 1, "rounded", "var(--tm-diagram-stencil-blue-100-fill)", "tm-ext-marker-k8s-namespace", "<rect x='73' y='15' width='27' height='27' rx='5' stroke-dasharray='4 3'/><path d='M80 24 H93 M80 33 H89'/>", "Namespace", tags: ["kubernetes", "namespace", "neutral"]);
        yield return Node(set, setKey, "kubernetes.workloads", "DiagramStencilPalette_KubernetesWorkloads", "kubernetes.pod", "Pod", "DiagramStencil_KubernetesPod", 2, "hexagon", "var(--tm-diagram-stencil-green-100-fill)", "tm-ext-marker-k8s-pod", "<polygon points='86,12 99,20 99,36 86,44 73,36 73,20'/><circle cx='86' cy='28' r='6'/>", "Pod", tags: ["kubernetes", "pod", "neutral"]);
        yield return Node(set, setKey, "kubernetes.workloads", "DiagramStencilPalette_KubernetesWorkloads", "kubernetes.deployment", "Deployment", "DiagramStencil_KubernetesDeployment", 3, "rounded", "var(--tm-diagram-stencil-green-fill)", "tm-ext-marker-k8s-deployment", "<path d='M86 13 L98 20 V36 L86 43 L74 36 V20 Z M80 24 H92 M80 32 H92'/>", "Deployment", tags: ["kubernetes", "deployment", "workload", "neutral"]);
        yield return Node(set, setKey, "kubernetes.network", "DiagramStencilPalette_KubernetesNetwork", "kubernetes.service", "Service", "DiagramStencil_KubernetesService", 0, "rounded", "var(--tm-diagram-stencil-indigo-fill)", "tm-ext-marker-k8s-service", "<circle cx='86' cy='28' r='14'/><path d='M74 28 H100 M86 14 V42'/>", "Service", tags: ["kubernetes", "service", "network", "neutral"]);
        yield return Node(set, setKey, "kubernetes.network", "DiagramStencilPalette_KubernetesNetwork", "kubernetes.ingress", "Ingress", "DiagramStencil_KubernetesIngress", 1, "rounded", "var(--tm-diagram-stencil-indigo-50-fill)", "tm-ext-marker-k8s-ingress", "<path d='M74 38 L86 14 L99 38 Z M86 22 V38'/>", "Ingress", tags: ["kubernetes", "ingress", "network", "neutral"]);
        yield return Node(set, setKey, "kubernetes.storage", "DiagramStencilPalette_KubernetesStorage", "kubernetes.config", "Config", "DiagramStencil_KubernetesConfig", 0, "document", "var(--tm-diagram-stencil-amber-fill)", "tm-ext-marker-k8s-config", DocumentMarker(), "Config", tags: ["kubernetes", "config", "secret", "neutral"]);
        yield return Node(set, setKey, "kubernetes.storage", "DiagramStencilPalette_KubernetesStorage", "kubernetes.persistent-volume", "Persistent Volume", "DiagramStencil_KubernetesPersistentVolume", 1, "cylinder", "var(--tm-diagram-stencil-amber-100-fill)", "tm-ext-marker-k8s-volume", DatabaseMarker(), "Volume", tags: ["kubernetes", "volume", "storage", "neutral"]);
    }

    private static DiagramStencil Node(
        string setId,
        string setResourceKey,
        string paletteId,
        string paletteResourceKey,
        string id,
        string name,
        string nameResourceKey,
        int order,
        string shape,
        string fill,
        string markerClass,
        string markerSvg,
        string defaultLabel,
        double width = 148,
        double height = 82,
        string[]? tags = null)
        => new()
        {
            Id = id,
            Name = name,
            NameResourceKey = nameResourceKey,
            Category = setId,
            SetId = setId,
            SetNameResourceKey = setResourceKey,
            PaletteId = paletteId,
            PaletteNameResourceKey = paletteResourceKey,
            PaletteOrder = PaletteOrder(paletteId),
            Order = order,
            Kind = DiagramStencilKind.Node,
            Origin = DiagramStencilOrigin.TempoOriginal,
            IconSvg = IconSvg(shape, markerSvg),
            DefaultWidth = width,
            DefaultHeight = height,
            Tags = tags?.ToList() ?? [setId, name.ToLowerInvariant()],
            Keywords = BuildKeywords(name, setId, paletteId, tags),
            Ports = CardinalPorts(),
            ConnectionPoints = CardinalConnectionPoints(),
            Layout = new()
            {
                BackgroundShape = shape,
                ShapeSvg = ShapeSvg(shape, markerClass, markerSvg),
                Fill = fill,
                Stroke = "var(--tm-diagram-stencil-stroke-on-fill)",
                StrokeWidth = 1.5,
                Sections = [TextSection("label", defaultLabel)]
            },
            DefaultData = new() { ["label"] = defaultLabel }
        };

    private static List<string> BuildKeywords(string name, string setId, string paletteId, string[]? tags)
    {
        var keywords = new List<string> { name, setId, paletteId, "tempo", "custom" };
        if (tags is not null)
            keywords.AddRange(tags);

        return keywords
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static DiagramStencil Edge(
        string setId,
        string setResourceKey,
        string paletteId,
        string paletteResourceKey,
        string id,
        string name,
        string nameResourceKey,
        int order,
        string connectorType,
        string endArrow,
        bool? endArrowFill,
        string routing,
        string? strokeDashPattern = null)
        => new()
        {
            Id = id,
            Name = name,
            NameResourceKey = nameResourceKey,
            Category = setId,
            SetId = setId,
            SetNameResourceKey = setResourceKey,
            PaletteId = paletteId,
            PaletteNameResourceKey = paletteResourceKey,
            PaletteOrder = PaletteOrder(paletteId),
            Order = order,
            Kind = DiagramStencilKind.Edge,
            Origin = DiagramStencilOrigin.TempoOriginal,
            IconSvg = EdgeIcon(strokeDashPattern),
            Tags = [setId, "relationship", "connector"],
            Keywords = [name, connectorType, setId, "tempo"],
            EdgeDefaults = new()
            {
                Routing = routing,
                ConnectorType = connectorType,
                Shape = "connector",
                EndArrow = endArrow,
                EndArrowFill = endArrowFill,
                Style = strokeDashPattern is null ? null : new DiagramStyle { StrokeDashPattern = strokeDashPattern }
            }
        };

    private static int PaletteOrder(string paletteId)
        => paletteId switch
        {
            "tempo-flowchart.core" or "tempo-erd.core" or "c4.software-systems" or "cloud-architecture.compute" or "kubernetes.workloads" => 0,
            "c4.containers" or "cloud-architecture.network" or "kubernetes.network" => 1,
            "c4.relationships" or "cloud-architecture.data" or "kubernetes.storage" => 2,
            _ => 10
        };

    private static DiagramStencilSection TextSection(string dataKey, string defaultText)
        => new()
        {
            Type = "text",
            DataKey = dataKey,
            DefaultText = defaultText,
            Padding = 10,
            TextStyle = new()
            {
                TextAlign = StencilTextAlign.Center,
                FontSize = 12,
                Color = "var(--tm-diagram-node-ink-on-fill)"
            }
        };

    private static string ShapeSvg(string shape, string markerClass, string markerSvg)
    {
        var bg = shape switch
        {
            "rectangle" or "rounded" or "ellipse" or "diamond" or "hexagon" or "parallelogram" => string.Empty,
            "document" => "<path d='M0 0 H78 L100 22 V100 H0 Z' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><path d='M78 0 V22 H100' fill='none' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
            "cylinder" => "<ellipse cx='50' cy='15' rx='48' ry='14' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><rect x='2' y='15' width='96' height='70' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><ellipse cx='50' cy='85' rx='48' ry='14' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
            "pentagon" => "<path d='M8 0 H92 V62 L50 100 L8 62 Z' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
            _ => "<rect x='0' y='0' width='100' height='100' rx='4' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>"
        };

        return $"{bg}<g class='{markerClass}' fill='none' stroke='var(--stencil-stroke)' stroke-width='2.8' stroke-linecap='round' stroke-linejoin='round' vector-effect='non-scaling-stroke'>{markerSvg}</g>";
    }

    private static string IconSvg(string shape, string markerSvg)
    {
        var bg = shape switch
        {
            "rounded" => "<rect x='4' y='8' width='24' height='16' rx='5' fill='none' stroke='currentColor' stroke-width='2'/>",
            "diamond" => "<path d='M16 4 L29 16 L16 28 L3 16 Z' fill='none' stroke='currentColor' stroke-width='2'/>",
            "ellipse" => "<ellipse cx='16' cy='16' rx='12' ry='9' fill='none' stroke='currentColor' stroke-width='2'/>",
            "hexagon" => "<polygon points='9,6 23,6 29,16 23,26 9,26 3,16' fill='none' stroke='currentColor' stroke-width='2'/>",
            "cylinder" => "<ellipse cx='16' cy='9' rx='10' ry='4' fill='none' stroke='currentColor' stroke-width='2'/><path d='M6 9 V23 C6 28 26 28 26 23 V9' fill='none' stroke='currentColor' stroke-width='2'/>",
            _ => "<rect x='4' y='7' width='24' height='18' rx='2' fill='none' stroke='currentColor' stroke-width='2'/>"
        };
        return $"{bg}<g transform='translate(-70 -70) scale(.9)' fill='none' stroke='currentColor' stroke-width='3' stroke-linecap='round' stroke-linejoin='round'>{markerSvg}</g>";
    }

    private static string EdgeIcon(string? dashPattern)
        => $"<path d='M4 16 H27' fill='none' stroke='currentColor' stroke-width='2' stroke-linecap='round'{(dashPattern is null or "solid" ? string.Empty : " stroke-dasharray='4 3'")}/><path d='M22 11 L28 16 L22 21' fill='none' stroke='currentColor' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'/>";

    private static string PersonMarker()
        => "<circle cx='86' cy='16' r='6'/><path d='M86 22 V38 M75 29 H97 M78 46 L86 38 L94 46'/>";

    private static string DocumentMarker()
        => "<path d='M74 12 H91 L99 20 V44 H74 Z M91 12 V20 H99'/>";

    private static string DatabaseMarker()
        => "<ellipse cx='86' cy='18' rx='13' ry='5'/><path d='M73 18 V39 C73 46 99 46 99 39 V18 M73 29 C73 36 99 36 99 29'/>";

    private static string CloudMarker(string inner)
        => $"<path d='M76 35 C70 34 70 24 76 23 C79 13 94 14 96 24 C104 26 102 40 93 40 H78 C70 40 69 36 76 35 Z'/>{inner}";

    private static string ClusterMarker()
        => "<circle cx='86' cy='28' r='16'/><path d='M86 12 V18 M86 40 V46 M70 28 H76 M96 28 H102 M75 17 L79 21 M93 39 L97 43 M75 43 L79 39 M93 21 L97 17'/>";

    private static List<DiagramStencilPortDef> CardinalPorts()
        =>
        [
            new() { Name = "top", Side = PortSide.Top, Offset = 0.5, MagnetStrategy = "perimeter" },
            new() { Name = "right", Side = PortSide.Right, Offset = 0.5, MagnetStrategy = "perimeter" },
            new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5, MagnetStrategy = "perimeter" },
            new() { Name = "left", Side = PortSide.Left, Offset = 0.5, MagnetStrategy = "perimeter" }
        ];

    private static List<DiagramStencilConnectionPoint> CardinalConnectionPoints()
        =>
        [
            new() { Name = "N", RelativeX = 0.5, RelativeY = 0, Perimeter = true },
            new() { Name = "E", RelativeX = 1, RelativeY = 0.5, Perimeter = true },
            new() { Name = "S", RelativeX = 0.5, RelativeY = 1, Perimeter = true },
            new() { Name = "W", RelativeX = 0, RelativeY = 0.5, Perimeter = true },
            new() { Name = "C", RelativeX = 0.5, RelativeY = 0.5, Perimeter = false }
        ];
}
