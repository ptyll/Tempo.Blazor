using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Services;

namespace Tempo.Blazor.Components.Diagram.Stencils;

/// <summary>
/// Registers built-in diagram stencil definitions for Tempo.Blazor.
/// Priority 0 â€“ custom providers with higher priority can override individual stencils.
/// </summary>
public sealed class BuiltInDiagramStencilProvider : IDiagramStencilProvider
{
    /// <inheritdoc/>
    public int Priority => 0;

    /// <inheritdoc/>
    public IEnumerable<DiagramStencilSet> GetStencilSets()
    {
        var sets = new List<DiagramStencilSet>
        {
            GeneralSet(),
            RelationshipsSet(),
            UmlSet(),
            BpmnSet(),
            SwimlaneSet(),
            TableSet(),
            FlowchartSet(),
            ErdSet(),
            NetworkSet(),
            C4Set(),
            ProjectSet(),
            BusinessAnalysisSet(),
            StrategySet()
        };

        var noCornerRadiusShapes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ellipse", "diamond", "cylinder", "triangle", "star", "hexagon",
            "parallelogram", "actor", "cloud", "lollipop", "component", "cube",
            "double-ellipse", "half-ellipse", "pentagon"
        };

        foreach (var set in sets)
        {
            foreach (var stencil in set.Stencils)
            {
                MarkAsTempoOriginal(stencil);

                if (stencil.Kind != DiagramStencilKind.Node)
                    continue;

                if (noCornerRadiusShapes.Contains(stencil.Layout.BackgroundShape))
                    stencil.Layout.SupportsCornerRadius = false;

                if (stencil.ConnectionPoints.Count == 0)
                    stencil.ConnectionPoints = GetConnectionPointsForShape(stencil.Layout.BackgroundShape);
            }
        }

