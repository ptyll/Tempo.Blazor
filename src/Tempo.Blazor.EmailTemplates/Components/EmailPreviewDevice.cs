namespace Tempo.Blazor.EmailTemplates.Components;

/// <summary>
/// The device the preview is rendered for (§3.4/D17). <see cref="Tablet"/> is 768 px and
/// <see cref="Mobile"/> 390 px; <see cref="Desktop"/> is the document content width plus the stage margin.
/// The widths live in <see cref="EmailPreviewMetrics"/> so the component, its CSS and its tests cannot
/// drift apart.
/// </summary>
public enum EmailPreviewDevice
{
    /// <summary>Desktop preview: the document's own content width.</summary>
    Desktop = 0,

    /// <summary>Tablet preview (768 px).</summary>
    Tablet = 1,

    /// <summary>Mobile preview (390 px).</summary>
    Mobile = 2,
}
