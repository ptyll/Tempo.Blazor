namespace Tempo.Blazor.Components.Navigation;

/// <summary>
/// The menu-button contract <see cref="TmContextMenu"/> hands to a host trigger in AsChild mode
/// (<c>TmContextMenu.AsChild = true</c>). The wrapper is presentational then; the host's own
/// trigger element (usually a native <c>&lt;button&gt;</c> inside the <c>Trigger</c> fragment)
/// MUST declare <c>aria-haspopup="menu"</c> — REQUIRED, or the reader never learns that the
/// button opens a menu — and binds <see cref="IsOpen"/> to <c>aria-expanded</c> and calls
/// <see cref="ToggleAsync"/> from its click handler. Activation keys are the native button
/// contract — the menu adds no emulation.
/// </summary>
public sealed class ContextMenuTriggerContext
{
    private readonly TmContextMenu _menu;

    internal ContextMenuTriggerContext(TmContextMenu menu) => _menu = menu;

    /// <summary>Whether the menu is currently open — bind to <c>aria-expanded</c>.</summary>
    public bool IsOpen => _menu.IsOpen;

    /// <summary>Opens the menu when it is closed and closes it when it is open.</summary>
    public Task ToggleAsync() => _menu.ToggleAsync();

    /// <summary>
    /// Opens the menu (no-op while it is already open). The open state transition runs on the
    /// menu's dispatcher, so a host calling this from outside it cannot race the state read.
    /// </summary>
    public Task OpenAsync() => _menu.OpenFromTriggerAsync();

    /// <summary>
    /// Closes the menu (no-op while it is already closed). Mirrors <see cref="OpenAsync"/> for a
    /// host that closes the menu from its own action, e.g. a trigger-level "Done" command.
    /// </summary>
    public Task CloseAsync() => _menu.CloseMenuAsync();
}
