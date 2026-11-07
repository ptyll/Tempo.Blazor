using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Services;

namespace Tempo.Blazor.Components.Diagram.Stencils;

/// <summary>Provides Tempo-original BPMN 2.0 stencil definitions.</summary>
public sealed class Bpmn2DiagramStencilProvider : IDiagramStencilProvider
{
    private const string SetId = "bpmn2";
    private const string SetNameResourceKey = "DiagramStencilSet_Bpmn2";
    private const string Category = "BPMN 2.0";

    /// <summary>
    /// Token-based fill of every BPMN node shape. The drawing surface is the canvas PAPER
    /// (<c>--tm-diagram-paper</c>, declared on <c>.tm-diagram-canvas</c> next to the other
    /// canvas-scoped paints; theme-invariant by design, because the page is a paper metaphor even
    /// in a dark theme), so the shapes paint with the canvas tokens instead of a hard-coded hex: a
    /// host (or a high-contrast theme) can recolour them by overriding the tokens. The literal
    /// fallback covers an SVG viewer WITHOUT the stylesheet (the server-side export resolves the
    /// token through <c>DiagramSvgBuilder</c> instead).
    /// </summary>
    private const string NodeFillToken = "var(--tm-diagram-paper, #ffffff)";

    /// <summary>Token-based stroke of every BPMN node shape (the canvas ink keeps the boundary
    /// legible on the paper surface in every theme).</summary>
    private const string NodeStrokeToken = "var(--tm-diagram-node-stroke, #111827)";

    /// <inheritdoc />
    public int Priority => 10;

    /// <inheritdoc />
    public IEnumerable<DiagramStencilSet> GetStencilSets()
    {
        return
        [
            new()
            {
                Id = SetId,
                Name = "BPMN 2.0",
                NameResourceKey = SetNameResourceKey,
                Stencils =
                [
                    .. GeneralPalette(),
                    .. TasksPalette(),
                    .. EventsPalette(),
                    .. GatewaysPalette(),
                    .. SwimlanesPalette(),
                    .. RelationshipPalette()
                ]
            }
        ];
    }

    private static IEnumerable<DiagramStencil> GeneralPalette()
    {
        yield return Node(
            "bpmn2.data-object",
            "Data Object",
            "DiagramStencil_Bpmn2DataObject",
            "bpmn2.general",
            "DiagramStencilPalette_Bpmn2General",
            0,
            0,
            "<path d='M7 4 H22 L27 9 V28 H7 Z M22 4 V9 H27' fill='none' stroke='currentColor' stroke-width='2' stroke-linejoin='round'/>",
            96,
            116,
            "document",
            "<path d='M0 0 H78 L100 22 V100 H0 Z' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><path d='M78 0 V22 H100' fill='none' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
            [TextSection("label", "Data", 8, align: StencilTextAlign.Center)],
            new() { ["label"] = "Data" });

        yield return Node(
            "bpmn2.data-store",
            "Data Store",
            "DiagramStencil_Bpmn2DataStore",
            "bpmn2.general",
            "DiagramStencilPalette_Bpmn2General",
            0,
            1,
            "<ellipse cx='16' cy='7' rx='10' ry='4' fill='none' stroke='currentColor' stroke-width='2'/><path d='M6 7 V24 C6 29 26 29 26 24 V7' fill='none' stroke='currentColor' stroke-width='2'/><path d='M6 16 C6 21 26 21 26 16' fill='none' stroke='currentColor' stroke-width='1.5'/>",
            110,
            120,
            "cylinder",
            "<ellipse cx='50' cy='14' rx='48' ry='14' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><path d='M2 14 V84 C2 103 98 103 98 84 V14' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><path d='M2 42 C2 61 98 61 98 42' fill='none' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
            [TextSection("label", "Data Store", 10, align: StencilTextAlign.Center)],
            new() { ["label"] = "Data Store" });
    }

