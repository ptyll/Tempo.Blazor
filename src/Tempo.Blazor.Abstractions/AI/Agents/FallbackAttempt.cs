namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One model tried in a fallback chain: which model it was, how the attempt ended, why it failed or was skipped, how
/// long it took and when it ran.
/// <para>
/// The record is a VALUE: a host recomposes it with <c>attempt with { … }</c> as the chain walks on, and no component
/// mutates it. <see cref="ModelId"/> is the identity the host's own catalogue uses (the same id an
/// <see cref="AiModelInfo"/> carries), so an attempt can be matched to the model that was picked; the surface never
/// needs the catalogue to paint an attempt.
/// </para>
/// <para>
/// A failure or a skip is only ever painted together with its REASON and a localized word: a red branch on a diagram
/// is not an explanation (WCAG 1.4.1), and an attempt that reports no latency simply shows no latency rather than a
/// zero the host never measured.
/// </para>
/// </summary>
public sealed record FallbackAttempt
{
    /// <summary>Creates an attempt.</summary>
    /// <param name="modelId">
    /// The identity of the model in the host's catalogue. Null or whitespace generates one, so a list built in a loop
    /// still renders; a host that matches the attempt to its own record supplies its id.
    /// </param>
    public FallbackAttempt(string modelId)
    {
        ModelId = string.IsNullOrWhiteSpace(modelId) ? Guid.NewGuid().ToString("n") : modelId!;
        Label = string.Empty;
    }

    /// <summary>Stable identity of the model that was tried. Never empty: a missing id is generated at construction.</summary>
    public string ModelId { get; init; }

    /// <summary>
    /// The model as the reader should see it, when the host's catalogue names it differently from the id. Empty falls
    /// back to <see cref="ModelId"/>, so no attempt is ever blank.
    /// </summary>
    public string Label { get; init; }

    /// <summary>
    /// How the attempt ended. Default <see cref="FallbackAttemptStatus.NotTried"/>; painted as an icon, a localized
    /// word AND a tone, never as a colour alone.
    /// </summary>
    public FallbackAttemptStatus Status { get; init; }

    /// <summary>
    /// Why the attempt failed or was skipped, in the host's own words ("rate limited", "no vision support", "budget
    /// exhausted"). Empty renders no reason; the surface prints it verbatim and never parses it.
    /// </summary>
    public string? FailureReason { get; init; }

    /// <summary>
    /// How long the attempt took, in MILLISECONDS, or <c>null</c> when the host did not measure it. A negative value
    /// reads as "not measured" too — a duration below zero is not a fact a surface can print.
    /// </summary>
    public long? LatencyMs { get; init; }

    /// <summary>When the attempt ran, or <c>null</c> when the host does not record it.</summary>
    public DateTimeOffset? At { get; init; }

    /// <summary>The name a surface prints: <see cref="Label"/> when the host supplied one, else <see cref="ModelId"/>.</summary>
    public string DisplayName => HasLabel ? Label : ModelId;

    /// <summary>Whether the host supplied a label for the model.</summary>
    public bool HasLabel => !string.IsNullOrWhiteSpace(Label);

    /// <summary>Whether the attempt carries a reason worth printing.</summary>
    public bool HasFailureReason => !string.IsNullOrWhiteSpace(FailureReason);

    /// <summary>Whether the host measured a usable latency (a missing or negative value counts as none).</summary>
    public bool HasLatency => LatencyMs is { } latency && latency >= 0;

    /// <summary>The measured latency as a duration, or <c>null</c> when the host did not measure one.</summary>
    public TimeSpan? Latency => HasLatency ? TimeSpan.FromMilliseconds(LatencyMs!.Value) : null;

    /// <summary>Whether the attempt produced the answer.</summary>
    public bool Succeeded => Status == FallbackAttemptStatus.Succeeded;

    /// <summary>Whether the attempt ran and failed.</summary>
    public bool Failed => Status == FallbackAttemptStatus.Failed;

    /// <summary>Whether the attempt was deliberately not made.</summary>
    public bool Skipped => Status == FallbackAttemptStatus.Skipped;

    /// <summary>Whether the attempt never ran.</summary>
    public bool NotTried => Status == FallbackAttemptStatus.NotTried;

    /// <summary>
    /// Whether the attempt is a passive outcome (not tried or skipped) — the states in which nothing was spent and the
    /// chain simply moved on.
    /// </summary>
    public bool IsPassive => Status is FallbackAttemptStatus.NotTried or FallbackAttemptStatus.Skipped;

    /// <summary>Whether <see cref="Status"/> is one of the declared states (a cast integer may not be).</summary>
    public bool HasKnownStatus => Status is >= FallbackAttemptStatus.NotTried and <= FallbackAttemptStatus.Succeeded;
}
