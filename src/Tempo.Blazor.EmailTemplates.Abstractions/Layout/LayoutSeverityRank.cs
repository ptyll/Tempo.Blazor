namespace Tempo.Blazor.EmailTemplates.Abstractions.Layout;

/// <summary>
/// The display order of <see cref="LayoutSeverity"/> findings: Error first, then Warning, then Info.
/// </summary>
/// <remarks>
/// The enum's numeric values (0/1/2) are a serialization contract and put Info LAST only by accident of
/// history; ordering findings by the numeric value would rank a recommendation above a warning. Every
/// consumer that sorts findings goes through this rank (D19).
/// </remarks>
public static class LayoutSeverityRank
{
    /// <summary>Returns the display rank of <paramref name="severity"/>: Error = 0, Warning = 1, Info = 2.</summary>
    /// <param name="severity">The severity to rank.</param>
    /// <returns>The rank; lower sorts first.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The severity is not a known value.</exception>
    public static int Of(LayoutSeverity severity) => severity switch
    {
        LayoutSeverity.Error => 0,
        LayoutSeverity.Warning => 1,
        LayoutSeverity.Info => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, "Unknown layout severity."),
    };
}