    private static IEnumerable<DiagramStencil> TasksPalette()
    {
        yield return Task("bpmn2.task", "Task", "DiagramStencil_Bpmn2Task", 0, "Task", string.Empty);
        yield return Task("bpmn2.task.user", "User Task", "DiagramStencil_Bpmn2UserTask", 1, "User Task", UserTaskMarker("tm-bpmn-task-marker-user"));
        yield return Task("bpmn2.task.service", "Service Task", "DiagramStencil_Bpmn2ServiceTask", 2, "Service Task", ServiceTaskMarker("tm-bpmn-task-marker-service"));
        yield return Task("bpmn2.task.manual", "Manual Task", "DiagramStencil_Bpmn2ManualTask", 3, "Manual Task", ManualTaskMarker("tm-bpmn-task-marker-manual"));
        yield return Task("bpmn2.task.script", "Script Task", "DiagramStencil_Bpmn2ScriptTask", 4, "Script Task", ScriptTaskMarker("tm-bpmn-task-marker-script"));
        yield return Task("bpmn2.task.business-rule", "Business Rule Task", "DiagramStencil_Bpmn2BusinessRuleTask", 5, "Business Rule", BusinessRuleTaskMarker("tm-bpmn-task-marker-business-rule"));
        yield return Task("bpmn2.task.send", "Send Task", "DiagramStencil_Bpmn2SendTask", 6, "Send Task", EnvelopeTaskMarker("tm-bpmn-task-marker-send", filled: true));
        yield return Task("bpmn2.task.receive", "Receive Task", "DiagramStencil_Bpmn2ReceiveTask", 7, "Receive Task", EnvelopeTaskMarker("tm-bpmn-task-marker-receive", filled: false));

        yield return Task(
            "bpmn2.subprocess",
            "Subprocess",
            "DiagramStencil_Bpmn2Subprocess",
            8,
            "Subprocess",
            SubprocessMarker("tm-bpmn-subprocess-marker"),
            isCollapsible: true);

        yield return Task(
            "bpmn2.subprocess.collapsed",
            "Collapsed Subprocess",
            "DiagramStencil_Bpmn2CollapsedSubprocess",
            9,
            "Subprocess",
            SubprocessMarker("tm-bpmn-subprocess-marker"),
            isCollapsible: true);
    }

    private static IEnumerable<DiagramStencil> EventsPalette()
    {
        yield return Event("bpmn2.event.start", "Start Event", "DiagramStencil_Bpmn2StartEvent", 0, EventRing("start"), string.Empty, 58, 58);
        yield return Event("bpmn2.event.intermediate", "Intermediate Event", "DiagramStencil_Bpmn2IntermediateEvent", 1, EventRing("intermediate"), string.Empty, 62, 62);
        yield return Event("bpmn2.event.end", "End Event", "DiagramStencil_Bpmn2EndEvent", 2, EventRing("end"), string.Empty, 62, 62);
        yield return Event("bpmn2.event.message", "Message Event", "DiagramStencil_Bpmn2MessageEvent", 3, EventRing("intermediate"), MessageEventMarker("tm-bpmn-event-marker-message"), 62, 62);
        yield return Event("bpmn2.event.timer", "Timer Event", "DiagramStencil_Bpmn2TimerEvent", 4, EventRing("intermediate"), TimerEventMarker("tm-bpmn-event-marker-timer"), 62, 62);
        yield return Event("bpmn2.event.error", "Error Event", "DiagramStencil_Bpmn2ErrorEvent", 5, EventRing("intermediate"), ErrorEventMarker("tm-bpmn-event-marker-error"), 62, 62);
        yield return Event("bpmn2.event.signal", "Signal Event", "DiagramStencil_Bpmn2SignalEvent", 6, EventRing("intermediate"), SignalEventMarker("tm-bpmn-event-marker-signal"), 62, 62);
        yield return Event("bpmn2.event.terminate", "Terminate Event", "DiagramStencil_Bpmn2TerminateEvent", 7, EventRing("end"), TerminateEventMarker("tm-bpmn-event-marker-terminate"), 62, 62);
        yield return Event("bpmn2.event.non-interrupting", "Non-interrupting Event", "DiagramStencil_Bpmn2NonInterruptingEvent", 8, EventRing("intermediate", dashed: true), string.Empty, 62, 62);
    }

    private static IEnumerable<DiagramStencil> GatewaysPalette()
    {
        yield return Gateway("bpmn2.gateway.exclusive", "Exclusive Gateway", "DiagramStencil_Bpmn2ExclusiveGateway", 0, ExclusiveGatewayMarker("tm-bpmn-gateway-marker-exclusive"));
        yield return Gateway("bpmn2.gateway.parallel", "Parallel Gateway", "DiagramStencil_Bpmn2ParallelGateway", 1, ParallelGatewayMarker("tm-bpmn-gateway-marker-parallel"));
        yield return Gateway("bpmn2.gateway.inclusive", "Inclusive Gateway", "DiagramStencil_Bpmn2InclusiveGateway", 2, InclusiveGatewayMarker("tm-bpmn-gateway-marker-inclusive"));
        yield return Gateway("bpmn2.gateway.event-based", "Event-based Gateway", "DiagramStencil_Bpmn2EventBasedGateway", 3, EventBasedGatewayMarker("tm-bpmn-gateway-marker-event-based"));
        yield return Gateway("bpmn2.gateway.complex", "Complex Gateway", "DiagramStencil_Bpmn2ComplexGateway", 4, ComplexGatewayMarker("tm-bpmn-gateway-marker-complex"));
    }

