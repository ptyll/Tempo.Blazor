using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>Describes the modeling element or relationship the reader activated in the read-only viewer.</summary>
public sealed class ModelingViewerElementEventArgs
{
    /// <summary>
    /// Model element id, or the relationship id when <see cref="IsRelationship"/> is true (the
    /// generator uses the relationship id as the diagram edge id).
    /// </summary>
    public string ElementId { get; set; } = string.Empty;

    /// <summary>True when the activated item is a relationship (an edge) rather than an element.</summary>
    public bool IsRelationship { get; set; }

    /// <summary>Semantic type (e.g. <c>userTask</c> for an element, <c>sequenceFlow</c> for a relationship).</summary>
    public string SemanticType { get; set; } = string.Empty;

    /// <summary>Display name (the model's name, falling back to the id).</summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// The context handed to <c>TmModelingViewer.ElementListContent</c> for ONE row of the text alternative
/// list. The viewer keeps the list semantics and the keyboard model; the host replaces the row's
/// content (typically with a link or a richer card).
/// </summary>
public sealed class ModelingViewerElementContext
{
    /// <summary>Model element id, or the relationship id when <see cref="IsRelationship"/> is true.</summary>
    public required string ElementId { get; init; }

    /// <summary>True when the row represents a relationship (an edge) rather than an element.</summary>
    public bool IsRelationship { get; init; }

    /// <summary>Semantic type of the row (element or relationship type).</summary>
    public string SemanticType { get; init; } = string.Empty;

    /// <summary>Display name (the model's name, falling back to the id).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>True when the row is the current <c>SelectedElementId</c>.</summary>
    public bool IsSelected { get; init; }

    /// <summary>
    /// True when the row is only in the baseline: it is listed (so the text alternative is complete)
    /// but never drawn on the canvas.
    /// </summary>
    public bool IsRemoved { get; init; }

    /// <summary>The diff state of the row (<c>diff-added</c>, <c>diff-changed</c>, <c>diff-layout</c>), or null.</summary>
    public string? DiffState { get; init; }

    /// <summary>The raw heatmap value 0..1 of the row, or null when the heatmap has no value for it.</summary>
    public double? HeatmapValue { get; init; }

    /// <summary>The formatted heatmap value, or null (already culture-formatted).</summary>
    public string? HeatmapValueText { get; init; }
}
