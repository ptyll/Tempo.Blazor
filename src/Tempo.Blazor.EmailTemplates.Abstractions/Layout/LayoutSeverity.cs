namespace Tempo.Blazor.EmailTemplates.Abstractions.Layout;

/// <summary>Severity of a layout validation message.</summary>
/// <remarks>
/// The numeric values are a serialized contract: <see cref="Warning"/> and <see cref="Error"/> keep the
/// numbers they shipped with (0 and 1) and <see cref="Info"/> was appended as 2 (D19). Display order is
/// NOT the numeric order — use <see cref="LayoutSeverityRank.Of"/>.
/// </remarks>
public enum LayoutSeverity
{
    /// <summary>A non-fatal issue worth surfacing.</summary>
    Warning = 0,

    /// <summary>A structural problem that would produce broken or invalid output.</summary>
    Error = 1,

    /// <summary>A recommendation: nothing is broken, the output could simply be better (D9).</summary>
    Info = 2,
}
