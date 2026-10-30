using System.Globalization;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components;
using Tempo.Blazor.Components.Icons;

namespace Tempo.Blazor.Components.Chat;

/// <summary>
/// A presentation chat bubble for hosts that build their own message rows — the styling
/// counterpart of the <c>TmChat.MessageTemplate</c> slot. Markup passed into a template carries
/// the HOST's CSS scope, so the scoped classes of <c>TmChat.razor.css</c> do not apply to it;
/// this component brings its own scoped stylesheet (mirrored in the global fallback
/// <c>wwwroot/css/components/_chat.css</c>) and every colour, radius and width is driven by
/// <c>--tm-chat-bubble-*</c> custom properties a host can override.
/// <para>
/// The component carries NO domain copy: nothing here says "draft", "scheduled", shows a price
/// or explains a discard — all labels, badges and notes come from the host through the
/// <see cref="Badge"/>, <see cref="Meta"/>, <see cref="Note"/> and <see cref="Actions"/> slots
/// and the <see cref="ActorLabel"/> parameter. <see cref="TmChat"/> stays the owner of the
/// conversation; this component only paints one bubble.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// Variants with reserved semantics: <see cref="ChatBubbleVariant.Discarded"/> renders its body
/// as a semantic <c>&lt;del&gt;</c> element (strikethrough comes from HTML, not CSS);
/// <see cref="ChatBubbleVariant.Locked"/> NEVER renders <see cref="ChildContent"/> — locked
/// content must not leak into the DOM even when the host passes it (render the price through
/// <see cref="Badge"/>, the hint through <see cref="Note"/> and the unlock button through
/// <see cref="Actions"/> instead).
/// </para>
/// <para>
/// Dark mode needs no <c>[data-theme="dark"]</c> overrides: every knob's default references a
/// semantic token (<c>--tm-bg-muted</c>, <c>--tm-color-info-text</c>, …) that already flips in
/// <c>tokens-dark.css</c>, and the knobs are resolved on the bubble element — inside whatever
/// region the host themed.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// &lt;TmChat Messages="_messages"&gt;
///     &lt;MessageTemplate Context="context"&gt;
///         &lt;TmChatBubble Variant="ChatBubbleVariant.Pending"
///                       ActorLabel="@(context.Message.Author?.Name)"
///                       Timestamp="context.Message.Timestamp"&gt;
///             &lt;Badge&gt;&lt;span&gt;AI draft&lt;/span&gt;&lt;/Badge&gt;
///             &lt;ChildContent&gt;@context.Message.Text&lt;/ChildContent&gt;
///             &lt;Actions&gt;&lt;button&gt;Approve&lt;/button&gt;&lt;/Actions&gt;
///         &lt;/TmChatBubble&gt;
///     &lt;/MessageTemplate&gt;
/// &lt;/TmChat&gt;
/// </code>
/// </example>
public partial class TmChatBubble : TmComponentBase
{
    private const string MetaSeparator = " · ";

    /// <summary>
    /// Visual variant of the bubble. Defaults to <see cref="ChatBubbleVariant.Incoming"/>, the
    /// look of a received message.
    /// </summary>
    [Parameter] public ChatBubbleVariant Variant { get; set; } = ChatBubbleVariant.Incoming;

    /// <summary>
    /// Author name shown at the start of the meta row. Rendered before the time, separated from
    /// it by a middle dot (" · "). Null (the default) renders no actor label.
    /// </summary>
    [Parameter] public string? ActorLabel { get; set; }

    /// <summary>
    /// Message time. When set, the meta row renders a <c>&lt;time&gt;</c> element whose
    /// <c>datetime</c> attribute carries the machine-readable UTC instant and whose text is the
    /// local <c>HH:mm</c> label — the same format the built-in TmChat rows use. Null renders no
    /// time element.
    /// </summary>
    [Parameter] public DateTimeOffset? Timestamp { get; set; }

    /// <summary>
    /// Badge slot rendered above the body — a reason badge ("AI draft"), a price ("19 Kč"), a
    /// status chip. Rendered raw: the host owns the chip markup and its styling. Rendered for
    /// every variant, including <see cref="ChatBubbleVariant.Locked"/>.
    /// </summary>
    [Parameter] public RenderFragment? Badge { get; set; }

    /// <summary>
    /// Extra meta content rendered INSIDE the meta row, after the actor label and time — an
    /// "edited" tag, a countdown, a read receipt. The meta row renders whenever any of
    /// <see cref="ActorLabel"/>, <see cref="Timestamp"/> or <see cref="Meta"/> is set.
    /// </summary>
    [Parameter] public RenderFragment? Meta { get; set; }

    /// <summary>
    /// Note rendered BELOW the body — the reason a message was discarded, a scheduling hint, a
    /// delivery state. Rendered raw; the host owns the copy. Rendered for every variant,
    /// including <see cref="ChatBubbleVariant.Locked"/>.
    /// </summary>
    [Parameter] public RenderFragment? Note { get; set; }

