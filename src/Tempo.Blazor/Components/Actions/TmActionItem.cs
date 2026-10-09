namespace Tempo.Blazor.Components.Actions;

using Microsoft.AspNetCore.Components;

/// <summary>
/// A single action offered by an action surface (the F5 <c>TmMobileActionBar</c>, the F4 overflow
/// toolbar, and any future action host). The model is deliberately free of rendering concerns:
/// a host maps an item to a button, a menu entry, or another surface.
/// </summary>
public sealed class TmActionItem
{
    /// <summary>
    /// Stable identity of the action (used as a render key). When empty, <see cref="Label"/>
    /// identifies the item. Must not start with <c>tm-</c> when it lands in a CSS class or id —
    /// that prefix is reserved for library-internal stems (scoped-CSS ownership sweep).
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The user-visible label. Names the rendered button (and its full-text <c>title</c>).</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Optional icon name (an <c>IconNames</c> value or a registered icon).</summary>
    public string? Icon { get; set; }

    /// <summary>
    /// Ordering hint: higher priority actions render earlier (farther left in LTR). Items with
    /// equal priority keep the order of <c>Items</c>. Overflow into the "More" menu keeps this
    /// order, so the least important actions land in the menu first.
    /// </summary>
    public int Priority { get; set; }

    /// <summary>Disables the action. Disabled items render as disabled buttons/menuitems.</summary>
    public bool Disabled { get; set; }

    /// <summary>
    /// The action itself. Invoked exactly once per user activation. Create the callback with
    /// <c>EventCallback.Factory.Create(this, …)</c> — the receiver is the owning component, so
    /// its markup re-renders automatically when the action mutates state (e.g. opens a sibling
    /// dialog); a default (empty) callback is a no-op.
    /// </summary>
    public EventCallback OnClick { get; set; }

    /// <summary>
    /// When <see langword="true"/>, the "More" menu stays open after the action runs — use for
    /// actions that open another surface (a confirm dialog, a picker): the surface promotes
    /// AFTER the sheet, so it paints above it (top-layer order = open order), and closing it
    /// returns focus to this item. The default (<see langword="false"/>) closes the sheet
    /// BEFORE the action runs: focus returns to the "More" trigger and the action runs with no
    /// menu bookkeeping behind it.
    /// </summary>
    public bool KeepMenuOpen { get; set; }
}
