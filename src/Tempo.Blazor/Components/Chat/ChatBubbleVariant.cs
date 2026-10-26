namespace Tempo.Blazor.Components.Chat;

/// <summary>
/// Visual variant of a <see cref="TmChatBubble"/>. Each member maps to a
/// <c>tm-chat-bubble--incoming</c> style modifier class (block class + kebab-cased member
/// name) and to one of the <c>--tm-chat-bubble-*</c> custom property sets: <c>in</c>
/// (Incoming), <c>out</c> (Outgoing), <c>system</c> (System and Pill), <c>pending</c>,
/// <c>queued</c>, <c>discarded</c> and <c>locked</c> — so a host restyles a whole family of
/// bubbles by re-pointing one knob.
/// </summary>
public enum ChatBubbleVariant
{
    /// <summary>A message received from somebody else.</summary>
    Incoming,

    /// <summary>A message sent by the current user.</summary>
    Outgoing,

    /// <summary>A system event row (joins, leaves, status changes).</summary>
    System,

    /// <summary>A compact inline pill (tips, short notices). Shares the <c>system</c> colours.</summary>
    Pill,

    /// <summary>Content still being processed (an AI draft awaiting review).</summary>
    Pending,

    /// <summary>Content waiting to be delivered (a scheduled message).</summary>
    Queued,

    /// <summary>Withdrawn content, struck through with a host-provided note.</summary>
    Discarded,

    /// <summary>Locked content — the body is never rendered, only badges/notes/actions.</summary>
    Locked,
}