    private static IEnumerable<DiagramStencil> SwimlanesPalette()
    {
        yield return Node(
            "bpmn2.pool",
            "Pool",
            "DiagramStencil_Bpmn2Pool",
            "bpmn2.swimlanes",
            "DiagramStencilPalette_Bpmn2Swimlanes",
            4,
            0,
            "<rect x='4' y='5' width='24' height='22' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><path d='M10 5 V27 M10 16 H28' stroke='currentColor' stroke-width='2'/>",
            520,
            260,
            "swimlane-horizontal",
            // U3: ONE outer <rect> only. The swimlane component paints the header band, the
            // separators and the collapsed stripes itself, so a separator path here would paint
            // the same line twice (a dark stroke doubled over the header edge).
            "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
            [SwimlaneSection()],
            new() { ["label"] = "Pool" },
            isCollapsible: true,
            isSwimlane: true);

        yield return Node(
            "bpmn2.lane",
            "Lane",
            "DiagramStencil_Bpmn2Lane",
            "bpmn2.swimlanes",
            "DiagramStencilPalette_Bpmn2Swimlanes",
            4,
            1,
            "<rect x='4' y='8' width='24' height='16' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><path d='M10 8 V24' stroke='currentColor' stroke-width='2'/>",
            440,
            140,
            "swimlane-horizontal",
            // U3: ONE outer <rect> only — the label band separator is the component's job.
            "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
            [SwimlaneSection()],
            new() { ["label"] = "Lane" },
            isSwimlane: true);
    }

    private static IEnumerable<DiagramStencil> RelationshipPalette()
    {
        yield return Edge("bpmn2.flow.sequence", "Sequence Flow", "DiagramStencil_Bpmn2SequenceFlow", 0, "bpmn-sequence-flow", "block", true, null, "straight", "connector");
        yield return Edge("bpmn2.flow.conditional", "Conditional Flow", "DiagramStencil_Bpmn2ConditionalFlow", 1, "bpmn-conditional-flow", "block", true, "dashed", "straight", "connector", startArrow: "diamond", startArrowFill: false);
        yield return Edge("bpmn2.flow.default", "Default Flow", "DiagramStencil_Bpmn2DefaultFlow", 2, "bpmn-default-flow", "block", true, "dashed", "straight", "connector");
        yield return Edge("bpmn2.flow.message", "Message Flow", "DiagramStencil_Bpmn2MessageFlow", 3, "bpmn-message-flow", "open", false, "dashed", "straight", "connector", startArrow: "oval", startArrowFill: false);
        yield return Edge("bpmn2.association", "Association", "DiagramStencil_Bpmn2Association", 4, "bpmn-association", "none", null, "dotted", "straight", "connector");
        yield return Edge("bpmn2.data-association", "Data Association", "DiagramStencil_Bpmn2DataAssociation", 5, "bpmn-data-association", "open", false, "dotted", "straight", "connector");
    }

    private static DiagramStencil Task(
        string id,
        string name,
        string nameResourceKey,
        int order,
        string defaultLabel,
        string markerSvg,
        bool isCollapsible = false)
        => Node(
            id,
            name,
            nameResourceKey,
            "bpmn2.tasks",
            "DiagramStencilPalette_Bpmn2Tasks",
            1,
            order,
            TaskIcon(markerSvg),
            150,
            92,
            "rounded",
            $"<rect x='0' y='0' width='100' height='100' rx='10' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>{markerSvg}",
            [TextSection("label", defaultLabel, 8, align: StencilTextAlign.Center)],
            new() { ["label"] = defaultLabel },
            isCollapsible: isCollapsible);

    private static DiagramStencil Event(
        string id,
        string name,
        string nameResourceKey,
        int order,
        string ringSvg,
        string markerSvg,
        double width,
        double height)
        => Node(
            id,
            name,
            nameResourceKey,
            "bpmn2.events",
            "DiagramStencilPalette_Bpmn2Events",
            2,
            order,
            $"<circle cx='16' cy='16' r='11' fill='none' stroke='currentColor' stroke-width='2'/>{EventIconMarker(markerSvg)}",
            width,
            height,
            "ellipse",
            $"{ringSvg}{markerSvg}",
            [],
            []);

