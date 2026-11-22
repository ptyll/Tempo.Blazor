namespace Tempo.Blazor.Models;

/// <summary>
/// Which occurrence of an ambiguous local time (the fall-back DST hour) a time selector reports.
/// The offset itself is raised separately because <see cref="TimeOnly"/> cannot carry it.
/// </summary>
public enum AmbiguousTimeResolution
{
    /// <summary>The earlier occurrence (still on summer time). The default.</summary>
    Earlier = 0,

    /// <summary>The later occurrence (already on standard time).</summary>
    Later = 1,
}
