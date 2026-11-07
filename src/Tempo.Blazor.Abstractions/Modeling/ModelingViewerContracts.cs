namespace Tempo.Blazor.Modeling;

/// <summary>How the read-only modeling viewer fits the generated diagram into its box.</summary>
public enum ModelingViewerFitMode
{
    /// <summary>Scale by the container width only — a tall diagram stays scrollable.</summary>
    Width,

    /// <summary>Fit the whole page (both axes) into the container.</summary>
    Page
}

/// <summary>
/// The diff overlay of <c>TmModelingViewer</c>: which semantic element / relationship ids were added,
/// removed, changed or moved between two model versions. The viewer never draws an id that is absent
/// from the model — removed ids are counted by the legend and, when <see cref="BaselineModel"/> is
/// supplied, named in the viewer's text alternative list (a "Removed" group).
/// </summary>
public sealed class ModelingDiffOverlayDto
{
    /// <summary>Element ids present in the current model but not in the baseline.</summary>
    public IReadOnlyCollection<string> AddedElementIds { get; set; } = [];

    /// <summary>Element ids present in the baseline but no longer in the current model.</summary>
    public IReadOnlyCollection<string> RemovedElementIds { get; set; } = [];

    /// <summary>Element ids whose semantic content changed between the versions.</summary>
    public IReadOnlyCollection<string> ChangedElementIds { get; set; } = [];

    /// <summary>Element ids that changed their stored position only.</summary>
    public IReadOnlyCollection<string> LayoutOnlyElementIds { get; set; } = [];

    /// <summary>Relationship ids present in the current model but not in the baseline.</summary>
    public IReadOnlyCollection<string> AddedRelationshipIds { get; set; } = [];

    /// <summary>Relationship ids present in the baseline but no longer in the current model.</summary>
    public IReadOnlyCollection<string> RemovedRelationshipIds { get; set; } = [];

    /// <summary>
    /// Optional "before" model. Removed ids are resolved against it so the legend and the element
    /// list can NAME removed items (they are listed, never drawn).
    /// </summary>
    public ModelingModelDto? BaselineModel { get; set; }

    /// <summary>Optional host-supplied title of the diff legend.</summary>
    public string? LegendTitle { get; set; }
}

/// <summary>
/// The heatmap overlay of <c>TmModelingViewer</c>: one normalized value in the range 0..1 per element
/// id (or relationship id). A missing id gets no tint; the value is also rendered as text in the
/// viewer's element list and in the selected element's legend line, so the picture never carries the
/// information by colour alone.
/// </summary>
public sealed class ModelingHeatmapDto
{
    /// <summary>Normalized value 0..1 per element id (or relationship id). Missing = no tint.</summary>
    public IReadOnlyDictionary<string, double> Values { get; set; } = new Dictionary<string, double>(StringComparer.Ordinal);

    /// <summary>Label of the measured quantity, e.g. "SLA risk"; formatted with the value.</summary>
    public string? ValueLabel { get; set; }

    /// <summary>Label of the low end of the scale (defaults to 0 %).</summary>
    public string? LegendMinLabel { get; set; }

    /// <summary>Label of the high end of the scale (defaults to 100 %).</summary>
    public string? LegendMaxLabel { get; set; }

    /// <summary>Composite format applied to a value, e.g. <c>"{0:P0}"</c>. Null formats 0..1 with two decimals.</summary>
    public string? Format { get; set; }
}
