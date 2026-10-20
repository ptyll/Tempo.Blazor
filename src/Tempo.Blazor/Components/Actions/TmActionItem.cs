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

    /// <summary>The user-visible label. Names the rendered button and stays fully in the DOM.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Optional icon name (an <c>IconNames</c> value or a registered icon).</summary>
    public string? Icon { get; set; }

    /// <summary>
    /// Overflow rank ONLY — it never changes the render order. The bar and the "More" menu render
    /// in the order of <c>Items</c>; the items beyond the visible budget move into the menu by
    /// ascending priority (the lowest priority overflows first, ties drop the last item first).
    /// Disabled items still count towards the budget.
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// Pins the item to the surface or to the overflow menu, independently of
    /// <see cref="Priority"/>: <see cref="ActionOverflow.Never"/> keeps it on the surface,
    /// <see cref="ActionOverflow.Always"/> puts it in the "More" menu even when the budget would
    /// show it, <see cref="ActionOverflow.Auto"/> (default) lets the budget and the priority rank
    /// decide. Never-pinned items count against the visible budget; always-pinned items do not.
    /// </summary>
    public ActionOverflow Overflow { get; set; }

    /// <summary>
    /// Marks a destructive action. The overflow menu tints the entry with the danger colour so a
    /// destructive item keeps its meaning when it leaves the surface.
    /// </summary>
    public bool Danger { get; set; }

    /// <summary>
    /// The action's on/off state when it is a toggle. <see langword="null"/> (default) is a plain
    /// action — a <c>menuitem</c> in the "More" menu. <see langword="true"/>/<see langword="false"/>
    /// make it a <c>menuitemcheckbox</c> with <c>aria-checked</c> and a check mark in the menu, and
    /// add <c>aria-pressed</c> to its tile on the mobile action bar. Activating it still runs
    /// <see cref="OnClick"/> (and closes the menu like every entry — the close-before-invoke
    /// contract); the host flips the state in the callback.
    /// </summary>
    public bool? Checked { get; set; }

    /// <summary>
    /// Renders a toggle whose <see cref="Checked"/> is <see langword="false"/> as partially on
    /// (<c>aria-checked</c> / <c>aria-pressed</c> = <c>mixed</c>). Ignored when
    /// <see cref="Checked"/> is <see langword="null"/> or <see langword="true"/>.
    /// </summary>
    public bool CheckedMixed { get; set; }

    /// <summary>Disables the action. Disabled items render as disabled buttons/menuitems.</summary>
    public bool Disabled { get; set; }

    /// <summary>
    /// Stable key of the group the action belongs to in the "More" menu. Consecutive items (in
    /// <c>Items</c> order — grouping never reorders) with the same non-empty key render as one
    /// <c>role="group"</c> section named by <see cref="GroupLabel"/>, and adjacent runs are divided by
    /// a <c>role="separator"</c>. Null or empty means ungrouped. No effect on the surface itself.
    /// </summary>
    public string? Group { get; set; }

    /// <summary>
    /// Accessible name and visible header of the item's group in the "More" menu, read from the FIRST
    /// item of a run. Empty renders the run without a group wrapper (separators only).
    /// </summary>
    public string? GroupLabel { get; set; }

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
