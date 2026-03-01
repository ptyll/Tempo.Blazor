namespace Tempo.Blazor.Components.Feedback;

/// <summary>
/// Severity level for the alert component.
/// </summary>
public enum AlertSeverity
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>
/// Visual variant for the alert component.
/// </summary>
public enum AlertVariant
{
    /// <summary>Subtle background tint (default).</summary>
    Soft,

    /// <summary>Fully colored background with white text.</summary>
    Filled,

    /// <summary>Bordered with transparent background.</summary>
    Outlined
}
