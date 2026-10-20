namespace Tempo.Blazor.Components.Toolbar;

using Tempo.Blazor.Components.Actions;

/// <summary>
/// Internal. What <see cref="TmToolbar"/> needs from anything that can leave the bar into the "More" menu:
/// <see cref="TmToolbarButton"/> and <see cref="TmToolbarItem"/>. Registration is by the cascade at any
/// depth; the DOM order reported by tm-toolbar.js (<c>data-tm-toolbar-item</c> = <see cref="MenuId"/>) is
/// the order of record.
/// </summary>
internal interface IToolbarOverflowItem
{
    /// <summary>Stable id; rendered as <c>data-tm-toolbar-item</c> and used as the More entry id.</summary>
    string MenuId { get; }

    /// <summary>The EFFECTIVE priority the partition ranks (an item with no menu representation reports <see cref="ToolbarButtonPriority.Pinned"/>).</summary>
    ToolbarButtonPriority EffectivePriority { get; }

    /// <summary>Re-render after the toolbar's partition or labels changed.</summary>
    void Refresh();

    /// <summary>Appends this item's More-menu entries (one or more, in written order) to <paramref name="target"/>.</summary>
    void AppendMenuEntries(List<TmActionItem> target);
}
