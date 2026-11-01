namespace Tempo.Blazor.Abstractions.Models.Reviews;

/// <summary>
/// A host-defined button of a review queue row — the "decision inbox" variant, where an item is
/// resolved with two purpose-named buttons ("Approve &amp; file", "Send back to the author") instead
/// of the built-in verbs.
/// <para>
/// Activating one raises <c>OnAction</c> with
/// <see cref="ReviewQueueAction.QuickAction"/> and this action's <see cref="Id"/>. Like every action
/// it resolves the item only when the callback completes without throwing, and the localized result
/// announced to screen readers uses <see cref="Label"/> as the verb.
/// </para>
/// </summary>
public sealed class ReviewQueueQuickAction
{
    /// <summary>Stable identity of the action, carried in
    /// <see cref="ReviewQueueActionArgs{TItem}.QuickActionId"/>. Required and unique within the
    /// queue.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>The visible button label. Required — it is also the verb the live region
    /// announces.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Optional icon name (an <c>IconNames</c> value or a registered icon).</summary>
    public string? Icon { get; init; }

    /// <summary>The button's visual weight. Default <see cref="ReviewQueueQuickActionVariant.Secondary"/>.</summary>
    public ReviewQueueQuickActionVariant Variant { get; init; }

    /// <summary>
    /// Accessible name override for the row button. When omitted the button is named by a localized
    /// "&lt;label&gt;: &lt;item title&gt;" template (WCAG 2.4.6 — a row of three identically named
    /// "Approve" buttons is indistinguishable in a screen reader's button list).
    /// </summary>
    public string? AriaLabel { get; init; }
}
