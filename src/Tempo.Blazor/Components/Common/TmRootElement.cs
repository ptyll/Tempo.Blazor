namespace Tempo.Blazor.Components.Common;

/// <summary>
/// The root HTML element a component renders. It exists so a component that may sit inside
/// PHRASING content — a whole-card <c>&lt;a&gt;</c>/<c>&lt;button&gt;</c> target such as
/// <c>TmStatCard</c> — can promise a phrasing root instead of nesting a block in an inline element.
/// </summary>
public enum TmRootElement
{
    /// <summary>
    /// Renders a <c>&lt;div&gt;</c> (the historical root of <c>TmSparkline</c> and <c>TmSkeleton</c>,
    /// so a host that never sets the parameter renders exactly what it rendered before).
    /// </summary>
    Div = 0,

    /// <summary>
    /// Renders a <c>&lt;span&gt;</c>, i.e. a phrasing root and a phrasing subtree. Use it when the
    /// component lives inside a link, a button or another element that allows phrasing content only.
    /// </summary>
    Span = 1
}
