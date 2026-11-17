namespace Tempo.Blazor.Components.Navigation;

/// <summary>
/// How a <see cref="TmTabs"/> strip behaves when the tabs do not fit the available width.
/// <para>
/// <see cref="Scroll"/> (default) keeps the pre-2.12 behavior: one row that scrolls
/// horizontally, now with edge shadows that fade the clipped tabs. <see cref="Wrap"/>
/// lays the tabs out over multiple rows instead — the same layout the legacy
/// <c>Wrap</c> boolean opted into, available now as a first-class value.
/// </para>
/// </summary>
public enum TabsOverflow
{
    /// <summary>The strip stays on one row and scrolls horizontally (the historical default), with edge shadows signalling the clipped tabs.</summary>
    Scroll,

    /// <summary>The strip wraps onto multiple rows to fit the available width (the layout of the legacy <c>Wrap</c> boolean).</summary>
    Wrap
}
