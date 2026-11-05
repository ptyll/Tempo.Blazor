namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One step of an agent-to-agent handoff chain: who handed the work to whom, when, what it said about it, what
/// travelled and what was lost, and how far the receiving agent got.
/// <para>
/// The record is a VALUE: a host recomposes it with <c>step with { … }</c> when the receiving agent accepts or finishes
/// the work, and no component mutates it. Both agent identities are the shared <see cref="AgentRef"/>, so a step paints
/// the same initials, name and palette slot every other agent surface uses; a host that tracks agents by id only
/// supplies <see cref="FromAgentId"/> / <see cref="ToAgentId"/> and the surface prints the id.
/// </para>
/// <para>
/// The two shapes of one identity may COEXIST (<c>FromAgent</c> plus <c>FromAgentId</c>), which is what a host
/// mid-migration produces. There is one rule and no validation: the <see cref="AgentRef"/> WINS, because it is the
/// whole identity, so a raw id that disagrees is simply not read — the name, the initials and the palette slot all
/// come from the same reference instead of one being taken from each. A nameless <see cref="AgentRef"/> does not
/// silence the fallback either: <see cref="FromName"/> still prints <see cref="FromAgentId"/> when the reference
/// carries no name, because an unnamed agent must never render blank. A disagreement never throws, because a host
/// that fills both is describing the same agent twice, not reporting a conflict.
/// </para>
/// <para>
/// The step deliberately does not decide what "lost" costs: the two item lists are the host's truth and
/// <see cref="LostCount"/> / <see cref="LossRate"/> are the arithmetic a surface and a caller share, so a summary can
/// never disagree with the lists it summarises.
/// </para>
/// </summary>
public sealed record HandoffStep
{
    /// <summary>Creates a handoff step.</summary>
    /// <param name="id">
    /// Stable identity of the step — the key an expanded detail and a focus hand-back are tracked by. Null or
    /// whitespace generates one, so a list built in a loop still renders; a host that has to match a step to its own
    /// record supplies its own id (the same rule <see cref="AgentRef"/> and <see cref="FanOutTask"/> follow).
    /// </param>
    /// <param name="summary">
    /// What the handing-over agent said about the work, in the host's own words. Empty renders the surface's own
    /// localized fallback instead of a blank line.
    /// </param>
    public HandoffStep(string id, string summary)
    {
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("n") : id!;
        Summary = summary ?? string.Empty;
    }

    /// <summary>Stable identity of the step. Never empty: a missing id is generated at construction.</summary>
    public string Id { get; init; }

    /// <summary>
    /// The agent that handed the work over, or <c>null</c> when the host tracks agents by id only. Setting it makes the
    /// surface paint the agent's initials, name and palette slot.
    /// </summary>
    public AgentRef? FromAgent { get; init; }

    /// <summary>
    /// The agent that received the work, or <c>null</c> when the host tracks agents by id only.
    /// </summary>
    public AgentRef? ToAgent { get; init; }

    /// <summary>
    /// The handing-over agent's id as a plain string, for a host whose agents are not <see cref="AgentRef"/>s.
    /// <see cref="FromAgent"/> wins when both are set, because it carries the whole identity.
    /// </summary>
    public string? FromAgentId { get; init; }

    /// <summary>The receiving agent's id as a plain string. <see cref="ToAgent"/> wins when both are set.</summary>
    public string? ToAgentId { get; init; }

    /// <summary>When the handoff happened, or <c>null</c> while it is still pending.</summary>
    public DateTimeOffset? At { get; init; }

    /// <summary>What the handing-over agent said about the work. Empty renders the surface's own fallback.</summary>
    public string Summary { get; init; }

    /// <summary>
    /// The things that reached the next agent, in the order they should be read. Empty is legal (nothing useful
    /// travelled) and renders the surface's own "nothing was passed on" line.
    /// </summary>
    public IReadOnlyList<HandoffItem> PassedItems
    {
        get => _passedItems;
        init => _passedItems = value ?? [];
    }

