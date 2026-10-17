namespace Tempo.Blazor.Components.Diagram;

/// <summary>
/// How the inspector column of <see cref="TmDiagramEditor"/> presents the properties and layers
/// panels (DI2, desktop redesign phase 4). <see cref="Tabs"/> (the default) renders them as
/// "Properties | Layers" tabs; <see cref="Stacked"/> is the opt-in legacy column with both
/// panels stacked above each other.
/// </summary>
public enum DiagramInspectorLayout
{
    /// <summary>The properties and layers panels share one column behind "Properties | Layers" tabs. The default.</summary>
    Tabs = 0,

    /// <summary>Both panels render stacked in one scrollable column (the pre-redesign look), opt-in.</summary>
    Stacked = 1,
}
