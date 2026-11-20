namespace Tempo.Blazor.Components.Layout;

/// <summary>
/// How tall the BOTTOM sheet of a <see cref="TmSidePanel"/> is (the phone presentation). The side
/// sheet and the docked panel have no height of their own, so the choice does not apply to them.
/// </summary>
public enum SidePanelSheetHeight
{
    /// <summary>
    /// The sheet rests on a snap point: <see cref="TmSidePanel.SheetSnapPoints"/> (default half and
    /// the cap) at <see cref="TmSidePanel.SheetSnapIndex"/>, with a draggable, keyboard-operable
    /// handle between the snaps. The pre-10d behaviour.
    /// </summary>
    Snap,

    /// <summary>
    /// The sheet is as tall as its content, under the cap <see cref="TmSidePanel.SheetMaxHeight"/>
    /// (default 0.85 of the viewport): a short form shows whole without dragging, a longer body
    /// scrolls while the footer stays pinned to the bottom. The sheet has no snaps, so
    /// <see cref="TmSidePanel.SheetSnapPoints"/> and <see cref="TmSidePanel.SheetSnapIndex"/> are
    /// ignored and the grabber is a decorative swipe target (like <c>TmModal</c>'s sheet).
    /// </summary>
    Content,
}