    /// <summary>
    /// Action row rendered at the bottom of the bubble — Approve/Edit/Discard buttons, an unlock
    /// button, a Stop button. The host owns the buttons (and their events); this component only
    /// lays the row out. Rendered for every variant, including
    /// <see cref="ChatBubbleVariant.Locked"/>.
    /// </summary>
    [Parameter] public RenderFragment? Actions { get; set; }

    /// <summary>
    /// The bubble body. NOT rendered for <see cref="ChatBubbleVariant.Locked"/> — locked content
    /// must not leak into the DOM; for <see cref="ChatBubbleVariant.Discarded"/> the body element
    /// is a semantic <c>&lt;del&gt;</c>. Everything else renders it verbatim.
    /// </summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>
    /// Machine-readable status of the bubble, announced to assistive technology through a
    /// visually-hidden span rendered FIRST inside the bubble — before the badge, the body and the
    /// meta row. The host MUST provide a VISIBLE <see cref="Badge"/> or a <see cref="StatusLabel"/>
    /// for the <see cref="ChatBubbleVariant.Pending"/>, <see cref="ChatBubbleVariant.Queued"/>,
    /// <see cref="ChatBubbleVariant.Discarded"/> and <see cref="ChatBubbleVariant.Locked"/>
    /// variants: their surfaces alone do not say "pending", "queued", "discarded" or "locked", and
    /// screen-reader users must not depend on the decorative variant icon. Null renders no status
    /// span.
    /// </summary>
    [Parameter] public string? StatusLabel { get; set; }

    /// <summary>
    /// Whether the reserved-semantics variants render their decorative status icon inside the badge
    /// area: a <c>lock</c> for <see cref="ChatBubbleVariant.Locked"/> and a <c>clock</c> for
    /// <see cref="ChatBubbleVariant.Queued"/> (TmIcon is decorative — <c>aria-hidden="true"</c>).
    /// The icon is a redundant visual cue, never the only carrier of the state — pair it with a
    /// visible <see cref="Badge"/> or a <see cref="StatusLabel"/>. Defaults to <c>true</c>; has no
    /// effect on the other six variants.
    /// </summary>
    [Parameter] public bool ShowVariantIcon { get; set; } = true;

    /// <summary>Additional CSS classes appended to the <c>tm-chat-bubble</c> root.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional attributes splatted onto the root element (id, data-*, aria-*).</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    /// <summary>Whether the meta row has anything to show.</summary>
    private bool ShowMeta
        => !string.IsNullOrEmpty(ActorLabel) || Timestamp is not null || Meta is not null;

    /// <summary>Whether the body renders for the current variant.</summary>
    private bool ShowBody => Variant != ChatBubbleVariant.Locked && ChildContent is not null;

    /// <summary>The literal modifier class of the current variant.</summary>
    private string VariantClass => Variant switch
    {
        ChatBubbleVariant.Incoming => "tm-chat-bubble--incoming",
        ChatBubbleVariant.Outgoing => "tm-chat-bubble--outgoing",
        ChatBubbleVariant.System => "tm-chat-bubble--system",
        ChatBubbleVariant.Pill => "tm-chat-bubble--pill",
        ChatBubbleVariant.Pending => "tm-chat-bubble--pending",
        ChatBubbleVariant.Queued => "tm-chat-bubble--queued",
        ChatBubbleVariant.Discarded => "tm-chat-bubble--discarded",
        ChatBubbleVariant.Locked => "tm-chat-bubble--locked",
        _ => "tm-chat-bubble--incoming",
    };

    /// <summary>The full root class: block + variant modifier + host classes.</summary>
    private string RootClass
    {
        get
        {
            var classes = $"tm-chat-bubble {VariantClass}";
            return string.IsNullOrEmpty(Class) ? classes : $"{classes} {Class}";
        }
    }

    /// <summary>Machine-readable instant for the time element's datetime attribute (UTC, ISO 8601).</summary>
    private string DatetimeAttribute
        => Timestamp!.Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    /// <summary>Human-readable time label — local HH:mm, matching the built-in TmChat rows.</summary>
    private string TimeText
        => Timestamp!.Value.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture);

    /// <summary>Machine-readable variant hook for hosts and tests (<c>data-variant="pending"</c>).</summary>
    private string VariantAttribute => Variant.ToString().ToLowerInvariant();

    /// <summary>
    /// The decorative status icon of the current variant: <see cref="IconNames.Lock"/> for
    /// <see cref="ChatBubbleVariant.Locked"/>, <see cref="IconNames.Clock"/> for
    /// <see cref="ChatBubbleVariant.Queued"/>, none for the other six.
    /// </summary>
    private string? VariantIconName => Variant switch
    {
        ChatBubbleVariant.Locked => IconNames.Lock,
        ChatBubbleVariant.Queued => IconNames.Clock,
        _ => null,
    };

    /// <summary>Whether the badge area renders — a host badge or the decorative variant icon.</summary>
    private bool ShowBadgeArea => Badge is not null || (ShowVariantIcon && VariantIconName is not null);
}
