namespace Tempo.Blazor.Components.AI;

/// <summary>
/// How a <see cref="TmAutonomyDial"/> presents the stops of autonomy.
/// <para>
/// <see cref="Dial"/> is a real <c>role="slider"</c> with one tab stop, arrow-key stepping and Home/End, plus a pair of
/// native step buttons as the pointer alternative (WCAG 2.5.7). <see cref="Segmented"/> is a radio group in which every
/// stop is its own target, which is what a form or a touch-first dialog usually wants.
/// </para>
/// </summary>
public enum AutonomyDialPresentation
{
    /// <summary>One slider over the stops, with native step buttons for pointer users.</summary>
    Dial = 0,

    /// <summary>A segmented radio group in which every stop is a target of its own.</summary>
    Segmented = 1,
}
