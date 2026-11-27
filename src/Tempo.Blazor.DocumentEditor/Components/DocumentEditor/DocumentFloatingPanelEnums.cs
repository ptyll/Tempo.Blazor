namespace Tempo.Blazor.Components.DocumentEditor;

/// <summary>Width class of a <see cref="TmDocumentFloatingPanel"/>: the panel never exceeds its viewport, the class only caps it.</summary>
public enum DocumentFloatingPanelWidth
{
    /// <summary>The content decides, up to a compact cap (a menu of a few entries, the table grid).</summary>
    Compact,

    /// <summary>A form of one column (link, image, token list, field editor).</summary>
    Medium,

    /// <summary>A gallery or a two-column form (style gallery, equation palette, page layout).</summary>
    Wide,
}

/// <summary>Why a <see cref="TmDocumentFloatingPanel"/> asked its host to close it.</summary>
public enum DocumentFloatingCloseReason
{
    /// <summary>The close (X) button or the footer's close action.</summary>
    Button,

    /// <summary>Escape: focus returns to <see cref="TmDocumentFloatingPanel.RestoreFocusTarget"/>.</summary>
    Escape,

    /// <summary>A pointer press outside the panel and its trigger. The press is not swallowed: it still reaches its target.</summary>
    Outside,

    /// <summary>
    /// The Done action of the bottom sheet (a mobile layout, which has no footer buttons of its own to confirm with). A form panel
    /// treats it as "apply what is valid, then close"; a panel that applies immediately treats it like <see cref="Button"/>.
    /// </summary>
    Done,
}
