namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One present agent (or person) and where on the content their cursor sits — the input of a presence overlay.
/// <para>
/// The marker is anchored EITHER to an element inside the content (<see cref="AnchorId"/>, matched against
/// <c>data-presence-anchor</c>) or to a point the host computes itself (<see cref="X"/>/<see cref="Y"/> as fractions of
/// the content box). An anchor is measured from the real DOM and therefore survives a scroll, a resize and a reflow,
/// so it WINS when a marker carries both; the point is the fallback for content the host cannot annotate.
/// </para>
/// <para>
/// The record is a VALUE: a host recomposes it with <c>marker with { Activity = … }</c> as presence changes and the
/// component never mutates it. <see cref="X"/> and <see cref="Y"/> are normalised into <c>0…1</c> on assignment, so a
/// host that hands over a pixel offset by mistake still paints inside the content instead of off the edge.
/// </para>
/// </summary>
public sealed record PresenceMarker
{
    /// <summary>Creates a presence marker.</summary>
    /// <param name="id">Stable identity of the marker, used as the <c>@key</c> and as the handle the module places.
    /// Null or whitespace generates one, so two markers for the same agent are still two markers.</param>
    /// <param name="agent">The agent the cursor belongs to. Required.</param>
    /// <exception cref="ArgumentNullException"><paramref name="agent"/> is null.</exception>
    public PresenceMarker(string? id, AgentRef agent)
    {
        Agent = agent ?? throw new ArgumentNullException(nameof(agent));
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("n") : id!;
    }

    /// <summary>Stable identity of the marker. Never empty: a missing id is generated at construction.</summary>
    public string Id { get; init; }

    /// <summary>The agent (or person) the cursor belongs to.</summary>
    public AgentRef Agent { get; init; }

    /// <summary>
    /// The value of the <c>data-presence-anchor</c> attribute on an element inside the content this marker points at.
    /// Null or whitespace means the marker uses <see cref="X"/>/<see cref="Y"/> instead.
    /// </summary>
    public string? AnchorId { get; init; }

    private double _x;

    /// <summary>
    /// The horizontal position as a fraction of the content box (<c>0</c> is the inline start, <c>1</c> the inline end),
    /// normalised into <c>0…1</c> on assignment. Ignored when <see cref="AnchorId"/> is set.
    /// </summary>
    public double X
    {
        get => _x;
        init => _x = Normalize(value);
    }

    private double _y;

    /// <summary>
    /// The vertical position as a fraction of the content box (<c>0</c> is the top, <c>1</c> the bottom), normalised
    /// into <c>0…1</c> on assignment. Ignored when <see cref="AnchorId"/> is set.
    /// </summary>
    public double Y
    {
        get => _y;
        init => _y = Normalize(value);
    }

    /// <summary>What the agent is doing at the place the marker points at.</summary>
    public PresenceActivity Activity { get; init; }

    /// <summary>
    /// The host's own sentence for the activity ("Reviewing the summary"), used INSTEAD of the component's localized
    /// activity word. Null renders the localized word, which is not the same as an empty label.
    /// </summary>
    public string? Label { get; init; }

    /// <summary>Whether the marker is anchored to an element of the content (so it is measured, not computed).</summary>
    public bool HasAnchor => !string.IsNullOrWhiteSpace(AnchorId);

    /// <summary>Whether the marker carries a usable point (a marker with neither an anchor nor a point paints at the
    /// content's origin, which is a real position rather than a hidden marker).</summary>
    public bool HasPoint => !HasAnchor;

    /// <summary>Whether the host supplied its own activity sentence.</summary>
    public bool HasLabel => !string.IsNullOrWhiteSpace(Label);

    /// <summary>Normalises a coordinate into <c>0…1</c>; a value that is not finite reads as <c>0</c>.</summary>
    /// <param name="value">The fraction to normalise.</param>
    /// <returns>The clamped fraction.</returns>
    public static double Normalize(double value)
        => double.IsFinite(value) ? Math.Clamp(value, 0d, 1d) : 0d;
}
