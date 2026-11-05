namespace Tempo.Blazor.AI.Models;

/// <summary>
/// How a host resolves which model answers when a chain offers more than one — the rule the chain FOLLOWS, which is a
/// different fact from the order of the chain itself.
/// <para>
/// <see cref="Pin"/> is the default (0) on purpose: an unset policy must not silently fall back to another model,
/// because switching models mid-run is a decision a host has to make explicitly. The surface paints the policy as a
/// word, never as a colour, so a reader can tell a pinned single model from an ordered fallback.
/// </para>
/// </summary>
public enum FallbackResolutionPolicy
{
    /// <summary>Only the first model of the chain is used; nothing falls back.</summary>
    Pin = 0,

    /// <summary>The chain is walked in order until an attempt produces an answer.</summary>
    Fallback = 1,

    /// <summary>The host's router picks a model per request; the chain is its preference order.</summary>
    Auto = 2,
}