    private static DiagramStencil Gateway(
        string id,
        string name,
        string nameResourceKey,
        int order,
        string markerSvg)
        => Node(
            id,
            name,
            nameResourceKey,
            "bpmn2.gateways",
            "DiagramStencilPalette_Bpmn2Gateways",
            3,
            order,
            $"<polygon points='16,4 28,16 16,28 4,16' fill='none' stroke='currentColor' stroke-width='2'/>{GatewayIconMarker(markerSvg)}",
            84,
            84,
            "diamond",
            $"<polygon points='50,0 100,50 50,100 0,50' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>{markerSvg}",
            [],
            []);

    private static DiagramStencil Node(
        string id,
        string name,
        string nameResourceKey,
        string paletteId,
        string paletteNameResourceKey,
        int paletteOrder,
        int order,
        string iconSvg,
        double width,
        double height,
        string backgroundShape,
        string shapeSvg,
        List<DiagramStencilSection> sections,
        Dictionary<string, object> defaultData,
        bool isCollapsible = false,
        bool isSwimlane = false)
        => new()
        {
            Id = id,
            Name = name,
            NameResourceKey = nameResourceKey,
            Category = Category,
            SetId = SetId,
            SetNameResourceKey = SetNameResourceKey,
            PaletteId = paletteId,
            PaletteNameResourceKey = paletteNameResourceKey,
            PaletteOrder = paletteOrder,
            Order = order,
            Kind = DiagramStencilKind.Node,
            Origin = DiagramStencilOrigin.TempoOriginal,
            IconSvg = iconSvg,
            DefaultWidth = width,
            DefaultHeight = height,
            IsCollapsible = isCollapsible,
            IsSwimlane = isSwimlane,
            Tags = ["bpmn", "bpmn2", "bpmn20", "process"],
            Keywords = [name, paletteId, "BPMN 2.0", "2.0", "workflow"],
            Ports = CardinalPorts(),
            ConnectionPoints = CardinalConnectionPoints(),
            Layout = new()
            {
                BackgroundShape = backgroundShape,
                ShapeSvg = shapeSvg,
                Sections = sections,
                // Phase 4 of the Modeling authoring plan ("dark mode tokens"): without an explicit
                // fill/stroke the node renderer falls back to hard-coded #ffffff / #111827, which a
                // dark theme and a high-contrast theme cannot recolour. BPMN shapes therefore carry
                // the canonical TOKENS (the node's own DiagramStyle still wins when set).
                Fill = NodeFillToken,
                Stroke = NodeStrokeToken,
                StrokeWidth = 1.5
            },
            DefaultData = defaultData
        };

    private static DiagramStencil Edge(
        string id,
        string name,
        string nameResourceKey,
        int order,
        string connectorType,
        string endArrow,
        bool? endArrowFill,
        string? dashPattern,
        string routing,
        string shape,
        string startArrow = "none",
        bool? startArrowFill = null)
        => new()
        {
            Id = id,
            Name = name,
            NameResourceKey = nameResourceKey,
            Category = Category,
            SetId = SetId,
            SetNameResourceKey = SetNameResourceKey,
            PaletteId = "bpmn2.relationships",
            PaletteNameResourceKey = "DiagramStencilPalette_Bpmn2Relationships",
            PaletteOrder = 5,
            Order = order,
            Kind = DiagramStencilKind.Edge,
            Origin = DiagramStencilOrigin.TempoOriginal,
            IconSvg = EdgeIcon(startArrow, endArrow, dashPattern),
            Tags = ["bpmn", "bpmn2", "bpmn20", "flow", "relationship"],
            Keywords = [name, connectorType, "BPMN 2.0", "2.0", "workflow"],
            EdgeDefaults = new()
            {
                Routing = routing,
                ConnectorType = connectorType,
                Shape = shape,
                StartArrow = startArrow,
                EndArrow = endArrow,
                StartArrowFill = startArrowFill,
                EndArrowFill = endArrowFill,
                Style = dashPattern is null ? null : new DiagramStyle { StrokeDashPattern = dashPattern }
            }
        };

