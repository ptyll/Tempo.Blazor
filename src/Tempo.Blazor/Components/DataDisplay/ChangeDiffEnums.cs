namespace Tempo.Blazor.Components.DataDisplay;

/// <summary>
/// How the old/new values of a <see cref="TmChangeDiff"/> row behave when they do not fit the
/// available width. Default <see cref="Ellipsis"/> keeps the historical single-line truncation.
/// </summary>
public enum TmChangeDiffOverflow
{
    /// <summary>Single line, truncated with an ellipsis (the historical behavior, default).</summary>
    Ellipsis,

    /// <summary>The value wraps onto multiple lines instead of truncating.</summary>
    Wrap,

    /// <summary>The value renders in full: no truncation, no wrapping constraint (the row grows).</summary>
    Full,
}
