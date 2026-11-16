namespace Tempo.Blazor.Components;

/// <summary>
/// Process-wide counter for generated per-instance DOM ids (the <c>tm-{component}-{n}</c> scheme).
/// The holder is deliberately NON-generic: a static counter inside a generic component
/// (<c>TmSelect&lt;TValue&gt;</c>) is per closed type, and three selects over three different
/// <c>TValue</c>s on one page would share one id. The counter is a <see langword="long"/> so a
/// long-lived process can never wrap the 32-bit range into a negative (or reused) id.
/// </summary>
internal static class TmSequentialId
{
    private static long _sequence;

    /// <summary>Returns the next process-wide sequence number (thread-safe).</summary>
    internal static long Next() => Interlocked.Increment(ref _sequence);
}