    private static DiagramStencilSection TextSection(
        string dataKey,
        string defaultText,
        double padding,
        bool bold = false,
        StencilTextAlign align = StencilTextAlign.Left,
        double fontSize = 12)
        => new()
        {
            Type = "text",
            DataKey = dataKey,
            DefaultText = defaultText,
            Padding = padding,
            TextStyle = new()
            {
                IsBold = bold,
                TextAlign = align,
                FontSize = fontSize
            }
        };

    private static DiagramStencilSection SwimlaneSection()
        => new()
        {
            Type = "swimlane",
            DataKey = "swimlane",
            DefaultText = "Swimlane",
            Padding = 0
        };

    private static string TaskIcon(string markerSvg)
        => string.IsNullOrWhiteSpace(markerSvg)
            ? "<rect x='4' y='8' width='24' height='16' rx='5' fill='none' stroke='currentColor' stroke-width='2'/>"
            : "<rect x='4' y='8' width='24' height='16' rx='5' fill='none' stroke='currentColor' stroke-width='2'/><circle cx='10' cy='16' r='2' fill='currentColor'/><path d='M15 14 H25 M15 18 H23' stroke='currentColor' stroke-width='1.5'/>";

    private static string UserTaskMarker(string cssClass)
        => $"<g class='{cssClass}' fill='none' stroke='var(--stencil-stroke)' stroke-width='2' vector-effect='non-scaling-stroke'><circle cx='20' cy='22' r='5'/><path d='M12 38 C14 30 26 30 28 38'/></g>";

    private static string ServiceTaskMarker(string cssClass)
        => $"<g class='{cssClass}' fill='none' stroke='var(--stencil-stroke)' stroke-width='2' vector-effect='non-scaling-stroke'><circle cx='20' cy='26' r='7'/><path d='M20 17 V13 M20 43 V39 M11 26 H7 M33 26 H29 M14 20 L11 17 M26 20 L29 17 M14 32 L11 35 M26 32 L29 35'/></g>";

    private static string ManualTaskMarker(string cssClass)
        => $"<path class='{cssClass}' d='M12 30 C12 22 16 22 16 29 V20 C16 16 21 16 21 20 V29 V22 C21 18 26 18 26 22 V31 C26 38 22 42 16 42 C11 42 8 38 8 34 V30 Z' fill='none' stroke='var(--stencil-stroke)' stroke-width='2' stroke-linejoin='round' vector-effect='non-scaling-stroke'/>";

    private static string ScriptTaskMarker(string cssClass)
        => $"<path class='{cssClass}' d='M12 16 H30 L26 42 H8 Z M13 23 H25 M12 30 H24 M11 37 H20' fill='none' stroke='var(--stencil-stroke)' stroke-width='2' stroke-linejoin='round' vector-effect='non-scaling-stroke'/>";

    private static string BusinessRuleTaskMarker(string cssClass)
        => $"<g class='{cssClass}' fill='none' stroke='var(--stencil-stroke)' stroke-width='2' vector-effect='non-scaling-stroke'><rect x='10' y='18' width='28' height='24' rx='1'/><path d='M10 26 H38 M19 18 V42 M28 18 V42'/></g>";

    private static string EnvelopeTaskMarker(string cssClass, bool filled)
        => $"<path class='{cssClass}' d='M9 20 H37 V38 H9 Z M9 20 L23 31 L37 20' fill='{(filled ? "var(--stencil-stroke)" : "none")}' stroke='var(--stencil-stroke)' stroke-width='2' stroke-linejoin='round' vector-effect='non-scaling-stroke'/>";

    private static string SubprocessMarker(string cssClass)
        => $"<g class='{cssClass}' fill='none' stroke='var(--stencil-stroke)' stroke-width='2' vector-effect='non-scaling-stroke'><rect x='42' y='76' width='16' height='12' rx='1'/><path d='M50 78 V86 M46 82 H54'/></g>";

    private static string EventRing(string kind, bool dashed = false)
    {
        var dash = dashed ? " stroke-dasharray='6 4'" : string.Empty;
        var strokeWidth = kind == "end" ? 5 : 2;
        var inner = kind == "intermediate"
            ? "<circle cx='50' cy='50' r='36' fill='none' stroke='var(--stencil-stroke)' stroke-width='2' vector-effect='non-scaling-stroke'/>"
            : string.Empty;
        return $"<circle cx='50' cy='50' r='46' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='{strokeWidth}'{dash} vector-effect='non-scaling-stroke'/>{inner}";
    }

