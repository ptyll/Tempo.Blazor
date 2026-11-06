namespace Tempo.Blazor.AI.Models;

/// <summary>
/// What a present agent (or person) is doing at the place their marker points at — the word a presence label carries
/// next to the name.
/// <para>
/// The enum is a small, shared vocabulary on purpose: the activity is the SECOND thing a reader needs (the name comes
/// first) and a host that invented its own words would make two surfaces describe the same presence differently. A
/// label may override it with the host's own sentence through <see cref="PresenceMarker.Label"/>.
/// </para>
/// </summary>
public enum PresenceActivity
{
    /// <summary>The agent is only looking at the content.</summary>
    Viewing,

    /// <summary>The agent is writing new content at the marker.</summary>
    Typing,

    /// <summary>The agent is changing content that already exists at the marker.</summary>
    Editing,

    /// <summary>The agent is leaving a comment at the marker.</summary>
    Commenting,
}
