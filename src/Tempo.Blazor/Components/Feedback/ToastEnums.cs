namespace Tempo.Blazor.Components.Feedback;

/// <summary>Toast notification severity level.</summary>
public enum ToastSeverity
{
    /// <summary>Informational toast (blue).</summary>
    Info,

    /// <summary>Success toast (green).</summary>
    Success,

    /// <summary>Warning toast (yellow/amber).</summary>
    Warning,

    /// <summary>Error toast (red).</summary>
    Error
}

/// <summary>Position of the toast container on screen.</summary>
public enum ToastPosition
{
    /// <summary>Top-right corner of the screen.</summary>
    TopRight,

    /// <summary>Top-left corner of the screen.</summary>
    TopLeft,

    /// <summary>Top-center of the screen.</summary>
    TopCenter,

    /// <summary>Bottom-right corner of the screen.</summary>
    BottomRight,

    /// <summary>Bottom-left corner of the screen.</summary>
    BottomLeft,

    /// <summary>Bottom-center of the screen.</summary>
    BottomCenter
}

/// <summary>
/// Live-region politeness of a toast announcement: how urgently a screen reader interrupts to
/// speak it. Maps to the ARIA role the container renders: <see cref="Polite"/> →
/// <c>role="status"</c>, <see cref="Assertive"/> → <c>role="alert"</c>.
/// </summary>
public enum ToastPoliteness
{
    /// <summary>Announced when the screen reader is idle (does not interrupt ongoing speech).</summary>
    Polite,

    /// <summary>Announced immediately, interrupting whatever is being spoken.</summary>
    Assertive
}