    private static string MessageEventMarker(string cssClass)
        => $"<path class='{cssClass}' d='M28 37 H72 V63 H28 Z M28 37 L50 53 L72 37' fill='none' stroke='var(--stencil-stroke)' stroke-width='3' stroke-linejoin='round' vector-effect='non-scaling-stroke'/>";

    private static string TimerEventMarker(string cssClass)
        => $"<g class='{cssClass}' fill='none' stroke='var(--stencil-stroke)' stroke-width='3' vector-effect='non-scaling-stroke'><circle cx='50' cy='50' r='18'/><path d='M50 50 V36 M50 50 L61 56'/><path d='M50 32 V27 M50 73 V68 M27 50 H32 M68 50 H73'/></g>";

    private static string ErrorEventMarker(string cssClass)
        => $"<path class='{cssClass}' d='M37 68 L45 32 L54 48 L63 32 L56 68 L48 52 Z' fill='none' stroke='var(--stencil-stroke)' stroke-width='3' stroke-linejoin='round' vector-effect='non-scaling-stroke'/>";

    private static string SignalEventMarker(string cssClass)
        => $"<path class='{cssClass}' d='M50 28 L72 66 H28 Z' fill='none' stroke='var(--stencil-stroke)' stroke-width='3' stroke-linejoin='round' vector-effect='non-scaling-stroke'/>";

    private static string TerminateEventMarker(string cssClass)
        => $"<circle class='{cssClass}' cx='50' cy='50' r='19' fill='var(--stencil-stroke)' stroke='var(--stencil-stroke)' stroke-width='2' vector-effect='non-scaling-stroke'/>";

    private static string ExclusiveGatewayMarker(string cssClass)
        => $"<path class='{cssClass}' d='M33 33 L67 67 M67 33 L33 67' fill='none' stroke='var(--stencil-stroke)' stroke-width='7' stroke-linecap='round' vector-effect='non-scaling-stroke'/>";

    private static string ParallelGatewayMarker(string cssClass)
        => $"<path class='{cssClass}' d='M50 27 V73 M27 50 H73' fill='none' stroke='var(--stencil-stroke)' stroke-width='7' stroke-linecap='round' vector-effect='non-scaling-stroke'/>";

    private static string InclusiveGatewayMarker(string cssClass)
        => $"<circle class='{cssClass}' cx='50' cy='50' r='22' fill='none' stroke='var(--stencil-stroke)' stroke-width='6' vector-effect='non-scaling-stroke'/>";

    private static string EventBasedGatewayMarker(string cssClass)
        => $"<g class='{cssClass}' fill='none' stroke='var(--stencil-stroke)' stroke-width='3' vector-effect='non-scaling-stroke'><circle cx='50' cy='50' r='24'/><circle cx='50' cy='50' r='16'/><path d='M50 31 L56 44 L70 45 L60 55 L63 69 L50 62 L37 69 L40 55 L30 45 L44 44 Z'/></g>";

    private static string ComplexGatewayMarker(string cssClass)
        => $"<path class='{cssClass}' d='M50 26 V74 M26 50 H74 M33 33 L67 67 M67 33 L33 67' fill='none' stroke='var(--stencil-stroke)' stroke-width='5' stroke-linecap='round' vector-effect='non-scaling-stroke'/>";

    private static string EventIconMarker(string markerSvg)
        => string.IsNullOrWhiteSpace(markerSvg)
            ? string.Empty
            : "<circle cx='16' cy='16' r='4' fill='currentColor'/>";

    private static string GatewayIconMarker(string markerSvg)
        => markerSvg.Contains("exclusive", StringComparison.Ordinal)
            ? "<path d='M11 11 L21 21 M21 11 L11 21' stroke='currentColor' stroke-width='2'/>"
            : "<path d='M16 10 V22 M10 16 H22' stroke='currentColor' stroke-width='2'/>";

    private static string EdgeIcon(string startArrow, string endArrow, string? dashPattern)
    {
        var dash = dashPattern is "dashed" ? " stroke-dasharray='4 3'" : dashPattern is "dotted" ? " stroke-dasharray='1 3'" : string.Empty;
        var start = startArrow == "oval" ? "<circle cx='5' cy='16' r='3' fill='none' stroke='currentColor' stroke-width='2'/>" : string.Empty;
        var end = endArrow == "none" ? string.Empty : "<path d='M23 11 L28 16 L23 21 Z' fill='none' stroke='currentColor' stroke-width='2' stroke-linejoin='round'/>";
        return $"{start}<path d='M5 16 H28' fill='none' stroke='currentColor' stroke-width='2'{dash}/>{end}";
    }

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
