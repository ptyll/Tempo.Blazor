namespace Tempo.Blazor.Components.AI;

/// <summary>
/// How a <see cref="TmMemoryPanel"/> presents its status filter.
/// <para>
/// <see cref="Auto"/> (the default) renders BOTH surfaces and lets a container query decide which one is visible: on a
/// wide card the five-segment tray, and on a narrow one (a 375 px phone card) a real <see cref="Tempo.Blazor.Components.Inputs.TmSelect"/>
/// — a segmented tray that has to scroll reads as clipped exactly where it is the only way to narrow the list. A host
/// that knows better pins one presentation instead.
/// </para>
/// <para>
/// The switch is a container query on the panel's OWN box, never the viewport: the same panel is wide in a dashboard
/// column and narrow inside a sheet on the very same screen.
/// </para>
/// </summary>
public enum MemoryFilterPresentation
{
    /// <summary>Both surfaces are rendered; a container query shows the tray on a wide card and the select on a narrow one.</summary>
    Auto = 0,

    /// <summary>The segmented tray only, at every width.</summary>
    Segmented = 1,

    /// <summary>The select only, at every width.</summary>
    Select = 2,
}
