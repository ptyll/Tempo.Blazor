namespace Tempo.Blazor.Components.Layout;

/// <summary>
/// How the <see cref="TmEditorShell"/> presents its <c>Left</c>/<c>Right</c> panels on a mobile
/// layout: both stacked in one sheet, or switched through a tab strip inside the sheet.
/// </summary>
public enum MobilePanelPresentation
{
    /// <summary>One bottom sheet whose body stacks every open panel, in <c>Left</c>, <c>Right</c>
    /// order.</summary>
    Sheet,

    /// <summary>A tab strip (<c>role="tablist"</c>) inside the sheet switches between the open
    /// panels — one panel visible at a time.</summary>
    Tabs,
}
