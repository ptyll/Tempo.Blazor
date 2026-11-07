namespace Tempo.Blazor.Components.Diagram.Models;

/// <summary>A directed edge (connection) between two nodes on the canvas.</summary>
public sealed class DiagramEdge
{
    /// <summary>Unique identifier (short Guid, e.g. "a3f8c21b").</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>Source node identifier. Null when the edge has a free-floating source.</summary>
    public string? SourceNodeId { get; set; }

    /// <summary>Target node identifier. Null when the edge has a free-floating target.</summary>
    public string? TargetNodeId { get; set; }

    /// <summary>Optional source port identifier.</summary>
    public string? SourcePortId { get; set; }

    /// <summary>Optional target port identifier.</summary>
    public string? TargetPortId { get; set; }

    /// <summary>Optional source edge identifier (for edge-to-edge connections).</summary>
    public string? SourceEdgeId { get; set; }

    /// <summary>Optional target edge identifier (for edge-to-edge connections).</summary>
    public string? TargetEdgeId { get; set; }

    /// <summary>Edge routing type. Supported: "straight", "orthogonal", "elbow", "segment", "curved".</summary>
    public string Routing { get; set; } = "straight";

    /// <summary>Elbow orientation. "auto" (default), "horizontal" (horizontal-first L-shape), or "vertical" (vertical-first L-shape).</summary>
    public string ElbowOrientation { get; set; } = "auto";

    /// <summary>Whether this edge uses manually placed waypoints and should not be auto-routed.</summary>
    public bool IsManuallyRouted { get; set; }

    /// <summary>Connector semantic type. (e.g. "association", "inheritance", "dependency", "composition", "aggregation").</summary>
    public string ConnectorType { get; set; } = "association";

    /// <summary>Edge shape type. Supported: "connector", "link", "filledEdge", "pipe", "wire".</summary>
    public string Shape { get; set; } = "connector";

    /// <summary>Intermediate waypoints for the edge path. Empty for straight lines.</summary>
    public List<DiagramPoint> Waypoints { get; set; } = [];

    /// <summary>Optional label rendered along the connector path.</summary>
    public string? Label { get; set; }

    /// <summary>Start arrowhead style. Supported: none, classic, block, open, oval, diamond, async.</summary>
    public string StartArrow { get; set; } = "none";

    /// <summary>End arrowhead style. Supported: none, classic, block, open, oval, diamond, async.</summary>
    public string EndArrow { get; set; } = "classic";

    /// <summary>Start arrowhead size in pixels.</summary>
    public double? StartArrowSize { get; set; }

    /// <summary>End arrowhead size in pixels.</summary>
    public double? EndArrowSize { get; set; }

    /// <summary>Whether the start arrowhead is filled. Null = use default based on arrowhead type.</summary>
    public bool? StartArrowFill { get; set; }

    /// <summary>Whether the end arrowhead is filled. Null = use default based on arrowhead type.</summary>
    public bool? EndArrowFill { get; set; }

    /// <summary>Whether to use rounded corners on the edge path.</summary>
    public bool Rounded { get; set; }

    /// <summary>Whether curved routing uses cubic Bézier (C command) instead of quadratic (Q command).</summary>
    public bool CubicBezier { get; set; }

    /// <summary>Corner radius in pixels for rounded edges. Null = use default (8px).</summary>
    public double? ArcSize { get; set; }

    /// <summary>Line jump style when edges cross. Supported: arc, gap, sharp, line.</summary>
    public string? JumpStyle { get; set; }

    /// <summary>Line jump size in pixels.</summary>
    public double? JumpSize { get; set; }

    /// <summary>Spacing between source node boundary and edge start.</summary>
    public double? SourceSpacing { get; set; }

    /// <summary>Spacing between target node boundary and edge end.</summary>
    public double? TargetSpacing { get; set; }

    /// <summary>Absolute position of the free-floating source point. Used when <see cref="SourceNodeId"/> is null.</summary>
    public DiagramPoint? SourcePoint { get; set; }

    /// <summary>Absolute position of the free-floating target point. Used when <see cref="TargetNodeId"/> is null.</summary>
    public DiagramPoint? TargetPoint { get; set; }

    /// <summary>Source connection constraint. When set, overrides port-based connection.</summary>
    public DiagramConnectionConstraint? SourceConstraint { get; set; }

    /// <summary>Target connection constraint. When set, overrides port-based connection.</summary>
    public DiagramConnectionConstraint? TargetConstraint { get; set; }

    /// <summary>Parameter t (0-1) along the source edge for edge-to-edge connections.</summary>
    public double? SourceEdgeT { get; set; }

    /// <summary>Parameter t (0-1) along the target edge for edge-to-edge connections.</summary>
    public double? TargetEdgeT { get; set; }

    /// <summary>Label position along the edge path as parameter t (0-1). Default 0.5.</summary>
    public double LabelPositionT { get; set; } = 0.5;

    /// <summary>Horizontal offset of the label from the point on the edge path (px).</summary>
    public double LabelOffsetX { get; set; }

    /// <summary>Vertical offset of the label from the point on the edge path (px).</summary>
    public double LabelOffsetY { get; set; }

    /// <summary>Source cardinality for ER diagrams.</summary>
    public string? SourceCardinality { get; set; }

    /// <summary>Target cardinality for ER diagrams.</summary>
    public string? TargetCardinality { get; set; }

    /// <summary>Visual style overrides for this edge.</summary>
    public DiagramStyle Style { get; set; } = new();

    /// <summary>Z-order index for edge layering. Higher values render on top.</summary>
    public int ZIndex { get; set; }

    /// <summary>Optional layer identifier. When null, the edge belongs to the default layer.</summary>
    public string? LayerId { get; set; }

    /// <summary>Reserved for future collaborative editing – who has this edge locked.</summary>
    public string? LockedBy { get; set; }

    /// <summary>Optional hyperlink target (external URL or page:// link).</summary>
    public string? Link { get; set; }

    /// <summary>
    /// Optional accessible name / pointer tooltip. The canvas renders it as the edge group's first
    /// <c>&lt;title&gt;</c> child, so a screen reader and a hovering pointer both get the text. Used by
    /// the read-only Modeling viewer to carry an overlay reading ("Name: value (band)") that the
    /// picture alone would not convey; null renders nothing.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>Validates that at least one end of the edge is connected (to a node or edge) or has an absolute point.</summary>
    public bool IsValid()
    {
        bool hasSourceConnection = !string.IsNullOrEmpty(SourceNodeId) || !string.IsNullOrEmpty(SourceEdgeId) || SourcePoint is not null;
        bool hasTargetConnection = !string.IsNullOrEmpty(TargetNodeId) || !string.IsNullOrEmpty(TargetEdgeId) || TargetPoint is not null;
        return hasSourceConnection && hasTargetConnection;
    }
}
