namespace Tempo.Blazor.Components.AI;

/// <summary>
/// How big a <c>TmAgentBadge</c> renders. The size scales the avatar circle and the text together, so a lane header
/// and a fan-out tile can pick the density their surface needs without either of them hard-coding a pixel size.
/// </summary>
public enum AgentBadgeSize
{
    /// <summary>Compact — the density of a table row or a graph node label (20 px avatar).</summary>
    Sm = 0,

    /// <summary>The default — a lane header, a card title (24 px avatar).</summary>
    Md = 1,

    /// <summary>Prominent — the agent in focus, a detail header (32 px avatar).</summary>
    Lg = 2,
}
