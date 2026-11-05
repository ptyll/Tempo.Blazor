namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One stop of an autonomy dial: the numeric grade, the label a reader sees and the sentence that says what the agent may
/// do at that stop.
/// <para>
/// The record is a VALUE the host composes — no component mutates it. <see cref="Value"/> is the key a host persists and
/// the ceiling it compares against (a plan's <c>MaxAllowed</c>, a tenant's lock), while the stops are rendered in
/// ascending <see cref="Value"/> order; the label and the description come from the host's own wording or from the
/// component's localized defaults when the host supplies no levels at all.
/// </para>
/// </summary>
public sealed record AutonomyLevel
{
    /// <summary>Creates a stop at the given grade.</summary>
    /// <param name="value">
    /// The numeric grade this stop stands for. Hosts keep it stable across releases: it is what a persisted setting and a
    /// hard ceiling refer to.
    /// </param>
    public AutonomyLevel(int value)
    {
        Value = value;
        Label = string.Empty;
        Description = string.Empty;
    }

    /// <summary>The numeric grade of the stop, and the key a ceiling or a persisted setting compares against.</summary>
    public int Value { get; init; }

    /// <summary>
    /// The grade as the reader should see it ("Ask first", "Ask when risky", "Act freely"). Empty renders the
    /// component's own localized fallback for the stop's position, so no stop is ever blank.
    /// </summary>
    public string Label { get; init; }

    /// <summary>
    /// What the agent may do at this grade, in the host's own words. Empty renders no description line at all rather than
    /// an empty paragraph.
    /// </summary>
    public string Description { get; init; }

    /// <summary>Whether the host supplied a label for this stop.</summary>
    public bool HasLabel => !string.IsNullOrWhiteSpace(Label);

    /// <summary>Whether the host supplied a description for this stop.</summary>
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
}
