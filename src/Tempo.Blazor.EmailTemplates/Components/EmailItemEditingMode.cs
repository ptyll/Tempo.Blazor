namespace Tempo.Blazor.EmailTemplates.Components;

/// <summary>How the items of a block (social, navbar, carousel, accordion) are edited (§3.9).</summary>
public enum EmailItemEditingMode
{
    /// <summary>
    /// Every field of every item is rendered inline in the inspector. The default, and the behaviour of
    /// 2.9.x — an existing host sees no change.
    /// </summary>
    Inline = 0,

    /// <summary>
    /// The inspector shows a compact list (preview + edit / remove / move) and the item's fields open in a
    /// modal dialog over a <b>copy</b> of the item: Save applies the copy as one change (one undo step),
    /// Cancel, Escape and an overlay click leave the document untouched, and focus returns to the button
    /// that opened the dialog.
    /// </summary>
    Dialog = 1,
}