    /// <summary>
    /// The things that never reached the next agent. Empty is legal and renders the surface's own "nothing was lost"
    /// line; each entry may carry the reason it was lost.
    /// </summary>
    public IReadOnlyList<HandoffItem> LostItems
    {
        get => _lostItems;
        init => _lostItems = value ?? [];
    }

    /// <summary>
    /// How far the handoff has got. Default <see cref="HandoffStatus.Pending"/>; painted as an icon, a localized
    /// word AND a tone, never as a colour alone.
    /// </summary>
    public HandoffStatus Status { get; init; }

    private readonly IReadOnlyList<HandoffItem> _passedItems = [];
    private readonly IReadOnlyList<HandoffItem> _lostItems = [];

    /// <summary>Whether the host supplied a summary worth printing.</summary>
    public bool HasSummary => !string.IsNullOrWhiteSpace(Summary);

    /// <summary>How many items reached the next agent.</summary>
    public int PassedCount => _passedItems.Count;

    /// <summary>How many items never arrived.</summary>
    public int LostCount => _lostItems.Count;

    /// <summary>How many items the step tried to hand over (passed plus lost).</summary>
    public int ItemCount => _passedItems.Count + _lostItems.Count;

    /// <summary>Whether anything was lost in this step — the fact the surface paints first.</summary>
    public bool HasLosses => _lostItems.Count > 0;

    /// <summary>
    /// How much of what the step carried was lost, from 0 to 1, or 0 when it carried nothing (there is no loss rate
    /// without a whole).
    /// </summary>
    public double LossRate => ItemCount == 0 ? 0d : LostCount / (double)ItemCount;

    /// <summary>Whether the handoff was answered (accepted or completed).</summary>
    public bool IsSettled => Status is HandoffStatus.Accepted or HandoffStatus.Completed;

    /// <summary>Whether the handoff is still waiting for the receiving agent.</summary>
    public bool IsPending => Status == HandoffStatus.Pending;

    /// <summary>Whether the receiving agent refused the work.</summary>
    public bool IsRejected => Status == HandoffStatus.Rejected;

    /// <summary>Whether <see cref="Status"/> is one of the declared states (a cast integer may not be).</summary>
    public bool HasKnownStatus => Status is >= HandoffStatus.Pending and <= HandoffStatus.Completed;

    /// <summary>The handing-over agent's name for a surface to print: the agent's name, else the raw id.</summary>
    public string FromName => FromAgent is { HasName: true } from ? from.Name : FromAgentId ?? string.Empty;

    /// <summary>The receiving agent's name for a surface to print: the agent's name, else the raw id.</summary>
    public string ToName => ToAgent is { HasName: true } to ? to.Name : ToAgentId ?? string.Empty;

    /// <summary>Whether the host named the handing-over agent in any way.</summary>
    public bool HasFrom => !string.IsNullOrWhiteSpace(FromName);

    /// <summary>Whether the host named the receiving agent in any way.</summary>
    public bool HasTo => !string.IsNullOrWhiteSpace(ToName);

    /// <summary>
    /// The agent palette token the handing-over agent's avatar paints with, or an empty string when no
    /// <see cref="FromAgent"/> is set. Always a declared <c>--tm-agent-color-{0..7}</c> token when it is not empty.
    /// </summary>
    public string FromColorToken => FromAgent is { } from ? from.ColorToken : string.Empty;

    /// <summary>The receiving agent's palette token, or an empty string when no <see cref="ToAgent"/> is set.</summary>
    public string ToColorToken => ToAgent is { } to ? to.ColorToken : string.Empty;

    /// <summary>
    /// The distinct labels of everything the step lost, in the order the losses were declared — the "what did we
    /// actually drop" list a reader can act on.
    /// </summary>
    public IReadOnlyList<string> LostLabels
    {
        get
        {
            if (_lostItems.Count == 0)
            {
                return [];
            }

            var labels = new List<string>(_lostItems.Count);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in _lostItems)
            {
                if (item is not null && item.HasLabel && seen.Add(item.Label))
                {
                    labels.Add(item.Label);
                }
            }

            return labels;
        }
    }
}
