namespace Tempo.Blazor.Models;

/// <summary>
/// The outcome of one <see cref="TraceSpan"/>.
/// <para>
/// <see cref="Running"/> is the only value that keeps a span's bar growing: a running span has no
/// <see cref="TraceSpan.EndedAt"/>, so the layout extends it to the caller's "now". Every other
/// status is rendered with its own icon <em>and</em> localized text, so an error is never signalled
/// by colour alone.
/// </para>
/// </summary>
public enum TraceSpanStatus
{
    /// <summary>The span completed successfully.</summary>
    Ok,

    /// <summary>The span failed. Rendered with an error icon and the localized "Error" text.</summary>
    Error,

    /// <summary>The span is still in flight; its bar extends to "now" and the live mode ticks.</summary>
    Running,

    /// <summary>The span was cancelled before it completed.</summary>
    Cancelled
}