        return sets;
    }

    private static void MarkAsTempoOriginal(DiagramStencil stencil)
    {
        stencil.Origin = DiagramStencilOrigin.TempoOriginal;
        stencil.ExternalAssetSourceId = null;
    }

    private static DiagramStencilSet GeneralSet()
    {
        return new DiagramStencilSet
        {
            Id = "general",
            Name = "General",
            Stencils =
            [
                new()
                {
                    Id = "general.rectangle",
                    Name = "Rectangle",
                    Category = "General",
                    IconSvg = "<rect x='4' y='8' width='24' height='16' rx='2' fill='none' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 120,
                    DefaultHeight = 60,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5, MagnetStrategy = "perimeter" },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5, MagnetStrategy = "perimeter" },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5, MagnetStrategy = "perimeter" },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5, MagnetStrategy = "perimeter" }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "Rectangle",
                                TextStyle = new() { TextAlign = StencilTextAlign.Center }
                            }
                        ]
                    }
                },
                new()
                {
                    Id = "general.rounded",
                    Name = "Rounded Rectangle",
                    Category = "General",
                    IconSvg = "<rect x='4' y='8' width='24' height='16' rx='6' fill='none' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 120,
                    DefaultHeight = 60,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5, MagnetStrategy = "perimeter" },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5, MagnetStrategy = "perimeter" },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5, MagnetStrategy = "perimeter" },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5, MagnetStrategy = "perimeter" }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rounded",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='12' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "Rounded",
                                TextStyle = new() { TextAlign = StencilTextAlign.Center }
                            }
                        ]
                    }
                },
                new()
                {
                    Id = "general.ellipse",
                    Name = "Ellipse",
                    Category = "General",
                    IconSvg = "<ellipse cx='16' cy='16' rx='12' ry='8' fill='none' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 120,
                    DefaultHeight = 80,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5, MagnetStrategy = "perimeter" },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5, MagnetStrategy = "perimeter" },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5, MagnetStrategy = "perimeter" },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5, MagnetStrategy = "perimeter" }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "ellipse",
                        ShapeSvg = "<ellipse cx='50' cy='50' rx='48' ry='48' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "Ellipse",
                                TextStyle = new() { TextAlign = StencilTextAlign.Center }
                            }
                        ]
                    }
                },
                new()
                {
                    Id = "general.text",
                    Name = "Text",
                    Category = "General",
                    IconSvg = "<text x='16' y='20' text-anchor='middle' font-size='12' fill='currentColor'>T</text>",
                    DefaultWidth = 80,
                    DefaultHeight = 30,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        Fill = "transparent",
                        Stroke = "transparent",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "Text",
                                TextStyle = new() { TextAlign = StencilTextAlign.Center }
                            }
                        ]
                    }
                },
                new()
                {
                    Id = "general.sticky-note",
                    Name = "Sticky Note",
                    Category = "General",
                    IconSvg = "<rect x='6' y='4' width='20' height='20' rx='1' fill='#fef08a' stroke='currentColor' stroke-width='1.5' transform='rotate(6 16 14)'/><path d='M22 22 L26 26' stroke='currentColor' stroke-width='1.5'/>",
                    DefaultWidth = 120,
                    DefaultHeight = 120,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "sticky-note",
                        ShapeSvg = "<rect x='7' y='7' width='86' height='86' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke' transform='rotate(6 50 50)'/><path d='M70 70 L94 94' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke' transform='rotate(6 50 50)'/>",
                        Fill = "#fef08a",
                        Stroke = "#ca8a04",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "Note",
                                Padding = 8,
                                TextStyle = new() { TextAlign = StencilTextAlign.Left, FontSize = 13 }
                            }
                        ]
                    },
                    DefaultData = new() { ["label"] = "Note" }
                },
                new()
                {
                    Id = "general.rhombus",
                    Name = "Rhombus",
                    Category = "General",
                    IconSvg = "<polygon points='16,4 28,16 16,28 4,16' fill='none' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 120,
                    DefaultHeight = 80,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5, MagnetStrategy = "perimeter" },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5, MagnetStrategy = "perimeter" },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5, MagnetStrategy = "perimeter" },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5, MagnetStrategy = "perimeter" }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "diamond",
                        ShapeSvg = "<polygon points='50,0 100,50 50,100 0,50' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "Rhombus",
                                TextStyle = new() { TextAlign = StencilTextAlign.Center }
                            }
                        ]
                    },
                    DefaultData = new() { ["label"] = "Rhombus" }
                },
                new()
                {
                    Id = "general.cylinder",
                    Name = "Cylinder",
                    Category = "General",
                    IconSvg = "<ellipse cx='16' cy='8' rx='10' ry='4' fill='none' stroke='currentColor' stroke-width='2'/><rect x='6' y='8' width='20' height='16' fill='none' stroke='currentColor' stroke-width='2'/><ellipse cx='16' cy='24' rx='10' ry='4' fill='none' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 100,
                    DefaultHeight = 120,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5, MagnetStrategy = "perimeter" },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5, MagnetStrategy = "perimeter" },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5, MagnetStrategy = "perimeter" },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5, MagnetStrategy = "perimeter" }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "cylinder",
                        ShapeSvg = "<ellipse cx='50' cy='15' rx='50' ry='15' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><rect x='0' y='15' width='100' height='70' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><ellipse cx='50' cy='85' rx='50' ry='15' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "Cylinder",
                                Padding = 8,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["label"] = "Cylinder" }
                },
                new()
                {
                    Id = "general.parallelogram",
                    Name = "Parallelogram",
                    Category = "General",
                    IconSvg = "<polygon points='8,4 28,4 24,28 4,28' fill='none' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 120,
                    DefaultHeight = 60,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5, MagnetStrategy = "perimeter" },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5, MagnetStrategy = "perimeter" },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5, MagnetStrategy = "perimeter" },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5, MagnetStrategy = "perimeter" }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "parallelogram",
                        ShapeSvg = "<polygon points='15,0 100,0 85,100 0,100' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "Parallelogram",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["label"] = "Parallelogram" }
                },
                new()
                {
                    Id = "general.group",
                    Name = "Group",
                    Category = "General",
                    IconSvg = "<rect x='4' y='8' width='24' height='16' rx='2' fill='none' stroke='currentColor' stroke-width='2' stroke-dasharray='4,2'/><text x='16' y='17' text-anchor='middle' font-size='6' fill='currentColor'>G</text>",
                    DefaultWidth = 160,
                    DefaultHeight = 120,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        Fill = "transparent",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke' stroke-dasharray='4,2'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "Group",
                                Padding = 8,
                                TextStyle = new() { TextAlign = StencilTextAlign.Left, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["label"] = "Group" }
                },
                new()
                {
                    Id = "general.triangle",
                    Name = "Triangle",
                    Category = "General",
                    IconSvg = "<polygon points='16,4 28,26 4,26' fill='none' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 100,
                    DefaultHeight = 90,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5, MagnetStrategy = "perimeter" },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5, MagnetStrategy = "perimeter" },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5, MagnetStrategy = "perimeter" },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5, MagnetStrategy = "perimeter" }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "triangle",
                        ShapeSvg = "<polygon points='50,2 98,98 2,98' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "Triangle",
                                Padding = 8,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["label"] = "Triangle" }
                },
                new()
                {
                    Id = "general.star",
                    Name = "Star",
                    Category = "General",
                    IconSvg = "<polygon points='16,3 19,12 29,12 21,18 24,28 16,22 8,28 11,18 3,12 13,12' fill='none' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 100,
                    DefaultHeight = 100,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5, MagnetStrategy = "perimeter" },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5, MagnetStrategy = "perimeter" },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5, MagnetStrategy = "perimeter" },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5, MagnetStrategy = "perimeter" }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "star",
                        ShapeSvg = "<polygon points='50,2 61,39 98,39 68,61 79,95 50,73 21,95 32,61 2,39 39,39' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "Star",
                                Padding = 8,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["label"] = "Star" }
                },
                new()
                {
                    Id = "general.hexagon",
                    Name = "Hexagon",
                    Category = "General",
                    IconSvg = "<polygon points='16,4 26,10 26,22 16,28 6,22 6,10' fill='none' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 110,
                    DefaultHeight = 100,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5, MagnetStrategy = "perimeter" },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5, MagnetStrategy = "perimeter" },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5, MagnetStrategy = "perimeter" },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5, MagnetStrategy = "perimeter" }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "hexagon",
                        ShapeSvg = "<polygon points='25,0 75,0 100,50 75,100 25,100 0,50' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "Hexagon",
                                Padding = 8,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["label"] = "Hexagon" }
                }
            ]
        };
    }

    private static DiagramStencilSet RelationshipsSet()
    {
        return new DiagramStencilSet
        {
            Id = "relationships",
            Name = "Relationships",
            NameResourceKey = "DiagramStencilSet_Relationships",
            Stencils =
            [
                new()
                {
                    Id = "relationships.association",
                    Name = "Association",
                    NameResourceKey = "DiagramStencil_RelationshipAssociation",
                    Category = "Relationships",
                    SetId = "relationships",
                    SetNameResourceKey = "DiagramStencilSet_Relationships",
                    PaletteId = "relationships.general",
                    PaletteNameResourceKey = "DiagramStencilPalette_RelationshipsGeneral",
                    PaletteOrder = 0,
                    Order = 0,
                    Kind = DiagramStencilKind.Edge,
                    IconSvg = "<path d='M4 16 H28' fill='none' stroke='currentColor' stroke-width='2' stroke-linecap='round'/>",
                    Keywords = ["connector", "relation", "line"],
                    EdgeDefaults = new()
                    {
                        Routing = "straight",
                        ConnectorType = "association",
                        Shape = "connector",
                        StartArrow = "none",
                        EndArrow = "none"
                    }
                },
                new()
                {
                    Id = "relationships.dependency",
                    Name = "Dependency",
                    NameResourceKey = "DiagramStencil_RelationshipDependency",
                    Category = "Relationships",
                    SetId = "relationships",
                    SetNameResourceKey = "DiagramStencilSet_Relationships",
                    PaletteId = "relationships.general",
                    PaletteNameResourceKey = "DiagramStencilPalette_RelationshipsGeneral",
                    PaletteOrder = 0,
                    Order = 1,
                    Kind = DiagramStencilKind.Edge,
                    IconSvg = "<path d='M4 16 H27' fill='none' stroke='currentColor' stroke-width='2' stroke-linecap='round' stroke-dasharray='4 3'/><path d='M23 11 L28 16 L23 21' fill='none' stroke='currentColor' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'/>",
                    Keywords = ["connector", "relation", "dashed", "uml"],
                    EdgeDefaults = new()
                    {
                        Routing = "orthogonal",
                        ConnectorType = "dependency",
                        Shape = "connector",
                        StartArrow = "none",
                        EndArrow = "open",
                        EndArrowFill = false,
                        Style = new DiagramStyle
                        {
                            StrokeDashPattern = "dashed"
                        }
                    }
                }
            ]
        };
    }

    private static DiagramStencilSet UmlSet()
    {
        return new DiagramStencilSet
        {
            Id = "uml",
            Name = "UML",
            Stencils =
            [
                new()
                {
                    Id = "uml.class",
                    Name = "Class",
                    Category = "UML",
                    IconSvg = "<rect x='4' y='4' width='24' height='24' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><line x1='4' y1='12' x2='28' y2='12' stroke='currentColor' stroke-width='2'/><line x1='4' y1='20' x2='28' y2='20' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 180,
                    DefaultHeight = 140,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.25 },
                        new() { Name = "top-mid", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "top-right", Side = PortSide.Top, Offset = 0.75 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.25 },
                        new() { Name = "bottom-mid", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "bottom-right", Side = PortSide.Bottom, Offset = 0.75 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "ClassName",
                                Padding = 8,
                                TextStyle = new()
                                {
                                    IsBold = true,
                                    TextAlign = StencilTextAlign.Center,
                                    FontSize = 13
                                }
                            },
                            new()
                            {
                                Type = "divider",
                                Padding = 0
                            },
                            new()
                            {
                                Type = "list",
                                DataKey = "attributes",
                                DefaultText = "- attribute: Type",
                                Padding = 8,
                                TextStyle = new()
                                {
                                    TextAlign = StencilTextAlign.Left,
                                    FontSize = 11,
                                    FontFamily = "ui-monospace, SFMono-Regular, Menlo, Consolas, monospace"
                                }
                            },
                            new()
                            {
                                Type = "divider",
                                Padding = 0
                            },
                            new()
                            {
                                Type = "list",
                                DataKey = "methods",
                                DefaultText = "+ method(): Type",
                                Padding = 8,
                                TextStyle = new()
                                {
                                    TextAlign = StencilTextAlign.Left,
                                    FontSize = 11,
                                    FontFamily = "ui-monospace, SFMono-Regular, Menlo, Consolas, monospace"
                                }
                            }
                        ]
                    },
                    DefaultData = new()
                    {
                        ["name"] = "ClassName",
                        ["attributes"] = new[] { "- id: Guid", "- name: string" },
                        ["methods"] = new[] { "+ Save(): void", "+ Load(): void" }
                    }
                },
                new()
                {
                    Id = "uml.package",
                    Name = "Package",
                    Category = "UML",
                    IconSvg = "<rect x='4' y='8' width='24' height='20' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><rect x='4' y='4' width='10' height='6' rx='1' fill='none' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 200,
                    DefaultHeight = 160,
                    IsCollapsible = true,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "package",
                        ShapeSvg = "<rect x='0' y='18' width='100' height='82' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><rect x='0' y='0' width='35' height='25' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Package",
                                Padding = 6,
                                TextStyle = new()
                                {
                                    IsBold = true,
                                    TextAlign = StencilTextAlign.Left,
                                    FontSize = 12
                                }
                            }
                        ]
                    },
                    DefaultData = new()
                    {
                        ["name"] = "Package"
                    }
                },
                new()
                {
                    Id = "uml.actor",
                    Name = "Actor",
                    Category = "UML",
                    IconSvg = "<circle cx='16' cy='8' r='3' fill='none' stroke='currentColor' stroke-width='2'/><line x1='16' y1='11' x2='16' y2='18' stroke='currentColor' stroke-width='2'/><line x1='11' y1='14' x2='21' y2='14' stroke='currentColor' stroke-width='2'/><line x1='13' y1='24' x2='16' y2='18' stroke='currentColor' stroke-width='2'/><line x1='19' y1='24' x2='16' y2='18' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 80,
                    DefaultHeight = 120,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "actor",
                        ShapeSvg = "<circle cx='50' cy='18' r='10' fill='none' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='50' y1='28' x2='50' y2='60' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='20' y1='40' x2='80' y2='40' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='25' y1='90' x2='50' y2='60' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='75' y1='90' x2='50' y2='60' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        PreserveAspectRatio = true,
                        ContentPosition = "below",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Actor",
                                Padding = 4,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Actor" }
                },
                new()
                {
                    Id = "uml.use-case",
                    Name = "Use Case",
                    Category = "UML",
                    IconSvg = "<ellipse cx='16' cy='16' rx='12' ry='8' fill='none' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 140,
                    DefaultHeight = 80,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "ellipse",
                        ShapeSvg = "<ellipse cx='50' cy='50' rx='38' ry='25' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='2' vector-effect='non-scaling-stroke'/><ellipse cx='50' cy='50' rx='28' ry='19' fill='none' stroke='var(--stencil-stroke)' stroke-width='1.5' vector-effect='non-scaling-stroke'/><ellipse cx='50' cy='50' rx='19' ry='13' fill='none' stroke='var(--stencil-stroke)' stroke-width='1' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Use Case",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Use Case" }
                },
                new()
                {
                    Id = "uml.enum",
                    Name = "Enum",
                    Category = "UML",
                    IconSvg = "<rect x='4' y='4' width='24' height='24' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><line x1='4' y1='12' x2='28' y2='12' stroke='currentColor' stroke-width='2'/><line x1='4' y1='20' x2='28' y2='20' stroke='currentColor' stroke-width='2'/><text x='16' y='10' text-anchor='middle' font-size='5' fill='currentColor'>&lt;&lt;enum&gt;&gt;</text>",
                    DefaultWidth = 160,
                    DefaultHeight = 120,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><text x='50' y='22' text-anchor='middle' font-size='10' fill='var(--stencil-stroke)'>&lt;&lt;enum&gt;&gt;</text>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "EnumName",
                                Padding = 6,
                                TextStyle = new()
                                {
                                    IsBold = true,
                                    TextAlign = StencilTextAlign.Center,
                                    FontSize = 12
                                }
                            },
                            new() { Type = "divider", Padding = 0 },
                            new()
                            {
                                Type = "list",
                                DataKey = "values",
                                DefaultText = "Value1",
                                Padding = 6,
                                TextStyle = new()
                                {
                                    TextAlign = StencilTextAlign.Left,
                                    FontSize = 11,
                                    FontFamily = "ui-monospace, SFMono-Regular, Menlo, Consolas, monospace"
                                }
                            }
                        ]
                    },
                    DefaultData = new()
                    {
                        ["name"] = "EnumName",
                        ["values"] = new[] { "Value1", "Value2", "Value3" }
                    }
                },
                new()
                {
                    Id = "uml.interface",
                    Name = "Interface",
                    Category = "UML",
                    IconSvg = "<circle cx='22' cy='16' r='5' fill='none' stroke='currentColor' stroke-width='2'/><line x1='4' y1='16' x2='17' y2='16' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 100,
                    DefaultHeight = 40,
                    Ports =
                    [
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "lollipop",
                        ShapeSvg = "<circle cx='80' cy='20' r='16' fill='none' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='0' y1='20' x2='64' y2='20' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "IInterface",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Left, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "IInterface" }
                },
                new()
                {
                    Id = "uml.note",
                    Name = "Note",
                    Category = "UML",
                    IconSvg = "<rect x='4' y='4' width='20' height='20' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><path d='M24 4 L24 16 L16 24' fill='none' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 120,
                    DefaultHeight = 80,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "note",
                        ShapeSvg = "<path d='M0,0 L80,0 L100,20 L100,100 L0,100 Z' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><path d='M80,0 L80,20 L100,20' fill='none' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "Note",
                                Padding = 8,
                                TextStyle = new() { TextAlign = StencilTextAlign.Left, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["label"] = "Note" }
                },
                new()
                {
                    Id = "uml.abstract-class",
                    Name = "Abstract Class",
                    Category = "UML",
                    IconSvg = "<rect x='4' y='4' width='24' height='24' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><line x1='4' y1='12' x2='28' y2='12' stroke='currentColor' stroke-width='2'/><line x1='4' y1='20' x2='28' y2='20' stroke='currentColor' stroke-width='2'/><text x='16' y='10' text-anchor='middle' font-size='5' fill='currentColor'>&lt;&lt;abstract&gt;&gt;</text>",
                    DefaultWidth = 180,
                    DefaultHeight = 140,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><text x='50' y='22' text-anchor='middle' font-size='9' fill='var(--stencil-stroke)'>&lt;&lt;abstract&gt;&gt;</text>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "stereotype",
                                DefaultText = "<<abstract>>",
                                Padding = 4,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 10, FontFamily = "ui-monospace, SFMono-Regular, Menlo, Consolas, monospace" }
                            },
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "AbstractClass",
                                Padding = 4,
                                TextStyle = new() { IsBold = true, TextAlign = StencilTextAlign.Center, FontSize = 13 }
                            },
                            new() { Type = "divider", Padding = 0 },
                            new()
                            {
                                Type = "list",
                                DataKey = "attributes",
                                DefaultText = "- attribute: Type",
                                Padding = 8,
                                TextStyle = new() { TextAlign = StencilTextAlign.Left, FontSize = 11, FontFamily = "ui-monospace, SFMono-Regular, Menlo, Consolas, monospace" }
                            },
                            new() { Type = "divider", Padding = 0 },
                            new()
                            {
                                Type = "list",
                                DataKey = "methods",
                                DefaultText = "+ method(): Type",
                                Padding = 8,
                                TextStyle = new() { TextAlign = StencilTextAlign.Left, FontSize = 11, FontFamily = "ui-monospace, SFMono-Regular, Menlo, Consolas, monospace" }
                            }
                        ]
                    },
                    DefaultData = new()
                    {
                        ["stereotype"] = "<<abstract>>",
                        ["name"] = "AbstractClass",
                        ["attributes"] = new[] { "- id: Guid", "- name: string" },
                        ["methods"] = new[] { "+ Save(): void", "+ Load(): void" }
                    }
                },
                new()
                {
                    Id = "uml.component",
                    Name = "Component",
                    Category = "UML",
                    IconSvg = "<rect x='4' y='4' width='24' height='24' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><rect x='2' y='8' width='4' height='4' rx='1' fill='none' stroke='currentColor' stroke-width='1.5'/><rect x='2' y='20' width='4' height='4' rx='1' fill='none' stroke='currentColor' stroke-width='1.5'/>",
                    DefaultWidth = 180,
                    DefaultHeight = 140,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "component",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><rect x='6' y='12' width='14' height='10' rx='1' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><rect x='6' y='78' width='14' height='10' rx='1' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "stereotype",
                                DefaultText = "<<component>>",
                                Padding = 4,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 10, FontFamily = "ui-monospace, SFMono-Regular, Menlo, Consolas, monospace" }
                            },
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Component",
                                Padding = 4,
                                TextStyle = new() { IsBold = true, TextAlign = StencilTextAlign.Center, FontSize = 13 }
                            },
                            new() { Type = "divider", Padding = 0 },
                            new()
                            {
                                Type = "list",
                                DataKey = "attributes",
                                DefaultText = "- attribute: Type",
                                Padding = 8,
                                TextStyle = new() { TextAlign = StencilTextAlign.Left, FontSize = 11, FontFamily = "ui-monospace, SFMono-Regular, Menlo, Consolas, monospace" }
                            },
                            new() { Type = "divider", Padding = 0 },
                            new()
                            {
                                Type = "list",
                                DataKey = "methods",
                                DefaultText = "+ method(): Type",
                                Padding = 8,
                                TextStyle = new() { TextAlign = StencilTextAlign.Left, FontSize = 11, FontFamily = "ui-monospace, SFMono-Regular, Menlo, Consolas, monospace" }
                            }
                        ]
                    },
                    DefaultData = new()
                    {
                        ["stereotype"] = "<<component>>",
                        ["name"] = "Component",
                        ["attributes"] = new[] { "- id: Guid", "- name: string" },
                        ["methods"] = new[] { "+ Run(): void" }
                    }
                },
                new()
                {
                    Id = "uml.node",
                    Name = "Node",
                    Category = "UML",
                    IconSvg = "<polygon points='8,10 24,4 28,12 12,18' fill='none' stroke='currentColor' stroke-width='2'/><line x1='8' y1='10' x2='8' y2='24' stroke='currentColor' stroke-width='2'/><line x1='12' y1='18' x2='12' y2='28' stroke='currentColor' stroke-width='2'/><line x1='28' y1='12' x2='28' y2='22' stroke='currentColor' stroke-width='2'/><line x1='8' y1='24' x2='24' y2='18' stroke='currentColor' stroke-width='2'/><line x1='24' y1='18' x2='28' y2='22' stroke='currentColor' stroke-width='2'/><line x1='8' y1='24' x2='12' y2='28' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 120,
                    DefaultHeight = 100,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "cube",
                        ShapeSvg = "<path d='M15,32 L50,12 L85,32 L85,68 L50,88 L15,68 Z' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><path d='M15,32 L50,50 L85,32' fill='none' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><path d='M50,50 L50,88' fill='none' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Node",
                                Padding = 8,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12, IsBold = true }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Node" }
                },
                new()
                {
                    Id = "uml.artifact",
                    Name = "Artifact",
                    Category = "UML",
                    IconSvg = "<rect x='4' y='4' width='20' height='20' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><path d='M24 4 L24 16 L16 24' fill='none' stroke='currentColor' stroke-width='2'/><text x='14' y='17' text-anchor='middle' font-size='8' fill='currentColor'>&lt;&lt;artifact&gt;&gt;</text>",
                    DefaultWidth = 120,
                    DefaultHeight = 80,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "document",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='80' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><path d='M100 0 L100 48 L60 80' fill='none' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Artifact",
                                Padding = 8,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Artifact" }
                },
                new()
                {
                    Id = "uml.deployment-spec",
                    Name = "Deployment Specification",
                    Category = "UML",
                    IconSvg = "<rect x='4' y='8' width='24' height='20' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><rect x='4' y='4' width='8' height='6' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='22' text-anchor='middle' font-size='5' fill='currentColor'>&lt;&lt;deployment spec&gt;&gt;</text>",
                    DefaultWidth = 160,
                    DefaultHeight = 100,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "package",
                        ShapeSvg = "<rect x='0' y='15' width='100' height='85' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><rect x='0' y='0' width='30' height='20' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><text x='15' y='13' text-anchor='middle' font-size='9' fill='var(--stencil-stroke)'>sd</text>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "DeploymentSpec",
                                Padding = 6,
                                TextStyle = new() { IsBold = true, TextAlign = StencilTextAlign.Center, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "DeploymentSpec" }
                },
                new()
                {
                    Id = "uml.activity-start",
                    Name = "Activity Start",
                    Category = "UML",
                    IconSvg = "<circle cx='16' cy='16' r='10' fill='currentColor' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 60,
                    DefaultHeight = 60,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "ellipse",
                        ShapeSvg = "<circle cx='50' cy='50' r='46' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        // TG-004: the UML initial node is a SOLID ink disc — the fill is the stencil
                        // stroke token and the label ink the stencil fill, so the pair keeps its
                        // contrast in both themes (dark disc + light text in light, inverted in dark).
                        Fill = "var(--stencil-stroke)",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 10, Color = "var(--stencil-fill)" }
                            }
                        ]
                    },
                    DefaultData = new() { ["label"] = "" }
                },
                new()
                {
                    Id = "uml.activity-end",
                    Name = "Activity End",
                    Category = "UML",
                    IconSvg = "<circle cx='16' cy='16' r='10' fill='none' stroke='currentColor' stroke-width='2'/><circle cx='16' cy='16' r='6' fill='none' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 60,
                    DefaultHeight = 60,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "double-ellipse",
                        ShapeSvg = "<ellipse cx='50' cy='50' rx='48' ry='48' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><ellipse cx='50' cy='50' rx='38' ry='38' fill='none' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 10 }
                            }
                        ]
                    },
                    DefaultData = new() { ["label"] = "" }
                },
                new()
                {
                    Id = "uml.activity-action",
                    Name = "Action",
                    Category = "UML",
                    IconSvg = "<rect x='4' y='8' width='24' height='16' rx='4' fill='none' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 120,
                    DefaultHeight = 60,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rounded",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='12' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "Action",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["label"] = "Action" }
                },
                new()
                {
                    Id = "uml.activity-decision",
                    Name = "Decision",
                    Category = "UML",
                    IconSvg = "<polygon points='16,4 28,16 16,28 4,16' fill='none' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 80,
                    DefaultHeight = 80,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "diamond",
                        ShapeSvg = "<polygon points='50,2 98,50 50,98 2,50' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 11 }
                            }
                        ]
                    },
                    DefaultData = new() { ["label"] = "" }
                },
                new()
                {
                    Id = "uml.frame",
                    Name = "Frame",
                    Category = "UML",
                    IconSvg = "<rect x='4' y='8' width='24' height='20' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><rect x='4' y='4' width='10' height='6' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><text x='9' y='8' font-size='4' fill='currentColor'>sd</text>",
                    DefaultWidth = 240,
                    DefaultHeight = 160,
                    IsCollapsible = true,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "package",
                        ShapeSvg = "<rect x='0' y='18' width='100' height='82' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><rect x='0' y='0' width='30' height='20' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><text x='15' y='13' text-anchor='middle' font-size='9' fill='var(--stencil-stroke)'>sd</text>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Frame",
                                Padding = 6,
                                TextStyle = new() { IsBold = true, TextAlign = StencilTextAlign.Left, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Frame" }
                },
                new()
                {
                    Id = "uml.lifeline",
                    Name = "Lifeline",
                    Category = "UML",
                    IconSvg = "<rect x='4' y='4' width='24' height='8' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><line x1='16' y1='12' x2='16' y2='28' stroke='currentColor' stroke-width='2' stroke-dasharray='2 2'/>",
                    DefaultWidth = 100,
                    DefaultHeight = 300,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='30' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='50' y1='30' x2='50' y2='100' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' stroke-dasharray='4 2' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = ":Object",
                                Padding = 4,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12, IsBold = true }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = ":Object" }
                },
                new()
                {
                    Id = "archimate.business-actor",
                    Name = "Business Actor",
                    Category = "UML",
                    IconSvg = "<circle cx='16' cy='8' r='3' fill='none' stroke='currentColor' stroke-width='2'/><line x1='16' y1='11' x2='16' y2='18' stroke='currentColor' stroke-width='2'/><line x1='11' y1='14' x2='21' y2='14' stroke='currentColor' stroke-width='2'/><line x1='13' y1='24' x2='16' y2='18' stroke='currentColor' stroke-width='2'/><line x1='19' y1='24' x2='16' y2='18' stroke='currentColor' stroke-width='2'/><text x='16' y='28' text-anchor='middle' font-size='4' fill='currentColor'>&lt;&lt;actor&gt;&gt;</text>",
                    DefaultWidth = 80,
                    DefaultHeight = 120,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "actor",
                        ShapeSvg = "<circle cx='50' cy='18' r='10' fill='none' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='50' y1='28' x2='50' y2='60' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='20' y1='40' x2='80' y2='40' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='25' y1='90' x2='50' y2='60' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='75' y1='90' x2='50' y2='60' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Business Actor",
                                Padding = 4,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 11 }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Business Actor" }
                },
                new()
                {
                    Id = "archimate.business-role",
                    Name = "Business Role",
                    Category = "UML",
                    IconSvg = "<ellipse cx='16' cy='16' rx='12' ry='8' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='20' text-anchor='middle' font-size='5' fill='currentColor'>&lt;&lt;role&gt;&gt;</text>",
                    DefaultWidth = 140,
                    DefaultHeight = 80,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "ellipse",
                        ShapeSvg = "<ellipse cx='50' cy='50' rx='48' ry='46' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><text x='50' y='22' text-anchor='middle' font-size='9' fill='var(--stencil-stroke)'>&lt;&lt;role&gt;&gt;</text>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Business Role",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 11 }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Business Role" }
                },
                new()
                {
                    Id = "archimate.business-process",
                    Name = "Business Process",
                    Category = "UML",
                    IconSvg = "<rect x='4' y='8' width='24' height='16' rx='4' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='20' text-anchor='middle' font-size='5' fill='currentColor'>&lt;&lt;process&gt;&gt;</text>",
                    DefaultWidth = 160,
                    DefaultHeight = 80,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rounded",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><text x='50' y='18' text-anchor='middle' font-size='8' fill='var(--stencil-stroke)'>&lt;&lt;business process&gt;&gt;</text>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Business Process",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Business Process" }
                },
                new()
                {
                    Id = "archimate.business-function",
                    Name = "Business Function",
                    Category = "UML",
                    IconSvg = "<rect x='4' y='8' width='24' height='16' rx='4' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='20' text-anchor='middle' font-size='5' fill='currentColor'>&lt;&lt;function&gt;&gt;</text>",
                    DefaultWidth = 160,
                    DefaultHeight = 80,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rounded",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><text x='50' y='18' text-anchor='middle' font-size='8' fill='var(--stencil-stroke)'>&lt;&lt;business function&gt;&gt;</text>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Business Function",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Business Function" }
                },
                new()
                {
                    Id = "archimate.business-service",
                    Name = "Business Service",
                    Category = "UML",
                    IconSvg = "<rect x='4' y='8' width='24' height='16' rx='8' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='20' text-anchor='middle' font-size='5' fill='currentColor'>&lt;&lt;service&gt;&gt;</text>",
                    DefaultWidth = 160,
                    DefaultHeight = 80,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rounded",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><text x='50' y='18' text-anchor='middle' font-size='8' fill='var(--stencil-stroke)'>&lt;&lt;business service&gt;&gt;</text>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Business Service",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Business Service" }
                },
                new()
                {
                    Id = "archimate.business-object",
                    Name = "Business Object",
                    Category = "UML",
                    IconSvg = "<rect x='4' y='4' width='24' height='24' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='16' text-anchor='middle' font-size='5' fill='currentColor'>&lt;&lt;object&gt;&gt;</text>",
                    DefaultWidth = 120,
                    DefaultHeight = 80,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><text x='50' y='18' text-anchor='middle' font-size='8' fill='var(--stencil-stroke)'>&lt;&lt;business object&gt;&gt;</text>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Business Object",
                                Padding = 8,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Business Object" }
                },
                new()
                {
                    Id = "archimate.business-event",
                    Name = "Business Event",
                    Category = "UML",
                    IconSvg = "<polygon points='16,4 28,14 24,28 8,28 4,14' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='20' text-anchor='middle' font-size='5' fill='currentColor'>&lt;&lt;event&gt;&gt;</text>",
                    DefaultWidth = 120,
                    DefaultHeight = 80,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "pentagon",
                        ShapeSvg = "<polygon points='50,0 100,40 80,100 20,100 0,40' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Business Event",
                                Padding = 8,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Business Event" }
                },
                new()
                {
                    Id = "archimate.application-component",
                    Name = "Application Component",
                    Category = "UML",
                    IconSvg = "<rect x='4' y='4' width='24' height='24' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><rect x='2' y='8' width='4' height='4' rx='1' fill='none' stroke='currentColor' stroke-width='1.5'/><rect x='2' y='20' width='4' height='4' rx='1' fill='none' stroke='currentColor' stroke-width='1.5'/><text x='16' y='16' text-anchor='middle' font-size='4' fill='currentColor'>&lt;&lt;component&gt;&gt;</text>",
                    DefaultWidth = 160,
                    DefaultHeight = 100,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "component",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><rect x='6' y='12' width='14' height='10' rx='1' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><rect x='6' y='78' width='14' height='10' rx='1' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "App Component",
                                Padding = 8,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "App Component" }
                },
                new()
                {
                    Id = "archimate.application-service",
                    Name = "Application Service",
                    Category = "UML",
                    IconSvg = "<rect x='4' y='8' width='24' height='16' rx='8' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='20' text-anchor='middle' font-size='5' fill='currentColor'>&lt;&lt;service&gt;&gt;</text>",
                    DefaultWidth = 160,
                    DefaultHeight = 80,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rounded",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><text x='50' y='18' text-anchor='middle' font-size='8' fill='var(--stencil-stroke)'>&lt;&lt;application service&gt;&gt;</text>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "App Service",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "App Service" }
                },
                new()
                {
                    Id = "archimate.application-interface",
                    Name = "Application Interface",
                    Category = "UML",
                    IconSvg = "<circle cx='22' cy='16' r='5' fill='none' stroke='currentColor' stroke-width='2'/><line x1='4' y1='16' x2='17' y2='16' stroke='currentColor' stroke-width='2'/><text x='14' y='12' text-anchor='middle' font-size='4' fill='currentColor'>&lt;&lt;interface&gt;&gt;</text>",
                    DefaultWidth = 120,
                    DefaultHeight = 50,
                    Ports =
                    [
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "lollipop",
                        ShapeSvg = "<circle cx='80' cy='20' r='16' fill='none' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='0' y1='20' x2='64' y2='20' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "App Interface",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Left, FontSize = 11 }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "App Interface" }
                },
                new()
                {
                    Id = "archimate.application-function",
                    Name = "Application Function",
                    Category = "UML",
                    IconSvg = "<rect x='4' y='8' width='24' height='16' rx='4' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='20' text-anchor='middle' font-size='5' fill='currentColor'>&lt;&lt;function&gt;&gt;</text>",
                    DefaultWidth = 160,
                    DefaultHeight = 80,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rounded",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><text x='50' y='18' text-anchor='middle' font-size='8' fill='var(--stencil-stroke)'>&lt;&lt;application function&gt;&gt;</text>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "App Function",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "App Function" }
                },
                new()
                {
                    Id = "archimate.data-object",
                    Name = "Data Object",
                    Category = "UML",
                    IconSvg = "<rect x='4' y='4' width='24' height='24' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='16' text-anchor='middle' font-size='5' fill='currentColor'>&lt;&lt;data&gt;&gt;</text>",
                    DefaultWidth = 120,
                    DefaultHeight = 80,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><text x='50' y='18' text-anchor='middle' font-size='8' fill='var(--stencil-stroke)'>&lt;&lt;data object&gt;&gt;</text>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Data Object",
                                Padding = 8,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Data Object" }
                },
                new()
                {
                    Id = "archimate.goal",
                    Name = "Goal",
                    Category = "UML",
                    IconSvg = "<circle cx='16' cy='16' r='10' fill='none' stroke='currentColor' stroke-width='2'/><circle cx='16' cy='16' r='5' fill='currentColor'/><text x='16' y='30' text-anchor='middle' font-size='4' fill='currentColor'>&lt;&lt;goal&gt;&gt;</text>",
                    DefaultWidth = 100,
                    DefaultHeight = 100,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "double-ellipse",
                        ShapeSvg = "<ellipse cx='50' cy='50' rx='48' ry='48' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><ellipse cx='50' cy='50' rx='38' ry='38' fill='none' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Goal",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 11 }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Goal" }
                },
                new()
                {
                    Id = "archimate.principle",
                    Name = "Principle",
                    Category = "UML",
                    IconSvg = "<polygon points='16,4 28,26 4,26' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='20' text-anchor='middle' font-size='5' fill='currentColor'>&lt;&lt;principle&gt;&gt;</text>",
                    DefaultWidth = 120,
                    DefaultHeight = 90,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "triangle",
                        ShapeSvg = "<polygon points='50,2 98,98 2,98' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Principle",
                                Padding = 8,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Principle" }
                },
                new()
                {
                    Id = "archimate.stakeholder",
                    Name = "Stakeholder",
                    Category = "UML",
                    IconSvg = "<circle cx='16' cy='8' r='3' fill='none' stroke='currentColor' stroke-width='2'/><line x1='16' y1='11' x2='16' y2='18' stroke='currentColor' stroke-width='2'/><line x1='11' y1='14' x2='21' y2='14' stroke='currentColor' stroke-width='2'/><line x1='13' y1='24' x2='16' y2='18' stroke='currentColor' stroke-width='2'/><line x1='19' y1='24' x2='16' y2='18' stroke='currentColor' stroke-width='2'/><text x='16' y='28' text-anchor='middle' font-size='4' fill='currentColor'>&lt;&lt;stakeholder&gt;&gt;</text>",
                    DefaultWidth = 80,
                    DefaultHeight = 120,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "actor",
                        ShapeSvg = "<circle cx='50' cy='18' r='10' fill='none' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='50' y1='28' x2='50' y2='60' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='20' y1='40' x2='80' y2='40' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='25' y1='90' x2='50' y2='60' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='75' y1='90' x2='50' y2='60' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Stakeholder",
                                Padding = 4,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 11 }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Stakeholder" }
                }
            ]
        };
    }

    private static DiagramStencilSet BpmnSet()
    {
        return new DiagramStencilSet
        {
            Id = "bpmn",
            Name = "BPMN",
            Stencils =
            [
                new()
                {
                    Id = "bpmn.task",
                    Name = "Task",
                    Category = "BPMN",
                    IconSvg = "<rect x='4' y='8' width='24' height='16' rx='4' fill='none' stroke='currentColor' stroke-width='2'/><rect x='9' y='12' width='5' height='5' rx='1' fill='currentColor'/><line x1='18' y1='14' x2='25' y2='14' stroke='currentColor' stroke-width='2'/><line x1='18' y1='18' x2='25' y2='18' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 120,
                    DefaultHeight = 80,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rounded",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><text x='50' y='55' text-anchor='middle' font-size='14' fill='var(--stencil-stroke)'>BPMN</text>",
                        Sections =
                        [
                            new()
                            {
                                Type = "icon",
                                DataKey = "icon",
                                DefaultText = "⚙",
                                Padding = 8,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 16 }
                            },
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "Task",
                                Padding = 4,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new()
                    {
                        ["icon"] = "⚙",
                        ["label"] = "Task"
                    }
                },
                new()
                {
                    Id = "bpmn.gateway",
                    Name = "Gateway",
                    Category = "BPMN",
                    IconSvg = "<polygon points='16,4 28,16 16,28 4,16' fill='none' stroke='currentColor' stroke-width='2'/><line x1='16' y1='10' x2='16' y2='22' stroke='currentColor' stroke-width='2'/><line x1='10' y1='16' x2='22' y2='16' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 80,
                    DefaultHeight = 80,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "diamond",
                        ShapeSvg = "<polygon points='50,10 90,50 50,90 10,50' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='30' y1='50' x2='70' y2='50' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='50' y1='30' x2='50' y2='70' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "icon",
                                DataKey = "icon",
                                DefaultText = "✕",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 22 }
                            }
                        ]
                    },
                    DefaultData = new()
                    {
                        ["icon"] = "✕"
                    }
                },
                new()
                {
                    Id = "bpmn.event",
                    Name = "Event",
                    Category = "BPMN",
                    IconSvg = "<circle cx='16' cy='16' r='10' fill='none' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 80,
                    DefaultHeight = 80,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "ellipse",
                        ShapeSvg = "<ellipse cx='50' cy='50' rx='48' ry='48' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "Start",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 11 }
                            }
                        ]
                    },
                    DefaultData = new()
                    {
                        ["label"] = "Start"
                    }
                },
                new()
                {
                    Id = "bpmn.pool",
                    Name = "Pool",
                    Category = "BPMN",
                    IconSvg = "<rect x='4' y='4' width='24' height='20' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><line x1='10' y1='4' x2='10' y2='24' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 300,
                    DefaultHeight = 180,
                    IsCollapsible = true,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "pool",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='30' y1='0' x2='30' y2='100' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "Pool",
                                Padding = 4,
                                TextStyle = new()
                                {
                                    IsBold = true,
                                    TextAlign = StencilTextAlign.Center,
                                    FontSize = 12
                                }
                            },
                            new()
                            {
                                Type = "divider",
                                Padding = 0
                            },
                            new()
                            {
                                Type = "text",
                                DataKey = "lane",
                                DefaultText = "Lane 1",
                                Padding = 8,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 11 }
                            }
                        ]
                    },
                    DefaultData = new()
                    {
                        ["label"] = "Pool",
                        ["lane"] = "Lane 1"
                    }
                },
                new()
                {
                    Id = "bpmn.user-task",
                    Name = "User Task",
                    Category = "BPMN",
                    IconSvg = "<rect x='4' y='8' width='24' height='16' rx='4' fill='none' stroke='currentColor' stroke-width='2'/><circle cx='10' cy='16' r='2' fill='currentColor'/><line x1='14' y1='15' x2='24' y2='15' stroke='currentColor' stroke-width='1.5'/><line x1='14' y1='19' x2='24' y2='19' stroke='currentColor' stroke-width='1.5'/>",
                    DefaultWidth = 120,
                    DefaultHeight = 80,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rounded",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='12' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><path d='M55 25 A8 8 0 0 1 55 41 A8 8 0 0 1 55 25' fill='none' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><path d='M45 45 L65 45 L65 60 L45 60 Z' fill='none' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "icon",
                                DataKey = "icon",
                                DefaultText = "👤",
                                Padding = 6,
                                TextStyle = new() { TextAlign = StencilTextAlign.Left, FontSize = 14 }
                            },
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "User Task",
                                Padding = 4,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["icon"] = "👤", ["label"] = "User Task" }
                },
                new()
                {
                    Id = "bpmn.service-task",
                    Name = "Service Task",
                    Category = "BPMN",
                    IconSvg = "<rect x='4' y='8' width='24' height='16' rx='4' fill='none' stroke='currentColor' stroke-width='2'/><rect x='8' y='12' width='6' height='6' rx='1' fill='currentColor'/><line x1='16' y1='14' x2='24' y2='14' stroke='currentColor' stroke-width='1.5'/><line x1='16' y1='18' x2='24' y2='18' stroke='currentColor' stroke-width='1.5'/>",
                    DefaultWidth = 120,
                    DefaultHeight = 80,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rounded",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='12' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><rect x='42' y='30' width='16' height='16' rx='2' fill='none' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='46' y1='38' x2='54' y2='38' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "icon",
                                DataKey = "icon",
                                DefaultText = "⚙",
                                Padding = 6,
                                TextStyle = new() { TextAlign = StencilTextAlign.Left, FontSize = 14 }
                            },
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "Service Task",
                                Padding = 4,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["icon"] = "⚙", ["label"] = "Service Task" }
                },
                new()
                {
                    Id = "bpmn.send-task",
                    Name = "Send Task",
                    Category = "BPMN",
                    IconSvg = "<rect x='4' y='8' width='24' height='16' rx='4' fill='none' stroke='currentColor' stroke-width='2'/><polygon points='8,14 14,18 8,22' fill='currentColor'/><line x1='16' y1='15' x2='24' y2='15' stroke='currentColor' stroke-width='1.5'/><line x1='16' y1='19' x2='24' y2='19' stroke='currentColor' stroke-width='1.5'/>",
                    DefaultWidth = 120,
                    DefaultHeight = 80,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rounded",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='12' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><path d='M40 35 L60 35 L50 50 Z' fill='none' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "icon",
                                DataKey = "icon",
                                DefaultText = "📤",
                                Padding = 6,
                                TextStyle = new() { TextAlign = StencilTextAlign.Left, FontSize = 14 }
                            },
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "Send Task",
                                Padding = 4,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["icon"] = "📤", ["label"] = "Send Task" }
                },
                new()
                {
                    Id = "bpmn.receive-task",
                    Name = "Receive Task",
                    Category = "BPMN",
                    IconSvg = "<rect x='4' y='8' width='24' height='16' rx='4' fill='none' stroke='currentColor' stroke-width='2'/><polygon points='14,14 8,18 14,22' fill='currentColor'/><line x1='16' y1='15' x2='24' y2='15' stroke='currentColor' stroke-width='1.5'/><line x1='16' y1='19' x2='24' y2='19' stroke='currentColor' stroke-width='1.5'/>",
                    DefaultWidth = 120,
                    DefaultHeight = 80,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rounded",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='12' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><path d='M40 35 L50 48 L60 35' fill='none' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "icon",
                                DataKey = "icon",
                                DefaultText = "📥",
                                Padding = 6,
                                TextStyle = new() { TextAlign = StencilTextAlign.Left, FontSize = 14 }
                            },
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "Receive Task",
                                Padding = 4,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["icon"] = "📥", ["label"] = "Receive Task" }
                },
                new()
                {
                    Id = "bpmn.start-event",
                    Name = "Start Event",
                    Category = "BPMN",
                    IconSvg = "<circle cx='16' cy='16' r='10' fill='none' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 60,
                    DefaultHeight = 60,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "ellipse",
                        ShapeSvg = "<ellipse cx='50' cy='50' rx='48' ry='48' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 9 }
                            }
                        ]
                    },
                    DefaultData = new() { ["label"] = "" }
                },
                new()
                {
                    Id = "bpmn.end-event",
                    Name = "End Event",
                    Category = "BPMN",
                    IconSvg = "<circle cx='16' cy='16' r='10' fill='none' stroke='currentColor' stroke-width='2'/><circle cx='16' cy='16' r='7' fill='none' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 60,
                    DefaultHeight = 60,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "double-ellipse",
                        ShapeSvg = "<ellipse cx='50' cy='50' rx='48' ry='48' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><ellipse cx='50' cy='50' rx='38' ry='38' fill='none' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 9 }
                            }
                        ]
                    },
                    DefaultData = new() { ["label"] = "" }
                },
                new()
                {
                    Id = "bpmn.intermediate-event",
                    Name = "Intermediate Event",
                    Category = "BPMN",
                    IconSvg = "<circle cx='16' cy='16' r='10' fill='none' stroke='currentColor' stroke-width='2'/><circle cx='16' cy='16' r='7' fill='none' stroke='currentColor' stroke-width='1.5'/>",
                    DefaultWidth = 60,
                    DefaultHeight = 60,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "double-ellipse",
                        ShapeSvg = "<ellipse cx='50' cy='50' rx='48' ry='48' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><ellipse cx='50' cy='50' rx='38' ry='38' fill='none' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 9 }
                            }
                        ]
                    },
                    DefaultData = new() { ["label"] = "" }
                },
                new()
                {
                    Id = "bpmn.data-object",
                    Name = "Data Object",
                    Category = "BPMN",
                    IconSvg = "<rect x='4' y='4' width='20' height='20' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><path d='M24 4 L24 16 L16 24' fill='none' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 80,
                    DefaultHeight = 100,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "document",
                        ShapeSvg = "<path d='M0,0 L100,0 L100,80 L80,100 L0,100 Z' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><path d='M100,80 L100,100 L80,100' fill='none' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "Data",
                                Padding = 8,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 11 }
                            }
                        ]
                    },
                    DefaultData = new() { ["label"] = "Data" }
                },
                new()
                {
                    Id = "bpmn.data-store",
                    Name = "Data Store",
                    Category = "BPMN",
                    IconSvg = "<ellipse cx='16' cy='8' rx='10' ry='4' fill='none' stroke='currentColor' stroke-width='2'/><rect x='6' y='8' width='20' height='16' fill='none' stroke='currentColor' stroke-width='2'/><ellipse cx='16' cy='24' rx='10' ry='4' fill='none' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 100,
                    DefaultHeight = 120,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "cylinder",
                        ShapeSvg = "<ellipse cx='50' cy='15' rx='50' ry='15' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><rect x='0' y='15' width='100' height='70' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><ellipse cx='50' cy='85' rx='50' ry='15' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "Data Store",
                                Padding = 8,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["label"] = "Data Store" }
                }
            ]
        };
    }

    private static DiagramStencilSet SwimlaneSet()
    {
        return new DiagramStencilSet
        {
            Id = "swimlane",
            Name = "Swimlanes",
            Stencils =
            [
                new()
                {
                    Id = "swimlane.horizontal",
                    Name = "Horizontal Swimlane",
                    Category = "Swimlanes",
                    IconSvg = "<rect x='4' y='4' width='24' height='20' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><line x1='4' y1='12' x2='28' y2='12' stroke='currentColor' stroke-width='2'/><line x1='4' y1='20' x2='28' y2='20' stroke='currentColor' stroke-width='2'/><text x='7' y='10' font-size='4' fill='currentColor'>A</text><text x='7' y='18' font-size='4' fill='currentColor'>B</text>",
                    DefaultWidth = 400,
                    DefaultHeight = 200,
                    IsSwimlane = true,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "swimlane-horizontal",
                        Sections =
                        [
                            new()
                            {
                                Type = "swimlane",
                                DataKey = "swimlane",
                                DefaultText = "Swimlane",
                                Padding = 0
                            }
                        ]
                    },
                    DefaultData = new()
                    {
                        ["swimlane"] = "Swimlane"
                    }
                },
                new()
                {
                    Id = "swimlane.vertical",
                    Name = "Vertical Swimlane",
                    Category = "Swimlanes",
                    IconSvg = "<rect x='4' y='4' width='20' height='24' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><line x1='12' y1='4' x2='12' y2='28' stroke='currentColor' stroke-width='2'/><line x1='20' y1='4' x2='20' y2='28' stroke='currentColor' stroke-width='2'/><text x='10' y='10' font-size='4' fill='currentColor' transform='rotate(-90 10 10)'>A</text><text x='18' y='10' font-size='4' fill='currentColor' transform='rotate(-90 18 10)'>B</text>",
                    DefaultWidth = 300,
                    DefaultHeight = 300,
                    IsSwimlane = true,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "swimlane-vertical",
                        Sections =
                        [
                            new()
                            {
                                Type = "swimlane",
                                DataKey = "swimlane",
                                DefaultText = "Swimlane",
                                Padding = 0
                            }
                        ]
                    },
                    DefaultData = new()
                    {
                        ["swimlane"] = "Swimlane"
                    }
                }
            ]
        };
    }

    private static DiagramStencilSet TableSet()
    {
        return new DiagramStencilSet
        {
            Id = "table",
            Name = "Tables",
            Stencils =
            [
                new()
                {
                    Id = "table.basic",
                    Name = "Basic Table",
                    Category = "Tables",
                    IconSvg = "<rect x='4' y='6' width='24' height='20' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><line x1='4' y1='14' x2='28' y2='14' stroke='currentColor' stroke-width='2'/><line x1='12' y1='14' x2='12' y2='26' stroke='currentColor' stroke-width='2'/><line x1='20' y1='14' x2='20' y2='26' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 240,
                    DefaultHeight = 160,
                    IsTable = true,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "table",
                        Sections =
                        [
                            new()
                            {
                                Type = "table",
                                DataKey = "table",
                                DefaultText = "Table",
                                Padding = 0
                            }
                        ]
                    },
                    DefaultData = new()
                    {
                        ["rowCount"] = 3,
                        ["columnCount"] = 2,
                        ["cells"] = new List<DiagramTableCellData>
                        {
                            new() { Row = 0, Column = 0, Text = "Header 1", Style = new() { FontWeight = "bold", BackgroundColor = "#f3f4f6" } },
                            new() { Row = 0, Column = 1, Text = "Header 2", Style = new() { FontWeight = "bold", BackgroundColor = "#f3f4f6" } },
                            new() { Row = 1, Column = 0, Text = "Row 1 Col 1" },
                            new() { Row = 1, Column = 1, Text = "Row 1 Col 2" },
                            new() { Row = 2, Column = 0, Text = "Row 2 Col 1" },
                            new() { Row = 2, Column = 1, Text = "Row 2 Col 2" }
                        }
                    }
                }
            ]
        };
    }

    private static DiagramStencilSet FlowchartSet()
    {
        return new DiagramStencilSet
        {
            Id = "flowchart",
            Name = "Flowchart",
            Stencils =
            [
                new()
                {
                    Id = "flowchart.terminator",
                    Name = "Start / End",
                    Category = "Flowchart",
                    IconSvg = "<rect x='4' y='8' width='24' height='16' rx='8' fill='none' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 120,
                    DefaultHeight = 50,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "ellipse",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='25' ry='25' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "Start",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new()
                    {
                        ["label"] = "Start"
                    }
                },
                new()
                {
                    Id = "flowchart.decision",
                    Name = "Decision",
                    Category = "Flowchart",
                    IconSvg = "<polygon points='16,4 28,16 16,28 4,16' fill='none' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 120,
                    DefaultHeight = 80,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "diamond",
                        ShapeSvg = "<polygon points='50,2 98,50 50,98 2,50' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "Decision?",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new()
                    {
                        ["label"] = "Decision?"
                    }
                },
                new()
                {
                    Id = "flowchart.process",
                    Name = "Process",
                    Category = "Flowchart",
                    IconSvg = "<rect x='4' y='8' width='24' height='16' rx='1' fill='none' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 120,
                    DefaultHeight = 60,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "Process",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new()
                    {
                        ["label"] = "Process"
                    }
                },
                new()
                {
                    Id = "flowchart.document",
                    Name = "Document",
                    Category = "Flowchart",
                    IconSvg = "<rect x='4' y='4' width='24' height='20' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><path d='M4 20 Q8 24 12 20 Q16 16 20 20 Q24 24 28 20' fill='none' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 120,
                    DefaultHeight = 70,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "document",
                        ShapeSvg = "<path d='M0,0 L100,0 L100,80 L85,100 L70,80 L55,100 L40,80 L25,100 L10,80 L0,100 Z' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "Document",
                                Padding = 4,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new()
                    {
                        ["label"] = "Document"
                    }
                },
                new()
                {
                    Id = "flowchart.preparation",
                    Name = "Preparation",
                    Category = "Flowchart",
                    IconSvg = "<polygon points='16,4 26,10 26,22 16,28 6,22 6,10' fill='none' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 110,
                    DefaultHeight = 100,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "hexagon",
                        ShapeSvg = "<polygon points='25,0 75,0 100,50 75,100 25,100 0,50' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "Prep",
                                Padding = 8,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["label"] = "Prep" }
                },
                new()
                {
                    Id = "flowchart.input-output",
                    Name = "Input / Output",
                    Category = "Flowchart",
                    IconSvg = "<polygon points='8,4 28,4 24,28 4,28' fill='none' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 120,
                    DefaultHeight = 60,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "parallelogram",
                        ShapeSvg = "<polygon points='15,0 100,0 85,100 0,100' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "Input",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["label"] = "Input" }
                },
                new()
                {
                    Id = "flowchart.off-page-connector",
                    Name = "Off-Page Connector",
                    Category = "Flowchart",
                    IconSvg = "<polygon points='16,4 28,16 16,28 4,28 4,4' fill='none' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 100,
                    DefaultHeight = 80,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "pentagon",
                        ShapeSvg = "<polygon points='50,0 100,40 80,100 20,100 0,40' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "A",
                                Padding = 8,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 14, IsBold = true }
                            }
                        ]
                    },
                    DefaultData = new() { ["label"] = "A" }
                },
                new()
                {
                    Id = "flowchart.delay",
                    Name = "Delay",
                    Category = "Flowchart",
                    IconSvg = "<path d='M4,4 L20,4 A12,12 0 0,1 20,28 L4,28 Z' fill='none' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 120,
                    DefaultHeight = 60,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "half-ellipse",
                        ShapeSvg = "<path d='M0,100 L0,50 A50,50 0 0,1 100,50 L100,100 Z' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "Delay",
                                Padding = 8,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["label"] = "Delay" }
                }
            ]
        };
    }

    private static DiagramStencilSet ErdSet()
    {
        return new DiagramStencilSet
        {
            Id = "erd",
            Name = "ERD",
            Stencils =
            [
                new()
                {
                    Id = "erd.entity",
                    Name = "Entity",
                    Category = "ERD",
                    IconSvg = "<rect x='4' y='4' width='24' height='10' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><rect x='4' y='14' width='24' height='14' rx='1' fill='none' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 140,
                    DefaultHeight = 100,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><text x='50' y='55' text-anchor='middle' font-size='14' fill='var(--stencil-stroke)'>ERD</text>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Entity",
                                Padding = 6,
                                TextStyle = new()
                                {
                                    IsBold = true,
                                    TextAlign = StencilTextAlign.Center,
                                    FontSize = 12
                                }
                            },
                            new()
                            {
                                Type = "divider",
                                Padding = 0
                            },
                            new()
                            {
                                Type = "list",
                                DataKey = "attributes",
                                DefaultText = "id: PK",
                                Padding = 6,
                                TextStyle = new()
                                {
                                    TextAlign = StencilTextAlign.Left,
                                    FontSize = 11,
                                    FontFamily = "ui-monospace, SFMono-Regular, Menlo, Consolas, monospace"
                                }
                            }
                        ]
                    },
                    DefaultData = new()
                    {
                        ["name"] = "Entity",
                        ["attributes"] = new[] { "id: PK", "name: string" }
                    }
                },
                new()
                {
                    Id = "erd.relationship",
                    Name = "Relationship",
                    Category = "ERD",
                    IconSvg = "<polygon points='16,4 28,16 16,28 4,16' fill='none' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 100,
                    DefaultHeight = 80,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "diamond",
                        ShapeSvg = "<polygon points='50,2 98,50 50,98 2,50' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "Has",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 11 }
                            }
                        ]
                    },
                    DefaultData = new()
                    {
                        ["label"] = "Has"
                    }
                },
                new()
                {
                    Id = "erd.weak-entity",
                    Name = "Weak Entity",
                    Category = "ERD",
                    IconSvg = "<rect x='4' y='4' width='24' height='10' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><rect x='4' y='14' width='24' height='14' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><rect x='5' y='5' width='22' height='8' rx='1' fill='none' stroke='currentColor' stroke-width='1.5'/><rect x='5' y='15' width='22' height='12' rx='1' fill='none' stroke='currentColor' stroke-width='1.5'/>",
                    DefaultWidth = 140,
                    DefaultHeight = 100,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "weak-entity",
                        ShapeSvg = "<rect x='4' y='4' width='92' height='92' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><rect x='0' y='0' width='100' height='100' rx='2' fill='none' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        StrokeWidth = 3,
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Weak Entity",
                                Padding = 6,
                                TextStyle = new()
                                {
                                    IsBold = true,
                                    TextAlign = StencilTextAlign.Center,
                                    FontSize = 12
                                }
                            },
                            new()
                            {
                                Type = "divider",
                                Padding = 0
                            },
                            new()
                            {
                                Type = "list",
                                DataKey = "attributes",
                                DefaultText = "id: PK",
                                Padding = 6,
                                TextStyle = new()
                                {
                                    TextAlign = StencilTextAlign.Left,
                                    FontSize = 11,
                                    FontFamily = "ui-monospace, SFMono-Regular, Menlo, Consolas, monospace"
                                }
                            }
                        ]
                    },
                    DefaultData = new()
                    {
                        ["name"] = "Weak Entity",
                        ["attributes"] = new[] { "id: PK", "owner_id: FK" }
                    }
                },
                new()
                {
                    Id = "erd.attribute",
                    Name = "Attribute",
                    Category = "ERD",
                    IconSvg = "<ellipse cx='16' cy='16' rx='12' ry='8' fill='none' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 120,
                    DefaultHeight = 60,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "ellipse",
                        ShapeSvg = "<ellipse cx='50' cy='50' rx='48' ry='46' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "attr",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 11 }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "attr" }
                },
                new()
                {
                    Id = "erd.key-attribute",
                    Name = "Key Attribute",
                    Category = "ERD",
                    IconSvg = "<ellipse cx='16' cy='16' rx='12' ry='8' fill='none' stroke='currentColor' stroke-width='2'/><line x1='8' y1='18' x2='24' y2='18' stroke='currentColor' stroke-width='1.5'/>",
                    DefaultWidth = 120,
                    DefaultHeight = 60,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "ellipse",
                        ShapeSvg = "<ellipse cx='50' cy='50' rx='48' ry='46' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='25' y1='50' x2='75' y2='50' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "id",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 11, IsBold = true }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "id" }
                }
            ]
        };
    }

    private static DiagramStencilSet NetworkSet()
    {
        return new DiagramStencilSet
        {
            Id = "network",
            Name = "Network",
            Stencils =
            [
                new()
                {
                    Id = "network.cloud",
                    Name = "Cloud",
                    Category = "Network",
                    IconSvg = "<path d='M8 20 A6 6 0 0 1 8 8 A6 6 0 0 1 20 6 A6 6 0 0 1 28 14 A6 6 0 0 1 24 24 H10' fill='none' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 140,
                    DefaultHeight = 90,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "cloud",
                        ShapeSvg = "<path d='M15 70 A18 18 0 0 1 18 37 A20 20 0 0 1 55 30 A20 20 0 0 1 88 40 A18 18 0 0 1 82 70 Z' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "Cloud",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["label"] = "Cloud" }
                },
                new()
                {
                    Id = "network.server",
                    Name = "Server",
                    Category = "Network",
                    IconSvg = "<rect x='6' y='6' width='20' height='20' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><circle cx='10' cy='12' r='1.5' fill='currentColor'/><line x1='14' y1='12' x2='22' y2='12' stroke='currentColor' stroke-width='1.5'/><circle cx='10' cy='20' r='1.5' fill='currentColor'/><line x1='14' y1='20' x2='22' y2='20' stroke='currentColor' stroke-width='1.5'/>",
                    DefaultWidth = 100,
                    DefaultHeight = 100,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='20' y='15' width='60' height='70' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><circle cx='32' cy='30' r='3' fill='var(--stencil-stroke)'/><line x1='42' y1='30' x2='74' y2='30' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><circle cx='32' cy='50' r='3' fill='var(--stencil-stroke)'/><line x1='42' y1='50' x2='74' y2='50' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><circle cx='32' cy='70' r='3' fill='var(--stencil-stroke)'/><line x1='42' y1='70' x2='74' y2='70' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "icon",
                                DataKey = "icon",
                                DefaultText = "🖥",
                                Padding = 8,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 20 }
                            },
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "Server",
                                Padding = 4,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 11 }
                            }
                        ]
                    },
                    DefaultData = new() { ["icon"] = "🖥", ["label"] = "Server" }
                },
                new()
                {
                    Id = "network.router",
                    Name = "Router",
                    Category = "Network",
                    IconSvg = "<circle cx='16' cy='16' r='10' fill='none' stroke='currentColor' stroke-width='2'/><circle cx='16' cy='16' r='3' fill='none' stroke='currentColor' stroke-width='2'/><line x1='16' y1='6' x2='16' y2='10' stroke='currentColor' stroke-width='2'/><line x1='16' y1='22' x2='16' y2='26' stroke='currentColor' stroke-width='2'/><line x1='6' y1='16' x2='10' y2='16' stroke='currentColor' stroke-width='2'/><line x1='22' y1='16' x2='26' y2='16' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 100,
                    DefaultHeight = 100,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "ellipse",
                        ShapeSvg = "<ellipse cx='50' cy='50' rx='40' ry='38' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><circle cx='50' cy='50' r='10' fill='none' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='50' y1='22' x2='50' y2='38' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='50' y1='62' x2='50' y2='78' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='22' y1='50' x2='38' y2='50' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='62' y1='50' x2='78' y2='50' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "Router",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["label"] = "Router" }
                },
                new()
                {
                    Id = "network.database",
                    Name = "Database",
                    Category = "Network",
                    IconSvg = "<ellipse cx='16' cy='8' rx='10' ry='4' fill='none' stroke='currentColor' stroke-width='2'/><rect x='6' y='8' width='20' height='16' fill='none' stroke='currentColor' stroke-width='2'/><ellipse cx='16' cy='24' rx='10' ry='4' fill='none' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 100,
                    DefaultHeight = 120,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "cylinder",
                        ShapeSvg = "<ellipse cx='50' cy='15' rx='50' ry='15' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><rect x='0' y='15' width='100' height='70' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><ellipse cx='50' cy='85' rx='50' ry='15' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "DB",
                                Padding = 8,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["label"] = "DB" }
                },
                new()
                {
                    Id = "network.firewall",
                    Name = "Firewall",
                    Category = "Network",
                    IconSvg = "<rect x='4' y='4' width='24' height='24' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><line x1='4' y1='8' x2='28' y2='8' stroke='currentColor' stroke-width='1.5'/><line x1='4' y1='12' x2='28' y2='12' stroke='currentColor' stroke-width='1.5'/><line x1='4' y1='16' x2='28' y2='16' stroke='currentColor' stroke-width='1.5'/><line x1='4' y1='20' x2='28' y2='20' stroke='currentColor' stroke-width='1.5'/><line x1='4' y1='24' x2='28' y2='24' stroke='currentColor' stroke-width='1.5'/>",
                    DefaultWidth = 100,
                    DefaultHeight = 100,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='0' y1='20' x2='100' y2='20' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='0' y1='40' x2='100' y2='40' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='0' y1='60' x2='100' y2='60' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='0' y1='80' x2='100' y2='80' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "Firewall",
                                Padding = 4,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 11 }
                            }
                        ]
                    },
                    DefaultData = new() { ["label"] = "Firewall" }
                },
                new()
                {
                    Id = "network.switch",
                    Name = "Switch",
                    Category = "Network",
                    IconSvg = "<rect x='4' y='8' width='24' height='16' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><line x1='10' y1='14' x2='14' y2='14' stroke='currentColor' stroke-width='2'/><line x1='18' y1='14' x2='22' y2='14' stroke='currentColor' stroke-width='2'/><line x1='10' y1='20' x2='14' y2='20' stroke='currentColor' stroke-width='2'/><line x1='18' y1='20' x2='22' y2='20' stroke='currentColor' stroke-width='2'/><line x1='12' y1='14' x2='12' y2='20' stroke='currentColor' stroke-width='2'/><line x1='20' y1='14' x2='20' y2='20' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 100,
                    DefaultHeight = 80,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='15' y='35' width='70' height='30' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='25' y1='45' x2='35' y2='45' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='45' y1='45' x2='55' y2='45' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='65' y1='45' x2='75' y2='45' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='25' y1='55' x2='35' y2='55' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='45' y1='55' x2='55' y2='55' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='65' y1='55' x2='75' y2='55' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "Switch",
                                Padding = 4,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 11 }
                            }
                        ]
                    },
                    DefaultData = new() { ["label"] = "Switch" }
                },
                new()
                {
                    Id = "network.workstation",
                    Name = "Workstation",
                    Category = "Network",
                    IconSvg = "<rect x='6' y='6' width='14' height='10' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><rect x='10' y='16' width='6' height='2' fill='none' stroke='currentColor' stroke-width='2'/><rect x='20' y='8' width='6' height='8' rx='1' fill='none' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 100,
                    DefaultHeight = 80,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='20' y='20' width='60' height='45' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><rect x='25' y='25' width='50' height='35' rx='1' fill='none' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><rect x='40' y='65' width='20' height='8' rx='1' fill='none' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "Workstation",
                                Padding = 4,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 11 }
                            }
                        ]
                    },
                    DefaultData = new() { ["label"] = "Workstation" }
                },
                new()
                {
                    Id = "network.laptop",
                    Name = "Laptop",
                    Category = "Network",
                    IconSvg = "<rect x='6' y='8' width='20' height='12' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><path d='M4 22 L28 22' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 100,
                    DefaultHeight = 70,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='18' y='20' width='64' height='42' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><path d='M12 68 L88 68' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><rect x='28' y='28' width='44' height='26' rx='1' fill='none' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "Laptop",
                                Padding = 4,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 11 }
                            }
                        ]
                    },
                    DefaultData = new() { ["label"] = "Laptop" }
                },
                new()
                {
                    Id = "network.mobile",
                    Name = "Mobile",
                    Category = "Network",
                    IconSvg = "<rect x='10' y='4' width='12' height='24' rx='2' fill='none' stroke='currentColor' stroke-width='2'/><line x1='13' y1='8' x2='19' y2='8' stroke='currentColor' stroke-width='1.5'/><circle cx='16' cy='22' r='1.5' fill='currentColor'/>",
                    DefaultWidth = 60,
                    DefaultHeight = 100,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='35' y='15' width='30' height='55' rx='3' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><rect x='40' y='22' width='20' height='35' rx='1' fill='none' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "Mobile",
                                Padding = 4,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 10 }
                            }
                        ]
                    },
                    DefaultData = new() { ["label"] = "Mobile" }
                },
                new()
                {
                    Id = "network.internet",
                    Name = "Internet",
                    Category = "Network",
                    IconSvg = "<circle cx='16' cy='16' r='10' fill='none' stroke='currentColor' stroke-width='2'/><ellipse cx='16' cy='16' rx='10' ry='4' fill='none' stroke='currentColor' stroke-width='1.5'/><line x1='16' y1='6' x2='16' y2='26' stroke='currentColor' stroke-width='1.5'/><line x1='6' y1='16' x2='26' y2='16' stroke='currentColor' stroke-width='1.5'/>",
                    DefaultWidth = 100,
                    DefaultHeight = 100,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "ellipse",
                        ShapeSvg = "<ellipse cx='50' cy='50' rx='48' ry='46' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><ellipse cx='50' cy='50' rx='48' ry='19' fill='none' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='50' y1='2' x2='50' y2='98' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='2' y1='50' x2='98' y2='50' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "Internet",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 11 }
                            }
                        ]
                    },
                    DefaultData = new() { ["label"] = "Internet" }
                },
                new()
                {
                    Id = "network.storage",
                    Name = "Storage",
                    Category = "Network",
                    IconSvg = "<rect x='6' y='6' width='20' height='20' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><circle cx='11' cy='12' r='1.5' fill='currentColor'/><line x1='15' y1='12' x2='22' y2='12' stroke='currentColor' stroke-width='1.5'/><circle cx='11' cy='20' r='1.5' fill='currentColor'/><line x1='15' y1='20' x2='22' y2='20' stroke='currentColor' stroke-width='1.5'/>",
                    DefaultWidth = 100,
                    DefaultHeight = 100,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='20' y='20' width='60' height='60' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><circle cx='30' cy='35' r='3' fill='var(--stencil-stroke)'/><line x1='38' y1='35' x2='70' y2='35' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><circle cx='30' cy='55' r='3' fill='var(--stencil-stroke)'/><line x1='38' y1='55' x2='70' y2='55' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "Storage",
                                Padding = 4,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 11 }
                            }
                        ]
                    },
                    DefaultData = new() { ["label"] = "Storage" }
                },
                new()
                {
                    Id = "network.printer",
                    Name = "Printer",
                    Category = "Network",
                    IconSvg = "<rect x='6' y='10' width='20' height='12' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><rect x='9' y='6' width='14' height='6' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><circle cx='11' cy='16' r='1' fill='currentColor'/><line x1='14' y1='16' x2='22' y2='16' stroke='currentColor' stroke-width='1.5'/>",
                    DefaultWidth = 100,
                    DefaultHeight = 80,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='20' y='30' width='60' height='40' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><rect x='25' y='20' width='50' height='15' rx='1' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><rect x='30' y='40' width='40' height='20' rx='1' fill='none' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "label",
                                DefaultText = "Printer",
                                Padding = 4,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 11 }
                            }
                        ]
                    },
                    DefaultData = new() { ["label"] = "Printer" }
                },
                new()
                {
                    Id = "archimate.node",
                    Name = "Node",
                    Category = "Network",
                    IconSvg = "<polygon points='8,10 24,4 28,12 12,18' fill='none' stroke='currentColor' stroke-width='2'/><line x1='8' y1='10' x2='8' y2='24' stroke='currentColor' stroke-width='2'/><line x1='12' y1='18' x2='12' y2='28' stroke='currentColor' stroke-width='2'/><line x1='28' y1='12' x2='28' y2='22' stroke='currentColor' stroke-width='2'/><line x1='8' y1='24' x2='24' y2='18' stroke='currentColor' stroke-width='2'/><line x1='24' y1='18' x2='28' y2='22' stroke='currentColor' stroke-width='2'/><line x1='8' y1='24' x2='12' y2='28' stroke='currentColor' stroke-width='2'/><text x='18' y='22' text-anchor='middle' font-size='4' fill='currentColor'>&lt;&lt;node&gt;&gt;</text>",
                    DefaultWidth = 120,
                    DefaultHeight = 100,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "cube",
                        ShapeSvg = "<path d='M20,35 L50,18 L80,35 L80,65 L50,82 L20,65 Z' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><path d='M20,35 L50,50 L80,35' fill='none' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><path d='M50,50 L50,82' fill='none' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Node",
                                Padding = 8,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12, IsBold = true }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Node" }
                },
                new()
                {
                    Id = "archimate.device",
                    Name = "Device",
                    Category = "Network",
                    IconSvg = "<rect x='6' y='6' width='20' height='20' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><rect x='10' y='10' width='12' height='8' rx='1' fill='none' stroke='currentColor' stroke-width='1.5'/><text x='16' y='24' text-anchor='middle' font-size='4' fill='currentColor'>&lt;&lt;device&gt;&gt;</text>",
                    DefaultWidth = 100,
                    DefaultHeight = 100,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='20' y='20' width='60' height='60' rx='3' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><rect x='30' y='30' width='40' height='28' rx='2' fill='none' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Device",
                                Padding = 8,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Device" }
                },
                new()
                {
                    Id = "archimate.system-software",
                    Name = "System Software",
                    Category = "Network",
                    IconSvg = "<rect x='4' y='4' width='24' height='24' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='16' text-anchor='middle' font-size='4' fill='currentColor'>&lt;&lt;software&gt;&gt;</text>",
                    DefaultWidth = 120,
                    DefaultHeight = 80,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><text x='50' y='18' text-anchor='middle' font-size='8' fill='var(--stencil-stroke)'>&lt;&lt;software&gt;&gt;</text>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "System Software",
                                Padding = 8,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "System Software" }
                },
                new()
                {
                    Id = "archimate.technology-service",
                    Name = "Technology Service",
                    Category = "Network",
                    IconSvg = "<rect x='4' y='8' width='24' height='16' rx='8' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='20' text-anchor='middle' font-size='5' fill='currentColor'>&lt;&lt;service&gt;&gt;</text>",
                    DefaultWidth = 160,
                    DefaultHeight = 80,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rounded",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><text x='50' y='18' text-anchor='middle' font-size='8' fill='var(--stencil-stroke)'>&lt;&lt;technology service&gt;&gt;</text>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Tech Service",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Tech Service" }
                },
                new()
                {
                    Id = "archimate.technology-interface",
                    Name = "Technology Interface",
                    Category = "Network",
                    IconSvg = "<circle cx='22' cy='16' r='5' fill='none' stroke='currentColor' stroke-width='2'/><line x1='4' y1='16' x2='17' y2='16' stroke='currentColor' stroke-width='2'/><text x='14' y='12' text-anchor='middle' font-size='4' fill='currentColor'>&lt;&lt;interface&gt;&gt;</text>",
                    DefaultWidth = 120,
                    DefaultHeight = 50,
                    Ports =
                    [
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "lollipop",
                        ShapeSvg = "<circle cx='80' cy='20' r='16' fill='none' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='0' y1='20' x2='64' y2='20' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Tech Interface",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Left, FontSize = 11 }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Tech Interface" }
                },
                new()
                {
                    Id = "archimate.artifact",
                    Name = "Artifact",
                    Category = "Network",
                    IconSvg = "<rect x='4' y='4' width='20' height='20' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><path d='M24 4 L24 16 L16 24' fill='none' stroke='currentColor' stroke-width='2'/><text x='14' y='16' text-anchor='middle' font-size='4' fill='currentColor'>&lt;&lt;artifact&gt;&gt;</text>",
                    DefaultWidth = 120,
                    DefaultHeight = 80,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "document",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='80' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><path d='M100 0 L100 48 L60 80' fill='none' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Artifact",
                                Padding = 8,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Artifact" }
                },
                new()
                {
                    Id = "archimate.network",
                    Name = "Network",
                    Category = "Network",
                    IconSvg = "<path d='M8 20 A6 6 0 0 1 8 8 A6 6 0 0 1 20 6 A6 6 0 0 1 28 14 A6 6 0 0 1 24 24 H10' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='30' text-anchor='middle' font-size='4' fill='currentColor'>&lt;&lt;network&gt;&gt;</text>",
                    DefaultWidth = 140,
                    DefaultHeight = 90,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "cloud",
                        ShapeSvg = "<path d='M20 60 A15 15 0 0 1 20 30 A15 15 0 0 1 50 25 A15 15 0 0 1 85 35 A15 15 0 0 1 75 65 H25' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Network",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 11 }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Network" }
                }
            ]
        };
    }

    private static DiagramStencilSet C4Set()
    {
        return new DiagramStencilSet
        {
            Id = "c4",
            Name = "C4 Model",
            Stencils =
            [
                new()
                {
                    Id = "c4.person",
                    Name = "Person",
                    Category = "C4",
                    IconSvg = "<circle cx='16' cy='10' r='6' fill='none' stroke='currentColor' stroke-width='2'/><ellipse cx='16' cy='28' rx='10' ry='4' fill='none' stroke='currentColor' stroke-width='2'/><line x1='16' y1='16' x2='16' y2='24' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 100,
                    DefaultHeight = 120,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "actor",
                        ShapeSvg = "<circle cx='50' cy='22' r='14' fill='none' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='50' y1='36' x2='50' y2='62' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='28' y1='48' x2='72' y2='48' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='32' y1='88' x2='50' y2='62' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='68' y1='88' x2='50' y2='62' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        PreserveAspectRatio = true,
                        ContentPosition = "below",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Person",
                                Padding = 4,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Person" }
                },
                new()
                {
                    Id = "c4.software-system",
                    Name = "Software System",
                    Category = "C4",
                    IconSvg = "<rect x='4' y='4' width='24' height='24' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='16' text-anchor='middle' font-size='5' fill='currentColor'>&lt;&lt;System&gt;&gt;</text>",
                    DefaultWidth = 200,
                    DefaultHeight = 120,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><text x='50' y='22' text-anchor='middle' font-size='9' fill='var(--stencil-stroke)'>&lt;&lt;System&gt;&gt;</text>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Software System",
                                Padding = 8,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 14, IsBold = true }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Software System" }
                },
                new()
                {
                    Id = "c4.container",
                    Name = "Container",
                    Category = "C4",
                    IconSvg = "<rect x='4' y='4' width='24' height='24' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='14' text-anchor='middle' font-size='5' fill='currentColor'>&lt;&lt;Container&gt;&gt;</text><text x='16' y='24' text-anchor='middle' font-size='4' fill='currentColor'>[Technology]</text>",
                    DefaultWidth = 200,
                    DefaultHeight = 120,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><text x='50' y='22' text-anchor='middle' font-size='9' fill='var(--stencil-stroke)'>&lt;&lt;Container&gt;&gt;</text><text x='50' y='34' text-anchor='middle' font-size='7' fill='var(--stencil-stroke)'>[Technology]</text>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Web Application",
                                Padding = 4,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 13, IsBold = true }
                            },
                            new()
                            {
                                Type = "text",
                                DataKey = "technology",
                                DefaultText = "[ASP.NET Core + Blazor]",
                                Padding = 4,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 10, FontFamily = "ui-monospace, SFMono-Regular, Menlo, Consolas, monospace" }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Web Application", ["technology"] = "[ASP.NET Core + Blazor]" }
                },
                new()
                {
                    Id = "c4.external-system",
                    Name = "External System",
                    Category = "C4",
                    IconSvg = "<polygon points='16,4 28,10 28,22 16,28 4,22 4,10' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='18' text-anchor='middle' font-size='5' fill='currentColor'>&lt;&lt;External&gt;&gt;</text>",
                    DefaultWidth = 180,
                    DefaultHeight = 100,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "hexagon",
                        ShapeSvg = "<polygon points='50,2 96,25 96,75 50,98 4,75 4,25' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "External System",
                                Padding = 8,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12, IsBold = true }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "External System" }
                },
                new()
                {
                    Id = "c4.database",
                    Name = "Database",
                    Category = "C4",
                    IconSvg = "<ellipse cx='16' cy='8' rx='10' ry='4' fill='none' stroke='currentColor' stroke-width='2'/><rect x='6' y='8' width='20' height='16' fill='none' stroke='currentColor' stroke-width='2'/><ellipse cx='16' cy='24' rx='10' ry='4' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='18' text-anchor='middle' font-size='4' fill='currentColor'>&lt;&lt;Database&gt;&gt;</text>",
                    DefaultWidth = 120,
                    DefaultHeight = 140,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "cylinder",
                        ShapeSvg = "<ellipse cx='50' cy='15' rx='50' ry='15' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><rect x='0' y='15' width='100' height='70' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><ellipse cx='50' cy='85' rx='50' ry='15' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Database",
                                Padding = 8,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12, IsBold = true }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Database" }
                }
            ]
        };
    }

    private static DiagramStencilSet ProjectSet()
    {
        return new DiagramStencilSet
        {
            Id = "project",
            Name = "Project",
            Stencils =
            [
                new()
                {
                    Id = "project.swimlane",
                    Name = "Swimlane",
                    Category = "Project",
                    IconSvg = "<rect x='4' y='4' width='24' height='20' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><line x1='10' y1='4' x2='10' y2='24' stroke='currentColor' stroke-width='2'/><text x='7' y='16' font-size='4' fill='currentColor'>Role</text>",
                    DefaultWidth = 300,
                    DefaultHeight = 120,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "pool",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='30' y1='0' x2='30' y2='100' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "role",
                                DefaultText = "Analyst",
                                Padding = 4,
                                TextStyle = new() { IsBold = true, TextAlign = StencilTextAlign.Left, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["role"] = "Analyst" }
                },
                new()
                {
                    Id = "project.milestone",
                    Name = "Milestone",
                    Category = "Project",
                    IconSvg = "<polygon points='16,4 28,16 16,28 4,16' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='18' text-anchor='middle' font-size='6' fill='currentColor'>M</text>",
                    DefaultWidth = 100,
                    DefaultHeight = 80,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "diamond",
                        ShapeSvg = "<polygon points='50,2 98,50 50,98 2,50' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Milestone",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 11, IsBold = true }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Milestone" }
                },
                new()
                {
                    Id = "project.phase",
                    Name = "Phase",
                    Category = "Project",
                    IconSvg = "<rect x='4' y='8' width='24' height='16' rx='8' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='20' text-anchor='middle' font-size='6' fill='currentColor'>1</text>",
                    DefaultWidth = 160,
                    DefaultHeight = 60,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rounded",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='25' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Phase 1",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Phase 1" }
                },
                new()
                {
                    Id = "project.task",
                    Name = "Task",
                    Category = "Project",
                    IconSvg = "<rect x='4' y='8' width='24' height='16' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><rect x='7' y='12' width='4' height='4' rx='1' fill='none' stroke='currentColor' stroke-width='1.5'/><line x1='14' y1='14' x2='24' y2='14' stroke='currentColor' stroke-width='1.5'/>",
                    DefaultWidth = 140,
                    DefaultHeight = 60,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><rect x='20' y='20' width='60' height='15' rx='1' fill='none' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><rect x='20' y='40' width='60' height='15' rx='1' fill='none' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Task",
                                Padding = 4,
                                TextStyle = new() { TextAlign = StencilTextAlign.Left, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Task" }
                },
                new()
                {
                    Id = "project.risk",
                    Name = "Risk",
                    Category = "Project",
                    IconSvg = "<polygon points='16,4 28,26 4,26' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='20' text-anchor='middle' font-size='6' fill='currentColor'>!</text>",
                    DefaultWidth = 120,
                    DefaultHeight = 90,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "triangle",
                        ShapeSvg = "<polygon points='50,5 95,90 5,90' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Fill = "#fef2f2",
                        Stroke = "#dc2626",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Risk",
                                Padding = 8,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12, Color = "#991b1b" }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Risk" }
                },
                new()
                {
                    Id = "project.issue",
                    Name = "Issue",
                    Category = "Project",
                    IconSvg = "<rect x='4' y='4' width='24' height='24' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='18' text-anchor='middle' font-size='8' fill='currentColor'>!</text>",
                    DefaultWidth = 100,
                    DefaultHeight = 80,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><text x='50' y='55' text-anchor='middle' font-size='14' fill='var(--stencil-stroke)'>!</text>",
                        Fill = "#fef2f2",
                        Stroke = "#dc2626",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Issue",
                                Padding = 4,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 11, Color = "#991b1b" }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Issue" }
                },
                new()
                {
                    Id = "project.assumption",
                    Name = "Assumption",
                    Category = "Project",
                    IconSvg = "<ellipse cx='16' cy='16' rx='12' ry='8' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='18' text-anchor='middle' font-size='6' fill='currentColor'>?</text>",
                    DefaultWidth = 140,
                    DefaultHeight = 80,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "ellipse",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><text x='50' y='55' text-anchor='middle' font-size='10' fill='var(--stencil-stroke)'>A</text>",
                        Fill = "#eff6ff",
                        Stroke = "#2563eb",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Assumption",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 11, Color = "#1e40af" }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Assumption" }
                },
                new()
                {
                    Id = "project.constraint",
                    Name = "Constraint",
                    Category = "Project",
                    IconSvg = "<rect x='4' y='4' width='24' height='24' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><line x1='8' y1='8' x2='24' y2='24' stroke='currentColor' stroke-width='2'/><line x1='24' y1='8' x2='8' y2='24' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 100,
                    DefaultHeight = 80,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='25' y1='25' x2='75' y2='75' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='75' y1='25' x2='25' y2='75' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Fill = "#fff7ed",
                        Stroke = "#ea580c",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Constraint",
                                Padding = 4,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 11, Color = "#9a3412" }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Constraint" }
                },
                new()
                {
                    Id = "project.deliverable",
                    Name = "Deliverable",
                    Category = "Project",
                    IconSvg = "<rect x='4' y='4' width='20' height='20' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><path d='M24 4 L24 16 L16 24' fill='none' stroke='currentColor' stroke-width='2'/><text x='14' y='16' text-anchor='middle' font-size='5' fill='currentColor'>&#10003;</text>",
                    DefaultWidth = 120,
                    DefaultHeight = 80,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "document",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='80' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><path d='M100 0 L100 48 L60 80' fill='none' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Deliverable",
                                Padding = 8,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Deliverable" }
                },
                new()
                {
                    Id = "project.budget",
                    Name = "Budget",
                    Category = "Project",
                    IconSvg = "<rect x='4' y='4' width='24' height='24' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='18' text-anchor='middle' font-size='8' fill='currentColor'>$</text>",
                    DefaultWidth = 100,
                    DefaultHeight = 80,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><circle cx='50' cy='50' r='25' fill='none' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><text x='50' y='55' text-anchor='middle' font-size='12' fill='var(--stencil-stroke)'>$</text>",
                        Fill = "#f0fdf4",
                        Stroke = "#16a34a",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Budget",
                                Padding = 4,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 11, Color = "#166534" }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Budget" }
                },
                new()
                {
                    Id = "project.resource",
                    Name = "Resource",
                    Category = "Project",
                    IconSvg = "<circle cx='16' cy='12' r='5' fill='none' stroke='currentColor' stroke-width='2'/><ellipse cx='16' cy='26' rx='8' ry='4' fill='none' stroke='currentColor' stroke-width='2'/><line x1='16' y1='17' x2='16' y2='22' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 80,
                    DefaultHeight = 100,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "actor",
                        ShapeSvg = "<circle cx='50' cy='18' r='10' fill='none' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='50' y1='28' x2='50' y2='60' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='20' y1='40' x2='80' y2='40' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='25' y1='90' x2='50' y2='60' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><line x1='75' y1='90' x2='50' y2='60' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Resource",
                                Padding = 4,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 10 }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Resource" }
                },
                new()
                {
                    Id = "project.meeting",
                    Name = "Meeting",
                    Category = "Project",
                    IconSvg = "<rect x='4' y='8' width='24' height='16' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><circle cx='10' cy='16' r='2' fill='currentColor'/><circle cx='16' cy='16' r='2' fill='currentColor'/><circle cx='22' cy='16' r='2' fill='currentColor'/>",
                    DefaultWidth = 140,
                    DefaultHeight = 70,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><ellipse cx='35' cy='38' rx='8' ry='6' fill='none' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><ellipse cx='65' cy='38' rx='8' ry='6' fill='none' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><ellipse cx='50' cy='55' rx='18' ry='10' fill='none' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Meeting",
                                Padding = 4,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Meeting" }
                },
                new()
                {
                    Id = "project.decision",
                    Name = "Decision",
                    Category = "Project",
                    IconSvg = "<polygon points='16,4 28,16 16,28 4,16' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='18' text-anchor='middle' font-size='6' fill='currentColor'>?</text>",
                    DefaultWidth = 100,
                    DefaultHeight = 80,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "diamond",
                        ShapeSvg = "<polygon points='50,2 98,50 50,98 2,50' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Decision",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 11 }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Decision" }
                },
                new()
                {
                    Id = "project.timeline-bar",
                    Name = "Timeline Bar",
                    Category = "Project",
                    IconSvg = "<rect x='4' y='10' width='24' height='12' rx='6' fill='none' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 200,
                    DefaultHeight = 40,
                    Ports =
                    [
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rounded",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='12' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Q1 2026",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 11 }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Q1 2026" }
                },
                new()
                {
                    Id = "project.goal",
                    Name = "Goal",
                    Category = "Project",
                    IconSvg = "<circle cx='16' cy='16' r='10' fill='none' stroke='currentColor' stroke-width='2'/><circle cx='16' cy='16' r='4' fill='currentColor'/><text x='16' y='30' text-anchor='middle' font-size='4' fill='currentColor'>&#127942;</text>",
                    DefaultWidth = 100,
                    DefaultHeight = 100,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "double-ellipse",
                        ShapeSvg = "<ellipse cx='50' cy='50' rx='48' ry='48' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><ellipse cx='50' cy='50' rx='38' ry='38' fill='none' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Goal",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 11 }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Goal" }
                }
            ]
        };
    }

    private static DiagramStencilSet BusinessAnalysisSet()
    {
        return new DiagramStencilSet
        {
            Id = "ba",
            Name = "Business Analysis",
            Stencils =
            [
                new()
                {
                    Id = "ba.user-story",
                    Name = "User Story",
                    Category = "Business Analysis",
                    IconSvg = "<rect x='4' y='8' width='24' height='16' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='18' text-anchor='middle' font-size='5' fill='currentColor'>As a... I want...</text>",
                    DefaultWidth = 200,
                    DefaultHeight = 80,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><text x='50' y='55' text-anchor='middle' font-size='14' fill='var(--stencil-stroke)'>BA</text>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "As a user, I want...",
                                Padding = 8,
                                TextStyle = new() { TextAlign = StencilTextAlign.Left, FontSize = 11 }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "As a user, I want..." }
                },
                new()
                {
                    Id = "ba.epic",
                    Name = "Epic",
                    Category = "Business Analysis",
                    IconSvg = "<rect x='4' y='4' width='24' height='24' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='16' text-anchor='middle' font-size='5' fill='currentColor'>&lt;&lt;Epic&gt;&gt;</text>",
                    DefaultWidth = 180,
                    DefaultHeight = 100,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><text x='50' y='18' text-anchor='middle' font-size='9' fill='var(--stencil-stroke)'>Epic</text>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Epic",
                                Padding = 8,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 13, IsBold = true }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Epic" }
                },
                new()
                {
                    Id = "ba.feature",
                    Name = "Feature",
                    Category = "Business Analysis",
                    IconSvg = "<rect x='4' y='8' width='24' height='16' rx='4' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='20' text-anchor='middle' font-size='5' fill='currentColor'>&lt;&lt;Feature&gt;&gt;</text>",
                    DefaultWidth = 160,
                    DefaultHeight = 70,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rounded",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><text x='50' y='18' text-anchor='middle' font-size='9' fill='var(--stencil-stroke)'>Feature</text>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Feature",
                                Padding = 4,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12, IsBold = true }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Feature" }
                },
                new()
                {
                    Id = "ba.requirement-functional",
                    Name = "Functional Requirement",
                    Category = "Business Analysis",
                    IconSvg = "<rect x='4' y='8' width='24' height='16' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='18' text-anchor='middle' font-size='5' fill='currentColor'>FR</text>",
                    DefaultWidth = 180,
                    DefaultHeight = 70,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><text x='50' y='18' text-anchor='middle' font-size='8' fill='var(--stencil-stroke)'>Functional Req</text>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Functional Req",
                                Padding = 4,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 11 }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Functional Req" }
                },
                new()
                {
                    Id = "ba.requirement-nonfunctional",
                    Name = "Non-Functional Requirement",
                    Category = "Business Analysis",
                    IconSvg = "<rect x='4' y='8' width='24' height='16' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='18' text-anchor='middle' font-size='5' fill='currentColor'>NFR</text>",
                    DefaultWidth = 180,
                    DefaultHeight = 70,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><text x='50' y='18' text-anchor='middle' font-size='7' fill='var(--stencil-stroke)'>Non-Functional Req</text>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Non-Functional Req",
                                Padding = 4,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 11 }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Non-Functional Req" }
                },
                new()
                {
                    Id = "ba.business-rule",
                    Name = "Business Rule",
                    Category = "Business Analysis",
                    IconSvg = "<rect x='4' y='8' width='24' height='16' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='18' text-anchor='middle' font-size='5' fill='currentColor'>&lt;&lt;Rule&gt;&gt;</text>",
                    DefaultWidth = 160,
                    DefaultHeight = 70,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><text x='50' y='18' text-anchor='middle' font-size='8' fill='var(--stencil-stroke)'>Business Rule</text>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Business Rule",
                                Padding = 4,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 11, IsBold = true }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Business Rule" }
                },
                new()
                {
                    Id = "ba.gap",
                    Name = "Gap",
                    Category = "Business Analysis",
                    IconSvg = "<polygon points='16,4 28,26 4,26' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='20' text-anchor='middle' font-size='5' fill='currentColor'>&lt;&lt;Gap&gt;&gt;</text>",
                    DefaultWidth = 120,
                    DefaultHeight = 90,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "triangle",
                        ShapeSvg = "<polygon points='50,2 98,98 2,98' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Fill = "#fefce8",
                        Stroke = "#ca8a04",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Gap",
                                Padding = 8,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12, Color = "#854d0e" }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Gap" }
                },
                new()
                {
                    Id = "ba.fishbone-category",
                    Name = "Fishbone Category",
                    Category = "Business Analysis",
                    IconSvg = "<rect x='4' y='10' width='24' height='12' rx='1' fill='none' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 140,
                    DefaultHeight = 50,
                    Ports =
                    [
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Category",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 11, IsBold = true }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Category" }
                },
                new()
                {
                    Id = "ba.fishbone-cause",
                    Name = "Fishbone Cause",
                    Category = "Business Analysis",
                    IconSvg = "<rect x='4' y='10' width='24' height='12' rx='6' fill='none' stroke='currentColor' stroke-width='2'/>",
                    DefaultWidth = 120,
                    DefaultHeight = 40,
                    Ports =
                    [
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rounded",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='12' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Cause",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 10 }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Cause" }
                },
                new()
                {
                    Id = "ba.stakeholder-group",
                    Name = "Stakeholder Group",
                    Category = "Business Analysis",
                    IconSvg = "<ellipse cx='16' cy='16' rx='12' ry='8' fill='none' stroke='currentColor' stroke-width='2'/><ellipse cx='16' cy='16' rx='9' ry='6' fill='none' stroke='currentColor' stroke-width='1.5'/><ellipse cx='16' cy='16' rx='6' ry='4' fill='none' stroke='currentColor' stroke-width='1'/>",
                    DefaultWidth = 140,
                    DefaultHeight = 80,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "ellipse",
                        ShapeSvg = "<ellipse cx='50' cy='50' rx='38' ry='25' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='2' vector-effect='non-scaling-stroke'/><ellipse cx='50' cy='50' rx='28' ry='19' fill='none' stroke='var(--stencil-stroke)' stroke-width='1.5' vector-effect='non-scaling-stroke'/><ellipse cx='50' cy='50' rx='19' ry='13' fill='none' stroke='var(--stencil-stroke)' stroke-width='1' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Stakeholder Group",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 11 }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Stakeholder Group" }
                },
                new()
                {
                    Id = "ba.process-step",
                    Name = "Process Step",
                    Category = "Business Analysis",
                    IconSvg = "<rect x='4' y='8' width='24' height='16' rx='4' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='20' text-anchor='middle' font-size='5' fill='currentColor'>&lt;&lt;Step&gt;&gt;</text>",
                    DefaultWidth = 140,
                    DefaultHeight = 60,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rounded",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><text x='50' y='18' text-anchor='middle' font-size='8' fill='var(--stencil-stroke)'>Process Step</text>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Process Step",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12 }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Process Step" }
                },
                new()
                {
                    Id = "ba.decision-node",
                    Name = "Decision Node",
                    Category = "Business Analysis",
                    IconSvg = "<polygon points='16,4 28,16 16,28 4,16' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='18' text-anchor='middle' font-size='5' fill='currentColor'>Yes/No</text>",
                    DefaultWidth = 100,
                    DefaultHeight = 80,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "diamond",
                        ShapeSvg = "<polygon points='50,2 98,50 50,98 2,50' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Decision",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 11 }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Decision" }
                },
                new()
                {
                    Id = "ba.assumption",
                    Name = "Assumption",
                    Category = "Business Analysis",
                    IconSvg = "<ellipse cx='16' cy='16' rx='12' ry='8' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='18' text-anchor='middle' font-size='5' fill='currentColor'>A</text>",
                    DefaultWidth = 120,
                    DefaultHeight = 70,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "ellipse",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><text x='50' y='55' text-anchor='middle' font-size='12' fill='var(--stencil-stroke)'>A</text>",
                        Fill = "#eff6ff",
                        Stroke = "#2563eb",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Assumption",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 10, Color = "#1e40af" }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Assumption" }
                },
                new()
                {
                    Id = "ba.system-context",
                    Name = "System Context",
                    Category = "Business Analysis",
                    IconSvg = "<rect x='4' y='4' width='24' height='24' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='16' text-anchor='middle' font-size='5' fill='currentColor'>&lt;&lt;Context&gt;&gt;</text>",
                    DefaultWidth = 180,
                    DefaultHeight = 100,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><text x='50' y='18' text-anchor='middle' font-size='8' fill='var(--stencil-stroke)'>System Context</text>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "System Context",
                                Padding = 8,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12, IsBold = true }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "System Context" }
                },
                new()
                {
                    Id = "ba.data-flow",
                    Name = "Data Flow",
                    Category = "Business Analysis",
                    IconSvg = "<polygon points='8,4 28,4 24,28 4,28' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='18' text-anchor='middle' font-size='5' fill='currentColor'>&lt;&lt;Data&gt;&gt;</text>",
                    DefaultWidth = 140,
                    DefaultHeight = 60,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "parallelogram",
                        ShapeSvg = "<polygon points='15,0 100,0 85,100 0,100' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Data Flow",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 11 }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Data Flow" }
                }
            ]
        };
    }

    private static DiagramStencilSet StrategySet()
    {
        return new DiagramStencilSet
        {
            Id = "strategy",
            Name = "Strategy",
            Stencils =
            [
                new()
                {
                    Id = "strategy.swot-strength",
                    Name = "Strength",
                    Category = "Strategy",
                    IconSvg = "<rect x='4' y='4' width='24' height='24' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='18' text-anchor='middle' font-size='6' fill='currentColor'>S</text>",
                    DefaultWidth = 140,
                    DefaultHeight = 100,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><text x='50' y='55' text-anchor='middle' font-size='12' fill='var(--stencil-stroke)'>Strategy</text>",
                        Fill = "#f0fdf4",
                        Stroke = "#16a34a",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Strength",
                                Padding = 8,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12, Color = "#166534" }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Strength" }
                },
                new()
                {
                    Id = "strategy.swot-weakness",
                    Name = "Weakness",
                    Category = "Strategy",
                    IconSvg = "<rect x='4' y='4' width='24' height='24' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='18' text-anchor='middle' font-size='6' fill='currentColor'>W</text>",
                    DefaultWidth = 140,
                    DefaultHeight = 100,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><text x='50' y='18' text-anchor='middle' font-size='9' fill='var(--stencil-stroke)'>Weakness</text>",
                        Fill = "#fef2f2",
                        Stroke = "#dc2626",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Weakness",
                                Padding = 8,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12, Color = "#991b1b" }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Weakness" }
                },
                new()
                {
                    Id = "strategy.swot-opportunity",
                    Name = "Opportunity",
                    Category = "Strategy",
                    IconSvg = "<rect x='4' y='4' width='24' height='24' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='18' text-anchor='middle' font-size='6' fill='currentColor'>O</text>",
                    DefaultWidth = 140,
                    DefaultHeight = 100,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><text x='50' y='18' text-anchor='middle' font-size='9' fill='var(--stencil-stroke)'>Opportunity</text>",
                        Fill = "#eff6ff",
                        Stroke = "#2563eb",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Opportunity",
                                Padding = 8,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12, Color = "#1e40af" }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Opportunity" }
                },
                new()
                {
                    Id = "strategy.swot-threat",
                    Name = "Threat",
                    Category = "Strategy",
                    IconSvg = "<rect x='4' y='4' width='24' height='24' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='18' text-anchor='middle' font-size='6' fill='currentColor'>T</text>",
                    DefaultWidth = 140,
                    DefaultHeight = 100,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><text x='50' y='18' text-anchor='middle' font-size='9' fill='var(--stencil-stroke)'>Threat</text>",
                        Fill = "#fff7ed",
                        Stroke = "#ea580c",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Threat",
                                Padding = 8,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12, Color = "#9a3412" }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Threat" }
                },
                new()
                {
                    Id = "strategy.bmc-key-partner",
                    Name = "Key Partner",
                    Category = "Strategy",
                    IconSvg = "<rect x='4' y='4' width='24' height='24' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='12' text-anchor='middle' font-size='4' fill='currentColor'>&lt;&lt;Partner&gt;&gt;</text>",
                    DefaultWidth = 140,
                    DefaultHeight = 100,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><text x='50' y='18' text-anchor='middle' font-size='8' fill='var(--stencil-stroke)'>Key Partner</text>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Key Partner",
                                Padding = 4,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 11, IsBold = true }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Key Partner" }
                },
                new()
                {
                    Id = "strategy.bmc-key-activity",
                    Name = "Key Activity",
                    Category = "Strategy",
                    IconSvg = "<rect x='4' y='4' width='24' height='24' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='12' text-anchor='middle' font-size='4' fill='currentColor'>&lt;&lt;Activity&gt;&gt;</text>",
                    DefaultWidth = 140,
                    DefaultHeight = 100,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><text x='50' y='18' text-anchor='middle' font-size='8' fill='var(--stencil-stroke)'>Key Activity</text>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Key Activity",
                                Padding = 4,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 11, IsBold = true }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Key Activity" }
                },
                new()
                {
                    Id = "strategy.bmc-key-resource",
                    Name = "Key Resource",
                    Category = "Strategy",
                    IconSvg = "<rect x='4' y='4' width='24' height='24' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='12' text-anchor='middle' font-size='4' fill='currentColor'>&lt;&lt;Resource&gt;&gt;</text>",
                    DefaultWidth = 140,
                    DefaultHeight = 100,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><text x='50' y='18' text-anchor='middle' font-size='8' fill='var(--stencil-stroke)'>Key Resource</text>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Key Resource",
                                Padding = 4,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 11, IsBold = true }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Key Resource" }
                },
                new()
                {
                    Id = "strategy.bmc-value-proposition",
                    Name = "Value Proposition",
                    Category = "Strategy",
                    IconSvg = "<rect x='4' y='4' width='24' height='24' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='12' text-anchor='middle' font-size='4' fill='currentColor'>&lt;&lt;Value&gt;&gt;</text>",
                    DefaultWidth = 140,
                    DefaultHeight = 100,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><text x='50' y='18' text-anchor='middle' font-size='7' fill='var(--stencil-stroke)'>Value Proposition</text>",
                        Fill = "#fefce8",
                        Stroke = "#ca8a04",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Value Proposition",
                                Padding = 4,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 11, IsBold = true, Color = "#854d0e" }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Value Proposition" }
                },
                new()
                {
                    Id = "strategy.bmc-customer-relationship",
                    Name = "Customer Relationship",
                    Category = "Strategy",
                    IconSvg = "<rect x='4' y='4' width='24' height='24' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='12' text-anchor='middle' font-size='3' fill='currentColor'>&lt;&lt;Relationship&gt;&gt;</text>",
                    DefaultWidth = 140,
                    DefaultHeight = 100,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><text x='50' y='18' text-anchor='middle' font-size='6' fill='var(--stencil-stroke)'>Customer Relationship</text>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Relationship",
                                Padding = 4,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 10, IsBold = true }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Relationship" }
                },
                new()
                {
                    Id = "strategy.bmc-channel",
                    Name = "Channel",
                    Category = "Strategy",
                    IconSvg = "<rect x='4' y='4' width='24' height='24' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='12' text-anchor='middle' font-size='4' fill='currentColor'>&lt;&lt;Channel&gt;&gt;</text>",
                    DefaultWidth = 140,
                    DefaultHeight = 100,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><text x='50' y='18' text-anchor='middle' font-size='9' fill='var(--stencil-stroke)'>Channel</text>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Channel",
                                Padding = 4,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 11, IsBold = true }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Channel" }
                },
                new()
                {
                    Id = "strategy.bmc-customer-segment",
                    Name = "Customer Segment",
                    Category = "Strategy",
                    IconSvg = "<rect x='4' y='4' width='24' height='24' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='12' text-anchor='middle' font-size='3' fill='currentColor'>&lt;&lt;Segment&gt;&gt;</text>",
                    DefaultWidth = 140,
                    DefaultHeight = 100,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><text x='50' y='18' text-anchor='middle' font-size='7' fill='var(--stencil-stroke)'>Customer Segment</text>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Customer Segment",
                                Padding = 4,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 10, IsBold = true }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Customer Segment" }
                },
                new()
                {
                    Id = "strategy.bmc-cost-structure",
                    Name = "Cost Structure",
                    Category = "Strategy",
                    IconSvg = "<rect x='4' y='4' width='24' height='24' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='12' text-anchor='middle' font-size='3' fill='currentColor'>&lt;&lt;Cost&gt;&gt;</text>",
                    DefaultWidth = 140,
                    DefaultHeight = 100,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><text x='50' y='18' text-anchor='middle' font-size='7' fill='var(--stencil-stroke)'>Cost Structure</text>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Cost Structure",
                                Padding = 4,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 10, IsBold = true }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Cost Structure" }
                },
                new()
                {
                    Id = "strategy.bmc-revenue-stream",
                    Name = "Revenue Stream",
                    Category = "Strategy",
                    IconSvg = "<rect x='4' y='4' width='24' height='24' rx='1' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='12' text-anchor='middle' font-size='3' fill='currentColor'>&lt;&lt;Revenue&gt;&gt;</text>",
                    DefaultWidth = 140,
                    DefaultHeight = 100,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rectangle",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><text x='50' y='18' text-anchor='middle' font-size='7' fill='var(--stencil-stroke)'>Revenue Stream</text>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Revenue Stream",
                                Padding = 4,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 10, IsBold = true }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Revenue Stream" }
                },
                new()
                {
                    Id = "strategy.value-stream",
                    Name = "Value Stream",
                    Category = "Strategy",
                    IconSvg = "<polygon points='4,16 10,8 22,8 28,16 22,24 10,24' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='18' text-anchor='middle' font-size='5' fill='currentColor'>&lt;&lt;VS&gt;&gt;</text>",
                    DefaultWidth = 200,
                    DefaultHeight = 80,
                    Ports =
                    [
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "hexagon",
                        ShapeSvg = "<polygon points='25,0 75,0 100,50 75,100 25,100 0,50' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Value Stream",
                                Padding = 8,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12, IsBold = true }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Value Stream" }
                },
                new()
                {
                    Id = "strategy.capability",
                    Name = "Capability",
                    Category = "Strategy",
                    IconSvg = "<rect x='4' y='8' width='24' height='16' rx='4' fill='none' stroke='currentColor' stroke-width='2'/><text x='16' y='20' text-anchor='middle' font-size='5' fill='currentColor'>&lt;&lt;Capability&gt;&gt;</text>",
                    DefaultWidth = 160,
                    DefaultHeight = 80,
                    Ports =
                    [
                        new() { Name = "top", Side = PortSide.Top, Offset = 0.5 },
                        new() { Name = "right", Side = PortSide.Right, Offset = 0.5 },
                        new() { Name = "bottom", Side = PortSide.Bottom, Offset = 0.5 },
                        new() { Name = "left", Side = PortSide.Left, Offset = 0.5 }
                    ],
                    Layout = new()
                    {
                        BackgroundShape = "rounded",
                        ShapeSvg = "<rect x='0' y='0' width='100' height='100' rx='2' fill='var(--stencil-fill)' stroke='var(--stencil-stroke)' stroke-width='var(--stencil-stroke-width)' vector-effect='non-scaling-stroke'/><text x='50' y='18' text-anchor='middle' font-size='8' fill='var(--stencil-stroke)'>Capability</text>",
                        Sections =
                        [
                            new()
                            {
                                Type = "text",
                                DataKey = "name",
                                DefaultText = "Capability",
                                Padding = 0,
                                TextStyle = new() { TextAlign = StencilTextAlign.Center, FontSize = 12, IsBold = true }
                            }
                        ]
                    },
                    DefaultData = new() { ["name"] = "Capability" }
                }
            ]
        };
    }

    private static List<DiagramStencilConnectionPoint> GetConnectionPointsForShape(string? shape)
    {
        var s = shape?.ToLowerInvariant() ?? "rectangle";

        // Ellipse / double-ellipse / half-ellipse / cylinder-top-ish shapes: 8 points on perimeter
        if (s is "ellipse" or "double-ellipse" or "half-ellipse" or "use-case")
        {
            return
            [
                new() { Name = "E",  RelativeX = 1.0, RelativeY = 0.5, Perimeter = true },
                new() { Name = "NE", RelativeX = 0.85, RelativeY = 0.15, Perimeter = true },
                new() { Name = "N",  RelativeX = 0.5, RelativeY = 0.0, Perimeter = true },
                new() { Name = "NW", RelativeX = 0.15, RelativeY = 0.15, Perimeter = true },
                new() { Name = "W",  RelativeX = 0.0, RelativeY = 0.5, Perimeter = true },
                new() { Name = "SW", RelativeX = 0.15, RelativeY = 0.85, Perimeter = true },
                new() { Name = "S",  RelativeX = 0.5, RelativeY = 1.0, Perimeter = true },
                new() { Name = "SE", RelativeX = 0.85, RelativeY = 0.85, Perimeter = true }
            ];
        }

        // Diamond / rhombus: 4 corners + center
        if (s is "diamond" or "rhombus")
        {
            return
            [
                new() { Name = "N",  RelativeX = 0.5, RelativeY = 0.0, Perimeter = true },
                new() { Name = "E",  RelativeX = 1.0, RelativeY = 0.5, Perimeter = true },
                new() { Name = "S",  RelativeX = 0.5, RelativeY = 1.0, Perimeter = true },
                new() { Name = "W",  RelativeX = 0.0, RelativeY = 0.5, Perimeter = true },
                new() { Name = "C",  RelativeX = 0.5, RelativeY = 0.5, Perimeter = false }
            ];
        }

        // Triangle: 3 corners + 3 midpoints
        if (s is "triangle")
        {
            return
            [
                new() { Name = "N",  RelativeX = 0.5, RelativeY = 0.0, Perimeter = true },
                new() { Name = "NE", RelativeX = 0.75, RelativeY = 0.5, Perimeter = true },
                new() { Name = "SE", RelativeX = 0.75, RelativeY = 1.0, Perimeter = true },
                new() { Name = "S",  RelativeX = 0.5, RelativeY = 1.0, Perimeter = true },
                new() { Name = "SW", RelativeX = 0.25, RelativeY = 1.0, Perimeter = true },
                new() { Name = "NW", RelativeX = 0.25, RelativeY = 0.5, Perimeter = true }
            ];
        }

        // Hexagon: 6 corners + top/bottom midpoints
        if (s is "hexagon")
        {
            return
            [
                new() { Name = "N",  RelativeX = 0.5, RelativeY = 0.0, Perimeter = true },
                new() { Name = "NE", RelativeX = 0.875, RelativeY = 0.25, Perimeter = true },
                new() { Name = "E",  RelativeX = 1.0, RelativeY = 0.5, Perimeter = true },
                new() { Name = "SE", RelativeX = 0.875, RelativeY = 0.75, Perimeter = true },
                new() { Name = "S",  RelativeX = 0.5, RelativeY = 1.0, Perimeter = true },
                new() { Name = "SW", RelativeX = 0.125, RelativeY = 0.75, Perimeter = true },
                new() { Name = "W",  RelativeX = 0.0, RelativeY = 0.5, Perimeter = true },
                new() { Name = "NW", RelativeX = 0.125, RelativeY = 0.25, Perimeter = true }
            ];
        }

        // Star: 8 cardinal-ish points on bounding box
        if (s is "star")
        {
            return
            [
                new() { Name = "N",  RelativeX = 0.5, RelativeY = 0.0, Perimeter = true },
                new() { Name = "NE", RelativeX = 1.0, RelativeY = 0.25, Perimeter = true },
                new() { Name = "E",  RelativeX = 1.0, RelativeY = 0.5, Perimeter = true },
                new() { Name = "SE", RelativeX = 1.0, RelativeY = 0.75, Perimeter = true },
                new() { Name = "S",  RelativeX = 0.5, RelativeY = 1.0, Perimeter = true },
                new() { Name = "SW", RelativeX = 0.0, RelativeY = 0.75, Perimeter = true },
                new() { Name = "W",  RelativeX = 0.0, RelativeY = 0.5, Perimeter = true },
                new() { Name = "NW", RelativeX = 0.0, RelativeY = 0.25, Perimeter = true }
            ];
        }

        // Parallelogram: 4 corners + 4 midpoints
        if (s is "parallelogram")
        {
            return
            [
                new() { Name = "NW", RelativeX = 0.15, RelativeY = 0.0, Perimeter = true },
                new() { Name = "N",  RelativeX = 0.575, RelativeY = 0.0, Perimeter = true },
                new() { Name = "NE", RelativeX = 1.0, RelativeY = 0.0, Perimeter = true },
                new() { Name = "E",  RelativeX = 0.85, RelativeY = 0.5, Perimeter = true },
                new() { Name = "SE", RelativeX = 0.85, RelativeY = 1.0, Perimeter = true },
                new() { Name = "S",  RelativeX = 0.425, RelativeY = 1.0, Perimeter = true },
                new() { Name = "SW", RelativeX = 0.0, RelativeY = 1.0, Perimeter = true },
                new() { Name = "W",  RelativeX = 0.15, RelativeY = 0.5, Perimeter = true }
            ];
        }

        // Pentagon: 5 corners + top midpoint
        if (s is "pentagon")
        {
            return
            [
                new() { Name = "N",  RelativeX = 0.5, RelativeY = 0.0, Perimeter = true },
                new() { Name = "NE", RelativeX = 1.0, RelativeY = 0.35, Perimeter = true },
                new() { Name = "E",  RelativeX = 0.95, RelativeY = 0.75, Perimeter = true },
                new() { Name = "SE", RelativeX = 0.7, RelativeY = 1.0, Perimeter = true },
                new() { Name = "S",  RelativeX = 0.3, RelativeY = 1.0, Perimeter = true },
                new() { Name = "SW", RelativeX = 0.05, RelativeY = 0.75, Perimeter = true },
                new() { Name = "NW", RelativeX = 0.0, RelativeY = 0.35, Perimeter = true },
                new() { Name = "W",  RelativeX = 0.15, RelativeY = 0.5, Perimeter = true }
            ];
        }

        // Actor: bounding box with 8 points (perimeter projects to bounding box)
        if (s is "actor")
        {
            return
            [
                new() { Name = "N",  RelativeX = 0.5, RelativeY = 0.0, Perimeter = true },
                new() { Name = "NE", RelativeX = 1.0, RelativeY = 0.25, Perimeter = true },
                new() { Name = "E",  RelativeX = 1.0, RelativeY = 0.5, Perimeter = true },
                new() { Name = "SE", RelativeX = 1.0, RelativeY = 0.75, Perimeter = true },
                new() { Name = "S",  RelativeX = 0.5, RelativeY = 1.0, Perimeter = true },
                new() { Name = "SW", RelativeX = 0.0, RelativeY = 0.75, Perimeter = true },
                new() { Name = "W",  RelativeX = 0.0, RelativeY = 0.5, Perimeter = true },
                new() { Name = "NW", RelativeX = 0.0, RelativeY = 0.25, Perimeter = true }
            ];
        }

        // Cloud: bounding box with 8 points
        if (s is "cloud")
        {
            return
            [
                new() { Name = "N",  RelativeX = 0.5, RelativeY = 0.0, Perimeter = true },
                new() { Name = "NE", RelativeX = 1.0, RelativeY = 0.25, Perimeter = true },
                new() { Name = "E",  RelativeX = 1.0, RelativeY = 0.5, Perimeter = true },
                new() { Name = "SE", RelativeX = 1.0, RelativeY = 0.75, Perimeter = true },
                new() { Name = "S",  RelativeX = 0.5, RelativeY = 1.0, Perimeter = true },
                new() { Name = "SW", RelativeX = 0.0, RelativeY = 0.75, Perimeter = true },
                new() { Name = "W",  RelativeX = 0.0, RelativeY = 0.5, Perimeter = true },
                new() { Name = "NW", RelativeX = 0.0, RelativeY = 0.25, Perimeter = true }
            ];
        }

        // Cylinder: bounding box with 8 points
        if (s is "cylinder")
        {
            return
            [
                new() { Name = "N",  RelativeX = 0.5, RelativeY = 0.0, Perimeter = true },
                new() { Name = "NE", RelativeX = 1.0, RelativeY = 0.15, Perimeter = true },
                new() { Name = "E",  RelativeX = 1.0, RelativeY = 0.5, Perimeter = true },
                new() { Name = "SE", RelativeX = 1.0, RelativeY = 0.85, Perimeter = true },
                new() { Name = "S",  RelativeX = 0.5, RelativeY = 1.0, Perimeter = true },
                new() { Name = "SW", RelativeX = 0.0, RelativeY = 0.85, Perimeter = true },
                new() { Name = "W",  RelativeX = 0.0, RelativeY = 0.5, Perimeter = true },
                new() { Name = "NW", RelativeX = 0.0, RelativeY = 0.15, Perimeter = true }
            ];
        }

        // Lollipop: left end + circle perimeter points
        if (s is "lollipop")
        {
            return
            [
                new() { Name = "W",  RelativeX = 0.0, RelativeY = 0.5, Perimeter = true },
                new() { Name = "NW", RelativeX = 0.6, RelativeY = 0.1, Perimeter = true },
                new() { Name = "N",  RelativeX = 0.8, RelativeY = 0.0, Perimeter = true },
                new() { Name = "NE", RelativeX = 1.0, RelativeY = 0.1, Perimeter = true },
                new() { Name = "E",  RelativeX = 1.0, RelativeY = 0.5, Perimeter = true },
                new() { Name = "SE", RelativeX = 1.0, RelativeY = 0.9, Perimeter = true },
                new() { Name = "S",  RelativeX = 0.8, RelativeY = 1.0, Perimeter = true },
                new() { Name = "SW", RelativeX = 0.6, RelativeY = 0.9, Perimeter = true }
            ];
        }

        // Default (rectangle, rounded, table, swimlane, group, package, note, document, component, cube, etc.):
        // 9 points = 4 corners + 4 midpoints + center
        return
        [
            new() { Name = "NW", RelativeX = 0.0, RelativeY = 0.0, Perimeter = true },
            new() { Name = "N",  RelativeX = 0.5, RelativeY = 0.0, Perimeter = true },
            new() { Name = "NE", RelativeX = 1.0, RelativeY = 0.0, Perimeter = true },
            new() { Name = "E",  RelativeX = 1.0, RelativeY = 0.5, Perimeter = true },
            new() { Name = "C",  RelativeX = 0.5, RelativeY = 0.5, Perimeter = false },
            new() { Name = "W",  RelativeX = 0.0, RelativeY = 0.5, Perimeter = true },
            new() { Name = "SW", RelativeX = 0.0, RelativeY = 1.0, Perimeter = true },
            new() { Name = "S",  RelativeX = 0.5, RelativeY = 1.0, Perimeter = true },
            new() { Name = "SE", RelativeX = 1.0, RelativeY = 1.0, Perimeter = true }
        ];
    }
}
